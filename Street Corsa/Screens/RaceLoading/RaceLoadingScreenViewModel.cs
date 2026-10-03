using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Text;
using StreetCorsa.Services;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Time;
using StreetCorsa.ViewModels;
using System.Windows.Input;

namespace StreetCorsa.Screens.RaceLoading
{
    /// <summary>
    /// Loading screen shown while AC is running.
    /// Waits for AC to exit, processes results, then navigates back.
    /// </summary>
    public class RaceLoadingScreenViewModel : BaseScreenViewModel
    {
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly IAssettoCorsaLauncher _launcher;
        private readonly DragRaceLaunchIntent _launchIntent;
        private readonly IGameTimeService _timeService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly IAppLogger _logger;

        // The "Stop Race" question while it is up: withdrawn when the race ends on its own, so it never stays over
        // the diner with the race's messages queued behind it
        private ConfirmationDialogViewModel? _stopDialog;

        private string _statusMessage = GameText.Say("raceload.launching");
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isLoading = true;

        // The race's time took the day over into the next morning
        private bool _raceRanIntoNextDay;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value)) CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>
        /// The way out of a race that hangs (AC stuck loading a broken car or track): stops AC, and the launcher
        /// puts the install back as it does after any race
        /// </summary>
        public ICommand StopRaceCommand { get; }

        public RaceLoadingScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            IAssettoCorsaLauncher launcher,
            DragRaceLaunchIntent launchIntent,
            IGameTimeService timeService,
            IGameStateRepository gameStateRepo)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _launcher = launcher;
            _launchIntent = launchIntent;
            _timeService = timeService;
            _gameStateRepo = gameStateRepo;
            _logger = AppLoggerFactory.CreateLogger("RaceLoading");
            StopRaceCommand = new RelayCommand(ConfirmStopRace, () => IsLoading);
        }

        private void ConfirmStopRace()
        {
            // Before AC has started, stopping calls the launch off and costs nothing
            // A test-and-tune writes its result after every pass: the passes run so far count, whenever AC stops
            var atStake = Context is { } context && (context.IsPinkSlip || context.CashWager > 0);
            var message = !_launcher.IsAssettoCorsaRunning
                ? GameText.Say("raceload.stop.not_started")
                : _launchIntent.TunePasses != null
                    ? GameText.Say("raceload.stop.tune")
                : atStake
                    ? GameText.Say("raceload.stop.at_stake")
                    : GameText.Say("raceload.stop.no_count");

            _stopDialog = new ConfirmationDialogViewModel(_dialogService, message, GameText.Say("raceload.stop.title"), confirmed =>
            {
                _stopDialog = null;
                if (!confirmed || !IsLoading) return;

                _logger.Warning("The player stopped the race");
                StatusMessage = GameText.Say("raceload.stopping");
                _launcher.CancelRace();
            });
            _dialogService.ShowDialog(_stopDialog);
        }

        /// <summary>The race as it was set up, carried by the intent; null for a launch without one</summary>
        private RaceContext? Context =>
            _launchIntent.Metadata.TryGetValue("RaceContext", out var value) ? value as RaceContext : null;

        public override async void Enter()
        {
            base.Enter();
            _logger.Information("Entered race loading screen");

            // Nothing may escape from here: this runs as a fire-and-forget call, and the player has to get back
            // to the game whatever happened to the race
            var messages = new List<PlayerMessage>();
            try
            {
                messages = await LaunchAndWaitForRace();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The race could not be seen through");
            }

            IsLoading = false;

            // The race is over: a Stop question still up has nothing left to stop
            if (_stopDialog is { } stopDialog)
            {
                _stopDialog = null;
                try
                {
                    _dialogService.Withdraw(stopDialog);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Could not withdraw the Stop Race question");
                }
            }

            ReturnFromRace(towed: messages.Any(m => m.TowedToGarage));

            // One at a time, over the screen the player came back to: the dialog service queues them. Each on its
            // own, so one that cannot be shown does not lose the rest
            foreach (var message in messages)
            {
                try
                {
                    Dialogs.PlayerMessageDialogs.Show(_dialogService, message, () => _navigationService.GoToMenu());
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not show a message from the race");
                }
            }
        }

        private async Task<List<PlayerMessage>> LaunchAndWaitForRace()
        {
            var messages = new List<PlayerMessage>();
            try
            {
                // The launcher puts the race on record in the save (pending) before AC starts, after settling an
                // earlier race still waiting for its result, and takes it off again if AC never starts
                StatusMessage = GameText.Say("raceload.racing");
                _logger.Information("Launching race with intent: Player={PlayerCar}, Opponent={OpponentCar}",
                    _launchIntent.PlayerCarId, _launchIntent.OpponentCarId);

                var result = await _launcher.LaunchRaceAsync(_launchIntent);
                messages.AddRange(result.PlayerMessages);
                _logger.Information("Race over: {Outcome} (success: {Success})", result.Outcome, result.Success);

                // Time passes for a race that was run, whatever came of it; not for one that never started
                var raced = result.Outcome is RaceOutcome.Processed or RaceOutcome.NoResult
                    or RaceOutcome.Quarantined or RaceOutcome.ResultPending;
                if (raced)
                {
                    StatusMessage = result.Outcome == RaceOutcome.ResultPending
                        ? GameText.Say("raceload.result_pending")
                        : GameText.Say("raceload.processing");

                    await SpendRaceTime();

                    // Small delay to show the processing message
                    await Task.Delay(500);
                }
                else if (result.Outcome == RaceOutcome.Cancelled)
                {
                    StatusMessage = GameText.Say("raceload.called_off");
                    await Task.Delay(1000);
                }
                else
                {
                    _logger.Warning("Race launch failed: {Error}", result.ErrorMessage);
                    StatusMessage = GameText.Say("raceload.failed", ("problem", result.ErrorMessage));
                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during race");
                StatusMessage = GameText.Say("raceload.error", ("problem", ex.Message));
                await Task.Delay(2000);
            }

            return messages;
        }

        /// <summary>The time the race took, and a save for it: late in the day that is the next morning</summary>
        private async Task SpendRaceTime()
        {
            try
            {
                var spent = await _timeService.SpendTimeAsync(_gameState, _launchIntent.SessionAction);
                _raceRanIntoNextDay = spent.NewDayStarted;

                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend and save the time for the race");
            }
        }

        /// <summary>
        /// Back where the race was set up (the diner, the street, the garage for a test-and-tune or the racing hub), or
        /// where that screen's <c>after_race</c> way out leads; home when that cannot be opened, and the main menu when
        /// home cannot either: the player is never left on this screen. A player whose car was towed home goes home,
        /// where the car is; so does one whose night on the street ended while they raced.
        /// </summary>
        private void ReturnFromRace(bool towed)
        {
            var flow = _navigationService.Flow;
            var back = flow.Exit(_launchIntent.ReturnTo, "after_race") ?? _launchIntent.ReturnTo;
            var args = new FlowArgs { SkipAnimation = true, Returning = true };

            if (towed)
            {
                _logger.Information("Towed home after a crash: navigating home");
            }
            else if (_raceRanIntoNextDay && flow.KindOf(back) == ScreenKinds.Street)
            {
                // The street's night ended while they raced: home, as the street itself sends the player at the day's end
                _logger.Information("The street race ran past the night's end: home");
                _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService,
                    GameText.Say("raceload.late_night"), GameText.Say("cruise.late_night.title")));
            }
            else if (ScreenKinds.IsOverlay(flow.KindOf(back)))
            {
                // The racing hub or the career would open over this screen and leave the player on it
                _logger.Warning("{Screen} opens over a screen, not in its place: home after the race", back);
            }
            else if (back != null && flow.IsOpen(back))
            {
                _logger.Information("Back to {Screen} after the race", back);
                if (_navigationService.Go(back, _gameState, args)) return;
                _logger.Warning("{Screen} could not be opened after the race: going home", back);
            }

            if (_navigationService.GoHome(_gameState, args)) return;

            _logger.Warning("Home could not be opened after the race either: going to the main menu");
            _navigationService.GoToMenu();
        }
    }
}
