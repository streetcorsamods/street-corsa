using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using StreetCorsa.Audio;
using StreetCorsa.Configuration;
using StreetCorsa.Controls.Street;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.Station
{
    /// <summary>
    /// The gas station, as Street Rod had it: the player drives there from the garage, seen from the driver's seat, and
    /// pulls up at the pump. The attendant fills the tank, tops up the oil and washes the car, for money and a quarter of
    /// an hour each, and while he is at it says what he noticed about the car (<see cref="AttendantCheck"/>). A car with
    /// too little gas to make it there is brought in by the tow truck, at a price. Then the player drives off, home.
    /// </summary>
    public class GasStationScreenViewModel : BaseScreenViewModel
    {
        /// <summary>The sound of the place</summary>
        public override string? Ambience => "ambience.station";

        /// <summary>At the wheel: the radio is the car's</summary>
        public override bool InCar => true;

        // A station that is loading is waited on this long at most before the car is simply there
        private static readonly TimeSpan StationLoadWait = TimeSpan.FromSeconds(25);

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly IGameTimeService _timeService;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly ICarPartsService _partsService;
        private readonly IAppLogger _logger;
        private readonly Random _random = new();
        private readonly DispatcherTimer _waitForStation;

        private Car? _car;
        private PartsCatalog? _catalog;
        private DateTime _waitingSince;
        private bool _active;
        private bool _busy;
        private bool _leaving;

        public GasStationScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            IContentCatalogRepository catalogRepository,
            IGameTimeService timeService,
            IGameStateRepository gameStateRepo,
            ICarPartsService partsService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepository = catalogRepository;
            _timeService = timeService;
            _gameStateRepo = gameStateRepo;
            _partsService = partsService;
            _logger = AppLoggerFactory.CreateLogger("Station");

            DriveOffCommand = new RelayCommand(DriveOff, () => IsAtPump && !_busy && !_leaving);
            _waitForStation = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _waitForStation.Tick += (_, _) => PullInWhenShown();

            Stage.AtPump += OnAtPump;
            Stage.Gone += OnGone;
        }

        public RelayCommand DriveOffCommand { get; }

        /// <summary>The view's curtain lifting off the scene, as the car rolls in (set by the view)</summary>
        public Action? OpenCurtain { get; set; }

        /// <summary>The view's curtain coming down, before the garage (set by the view)</summary>
        public Func<Task>? CloseCurtain { get; set; }

        #region The station

        /// <summary>What the car does, which the station's viewport shows</summary>
        public StationStage Stage { get; } = new();

        public StationScene? Station { get; private set; }

        public StreetLight Light { get; private set; }

        public StreetCar? PlayerCar { get; private set; }

        /// <summary>The player's own engine, driven by the viewport while the car moves</summary>
        public EngineRunner PlayerEngine { get; } = new(EngineChannel.Main);

        public string Title => Station?.Title ?? GameText.Say("station.ui.default_title");

        private string _statusText = string.Empty;

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        private bool _isAtPump;

        /// <summary>Stopped at the pump: the attendant is there and the jobs are up</summary>
        public bool IsAtPump
        {
            get => _isAtPump;
            private set
            {
                if (SetProperty(ref _isAtPump, value)) RelayCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>What the attendant has said, first to last, in quotes</summary>
        public ObservableCollection<string> AttendantLines { get; } = [];

        public ObservableCollection<StationJobRow> Jobs { get; } = [];

        public bool HasJobs => Jobs.Count > 0;

        private bool _isWorking;

        /// <summary>A job under way: the pump running, the oil going in, the wash; nothing else can be done meanwhile</summary>
        public bool IsWorking
        {
            get => _isWorking;
            private set => SetProperty(ref _isWorking, value);
        }

        private string _workTitle = string.Empty;

        public string WorkTitle
        {
            get => _workTitle;
            private set => SetProperty(ref _workTitle, value);
        }

        private string _workDetail = string.Empty;

        /// <summary>The pump's dials: the litres and the dollars so far</summary>
        public string WorkDetail
        {
            get => _workDetail;
            private set => SetProperty(ref _workDetail, value);
        }

        private double _workProgress;

        /// <summary>0 to 1</summary>
        public double WorkProgress
        {
            get => _workProgress;
            private set => SetProperty(ref _workProgress, value);
        }

        /// <summary>A dry tank that was not filled: the tow truck takes the car home</summary>
        public string DriveOffText => _car != null && GasStation.NeedsTow(_car) ? GameText.Say("station.ui.tow_home") : GameText.Say("station.ui.drive_off");

        #endregion

        #region Coming and going

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Went to the gas station");
            _active = true;

            _car = _gameState.Player.Cars.FirstOrDefault(c => c.InstanceId == _gameState.Player.SelectedCarInstanceId);
            if (_car == null || _catalogRepository.GetCar(_car.DefinitionId) == null)
            {
                SendHome(GameText.Say("station.ui.no_car"), GameText.Say("station.ui.no_car.title"));
                return;
            }

            // The driveway bell, as the car rolls over the hose
            Audio.Sfx.Play(Audio.Sfx.StationBell, 0.6f);

            if (_car.IsImpounded)
            {
                SendHome(GameText.Say("station.ui.impounded"), GameText.Say("station.ui.impounded.title"));
                return;
            }

            _catalog = _partsService.IsAvailable ? _partsService.Catalog : null;
            var groupOf = _catalog == null ? null : CarCondition.Groups(_catalog);

            // Only an empty tank brings the tow truck: a car that won't run for anything else is the garage's to fix
            var problems = CarCondition.WhyCannotRace(_car, groupOf, counting: false);
            if (_catalog != null && _car.HasPartsAssigned && _car.Engine == null) problems.Add(GameText.Say("station.ui.no_engine"));
            if (problems.Count > 0)
            {
                SendHome(GameText.Say("station.ui.wont_run", ("problems", string.Join("; ", problems))), GameText.Say("station.ui.wont_run.title"));
                return;
            }

            var stations = StationScenes.Installed;
            Station = stations.Count == 0 ? null : stations[_random.Next(stations.Count)];
            if (Station == null)
            {
                SendHome(GameText.Say("station.ui.none"), GameText.Say("station.ui.none.title"));
                return;
            }

            var towed = GasStation.NeedsTow(_car);
            var tow = TowPrice();
            if (towed && _gameState.Player.Money < tow)
            {
                SendHome(GameText.Say("station.ui.cant_tow", ("tow", $"${tow:N0}")), GameText.Say("station.ui.cant_tow.title"));
                return;
            }

            // The scene first: a station that fails to set up sends the player home before the trip is paid for
            Light = StreetLights.At(_gameState.Date);
            PlayerCar = new StreetCar(Path.Combine(AppSettings.Instance.CarsPath, _car.DefinitionId), string.IsNullOrEmpty(_car.SkinId) ? null : _car.SkinId);
            OnPropertyChanged(nameof(Station));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Light));
            OnPropertyChanged(nameof(PlayerCar));
            PlayerEngine.Place(null, StreetStage.OwnEngineVolume);

            // Then the trip: the tow truck's charge, or the gas the drive takes
            if (towed)
            {
                _gameState.Player.Money -= tow;
                _logger.Information("{Car} towed to {Station} for ${Tow}", _car.DefinitionId, Station.Id, tow);
                StatusText = GameText.Say("station.ui.towed_in", ("station", Station.Title), ("tow", $"${tow:N0}"));
            }
            else
            {
                GasStation.Drive(_car);
                StatusText = GameText.Say("station.ui.drive_over", ("station", Station.Title));
            }

            // The trip's time goes by while the car rolls in; saving and going home wait for it (the day may roll over)
            _trip = SpendAsync(towed ? GameAction.TowToStation : GameAction.DriveToStation);

            // The jobs are priced on what the trip left in the tank
            Refresh();
            _ = StartOwnEngineAsync(_car, running: !towed);

            Stage.FuelLitres = FuelInTank();
            _waitingSince = DateTime.UtcNow;
            _comesOnTheTow = towed;
            _waitForStation.Start();
        }

        // How the car comes, until it does (then the stage has it: Stage.Towed)
        private bool _comesOnTheTow;

        private Task _trip = Task.CompletedTask;

        // The tow home is paid once, however often the garage would not open
        private bool _towHomePaid;

        /// <summary>What the tow truck charges, either way</summary>
        private decimal TowPrice() => Station == null ? 0
            : PartPricing.Round((double)Station.Prices.Tow * GameRules.Sane(_gameState.Rules.PartPriceMultiplier));

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Left the gas station");
            _active = false;
            _waitForStation.Stop();
            Stage.Clear();
            PlayerEngine.Detach();
        }

        /// <summary>The car rolls in once the station is up on screen, or at once when it cannot be shown</summary>
        private void PullInWhenShown()
        {
            if (!_active) return;
            var waited = DateTime.UtcNow - _waitingSince;
            if (!Stage.IsShown && !Stage.CannotShow && Station?.HasModel == true && waited < StationLoadWait) return;

            _waitForStation.Stop();
            Stage.Arrive(PlayerEngine, _comesOnTheTow);
            OpenCurtain?.Invoke();
        }

        private async Task StartOwnEngineAsync(Car car, bool running)
        {
            try
            {
                var spec = await EngineSpecs.ForAsync(_partsService, car, CarNames.Of(_catalogRepository, car.DefinitionId));
                if (!_active) return;
                await PlayerEngine.SetEngineAsync(spec);

                // Only on the way in: an engine worked out after the car stopped at the pump stays off (nobody fills a
                // running car), and driving off starts it itself
                if (_active && running && Stage.Phase is StationPhase.Waiting or StationPhase.Arriving) PlayerEngine.StartRunning();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "The player's engine could not be started at the gas station");
            }
        }

        private void SendHome(string message, string title)
        {
            // It says why and takes the player back to the garage, once the navigation that brought them here is over
            _leaving = true;
            _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));
            _navigationService.NavigateToGarageLater(_gameState);
        }

        #endregion

        #region At the pump

        private void OnAtPump()
        {
            if (!_active || _car == null) return;

            IsAtPump = true;
            StatusText = GameText.Say("station.ui.at_pump", ("station", Title));

            // Nobody fills a running car: the engine goes off at the pump, and on again to drive off
            if (PlayerEngine.IsRunning) PlayerEngine.Toggle();
            Say(Stage.Towed ? "station.greeting.towed" : "station.greeting");

            // He checks the oil and has a look while he's there
            var left = CarCondition.OilLeft(_car);
            var quarts = GasStation.Quarts(GasStation.OilToTopUp(_car, _catalog));
            if (_car.Engine != null) Say(left < 0.4 ? "station.oil.very_low" : left < 0.95 ? "station.oil.low" : "station.oil.ok", new TextValues { ["quarts"] = quarts });

            var hints = AttendantCheck.Look(_car, _catalog);
            foreach (var hint in hints) Say(hint.TextKey, new TextValues { ["where"] = hint.Where });
            if (hints.Count == 0 && _car.Engine != null) Say("station.all_good");
            RaiseAll();
            _logger.Information("At the pump with {Car}: oil {Oil:0.00}, {Hints} thing(s) noticed: {Keys}", _car.DefinitionId, left, hints.Count,
                string.Join(", ", hints.Select(h => h.TextKey)));
        }

        private void Say(string key, TextValues? values = null)
        {
            values ??= new TextValues();
            if (_car != null && _catalogRepository.GetCar(_car.DefinitionId) is { } definition) values["car"] = CarNames.Short(definition);
            var line = GameText.Installed.Pick(key, _random, values);
            if (!string.IsNullOrWhiteSpace(line)) AttendantLines.Add($"“{line}”");
        }

        private void Refresh()
        {
            Jobs.Clear();
            if (_car != null && Station != null)
            {
                foreach (var job in GasStation.Jobs(_car, _catalog, Station.Prices, _gameState.Rules.PartPriceMultiplier))
                    Jobs.Add(new StationJobRow(job, new AsyncRelayCommand(() => DoAsync(job), () => IsAtPump && !_busy && !_leaving && _gameState.Player.Money >= job.Cost, name: "StationJob")));
            }

            OnPropertyChanged(nameof(HasJobs));
            OnPropertyChanged(nameof(DriveOffText));
        }

        private async Task DoAsync(RepairJob job)
        {
            if (_busy || _car == null || _gameState.Player.Money < job.Cost) return;

            _busy = true;
            RaiseAll();
            try
            {
                await WatchAsync(job);
                if (!_active) return;

                // The work first: a job that fails to go on costs nothing
                job.Apply();
                _gameState.Player.Money -= job.Cost;
                Stage.FuelLitres = FuelInTank();
                _logger.Information("{Car} at the gas station: {Job} for ${Cost}", _car.DefinitionId, job.Id, job.Cost);
                Say(job.Id switch
                {
                    GasStation.FillUp => "station.filled",
                    GasStation.Wash => "station.washed",
                    _ => "station.oil.topped"
                });

                await _trip;
                await SpendAsync(job.Time);
                Save();
            }
            finally
            {
                _busy = false;
                Refresh();
                RaiseAll();
            }
        }

        /// <summary>The job done before the player's eyes: the pump's dials counting up, or the bar for the oil and the wash</summary>
        private async Task WatchAsync(RepairJob job)
        {
            var litres = CarCondition.LitresToFill(_car!);
            var from = CarCondition.FuelLeft(_car!) ?? 0;
            var seconds = GasStation.WatchSeconds(job.Id, litres);
            WorkTitle = job.Id switch
            {
                GasStation.FillUp => GameText.Say("station.ui.filling"),
                GasStation.TopUpOil => GameText.Say("station.ui.oiling"),
                _ => GameText.Say("station.ui.washing")
            };

            IsWorking = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (_active)
                {
                    var done = Math.Min(1, clock.Elapsed.TotalSeconds / seconds);
                    WorkProgress = done;
                    if (job.Id == GasStation.FillUp) Stage.FuelLitres = (float)(from + litres * done);
                    WorkDetail = job.Id == GasStation.FillUp
                        ? GameText.Say("station.ui.fill_detail", ("litres", $"{litres * done:0.0}"), ("cost", $"${job.Cost * (decimal)done:0.00}"),
                            ("fuel", $"{from + litres * done:0}"), ("tank", $"{_car!.FuelTankLitres:0}"))
                        : $"{done * 100:0}%";
                    if (done >= 1) break;
                    await Task.Delay(40);
                }
            }
            finally
            {
                IsWorking = false;
            }
        }

        /// <summary>What the fuel gauge reads: the litres in the tank, a full tank's when it is full</summary>
        private float FuelInTank() => _car == null ? 0f
            : (float)(CarCondition.FuelLeft(_car) ?? CarCondition.TankLitres(_car) ?? 60);

        private void RaiseAll()
        {
            RelayCommand.RaiseCanExecuteChanged();
            foreach (var row in Jobs) row.Command.RaiseCanExecuteChanged();
        }

        private async Task SpendAsync(GameAction action)
        {
            try
            {
                await _timeService.SpendTimeAsync(_gameState, action);
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not spend the time at the gas station: {Error}", ex.Message);
            }
        }

        private void Save()
        {
            try
            {
                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save at the gas station");
            }
        }

        #endregion

        #region Driving off

        private async void DriveOff()
        {
            if (!IsAtPump || _leaving || _car == null) return;
            _leaving = true;
            RaiseAll();

            // Nothing may escape an async void; a throw (a line missing from a mod's words) leaves the player at the
            // pump with DRIVE OFF to try again, not stuck there with it disabled
            try
            {
                // Left dry: the tow truck takes it home, for what it charged to bring it (or all the player has left),
                // and an hour; there is nothing to watch
                if (GasStation.NeedsTow(_car))
                {
                    if (!_towHomePaid)
                    {
                        var tow = Math.Min(TowPrice(), Math.Max(0, _gameState.Player.Money));
                        _gameState.Player.Money -= tow;
                        _towHomePaid = true;
                        _logger.Information("{Car} towed home from {Station} for ${Tow}", _car.DefinitionId, Station?.Id, tow);
                        StatusText = GameText.Say("station.ui.towed_home", ("tow", $"${tow:N0}"));
                        await _trip;
                        await SpendAsync(GameAction.TowToStation);
                    }

                    if (_active) GoHome();
                    return;
                }

                Say("station.goodbye");
                StatusText = GameText.Say("station.ui.start_up");
                try
                {
                    await StartUpAsync();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "The engine could not be started to drive off");
                }

                if (!_active) return;
                StatusText = GameText.Say("station.ui.pull_out");
                Stage.Leave();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not drive off from the gas station");
                if (!_active) return;
                StatusText = GameText.Say("station.ui.still_at_pump");
                _leaving = false;
                RaiseAll();
            }
        }

        /// <summary>
        /// The starter, and a moment at idle before pulling away. An engine that will not catch (or has no sound to
        /// start) is waited on a few seconds at most: the car drives off all the same.
        /// </summary>
        private async Task StartUpAsync()
        {
            if (!PlayerEngine.IsRunning) PlayerEngine.Toggle();

            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (_active && PlayerEngine.State != Audio.EngineState.Running && clock.Elapsed < StartWait)
                await Task.Delay(50);

            await Task.Delay(IdleBeforePullingAway);
            if (_active && PlayerEngine.State != Audio.EngineState.Running) PlayerEngine.StartRunning();
        }

        private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan IdleBeforePullingAway = TimeSpan.FromSeconds(1.2);

        private void OnGone()
        {
            if (_active) GoHome();
        }

        /// <summary>The curtain comes down and the player is home; a garage that would not open leaves them at the pump</summary>
        private async void GoHome()
        {
            try
            {
                // The trip's time first: a day that rolled over is saved and shown as it now is
                await _trip;
                Save();
                if (CloseCurtain is { } close) await close();
                if (_navigationService.NavigateToGarage(_gameState)) return;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not go home from the gas station");
            }

            // Back at the pump, engine off, so DRIVE OFF can try again
            if (PlayerEngine.IsRunning) PlayerEngine.Toggle();
            Stage.BackToPump();
            StatusText = GameText.Say("station.ui.still_at_pump");
            OpenCurtain?.Invoke();
            _leaving = false;
            RaiseAll();
        }

        #endregion
    }

    public sealed class StationJobRow
    {
        public StationJobRow(RepairJob job, AsyncRelayCommand command)
        {
            Name = job.Name;
            Detail = job.Detail;
            CostDisplay = $"${job.Cost:N0}";
            TimeDisplay = job.Time.GetTimeDescription();
            Command = command;
        }

        public string Name { get; }
        public string Detail { get; }
        public string CostDisplay { get; }
        public string TimeDisplay { get; }
        public AsyncRelayCommand Command { get; }
    }
}
