using System.Collections.ObjectModel;
using System.IO;
using StreetCorsa.Configuration;
using StreetCorsa.Controls;
using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.CarLot;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Time;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.CarLot
{
    /// <summary>
    /// The player's cars, parked in their warehouses (<see cref="CarStorage"/>): one warehouse at a time in 3D, the next
    /// and the one before a click away, and every car in the list down the side, which goes to its warehouse and onto it.
    ///
    /// Like the dealer's lot, two views of one scene, the whole warehouse or one car, and which one is showing is
    /// <see cref="SelectedIndex"/>. With a car picked, one button takes it (it becomes the car the garage works on).
    ///
    /// The car the player drives is in the garage, not here: it heads the list, and is parked in the bay of the car taken
    /// out in its place. A car in the police impound keeps its bay for when it is back, but is not standing in it.
    /// </summary>
    public class CarLotScreenViewModel : BaseScreenViewModel
    {
        /// <summary>How many cars a warehouse holds when no scene is installed to say: the lot is then only the list</summary>
        private const int FallbackCapacity = 10;

        private readonly NavigationService _navigationService;
        private readonly GameState _gameState;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly IGameTimeService _timeService;
        private readonly IAppLogger _logger;

        // Read as the screen is entered: the mods installed then
        private IReadOnlyList<WarehouseScene> _scenes = [];

        // A pick under way: the switch is paid for once, and nothing on the screen moves while the time is spent
        private bool _taking;

        private List<CarLotCarViewModel> _warehouseCars = [];

        public RelayCommand BackCommand { get; }
        public RelayCommand PreviousWarehouseCommand { get; }
        public RelayCommand NextWarehouseCommand { get; }
        public RelayCommand TakeCarCommand { get; }
        public RelayCommand<CarLotCarViewModel> FindCarCommand { get; }

        /// <summary>Every car the player has, the one in the garage first, then by warehouse and bay: the list down the side</summary>
        public ObservableCollection<CarLotCarViewModel> AllCars { get; } = [];

        /// <summary>The cars standing in the warehouse on screen, by bay, in the same order as <see cref="LotCars"/></summary>
        public IReadOnlyList<CarLotCarViewModel> WarehouseCars => _warehouseCars;

        /// <summary>The cars the 3D warehouse stands up, each in its bay</summary>
        public IReadOnlyList<LotCarPlacement> LotCars { get; private set; } = [];

        /// <summary>The scene's model, or null when no warehouse is installed</summary>
        public string? ShowroomKn5 { get; private set; }

        public LotDressing? Dressing { get; private set; }
        public LotView? LotView { get; private set; }
        public LotRoom? LotRoom { get; private set; }
        public float LotRadius { get; private set; } = 12f;

        public bool HasScene => ShowroomKn5 != null;

        /// <summary>No car is being taken: the warehouse and the list answer the pointer</summary>
        public bool IsIdle => !_taking;

        public string BankrollDisplay => $"${_gameState.Player.Money:N0}";
        public string DateDisplay => _gameState.Date.ToString("dddd, MMMM d, yyyy");
        public string TimeDisplay => _gameState.Date.ToString("h:mm tt");

        public CarLotScreenViewModel(
            NavigationService navigationService,
            GameState gameState,
            IContentCatalogRepository catalogRepo,
            IGameStateRepository gameStateRepo,
            IGameTimeService timeService)
        {
            _navigationService = navigationService;
            _gameState = gameState;
            _catalogRepo = catalogRepo;
            _gameStateRepo = gameStateRepo;
            _timeService = timeService;
            _logger = AppLoggerFactory.CreateLogger("CarLot");

            BackCommand = new RelayCommand(OnBack, () => !_taking);
            PreviousWarehouseCommand = new RelayCommand(() => ShowWarehouse(Warehouse - 1), () => !_taking && Warehouse > 1);
            NextWarehouseCommand = new RelayCommand(() => ShowWarehouse(Warehouse + 1), () => !_taking && Warehouse < WarehouseCount);
            TakeCarCommand = new RelayCommand(OnTakeCar, () => !_taking && SelectedCar != null);
            FindCarCommand = new RelayCommand<CarLotCarViewModel>(OnFindCar, _ => !_taking);
        }

        #region Warehouses

        private int _warehouse = 1;

        /// <summary>The warehouse on screen, from 1</summary>
        public int Warehouse
        {
            get => _warehouse;
            private set
            {
                _warehouse = value;
                OnPropertyChanged(nameof(Warehouse));
                OnPropertyChanged(nameof(WarehouseTitle));
                OnPropertyChanged(nameof(WarehouseSummary));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>How many warehouses the player has: up to the last one with a car of the list in it</summary>
        public int WarehouseCount { get; private set; } = 1;

        public bool HasSeveralWarehouses => WarehouseCount > 1;

        public string WarehouseTitle => WarehouseCount > 1 ? $"Warehouse {Warehouse} of {WarehouseCount}" : "Your warehouse";

        /// <summary>What is in this warehouse, and in all of them</summary>
        public string WarehouseSummary
        {
            get
            {
                var here = WarehouseCars.Count;
                var inAll = AllCars.Count(c => c.Car.Warehouse != null);

                // A bay with a car's place in it is taken, whether the car is standing there or not (at the impound,
                // or its model no longer installed)
                var room = CapacityOf(Warehouse) - (_gameState.Player.Cars ?? []).Count(c => c.Warehouse == Warehouse && c.Bay != null);
                if (inAll == 0) return AllCars.Count == 0 ? "No cars yet: the dealers have plenty" : "Empty: your car is in the garage";

                var line = here == 0 ? "Nothing parked in here" : here == 1 ? "1 car in here" : $"{here} cars in here";
                if (WarehouseCount > 1) line += $", {inAll} in all";
                return room > 0 ? $"{line}; room for {room} more" : $"{line}; full";
            }
        }

        /// <summary>The scene warehouse <paramref name="number"/> is in: the installed ones in turn</summary>
        private WarehouseScene? SceneOf(int number) => _scenes.Count == 0 ? null : _scenes[(number - 1) % _scenes.Count];

        private int CapacityOf(int number) => SceneOf(number)?.Capacity ?? FallbackCapacity;

        /// <summary>
        /// Gives every car a place, and fills the list. Places given for the first time are saved at once, so a car is
        /// where the player left it next time whatever happens before the next save.
        /// </summary>
        private void Arrange()
        {
            var cars = _gameState.Player.Cars ??= [];
            var inGarage = _gameState.Player.SelectedCarInstanceId;
            if (CarStorage.Arrange(cars, CapacityOf, inGarage)) Save("new places in the warehouses");

            // A car whose model is not installed keeps its bay for when it is back, but is not in the list, and a
            // warehouse only it is in is not one to go to
            var listed = new List<(Car Car, CarDefinition Definition)>();
            foreach (var car in cars)
            {
                if (_catalogRepo.GetCar(car.DefinitionId) is { } definition) listed.Add((car, definition));
                else _logger.Warning("Car definition not found for car instance {InstanceId}, definition {DefinitionId}", car.InstanceId, car.DefinitionId);
            }

            WarehouseCount = CarStorage.WarehouseCount(listed.Select(l => l.Car));
            OnPropertyChanged(nameof(WarehouseCount));
            OnPropertyChanged(nameof(HasSeveralWarehouses));

            AllCars.Clear();
            foreach (var (car, definition) in listed.OrderBy(l => l.Car.InstanceId != inGarage).ThenBy(l => l.Car.Warehouse).ThenBy(l => l.Car.Bay))
                AllCars.Add(new CarLotCarViewModel(car, definition, WarehouseCount > 1) { IsCurrent = car.InstanceId == inGarage });
        }

        /// <summary>
        /// Puts warehouse <paramref name="number"/> on screen, dressed as it is at this hour, back on the view of the whole
        /// warehouse unless <paramref name="select"/> says which car to go to
        /// </summary>
        private void ShowWarehouse(int number, CarLotCarViewModel? select = null)
        {
            number = Math.Clamp(number, 1, WarehouseCount);
            SelectedIndex = -1;
            Warehouse = number;

            var scene = SceneOf(number);
            var listed = AllCars.ToDictionary(c => c.Car.InstanceId);
            var cars = CarStorage.In(AllCars.Select(c => c.Car), number).Select(c => listed[c.InstanceId]).ToList();
            _warehouseCars = cars;

            var placements = new List<LotCarPlacement>();
            if (scene != null)
            {
                foreach (var car in cars)
                {
                    var bay = scene.Bays[Math.Clamp(car.Car.Bay ?? 0, 0, scene.Bays.Count - 1)];
                    placements.Add(new LotCarPlacement(car.CarDirectory, car.SkinId, bay.X, bay.Z, bay.Heading, car.DisplayName));
                }

                // The same scene is dressed again, not loaded again: warehouse 2 is warehouse 1 in its next look
                var round = (number - 1) / _scenes.Count;
                var dressing = scene.Dress(number, round, StreetLights.At(_gameState.Date));
                Dressing = new LotDressing(dressing.Lighting, dressing.Sun, dressing.Pictures, dressing.Glow);
                LotView = new LotView(scene.Camera.Alpha, scene.Camera.Beta, scene.Camera.TargetHeight);
                LotRoom = scene.Room is { } room ? new LotRoom(room.HalfLength, room.HalfWidth, room.Height) : null;
                LotRadius = scene.Camera.Radius;
            }

            ShowroomKn5 = scene?.Kn5;
            LotCars = placements;

            OnPropertyChanged(nameof(ShowroomKn5));
            OnPropertyChanged(nameof(HasScene));
            OnPropertyChanged(nameof(Dressing));
            OnPropertyChanged(nameof(LotView));
            OnPropertyChanged(nameof(LotRoom));
            OnPropertyChanged(nameof(LotRadius));
            OnPropertyChanged(nameof(LotCars));
            OnPropertyChanged(nameof(WarehouseCars));
            OnPropertyChanged(nameof(WarehouseTitle));
            OnPropertyChanged(nameof(WarehouseSummary));
            foreach (var car in AllCars) car.IsHere = cars.Contains(car);

            _logger.Information("Warehouse {Number} of {Count}: {Cars} cars", number, WarehouseCount, cars.Count);

            if (select != null) SelectedIndex = cars.IndexOf(select);
        }

        /// <summary>
        /// A car in the list: its warehouse, and the camera onto it; the one in the garage is not in one, and one at the
        /// impound is not standing in its
        /// </summary>
        private void OnFindCar(CarLotCarViewModel? car)
        {
            if (_taking || car is null || car.Car.IsImpounded || car.Car.Warehouse is not { } number) return;

            if (number == Warehouse)
            {
                SelectedIndex = _warehouseCars.IndexOf(car);
                return;
            }

            ShowWarehouse(number, car);
        }

        #endregion

        #region The car looked at

        private int _selectedIndex = -1;

        /// <summary>Which car of <see cref="WarehouseCars"/> the camera is on, or -1 for the whole warehouse</summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                OnPropertyChanged(nameof(SelectedIndex));
                OnPropertyChanged(nameof(SelectedCar));
                OnPropertyChanged(nameof(HasSelection));
                foreach (var car in AllCars) car.IsLookedAt = car == SelectedCar;
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>The car being looked at, or null on the view of the whole warehouse</summary>
        public CarLotCarViewModel? SelectedCar =>
            _selectedIndex >= 0 && _selectedIndex < WarehouseCars.Count ? WarehouseCars[_selectedIndex] : null;

        public bool HasSelection => SelectedCar != null;

        #endregion

        #region Actions

        /// <summary>
        /// Takes the car being looked at: it becomes the car the garage works on, and the player goes there with it.
        /// A quarter of an hour goes by moving the cars round.
        /// </summary>
        private async void OnTakeCar()
        {
            if (SelectedCar is not { } car || _taking) return;

            _taking = true;
            OnPropertyChanged(nameof(IsIdle));
            RelayCommand.RaiseCanExecuteChanged();
            try
            {
                _logger.Information("Took {CarId} (Instance: {InstanceId}) out of warehouse {Warehouse}",
                    car.Definition.Id, car.Car.InstanceId, Warehouse);

                // Out of its bay, and the car from the garage parked in it
                var player = _gameState.Player;
                CarStorage.TakeOut(car.Car, player.Cars.FirstOrDefault(c => c.InstanceId == player.SelectedCarInstanceId));
                player.SelectedCarInstanceId = car.Car.InstanceId;

                // Waited for before saving: late in the day that is the next morning, and what the new day brings
                // belongs in the save
                try
                {
                    await _timeService.SpendTimeAsync(_gameState, GameAction.SwitchCar);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not spend the time for switching cars");
                }

                Save("taking a car");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not switch cars");
            }
            finally
            {
                _taking = false;
                OnPropertyChanged(nameof(IsIdle));
                RelayCommand.RaiseCanExecuteChanged();
            }

            // The navigation service catches and reports a garage that will not open
            OnBack();
        }

        private void Save(string why)
        {
            try
            {
                if (!string.IsNullOrEmpty(_gameState.SaveName))
                {
                    _gameStateRepo.Save(_gameState, _gameState.SaveName);
                    _logger.Information("Game saved after {Why}", why);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save the game after {Why}", why);
            }
        }

        private void OnBack()
        {
            _logger.Information("Back to the garage");
            _navigationService.NavigateToGarage(_gameState, skipAnimation: true);
        }

        #endregion

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered the car lot");

            _scenes = WarehouseScenes.LoadAll(Mods.ModSet.Installed);
            Arrange();
            ShowWarehouse(1);
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Left the car lot");
        }
    }

    /// <summary>One of the player's cars, in the list and in the warehouse</summary>
    public sealed class CarLotCarViewModel : ObservableObject
    {
        public CarLotCarViewModel(Car car, CarDefinition definition, bool severalWarehouses)
        {
            Car = car;
            Definition = definition;
            SeveralWarehouses = severalWarehouses;
        }

        public Car Car { get; }
        public CarDefinition Definition { get; }
        private bool SeveralWarehouses { get; }

        /// <summary>AC car folder, for the 3D warehouse</summary>
        public string CarDirectory => Path.Combine(AppSettings.Instance.CarsPath, Definition.Id);
        public string SkinId => !string.IsNullOrEmpty(Car.SkinId) ? Car.SkinId : "default";

        public string DisplayName => $"{Definition.Brand} {Definition.Name}";

        /// <summary>Where it is kept: "W2 · bay 4" when there are several warehouses, "Bay 4" when there is one</summary>
        public string PlaceDisplay => Car.IsImpounded ? "At the police impound"
            : Car.Warehouse == null ? "In the garage"
            : SeveralWarehouses ? $"W{Car.Warehouse} · bay {Car.Bay + 1}" : $"Bay {Car.Bay + 1}";

        /// <summary>The car the garage works on: in the garage, not in a warehouse</summary>
        public bool IsCurrent { get; init; }

        private bool _isHere;

        /// <summary>In the warehouse on screen</summary>
        public bool IsHere
        {
            get => _isHere;
            set => SetProperty(ref _isHere, value);
        }

        private bool _isLookedAt;

        /// <summary>The car the camera is on</summary>
        public bool IsLookedAt
        {
            get => _isLookedAt;
            set => SetProperty(ref _isLookedAt, value);
        }
    }
}
