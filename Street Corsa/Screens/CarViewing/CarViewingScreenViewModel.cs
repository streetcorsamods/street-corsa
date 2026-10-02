using System.IO;
using StreetCorsa.Configuration;
using StreetCorsa.Controls;
using StreetCorsa.Dialogs;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
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
        private readonly UsedCarListingViewModel _car;
        private readonly IDealerCatalog _dealerCatalog;
        private readonly ICarPartsService _partsService;
        private readonly PurchaseFlow _purchaseFlow;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarViewing");
        private readonly CarViewingSeller _seller;

        public CarViewingScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            UsedCarListingViewModel car,
            IDealerCatalog dealerCatalog,
            ICarPurchaseService purchaseService,
            ICarPartsService partsService)
        {
            _navigationService = navigationService;
            _gameState = gameState;
            _car = car;
            _listing = car.Listing;
            _dealerCatalog = dealerCatalog;
            _partsService = partsService;
            _purchaseFlow = new PurchaseFlow(dialogService, purchaseService, _logger);

            // Known before the screen is shown: the place's air is asked for as it comes up
            _seller = CarViewingPlace.Of(car, id => dealerCatalog.Get(id) != null);

            BuyCommand = new RelayCommand(OnBuy, () => Car != null);
            BackCommand = new RelayCommand(OnBack);
            GarageCommand = new RelayCommand(OnGarage);
            TalkCommand = new RelayCommand(StartTalk, () => CanTalk);
        }

        /// <summary>The air of where the car stands</summary>
        public override string? Ambience => _seller.Spot == CarViewingSpot.DealerLot ? "ambience.lot" : "ambience.street";

        public RelayCommand BuyCommand { get; }

        public RelayCommand BackCommand { get; }

        public RelayCommand GarageCommand { get; }

        /// <summary>Talk to the seller: the player's AI speaking for them, when it is on</summary>
        public RelayCommand TalkCommand { get; }

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

        private TalkViewModel? _talk;

        /// <summary>The talk with the seller, once begun: a dealer's salesman begins it himself as the player arrives</summary>
        public TalkViewModel? Talk
        {
            get => _talk;
            private set
            {
                if (!SetProperty(ref _talk, value)) return;
                OnPropertyChanged(nameof(HasTalk));
                OnPropertyChanged(nameof(CanTalk));
            }
        }

        public bool HasTalk => Talk != null;

        /// <summary>The seller can be talked to while the player's AI is on</summary>
        public bool CanTalk => Car != null && Talk == null && Services.Talk.TalkDesk.Shared.IsAvailable;

        /// <summary>"Talk to the salesman" on a lot, "Talk to the seller" at the curb</summary>
        public string TalkLabel => GameText.Say(_seller.Spot == CarViewingSpot.DealerLot ? "chat.ui.talk_salesman" : "chat.ui.talk_seller");

        private void StartTalk()
        {
            if (Talk != null || Car == null || !Services.Talk.TalkDesk.Shared.IsAvailable) return;

            var talk = Services.Talk.TalkDesk.Shared.ForCar(_gameState, _car, _seller.DealerId, Services.Talk.TalkPlace.Curb);
            var portrait = talk.Rival is { } rival ? RacersOut.PortraitOf(rival, _logger) : null;
            var view = new TalkViewModel(talk, portrait);
            view.PriceChanged += OnPriceChanged;
            Talk = view;
            RelayCommand.RaiseCanExecuteChanged();
            _ = view.OpenAsync();
        }

        /// <summary>The seller came down: the card, the money line and Buy go by the new price</summary>
        private void OnPriceChanged()
        {
            Car = null;
            OnPropertyChanged(nameof(Car));
            _car.CanAfford = _gameState.Player.Money >= _listing.Price;
            Car = _car;
            OnPropertyChanged(nameof(Car));
            OnPropertyChanged(nameof(CanAfford));
            OnPropertyChanged(nameof(AffordLine));
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

            // The car as the paper's ad has it, seller and all
            _car.CanAfford = _gameState.Player.Money >= _listing.Price;
            Car = _car;

            Stage = BuildStage(_car.CarDefinition.Id);
            _logger.Information("Gone to see {Car} ({Spot}), {Scene}", Car.DisplayName, _seller.Spot, Stage?.SceneKn5 ?? "no scene");

            OnPropertyChanged(nameof(Car));
            OnPropertyChanged(nameof(Stage));
            OnPropertyChanged(nameof(PlaceLine));
            OnPropertyChanged(nameof(SellerLine));
            OnPropertyChanged(nameof(CanAfford));
            OnPropertyChanged(nameof(AffordLine));
            OnPropertyChanged(nameof(CanTalk));
            OnPropertyChanged(nameof(TalkLabel));
            RelayCommand.RaiseCanExecuteChanged();

            // A salesman does not wait to be asked: he comes over as the player walks onto the lot
            if (_seller.Spot == CarViewingSpot.DealerLot) StartTalk();

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

        private void OnGarage()
        {
            _logger.Information("Home to the garage from {Car}", Car?.DisplayName);
            _navigationService.NavigateToGarage(_gameState);
        }

        public override void Exit()
        {
            base.Exit();

            // The engine stops with the screen, and the talk is over
            Engine = null;
            Talk?.End();
        }
    }
}
