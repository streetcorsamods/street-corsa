using StreetCorsa.Configuration;
using StreetCorsa.Dialogs;
using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Police;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;
using StreetCorsa.ViewModels;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using StreetCorsa.Helpers;

namespace StreetCorsa.Screens.Garage
{
    public class GarageScreenViewModel : BaseScreenViewModel
    {
        /// <summary>The sound of the place</summary>
        public override string? Ambience => "ambience.garage";

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly IAssettoCorsaLauncher _launcher;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly ICarPartsService _partsService;
        private readonly IGameTimeService _timeService;
        private readonly GameSettingsService _settingsService;
        private readonly ICarSaleService _saleService;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly RaceCarDataService _raceCarDataService;
        private readonly IAppLogger _logger;

        /// <summary>Asks where to go: the main menu, or out of the game</summary>
        public RelayCommand QuitCommand { get; }

        /// <summary>Everything about the selected car, and every part in it</summary>
        public RelayCommand CarInfoCommand { get; }
        public AsyncRelayCommand FreeRunCommand { get; }

        /// <summary>The selected car at the strip on its own, pass after pass, for timeslips (<see cref="OnTestAndTune"/>)</summary>
        public AsyncRelayCommand TestAndTuneCommand { get; }

        /// <summary>How many passes a test-and-tune runs</summary>
        public const int TestAndTunePasses = 6;
        public RelayCommand RepairsCommand { get; }
        public RelayCommand SellCommand { get; }

        /// <summary>Pays the impound and brings the selected car home, once its days there are up</summary>
        public RelayCommand CollectCommand { get; }

        /// <summary>Where a free run can go: every track of the install, one entry per layout</summary>
        public ObservableCollection<FreeRunTrackViewModel> FreeRunTracks { get; } = new();

        private FreeRunTrackViewModel? _freeRunTrack;

        public FreeRunTrackViewModel? FreeRunTrack
        {
            get => _freeRunTrack;
            set
            {
                if (!SetProperty(ref _freeRunTrack, value) || value == null) return;

                // Remembered for next time on a free run or when the garage is left, not on every step through the list
                FreeRunCommand.RaiseCanExecuteChanged();
            }
        }

        public bool HasFreeRunTracks => FreeRunTracks.Count > 0;
        public RelayCommand SelectCarCommand { get; }
        public RelayCommand ShowCalendarCommand { get; }

        /// <summary>Calls it a day: the rest of today goes by, and the game picks up tomorrow morning</summary>
        public RelayCommand EndDayCommand { get; }
        public RelayCommand NewspaperCommand { get; }
        public RelayCommand CarDealersCommand { get; }
        public RelayCommand PartDealersCommand { get; }
        /// <summary>The racing hub: the race invitations, Street News, and the way out onto the streets</summary>
        public RelayCommand GoRaceCommand { get; }
        public AsyncRelayCommand GasStationCommand { get; }
        public RelayCommand CareerCommand { get; }

        /// <summary>The parts view of the selected car: its engine, the part clicked on, the shelf of loose parts</summary>
        public PartsWorkbenchViewModel Workbench { get; }

        private ObservableCollection<CarDisplayViewModel> _cars;
        public ObservableCollection<CarDisplayViewModel> Cars
        {
            get => _cars;
            set
            {
                _cars = value;
                OnPropertyChanged(nameof(Cars));
            }
        }

        private CarDisplayViewModel? _selectedCar;
        public CarDisplayViewModel? SelectedCar
        {
            get => _selectedCar;
            set
            {
                var carChanged = !ReferenceEquals(_selectedCar?.CarInstance, value?.CarInstance);
                _selectedCar = value;
                OnPropertyChanged(nameof(SelectedCar));
                OnPropertyChanged(nameof(SelectedCarDisplay));
                OnPropertyChanged(nameof(HasCars));
                OnPropertyChanged(nameof(SellButtonText));
                RaiseImpoundChanged();
                if (carChanged)
                {
                    // A car in the impound isn't here to work on
                    Workbench.SetCar(value?.CarInstance is { IsImpounded: false } car ? car : null);
                    RefreshEngine();
                }
            }
        }

        private Audio.EngineSpec? _selectedEngine;
        private int _engineVersion;

        // The engine being worked out now, or last: what the car's sheet waits for
        private Task<Audio.EngineSpec?> _engineWork = Task.FromResult<Audio.EngineSpec?>(null);

        /// <summary>The selected car's engine, to start and rev where it stands; null without a car or an engine</summary>
        public Audio.EngineSpec? SelectedEngine
        {
            get => _selectedEngine;
            private set
            {
                _selectedEngine = value;
                OnPropertyChanged(nameof(SelectedEngine));
            }
        }

        /// <summary>Works the engine out again: the car changed, or what is in it</summary>
        private async void RefreshEngine()
        {
            // Nothing may escape an async void
            try
            {
                var version = ++_engineVersion;
                var car = SelectedCar;

                // The car just brought from the lot had its engine worked out there: the same parts, so not again
                var handed = _arrival is { } arrival && car?.CarInstance.InstanceId == arrival.InstanceId ? arrival.Engine : null;
                if (car != null) _arrival = null;
                var work = car == null ? Task.FromResult<Audio.EngineSpec?>(null)
                    : handed != null ? Task.FromResult<Audio.EngineSpec?>(handed)
                    : Services.Parts.EngineSpecs.ForAsync(_partsService, car.CarInstance, car.DisplayName);
                _engineWork = work;
                var spec = await work;
                if (version == _engineVersion) SelectedEngine = spec;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not work out the selected car's engine");
            }
        }

        public string SelectedCarDisplay
        {
            get
            {
                if (SelectedCar == null)
                    return GameText.Say("garage.no_car_selected");

                return $"{SelectedCar.CarDefinition.Brand} {SelectedCar.CarDefinition.Name} ({SelectedCar.CarDefinition.Year ?? 0})";
            }
        }

        public bool HasCars => Cars.Count > 0;

        /// <summary>Showroom model used as the environment of the 3D viewport</summary>
        public string GarageShowroomKn5 => AppSettings.Instance.GarageShowroomKn5;

        public bool SkipEnterAnimation { get; }

        /// <summary>The car was just taken out of the lot and drives in under its own power as the garage comes up</summary>
        public bool CarArriving { get; }

        // The car just taken out of the lot, until its engine is first worked out
        private CarArrival? _arrival;

        /// <summary>The tiles that lead out of the garage make room while the car is being worked on</summary>
        public bool ShowNavigation => !Workbench.IsOpen;

        // Panel System
        private GaragePanel _activePanel = GaragePanel.CarPreview;
        public GaragePanel ActivePanel
        {
            get => _activePanel;
            set
            {
                _activePanel = value;
                OnPropertyChanged(nameof(ActivePanel));
            }
        }

        // Calendar Properties
        public DateTime CurrentGameDate => _gameState.Date;
        public string CurrentMonthYear => GameText.Date(_gameState.Date, "format.month");
        public int CurrentDay => _gameState.Date.Day;
        public string CurrentDayOfWeek => GameText.Date(_gameState.Date, "format.weekday");
        public string CurrentTimeDisplay => HeaderTime(_gameState.Date);
        public BitmapImage? CalendarImage1 => GetRandomCalendarImage(0);
        public BitmapImage? CalendarImage2 => GetRandomCalendarImage(1);
        public BitmapImage? CalendarImage3 => GetRandomCalendarImage(2);

        /// <summary>The calendar pictures once decoded, by number, for the whole session: the same twelve come round every year</summary>
        private static readonly Dictionary<int, BitmapImage?> CalendarImages = new();

        /// <summary>
        /// Height the pictures are decoded at. They are 1536x1024 PNGs shown 120 px high; decoded at twice that
        /// they stay sharp on a high-DPI screen at a small fraction of the memory and the decode time.
        /// </summary>
        private const int CalendarDecodeHeight = 240;

        /// <summary>The month whose pictures the view was last told about, so they are raised only when it turns</summary>
        private int _shownCalendarMonth = -1;

        /// <summary>The day the calendar grid was last raised for</summary>
        private DateTime _shownCalendarDay = DateTime.MinValue;

        private BitmapImage? GetRandomCalendarImage(int index)
        {
            // Seed random with year and month so images are consistent for each month but different between months
            var seed = _gameState.Date.Year * 100 + _gameState.Date.Month + index * 17;
            var random = new Random(seed);
            var imageNumber = random.Next(1, 13); // 1-12
            return CalendarImage(imageNumber);
        }

        /// <summary>One calendar picture, decoded small and frozen once, then shared</summary>
        public static BitmapImage? CalendarImage(int imageNumber)
        {
            if (CalendarImages.TryGetValue(imageNumber, out var cached)) return cached;

            // A mod's picture (theme/images/misc/calendar/calendar01.png) stands in for the game's
            var image = Styles.ThemeImages.Load($"misc/calendar/calendar{imageNumber:D2}", decodeHeight: CalendarDecodeHeight);
            CalendarImages[imageNumber] = image;
            return image;
        }

        public List<CalendarDayViewModel> CalendarDays
        {
            get
            {
                var days = new List<CalendarDayViewModel>();
                var date = _gameState.Date;
                var firstDayOfMonth = new DateTime(date.Year, date.Month, 1);
                var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);

                // Add empty slots for days before the first day of the month
                var startDayOfWeek = (int)firstDayOfMonth.DayOfWeek;
                for (int i = 0; i < startDayOfWeek; i++)
                {
                    days.Add(new CalendarDayViewModel { Day = 0, IsCurrentDay = false, IsEmpty = true });
                }

                // Add days of the month
                for (int day = 1; day <= daysInMonth; day++)
                {
                    days.Add(new CalendarDayViewModel
                    {
                        Day = day,
                        IsCurrentDay = day == date.Day,
                        IsPast = day < date.Day,
                        IsEmpty = false
                    });
                }

                return days;
            }
        }

        public GarageScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            IContentCatalogRepository catalogRepo,
            IAssettoCorsaLauncher launcher,
            ICarPartsService partsService,
            IGameStateRepository gameStateRepo,
            IGameTimeService timeService,
            GameSettingsService settingsService,
            IAssettoCorsaContentService contentService,
            RaceCarDataService raceCarDataService,
            ICarSaleService saleService,
            bool skipAnimation = false,
            CarArrival? arrival = null)
        {
            _saleService = saleService;
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _catalogRepo = catalogRepo;
            _launcher = launcher;
            _gameStateRepo = gameStateRepo;
            _partsService = partsService;
            _timeService = timeService;
            _settingsService = settingsService;
            _contentService = contentService;
            _raceCarDataService = raceCarDataService;
            _logger = AppLoggerFactory.CreateLogger("Garage");
            SkipEnterAnimation = skipAnimation;
            _arrival = arrival;
            CarArriving = arrival?.DrivesIn == true;

            QuitCommand = new RelayCommand(OnQuit, CanLeave);
            CarInfoCommand = new RelayCommand(OnCarInfo, () => SelectedCar != null);
            FreeRunCommand = new AsyncRelayCommand(OnFreeRun, () => SelectedCarHere && FreeRunTrack != null && !_launcher.IsExecutionLocked && CanLeave());
            TestAndTuneCommand = new AsyncRelayCommand(OnTestAndTune, () => SelectedCarHere && !_launcher.IsExecutionLocked && CanLeave());
            LoadFreeRunTracks();
            // Not while the night runs: its tasks read and change the cars and the money these spend
            RepairsCommand = new RelayCommand(OnRepairs, () => SelectedCarHere && !_endingDay);
            SellCommand = new RelayCommand(OnSell, () => SelectedCarHere && !_endingDay && !_launcher.IsExecutionLocked);
            CollectCommand = new RelayCommand(OnCollect, CanCollect);
            SelectCarCommand = new RelayCommand(OnSelectCar, CanLeave);
            ShowCalendarCommand = new RelayCommand(OnShowCalendar);
            EndDayCommand = new RelayCommand(OnEndDay, () => !_endingDay && !_launcher.IsExecutionLocked);

            // While the night's work runs nothing leads out of the garage: the screens out there are built from the day
            // that is ending, and a race launched now would be stamped with it
            NewspaperCommand = new RelayCommand(() => LeaveTo(() => _navigationService.NavigateToNewspaper(_gameState)), CanLeave);
            CarDealersCommand = new RelayCommand(() => LeaveTo(() => _navigationService.NavigateToDealerMap(_gameState)), CanLeave);
            PartDealersCommand = new RelayCommand(() => LeaveTo(() => _navigationService.NavigateToPartShopMap(_gameState)), CanLeave);
            GoRaceCommand = new RelayCommand(OnGoRace, CanLeave);
            GasStationCommand = new AsyncRelayCommand(OnGasStation, () => SelectedCarHere && CanLeave());
            // Not while the night runs: picking a victory path may win the game there and then, and saves
            CareerCommand = new RelayCommand(OnCareer, CanLeave);

            // The garage is the game's home screen: make sure a car is selected if the player owns any
            if (_gameState.Player.SelectedCarInstanceId == null && _gameState.Player.Cars?.Count > 0)
            {
                _gameState.Player.SelectedCarInstanceId = _gameState.Player.Cars[0].InstanceId;
            }

            _cars = new ObservableCollection<CarDisplayViewModel>();

            Workbench = new PartsWorkbenchViewModel(
                partsService,
                _gameState,
                save: () =>
                {
                    if (!string.IsNullOrEmpty(_gameState.SaveName)) gameStateRepo.Save(_gameState, _gameState.SaveName);
                },
                spendMinutes: minutes => _timeService.SpendTimeAsync(_gameState, minutes));
            // Parts went on or came off the car: its engine is another one to start. Work on the stand's engine,
            // and switching between the views, leave the car's engine alone
            Workbench.CarEngineChanged += RefreshEngine;
            Workbench.StateChanged += () =>
            {
                RefreshCalendarDisplay();
            };
            Workbench.SaveFailed += _ => _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                _dialogService,
                GameText.Say("garage.save_warning"),
                GameText.Say("garage.save_warning.title")));
            Workbench.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PartsWorkbenchViewModel.IsOpen)) OnPropertyChanged(nameof(ShowNavigation));
            };

            // The cars are loaded in Enter, once: the screen is always entered right after it is made
        }

        private void LoadPlayerCars()
        {
            Cars.Clear();

            if (_gameState.Player.Cars == null || _gameState.Player.Cars.Count == 0)
            {
                _logger.Information("Player has no cars in garage");
                return;
            }

            _logger.Information("Loading {Count} player cars", _gameState.Player.Cars.Count);

            foreach (var entry in PlayerCars.Load(_gameState, _catalogRepo, _logger))
            {
                Cars.Add(new CarDisplayViewModel
                {
                    CarInstance = entry.Car,
                    CarDefinition = entry.Definition,
                    PreviewImagePath = entry.PreviewImagePath
                });
            }

            // Select the car that matches the game state's selected car, or first car by default
            if (Cars.Count > 0)
            {
                if (_gameState.Player.SelectedCarInstanceId != null)
                {
                    var selectedCarVm = Cars.FirstOrDefault(c =>
                        c.CarInstance.InstanceId == _gameState.Player.SelectedCarInstanceId);
                    SelectedCar = selectedCarVm ?? Cars[0];
                }
                else
                {
                    SelectedCar = Cars[0];
                }
            }

            _logger.Information("Loaded {Count} cars into garage view", Cars.Count);
        }

        private void LoadFreeRunTracks()
        {
            try
            {
                foreach (var track in _contentService.GetTracks().OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (track.Configurations.Count == 0) FreeRunTracks.Add(new FreeRunTrackViewModel(track.TrackId, null, track.Name));
                    foreach (var configuration in track.Configurations)
                        FreeRunTracks.Add(new FreeRunTrackViewModel(track.TrackId, configuration.FolderName, $"{track.Name} - {configuration.Name}"));
                }

                var remembered = _settingsService.Current.FreeRunTrack;
                _freeRunTrack = FreeRunTracks.FirstOrDefault(t => t.Key == remembered) ?? FreeRunTracks.FirstOrDefault(t => !t.Key.Contains("drag", StringComparison.OrdinalIgnoreCase)) ?? FreeRunTracks.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not list the tracks for a free run");
            }
        }

        /// <summary>
        /// The selected car out on its own on the chosen track, on what its parts make of it: a practice
        /// session the player ends when they have had enough. No opponent, no stake, no results; an hour goes by.
        /// </summary>
        private async Task OnFreeRun()
        {
            // Read once: the selection can change while the parts are checked and AC runs
            if (SelectedCar is not { } selected || FreeRunTrack is not { } track) return;

            var car = selected.CarInstance;
            _logger.Information("Free run: {Car} on {Track}", car.DefinitionId, track.Key);
            RememberFreeRunTrack();

            try
            {
                // The car races on its parts; one that will not go stays in
                Services.Race.RaceCarData? data = null;
                if (_partsService.IsAvailable)
                {
                    if (await _partsService.EnsurePartsAsync(car) && !string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);

                    // Blown, wrecked or totaled: it goes nowhere until it is repaired
                    var damage = Parts.Cars.CarCondition.WhyCannotRace(car, Parts.Cars.CarCondition.Groups(_partsService.Catalog));
                    if (damage.Count > 0)
                    {
                        _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                            _dialogService,
                            GameText.Say("garage.wont_run.damage", ("damage", string.Join("; ", damage)), ("fix", WhereToSortOut(car))),
                            GameText.Say("garage.wont_run.title")));
                        return;
                    }

                    // On a copy made here: the workbench may change the car's own parts while the worker reads them
                    var snapshot = car.Snapshot();
                    data = await Task.Run(() => _raceCarDataService.Prepare(snapshot, rateCooling: false));
                }

                if (data is { CanDrive: false })
                {
                    _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                        _dialogService,
                        GameText.Say("garage.wont_run.problem", ("problem", data.Problem)),
                        GameText.Say("garage.wont_run.title")));
                    return;
                }

                var intent = new FreeRunLaunchIntent
                {
                    CarId = selected.CarDefinition.Id,
                    SkinId = car.SkinId,
                    PlayerName = _gameState.Player.Name,
                    TrackId = track.TrackId,
                    TrackConfig = track.Configuration
                };
                if (data != null) intent.CarData.Add(data);

                // Checking the car takes a moment: a player who left meanwhile has called it off
                if (!ReferenceEquals(_navigationService.CurrentScreen, this)) return;

                FreeRunCommand.RaiseCanExecuteChanged();
                var result = await _launcher.LaunchRaceAsync(intent);
                if (result.Success)
                {
                    await _timeService.SpendTimeAsync(_gameState, GameAction.FreeRun);
                }
                else
                {
                    _logger.Error("Free run failed: {Error}", result.ErrorMessage);
                    _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                        _dialogService, GameText.Say("garage.free_run_failed", ("error", result.ErrorMessage)), GameText.Say("garage.launch_error.title")));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during the free run");
                _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                    _dialogService, GameText.Say("garage.free_run_failed", ("error", ex.Message)), GameText.Say("garage.launch_error.title")));
            }
            finally
            {
                FreeRunCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// The selected car at the strip on its own: a test-and-tune, pass after pass, each with its own tree and timeslip,
        /// nothing at stake but the car's wear. Run through the race pipeline (the race mode, a result, the timeslips
        /// after), an hour goes by, and the player comes back to the garage.
        /// </summary>
        private async Task OnTestAndTune()
        {
            if (SelectedCar is not { } selected) return;
            var car = selected.CarInstance;

            try
            {
                var strip = Shared.RaceSetupBuilder.PickStrip(_contentService.GetTracks());
                if (strip is not { } picked)
                {
                    _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                        _dialogService, GameText.Say("garage.no_strip"), GameText.Say("garage.no_strip.title")));
                    return;
                }

                var (trackId, trackConfig) = picked;
                _logger.Information("Test-and-tune: {Car} on {Track}/{Layout}", car.DefinitionId, trackId, trackConfig ?? "(none)");
                var setup = await new Shared.RaceSetupBuilder(_partsService, _raceCarDataService).BuildAsync(new Shared.RaceEntry
                {
                    PlayerName = _gameState.Player.Name,
                    PlayerCar = car,
                    TrackId = trackId,
                    TrackConfig = trackConfig,
                    RaceType = Models.Race.RaceType.DragRace,
                    DamagePercent = _gameState.Rules.RaceDamagePercent,
                    RaceTime = _gameState.Date,
                    TunePasses = TestAndTunePasses,
                    ReturnToGarage = true
                });

                // Setting the car up takes a moment: a player who left meanwhile has called it off
                if (!ReferenceEquals(_navigationService.CurrentScreen, this)) return;

                if (setup.Intent == null)
                {
                    _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                        _dialogService,
                        GameText.Say("garage.wont_run.damage", ("damage", setup.PlayerCarProblem), ("fix", WhereToSortOut(car))),
                        GameText.Say("garage.wont_run.title")));
                    return;
                }

                _navigationService.NavigateToRaceLoading(_gameState, setup.Intent);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception setting up the test-and-tune");
                _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                    _dialogService, GameText.Say("garage.strip_failed", ("error", ex.Message)), GameText.Say("garage.launch_error.title")));
            }
        }

        /// <summary>The repair bay for the selected car: each job paid for, timed and saved as it is done</summary>
        private void OnRepairs()
        {
            if (SelectedCar is not { } selected) return;

            var car = selected.CarInstance;
            _dialogService.ShowDialog(new Dialogs.RepairShop.RepairShopDialogViewModel(
                _dialogService, car, selected.DisplayName, Workbench.Catalog, _gameState.Rules.PartPriceMultiplier,
                () => _gameState.Player.Money,
                async job =>
                {
                    if (_gameState.Player.Money < job.Cost) return;

                    // The work first: a job that fails to go on costs nothing
                    job.Apply();
                    _gameState.Player.Money -= job.Cost;
                    _logger.Information("Repaired {Car}: {Job} for ${Cost}", car.DefinitionId, job.Id, job.Cost);

                    try
                    {
                        // Before saving: the time the work took belongs in the save, and so does the new day it may end in
                        await _timeService.SpendTimeAsync(_gameState, job.Time);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Could not spend the time for the repair: {Error}", ex.Message);
                    }

                    try
                    {
                        if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Could not save the game after a repair");
                    }

                    RefreshCalendarDisplay();
                    OnPropertyChanged(nameof(SelectedCar));
                    // The parts view shows the parts' shape: it gets the repaired ones
                    Workbench.SetCar(car);
                }));
        }

        // ----- the police impound -----

        /// <summary>A car is selected and it is in the garage, not the police impound</summary>
        private bool SelectedCarHere => SelectedCar is { CarInstance.IsImpounded: false };

        public bool IsSelectedCarImpounded => SelectedCar?.CarInstance.IsImpounded == true;

        /// <summary>Where the selected car is and what getting it back takes</summary>
        public string ImpoundNote
        {
            get
            {
                if (SelectedCar?.CarInstance is not { ImpoundedUntil: { } until } car) return string.Empty;
                return PoliceRules.CanCollect(car, _gameState.Date)
                    ? GameText.Say("garage.impound.now", ("fee", new Money(car.ImpoundFee)))
                    : GameText.Say("garage.impound.until", ("until", until), ("fee", new Money(car.ImpoundFee)));
            }
        }

        public string CollectButtonText => SelectedCar?.CarInstance is { IsImpounded: true } car ? GameText.Say("garage.collect.fee", ("fee", new Money(car.ImpoundFee))) : GameText.Say("garage.collect");

        private bool CanCollect() =>
            !_endingDay && SelectedCar?.CarInstance is { } car && PoliceRules.CanCollect(car, _gameState.Date) && _gameState.Player.Money >= car.ImpoundFee;

        /// <summary>Pays the impound, brings the car home and saves; an hour goes by getting there and back</summary>
        private async void OnCollect()
        {
            try
            {
                if (SelectedCar is not { } selected || !CanCollect()) return;

                var car = selected.CarInstance;
                _gameState.Player.Money -= car.ImpoundFee;
                _gameState.Player.Stats.TotalLosses += car.ImpoundFee;
                _logger.Information("Collected {Car} from the impound for ${Fee}", car.DefinitionId, car.ImpoundFee);
                PoliceRules.Release(car);

                try
                {
                    await _timeService.SpendTimeAsync(_gameState, GameAction.CollectFromImpound);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not spend the time for the impound: {Error}", ex.Message);
                }

                try
                {
                    if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not save the game after collecting the car");
                }

                Workbench.SetCar(car);
                RefreshCalendarDisplay();
                RaiseImpoundChanged();
            }
            catch (Exception ex)
            {
                // Nothing may escape an async void
                _logger.Error(ex, "Could not collect the car from the impound");
            }
        }

        private void RaiseImpoundChanged()
        {
            OnPropertyChanged(nameof(IsSelectedCarImpounded));
            OnPropertyChanged(nameof(ImpoundNote));
            OnPropertyChanged(nameof(CollectButtonText));
            RelayCommand.RaiseCanExecuteChanged();
            FreeRunCommand?.RaiseCanExecuteChanged();
            TestAndTuneCommand?.RaiseCanExecuteChanged();
        }

        /// <summary>"Sell", or where the selected car's sale stands when it is in the paper</summary>
        public string SellButtonText
        {
            get
            {
                if (SelectedCar is not { } selected || _saleService.AdFor(_gameState, selected.CarInstance) is not { } ad) return GameText.Say("garage.sell");
                return ad.Offer is { } offer && offer.Expires >= _gameState.Date ? GameText.Say("garage.sell.offer") : GameText.Say("garage.sell.in_paper");
            }
        }

        /// <summary>Selling the selected car: to the dealer, or through the paper</summary>
        private void OnSell()
        {
            if (SelectedCar is not { } selected) return;

            _dialogService.ShowDialog(new Dialogs.SellCar.SellCarDialogViewModel(
                _dialogService, _gameState, selected.CarInstance, selected.DisplayName, _saleService,
                sold: () =>
                {
                    // The car is gone: the garage shows what is left, the selected car the sale moved on to
                    LoadPlayerCars();
                    if (Cars.Count == 0) SelectedCar = null;
                    OnPropertyChanged(nameof(HasCars));
                    AfterSale();
                },
                changed: AfterSale));
        }

        private void AfterSale()
        {
            RefreshCalendarDisplay();
            OnPropertyChanged(nameof(SellButtonText));
        }

        private void OnSelectCar()
        {
            _logger.Information("Navigating to the car lot");
            _navigationService.NavigateToCarLot(_gameState);
        }

        /// <summary>
        /// Where a car that will not go is sorted out: an empty tank at the gas station (the garage only has a can of
        /// gas, to get it there), the damage with the Repairs button
        /// </summary>
        private string WhereToSortOut(Car car)
        {
            var dry = Parts.Cars.CarCondition.FuelLeft(car) is { } fuel && fuel < Parts.Cars.CarCondition.TooLittleToRaceLitres;
            if (!dry) return GameText.Say("garage.fix.repairs");

            var groupOf = _partsService.IsAvailable ? Parts.Cars.CarCondition.Groups(_partsService.Catalog) : null;
            var damaged = Parts.Cars.CarCondition.WhyCannotRace(car, groupOf, counting: false).Count > 0;
            return damaged ? GameText.Say("garage.fix.repairs_and_gas") : GameText.Say("garage.fix.gas");
        }

        /// <summary>
        /// To the gas station, in a car that gets there: one without an engine, or with one that will not run, stays in
        /// (an empty tank is the tow truck's, the station's own business)
        /// </summary>
        private async Task OnGasStation()
        {
            if (SelectedCar is not { } selected) return;
            var car = selected.CarInstance;

            try
            {
                if (_partsService.IsAvailable)
                {
                    if (await _partsService.EnsurePartsAsync(car) && !string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);

                    var spec = car.Engine == null ? null : await EngineSpecs.ForAsync(_partsService, car, selected.DisplayName);
                    var problem = car.HasPartsAssigned && car.Engine == null ? GameText.Say("garage.station.no_engine")
                        : spec is { Runs: false } ? GameText.Say("garage.station.engine_wont_run", ("problem", spec.Problem))
                        : null;
                    if (!ReferenceEquals(_navigationService.CurrentScreen, this)) return;
                    if (problem != null)
                    {
                        _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                            _dialogService, GameText.Say("garage.station.wont_make_it", ("problem", problem)), GameText.Say("garage.wont_run.title")));
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // The engine could not be worked out: the station finds out for itself
                _logger.Warning(ex, "Could not check {Car} before the gas station", car.DefinitionId);
            }

            // Out here too: a check that threw never got to it, and the player may have left meanwhile
            if (!ReferenceEquals(_navigationService.CurrentScreen, this)) return;

            LeaveTo(() => _navigationService.NavigateToGasStation(_gameState));
        }

        /// <summary>Leaves the garage (the window fades the screens over), unless the day is being ended</summary>
        private void LeaveTo(Func<bool> navigate)
        {
            if (!CanLeave()) return;

            try
            {
                navigate();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not leave the garage");
            }
        }

        private bool _endingDay;

        /// <summary>Nothing leads out of the garage, nor launches a race, while the day is being ended</summary>
        private bool CanLeave() => !_endingDay;

        /// <summary>Asks, then ends the day: the night's scheduled work runs, and the new day is saved</summary>
        private void OnEndDay()
        {
            var left = _timeService.GetRemainingMinutesToday(_gameState);
            // The day's first hour is the rules': written as the game's clock writes the time (8:00 AM, 2:00 PM)
            var morning = GameText.Date(DateTime.Today.AddHours(_timeService.DayStartHour), "format.time");
            var message = left > 0
                ? GameText.Say("garage.end_day.ask.left", ("hours", left / 60), ("minutes", left % 60), ("morning", morning))
                : GameText.Say("garage.end_day.ask", ("morning", morning));

            _dialogService.ShowDialog(new Dialogs.Confirmation.ConfirmationDialogViewModel(
                _dialogService, message, GameText.Say("garage.end_day.title"),
                confirmed =>
                {
                    if (confirmed) EndDay();
                }));
        }

        private async void EndDay()
        {
            if (_endingDay) return;
            _endingDay = true;
            RelayCommand.RaiseCanExecuteChanged();

            try
            {
                var result = await _timeService.EndDayAsync(_gameState);
                _logger.Information("Day ended from the garage; now {Date}", _gameState.Date);
                if (result.FailedTaskIds.Count > 0)
                    _logger.Warning("The night's work did not all get done: {Tasks}", string.Join(", ", result.FailedTaskIds));

                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not end the day");
                _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                    _dialogService, GameText.Say("garage.end_day.failed", ("error", ex.Message)), GameText.Say("garage.end_day.failed.title")));
            }
            finally
            {
                _endingDay = false;
                RelayCommand.RaiseCanExecuteChanged();
                // Nothing may escape an async void, the refresh included
                try
                {
                    RefreshCalendarDisplay();
                    // The night may have brought an offer for a car in the paper, or the impound's release date
                    OnPropertyChanged(nameof(SellButtonText));
                    RaiseImpoundChanged();

                    // And the mail order, the morning after
                    TellOfDeliveries();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not refresh the garage after the end of the day");
                }
            }
        }

        private void OnShowCalendar()
        {
            // Toggle between calendar and car preview
            ActivePanel = ActivePanel == GaragePanel.Calendar
                ? GaragePanel.CarPreview
                : GaragePanel.Calendar;

            _logger.Debug("Active panel changed to {Panel}", ActivePanel);
        }

        /// <summary>The way out: back to the main menu (the game stays saved), or out of the game altogether</summary>
        private void OnQuit()
        {
            _dialogService.ShowDialog(new Dialogs.Quit.QuitDialogViewModel(
                _dialogService,
                choice =>
                {
                    switch (choice)
                    {
                        case Dialogs.Quit.QuitChoice.MainMenu:
                            _logger.Information("Navigating back to main menu");
                            LeaveTo(() => _navigationService.NavigateToMainMenu());
                            break;
                        // Not while the night is being worked out: the exit saves, and a half-ended day would be saved
                        case Dialogs.Quit.QuitChoice.Desktop when CanLeave():
                            System.Windows.Application.Current.Shutdown();
                            break;
                    }
                }));
        }

        /// <summary>The selected car's sheet: its shape, what is in the tank and the sump, and every part in it</summary>
        private async void OnCarInfo()
        {
            // Nothing may escape an async void
            try
            {
                if (SelectedCar is not { } selected) return;

                // The engine as it is now: a change a moment ago may still be being worked out
                Task<Audio.EngineSpec?> work;
                Audio.EngineSpec? engine;
                do
                {
                    work = _engineWork;
                    engine = await work;
                } while (!ReferenceEquals(work, _engineWork));

                // Another car was picked meanwhile: its sheet is not this one
                if (!ReferenceEquals(SelectedCar, selected)) return;

                _dialogService.ShowDialog(new Dialogs.CarInfo.CarInfoDialogViewModel(
                    _dialogService,
                    selected.CarInstance,
                    SelectedCarDisplay,
                    _partsService.IsAvailable ? _partsService.Catalog : null,
                    engine));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not show the car's sheet");
            }
        }

        /// <summary>The racing hub over the garage; its "Hit the streets" goes to the diner</summary>
        private void OnGoRace()
        {
            if (!CanLeave()) return;

            _navigationService.ShowRaceHub(_gameState, () => LeaveTo(() => _navigationService.NavigateToDiner(_gameState)));
        }

        /// <summary>The career over the garage: the victory paths, the player's record and the milestones</summary>
        private void OnCareer()
        {
            if (!CanLeave()) return;

            _navigationService.ShowCareer(_gameState, () => LeaveTo(() => _navigationService.NavigateToMainMenu()));
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered garage screen");

            try
            {
                // Refresh calendar properties to reflect current game time
                RefreshCalendarDisplay();

                LoadPlayerCars();
                TellOfDeliveries();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not set up the garage");
            }
        }

        /// <summary>What the mail order brought while the player was out: on the shelf already, told of once</summary>
        private void TellOfDeliveries()
        {
            var delivered = _gameState.Player.Delivered;
            if (delivered is not { Count: > 0 }) return;

            var lines = delivered.GroupBy(n => n).Select(g => g.Count() == 1 ? g.Key : $"{g.Key} (x{g.Count()})");
            var message = GameText.Say("garage.delivery") + Environment.NewLine + Environment.NewLine +
                          string.Join(Environment.NewLine, lines.Select(l => "  •  " + l));
            delivered.Clear();
            _dialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(_dialogService, message, GameText.Say("garage.delivery.title")));

            try
            {
                if (!string.IsNullOrEmpty(_gameState.SaveName)) _gameStateRepo.Save(_gameState, _gameState.SaveName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save after telling of the deliveries");
            }
        }

        /// <summary>
        /// Notifies the UI that all calendar-related properties should be refreshed
        /// </summary>
        private void RefreshCalendarDisplay()
        {
            OnPropertyChanged(nameof(CurrentGameDate));
            OnPropertyChanged(nameof(CurrentMonthYear));
            OnPropertyChanged(nameof(CurrentDay));
            OnPropertyChanged(nameof(CurrentDayOfWeek));
            OnPropertyChanged(nameof(CurrentTimeDisplay));

            // The grid changes with the day and the pictures with the month: not on every piece of work in the garage
            if (_gameState.Date.Date != _shownCalendarDay)
            {
                _shownCalendarDay = _gameState.Date.Date;
                OnPropertyChanged(nameof(CalendarDays));
            }

            var month = _gameState.Date.Year * 100 + _gameState.Date.Month;
            if (month == _shownCalendarMonth) return;

            _shownCalendarMonth = month;
            OnPropertyChanged(nameof(CalendarImage1));
            OnPropertyChanged(nameof(CalendarImage2));
            OnPropertyChanged(nameof(CalendarImage3));
        }

        public override void Exit()
        {
            base.Exit();
            RememberFreeRunTrack();
            // The part models are the garage's: the lots, the menus and a race have no use for them in memory
            StreetCorsa.Parts.PartAssembler.ClearModelCache();
            _logger.Information("Exited garage screen");
        }

        /// <summary>The free run's track, kept in the settings for next time, across saves; written only when it changed</summary>
        private void RememberFreeRunTrack()
        {
            if (_freeRunTrack is not { } track || track.Key == _settingsService.Current.FreeRunTrack) return;

            try
            {
                _settingsService.Current.FreeRunTrack = track.Key;
                _settingsService.Save();
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not remember the free run's track: {Error}", ex.Message);
            }
        }
    }

    /// <summary>
    /// View model for displaying a car in the garage
    /// </summary>
    public class CarDisplayViewModel
    {
        public Car CarInstance { get; set; } = new();
        public CarDefinition CarDefinition { get; set; } = new();
        public string PreviewImagePath { get; set; } = string.Empty;

        /// <summary>AC car folder, used by the 3D viewport</summary>
        public string CarDirectory => Path.Combine(AppSettings.Instance.CarsPath, CarDefinition.Id);
        public string SkinId => !string.IsNullOrEmpty(CarInstance.SkinId) ? CarInstance.SkinId : "default";

        public string DisplayName => $"{CarDefinition.Brand} {CarDefinition.Name}";
        public string YearDisplay => CarDefinition.Year?.ToString() ?? GameText.Say("garage.year_unknown");
        public bool HasPreviewImage => !string.IsNullOrEmpty(PreviewImagePath);
    }

    /// <summary>
    /// View model for a single day in the calendar grid
    /// </summary>
    public class CalendarDayViewModel
    {
        public int Day { get; set; }
        public bool IsCurrentDay { get; set; }
        /// <summary>A day of the month already gone, drawn faint</summary>
        public bool IsPast { get; set; }
        public bool IsEmpty { get; set; }
    }

    /// <summary>A track, or one layout of it, a free run can go to</summary>
    public sealed class FreeRunTrackViewModel
    {
        public FreeRunTrackViewModel(string trackId, string? configuration, string name)
        {
            TrackId = trackId;
            Configuration = string.IsNullOrEmpty(configuration) ? null : configuration;
            Name = name;
        }

        public string TrackId { get; }
        public string? Configuration { get; }
        public string Name { get; }

        /// <summary>"track" or "track/configuration", as the settings remember it</summary>
        public string Key => Configuration == null ? TrackId : $"{TrackId}/{Configuration}";

        public override string ToString() => Name;
    }

    /// <summary>
    /// A car just taken out of the lot comes to the garage: which one, its engine as the lot worked it out (null when it
    /// could not), and whether it drives in (it drove off out of the lot) or is simply there
    /// </summary>
    public sealed record CarArrival(Guid InstanceId, Audio.EngineSpec? Engine, bool DrivesIn);
}
