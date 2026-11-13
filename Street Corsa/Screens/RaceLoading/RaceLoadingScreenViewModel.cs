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
using StreetCorsa.Services.CarLot;
using StreetCorsa.Services.Station;
using StreetCorsa.Helpers;
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
            var ran = false;
            // The car raced, and the one in the garage as it set off: an event may be raced in a car from the lot, and only
            // the garage's own comes back to drive in
            var raced = _launchIntent.PlayerCarInstanceId != Guid.Empty ? _launchIntent.PlayerCarInstanceId : _gameState.Player.SelectedCarInstanceId;
            var inGarage = _gameState.Player.SelectedCarInstanceId;
            try
            {
                (messages, ran) = await LaunchAndWaitForRace();
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
                // A race that never ran (called off, AC would not start) left the car where it was: nothing came of it,
                // and nothing drives in
                var (car, won) = ran
                    ? CarAfterRace(Context, _gameState.Player.Cars, raced, towed: messages.Any(m => m.TowedToGarage), Drives)
                    : (AfterRaceCar.Kept, null);
                // An earlier race settled before this one has its own crash's tow to pay, whether or not this one ran
                PayTows(ran ? raced : null, won, messages);
                ReturnFromRace(car, won, drives: ran && (won != null || raced == inGarage));
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

        /// <summary>The race run through: what the player is told after it, and whether it ran at all (true whatever came of it)</summary>
        private async Task<(List<PlayerMessage> Messages, bool Ran)> LaunchAndWaitForRace()
        {
            var messages = new List<PlayerMessage>();
            var ran = false;
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
                ran = result.Outcome is RaceOutcome.Processed or RaceOutcome.NoResult
                    or RaceOutcome.Quarantined or RaceOutcome.ResultPending;
                if (ran)
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

            return (messages, ran);
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
        /// What came of the player's car (<paramref name="raced"/>) in a race that ran, from the player's cars after it
        /// (<paramref name="cars"/>): won the rival's (that car), lost it, towed home after a crash or the police
        /// (<paramref name="towed"/>), or kept but not fit to drive (<paramref name="drives"/> false). The car won is the
        /// one the race put at stake (<paramref name="context"/>), never just a car that is new: an earlier race left
        /// pending is settled as this one sets off, and moves cars of its own.
        /// </summary>
        internal static (AfterRaceCar Car, Car? Won) CarAfterRace(RaceContext? context, IReadOnlyList<Car> cars, Guid? raced, bool towed,
            Func<Car, bool> drives)
        {
            if (context is { IsPinkSlip: true } stakes && stakes.OpponentCarInstanceId != Guid.Empty
                && cars.FirstOrDefault(c => c.InstanceId == stakes.OpponentCarInstanceId) is { } won)
                return (AfterRaceCar.Won, won);

            var mine = raced is { } id ? cars.FirstOrDefault(c => c.InstanceId == id) : null;
            if (raced != null && mine == null) return (AfterRaceCar.Lost, null);
            if (towed) return (AfterRaceCar.Towed, null);
            if (mine is { IsImpounded: false } && !drives(mine)) return (AfterRaceCar.WontRun, null);
            return (AfterRaceCar.Kept, null);
        }

        private PartsCatalog? Catalog => _partsService.IsAvailable ? _partsService.Catalog : null;

        private bool Drives(Car car) => DrivingHome.Drives(car, Catalog);

        /// <summary>
        /// A tow is always paid (all the player has when it is more): for the car raced when it crashed or will not run
        /// home, and for a car won that will not drive. Each is told: a crash's own message says what the tow cost, the
        /// others get one saying what keeps the car off the road. Never the police's: a car they took is in the impound.
        /// </summary>
        private void PayTows(Guid? raced, Car? won, List<PlayerMessage> messages)
        {
            var tows = TowsAfterRace(_gameState.Player.Cars, raced, won, messages, Drives);
            if (tows.Crash < 0 && tows.WontRun == null && tows.Won == null && tows.Earlier.Count == 0) return;

            var price = Parts.Cars.GasStation.TowPrice(StationScenes.RaceTow(StationScenes.Installed), _gameState.Rules);
            if (tows.Crash >= 0 && tows.Raced is { } crashed)
                messages[tows.Crash] = messages[tows.Crash] with { Text = Joined(messages[tows.Crash].Text, PayTow(price, crashed)) };
            foreach (var (index, earlierCar) in tows.Earlier)
                messages[index] = messages[index] with { Text = Joined(messages[index].Text, PayTow(price, earlierCar)) };
            if (tows.WontRun is { } wontRun) messages.Add(TowedMessage(wontRun, "raceload.wont_run", price));
            if (tows.Won is { } wonTowed) messages.Add(TowedMessage(wonTowed, "raceload.won_towed", price));

            Services.Market.GameSaves.TrySave(_gameStateRepo, _gameState, _logger, "the tow home");
        }

        /// <summary>
        /// The tows a race that ran leaves to pay (<see cref="PayTows"/>): the car raced (<paramref name="raced"/>, not
        /// impounded) on the crash message among <paramref name="messages"/> (<see cref="RaceTows.Crash"/>, -1 for none), else as
        /// a car that will not run home (<see cref="RaceTows.WontRun"/>); and the car won when it will not drive. The messages are
        /// this race's: an earlier race's go with them marked as not towed (<see cref="AssettoCorsaLauncher.AsEarlier"/>), its
        /// crash's tow paid for its own car (<see cref="RaceTows.Earlier"/>: each car once, the player's and not impounded).
        /// </summary>
        internal static RaceTows TowsAfterRace(IReadOnlyList<Car> cars, Guid? raced, Car? won, List<PlayerMessage> messages,
            Func<Car, bool> drives)
        {
            var mine = cars.FirstOrDefault(c => c.InstanceId == raced) is { IsImpounded: false } car ? car : null;
            var crash = mine == null ? -1 : messages.FindIndex(m => m.TowedToGarage);
            var wontRun = mine != null && crash < 0 && !drives(mine) ? mine : null;
            var earlier = new List<(int, Car)>();
            for (var i = 0; i < messages.Count; i++)
            {
                if (messages[i].EarlierTowOf is not { } towed || earlier.Exists(e => e.Item2.InstanceId == towed)) continue;
                if (cars.FirstOrDefault(c => c.InstanceId == towed) is { IsImpounded: false } earlierCar) earlier.Add((i, earlierCar));
            }

            return new RaceTows(crash, mine, wontRun, won is { IsImpounded: false } && !drives(won) ? won : null, earlier);
        }

        /// <param name="Crash">The crash message that carries the raced car's tow; -1 for none</param>
        /// <param name="Raced">The car raced, when it is the player's and not impounded</param>
        /// <param name="WontRun">The car raced, towed home as one that will not run (no crash)</param>
        /// <param name="Won">The car won, towed home as one that will not drive</param>
        /// <param name="Earlier">An earlier race's crash messages settled before this one, each with the car it towed</param>
        internal sealed record RaceTows(int Crash, Car? Raced, Car? WontRun, Car? Won, IReadOnlyList<(int Message, Car Car)> Earlier);

        /// <summary>The message for a car the tow truck brings home: what keeps it off the road (<paramref name="key"/>), and what the tow cost</summary>
        private PlayerMessage TowedMessage(Car car, string key, decimal price)
        {
            var problems = DrivingHome.WhyNotHome(car, Catalog);
            var text = problems.Count == 0 ? GameText.Say(key + ".dry") : GameText.Say(key, ("problems", string.Join("; ", problems)));
            return new PlayerMessage(GameText.Say("raceload.wont_run.title"), Joined(text, PayTow(price, car)));
        }

        /// <summary>The tow paid for <paramref name="car"/>, in words; empty when it costs nothing</summary>
        private string PayTow(decimal price, Car car)
        {
            if (price <= 0) return string.Empty;
            var paid = Parts.Cars.GasStation.PayTow(_gameState.Player, price);
            _logger.Information("{Car} towed home for ${Paid} (the tow is ${Price})", car.DefinitionId, paid, price);
            return paid < price
                ? GameText.Say("raceload.tow_short", ("tow", new Money(price)), ("paid", new Money(paid)))
                : GameText.Say("raceload.tow_paid", ("tow", new Money(paid)));
        }

        private static string Joined(string text, string more) => more.Length == 0 ? text : text + "\n\n" + more;

        /// <summary>
        /// Back where the race was set up (the diner, the street, the garage for a test-and-tune or the racing hub), or
        /// where that screen's <c>after_race</c> way out leads; home when that cannot be opened, and the main menu when
        /// home cannot either: the player is never left on this screen. Home for the night when the street's ended while
        /// they raced; home in a car won, which drives in; home without a car that was lost, towed or will not run. A car
        /// that comes home under its own power drives into the garage.
        /// </summary>
        /// <param name="drives">The race ran, in the car from the garage (or a car was won): a car that comes home may drive in</param>
        private void ReturnFromRace(AfterRaceCar car, Car? won, bool drives)
        {
            var (way, back) = WayBack(_navigationService.Flow, _launchIntent.ReturnTo, car, _raceRanIntoNextDay);
            drives &= DrivesHome(way);
            switch (way)
            {
                case AfterRaceWay.Won:
                    // Home in the car won, towed in when it will not drive: it goes in the garage, and the car that stood
                    // there to the lot
                    _logger.Information("Won {Car}: home in it", won?.DefinitionId);
                    if (won != null)
                    {
                        CarStorage.ToGarage(_gameState.Player, won, n => CarStorage.CapacityOf(WarehouseScenes.Installed, n));
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
                    if (_navigationService.Go(back, _gameState, AfterRaceArgs(drives))) return;
                    _logger.Warning("{Screen} could not be opened after the race: going home", back);
                    break;
            }

            GoHomeOrMenu(drives);
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
