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
    public class GarageScreenViewModel : BaseScreenViewModel, IScriptedScreen
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
        private readonly ICarSaleService _saleService;
        private readonly IAppLogger _logger;

        public RelayCommand RepairsCommand { get; }
        public RelayCommand SellCommand { get; }

        /// <summary>Pays the impound and brings the selected car home, once its days there are up</summary>
        public RelayCommand CollectCommand { get; }

        public RelayCommand SelectCarCommand { get; }

        /// <summary>Calls it a day: the rest of today goes by, and the game picks up tomorrow morning</summary>
        public RelayCommand EndDayCommand { get; }

        // The round buttons round the car, by where they stand: this screen's tiles in the flow, built the first time
        // they are asked for (the screen knows its id in the flow only once it is built)
        private Dictionary<string, List<GarageTile>>? _tiles;

        public IReadOnlyList<GarageTile> TopLeftTiles => TilesAt(TileSpots.TopLeft);
        public IReadOnlyList<GarageTile> LeftTiles => TilesAt(TileSpots.Left);
        public IReadOnlyList<GarageTile> BottomLeftTiles => TilesAt(TileSpots.BottomLeft);
        public IReadOnlyList<GarageTile> TopRightTiles => TilesAt(TileSpots.TopRight);
        public IReadOnlyList<GarageTile> RightTiles => TilesAt(TileSpots.Right);
        public IReadOnlyList<GarageTile> BottomRightTiles => TilesAt(TileSpots.BottomRight);

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
                RaiseSaleChanged();
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
            ICarSaleService saleService,
            bool skipAnimation = false,
            CarArrival? arrival = null,
            bool endDay = false)
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
            _logger = AppLoggerFactory.CreateLogger("Garage");
            SkipEnterAnimation = skipAnimation;
            _arrival = arrival;
            CarArriving = arrival?.DrivesIn == true;
            _endDayOnArrival = endDay;

            // Not while the night runs: its tasks read and change the cars and the money these spend
            RepairsCommand = new RelayCommand(OnRepairs, () => SelectedCarHere && !_endingDay);
            SellCommand = new RelayCommand(OnSell, () => SelectedCarHere && !_endingDay && !_launcher.IsExecutionLocked);
            CollectCommand = new RelayCommand(OnCollect, CanCollect);
            SelectCarCommand = new RelayCommand(OnSelectCar, CanLeave);
            EndDayCommand = new RelayCommand(OnEndDay, () => !_endingDay && !_launcher.IsExecutionLocked);

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
        }

        /// <summary>"Sell", or where the selected car's sale stands when it is in the paper</summary>
        public string SellButtonText => !IsSelectedCarInPaper ? GameText.Say("garage.sell")
            : HasSaleOffer ? GameText.Say("garage.sell.offer")
            : GameText.Say("garage.sell.in_paper");

        /// <summary>The selected car has an ad in the paper</summary>
        public bool IsSelectedCarInPaper => SelectedAd != null;

        /// <summary>Someone answered the selected car's ad, and the offer still stands</summary>
        public bool HasSaleOffer => SelectedAd?.Offer is { } offer && offer.Expires >= _gameState.Date;

        private CarSaleAd? SelectedAd => SelectedCar is { } selected ? _saleService.AdFor(_gameState, selected.CarInstance) : null;

        private void RaiseSaleChanged()
        {
            OnPropertyChanged(nameof(SellButtonText));
            OnPropertyChanged(nameof(IsSelectedCarInPaper));
            OnPropertyChanged(nameof(HasSaleOffer));
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
            RaiseSaleChanged();
        }

        private void OnSelectCar()
        {
            _logger.Information("Navigating to the car lot");
            _navigationService.Leave(this, "car_lot", _gameState);
        }

        /// <summary>
        /// To the gas station, in a car that gets there: one without an engine, or with one that will not run, stays in
        /// (an empty tank is the tow truck's, the station's own business)
        /// </summary>
        private async Task OnGasStation(string station)
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
            if (!ReferenceEquals(_navigationService.CurrentScreen, this) || !CanLeave()) return;

            // The car drives out as a mod's script has it; the garage goes when the script lets it
            var droveOut = false;
            if (DriveCarOut is { } driveOut)
            {
                try
                {
                    droveOut = await driveOut();
                }
                catch (Exception ex)
                {
                    // The show is not the business: the car goes all the same
                    _logger.Error(ex, "The car could not drive out");
                }
            }

            // The script may have taken a while: the player may have gone some other way meanwhile
            if (!ReferenceEquals(_navigationService.CurrentScreen, this)) return;

            LeaveTo(() => _navigationService.Go(station, _gameState));

            // Still here (the station would not open): the car back in its place
            if (droveOut && ReferenceEquals(_navigationService.CurrentScreen, this)) PutCarBack?.Invoke();
        }

        /// <summary>
        /// The car drives out as its script has it before the garage is left for the road (set by the view); true when
        /// a script played. Done when the garage may go.
        /// </summary>
        public Func<Task<bool>>? DriveCarOut { get; set; }

        /// <summary>The car back in its place, when the garage stays after all (set by the view)</summary>
        public Action? PutCarBack { get; set; }

        /// <inheritdoc cref="IScriptedScreen.PlayScreenScript"/>
        public Func<string, Services.Scripting.ScriptFacts, Task?>? PlayScreenScript { get; set; }

        /// <inheritdoc cref="IScriptedScreen.PutBackAfterLeave"/>
        public void PutBackAfterLeave() => PutCarBack?.Invoke();

        /// <summary>The view's word: the scene is up and the screen's own show is over (the flow's enter script may play)</summary>
        public void SceneReady() => _navigationService.ScreenReady(this);

        /// <summary>The moment the car moves in, for its script: the game's clock and the light of the day</summary>
        public Services.Scripting.ScriptFacts ScriptFacts => new(_gameState.Date, Services.Street.StreetLights.At(_gameState.Date));

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
        private void OnEndDay() => Dialogs.Confirmation.EndDayQuestion.Ask(_dialogService, _timeService, _gameState, EndDay);

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
                    RaiseSaleChanged();
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
                            LeaveTo(() => _navigationService.Leave(this, "quit", null));
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

        private IReadOnlyList<GarageTile> TilesAt(string spot)
        {
            _tiles ??= BuildTiles();
            return _tiles.TryGetValue(spot, out var tiles) ? tiles : [];
        }

        private Dictionary<string, List<GarageTile>> BuildTiles()
        {
            var flow = _navigationService.Flow;
            var hub = _navigationService.IdOf(this) ?? flow.FirstOfKind(ScreenKinds.Garage);
            var bySpot = new Dictionary<string, List<GarageTile>>();
            foreach (var (id, tile) in flow.TilesOf(hub))
            {
                if (!bySpot.TryGetValue(tile.Spot, out var spot)) bySpot[tile.Spot] = spot = [];
                var text = tile.Text is { } key ? GameText.Say(key) : null;
                var icon = tile.Icon is { } image ? Styles.ThemeImages.Icon(image, 128) : null;
                spot.Add(new GarageTile(id, tile, text, icon, TileCommand(tile), first: spot.Count == 0));
            }

            return bySpot;
        }

        /// <summary>
        /// What a tile does: one of the garage's own things, or a way out to its screen. While the night's work runs
        /// nothing leads out of the garage: the screens out there are built from the day that is ending, a race launched
        /// now would be stamped with it, and picking a victory path in the career may win the game there and then.
        /// </summary>
        private System.Windows.Input.ICommand TileCommand(FlowTile tile)
        {
            bool Has() => tile.Requires.All(need => need switch
            {
                TileRequirements.Car => SelectedCar != null,
                TileRequirements.CarHere => SelectedCarHere,
                _ => true
            });

            switch (tile.To.ToLowerInvariant())
            {
                case TileActions.Quit:
                    return new RelayCommand(OnQuit, () => CanLeave() && Has());
                case TileActions.CarInfo:
                    return new RelayCommand(OnCarInfo, () => SelectedCar != null && Has());
                case TileActions.Calendar:
                    return new RelayCommand(OnShowCalendar, Has);
                case TileActions.Radio:
                    return Audio.Radio.RadioPanelViewModel.ToggleCommand;
            }

            var to = tile.To;
            return new AsyncRelayCommand(() => OnTileAsync(to), () => CanLeave() && Has(), name: "tile " + to);
        }

        /// <summary>
        /// Out of the garage to the tile's screen: the racing hub and the career open over it, the car drives out to the
        /// gas station as its script has it
        /// </summary>
        private async Task OnTileAsync(string to)
        {
            if (!CanLeave()) return;

            var kind = _navigationService.Flow.KindOf(to);
            if (ScreenKinds.IsOverlay(kind))
            {
                _navigationService.Go(to, _gameState, leaveVia: LeaveTo);
                return;
            }

            if (kind == ScreenKinds.GasStation)
            {
                await OnGasStation(to);
                return;
            }

            _logger.Information("From the garage to {Screen}", to);
            LeaveTo(() => _navigationService.GoFrom(this, to, _gameState));
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

            // Home for the night from another screen, the player having said so there: the day ends once the garage is up
            if (_endDayOnArrival)
            {
                _endDayOnArrival = false;
                _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(EndDay);
            }
        }

        private bool _endDayOnArrival;

        /// <summary>The flow lets the day be ended here: the End Day button is shown</summary>
        public bool CanEndDayHere => _navigationService.Flow.Screen(_navigationService.IdOf(this)) is not { EndDay: false };

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
            // The part models are the garage's: the lots, the menus and a race have no use for them in memory
            StreetCorsa.Parts.PartAssembler.ClearModelCache();
            _logger.Information("Exited garage screen");
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

    /// <summary>
    /// A car just taken out of the lot comes to the garage: which one, its engine as the lot worked it out (null when it
    /// could not), and whether it drives in (it drove off out of the lot) or is simply there
    /// </summary>
    public sealed record CarArrival(Guid InstanceId, Audio.EngineSpec? Engine, bool DrivesIn);
}
