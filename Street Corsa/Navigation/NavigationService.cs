using System.Runtime.CompilerServices;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Text;
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

        // The career section each game's player looked at last, for as long as that game is in memory
        private readonly ConditionalWeakTable<GameState, StrongBox<Dialogs.Career.CareerSection>> _careerSections = new();
        private readonly Action<GameState?> _setCurrentGame;
        private readonly RaceSetupBuilder _raceSetup;
        // The flow given, or the content in play's: the main screen's core, a game's setting
        private readonly FlowBook? _givenFlow;
        private FlowBook _flow => _givenFlow ?? FlowBook.Installed;
        private readonly Dictionary<string, Func<GameState, FlowArgs, Func<IScreen>?>> _screens;

        /// <summary>
        /// The core alone in play again, called before the main screen is made with no game (a game left): set by the app
        /// </summary>
        public Action? BackToCore { get; set; }

        /// <summary>
        /// Reads a game's content (the setting, by id; null for the bare core), for New Game and Load Game; false when it
        /// could not. Set by the app
        /// </summary>
        public Func<string?, Task<bool>> PlayContent { get; set; } = _ => Task.FromResult(true);

        /// <summary>A game's start given up after its content was read: the core again. Set by the app</summary>
        public Action DropContent { get; set; } = () => { };

        /// <summary>A screen went on show (or failed to): the loader of a game's start comes down. Set by the app</summary>
        public Action ScreenShown { get; set; } = () => { };

        // The flow id each screen was built for, and the one on show when it was
        private readonly ConditionalWeakTable<IScreen, string> _ids = new();
        private readonly ConditionalWeakTable<IScreen, string> _cameFrom = new();

        // The screens whose enter script has played, and those a leave script is taking out of
        private readonly ConditionalWeakTable<IScreen, object> _entered = new();
        private readonly ConditionalWeakTable<IScreen, object> _departing = new();
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
        /// <param name="flow">How the screens hang together; the installed mods' when none is given</param>
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
            Action<GameState?> setCurrentGame,
            FlowBook? flow = null)
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
            _givenFlow = flow;
            _screens = ScreenFactories();
        }

        /// <summary>
        /// Low-level navigation method. Prefer using typed factory methods instead.
        /// </summary>
        public void NavigateTo(IScreen screen)
        {
            var entering = Show(screen);
            if (!entering.IsCompleted) _ = BackOutIfFailsAsync(entering, screen, null, screen.GetType().Name, report: false);
        }

        /// <summary>
        /// Leaves the screen on show and enters <paramref name="screen"/>: what its entering waits for comes back started
        /// (<see cref="IScreen.EnterAsync"/>). One that failed before its first await throws here, as an Enter that throws
        /// does, so both are backed out of the same way.
        /// </summary>
        private Task Show(IScreen screen)
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
            screen.Enter();

            var entering = screen.EnterAsync() ?? Task.CompletedTask;
            if (entering.IsFaulted) entering.GetAwaiter().GetResult();
            return entering;
        }

        /// <summary>
        /// Waits for what a screen's entering waits for. One that fails while the screen is still on show is left again for
        /// the screen the player came from, as <see cref="SafeNavigate"/> does an Enter that throws: logged, and with
        /// <paramref name="report"/> the player told. One the player has already left only goes to the log; one that was
        /// cancelled (the screen left meanwhile) not even there.
        /// </summary>
        private async Task BackOutIfFailsAsync(Task entering, IScreen screen, IScreen? previous, string screenName, bool report)
        {
            try
            {
                await entering;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not go to the {Screen} screen", screenName);
                if (!ReferenceEquals(CurrentScreen, screen)) return;

                try
                {
                    BackTo(previous, screen);
                    if (!report) return;
                    ShowNavigationError(screenName);
                    DropContent();
                }
                catch (Exception backEx)
                {
                    _logger.Error(backEx, "Could not back out of the {Screen} screen", screenName);
                }
            }
        }

        /// <summary>Awaits a task nobody else waits for (a mod's enter script), so whatever it throws is logged by <paramref name="failed"/></summary>
        private static async Task ObserveAsync(Task task, Action<Exception> failed)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                failed(ex);
            }
        }

        /// <summary>
        /// Builds a screen and goes to it. A screen whose constructor throws is never shown; one whose Enter
        /// throws, or whose <see cref="IScreen.EnterAsync"/> fails (now, or later while it is on show), is left again for
        /// the screen the player came from. Either way the error is logged and the player told, instead of the exception
        /// ending the game. False when the player did not get there; true for a screen still entering.
        /// </summary>
        /// <param name="id">The screen's id in the flow</param>
        internal bool SafeNavigate(string screenName, Func<IScreen> create, string? id = null)
        {
            IScreen screen;
            try
            {
                screen = create();
                if (id != null) _ids.AddOrUpdate(screen, id);
                if (CurrentId is { } from) _cameFrom.AddOrUpdate(screen, from);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the {Screen} screen", screenName);
                ShowNavigationError(screenName);
                DropContent();
                return false;
            }

            var previous = CurrentScreen;
            try
            {
                var entering = Show(screen);
                ScreenShown();
                if (!entering.IsCompleted) _ = BackOutIfFailsAsync(entering, screen, previous, screenName, report: true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not go to the {Screen} screen", screenName);
                BackTo(previous, screen);
                ShowNavigationError(screenName);
                DropContent();
                return false;
            }
        }

        /// <summary>
        /// Puts the screen the player came from back after a failed navigation, as far as it will go. It is
        /// resumed, never entered again: an Enter can launch AC (the race screen) or spend time and save (the
        /// newspaper, the diner, the mail order), and a failed navigation must do neither a second time.
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

                // The car's screen is still up and will not say so again: the radio goes back in the car here
                if (previous.InCar) Audio.Radio.RadioPlayer.Shared.SetInCar(true);
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
                var entering = card.EnterAsync() ?? Task.CompletedTask;
                if (entering.IsFaulted) entering.GetAwaiter().GetResult();

                // The main screen hosts a card that is up: one that fails later stays up, the player told
                if (!entering.IsCompleted)
                    _ = ObserveAsync(entering, ex =>
                    {
                        _logger.Error(ex, "Could not enter the {Card} card", cardName);
                        ShowNavigationError(cardName);
                    });
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
            // The screen's name in the player's language: menu.screen.<its id, spaces as underscores>, else its kind's
            var key = "menu.screen." + screenName.Replace(' ', '_').Replace("'", string.Empty);
            var kindKey = "menu.screen." + _flow.KindOf(screenName);
            var name = GameText.Installed.Has(key) ? GameText.Say(key)
                : GameText.Installed.Has(kindKey) ? GameText.Say(kindKey)
                : screenName;
            _dialogService.ShowDialog(new InformationDialogViewModel(
                _dialogService,
                GameText.Say("menu.open_failed", ("screen", name)),
                GameText.Say("menu.open_failed.title")));
        }

        // ============================================================
        // THE FLOW
        // Screens are gone to by their id in the flow (flow/*.json, FlowBook), each built here by its kind. A screen
        // leaves by one of its ways out, named, which the flow leads to a screen id; one that leads nowhere open goes
        // home. The screens never name each other.
        // ============================================================

        /// <summary>How the screens hang together: the mods' flow</summary>
        public FlowBook Flow => _flow;

        /// <summary>The flow id of a screen built here; null for one built some other way</summary>
        public string? IdOf(IScreen? screen) => screen != null && _ids.TryGetValue(screen, out var id) ? id : null;

        /// <summary>The flow id of the screen on show</summary>
        public string? CurrentId => IdOf(CurrentScreen);

        /// <summary>
        /// Goes to the screen of this id in the flow, built as its kind is; what the screen needs to be told (the shop,
        /// the car, the race) comes in <paramref name="args"/>. A screen that is not there, or whose system is off, is not
        /// gone to (logged, and the player told). An overlay kind (the racing hub, the career) opens over the screen on
        /// show, and its own ways out go through <paramref name="leaveVia"/> when given (the garage's: not while the night
        /// is being worked out). False when the player did not get there.
        /// </summary>
        public bool Go(string? id, GameState? gameState, FlowArgs? args = null, Action<Func<bool>>? leaveVia = null)
        {
            args ??= FlowArgs.None;

            // The main screen with no game is the core's: a game's setting goes when its game is left for it
            if (gameState == null && _flow.Screen(id) is { Kind: ScreenKinds.MainMenu })
            {
                BackToCore?.Invoke();
                id = _flow.Menu;
            }
            if (_flow.Screen(id) is not { } screen || !_flow.IsOpen(id))
            {
                _logger.Warning("There is no screen {Screen} open in the flow: not gone to", id);
                ShowNavigationError(id ?? "?");
                return false;
            }

            var name = id!;
            if (screen.Kind is not (ScreenKinds.MainMenu or ScreenKinds.SoundBrowser or ScreenKinds.CarCatalogEditor) && gameState == null)
            {
                _logger.Warning("The {Screen} screen needs a game in play: not gone to", name);
                ShowNavigationError(name);
                return false;
            }

            var game = gameState!;
            if (screen.Kind == ScreenKinds.RacingHub) return ShowRaceHub(name, game, leaveVia);
            if (screen.Kind == ScreenKinds.Career) return ShowCareer(name, game, leaveVia);

            if (!_screens.TryGetValue(screen.Kind, out var build) || build(game, args) is not { } create)
            {
                _logger.Warning("The {Screen} screen ({Kind}) was not told what to show: not gone to", name, screen.Kind);
                ShowNavigationError(name);
                return false;
            }

            return SafeNavigate(name, create, id);
        }

        /// <summary>
        /// How each kind of screen (but the overlays) is built, from the game in play and what it is told: no screen when it
        /// was not told what it needs (the car to see, the dealer, the shop, the race). The game is null for the kinds that
        /// need none (the main menu, the sound library, the car catalog editor).
        /// </summary>
        private Dictionary<string, Func<GameState, FlowArgs, Func<IScreen>?>> ScreenFactories() => new()
        {
            [ScreenKinds.MainMenu] = (_, args) => () => new Screens.MainMenu.MainMenuScreenViewModel(
                this, _dialogService, _catalogRepository, args.Card, args.Intro),
            [ScreenKinds.Garage] = (game, args) => () => new Screens.Garage.GarageScreenViewModel(
                this, _dialogService, game, _catalogRepository, _launcher, _carPartsService, _gameStateRepository,
                _timeService, _saleService,
                args.SkipAnimation, args.Arrival, args.EndDay, _raceCarDataService),
            [ScreenKinds.CarLot] = (game, _) => () => new Screens.CarLot.CarLotScreenViewModel(
                this, game, _catalogRepository, _gameStateRepository, _timeService, _carPartsService),
            [ScreenKinds.Diner] = (game, args) => () => new Screens.Diner.DinerScreenViewModel(
                this, _dialogService, game, _catalogRepository, _opponentChallengeService, _contentService,
                _talkService, _timeService, _gameStateRepository, _raceSetup, _carPartsService, args.Returning, args.DinerId),
            [ScreenKinds.Street] = (game, args) => () => new Screens.Cruise.CruiseScreenViewModel(
                this, _dialogService, game, _catalogRepository, _opponentChallengeService, _contentService,
                _talkService, _timeService, _gameStateRepository, _raceSetup, _carPartsService, args.DinerId),
            [ScreenKinds.GasStation] = (game, _) => () => new Screens.Station.GasStationScreenViewModel(
                this, _dialogService, game, _catalogRepository, _timeService, _gameStateRepository, _carPartsService),
            [ScreenKinds.PaintBooth] = (game, _) => () => new Screens.PaintBooth.PaintBoothScreenViewModel(
                this, _dialogService, game, _catalogRepository, _timeService, _gameStateRepository, _carPartsService),
            [ScreenKinds.Newspaper] = (game, args) => () => new Screens.Newspaper.NewspaperScreenViewModel(
                this, _dialogService, game, _catalogRepository, _profileRepository, _marketService, _carPartsService,
                _partsShopService, _timeService, _gameStateRepository, _saleService, args.SkipAnimation),
            // "You drive over to see it": a car from the paper's ads where its seller has it (a dealer's on the lot, a
            // private seller's at the curb), to walk round and buy
            [ScreenKinds.CarViewing] = (game, args) => args.Car is { } car
                ? () => new Screens.CarViewing.CarViewingScreenViewModel(
                    this, _dialogService, game, car, _dealerCatalog, _purchaseService, _carPartsService)
                : null,
            [ScreenKinds.DealerMap] = (game, _) => () => new Screens.DealerMap.DealerMapScreenViewModel(
                this, _dialogService, game, _dealerCatalog, _marketService, _timeService, _partShopService),
            // The same map with the part shops on it instead of the car lots
            [ScreenKinds.PartShopsMap] = (game, _) => () => new Screens.DealerMap.DealerMapScreenViewModel(
                this, _dialogService, game, _dealerCatalog, _marketService, _timeService, _partShopService,
                partShopsMap: true),
            // The same map with the diners on it: who is in tonight is counted off the catalog and the challenge service,
            // the car checked for the street as the hub checks it
            [ScreenKinds.DinerMap] = (game, args) => () => new Screens.DealerMap.DealerMapScreenViewModel(
                this, _dialogService, game, _dealerCatalog, _marketService, _timeService, _partShopService,
                dinerMap: true, catalogRepository: _catalogRepository, challengeService: _opponentChallengeService,
                partsService: _carPartsService, fromDinerId: args.DinerId),
            [ScreenKinds.DealerLot] = (game, args) => args.DealerId is { } dealerId
                ? () => new Screens.DealerLot.DealerLotScreenViewModel(
                    this, _dialogService, game, dealerId, _dealerCatalog, _marketService, _catalogRepository,
                    _profileRepository, _gameStateRepository, _purchaseService, _carPartsService)
                : null,
            [ScreenKinds.PartShop] = (game, args) => args.ShopId is { } shopId ? () => PartShop(game, shopId) : null,
            [ScreenKinds.MailOrder] = (game, _) => () => new Screens.UsedParts.UsedPartsScreenViewModel(
                this, _dialogService, game, _carPartsService, _partsShopService, _gameStateRepository,
                _catalogRepository, _timeService),
            [ScreenKinds.Race] = (game, args) => args.Race is { } intent
                ? () => new Screens.RaceLoading.RaceLoadingScreenViewModel(
                    this, _dialogService, game, _launcher, intent, _timeService, _gameStateRepository)
                : null,
            // Every sound file and engine sound, to be heard one by one. The start music ends here, so it does not play
            // under what is listened to.
            [ScreenKinds.SoundBrowser] = (_, _) => () =>
            {
                Audio.StartMusic.End();
                return new Screens.SoundBrowser.SoundBrowserScreenViewModel(this, _carPartsService);
            },
            [ScreenKinds.CarCatalogEditor] = (_, _) => () => new Screens.CarCatalogEditor.CarCatalogEditorScreenViewModel(
                this, _dialogService, _catalogRepository, _profileRepository, _profileService, _carPartsService),
        };

        /// <summary>A part shop's counter. A machine shop is a counter of its own: it works on the player's parts and sells none</summary>
        private IScreen PartShop(GameState game, string shopId) =>
            _partShopService.Catalog.Get(shopId) is { KindOf: Models.GameState.PartShopKind.Machine }
                ? new Screens.PartShop.MachineShopScreenViewModel(
                    this, _dialogService, game, shopId, _partShopService, _carPartsService, _gameStateRepository)
                : new Screens.PartShop.PartShopScreenViewModel(
                    this, _dialogService, game, shopId, _partShopService, _carPartsService, _partsShopService,
                    _gameStateRepository, _catalogRepository);

        /// <summary>
        /// Leaves a screen by one of its ways out (<c>"back"</c>, <c>"bought"</c>): to the screen the flow leads it to.
        /// A way out that leads to no screen open goes home instead (from anywhere but home), or with no game in play to
        /// the main menu. False when the player did not get anywhere: a screen that failed to open leaves them where they
        /// were, as any navigation does. True while a leave script plays: a way out that goes nowhere after it puts the
        /// car back itself (<see cref="IScriptedScreen.PutBackAfterLeave"/>); a screen with more to put back awaits
        /// <see cref="LeaveAsync"/> instead.
        /// </summary>
        public bool Leave(IScreen from, string exit, GameState? gameState, FlowArgs? args = null)
        {
            if (!IsOnShow(from, exit)) return false;
            var id = IdOf(from);
            return Depart(from, LeadsTo(id, exit, gameState), () => LeaveFrom(id, exit, gameState, args));
        }

        /// <summary>
        /// <see cref="Leave"/>, done once the player has gone or not: the leave script first, when a mod has one, then
        /// <paramref name="first"/> (the screen's own curtain), then the way out. False when the player did not get
        /// anywhere, the screen to put back as it was (the car included): nothing is put back here.
        /// </summary>
        public async Task<bool> LeaveAsync(IScreen from, string exit, GameState? gameState, FlowArgs? args = null, Func<Task>? first = null)
        {
            var id = IdOf(from);
            if (IsLeaving(from)) return true;
            if (!IsOnShow(from, exit)) return false;

            var playing = StartLeaveScript(from, LeadsTo(id, exit, gameState));
            if (playing != null) _departing.AddOrUpdate(from, new object());
            try
            {
                if (playing != null)
                {
                    try
                    {
                        await playing;
                    }
                    catch (Exception ex)
                    {
                        // The show is not the business: the player goes all the same
                        _logger.Error(ex, "A leave script failed");
                    }

                    // The player may have gone some other way meanwhile
                    if (!ReferenceEquals(CurrentScreen, from)) return true;
                }

                if (first != null) await first();
                return LeaveFrom(id, exit, gameState, args);
            }
            finally
            {
                if (playing != null) _departing.Remove(from);
            }
        }

        /// <summary>Where a screen's way out takes the player, as <see cref="LeaveFrom"/> goes: for the leave script's "to"</summary>
        private string? LeadsTo(string? id, string exit, GameState? gameState) =>
            _flow.Exit(id, exit) ?? (gameState != null && !IsHome(id) ? _flow.Home : gameState == null ? _flow.Menu : null);

        /// <summary>
        /// Leaves a screen for a screen of the flow it names itself (a hub's tile), as <see cref="Leave"/> does: its leave
        /// script first. An overlay opens over it, and the screen is not left.
        /// </summary>
        public bool GoFrom(IScreen from, string to, GameState? gameState, FlowArgs? args = null, Action<Func<bool>>? leaveVia = null) =>
            IsOnShow(from, to) && Depart(from, to, () => Go(to, gameState, args, leaveVia));

        /// <summary>
        /// The screen leaving is the one on show. One the player has already left (it went on after an await: time spent,
        /// a check) takes nobody anywhere from wherever they are now.
        /// </summary>
        private bool IsOnShow(IScreen from, string way)
        {
            if (ReferenceEquals(CurrentScreen, from)) return true;
            _logger.Warning("The {Screen} screen is not on show: not left by {Way}", IdOf(from) ?? from.GetType().Name, way);
            return false;
        }

        /// <summary>A leave script is taking the player off this screen: what they do meanwhile is not done</summary>
        public bool IsLeaving(IScreen screen) => _departing.TryGetValue(screen, out _);

        /// <summary>
        /// A screen with its car on a 3D scene plays its <c>screen.&lt;id&gt;.leave</c> script first, when a mod has one
        /// for the moment, and is left once the script lets it go; a way out that then goes nowhere puts the car back.
        /// True while that is under way.
        /// </summary>
        private bool Depart(IScreen from, string? to, Func<bool> go)
        {
            if (IsLeaving(from)) return true;
            if (StartLeaveScript(from, to) is not { } playing) return go();

            _departing.AddOrUpdate(from, new object());
            _ = DepartAfterAsync(playing, (IScriptedScreen)from, go);
            return true;
        }

        /// <summary>
        /// The screen's leave script for a way out to <paramref name="to"/>, started: done once it lets the screen go.
        /// None for a screen with no 3D scene or not on show, for an overlay (the screen stays), for a way out that leads
        /// to no screen open (nothing to drive off for), and when no script plays for the moment.
        /// </summary>
        private Task? StartLeaveScript(IScreen from, string? to)
        {
            if (from is not IScriptedScreen { PlayScreenScript: { } play } scripted || IdOf(from) is not { } id
                || !ReferenceEquals(CurrentScreen, from) || !_flow.IsOpen(to) || ScreenKinds.IsOverlay(_flow.KindOf(to)))
                return null;

            try
            {
                return play(Services.Scripting.ScriptTriggers.ScreenLeave(id), FactsFor(scripted, id, to));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The {Screen} screen's leave script could not be played", id);
                return null;
            }
        }

        private async Task DepartAfterAsync(Task playing, IScriptedScreen from, Func<bool> go)
        {
            try
            {
                await playing;
            }
            catch (Exception ex)
            {
                // The show is not the business: the player goes all the same
                _logger.Error(ex, "A leave script failed");
            }

            try
            {
                // The player may have gone some other way meanwhile
                if (!ReferenceEquals(CurrentScreen, from)) return;

                var went = false;
                try
                {
                    went = go();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not leave after the leave script");
                }

                if (!went && ReferenceEquals(CurrentScreen, from)) from.PutBackAfterLeave();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not put the car back after a leave that went nowhere");
            }
            finally
            {
                _departing.Remove(from);
            }
        }

        /// <summary>
        /// A screen with its car on a 3D scene is up, its own show over: its <c>screen.&lt;id&gt;.enter</c> script plays,
        /// when a mod has one for the moment (once for the screen)
        /// </summary>
        public void ScreenReady(IScriptedScreen screen)
        {
            if (screen.PlayScreenScript is not { } play || IdOf(screen) is not { } id || !ReferenceEquals(CurrentScreen, screen)) return;
            if (_entered.TryGetValue(screen, out _)) return;
            _entered.AddOrUpdate(screen, new object());

            try
            {
                if (play(Services.Scripting.ScriptTriggers.ScreenEnter(id), FactsFor(screen, id, null)) is { } playing)
                    _ = ObserveAsync(playing, ex => _logger.Error(ex, "The {Screen} screen's enter script failed", id));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The {Screen} screen's enter script could not be played", id);
            }
        }

        /// <summary>
        /// The enter script still due for <paramref name="screen"/> (<see cref="ScreenReady"/> would play it): its trigger
        /// and the moment, so the screen may ready its scene for it; null once it played, or for a screen not on show
        /// </summary>
        public (string Trigger, Services.Scripting.ScriptFacts Facts)? EnterScriptFor(IScriptedScreen screen)
        {
            if (IdOf(screen) is not { } id || !ReferenceEquals(CurrentScreen, screen) || _entered.TryGetValue(screen, out _)) return null;
            return (Services.Scripting.ScriptTriggers.ScreenEnter(id), FactsFor(screen, id, null));
        }

        /// <summary>The moment as the screen tells it, with the flow's: the screen, where the player came from, where to</summary>
        private Services.Scripting.ScriptFacts FactsFor(IScriptedScreen screen, string id, string? to) =>
            screen.ScriptFacts with { Screen = id, From = _cameFrom.TryGetValue(screen, out var from) ? from : null, To = to };

        /// <summary><see cref="Leave"/> from a screen known by its id (an overlay, which is not a screen on show)</summary>
        public bool LeaveFrom(string? from, string exit, GameState? gameState, FlowArgs? args = null, Action<Func<bool>>? leaveVia = null)
        {
            if (_flow.Exit(from, exit) is { } to)
            {
                _logger.Information("From {From} by {Exit} to {To}", from, exit, to);
                return Go(to, gameState, args, leaveVia);
            }

            if (gameState == null)
            {
                _logger.Information("From {From} by {Exit}: no screen open there, to the main menu", from, exit);
                return !string.Equals(from, _flow.Menu, StringComparison.OrdinalIgnoreCase) && GoToMenu(args?.Card ?? default);
            }

            if (IsHome(from))
            {
                _logger.Warning("From {From} by {Exit}: no screen open there", from, exit);
                return false;
            }

            _logger.Information("From {From} by {Exit}: no screen open there, going home", from, exit);
            return GoHome(gameState, args);
        }

        /// <summary>Whether a screen's way out leads to a screen that is open: its button is shown only then</summary>
        public bool CanLeave(IScreen from, string exit) => _flow.Exit(IdOf(from), exit) != null;

        /// <summary>
        /// The day can be ended from the screen on show with the window's own End Day button: the flow says so of it, and
        /// it has no button of its own (the garage's is in its calendar)
        /// </summary>
        public bool CanEndDayHere =>
            CurrentScreen is { InGame: true } and not Screens.Garage.GarageScreenViewModel
            && _flow.Screen(CurrentId) is { EndDay: true };

        /// <summary>Whether this screen id is home</summary>
        public bool IsHome(string? id) => string.Equals(id, _flow.Home, StringComparison.OrdinalIgnoreCase);

        /// <summary>Home: where a game starts, and where a way out that leads nowhere goes</summary>
        public bool GoHome(GameState gameState, FlowArgs? args = null) => Go(_flow.Home, gameState, args);

        /// <summary>
        /// Home, once what runs on the UI thread now is over: for a screen that sends the player home from its
        /// <c>Enter</c>, while the navigation that brought them there is still going on. Only while that screen is still
        /// on show then: a second ask, or one from a screen already left, goes nowhere.
        /// </summary>
        public void GoHomeLater(IScreen from, GameState gameState)
        {
            // Home sending the player home would build it again, and again
            if (IsHome(IdOf(from))) return;
            LaterOn(from, () =>
            {
                if (!IsHome(CurrentId)) GoHome(gameState);
            });
        }

        /// <summary>
        /// <see cref="ScreenReady"/>, once what runs on the UI thread now is over (the viewport has started the show it
        /// was telling of), while the screen is still on show
        /// </summary>
        public void ScreenReadyLater(IScriptedScreen screen) => LaterOn(screen, () => ScreenReady(screen));

        /// <summary>
        /// The day ended at home (<paramref name="endDay"/>), once the screen that asked is up and still on show: for home
        /// reached from a screen where the player said to end the day
        /// </summary>
        public void EndDayLater(IScreen home, Action endDay) => LaterOn(home, endDay);

        /// <summary>
        /// Runs <paramref name="run"/> on the UI thread once what runs there now is over (<see cref="Helpers.UiThread.Later"/>),
        /// only while <paramref name="asker"/> is the screen on show when it comes to it
        /// </summary>
        private void LaterOn(IScreen asker, Action run) => Helpers.UiThread.Later(() =>
        {
            if (ReferenceEquals(CurrentScreen, asker)) run();
        });

        /// <summary>Where the game opens: the main screen, under its title while the cars load behind it</summary>
        public bool NavigateToStart() => Go(_flow.Menu, null, new FlowArgs { Intro = true });

        /// <summary>The main menu, a game over or left; <paramref name="card"/> opens on arrival</summary>
        public bool GoToMenu(Screens.MainMenu.MainMenuCard card = Screens.MainMenu.MainMenuCard.None) =>
            Go(_flow.Menu, null, new FlowArgs { Card = card });

        /// <summary>
        /// A race, set up by the screen on show: after it the player comes back there (or where its <c>after_race</c>
        /// way out leads), unless the intent already says where. The screen's leave script plays first, as for any way
        /// out; not for a race set up in an overlay over it (the racing hub's invitations), which the show would play
        /// behind. The hub's test-and-tune plays it, as the garage's own button did.
        /// </summary>
        public bool GoRace(GameState gameState, DragRaceLaunchIntent intent, bool fromOverlay = false)
        {
            intent.ReturnTo ??= CurrentId;
            var race = _flow.FirstOfKind(ScreenKinds.Race);
            bool Go() => GoKind(ScreenKinds.Race, gameState, new FlowArgs { Race = intent });
            return fromOverlay || CurrentScreen is not { } from ? Go() : Depart(from, race, Go);
        }

        /// <summary>
        /// The first screen of a kind, for code that knows only what it wants (the race, the sound library); a flow with
        /// none tells the player so
        /// </summary>
        public bool GoKind(string kind, GameState? gameState, FlowArgs? args = null)
        {
            if (_flow.FirstOfKind(kind) is { } id) return Go(id, gameState, args);

            _logger.Warning("The flow has no {Kind} screen: not gone to", kind);
            ShowNavigationError(kind);
            return false;
        }

        // The main menu's cards. They open over the main screen rather than in place of it; back closes them.

        public Screens.Settings.SettingsScreenViewModel CreateSettingsCard(Action back) =>
            new(this, _dialogService, _gameSettingsService, back);

        public Screens.NewGame.NewGameScreenViewModel CreateNewGameCard(Action back) =>
            new(this, _dialogService, _gameStateRepository, _raceEventService, _setCurrentGame, back, StockFirstMarket, PlayContent, DropContent);

        /// <summary>A new game's first classifieds, made in the background; one that fails is made when the paper is opened</summary>
        private async void StockFirstMarket(GameState gameState)
        {
            try
            {
                await Services.Market.FirstMarket.EnsureAsync(gameState, _marketService, _partsShopService);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not make the new game's first market");
            }
        }

        public Screens.LoadGame.LoadGameScreenViewModel CreateLoadGameCard(Action back) =>
            new(this, _dialogService, _gameStateRepository, _carPartsService, _setCurrentGame, back, PlayContent, DropContent);

        /// <summary>
        /// The career, over the current screen, on the section last looked at in this game. One that fails to build is
        /// logged and the player told, as a screen is. False when it did not open.
        /// </summary>
        /// <param name="leaveVia">How its way out ("won", when picking a path wins the game) is taken</param>
        internal bool ShowCareer(string id, GameState gameState, Action<Func<bool>>? leaveVia)
        {
            try
            {
                var under = CurrentScreen;
                _dialogService.ShowDialog(new Dialogs.Career.CareerDialogViewModel(
                    _dialogService,
                    _victoryConditionService,
                    _milestoneService,
                    gameState,
                    _gameStateRepository,
                    _careerSections.GetOrCreateValue(gameState),
                    () => Via(leaveVia, () => LeaveOverlay(under, id, "won", gameState, () => LeaveFrom(id, "won", gameState) || GoToMenu()))));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the career");
                ShowNavigationError(id);
                return false;
            }
        }

        private static void Via(Action<Func<bool>>? leaveVia, Func<bool> leave)
        {
            if (leaveVia != null) leaveVia(leave);
            else leave();
        }

        /// <summary>
        /// An overlay's way out, closed already: the screen it was opened over is left, its leave script first, as it is
        /// by a way out of its own
        /// </summary>
        private bool LeaveOverlay(IScreen? under, string id, string exit, GameState gameState, Func<bool> go) =>
            under != null && ReferenceEquals(CurrentScreen, under) ? Depart(under, _flow.Exit(id, exit), go) : go();

        /// <summary>
        /// The racing hub, over the current screen (the garage's "Go race!"): the race invitations, Street News, a free run
        /// and a test-and-tune of the selected car, the way out onto the streets (its "streets" way out, shown while it
        /// leads somewhere: a setting's diners' map, say). A race set up from it comes back
        /// to the screen it was opened over. One that fails to build is logged and the player told, as a screen is.
        /// False when it did not open.
        /// </summary>
        private bool ShowRaceHub(string id, GameState gameState, Action<Func<bool>>? leaveVia)
        {
            try
            {
                // A race set up from it goes ahead only while the player is still where the hub was opened
                var home = CurrentScreen;
                var streets = _flow.Exit(id, "streets") != null;
                Action? hitTheStreets = streets
                    ? () => Via(leaveVia, () => LeaveOverlay(home, id, "streets", gameState, () => LeaveFrom(id, "streets", gameState)))
                    : null;
                _dialogService.ShowDialog(new Dialogs.RaceHub.RaceHubDialogViewModel(
                    _dialogService,
                    gameState,
                    _raceEventService,
                    _carFilterService,
                    _catalogRepository,
                    _eventOpponentService,
                    _contentService,
                    _raceSetup,
                    new Screens.Shared.PracticeRuns(_dialogService, _launcher, _carPartsService, _gameStateRepository, _timeService,
                        _gameSettingsService, _contentService, _raceCarDataService, _raceSetup),
                    stillHome: () => ReferenceEquals(CurrentScreen, home),
                    race: (intent, leave) => GoRace(gameState, intent, fromOverlay: !leave),
                    hitTheStreets));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the racing hub");
                ShowNavigationError(id);
                return false;
            }
        }

        /// <summary>
        /// A part to look at in 3D over the current screen (with whatever is bolted on it), its name, condition and
        /// <paramref name="priceLine"/>; Buy only with a <paramref name="buy"/>, which runs once the preview has
        /// closed. One that fails to build is logged and the player told. False when it did not open. With
        /// <paramref name="talk"/> the seller can be called (while the player's AI is on); <paramref name="talked"/> runs
        /// after the call, for the screen behind to show a price the call changed.
        /// </summary>
        public bool ShowPartPreview(PartInstance part, string title, string priceLine, Action? buy,
            Func<Services.Talk.Conversation>? talk = null, Action? talked = null)
        {
            try
            {
                _dialogService.ShowDialog(new Dialogs.PartPreview.PartPreviewDialogViewModel(
                    _dialogService, _carPartsService.Catalog, part, title, priceLine, buy, talk, talked));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not open the preview of {Part}", part.DefinitionId);
                ShowNavigationError("part_preview");
                return false;
            }
        }
    }

    /// <summary>
    /// What a screen gone to is told besides its id: what it shows (a shop, a dealer, a car, a race) and how it comes in
    /// </summary>
    public sealed record FlowArgs
    {
        public static readonly FlowArgs None = new();

        /// <summary>The screen comes up at once, without its opening show</summary>
        public bool SkipAnimation { get; init; }

        /// <summary>The garage: the car just taken out of the lot, its engine, and whether it drives in</summary>
        public Screens.Garage.CarArrival? Arrival { get; init; }

        /// <summary>The garage: the player ended the day elsewhere (and said yes to it) and came home for the night</summary>
        public bool EndDay { get; init; }

        /// <summary>Back from a race: a visit paid for on the way in costs no time again (the diner)</summary>
        public bool Returning { get; init; }

        /// <summary>The diner to show (<see cref="Models.Diners.Diner.Id"/>); none, or one that is gone: the first</summary>
        public string? DinerId { get; init; }

        /// <summary>The part shop to show</summary>
        public string? ShopId { get; init; }

        /// <summary>The dealer whose lot to show</summary>
        public string? DealerId { get; init; }

        /// <summary>The car from the paper to go and see</summary>
        public Screens.Shared.UsedCarListingViewModel? Car { get; init; }

        /// <summary>The race to run</summary>
        public DragRaceLaunchIntent? Race { get; init; }

        /// <summary>The main menu: a card to open on arrival</summary>
        public Screens.MainMenu.MainMenuCard Card { get; init; }

        /// <summary>The main menu: under its title, while the cars load behind it (the game opening)</summary>
        public bool Intro { get; init; }
    }
}
