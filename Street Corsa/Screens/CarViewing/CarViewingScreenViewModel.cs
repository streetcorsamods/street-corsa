using System.IO;
using StreetCorsa.Configuration;
using StreetCorsa.Controls;
using StreetCorsa.Dialogs;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.CarViewing
{
    /// <summary>
    /// "You drive over to see it": a car from the paper's ads, standing where its seller has it, a dealer's on the
    /// dealer's lot and a private seller's at the curb of their street, to walk round, start up, and buy or leave. Buying
    /// is the paper's and the lot's purchase (<see cref="PurchaseFlow"/>), with its question and its answers; bought, the
    /// player goes home to the car, gone meanwhile, back to the paper. Back goes back to the paper.
    /// </summary>
    public class CarViewingScreenViewModel : BaseScreenViewModel
    {
        // Where a dealer's car stands in the showroom: in the middle, a little off square, as a lot of one car stands
        private const float LotHeading = 20f;

        // The camera comes round to the car's front three-quarter: from its left side, this far towards its nose. At the
        // curb the left is the street's side, away from the curb, and the camera comes further round to the nose, which
        // the street's sun lights
        private const float CameraFromSide = 0.8f;
        private const float CurbCameraFromSide = 1.1f;

        // How far the camera may stand back: the street's backdrop is a photograph taken from the middle of the road,
        // and the further the camera goes from there the more it shows it; a showroom's walls are the limit there
        private const float StreetMaxRadius = 10f;
        private const float LotWallMargin = 1.5f;
        private const float LotMaxRadius = 12f;

        private readonly NavigationService _navigationService;
        private readonly GameState _gameState;
        private readonly UsedCarListing _listing;
        private readonly IDealerCatalog _dealerCatalog;
        private readonly IContentCatalogRepository _catalogRepo;
        private readonly ICarProfileRepository _profileRepo;
        private readonly ICarPartsService _partsService;
        private readonly PurchaseFlow _purchaseFlow;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarViewing");
        private readonly CarViewingSeller _seller;

        public CarViewingScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            UsedCarListing listing,
            IDealerCatalog dealerCatalog,
            IContentCatalogRepository catalogRepo,
            ICarProfileRepository profileRepo,
            ICarPurchaseService purchaseService,
            ICarPartsService partsService)
        {
            _navigationService = navigationService;
            _gameState = gameState;
            _listing = listing;
            _dealerCatalog = dealerCatalog;
            _catalogRepo = catalogRepo;
            _profileRepo = profileRepo;
            _partsService = partsService;
            _purchaseFlow = new PurchaseFlow(dialogService, purchaseService, _logger);

            // Known before the screen is shown: the place's air is asked for as it comes up
            _seller = CarViewingPlace.Of(gameState, listing, id => dealerCatalog.Get(id) != null);

            BuyCommand = new RelayCommand(OnBuy, () => Car != null);
            BackCommand = new RelayCommand(OnBack);
            AskSellerCommand = new AsyncRelayCommand(OnAskSellerAsync, () => CanAskSeller);
        }

        /// <summary>The air of where the car stands</summary>
        public override string? Ambience => _seller.Spot == CarViewingSpot.DealerLot ? "ambience.lot" : "ambience.street";

        public RelayCommand BuyCommand { get; }

        public RelayCommand BackCommand { get; }

        /// <summary>Ask the seller what they make of it: the player's AI speaking for them, when it is on</summary>
        public AsyncRelayCommand AskSellerCommand { get; }

        /// <summary>The car as the ads show it; null until the screen is entered</summary>
        public UsedCarListingViewModel? Car { get; private set; }

        /// <summary>The scene and where the car stands in it; null until the screen is entered, or when there is none</summary>
        public CarViewingStage? Stage { get; private set; }

        /// <summary>Where the player has come to: the dealer's name, or the street's and the seller's</summary>
        public string PlaceLine { get; private set; } = string.Empty;

        /// <summary>Who sells it, as the purchase question says it</summary>
        public string SellerLine => Car?.SellerLine ?? string.Empty;

        public bool CanAfford => Car != null && _gameState.Player.Money >= Car.Listing.Price;

        /// <summary>A word when the player is short of the asking price</summary>
        public string AffordLine => Car != null && !CanAfford ? GameText.Say("preview.car.short", ("price", new Helpers.Money(Car.Listing.Price))) : string.Empty;

        /// <summary>The ad's few words, as the paper printed them</summary>
        public string Description => _listing.Description ?? string.Empty;

        private DealerLocation? _dealer;
        private string _sellerWord = string.Empty;
        private bool _asking;

        /// <summary>What the seller said when asked; empty until then</summary>
        public string SellerWord
        {
            get => _sellerWord;
            private set
            {
                if (SetProperty(ref _sellerWord, value)) OnPropertyChanged(nameof(CanAskSeller));
            }
        }

        /// <summary>The seller can be asked once, while the player's AI is on</summary>
        public bool CanAskSeller => Car != null && !_asking && SellerWord.Length == 0 && Services.Ai.AiHub.Shared.Writer.IsAvailable;

        private async Task OnAskSellerAsync()
        {
            _asking = true;
            OnPropertyChanged(nameof(CanAskSeller));
            AskSellerCommand.RaiseCanExecuteChanged();
            try
            {
                var said = await Services.Ai.AiHub.Shared.Writer.DealerTakeAsync(_listing, _dealer);
                SellerWord = said is { Length: > 0 } ? said : GameText.Say("preview.car.seller_shrugs");
            }
            finally
            {
                _asking = false;
                OnPropertyChanged(nameof(CanAskSeller));
                AskSellerCommand.RaiseCanExecuteChanged();
            }
        }

        private Audio.EngineSpec? _engine;

        /// <summary>The engine as it is sold, to start and rev where it stands</summary>
        public Audio.EngineSpec? Engine
        {
            get => _engine;
            private set => SetProperty(ref _engine, value);
        }

        public override void Enter()
        {
            base.Enter();

            // A car the catalog no longer has cannot be shown or bought: the navigation fails, the player stays put
            var definition = _catalogRepo.GetCar(_listing.CarDefinitionId)
                ?? throw new InvalidOperationException($"The car {_listing.CarDefinitionId} of listing {_listing.Id} is not in the catalog");
            var profile = _profileRepo.GetProfile(_listing.CarDefinitionId) ?? new Models.Catalog.CarProfile();

            var dealer = _dealer = _seller.DealerId != null ? DealerRoster.Find(_gameState, _seller.DealerId) : null;
            Car = new UsedCarListingViewModel
            {
                Listing = _listing,
                CarDefinition = definition,
                CarProfile = profile,
                RivalAd = _seller.Rival?.Ad,
                PrivateSeller = _seller.Rival?.Seller.Name,
                DealerName = _seller.Rival != null
                    ? UsedCarListingViewModel.PrivateSellerName(_seller.Rival.Seller.Name)
                    : dealer?.Name ?? _dealerCatalog.Get(_seller.DealerId ?? string.Empty)?.Name ?? GameText.Say("preview.car.private_seller"),
                CanAfford = _gameState.Player.Money >= _listing.Price
            };

            Stage = BuildStage(definition.Id);
            _logger.Information("Gone to see {Car} ({Spot}), {Scene}", Car.DisplayName, _seller.Spot, Stage?.SceneKn5 ?? "no scene");

            OnPropertyChanged(nameof(Car));
            OnPropertyChanged(nameof(Stage));
            OnPropertyChanged(nameof(PlaceLine));
            OnPropertyChanged(nameof(SellerLine));
            OnPropertyChanged(nameof(CanAfford));
            OnPropertyChanged(nameof(AffordLine));
            OnPropertyChanged(nameof(CanAskSeller));
            RelayCommand.RaiseCanExecuteChanged();
            AskSellerCommand.RaiseCanExecuteChanged();

            _ = LoadEngineAsync(Car);
        }

        /// <summary>Where the car stands: its dealer's showroom, or the curb of a street in the light of the hour</summary>
        private CarViewingStage? BuildStage(string carId)
        {
            var carDirectory = Path.Combine(AppSettings.Instance.CarsPath, carId);
            var skin = string.IsNullOrWhiteSpace(_listing.SkinId) ? null : _listing.SkinId;

            if (_seller.Spot == CarViewingSpot.DealerLot && _dealerCatalog.Get(_seller.DealerId!) is { } dealer)
            {
                var showroom = string.IsNullOrWhiteSpace(dealer.ShowroomId) ? null : Services.Showcase.ShowcaseContent.FindScene(dealer.ShowroomId);
                if (showroom != null)
                {
                    PlaceLine = GameText.Say("preview.car.at_dealer", ("dealer", Car?.DealerName ?? dealer.Name));
                    var walls = _dealerCatalog.GetShowroom(dealer.ShowroomId ?? string.Empty).WallRadius;
                    return new CarViewingStage(showroom, null, null, carDirectory, skin, 0f, 0f, LotHeading, CameraFromSide,
                        Math.Clamp(walls - LotWallMargin, 5f, LotMaxRadius));
                }

                _logger.Warning("The showroom of {Dealer} is not installed: the car is seen at the curb", dealer.Name);
            }

            var light = StreetLights.At(_gameState.Date);
            if (CarViewingPlace.StreetFor(StreetScenes.Installed, light, _listing.Id) is not { } found)
            {
                _logger.Warning("No street is installed: the car is shown without a scene");
                PlaceLine = SellerLine;
                return new CarViewingStage(null, null, null, carDirectory, skin, 0f, 0f, LotHeading, CameraFromSide, StreetMaxRadius);
            }

            var (street, time) = found;
            PlaceLine = GameText.Say("preview.car.at_curb", ("street", street.Title), ("seller", Car?.DealerName ?? string.Empty));
            var spot = street.Player;
            return new CarViewingStage(street.Kn5For(time), street.LightingFor(time), street.Sun, carDirectory, skin,
                spot.X, spot.Z, spot.Heading, CurbCameraFromSide, StreetMaxRadius);
        }

        private async Task LoadEngineAsync(UsedCarListingViewModel car)
        {
            try
            {
                var spec = await EngineSpecs.ForSaleAsync(_partsService, car.Listing, car.DisplayName);
                if (ReferenceEquals(car, Car)) Engine = spec;
            }
            catch (Exception ex)
            {
                // No engine to start is all that comes of it: the car is still there to look at
                _logger.Error(ex, "Could not work out the engine of {Car}", car.DisplayName);
            }
        }

        private void OnBuy()
        {
            if (Car is not { } car) return;

            _purchaseFlow.Offer(_gameState, car, result =>
            {
                // The purchase takes a moment: a player who has gone meanwhile stays where they went
                if (!ReferenceEquals(_navigationService.CurrentScreen, this)) return;

                switch (CarViewingPlace.Next(result))
                {
                    case AfterPurchase.Garage:
                        _navigationService.NavigateToGarage(_gameState, skipAnimation: true);
                        break;
                    case AfterPurchase.Newspaper:
                        _navigationService.NavigateToNewspaper(_gameState, skipAnimation: true);
                        break;
                    default:
                        OnPropertyChanged(nameof(CanAfford));
                        OnPropertyChanged(nameof(AffordLine));
                        break;
                }
            });
        }

        private void OnBack()
        {
            _logger.Information("Back to the paper from {Car}", Car?.DisplayName);
            _navigationService.NavigateToNewspaper(_gameState, skipAnimation: true);
        }

        public override void Exit()
        {
            base.Exit();

            // The engine stops with the screen
            Engine = null;
        }
    }
}
