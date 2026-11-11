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
using StreetCorsa.Services.Parts;
using StreetCorsa.Parts;
using StreetCorsa.Screens.Shared;
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
        private readonly ICarPartsService _partsService;
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
            IGameStateRepository gameStateRepo,
            ICarPartsService partsService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _launcher = launcher;
            _launchIntent = launchIntent;
            _timeService = timeService;
            _gameStateRepo = gameStateRepo;
            _partsService = partsService;
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

        public override async Task EnterAsync()
        {
            _logger.Information("Entered race loading screen");

            // Nothing may escape from here: the player has to get back to the game whatever happened to the race, and
            // the screen the race was set up on is no place to put them back as it was
            var messages = new List<PlayerMessage>();
            // The player's cars as they went into the race: one more after it was won, the one raced gone when it was lost
            var carsBefore = _gameState.Player.Cars.Select(c => c.InstanceId).ToHashSet();
            var raced = _launchIntent.PlayerCarInstanceId != Guid.Empty ? _launchIntent.PlayerCarInstanceId : _gameState.Player.SelectedCarInstanceId;
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

            try
            {
                var (car, won) = CarAfterRace(carsBefore, raced, towed: messages.Any(m => m.TowedToGarage), messages);
                ReturnFromRace(car, won);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not go back after the race: home, else the main menu");
                GoHomeOrMenu();
            }

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

                Services.Market.GameSaves.TrySave(_gameStateRepo, _gameState, _logger, "the race's time");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not spend the time for the race");
            }
        }

        /// <summary>
        /// What came of the player's car (<paramref name="raced"/>) in the race: won another (that car), lost it, towed
        /// home after a crash or the police, or kept but not fit to drive (towed home all the same: what keeps it off the
        /// road goes to the player in <paramref name="messages"/>)
        /// </summary>
        private (AfterRaceCar Car, Car? Won) CarAfterRace(HashSet<Guid> carsBefore, Guid? raced, bool towed, List<PlayerMessage> messages)
        {
            var cars = _gameState.Player.Cars;
            if (cars.FirstOrDefault(c => !carsBefore.Contains(c.InstanceId)) is { } won) return (AfterRaceCar.Won, won);
            if (raced is { } id && carsBefore.Contains(id) && cars.All(c => c.InstanceId != id)) return (AfterRaceCar.Lost, null);
            if (towed) return (AfterRaceCar.Towed, null);

            if (cars.FirstOrDefault(c => c.InstanceId == raced) is { IsImpounded: false } car && !DrivingHome.Drives(car, Catalog))
            {
                var problems = DrivingHome.WhyWontRun(car, Catalog);
                messages.Add(new PlayerMessage(GameText.Say("raceload.wont_run.title"), problems.Count == 0
                    ? GameText.Say("raceload.wont_run.dry")
                    : GameText.Say("raceload.wont_run", ("problems", string.Join("; ", problems)))));
                return (AfterRaceCar.WontRun, null);
            }

            return (AfterRaceCar.Kept, null);
        }

        private PartsCatalog? Catalog => _partsService.IsAvailable ? _partsService.Catalog : null;

        /// <summary>
        /// Back where the race was set up (the diner, the street, the garage for a test-and-tune or the racing hub), or
        /// where that screen's <c>after_race</c> way out leads; home when that cannot be opened, and the main menu when
        /// home cannot either: the player is never left on this screen. Home for the night when the street's ended while
        /// they raced; home in a car won, which drives in; home without a car that was lost, towed or will not run. A car
        /// that comes home under its own power drives into the garage.
        /// </summary>
        private void ReturnFromRace(AfterRaceCar car, Car? won)
        {
            var (way, back) = WayBack(_navigationService.Flow, _launchIntent.ReturnTo, car, _raceRanIntoNextDay);
            switch (way)
            {
                case AfterRaceWay.Won:
                    // Home in the car won: it is the one the garage shows, driving in
                    _logger.Information("Won {Car}: home in it", won?.DefinitionId);
                    if (won != null)
                    {
                        _gameState.Player.SelectedCarInstanceId = won.InstanceId;
                        Services.Market.GameSaves.TrySave(_gameStateRepo, _gameState, _logger, "the car won");
                    }
                    break;
                case AfterRaceWay.Lost:
                    _logger.Information("Lost the car in the race: home without it");
                    break;
                case AfterRaceWay.Towed:
                    _logger.Information("Towed home after a crash: navigating home");
                    break;
                case AfterRaceWay.WontRun:
                    _logger.Information("The car will not run after the race: towed home");
                    break;
                case AfterRaceWay.NightOver:
                    // The street's night ended while they raced: home, as the street itself sends the player at the day's end
                    _logger.Information("The street race ran past the night's end: home");
                    _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService,
                        GameText.Say("raceload.late_night"), GameText.Say("cruise.late_night.title")));
                    break;
                case AfterRaceWay.Overlay:
                    // The racing hub or the career would open over this screen and leave the player on it
                    _logger.Warning("{Screen} opens over a screen, not in its place: home after the race", back);
                    break;
                case AfterRaceWay.Back:
                    _logger.Information("Back to {Screen} after the race", back);
                    if (_navigationService.Go(back, _gameState, AfterRaceArgs(DrivesHome(way)))) return;
                    _logger.Warning("{Screen} could not be opened after the race: going home", back);
                    break;
            }

            GoHomeOrMenu(DrivesHome(way));
        }

        /// <summary>The car comes home under its own power after the race (the garage drives it in when it can)</summary>
        internal static bool DrivesHome(AfterRaceWay way) => way is not (AfterRaceWay.Lost or AfterRaceWay.Towed or AfterRaceWay.WontRun);

        /// <summary>
        /// Where the player goes after a race (<see cref="ReturnFromRace"/>): the screen the race came back to (the one it
        /// was set up on, or where its <c>after_race</c> way out leads), and whether they go there or home, and why. A car
        /// won, lost, towed or unfit to drive takes the player home whatever the screen.
        /// </summary>
        internal static (AfterRaceWay Way, string? Back) WayBack(FlowBook flow, string? returnTo, AfterRaceCar car, bool nightOver,
            Rules.SystemRules? systems = null)
        {
            var back = flow.Exit(returnTo, "after_race", systems) ?? returnTo;
            switch (car)
            {
                case AfterRaceCar.Won: return (AfterRaceWay.Won, back);
                case AfterRaceCar.Lost: return (AfterRaceWay.Lost, back);
                case AfterRaceCar.Towed: return (AfterRaceWay.Towed, back);
                case AfterRaceCar.WontRun: return (AfterRaceWay.WontRun, back);
            }

            if (nightOver && flow.KindOf(back) == ScreenKinds.Street) return (AfterRaceWay.NightOver, back);
            if (ScreenKinds.IsOverlay(flow.KindOf(back))) return (AfterRaceWay.Overlay, back);
            return (back != null && flow.IsOpen(back, systems) ? AfterRaceWay.Back : AfterRaceWay.Home, back);
        }

        /// <summary>
        /// How the screen after the race comes in: at once, back from the race, at the diner raced from; the garage with
        /// the selected car driving in when it came home under its own power (<paramref name="drives"/>) and can drive
        /// </summary>
        private FlowArgs AfterRaceArgs(bool drives) => new()
        {
            SkipAnimation = true,
            Returning = true,
            DinerId = _launchIntent.ReturnDiner,
            Arrival = drives ? DrivingHome.Arrival(_gameState, null, Catalog) : null
        };

        /// <summary>Home after the race; the main menu when home cannot be opened, or throws</summary>
        private void GoHomeOrMenu(bool drives = false)
        {
            try
            {
                if (_navigationService.GoHome(_gameState, AfterRaceArgs(drives))) return;
                _logger.Warning("Home could not be opened after the race either: going to the main menu");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not go home after the race: going to the main menu");
            }

            _navigationService.GoToMenu();
        }
    }

    /// <summary>The way the player goes after a race (<see cref="RaceLoadingScreenViewModel.WayBack"/>)</summary>
    public enum AfterRaceWay
    {
        /// <summary>Back to the screen the race came back to</summary>
        Back,

        /// <summary>Home: the screen the race came back to is not open</summary>
        Home,

        /// <summary>Home, where the car was towed after a crash</summary>
        Towed,

        /// <summary>Home: the street's night ended while they raced</summary>
        NightOver,

        /// <summary>Home: the screen the race came back to opens over another (the racing hub, the career)</summary>
        Overlay,

        /// <summary>Home in the car won off the rival, which drives in</summary>
        Won,

        /// <summary>Home without the car, lost to the rival</summary>
        Lost,

        /// <summary>Home, where the car was towed: it came out of the race unfit to drive</summary>
        WontRun
    }

    /// <summary>What came of the player's car in a race, for the way home (<see cref="RaceLoadingScreenViewModel.WayBack"/>)</summary>
    public enum AfterRaceCar
    {
        /// <summary>Theirs still, and fit to drive</summary>
        Kept,

        /// <summary>They won the rival's car too</summary>
        Won,

        /// <summary>Lost to the rival</summary>
        Lost,

        /// <summary>Towed home after a crash, or the police took it</summary>
        Towed,

        /// <summary>Theirs still, but not fit to drive: towed home</summary>
        WontRun
    }
}
