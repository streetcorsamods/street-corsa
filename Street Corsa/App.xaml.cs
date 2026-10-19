using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using StreetCorsa.Configuration;
using StreetCorsa.Dialogs;
using StreetCorsa.Logging;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Text;
using StreetCorsa.Services;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Scheduler;
using StreetCorsa.Services.Scheduler.Tasks;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Time;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Career;

namespace StreetCorsa
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public IAssettoCorsaContentService ContentService { get; private set; }
        public IAssettoCorsaLauncher Launcher { get; private set; }
        public NavigationService NavigationService { get; private set; }
        public DialogService DialogService { get; private set; }
        public IGameStateRepository GameStateRepository { get; private set; }
        public IContentCatalogRepository CatalogRepository { get; private set; }
        public ICarImportService CarImportService { get; private set; }
        public ICarProfileRepository ProfileRepository { get; private set; }
        public ICarProfileService ProfileService { get; private set; }
        public IUsedCarMarketService MarketService { get; private set; }
        public Services.Dealers.IDealerCatalog DealerCatalog { get; private set; }
        public ICarPurchaseService PurchaseService { get; private set; }
        public ICarSaleService SaleService { get; private set; }
        public Services.Parts.ICarPartsService CarPartsService { get; private set; }
        public Services.Parts.INewspaperPartsService NewspaperPartsService { get; private set; }
        public Services.PartShops.IPartShopService PartShopService { get; private set; }
        public IIniModificationService IniModificationService { get; private set; }
        public Services.Race.IRaceResultIngestionService RaceResultIngestionService { get; private set; }
        public CarDataOverlay CarDataOverlay { get; private set; }
        public Services.Race.RaceCarDataService RaceCarDataService { get; private set; }
        public IOpponentRepository OpponentRepository { get; private set; }
        public IOpponentInitializationService OpponentInitializationService { get; private set; }
        public IOpponentChallengeService OpponentChallengeService { get; private set; }
        public IGameTimeScheduler Scheduler { get; private set; }
        public IGameTimeService GameTimeService { get; private set; }
        public GameSettingsService GameSettingsService { get; private set; }

        // Career services
        public ICarFilterService CarFilterService { get; private set; }
        public IMilestoneService MilestoneService { get; private set; }
        public IVictoryConditionService VictoryConditionService { get; private set; }
        public IRaceEventService RaceEventService { get; private set; }
        public IEventOpponentService EventOpponentService { get; private set; }
        public ICareerProgressService CareerProgressService { get; private set; }
        public ITalkService TalkService { get; private set; }

        private Models.GameState.GameState? _currentGameState;

        /// <summary>
        /// Current game state (set when a game is loaded or created). A newly set game gets the results of a race it
        /// ran but never heard back from (the app closed during it): only now is there a save to apply them to.
        /// </summary>
        public Models.GameState.GameState? CurrentGameState
        {
            get => _currentGameState;
            set
            {
                var loaded = value != null && !ReferenceEquals(value, _currentGameState);
                _currentGameState = value;

                // No game in play: the mods' rules
                if (value == null) StreetCorsa.Rules.RuleBook.PlayBy(null);
                if (!loaded) return;

                // Already so when the game came through the load or new game screen; whatever else sets one plays by its rules too
                StreetCorsa.Rules.RuleBook.PlayBy(value);

                // The ads of the game put away wait for the AI no longer
                Services.Market.AdBlurbs.Forget();

                _ = ProcessOrphanedResultsAsync();

                // The music the game started to fades, and the radio comes back as it was left, in the game rather
                // than at the main menu
                Audio.StartMusic.End();
                Audio.Radio.RadioPlayer.Shared.GameLoaded();
            }
        }

        // The crash path runs once, whichever handler gets there first
        private int _fatalPathRan;

        /// <summary>How long the crash and exit paths wait for the UI thread or the install's gate before going without</summary>
        private static readonly TimeSpan FatalPathWait = TimeSpan.FromSeconds(5);

        // A wait for AC to exit (to put the install back and settle a pending race) is under way
        private int _waitingForAcExit;

        /// <summary>Held for the life of the process: a second copy of the game would restore the install under the first</summary>
        private const string SingleInstanceName = @"Local\StreetCorsa.SingleInstance";

        // Kept referenced so it is not collected (and released) while the app runs; the OS lets go of it at the end
        private static Mutex? _singleInstance;

        // Recoverable UI exceptions lately: a handler failing over and over (a render callback) is not recoverable
        private readonly Queue<DateTime> _recentUiErrors = new();
        private bool _closeAfterRace;

        /// <summary>
        /// Spends game time for an action. Handles day transitions and scheduled tasks.
        /// </summary>
        public async Task<TimeSpendResult> SpendTimeAsync(GameAction action)
        {
            if (CurrentGameState == null)
                return new TimeSpendResult { NewDayStarted = false, DaysPassed = 0 };

            return await GameTimeService.SpendTimeAsync(CurrentGameState, action);
        }

        /// <summary>
        /// Spends a custom amount of game time in minutes.
        /// </summary>
        public async Task<TimeSpendResult> SpendTimeAsync(int minutes)
        {
            if (CurrentGameState == null)
                return new TimeSpendResult { NewDayStarted = false, DaysPassed = 0 };

            return await GameTimeService.SpendTimeAsync(CurrentGameState, minutes);
        }

        /// <summary>
        /// Ends the current game day and advances to next morning.
        /// </summary>
        public async Task<TimeSpendResult> EndDayAsync()
        {
            if (CurrentGameState == null)
                return new TimeSpendResult { NewDayStarted = false, DaysPassed = 0 };

            return await GameTimeService.EndDayAsync(CurrentGameState);
        }

        public App()
        {
            // The installer's clean-up (AcUninstall) runs with no window, and not beside the game, which puts the install
            // back itself when it closes
            var uninstall = Services.AcUninstall.Asked(Environment.GetCommandLineArgs());

            // One copy of the game at a time, before anything is touched: a second one would put the install back at
            // start-up in the middle of the first one's race, and could not open the save the first one holds
            _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var firstInstance);
            if (!firstInstance && uninstall) Environment.Exit(0);
            if (!firstInstance)
            {
                System.Windows.MessageBox.Show("Street Corsa is already running.", "Street Corsa",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Environment.Exit(0);
            }

            // The program runs in US English whatever the machine's culture: every file it reads and writes (race.ini,
            // the cars' data, the saves) is written the one way. What the player reads is the game's (below).
            var englishCulture = new CultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentCulture = englishCulture;
            CultureInfo.DefaultThreadCurrentUICulture = englishCulture;
            Thread.CurrentThread.CurrentCulture = englishCulture;
            Thread.CurrentThread.CurrentUICulture = englishCulture;

            if (uninstall)
            {
                // The exit code is what the installer reads: AcUninstall.ExitAcRunning has it ask the player to close AC and retry
                Environment.Exit(Services.AcUninstall.Run());
            }

            // The last line of defence before anything that can fail: the rename and the log's folder below touch
            // %AppData%, and one that throws must still end with a message rather than a silent exit
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            // What the game wrote under its old name goes to the new folders first: the log opens its file there
            var renamed = Services.Storage.LegacyRename.Run();

            // Initialize logging FIRST. A log folder that cannot be made (an unwritable %AppData%) leaves the game
            // without a log, not without a start: the loggers write nowhere.
            Exception? logFailure = null;
            try
            {
                AppLoggerFactory.Initialize();
            }
            catch (Exception ex)
            {
                logFailure = ex;
                AppLoggerFactory.InitializeSilent();
            }

            var startLogger = AppLoggerFactory.CreateLogger(LogCategory.Startup);
            if (logFailure != null) startLogger.Error(logFailure, "The log could not be opened; the game runs without one");
            foreach (var note in renamed) startLogger.Information("Rename to Street Corsa: {Note}", note);

            // The game's locale (game.json): the culture the player reads numbers and dates in, the currency, the
            // units. The screens' bindings write in that culture too, from before the first element is made.
            var locale = Rules.GameFile.Installed.Locale;
            Helpers.Locale.Culture = locale.CultureInfo;
            Helpers.Locale.Money = locale.Money;
            Helpers.Locale.Units = locale.UnitSystem;
            XmlLanguage language;
            try
            {
                language = XmlLanguage.GetLanguage(Helpers.Locale.Culture.IetfLanguageTag);
            }
            catch (ArgumentException ex)
            {
                // GameLocale.Checked lets through only cultures WPF takes; should one slip past, the game still starts
                startLogger.Warning(ex, "locale.culture {Culture} is no language WPF takes; en-US is used", Helpers.Locale.Culture.Name);
                Helpers.Locale.Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                language = XmlLanguage.GetLanguage("en-US");
            }

            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(language));

            // A write that had to take a read-only mark off a file (race.ini, video.ini) says so in the log
            Helpers.SafeFile.ReadOnlyCleared = path =>
                AppLoggerFactory.CreateLogger(LogCategory.ACIntegration).Warning("{Path} was read-only; the mark was taken off to write it", path);

            // Then the rest of the backstop behind the handlers' own guards: an exception nobody catches must not end
            // the app with the AC install still changed for a race
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            InitializeComponent();

            // The look: the game's colours, sizes, typefaces and pictures with the mods' theme over them, before any
            // window is made
            Styles.Theme.Apply(Resources, Mods.ModSet.Installed);

            // The settings before anything that reads the install: they name the AC folder
            GameSettingsService = new GameSettingsService();
            // And the player's language, before any word is said, and the units they picked over the game's
            Services.Text.GameText.Language = GameSettingsService.Current.Language;
            if (Helpers.Locale.ParseUnits(GameSettingsService.Current.Units) is { } units) Helpers.Locale.Units = units;
            // A language whose mod is gone is told once and forgotten: the picker cannot show it to be taken back
            if (Services.Text.GameText.Installed.LanguageMissing)
            {
                GameSettingsService.Current.Language = null;
                GameSettingsService.Save();
            }

            // The game's content is the mods under Content, the base game first: what each kind of content is read from
            LogMods();

            ContentService = new AssettoCorsaContentService();
            IniModificationService = new IniModificationService();
            DialogService = new DialogService();
            ViewModels.AsyncRelayCommand.ReportFailure = ShowSomethingWentWrong;
            CatalogRepository = new ContentCatalogRepository();
            CarImportService = new CarImportService(CatalogRepository);
            ProfileRepository = new CarProfileRepository();
            var carParts = new Services.Parts.CarPartsService(CatalogRepository, ProfileRepository);
            CarPartsService = carParts;
            // Every price and engine figure in the game reads the rules and this service's book from here on; the UI
            // thread never waits for the book to be measured (the warm-up does that): base prices until it is
            Services.Parts.PartsWiring.Wire(carParts, mayWait: () => Dispatcher.CheckAccess() == false);
            // Cars are priced by their factory engine and running gear: the parts first
            ProfileService = new CarProfileService(CatalogRepository, ProfileRepository, CarPartsService);
            var partShops = new Services.PartShops.PartShopCatalog();
            NewspaperPartsService = new Services.Parts.NewspaperPartsService(CarPartsService, partShops);
            DealerCatalog = new Services.Dealers.DealerCatalog();
            PartShopService = new Services.PartShops.PartShopService(partShops, CarPartsService, NewspaperPartsService);
            MarketService = new UsedCarMarketService(CatalogRepository, ProfileRepository, CarPartsService, DealerCatalog);

            // Scheduler and Time Service
            Scheduler = new GameTimeScheduler();
            // Note: the rivals' tasks are registered once their services are made, and after them the event, market
            // and parts ads tasks, once RaceEventService is created
            GameTimeService = new GameTimeService(Scheduler);

            // Opponent services (must be initialized before GameStateRepository)
            OpponentRepository = new OpponentRepository();
            OpponentInitializationService = new OpponentInitializationService(OpponentRepository, CatalogRepository, ProfileRepository, CarPartsService);

            // The dealers' morning first (a sale marked down, a lot closing or opening), then the part shops' (the same, and
            // the deliveries), then the rivals' (cars bought, fixed, tuned from the shops' shelves, the sales shopped), then
            // their races against each other
            Scheduler.RegisterTask(new Services.Scheduler.Tasks.DealerReviewTask(new Services.Dealers.DealerLifeService(DealerCatalog)));
            Scheduler.RegisterTask(new Services.Scheduler.Tasks.PartShopReviewTask(new Services.PartShops.PartShopLifeService(PartShopService)));
            Scheduler.RegisterTask(new OpponentReviewTask(new OpponentLifeService(CarPartsService, MarketService, CatalogRepository, OpponentInitializationService,
                partShops: PartShopService)));
            Scheduler.RegisterTask(new Services.Scheduler.Tasks.RaceSimulatorTask(new Services.Simulation.RaceSimulatorService(MarketService, CarPartsService), CatalogRepository));
            OpponentChallengeService = new OpponentChallengeService(CatalogRepository, ProfileRepository, CarPartsService);

            // Game state repository (depends on opponent initialization service). The save in use stays open in
            // one place, shared with the race sessions, so a race's result and the state it changes are one write
            var saveDatabase = new Services.Storage.SaveDatabase();
            GameStateRepository = new GameStateRepository(OpponentInitializationService, saveDatabase,
                isGameCar: id => CatalogRepository.GetCar(id) is { Status: Models.Catalog.ContentStatus.Active } && ProfileRepository.ProfileExists(id));
            PurchaseService = new CarPurchaseService(GameStateRepository, CarPartsService, GameTimeService);
            SaleService = new CarSaleService(MarketService, GameTimeService, GameStateRepository);

            // Career services
            CarFilterService = new CarFilterService(car => MarketService.ValueOf(car));
            MilestoneService = new MilestoneService();
            VictoryConditionService = new VictoryConditionService(
                getTotalOpponents: Services.Career.VictoryConditionService.OpponentsToDominate,
                playerHasMostWins: Services.Career.VictoryConditionService.IsPlayerSeasonChampion
            );
            RaceEventService = new RaceEventService(
                CarFilterService,
                carDefId => CatalogRepository.GetCar(carDefId)
            );
            EventOpponentService = new EventOpponentService(CarFilterService, CatalogRepository, OpponentChallengeService.CanRace);
            CareerProgressService = new CareerProgressService(MilestoneService, VictoryConditionService);

            // ----- The player's AI (step 25): off by default, never waited on. Rivals' lines, the diner's talk, the paper's
            // pieces and new faces' portraits; what it writes goes into the game on the UI thread -----
            var ai = Services.Ai.AiHub.Start(() => GameSettingsService.Current.Ai, () => CurrentGameState,
                change => Dispatcher.BeginInvoke(change), id => Services.Catalog.CarNames.Of(CatalogRepository, id));
            Services.News.NewsWriter.Added = ai.Writer.NewsAdded;
            OpponentLifeService.TalkAdded = ai.Writer.StreetTalkAdded;
            OpponentLifeService.NewFaceArrived = ai.Writer.NewFace;
            Services.Market.AdBlurbs.Writer = ai.Writer;

            // Talk service for opponent dialogue: the game's own lines, in the AI's words when it has them ready
            TalkService = new AiTalkService(new StaticTalkService(), ai.Text);

            // Register event generation task (must be after RaceEventService is created)
            Scheduler.RegisterTask(new Services.Scheduler.Tasks.EventGenerationTask(RaceEventService));
            Scheduler.RegisterTask(new CarAdsReviewTask(SaleService));
            // A game with no gas station fills the cars up overnight
            Scheduler.RegisterTask(new Services.Scheduler.Tasks.StationStandInTask());
            Scheduler.RegisterTask(new Services.Scheduler.Tasks.RustTask());

            // Last: these two put engines together on a worker thread and hand the day back before they are
            // done. What comes before them is finished by the time whoever spent the time carries on.
            Scheduler.RegisterTask(new MarketRefreshTask(MarketService));
            Scheduler.RegisterTask(new PartsAdsRefreshTask(NewspaperPartsService));
            Scheduler.RegisterTask(new Services.Scheduler.Tasks.MachineWorkTask(PartShopService.Machine));

            // Race result services
            var raceResultValidator = new Services.Race.Validation.RaceResultValidator();
            var sessionRepository = new Services.Race.RaceSessionRepository(saveDatabase);
            var sessionDeduplicator = new Services.Race.Validation.SessionDeduplicator(sessionRepository);
            var raceResultProcessor = new Services.Race.RaceResultProcessor(GameStateRepository, sessionRepository, CareerProgressService, RaceEventService, CarPartsService, catalog: CatalogRepository);
            // Talking to people (step 26): the person's side is the player's AI, the race record what they know of the player
            Services.Talk.TalkDesk.Start(new Services.Talk.TalkDesk(ai.Text, (save, name) => sessionRepository.GetWithOpponentAsync(save, name),
                id => Services.Catalog.CarNames.Of(CatalogRepository, id), () => CarPartsService.Catalog,
                change => Dispatcher.BeginInvoke(change), salesmen: id => DealerCatalog.Get(id)?.Salesman, factory: MarketService.FactoryParts,
                detail: () => GameSettingsService.Current.Ai.TalkDetail));

            RaceResultIngestionService = new Services.Race.RaceResultIngestionService(
                raceResultValidator,
                sessionDeduplicator,
                raceResultProcessor,
                sessionRepository,
                () => CurrentGameState);

            // The cars' data for a race: what their parts make of them, put into the install and taken out again
            CarDataOverlay = new CarDataOverlay();
            RaceCarDataService = new Services.Race.RaceCarDataService(CarPartsService, CarDataOverlay);

            // Pass race result service to launcher
            Launcher = new AssettoCorsaLauncher(IniModificationService, RaceResultIngestionService, CarDataOverlay);

            // A game that outlived its launch (or the app) has closed: its race can be settled now
            Launcher.AssettoCorsaExited += OnAssettoCorsaExited;

            // A race has the sound device: the game's own sound fades out for it and comes back after; the radio goes off,
            // and the player's keys put it on over AC (on the UI thread, where the radio and its key hook live)
            Launcher.ExecutionStarted += (_, intent) => OnRacing(true, KeepsRadio(intent));
            Launcher.ExecutionEnded += (_, _) => OnRacing(false);

            // A car on the move in a scene (a drive into the garage, up to the pump): an AI on this machine is asked nothing
            // of the background's meanwhile, as its writing works the card the scene is drawn on and the drive lost frames
            // to it; a frame that came late says whether it was writing or painting then
            Controls.D3DViewportBase.ShowsChanged += showing => Services.Ai.AiHub.Shared.SetShowing(showing);
            Controls.D3DViewportBase.SlowFrameNotes = () => Services.Ai.AiHub.Shared switch
            {
                { IsWriting: true } => ", the AI writing on the card",
                { IsPainting: true } => ", the AI painting on the card",
                _ => null
            };

            // NavigationService (must be created after all its dependencies)
            NavigationService = new NavigationService(
                DialogService, CatalogRepository, ProfileRepository, Launcher, ContentService, OpponentChallengeService,
                GameStateRepository, MarketService, GameSettingsService, ProfileService, TalkService, DealerCatalog,
                PurchaseService, SaleService,
                GameTimeService, CarPartsService, NewspaperPartsService, PartShopService, RaceCarDataService, RaceEventService, CarFilterService,
                EventOpponentService, VictoryConditionService, MilestoneService,
                game => CurrentGameState = game);
        }

        private void OnRacing(bool racing, bool keepRadio = false)
        {
            Audio.AudioHub.Shared.SetRacing(racing);
            // A local AI model gives the GPU to the race, and is loaded again after it
            Services.Ai.AiHub.Shared.SetRacing(racing);
            Dispatcher.BeginInvoke(() => Audio.Radio.RadioPlayer.Shared.SetRacing(racing, keepRadio));
        }

        /// <summary>
        /// The player out on their own (a free run, a test-and-tune at the strip): the radio stays as it was. Only a race
        /// against somebody switches it off.
        /// </summary>
        public static bool KeepsRadio(Services.Configuration.Models.LaunchIntent intent) =>
            intent is Services.Configuration.Models.FreeRunLaunchIntent or Services.Configuration.Models.DragRaceLaunchIntent { TunePasses: not null };

        private static void LogMods()
        {
            var logger = AppLoggerFactory.CreateLogger(LogCategory.Startup);
            var mods = Mods.ModSet.Installed;
            logger.Information("Content: {Mods} from {Root}", string.Join(", ", mods.Mods), mods.Root);
            foreach (var problem in mods.Problems) logger.Warning("Mods: {Problem}", problem);
            Mods.ModProblems.Add("Mods", mods.Problems);
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var logger = AppLoggerFactory.CreateLogger(LogCategory.Startup);
            logger.Information("Application starting");

            // async void: whatever throws in here would otherwise end the app with nothing in the log
            try
            {
                await StartAsync(logger);
            }
            catch (Exception ex)
            {
                logger.Critical(ex, "Start-up failed");
                ShowStartupError($"Street Corsa could not start:\n{ex.Message}\n\nThe details are in the log.", "Street Corsa");
                Shutdown();
            }
        }

        private async Task StartAsync(IAppLogger logger)
        {
            // Validate AC installation
            if (!AppSettings.Instance.IsValidInstallation())
            {
                logger.Error("Assetto Corsa installation not found at path: {ACPath}",
                    AppSettings.Instance.AssettoCorsaPath);

                // A message box, not the game's dialog: that one does not wait, and the app is about to close. The
                // Settings screen cannot be reached from here, so the message says where the folder is set by hand.
                ShowStartupError(
                    GameText.Say("startup.no_ac", ("path", AppSettings.Instance.AssettoCorsaPath), ("settings", GameSettingsService.SettingsPath)),
                    GameText.Say("startup.no_ac.title"));
                Shutdown();
                return;
            }

            // A race that ended badly (a crash, the power) may have left cars with changed data and race.ini
            // rewritten. Not while the game still runs, though (the app was restarted during a race): it is reading
            // that data, so it goes back once the game has closed.
            var acRunning = AcProcesses.AnyRunning();
            var acStillRunning = acRunning && HasLeftoversToRestore(logger);
            var restoreProblems = new List<string>();
            if (acStillRunning)
            {
                logger.Warning("Assetto Corsa is running: what an earlier race changed in it goes back once it closes");
                StartWaitForAcExit(logger);
            }
            else if (acRunning)
            {
                logger.Information("Assetto Corsa is running, and nothing of an earlier race is waiting to go back");
            }
            else
            {
                RestoreInstall(logger, restoreProblems);
            }

            SweepTempFiles(logger);

            logger.Information("Assetto Corsa installation validated at {ACPath}",
                AppSettings.Instance.AssettoCorsaPath);

            // Import AC content into catalog at startup
            Task partsWarmUp = Task.CompletedTask;
            try
            {
                logger.Information("Starting car import");

                var result = await CarImportService.ImportCarsAsync();

                logger.Information("Car import completed. TotalFound: {TotalFound}, Imported: {Imported}, Updated: {Updated}, Skipped: {Skipped}, Failed: {Failed}, Duration: {Duration}s",
                    result.TotalFound, result.Imported, result.Updated, result.Skipped, result.Failed, result.Duration.TotalSeconds);

                if (result.Errors.Any())
                {
                    logger.Warning("Car import completed with {ErrorCount} errors", result.Errors.Count);
                    foreach (var error in result.Errors.Take(10))
                    {
                        logger.Warning("Import error: {Error}", error);
                    }
                }

                // Generate car profiles for all imported cars
                logger.Information("Ensuring car profiles exist");
                await ProfileService.EnsureProfilesExistAsync();
                logger.Information("Car profiles ready. Total profiles: {ProfileCount}",
                    ProfileRepository.GetProfileCount());

                // Parts catalog and factory engines load in the background; nothing needs them before the garage. The cars
                // are priced at their parts' prices once they are up (a new car's profile has its body's price until then)
                partsWarmUp = CarPartsService.WarmUpAsync().ContinueWith(_ => ProfileService.RepriceAsync(), TaskScheduler.Default).Unwrap();

                // Load track content
                logger.Information("Loading track content");
                var tracks = await ContentService.LoadTracksAsync();
                logger.Information("Loaded {TrackCount} tracks with {ConfigCount} total configurations",
                    tracks.Count,
                    tracks.Sum(t => t.Configurations.Count));
            }
            catch (Exception ex)
            {
                logger.Critical(ex, "Critical error during content import");

                ShowStartupError(
                    GameText.Say("startup.import_failed", ("error", ex.Message), ("settings", GameSettingsService.SettingsPath)),
                    GameText.Say("startup.import_failed.title"));
                Shutdown();
                return;
            }

            // The sound: the pump here, FMOD on a worker. Muted behind other windows if the player wants it
            Audio.AudioHub.Shared.Start(GameSettingsService.Current.Audio);
            Audio.Sfx.RegisterClicks();

            // The radio: its lists and folders read, the music listed on a worker, on again if the player asked
            Audio.Radio.RadioPanelViewModel.Settings = GameSettingsService;
            Audio.Radio.RadioPlayer.Shared.Start();
            // And the music the game starts to, over the intro and the main screen until a game is on
            Audio.StartMusic.Begin();
            Activated += (_, _) => Audio.AudioHub.Shared.SetFocused(true);
            Deactivated += (_, _) => Audio.AudioHub.Shared.SetFocused(false);

            // Orphaned race results are processed once a game is loaded (see CurrentGameState): a result can only
            // be applied to the save whose race it is

            // Navigate to initial screen
            logger.Information("Navigating to initial screen");
            NavigationService.NavigateToStart();

            // Closing the window during a race asks first: the race is stopped and the install put back before the app goes
            if (MainWindow != null) MainWindow.Closing += OnMainWindowClosing;

            if (acStillRunning)
            {
                DialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                    DialogService,
                    GameText.Say("startup.ac_running"),
                    GameText.Say("startup.ac_running.title")));
            }

            // What stayed changed in AC after the start-up restore: the player is told once, the next start tries again
            if (restoreProblems.Count > 0)
            {
                var message = AssettoCorsaLauncher.RestoreProblemsMessage(restoreProblems);
                DialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                    DialogService, message.Text, message.Title));
            }

            _ = ShowModProblemsAsync(partsWarmUp, logger);
        }

        // At most this many of the mods' problems in the dialog; the rest are in the log
        private const int ModProblemsShown = 12;

        /// <summary>
        /// The mods' problems, told to the player once at start-up (they used to reach only the log): the content is read
        /// on a worker first, the parts with it, so what a mod got wrong anywhere is there to be told. What a mod could not
        /// use is left out; the rest of the game plays as usual. Never throws.
        /// </summary>
        private async Task ShowModProblemsAsync(Task partsWarmUp, IAppLogger logger)
        {
            try
            {
                // Read here too, off the screens' thread: the first car to move would otherwise wait on every mod's scripts
                var garage = AppSettings.Instance.GarageId;
                await Task.WhenAll(partsWarmUp, Task.Run(() =>
                {
                    _ = Mods.ModSet.Installed;
                    _ = Rules.RuleBook.Installed;
                    _ = Services.Text.GameText.Installed;
                    _ = Services.Race.RaceFormats.Installed;
                    _ = Services.Career.CareerContent.Installed;
                    OpponentRepository.LoadAllOpponents();
                    _ = Services.Street.StreetScenes.Installed;
                    _ = Services.Station.StationScenes.Installed;
                    _ = Services.CarLot.WarehouseScenes.Installed;
                    _ = Services.Ai.AiPrompts.Installed;
                    _ = Services.Scripting.ScriptBook.Installed;
                    _ = Services.Scripting.GarageScene.For(garage);
                    Tell("Cars", Parts.Cars.CarBook.Installed.Problems);
                    Tell("Part groups", Parts.PartGroups.Installed.Problems);
                    Tell("Part classes", Parts.PartClasses.Installed.Problems);
                    Tell("Fuels", Parts.Logic.Fuels.Installed.Problems);
                }));
            }
            catch (Exception ex)
            {
                logger.Warning("Reading the mods' content for their problems failed: {Error}", ex.Message);
            }

            var problems = Mods.ModProblems.All;
            if (problems.Count == 0) return;

            // The parts code the tools link reads its own files and keeps its problems: logged and told here
            void Tell(string what, IReadOnlyList<string> found)
            {
                foreach (var problem in found) logger.Warning("{What}: {Problem}", what, problem);
                Mods.ModProblems.Add(what, found);
            }

            var shown = string.Join("\n", problems.Take(ModProblemsShown).Select(p => "  " + p));
            var more = problems.Count > ModProblemsShown
                ? "\n" + GameText.Say("startup.mod_problems.more", ("count", problems.Count - ModProblemsShown))
                : string.Empty;
            var text = GameText.Say("startup.mod_problems", ("problems", shown + more), ("log", AppLoggerFactory.LogDirectory));
            var title = GameText.Say("startup.mod_problems.title");
            _ = Dispatcher.BeginInvoke(() =>
                DialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(DialogService, text, title)));
        }

        /// <summary>A start-up failure the app closes on, in a message box that waits for the player; never throws</summary>
        private void ShowStartupError(string message, string title) => ShowMessage(message, title, MessageBoxImage.Error);

        /// <summary>
        /// A message box that waits for the player, owned by the main window when it is up: one without an owner can
        /// come up behind the maximized borderless window, and the game looks hung. From another thread it goes
        /// through the UI thread; when that does not take it up in time (stuck, or it is what crashed), it goes up on
        /// its own, over every window. Never throws.
        /// </summary>
        private void ShowMessage(string message, string title, MessageBoxImage image)
        {
            try
            {
                var dispatcher = Dispatcher;
                if (dispatcher.CheckAccess())
                {
                    ShowOwned();
                    return;
                }

                if (!dispatcher.HasShutdownStarted)
                {
                    var shown = false;
                    try
                    {
                        // The time limit is for the UI thread to take it up, not for the player to read it
                        dispatcher.Invoke(() => { shown = true; ShowOwned(); }, DispatcherPriority.Send, CancellationToken.None, FatalPathWait);
                    }
                    catch (TimeoutException)
                    {
                        // The UI thread did not answer: the box goes up from here
                    }

                    if (shown) return;
                }

                System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, image, MessageBoxResult.OK, System.Windows.MessageBoxOptions.DefaultDesktopOnly);
            }
            catch (Exception)
            {
                // No window to show it in either; the log has it
            }

            void ShowOwned()
            {
                if (MainWindow is { IsVisible: true } owner)
                    System.Windows.MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
                else
                    System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, image);
            }
        }

        /// <summary>
        /// The game's "Something went wrong" dialog, for a failure the game carries on after (a command that threw).
        /// Over whatever is up, so a failed sale in the sell dialog is told there and then; the dialog it covers comes
        /// back once this one is read.
        /// </summary>
        private void ShowSomethingWentWrong(Exception exception)
        {
            DialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                DialogService,
                GameText.Say("startup.error", ("error", exception.Message)),
                GameText.Say("startup.error.title")), jumpQueue: true);
        }

        // ===== THE AC INSTALL: PUT BACK =====

        /// <summary>
        /// Whether an earlier race left anything in the install to put back. A probe that fails (the restore folder
        /// unreadable) counts as yes: the restore is then deferred until AC has closed, never skipped, and start-up
        /// goes on.
        /// </summary>
        private bool HasLeftoversToRestore(IAppLogger logger)
        {
            try
            {
                return CarDataOverlay.Applied.Count > 0 || IniModificationService.HasPendingRestore;
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Could not look for what an earlier race left changed; it is put back once Assetto Corsa closes");
                return true;
            }
        }

        /// <summary>
        /// Car data, race copies and cfg INI files back as they were before a race. Each part on its own (one that
        /// fails does not stop the other), logged, never throws. Only call it when no AC process runs.
        /// </summary>
        /// <param name="problems">
        /// Gets a line about each car, copy or cfg file that stayed changed, for start-up to tell the player; the exit
        /// and fatal paths leave it out and only log
        /// </param>
        private void RestoreInstall(IAppLogger? logger, ICollection<string>? problems = null)
        {
            var found = problems ?? new List<string>();
            var restored = AssettoCorsaLauncher.RestoreInstall(CarDataOverlay, IniModificationService, logger, found);
            if (restored > 0) logger?.Warning("{Count} car(s) and file(s) put back from an earlier race", restored);
            if (found.Count > 0) logger?.Warning("{Count} thing(s) an earlier race changed stayed changed in Assetto Corsa", found.Count);
        }

        /// <summary>
        /// Temp files an atomic write left when it was cut short (a kill, the power), in the folders the game writes
        /// into: AC's cfg folder, the keep folder of what a race changed, the settings' folder and the race mode's
        /// folder in the install. Never throws.
        /// </summary>
        private static void SweepTempFiles(IAppLogger logger)
        {
            try
            {
                var removed = Helpers.SafeFile.SweepTemps(Services.Configuration.IniModificationService.DefaultCfgDirectory)
                    + Helpers.SafeFile.SweepTemps(AppSettings.AcRestorePath, recursive: true)
                    + Helpers.SafeFile.SweepTemps(AppSettings.AppDataPath)
                    + Helpers.SafeFile.SweepTemps(ScRaceMode.InstalledFolder(AppSettings.Instance.AssettoCorsaPath));
                if (removed > 0) logger.Information("Removed {Count} temp file(s) left by writes cut short", removed);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Could not clean up temp files left by writes cut short");
            }
        }

        /// <summary>
        /// Starts the wait for AC to exit (the install goes back after it, and a pending race is settled on
        /// <see cref="IAssettoCorsaLauncher.AssettoCorsaExited"/>), unless one is already under way
        /// </summary>
        private void StartWaitForAcExit(IAppLogger logger)
        {
            if (Interlocked.Exchange(ref _waitingForAcExit, 1) == 1) return;
            _ = RestoreWhenAcExitsAsync(logger);
        }

        /// <summary>The restore of an earlier race, once a game that was still running has closed; logged, never throws</summary>
        private async Task RestoreWhenAcExitsAsync(IAppLogger logger)
        {
            try
            {
                var restored = await Launcher.RestoreWhenAcExitsAsync();
                logger.Information("Assetto Corsa closed: {Count} car(s) and file(s) put back from an earlier race", restored);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Could not put back what an earlier race changed once Assetto Corsa closed");
            }
            finally
            {
                Interlocked.Exchange(ref _waitingForAcExit, 0);
            }
        }

        /// <summary>
        /// AC has closed after a wait for it (a race that outlived its launch, or one the app was restarted during):
        /// the loaded game's pending race can be settled now. On the UI thread, which owns the game state.
        /// </summary>
        private void OnAssettoCorsaExited(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (_currentGameState?.PendingRace != null) _ = ProcessOrphanedResultsAsync();
                });
            }
            catch (Exception ex)
            {
                AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion).Warning(ex, "Could not settle the pending race after Assetto Corsa closed");
            }
        }

        /// <summary>True when the race or showroom runs, the one the launcher started or any other; never throws</summary>
        private bool IsAssettoCorsaRunning()
        {
            try
            {
                if (Launcher?.IsAssettoCorsaRunning == true) return true;
            }
            catch (Exception)
            {
                return true;
            }

            return AcProcesses.AnyRunning();
        }

        /// <summary>
        /// Orphaned race results for the game just loaded (or once AC has closed); logged, never throws. What came of
        /// it is told to the player through the dialog queue, over whatever screen they are on, and a garage that was
        /// opened before the pass changed the game is opened again on the new state. A pending race whose game is
        /// still running is settled once it closes.
        /// </summary>
        private async Task ProcessOrphanedResultsAsync()
        {
            var logger = AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion);
            try
            {
                var state = _currentGameState;
                logger.Information("Processing orphaned race result files for {SaveName}", state?.SaveName);
                var orphanedResult = await RaceResultIngestionService.ProcessOrphanedResultsAsync();
                logger.Information("Orphaned results processed: {Processed} files, {Deferred} left for later, forfeit {Forfeit}, {Errors} errors",
                    orphanedResult.FilesProcessed, orphanedResult.FilesDeferred, orphanedResult.ForfeitApplied, orphanedResult.Errors.Count);

                if (orphanedResult.WaitingForAssettoCorsa)
                    StartWaitForAcExit(logger);

                // The game may have been switched meanwhile: messages about another save are not shown over it
                if (state == null || !ReferenceEquals(state, _currentGameState)) return;

                var changed = orphanedResult.FilesProcessed > 0 || orphanedResult.ForfeitApplied;
                if (changed && NavigationService.CurrentScreen is Screens.Garage.GarageScreenViewModel)
                    NavigationService.Go(NavigationService.CurrentId, state, new Navigation.FlowArgs { SkipAnimation = true });

                foreach (var message in orphanedResult.PlayerMessages)
                {
                    Dialogs.PlayerMessageDialogs.Show(DialogService, message, () => NavigationService.GoToMenu());
                }
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to process orphaned race results");
            }
        }

        /// <summary>
        /// The window closes during a race only after asking: yes stops the race (the launcher kills AC, puts the
        /// install back in its finally) and the app closes once that is done
        /// </summary>
        private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Only a launch under way asks. A wait for an earlier game to exit is not one: the app just goes, the game
            // is left running, and the next start puts the install back and settles the race.
            if (_closeAfterRace || Launcher == null || !Launcher.IsLaunchInProgress) return;

            e.Cancel = true;
            DialogService.ShowDialog(new Dialogs.Confirmation.ConfirmationDialogViewModel(
                DialogService,
                GameText.Say("quit.during_race"),
                GameText.Say("quit.during_race.title"),
                stop =>
                {
                    if (!stop) return;
                    _closeAfterRace = true;
                    _ = StopRaceAndCloseAsync();
                }), jumpQueue: true);
        }

        private async Task StopRaceAndCloseAsync()
        {
            var logger = AppLoggerFactory.CreateLogger(LogCategory.App);
            try
            {
                Launcher.CancelRace();

                // The launcher's finally puts the install back; the app waits for it (it gives up on a game that
                // will not die after a while, and the next start puts things back then)
                var waited = TimeSpan.Zero;
                while (Launcher.IsLaunchInProgress && waited < TimeSpan.FromSeconds(30))
                {
                    await Task.Delay(250);
                    waited += TimeSpan.FromMilliseconds(250);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Stopping the race on the way out failed");
            }

            Shutdown();
        }

        // ===== EXCEPTIONS NOBODY CAUGHT =====

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var logger = AppLoggerFactory.CreateLogger(LogCategory.App);
            logger.Critical(e.Exception, "Unhandled exception on the UI thread");

            if (IsRecoverable(e.Exception))
            {
                e.Handled = true;
                try
                {
                    // One dialog at a time: an open one belongs to a flow that is waiting on it, and the log has this
                    if (DialogService != null && !DialogService.IsDialogOpen)
                    {
                        DialogService.ShowDialog(new Dialogs.Information.InformationDialogViewModel(
                            DialogService,
                            $"Something went wrong:\n{e.Exception.Message}\n\nThe game carries on; the details are in the log.",
                            "Error"));
                    }
                }
                catch (Exception dialogError)
                {
                    logger.Error(dialogError, "Could not show the error dialog");
                }

                return;
            }

            // Not recoverable: the app ends. What must happen before it does happens now.
            RunFatalPath(e.Exception);
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                AppLoggerFactory.CreateLogger(LogCategory.App)
                    .Critical(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
            }
            catch (Exception)
            {
                // Logging is not there (a crash before it started); the fatal path still runs
            }

            RunFatalPath(e.ExceptionObject as Exception);
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                AppLoggerFactory.CreateLogger(LogCategory.App).Error(e.Exception, "A background task failed and nobody awaited it");
            }
            catch (Exception)
            {
                // Nothing more to do about it
            }

            e.SetObserved();
        }

        /// <summary>
        /// Whether the app can carry on after an exception on the UI thread. Not after the runtime is in trouble
        /// (memory, corrupt state, a type that failed to load), and not when handlers fail over and over (the same
        /// error from a render or timer callback: the dialog would come back forever).
        /// </summary>
        private bool IsRecoverable(Exception exception)
        {
            var root = exception;
            while (root is System.Reflection.TargetInvocationException { InnerException: { } inner }) root = inner;

            if (root is OutOfMemoryException or InsufficientExecutionStackException or AccessViolationException
                or System.Runtime.InteropServices.SEHException or InvalidProgramException or BadImageFormatException
                or TypeInitializationException or TypeLoadException)
                return false;

            var now = DateTime.UtcNow;
            _recentUiErrors.Enqueue(now);
            while (_recentUiErrors.Count > 0 && now - _recentUiErrors.Peek() > TimeSpan.FromSeconds(30)) _recentUiErrors.Dequeue();
            return _recentUiErrors.Count <= 10;
        }

        /// <summary>
        /// What must happen before the process dies: the AC install put back (only when no game runs), FMOD
        /// released, the save's database closed, the log flushed. The game is not saved: it goes on from its last save
        /// (<see cref="ShutDownCleanly"/> says why). Once, whichever handler gets here
        /// first; every step on its own, and it never throws.
        /// </summary>
        private void RunFatalPath(Exception? exception)
        {
            if (Interlocked.Exchange(ref _fatalPathRan, 1) == 1) return;

            IAppLogger? logger = null;
            try
            {
                logger = AppLoggerFactory.CreateLogger(LogCategory.App);
                logger.Critical(exception ?? new Exception("(no exception object)"), "Fatal error: putting Assetto Corsa back before the app ends (the game is not saved: it goes on from its last save)");
            }
            catch (Exception)
            {
                // Logging never started: the steps still run
            }

            ShutDownCleanly(logger, fatal: true);

            // Last, once everything that must reach the disk has: the player learns why the game is gone
            ShowMessage(
                "Street Corsa ran into an error it cannot recover from and has to close.\n\n" +
                (_currentGameState != null
                    ? "The game was not saved on the way out: it goes on from its last save, as it was before the error.\n\n"
                    : "") +
                $"The details are in the log:\n{AppLoggerFactory.LogDirectory}",
                "Street Corsa", MessageBoxImage.Error);
        }

        /// <summary>
        /// The way out, normal or not: save, restore, FMOD, the database, the log. Every step guarded. On the crash
        /// path the log is flushed before the database closes, and the close waits for the database's lock only so
        /// long: a thread stuck holding it must not keep what was logged about the crash off the disk.
        /// </summary>
        private void ShutDownCleanly(IAppLogger? logger, bool fatal = false)
        {
            // Nothing more goes into the AC install from here on: a launch still under way (on a worker, or inside the
            // crash path's message box) stops at its next change instead of going in after the restore below
            AcInstallGate.Seal();

            // The crash path does not save: whatever the UI thread was in the middle of when it threw (the money
            // taken, the car not yet added) would be written over the last good save. The game saves as it goes
            // (a purchase, a race, a new day), so the last save is a whole one, and the next load goes on from there.
            var state = _currentGameState;
            if (fatal && state != null)
                logger?.Warning("Not saving {SaveName} on the crash path: the game goes on from its last save", state.SaveName);

            // Save current game state if one is loaded. On the UI thread, which owns the state; a UI thread that
            // does not get to it in time (stuck) means no save rather than a hang.
            if (!fatal && state != null && !string.IsNullOrEmpty(state.SaveName) && GameStateRepository != null)
            {
                try
                {
                    logger?.Information("Saving game state on exit: {SaveName}", state.SaveName);
                    if (OnUiThread(() => GameStateRepository.Save(state, state.SaveName), logger))
                        logger?.Information("Game state saved successfully");
                    else
                        logger?.Error("The game could not be saved on exit: the UI thread did not answer within {Seconds}s", FatalPathWait.TotalSeconds);
                }
                catch (Exception ex)
                {
                    logger?.Error(ex, "Failed to save game state on exit");
                }
            }

            // The install goes back unless the game still runs on it; then the next start does it. Under the install's
            // gate, so it never falls in the middle of a launch preparing its changes; a gate that is not free in
            // time leaves the restore to the next start rather than hang the way out.
            try
            {
                if (IsAssettoCorsaRunning())
                {
                    logger?.Warning("Assetto Corsa is still running: what the race changed in it goes back at the next start");
                }
                else if (!AcInstallGate.TryRun(FatalPathWait, () => RestoreInstall(logger)))
                {
                    logger?.Warning("A launch was changing the install: what it changed goes back at the next start");
                }
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Could not put Assetto Corsa back on exit");
            }

            // Where the radio was, for the next start, and the settings (a volume turned a moment ago is still waiting
            // for its save); then FMOD's threads, which call back into this assembly, stop before the runtime goes
            try
            {
                GameSettingsService?.Save();
                Audio.Radio.RadioPlayer.Shared.Save();
                Audio.Radio.RadioPlayer.Shared.StopScanning();
                Audio.FmodLifetime.Shutdown();
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "FMOD did not shut down cleanly");
            }

            if (fatal)
            {
                logger?.Information("Closing the save's database; the log is flushed first");
                AppLoggerFactory.Shutdown();
                try
                {
                    // Nothing is logged from here on: the log is closed
                    if (GameStateRepository is GameStateRepository repository)
                        repository.Database.TryDispose(FatalPathWait);
                    else
                        (GameStateRepository as IDisposable)?.Dispose();
                }
                catch (Exception)
                {
                    // The process is ending; LiteDB's journal covers a close that did not happen
                }

                return;
            }

            // After the final save: the save's database closes
            try
            {
                (GameStateRepository as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "The save's database did not close cleanly");
            }

            AppLoggerFactory.Shutdown();
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread: directly when already on it (or when the dispatcher is
        /// gone), else through the dispatcher with a time limit. False when it did not run in time. Exceptions of
        /// the action come through.
        /// </summary>
        private bool OnUiThread(Action action, IAppLogger? logger)
        {
            var dispatcher = Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess() || dispatcher.HasShutdownStarted)
            {
                action();
                return true;
            }

            var ran = false;
            try
            {
                dispatcher.Invoke(() => { action(); ran = true; }, DispatcherPriority.Send, CancellationToken.None, FatalPathWait);
            }
            catch (TimeoutException)
            {
                logger?.Warning("The UI thread did not answer within {Seconds}s", FatalPathWait.TotalSeconds);
            }

            return ran;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            IAppLogger? logger = null;
            try
            {
                logger = AppLoggerFactory.CreateLogger(LogCategory.App);
                logger.Information("Application shutting down");
            }
            catch (Exception)
            {
                // The way out goes ahead without a log
            }

            // The crash path did all of this already if it ran
            if (Interlocked.Exchange(ref _fatalPathRan, 1) == 0) ShutDownCleanly(logger);

            base.OnExit(e);
        }
    }

}
