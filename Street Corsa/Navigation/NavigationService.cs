using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Time;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Navigation
{
    /// <summary>
    /// Central navigation service with typed factory methods for all screens.
    /// Screens should use these factory methods rather than directly instantiating other screens.
    ///
    /// Every service a screen needs comes in through here, so a screen's dependencies are all in its
    /// constructor. Every factory goes through <see cref="SafeNavigate"/>: a screen that fails to build or to
    /// enter is logged and reported, and the player stays where they were. The main menu's cards, which open over
    /// it rather than in place of it, go through <see cref="SafeOpenCard{T}"/> the same way.
    /// </summary>
    public class NavigationService : ObservableObject
    {
        private IScreen? _currentScreen;
        private readonly DialogService _dialogService;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly ICarProfileRepository _profileRepository;
        private readonly IAssettoCorsaLauncher _launcher;
        private readonly IAssettoCorsaContentService _contentService;
        private readonly IOpponentChallengeService _opponentChallengeService;
        private readonly IGameStateRepository _gameStateRepository;
        private readonly IUsedCarMarketService _marketService;
        private readonly GameSettingsService _gameSettingsService;
        private readonly ICarProfileService _profileService;
        private readonly ITalkService _talkService;
        private readonly IDealerCatalog _dealerCatalog;
        private readonly ICarPurchaseService _purchaseService;
        private readonly ICarSaleService _saleService;
        private readonly IGameTimeService _timeService;
        private readonly ICarPartsService _carPartsService;
        private readonly INewspaperPartsService _partsShopService;
        private readonly Services.PartShops.IPartShopService _partShopService;
        private readonly RaceCarDataService _raceCarDataService;
        private readonly IRaceEventService _raceEventService;
        private readonly ICarFilterService _carFilterService;
        private readonly IEventOpponentService _eventOpponentService;
        private readonly IVictoryConditionService _victoryConditionService;
        private readonly IMilestoneService _milestoneService;
        private readonly Action<GameState?> _setCurrentGame;
        private readonly RaceSetupBuilder _raceSetup;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Navigation);

        /// <summary>
        /// One screen is about to take another's place (not the first screen of all): the window takes a still of the
        /// one leaving, to fade it out over the new one
        /// </summary>
        public event Action? ScreenChanging;

        /// <summary>The screen on show; null until the first navigation</summary>
        public IScreen? CurrentScreen
        {
            get => _currentScreen;
            private set
            {
                if (ReferenceEquals(_currentScreen, value)) return;
                if (_currentScreen != null) ScreenChanging?.Invoke();
                if (!SetProperty(ref _currentScreen, value)) return;

                // The place's air follows the screen on show, a screen gone back to after a failed navigation too
                Audio.AudioHub.Shared.SetAmbience(value?.Ambience);
                // Out of the car as the screen goes; into it once the car's screen is up (it says so itself)
                if (value?.InCar != true) Audio.Radio.RadioPlayer.Shared.SetInCar(false);
            }
        }

        /// <param name="setCurrentGame">Makes a loaded or new game the one the app saves on exit and races with</param>
        public NavigationService(
            DialogService dialogService,
            IContentCatalogRepository catalogRepository,
            ICarProfileRepository profileRepository,
            IAssettoCorsaLauncher launcher,
            IAssettoCorsaContentService contentService,
            IOpponentChallengeService opponentChallengeService,
            IGameStateRepository gameStateRepository,
            IUsedCarMarketService marketService,
            GameSettingsService gameSettingsService,
            ICarProfileService profileService,
            ITalkService talkService,
            IDealerCatalog dealerCatalog,
            ICarPurchaseService purchaseService,
            ICarSaleService saleService,
            IGameTimeService timeService,
            ICarPartsService carPartsService,
            INewspaperPartsService partsShopService,
            Services.PartShops.IPartShopService partShopService,
            RaceCarDataService raceCarDataService,
            IRaceEventService raceEventService,
            ICarFilterService carFilterService,
            IEventOpponentService eventOpponentService,
            IVictoryConditionService victoryConditionService,
            IMilestoneService milestoneService,
            Action<GameState?> setCurrentGame)
        {
            _dialogService = dialogService;
            _catalogRepository = catalogRepository;
            _profileRepository = profileRepository;
            _launcher = launcher;
            _contentService = contentService;
            _opponentChallengeService = opponentChallengeService;
            _gameStateRepository = gameStateRepository;
            _marketService = marketService;
            _gameSettingsService = gameSettingsService;
            _profileService = profileService;
            _talkService = talkService;
            _dealerCatalog = dealerCatalog;
            _purchaseService = purchaseService;
            _saleService = saleService;
            _timeService = timeService;
            _carPartsService = carPartsService;
            _partsShopService = partsShopService;
            _partShopService = partShopService;
            _raceCarDataService = raceCarDataService;
            _raceEventService = raceEventService;
            _carFilterService = carFilterService;
            _eventOpponentService = eventOpponentService;
            _victoryConditionService = victoryConditionService;
            _milestoneService = milestoneService;
            _setCurrentGame = setCurrentGame;
            _raceSetup = new RaceSetupBuilder(carPartsService, raceCarDataService);
        }

        /// <summary>
        /// Low-level navigation method. Prefer using typed factory methods instead.
        /// </summary>
        public void NavigateTo(IScreen screen)
        {
            // A screen that fails to leave does not keep the player on it: it is left as far as it went, and the new
            // one comes. Stopping here would leave the old screen half-exited and the new one built but never shown.
            if (CurrentScreen is { } leaving)
            {
                try
                {
                    leaving.Exit();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "The {Screen} screen could not be left cleanly; going on", leaving.GetType().Name);
                }
            }

            CurrentScreen = screen;
            CurrentScreen?.Enter();
        }

        /// <summary>
        /// Builds a screen and goes to it. A screen whose constructor throws is never shown; one whose Enter
        /// throws is left again for the screen the player came from. Either way the error is logged and the
        /// player told, instead of the exception ending the game. False when the player did not get there.
        /// </summary>
        internal bool SafeNavigate(string screenName, Func<IScreen> create)
        {
            IScreen screen;
            try
            {
                screen = create();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the {Screen} screen", screenName);
                ShowNavigationError(screenName);
                return false;
            }

            var previous = CurrentScreen;
            try
            {
                NavigateTo(screen);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not go to the {Screen} screen", screenName);
                BackTo(previous, screen);
                ShowNavigationError(screenName);
                return false;
            }
        }

        /// <summary>
        /// Puts the screen the player came from back after a failed navigation, as far as it will go. It is
        /// resumed, never entered again: an Enter can launch AC (the race screen) or spend time and save (the
        /// newspaper, the diner, the parts pages), and a failed navigation must do neither a second time.
        /// </summary>
        private void BackTo(IScreen? previous, IScreen failed)
        {
            if (previous == null || ReferenceEquals(CurrentScreen, previous)) return;

            try
            {
                if (ReferenceEquals(CurrentScreen, failed)) failed.Exit();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "The screen that failed could not be left cleanly");
            }

            try
            {
                CurrentScreen = previous;
                previous.Resume();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not go back to the screen before");
            }
        }

        /// <summary>
        /// Builds a card that opens over the current screen and enters it, the way <see cref="SafeNavigate"/> does a
        /// screen: one whose constructor throws is never shown, one whose Enter throws is left again. Either way the
        /// error is logged and the player told. Null when the card did not open.
        /// </summary>
        internal T? SafeOpenCard<T>(string cardName, Func<T> create) where T : class, IScreen
        {
            T card;
            try
            {
                card = create();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the {Card} card", cardName);
                ShowNavigationError(cardName);
                return null;
            }

            try
            {
                card.Enter();
                return card;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not enter the {Card} card", cardName);
                try
                {
                    card.Exit();
                }
                catch (Exception exitEx)
                {
                    _logger.Warning(exitEx, "The card that failed could not be left cleanly");
                }

                ShowNavigationError(cardName);
                return null;
            }
        }

        private void ShowNavigationError(string screenName)
        {
            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService,
                $"The {screenName} could not be opened. The details are in the log.",
                "Something Went Wrong"));
        }

        // ============================================================
        // TYPED FACTORY METHODS
        // Each method encapsulates the creation of a specific screen
        // ============================================================

        /// <summary>Where the game opens: the main screen, under its title while the cars load behind it</summary>
        public bool NavigateToStart() => SafeNavigate("main menu", () =>
            new Screens.MainMenu.MainMenuScreenViewModel(this, _dialogService, _catalogRepository, showIntro: true));

        /// <param name="card">A card to open on arrival: the car catalog editor goes back to the settings it came from</param>
        public bool NavigateToMainMenu(Screens.MainMenu.MainMenuCard card = Screens.MainMenu.MainMenuCard.None) =>
            SafeNavigate("main menu", () => new Screens.MainMenu.MainMenuScreenViewModel(this, _dialogService, _catalogRepository, card));

        // The main menu's cards. They open over the main screen rather than in place of it; back closes them.

        public Screens.Settings.SettingsScreenViewModel CreateSettingsCard(Action back) =>
            new(this, _dialogService, _gameSettingsService, back);

        public Screens.NewGame.NewGameScreenViewModel CreateNewGameCard(Action back) =>
            new(this, _dialogService, _gameStateRepository, _raceEventService, _setCurrentGame, back);

        public Screens.LoadGame.LoadGameScreenViewModel CreateLoadGameCard(Action back) =>
            new(this, _dialogService, _gameStateRepository, _carPartsService, _setCurrentGame, back);

        /// <summary>
        /// The garage, once what runs on the UI thread now is over: for a screen that sends the player home from its
        /// <c>Enter</c>, while the navigation that brought them there is still going on
        /// </summary>
        public void NavigateToGarageLater(GameState gameState) =>
            _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(() => NavigateToGarage(gameState));

        public bool NavigateToGarage(GameState gameState, bool skipAnimation = false) => SafeNavigate("garage", () =>
            new Screens.Garage.GarageScreenViewModel(
                this,
                _dialogService,
                gameState,
                _catalogRepository,
                _launcher,
                _carPartsService,
                _gameStateRepository,
                _timeService,
                _gameSettingsService,
                _contentService,
                _raceCarDataService,
                _saleService,
                skipAnimation));

        public bool NavigateToCarLot(GameState gameState) => SafeNavigate("car lot", () =>
            new Screens.CarLot.CarLotScreenViewModel(
                this,
                gameState,
                _catalogRepository,
                _gameStateRepository,
                _timeService));

        /// <param name="returning">Back from a race: the visit was paid for on the way in, so no time is spent</param>
        public bool NavigateToDiner(GameState gameState, bool returning = false) => SafeNavigate("diner", () =>
            new Screens.Diner.DinerScreenViewModel(
                this,
                _dialogService,
                gameState,
                _catalogRepository,
                _opponentChallengeService,
                _contentService,
                _talkService,
                _timeService,
                _gameStateRepository,
                _raceSetup,
                returning));

        public bool NavigateToCruise(GameState gameState) => SafeNavigate("street", () =>
            new Screens.Cruise.CruiseScreenViewModel(
                this,
                _dialogService,
                gameState,
                _catalogRepository,
                _opponentChallengeService,
                _contentService,
                _talkService,
                _timeService,
                _gameStateRepository,
                _raceSetup,
                _carPartsService));

        public bool NavigateToGasStation(GameState gameState) => SafeNavigate("gas station", () =>
            new Screens.Station.GasStationScreenViewModel(
                this,
                _dialogService,
                gameState,
                _catalogRepository,
                _timeService,
                _gameStateRepository,
                _carPartsService));

        public bool NavigateToNewspaper(GameState gameState, bool skipAnimation = false) => SafeNavigate("newspaper", () =>
            new Screens.Newspaper.NewspaperScreenViewModel(
                this,
                _dialogService,
                gameState,
                _raceEventService,
                _carFilterService,
                _catalogRepository,
                _eventOpponentService,
                _contentService,
                _timeService,
                _gameStateRepository,
                _raceSetup,
                _saleService,
                skipAnimation));

        public bool NavigateToUsedCarMarket(GameState gameState) => SafeNavigate("used car ads", () =>
            new Screens.UsedCarMarket.UsedCarMarketScreenViewModel(
                this,
                _dialogService,
                gameState,
                _marketService,
                _catalogRepository,
                _profileRepository,
                _gameStateRepository,
                _purchaseService));

        public bool NavigateToUsedParts(GameState gameState) => SafeNavigate("parts pages", () =>
            new Screens.UsedParts.UsedPartsScreenViewModel(
                this,
                _dialogService,
                gameState,
                _carPartsService,
                _partsShopService,
                _gameStateRepository,
                _catalogRepository,
                _timeService));

        /// <summary>Every sound file and engine sound, to be heard one by one; back goes to the settings</summary>
        public bool NavigateToSoundBrowser() => SafeNavigate("sound library", () =>
            new Screens.SoundBrowser.SoundBrowserScreenViewModel(this, _carPartsService));

        public bool NavigateToCarCatalogEditor() => SafeNavigate("car catalog editor", () =>
            new Screens.CarCatalogEditor.CarCatalogEditorScreenViewModel(
                this,
                _dialogService,
                _catalogRepository,
                _profileRepository,
                _profileService,
                _carPartsService));

        public bool NavigateToRaceLoading(GameState gameState, DragRaceLaunchIntent launchIntent) => SafeNavigate("race", () =>
            new Screens.RaceLoading.RaceLoadingScreenViewModel(
                this,
                _dialogService,
                gameState,
                _launcher,
                launchIntent,
                _timeService,
                _gameStateRepository));

        public bool NavigateToDealerMap(GameState gameState) => SafeNavigate("dealer map", () =>
            new Screens.DealerMap.DealerMapScreenViewModel(
                this,
                _dialogService,
                gameState,
                _dealerCatalog,
                _marketService,
                _timeService,
                _partShopService));

        /// <summary>The same map with the part shops on it instead of the car lots</summary>
        public bool NavigateToPartShopMap(GameState gameState) => SafeNavigate("part shops map", () =>
            new Screens.DealerMap.DealerMapScreenViewModel(
                this,
                _dialogService,
                gameState,
                _dealerCatalog,
                _marketService,
                _timeService,
                _partShopService,
                partShopsMap: true));

        public bool NavigateToPartShop(GameState gameState, string shopId) => SafeNavigate("part shop", () =>
            new Screens.PartShop.PartShopScreenViewModel(
                this,
                _dialogService,
                gameState,
                shopId,
                _partShopService,
                _carPartsService,
                _partsShopService,
                _gameStateRepository,
                _catalogRepository));

        public bool NavigateToDealerLot(GameState gameState, string dealerId) => SafeNavigate("dealer's lot", () =>
            new Screens.DealerLot.DealerLotScreenViewModel(
                this,
                _dialogService,
                gameState,
                dealerId,
                _dealerCatalog,
                _marketService,
                _catalogRepository,
                _profileRepository,
                _gameStateRepository,
                _purchaseService,
                _carPartsService));

        public bool NavigateToCareer(GameState gameState) => SafeNavigate("career screen", () =>
            new Screens.Career.CareerScreenViewModel(
                this,
                _dialogService,
                _victoryConditionService,
                _milestoneService,
                gameState,
                _gameStateRepository));
    }
}
