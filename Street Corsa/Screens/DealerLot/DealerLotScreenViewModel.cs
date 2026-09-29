using System.Collections.ObjectModel;
using System.IO;
using StreetCorsa.Configuration;
using StreetCorsa.Controls;
using StreetCorsa.Dialogs;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Screens.UsedCarMarket;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.DealerLot
{
    /// <summary>
    /// One dealer's lot, with its cars standing in it.
    ///
    /// Two views of the same scene: the whole lot, or one car. Which one is showing is
    /// <see cref="SelectedIndex"/>, and everything else on screen follows from it.
    /// </summary>
    public class DealerLotScreenViewModel : BaseScreenViewModel
    {
        /// <summary>The sound of the place</summary>
        public override string? Ambience => "ambience.lot";

        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly Models.GameState.GameState _gameState;
        private readonly IDealerCatalog _dealerCatalog;
        private readonly IUsedCarMarketService _marketService;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly IGameStateRepository _gameStateRepo;
        private readonly PurchaseFlow _purchaseFlow;
        private readonly ICarPartsService _partsService;
        private readonly IAppLogger _logger;
        private readonly DealerDefinition? _dealer;

        public RelayCommand BackCommand { get; }
        public RelayCommand BackToLotCommand { get; }
        public RelayCommand<UsedCarListingViewModel> PurchaseCarCommand { get; }

        /// <summary>Everything this dealer has, whether or not it fitted on the lot</summary>
        public ObservableCollection<UsedCarListingViewModel> Stock { get; } = [];

        /// <summary>The cars the 3D lot should stand up, in the same order as the first of <see cref="Stock"/></summary>
        public IReadOnlyList<LotCarPlacement> LotCars { get; private set; } = [];

        public string DealerName => _dealer?.Name ?? "Dealer";
        public string DealerRegion => _dealer?.Region ?? string.Empty;
        public string DealerBlurb => _dealer?.Blurb ?? string.Empty;

        /// <summary>The closing-down sale, when this lot is holding one: a line under the name</summary>
        public string SaleNotice
        {
            get
            {
                if (DealerRoster.Find(_gameState, _dealer?.Id) is not { Status: DealerStatus.ClosingDown, ClosesOn: { } last } dealer) return string.Empty;

                var daysLeft = DealerRules.DaysLeft(dealer, _gameState.Date);
                return GameText.Installed.Get(daysLeft == 0 ? "dealer.lot.sale_last_day" : "dealer.lot.sale", new TextValues
                {
                    ["dealer"] = dealer.Name,
                    ["discount"] = (int)Math.Round(DealerRules.DiscountOn(dealer, _gameState.Date) * 100),
                    ["day"] = last
                });
            }
        }

        public bool HasSaleNotice => SaleNotice.Length > 0;
        public string BankrollDisplay => Bankroll(_gameState.Player.Money);

        /// <summary>The clock, so the hours the drive cost are in front of the player when they arrive</summary>
        public string DateDisplay => HeaderDate(_gameState.Date);
        public string TimeDisplay => HeaderTime(_gameState.Date);

        /// <summary>The lot's 3D scene, or null when the showroom is not installed</summary>
        public string? ShowroomKn5 { get; private set; }

        /// <summary>How far back the camera stands to take the lot in, worked out from the room it is in</summary>
        public float LotRadius { get; private set; } = 19f;

        public DealerLotScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            Models.GameState.GameState gameState,
            string dealerId,
            IDealerCatalog dealerCatalog,
            IUsedCarMarketService marketService,
            IContentCatalogRepository catalogRepo,
            ICarProfileRepository profileRepo,
            IGameStateRepository gameStateRepo,
            ICarPurchaseService purchaseService,
            ICarPartsService partsService)
        {
            _navigationService = navigationService;
            _dialogService = dialogService;
            _gameState = gameState;
            _dealerCatalog = dealerCatalog;
            _marketService = marketService;
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _gameStateRepo = gameStateRepo;
            _partsService = partsService;
            _logger = AppLoggerFactory.CreateLogger("DealerLot");
            _purchaseFlow = new PurchaseFlow(dialogService, purchaseService, _logger);
            _dealer = dealerCatalog.Get(dealerId);

            BackCommand = new RelayCommand(OnBack);
            BackToLotCommand = new RelayCommand(() => SelectedIndex = -1);
            PurchaseCarCommand = new RelayCommand<UsedCarListingViewModel>(OnPurchaseCar, CanPurchaseCar);

            // The showroom and the stock are loaded in Enter, once the screen before has been left
        }

        private int _selectedIndex = -1;

        /// <summary>Which car of <see cref="LotCars"/> the camera is on, or -1 for the whole lot</summary>
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
                RefreshEngine();
            }
        }

        private Audio.EngineSpec? _selectedEngine;
        private int _engineVersion;

        /// <summary>The engine of the car being looked at, as it is sold, to start and rev on the lot</summary>
        public Audio.EngineSpec? SelectedEngine
        {
            get => _selectedEngine;
            private set
            {
                _selectedEngine = value;
                OnPropertyChanged(nameof(SelectedEngine));
            }
        }

        private async void RefreshEngine()
        {
            var version = ++_engineVersion;
            var car = SelectedCar;

            // Off at once: the car the camera leaves must not keep running
            SelectedEngine = null;
            if (car == null) return;

            try
            {
                var spec = await EngineSpecs.ForSaleAsync(_partsService, car.Listing, car.DisplayName);
                if (version == _engineVersion) SelectedEngine = spec;
            }
            catch (Exception ex)
            {
                // No engine to start is all that comes of it: the lot and the car are still there to look at
                _logger.Error(ex, "Could not work out the engine of {Car}", car.DisplayName);
            }
        }

        /// <summary>The car being looked at, or null on the lot view</summary>
        public UsedCarListingViewModel? SelectedCar =>
            _selectedIndex >= 0 && _selectedIndex < Stock.Count ? Stock[_selectedIndex] : null;

        public bool HasSelection => SelectedCar != null;

        private void ResolveShowroom()
        {
            var id = _dealer?.ShowroomId;
            if (string.IsNullOrWhiteSpace(id)) return;

            // A mod's garage by that name, else the AC showroom (whose model is not always spelled like its folder)
            var found = Services.Showcase.ShowcaseContent.FindScene(id);
            if (found != null)
            {
                ShowroomKn5 = found;
                OnPropertyChanged(nameof(ShowroomKn5));
                return;
            }

            _logger.Warning("Showroom {Showroom} is not installed; the lot will have no scene", id);
        }

        private void LoadStock()
        {
            Stock.Clear();

            var listings = _marketService.GetListingsByDealer(_gameState.UsedCarMarket ?? [], _dealer?.Id ?? string.Empty);

            // The nearest cars go in the bays, in price order so the lot reads from cheap to dear
            foreach (var listing in listings.OrderBy(l => l.Price))
            {
                var carDef = _catalogRepo.GetCar(listing.CarDefinitionId);
                if (carDef == null)
                {
                    _logger.Warning("No car definition for listing {ListingId}", listing.Id);
                    continue;
                }

                var profile = _profileRepo.GetProfile(listing.CarDefinitionId);
                if (profile == null)
                {
                    _logger.Warning("No car profile for listing {ListingId}", listing.Id);
                    continue;
                }

                Stock.Add(new UsedCarListingViewModel
                {
                    Listing = listing,
                    CarDefinition = carDef,
                    CarProfile = profile,
                    DealerName = DealerName,
                    CanAfford = _gameState.Player.Money >= listing.Price
                });
            }

            // Bays are worked out from the room and the number of cars, so a lot always fits where it stands
            var showroom = _dealerCatalog.GetShowroom(_dealer?.ShowroomId ?? string.Empty);
            var bays = LotLayout.Build(Stock.Count, showroom);
            LotRadius = LotLayout.CameraRadiusFor(bays, showroom);

            var placements = new List<LotCarPlacement>();
            for (var i = 0; i < Stock.Count && i < bays.Count; i++)
            {
                var car = Stock[i];
                placements.Add(new LotCarPlacement(
                    Path.Combine(AppSettings.Instance.CarsPath, car.CarDefinition.Id),
                    string.IsNullOrWhiteSpace(car.Listing.SkinId) ? null : car.Listing.SkinId,
                    bays[i].X,
                    bays[i].Z,
                    bays[i].Heading,
                    car.DisplayName));
            }

            LotCars = placements;
            OnPropertyChanged(nameof(LotRadius));
            OnPropertyChanged(nameof(LotCars));
            OnPropertyChanged(nameof(Stock));
            OnPropertyChanged(nameof(StockSummary));

            _logger.Information("{Dealer}: {Stock} cars in stock, {Parked} of them parked",
                DealerName, Stock.Count, placements.Count);
        }

        /// <summary>What is on the lot, and what is not out front</summary>
        public string StockSummary
        {
            get
            {
                if (Stock.Count == 0) return "Nothing on the lot today";
                return LotCars.Count < Stock.Count
                    ? $"{LotCars.Count} out front, {Stock.Count - LotCars.Count} more round the back"
                    : $"{Stock.Count} on the lot";
            }
        }

        private bool CanPurchaseCar(UsedCarListingViewModel? car) =>
            car != null && _gameState.Player.Money >= car.Listing.Price;

        private void OnPurchaseCar(UsedCarListingViewModel? car)
        {
            if (car == null) return;

            _purchaseFlow.Offer(_gameState, car, result =>
            {
                if (!result.Succeeded)
                {
                    if (result.Outcome == PurchaseOutcome.NoLongerAvailable) Reload();
                    return;
                }

                // The car is bought and it is in the garage; that is where the player wants to be, not stood on
                // a lot looking at the gap where it was. Unless they have gone meanwhile (the purchase takes a moment):
                // then they stay where they went.
                OnPropertyChanged(nameof(BankrollDisplay));
                if (!ReferenceEquals(_navigationService.CurrentScreen, this)) return;
                _navigationService.NavigateToGarage(_gameState, skipAnimation: true);
            });
        }

        /// <summary>
        /// Puts the lot back the way the market says it is. Used when a car turned out to be gone: back to
        /// the whole-lot view first, because the car the camera was on is no longer there.
        /// </summary>
        private void Reload()
        {
            SelectedIndex = -1;
            LoadStock();
            OnPropertyChanged(nameof(BankrollDisplay));
        }

        private void OnBack()
        {
            _navigationService.NavigateToDealerMap(_gameState);
        }

        public override void Enter()
        {
            base.Enter();
            _logger.Information("Entered {Dealer}", DealerName);

            ResolveShowroom();
            LoadStock();
        }

        public override void Exit()
        {
            base.Exit();
            _logger.Information("Left {Dealer}", DealerName);
        }
    }
}
