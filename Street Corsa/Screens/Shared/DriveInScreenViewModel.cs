using System.Windows.Threading;
using StreetCorsa.Audio;
using StreetCorsa.Controls.Street;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// A screen the player drives into, seen from the driver's seat (the gas station, the diner): what its
    /// <see cref="Controls.DriveInViewport3D"/> shows (<see cref="Stage"/>), the player's own engine, and the view's black
    /// curtain. The car comes once the place is on screen, or at once when it cannot be shown, or once it has been waited
    /// on long enough; the flow's scripts play on the viewport through the stage.
    /// </summary>
    public abstract class DriveInScreenViewModel : BaseScreenViewModel, IScriptedScreen
    {
        // A place that is loading is waited on this long at most before the car is simply there
        private static readonly TimeSpan PlaceLoadWait = TimeSpan.FromSeconds(25);

        private readonly GameState _visitState;
        private readonly IAppLogger _visitLogger;
        private readonly DispatcherTimer _waitForPlace;
        private DateTime _waitingSince;
        private Func<bool> _canBeShown = () => false;
        private Action? _comeIn;

        protected DriveInScreenViewModel(GameState gameState, DriveInStage stage, IAppLogger logger)
        {
            _visitState = gameState;
            _visitLogger = logger;
            Stage = stage;
            _waitForPlace = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _waitForPlace.Tick += (_, _) => ComeInWhenShown();
        }

        /// <summary>What the car does, which the place's viewport shows</summary>
        public DriveInStage Stage { get; }

        /// <summary>The player's engine: it runs on the way in and out, off while the car stands there</summary>
        public EngineRunner PlayerEngine { get; } = new(EngineChannel.Main);

        /// <summary>The view's curtain lifting off the place, as the car comes in (set by the view)</summary>
        public Action? OpenCurtain { get; set; }

        /// <summary>The view's curtain coming down once the car has gone (set by the view)</summary>
        public Func<Task>? CloseCurtain { get; set; }

        /// <summary>The player is here: off once they have left the screen</summary>
        protected bool IsActive { get; set; }

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
            OpenCurtain?.Invoke();
        }

        /// <summary>The car stands there at once, nothing waited on (the place could not be set up), and the curtain lifts</summary>
        protected void StandThereAtOnce()
        {
            _waitForPlace.Stop();
            Stage.StandThere(PlayerEngine);
            OpenCurtain?.Invoke();
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

                if (IsActive && running && Stage.Phase is DriveInPhase.Waiting or DriveInPhase.Arriving) PlayerEngine.StartRunning();
            }
            catch (Exception ex)
            {
                _visitLogger.Warning(ex, "The player's engine could not be started");
            }
        }

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
