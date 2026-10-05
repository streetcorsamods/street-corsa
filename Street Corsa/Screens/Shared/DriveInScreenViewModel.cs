using System.Windows.Threading;
using StreetCorsa.Audio;
using StreetCorsa.Controls.Street;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Storage;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// A screen the player drives into, seen from the driver's seat (the gas station, the diner): what its
    /// <see cref="Controls.DriveInViewport3D"/> shows (<see cref="Stage"/>), the player's own engine, and the view's black
    /// curtain. The car comes once the place is on screen, or at once when it cannot be shown, or once it has been waited
    /// on long enough; the flow's scripts play on the viewport through the stage. What every visit does the same way is
    /// here too: the time spent, the save, sending the player home and telling the flow the car is in.
    /// </summary>
    public abstract class DriveInScreenViewModel : BaseScreenViewModel, IScriptedScreen
    {
        // A place that is loading is waited on this long at most before the car is simply there
        private static readonly TimeSpan PlaceLoadWait = TimeSpan.FromSeconds(25);

        private readonly GameState _visitState;
        private readonly IAppLogger _visitLogger;
        private readonly NavigationService _visitNavigation;
        private readonly DialogService _visitDialogs;
        private readonly IGameStateRepository _visitSaves;
        private readonly IGameTimeService _visitTime;
        private readonly DispatcherTimer _waitForPlace;
        private DateTime _waitingSince;
        private Func<bool> _canBeShown = () => false;
        private Action? _comeIn;

        protected DriveInScreenViewModel(
            NavigationService navigationService,
            DialogService dialogService,
            GameState gameState,
            IGameStateRepository gameStateRepo,
            IGameTimeService timeService,
            DriveInStage stage,
            IAppLogger logger)
        {
            _visitNavigation = navigationService;
            _visitDialogs = dialogService;
            _visitState = gameState;
            _visitSaves = gameStateRepo;
            _visitTime = timeService;
            _visitLogger = logger;
            Stage = stage;
            _waitForPlace = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _waitForPlace.Tick += (_, _) => ComeInWhenShown();
        }

        /// <summary>What the car does, which the place's viewport shows</summary>
        public DriveInStage Stage { get; }

        /// <summary>The player's engine: it runs on the way in and out, off while the car stands there</summary>
        public EngineRunner PlayerEngine { get; } = new(EngineChannel.Main);

        private Action? _openCurtain;
        private bool _liftWhenHung;

        /// <summary>
        /// The view's curtain lifting off the place, as the car comes in (set by the view). Asked to lift before the view
        /// hung it (the car stands there from Enter, before the screen's view is made), it lifts as it is hung.
        /// </summary>
        public Action? OpenCurtain
        {
            get => _openCurtain;
            set
            {
                _openCurtain = value;
                if (value == null || !_liftWhenHung) return;
                _liftWhenHung = false;
                value();
            }
        }

        /// <summary>The curtain lifts now, or as soon as the view hangs it</summary>
        protected void LiftCurtain()
        {
            if (_openCurtain is { } open) open();
            else _liftWhenHung = true;
        }

        /// <summary>The view's curtain coming down once the car has gone (set by the view)</summary>
        public Func<Task>? CloseCurtain { get; set; }

        /// <summary>The player is here: off once they have left the screen</summary>
        protected bool IsActive { get; set; }

        /// <summary>The player is on their way out (driving off, sent home): nothing new starts here</summary>
        protected bool Leaving { get; set; }

        /// <summary>The car's engine once worked out: it goes home with the car, which drives into the garage</summary>
        protected EngineSpec? OwnEngine { get; private set; }

        /// <summary>
        /// Waits for the place to be on screen (while <paramref name="canBeShown"/>, it has its model and the car), then
        /// <paramref name="comeIn"/> and the curtain lifts
        /// </summary>
        protected void ComeInOnceShown(Func<bool> canBeShown, Action comeIn)
        {
            _canBeShown = canBeShown;
            _comeIn = comeIn;
            _waitingSince = DateTime.UtcNow;
            _waitForPlace.Start();
        }

        private void ComeInWhenShown()
        {
            if (!IsActive) return;
            var waited = DateTime.UtcNow - _waitingSince;
            if (!Stage.IsShown && !Stage.CannotShow && _canBeShown() && waited < PlaceLoadWait) return;

            _waitForPlace.Stop();
            _comeIn?.Invoke();
            LiftCurtain();
        }

        /// <summary>The car stands there at once, nothing waited on (the place could not be set up), and the curtain lifts</summary>
        protected void StandThereAtOnce()
        {
            _waitForPlace.Stop();
            Stage.StandThere(PlayerEngine);
            LiftCurtain();
        }

        /// <summary>
        /// Works out the player's engine and starts it when <paramref name="running"/>, only while the car is on its way
        /// in: an engine worked out after it stopped stays off, and driving off starts it itself
        /// </summary>
        protected async Task StartOwnEngineAsync(ICarPartsService parts, IContentCatalogRepository catalog, Car car, bool running)
        {
            try
            {
                var spec = await EngineSpecs.ForAsync(parts, car, CarNames.Of(catalog, car.DefinitionId));
                if (!IsActive) return;
                OwnEngine = spec;
                await PlayerEngine.SetEngineAsync(spec);

                if (IsActive && running && spec is not { Runs: false } && Stage.Phase is DriveInPhase.Waiting or DriveInPhase.Arriving) PlayerEngine.StartRunning();
            }
            catch (Exception ex)
            {
                _visitLogger.Warning(ex, "The player's engine could not be started");
            }
        }

        /// <summary>
        /// Says why the player cannot stay and takes them home, once the navigation that brought them here is over (from
        /// <c>Enter</c>)
        /// </summary>
        protected void SendHome(string message, string title)
        {
            Leaving = true;
            _visitDialogs.ShowDialog(new InformationDialogViewModel(_visitDialogs, message, title));
            _visitNavigation.GoHomeLater(this, _visitState);
        }

        /// <summary>
        /// The car is in and the place's own show is over: the flow's enter script, when a mod has one, plays once the
        /// viewport has started what it was telling of
        /// </summary>
        protected void ScreenUp() => _visitNavigation.ScreenReadyLater(this);

        /// <summary>The time a thing done here takes; one that could not be spent is logged, the visit goes on</summary>
        protected async Task SpendAsync(GameAction action)
        {
            try
            {
                await _visitTime.SpendTimeAsync(_visitState, action);
            }
            catch (Exception ex)
            {
                _visitLogger.Warning(ex, "Could not spend the time here");
            }
        }

        /// <summary>The game saved, as it stands after what was done here (a failed save is logged)</summary>
        protected void Save() => GameSaves.TrySave(_visitSaves, _visitState, _visitLogger, "a visit");

        /// <summary>The player has left: nothing waits, the car is gone, the engine is quiet</summary>
        protected void EndVisit()
        {
            IsActive = false;
            _waitForPlace.Stop();
            Stage.Clear();
            PlayerEngine.Detach();
        }

        // The flow's scripts play on the place's viewport, through the stage
        Func<string, Services.Scripting.ScriptFacts, Task?>? IScriptedScreen.PlayScreenScript
        {
            get => Stage.PlayTrigger;
            set { }
        }

        /// <inheritdoc cref="IScriptedScreen.ScriptFacts"/>
        public Services.Scripting.ScriptFacts ScriptFacts => new(_visitState.Date, StreetLights.At(_visitState.Date), Stage.Towed);

        /// <summary>A leave script played and the way out went nowhere: the car stands there again</summary>
        void IScriptedScreen.PutBackAfterLeave() => Stage.BackToStand();
    }
}
