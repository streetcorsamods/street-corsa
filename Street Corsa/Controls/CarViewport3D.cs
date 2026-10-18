using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForwardDark;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.Scripting;
using StreetCorsa.Services.Text;
using static StreetCorsa.Helpers.Matrices;

namespace StreetCorsa.Controls;

/// <summary>
/// Real-time 3D car viewport backed by the AcTools renderer.
/// The renderer draws off-screen into a shared DX11 texture which is presented through a D3DImage,
/// so the viewport composes like any other WPF element (overlays, opacity, dialogs on top all work).
/// </summary>
public class CarViewport3D : D3DViewportBase
{
    // Orbit camera limits: stay above the floor and inside the showroom walls
    private const float MinBeta = 0.02f;
    private const float MaxBeta = 1.2f;
    private const float MinRadius = 3.2f;
    private const float MaxRadius = 9.0f;
    private const float DefaultRadius = 6.5f;
    private const float DefaultAlpha = 0.8f;
    private const float DefaultBeta = 0.15f;
    private const float EmptyGarageEyeHeight = 1.5f;

    // With the parts on show there is something worth a closer look
    private const float PartsMinRadius = 1.6f;

    // The engine stand: the camera close in on the engine, the car gone
    private const float StandRadius = 2.6f;
    private const float StandMinRadius = 1.0f;
    private const float StandAlpha = 0.9f;
    private const float StandBeta = 0.32f;

    // An engine on the hoist, between the car and the stand: the camera follows it from a little further out and above
    private const float CarryRadius = 3.6f;
    private const float CarryBeta = 0.3f;

    // What comes off before the engine goes on the hoist: one part after another, each starting this long after the
    // one before, while that one is still on its way
    private const float StripStagger = 0.45f;

    // A load on the hoist goes across with its bottom over the car's wings: this share of the car's height
    private const float WingLine = 0.72f;

    // How long into the lift the hoist has the engine's weight off the springs
    private const float LiftTakesWeight = 0.25f;

    // Parts on their way that have not got any further for this long are not waited on any more
    private const int MotionPatienceMs = 1500;

    /// <summary>
    /// How quickly the camera settles on a new framing, as the share of the remaining distance it covers per
    /// second. The first car of a session is put in place at once; after that the camera moves rather than
    /// cuts, so changing car or opening the workbench reads as the camera going somewhere.
    /// </summary>
    private const float CameraSettleRate = 2.6f;
    private const float CameraSettled = 0.004f;

    // Keep drawing for a while after a toggle so door/light animations play out
    private static readonly TimeSpan AnimationWindow = TimeSpan.FromSeconds(3);

    // Where the camera is headed, eased toward each frame
    private float _radiusGoal = DefaultRadius;
    private float _alphaGoal = DefaultAlpha;
    private float _betaGoal = DefaultBeta;
    private SlimDX.Vector3? _targetGoal;

    // Back from the stand the camera glides to the car itself; the renderer's own framing, which jumps a third of the
    // way every frame, takes over once it is there
    private bool _autoAdjustWhenSettled;

    // Off the stand: the camera holds on it until the car's parts say where it goes next
    private bool _cameraWaitsOnStand;

    // The camera goes after the engine on the hoist
    private bool _followCarryValue;

    private bool _followCarry
    {
        get => _followCarryValue;
        set
        {
            _followCarryValue = value;
            UpdateMovingEngine();
        }
    }

    // The stand's engine is on its way there, on the hoist or down from above: the stand is shown without it
    private bool _standKeptEmpty;

    // The engine that has just left the stand for the car, and where it was on the stand: it goes over on the hoist
    private (Guid Id, SlimDX.Matrix From)? _carryFromStandValue;

    private (Guid Id, SlimDX.Matrix From)? _carryFromStand
    {
        get => _carryFromStandValue;
        set
        {
            _carryFromStandValue = value;
            UpdateMovingEngine();
        }
    }

    // The engine on the stand turns back upright before it leaves the stand: the garage waits with it
    private bool _turningBackValue;

    private bool _turningBack
    {
        get => _turningBackValue;
        set
        {
            _turningBackValue = value;
            UpdateMovingEngine();
        }
    }

    private void UpdateMovingEngine() => IsMovingEngine = _followCarry || _carryFromStand != null || _turningBack;

    private string? _loadedCarDirectory;
    private System.Windows.Point _lastMouse;
    private bool _isDragging;
    private bool _isLoadingParts;
    private bool _applyPartsQueued;
    private string? _partsCarDirectory;
    private InstalledPart? _partsEngine;
    private IReadOnlyList<CarPart>? _partsGear;
    private EngineLayout _partsLayout;
    private bool _partsOnStand;
    private bool _applySceneryQueued;
    private bool _isLoadingScenery;
    private GarageRenderer? _sceneryRenderer;
    private (InstalledPart? Engine, bool WithEngine, PartsCatalog? Catalog)? _sceneryKey;

    // Counts the sceneries put up: a scenery built from what was bound before the parts view put up one of its own is
    // out of date by the time it is ready
    private int _sceneryVersion;
    private bool _isLoadingCandidates;

    // What the part models on screen are made of: node index to part, and where every part sits
    private IReadOnlyList<PlacedPart> _partNodes = Array.Empty<PlacedPart>();
    private Dictionary<InstalledPart, System.Numerics.Matrix4x4> _partWorlds = new();
    private IReadOnlyList<MountCandidate> _candidateNodes = Array.Empty<MountCandidate>();
    private IReadOnlyList<MountCandidate>? _shownCandidates;
    private CarAnchors? _anchors;

    // A press that does not turn into a drag is a click
    private const double ClickSlack = 4.0;
    private static readonly TimeSpan HoverInterval = TimeSpan.FromMilliseconds(30);
    private System.Windows.Point _pressedAt;
    private DateTime _lastHoverPick = DateTime.MinValue;

    public CarViewport3D()
        : base("Viewport3D", new LabelLook(13, 0xD0, 0x80, new Thickness(8, 4, 8, 4), new Vector(18, 14)))
    {
    }

    protected override string ViewportName => "3D viewport";

    /// <summary>A mounted part was clicked in the parts view; null for a click on nothing</summary>
    public event Action<InstalledPart?>? PartClicked;

    /// <summary>One of the places shown for a loose part was clicked</summary>
    public event Action<MountCandidate>? CandidateClicked;

    /// <summary>The engine stand beside the car, or the engine on it, was clicked while the stand is not being worked on</summary>
    public event Action? StandClicked;

    /// <summary>The car was clicked while the stand is being worked on</summary>
    public event Action? CarClicked;

    /// <summary>The engine pulled out of the car is down on the stand: the work goes on there</summary>
    public event Action? EngineOnStand;

    /// <summary>
    /// Fade the viewport in by itself when the first frame is ready.
    /// Turn off when the host reveals the whole screen at once.
    /// </summary>
    public bool FadeInOnReady { get; set; } = true;

    #region Dependency properties

    public static readonly DependencyProperty CarDirectoryProperty = DependencyProperty.Register(
        nameof(CarDirectory), typeof(string), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).RequestLoad()));

    /// <summary>Full path to the AC car folder (content/cars/{id})</summary>
    public string? CarDirectory
    {
        get => (string?)GetValue(CarDirectoryProperty);
        set => SetValue(CarDirectoryProperty, value);
    }

    public static readonly DependencyProperty SkinIdProperty = DependencyProperty.Register(
        nameof(SkinId), typeof(string), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).ApplySkin()));

    public string? SkinId
    {
        get => (string?)GetValue(SkinIdProperty);
        set => SetValue(SkinIdProperty, value);
    }

    public static readonly DependencyProperty ShowroomKn5Property = DependencyProperty.Register(
        nameof(ShowroomKn5), typeof(string), typeof(CarViewport3D), new PropertyMetadata(null));

    /// <summary>Full path to the showroom .kn5 used as the garage environment (optional)</summary>
    public string? ShowroomKn5
    {
        get => (string?)GetValue(ShowroomKn5Property);
        set => SetValue(ShowroomKn5Property, value);
    }

    public static readonly DependencyProperty HeadlightsOnProperty = DependencyProperty.Register(
        nameof(HeadlightsOn), typeof(bool), typeof(CarViewport3D),
        new PropertyMetadata(false, (d, _) => ((CarViewport3D)d).ApplyCarState()));

    public bool HeadlightsOn
    {
        get => (bool)GetValue(HeadlightsOnProperty);
        set => SetValue(HeadlightsOnProperty, value);
    }

    public static readonly DependencyProperty DoorsOpenProperty = DependencyProperty.Register(
        nameof(DoorsOpen), typeof(bool), typeof(CarViewport3D),
        new PropertyMetadata(false, (d, _) => ((CarViewport3D)d).ApplyCarState()));

    public bool DoorsOpen
    {
        get => (bool)GetValue(DoorsOpenProperty);
        set => SetValue(DoorsOpenProperty, value);
    }

    public static readonly DependencyProperty AutoRotateProperty = DependencyProperty.Register(
        nameof(AutoRotate), typeof(bool), typeof(CarViewport3D),
        new PropertyMetadata(false, (d, _) => ((CarViewport3D)d).ApplyCarState()));

    public bool AutoRotate
    {
        get => (bool)GetValue(AutoRotateProperty);
        set => SetValue(AutoRotateProperty, value);
    }

    public static readonly DependencyProperty PartsVisibleProperty = DependencyProperty.Register(
        nameof(PartsVisible), typeof(bool), typeof(CarViewport3D),
        new PropertyMetadata(false, (d, _) =>
        {
            var viewport = (CarViewport3D)d;
            if (viewport.StandMode) viewport.ResetCamera();
            viewport.QueueApplyParts();
            viewport.QueueApplyScenery();
            viewport.ApplyShown();
        }));

    /// <summary>X-ray view: the car fades to a shell and its parts show in their places</summary>
    public bool PartsVisible
    {
        get => (bool)GetValue(PartsVisibleProperty);
        set => SetValue(PartsVisibleProperty, value);
    }

    public static readonly DependencyProperty PartShowsProperty = DependencyProperty.Register(
        nameof(PartShows), typeof(IReadOnlyList<PartShowToggle>), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, e) => ((CarViewport3D)d).OnPartShowsChanged(e.OldValue as IReadOnlyList<PartShowToggle>, e.NewValue as IReadOnlyList<PartShowToggle>)));

    /// <summary>
    /// The parts view's buttons that put kinds of part out of sight: the parts of a kind whose button is off fade out
    /// of the car, and back in when it is on again. On the stand everything stays in sight.
    /// </summary>
    public IReadOnlyList<PartShowToggle>? PartShows
    {
        get => (IReadOnlyList<PartShowToggle>?)GetValue(PartShowsProperty);
        set => SetValue(PartShowsProperty, value);
    }

    private void OnPartShowsChanged(IReadOnlyList<PartShowToggle>? before, IReadOnlyList<PartShowToggle>? after)
    {
        foreach (var toggle in before ?? []) toggle.PropertyChanged -= OnPartShowToggled;
        foreach (var toggle in after ?? []) toggle.PropertyChanged += OnPartShowToggled;
        ApplyHiddenParts(fade: true);
    }

    private void OnPartShowToggled(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PartShowToggle.IsShown) || _revealingShows) return;
        ApplyHiddenParts(fade: true);
        AnimateFor(AnimationWindow);
    }

    // Set while the viewport itself turns a button back on (a part of a hidden kind just went on): no second pass
    private bool _revealingShows;

    /// <summary>The kinds whose button is off, out of sight in the car's parts view; none on the stand</summary>
    private void ApplyHiddenParts(bool fade)
    {
        if (Renderer is not { HasProp: true } renderer) return;

        var hidden = (PartShows ?? []).Where(t => !t.IsShown).Select(t => t.Show).ToHashSet();
        var nodes = new List<int>();
        if (hidden.Count > 0 && PartsVisible && !IsOnStand)
        {
            var engine = EngineParts();
            for (var i = 0; i < _partNodes.Count; i++)
            {
                if (ShowOf(_partNodes[i], engine) is { } show && hidden.Contains(show)) nodes.Add(i);
            }
        }

        renderer.SetHiddenParts(nodes, fade);
    }

    /// <summary>The parts of the engine laid out in the car</summary>
    private HashSet<InstalledPart> EngineParts() => _partsEngine?.SelfAndDescendants().ToHashSet() ?? [];

    /// <summary>The show a laid-out part goes with: by its kind, or as part of the engine</summary>
    private static PartShow? ShowOf(PlacedPart placed, HashSet<InstalledPart> engine) =>
        PartGroups.Current.ShowOf(PartKinds.GroupOf(placed.Part), placed.Source is { } source && engine.Contains(source));

    /// <summary>A part just put on whose kind is out of sight: its button comes back on, so the player sees what they did</summary>
    private void RevealShowsOf(IEnumerable<int> arrived)
    {
        if (PartShows is not { Count: > 0 } toggles) return;

        _revealingShows = true;
        try
        {
            var engine = EngineParts();
            foreach (var show in arrived.Where(n => n >= 0 && n < _partNodes.Count).Select(n => ShowOf(_partNodes[n], engine)).OfType<PartShow>().Distinct())
            {
                if (toggles.FirstOrDefault(t => ReferenceEquals(t.Show, show)) is { IsShown: false } toggle) toggle.IsShown = true;
            }
        }
        finally
        {
            _revealingShows = false;
        }
    }

    public static readonly DependencyProperty StandModeProperty = DependencyProperty.Register(
        nameof(StandMode), typeof(bool), typeof(CarViewport3D),
        new PropertyMetadata(false, (d, _) => ((CarViewport3D)d).OnStandModeChanged()));

    /// <summary>
    /// With <see cref="PartsVisible"/>: the work is on the engine stand beside the car. The car stays as it is, the
    /// camera goes over to the stand, and <see cref="Engine"/> (the one on the stand) is picked and mounted on there.
    /// </summary>
    public bool StandMode
    {
        get => (bool)GetValue(StandModeProperty);
        set => SetValue(StandModeProperty, value);
    }

    private bool IsOnStand => PartsVisible && StandMode;

    private void OnStandModeChanged()
    {
        // Off the stand with its engine in hand: the camera stays until it is known whether that engine is going in
        // the car, and goes after it on the hoist then, not to the car and back (LeaveStandAsync)
        if (!StandMode && _partsOnStand) _cameraWaitsOnStand = true;
        else ResetCamera();
        QueueApplyParts();
        QueueApplyScenery();
    }

    public static readonly DependencyProperty StandEngineProperty = DependencyProperty.Register(
        nameof(StandEngine), typeof(InstalledPart), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).QueueApplyScenery()));

    /// <summary>
    /// The engine on the stand beside the car, with everything on it; null for an empty stand. The stand is always in
    /// the garage while there is a <see cref="PartsCatalog"/>. A tree of the view's own, as <see cref="Engine"/> is.
    /// </summary>
    public InstalledPart? StandEngine
    {
        get => (InstalledPart?)GetValue(StandEngineProperty);
        set => SetValue(StandEngineProperty, value);
    }

    public static readonly DependencyProperty EngineTurnedOverProperty = DependencyProperty.Register(
        nameof(EngineTurnedOver), typeof(bool), typeof(CarViewport3D),
        new PropertyMetadata(false, (d, _) => ((CarViewport3D)d).OnEngineTurnedOverChanged()));

    /// <summary>
    /// The engine on the stand is upside down, to get at its crankshaft and oil pan: it turns over on the stand's
    /// spindle, whatever is on it with it. Only while the stand is worked on; it turns back before it leaves the stand.
    /// </summary>
    public bool EngineTurnedOver
    {
        get => (bool)GetValue(EngineTurnedOverProperty);
        set => SetValue(EngineTurnedOverProperty, value);
    }

    private void OnEngineTurnedOverChanged()
    {
        if (Renderer is not { } renderer) return;

        // Only the stand being worked on turns it: the renderer holds to that
        renderer.EngineTurnedOver = EngineTurnedOver;
        AnimateFor(AnimationWindow);
    }

    public static readonly DependencyProperty LiftStopProperty = DependencyProperty.Register(
        nameof(LiftStop), typeof(Models.GameState.LiftStop), typeof(CarViewport3D),
        new PropertyMetadata(Models.GameState.LiftStop.Down, (d, _) => ((CarViewport3D)d).OnLiftStopChanged()));

    /// <summary>
    /// Where the garage's lift has the car (<see cref="Models.GameState.Car.LiftStop"/>): it goes there over a few seconds,
    /// the whole car with its parts, the lift's post rising under it; a car loaded comes at its stop at once
    /// </summary>
    public Models.GameState.LiftStop LiftStop
    {
        get => (Models.GameState.LiftStop)GetValue(LiftStopProperty);
        set => SetValue(LiftStopProperty, value);
    }

    private static readonly DependencyPropertyKey IsLiftMovingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsLiftMoving), typeof(bool), typeof(CarViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty IsLiftMovingProperty = IsLiftMovingPropertyKey.DependencyProperty;

    /// <summary>True while the lift is on its way to <see cref="LiftStop"/>: nothing is to be done to the car in the meantime</summary>
    public bool IsLiftMoving
    {
        get => (bool)GetValue(IsLiftMovingProperty);
        private set => SetValue(IsLiftMovingPropertyKey, value);
    }

    private void OnLiftStopChanged()
    {
        // A car on its way in comes at its stop as it is loaded (LoadAsync); the one going stays as it was
        if (Renderer?.CarNode == null || RequestedCarDirectory != _loadedCarDirectory) return;
        if (_liftNow == LiftGoal) return;

        _liftHum ??= Audio.AudioHub.Shared.PlayLoop(Audio.Sfx.LiftMoves);
        IsLiftMoving = true;
        AnimateFor(AnimationWindow);
    }

    public static readonly DependencyProperty PartsCatalogProperty = DependencyProperty.Register(
        nameof(PartsCatalog), typeof(PartsCatalog), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) =>
        {
            var viewport = (CarViewport3D)d;
            viewport.QueueApplyParts();
            viewport.QueueApplyScenery();
        }));

    /// <summary>Where the part models come from; without it the parts view stays off</summary>
    public PartsCatalog? PartsCatalog
    {
        get => (PartsCatalog?)GetValue(PartsCatalogProperty);
        set => SetValue(PartsCatalogProperty, value);
    }

    public static readonly DependencyProperty EngineProperty = DependencyProperty.Register(
        nameof(Engine), typeof(InstalledPart), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).QueueApplyParts()));

    /// <summary>
    /// The car's engine block with everything that is on it; null for an empty engine bay.
    /// The tree is drawn as it is when set: after a change, set a new tree. It is the view's own from then on and is
    /// walked on worker threads while the models are built, so nothing may change it once set (the workbench makes a
    /// new tree out of the saved parts for every change: PartTrees.ToInstalled). The same goes for <see cref="Gear"/>,
    /// <see cref="StandEngine"/> and <see cref="Candidates"/>.
    /// </summary>
    public InstalledPart? Engine
    {
        get => (InstalledPart?)GetValue(EngineProperty);
        set => SetValue(EngineProperty, value);
    }

    public static readonly DependencyProperty GearProperty = DependencyProperty.Register(
        nameof(Gear), typeof(IReadOnlyList<CarPart>), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).QueueApplyParts()));

    /// <summary>What sits on the car's wheel slots, each with what is on it; null or empty for bare hubs. Trees of the view's own, as <see cref="Engine"/> is.</summary>
    public IReadOnlyList<CarPart>? Gear
    {
        get => (IReadOnlyList<CarPart>?)GetValue(GearProperty);
        set => SetValue(GearProperty, value);
    }

    public static readonly DependencyProperty EngineOffsetProperty = DependencyProperty.Register(
        nameof(EngineOffset), typeof(System.Numerics.Vector3), typeof(CarViewport3D),
        new PropertyMetadata(System.Numerics.Vector3.Zero, (d, _) => ((CarViewport3D)d).OnEngineOffsetChanged()));

    /// <summary>Where the car's engine is moved from its place by hand (cars.json): the parts are laid out with it</summary>
    public System.Numerics.Vector3 EngineOffset
    {
        get => (System.Numerics.Vector3)GetValue(EngineOffsetProperty);
        set => SetValue(EngineOffsetProperty, value);
    }

    /// <summary>The placement mode moved the engine block to this offset: for the screen to keep (cars.json)</summary>
    public event Action<System.Numerics.Vector3>? EngineMoved;

    private void OnEngineOffsetChanged()
    {
        if (_anchors is { } anchors) _anchors = anchors with { EngineOffset = EngineOffset };
        Relayout();
        ReportPlacement();
    }

    public static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        nameof(Shown), typeof(ShownSet), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).ApplyShown()));

    /// <summary>
    /// What the car shows that its own model does not have (<see cref="ShownPartsOnCar"/>): its wheels, and the engine
    /// parts that stand out except in the parts view, which draws the whole engine
    /// </summary>
    public ShownSet? Shown
    {
        get => (ShownSet?)GetValue(ShownProperty);
        set => SetValue(ShownProperty, value);
    }

    public static readonly DependencyProperty EngineLayoutProperty = DependencyProperty.Register(
        nameof(EngineLayout), typeof(EngineLayout), typeof(CarViewport3D),
        new PropertyMetadata(EngineLayout.Front, (d, _) => ((CarViewport3D)d).QueueApplyParts()));

    /// <summary>Where the car carries its engine: where <see cref="Engine"/> and the candidates for its place are drawn</summary>
    public EngineLayout EngineLayout
    {
        get => (EngineLayout)GetValue(EngineLayoutProperty);
        set => SetValue(EngineLayoutProperty, value);
    }

    public static readonly DependencyProperty CandidatesProperty = DependencyProperty.Register(
        nameof(Candidates), typeof(IReadOnlyList<MountCandidate>), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).ApplyCandidates()));

    /// <summary>
    /// A loose part in every place it could go, shown glowing; a click on one raises <see cref="CandidateClicked"/>.
    /// A new list of trees of the view's own for every change, as <see cref="Engine"/> is.
    /// </summary>
    public IReadOnlyList<MountCandidate>? Candidates
    {
        get => (IReadOnlyList<MountCandidate>?)GetValue(CandidatesProperty);
        set => SetValue(CandidatesProperty, value);
    }

    public static readonly DependencyProperty SelectedPartProperty = DependencyProperty.Register(
        nameof(SelectedPart), typeof(InstalledPart), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) =>
        {
            var viewport = (CarViewport3D)d;
            viewport.ApplySelection();
            viewport.FollowSelection();
        }));

    /// <summary>The mounted part that is marked as selected</summary>
    public InstalledPart? SelectedPart
    {
        get => (InstalledPart?)GetValue(SelectedPartProperty);
        set => SetValue(SelectedPartProperty, value);
    }

    public static readonly DependencyProperty WorkPieceProperty = DependencyProperty.Register(
        nameof(WorkPiece), typeof(WorkPiece), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, e) => ((CarViewport3D)d).OnWorkPieceChanged((WorkPiece?)e.NewValue)));

    /// <summary>
    /// The part a job of the parts view's "Work on it" is being done on: a loose one comes down onto the workbench and
    /// turns there, the camera goes to a mounted one where it is; its grime is drawn over it at <see cref="WorkGrime"/>.
    /// Null when no job is on: the part goes, the camera comes back.
    /// </summary>
    public WorkPiece? WorkPiece
    {
        get => (WorkPiece?)GetValue(WorkPieceProperty);
        set => SetValue(WorkPieceProperty, value);
    }

    public static readonly DependencyProperty WorkGrimeProperty = DependencyProperty.Register(
        nameof(WorkGrime), typeof(double), typeof(CarViewport3D),
        new PropertyMetadata(0.0, (d, _) => ((CarViewport3D)d).ApplyWorkGrime()));

    /// <summary>How much grime the part being worked on still carries, 0 clean to 1</summary>
    public double WorkGrime
    {
        get => (double)GetValue(WorkGrimeProperty);
        set => SetValue(WorkGrimeProperty, value);
    }

    // Coming down onto the bench from this high, over this long, at how far in sight; turning slowly while it is there
    private const float WorkDrop = 0.45f;
    private const float WorkArriveSeconds = 0.8f;
    private const float WorkLeaveSeconds = 0.5f;
    private const float WorkTurnRate = 0.22f;

    // The camera on the bench (from in front of it and the stand's side), and on a mounted part
    private const float BenchRadius = 1.7f;
    private const float BenchAlpha = 0.9f;
    private const float BenchBeta = 0.42f;
    private const float WorkRadius = 1.5f;
    private const float WorkBeta = 0.35f;

    // The loose part on the bench: its model's box, how long it has been there, when it began to go and how far in
    // sight and above the bench it was then, and the version of the job it belongs to (a new job, or none, while one
    // is still being built, leaves the old one out)
    private System.Numerics.Vector3 _workMin;
    private System.Numerics.Vector3 _workMax;
    private float _workTime;
    private float _workLeftAt;
    private float _workLeaveOpacity;
    private float _workLeaveLift;
    private bool _workOnBench;
    private bool _workLeaving;
    private int _workVersion;

    // The camera went to the work: it comes back to the car, or the stand, when the work is done
    private bool _cameraOnWork;

    private void OnWorkPieceChanged(WorkPiece? piece)
    {
        var version = ++_workVersion;
        if (piece == null)
        {
            if (_workOnBench && !_workLeaving && Renderer != null)
            {
                // Goes from where it is and as it looks: a part still coming down fades out where it is
                _workLeaveLift = WorkLift();
                _workLeaveOpacity = Renderer.WorkOpacity;
                _workLeftAt = _workTime;
                _workLeaving = true;
                AnimateFor(TimeSpan.FromSeconds(WorkLeaveSeconds) + AnimationWindow);
            }
            else if (!_workOnBench) Renderer?.SetWork(null, null, SlimDX.Matrix.Identity, false);

            // Whether or not the parts are still on show: the view may have closed as the work ended
            if (Renderer != null && _cameraOnWork) ResetCamera();
            _cameraOnWork = false;
            return;
        }

        ApplyWorkAsync(piece, version);
    }

    private async void ApplyWorkAsync(WorkPiece piece, int version)
    {
        var renderer = Renderer;
        var catalog = PartsCatalog;
        if (renderer == null || catalog == null) return;

        try
        {
            if (piece.OnBench)
            {
                // A garage without a workbench shows the job without the part
                if (_bench is not { } bench) return;

                // Built where it lies: its box puts it on the bench's top, whatever its own origin
                var built = await Task.Run(() =>
                {
                    var placed = PartAssembler.Assemble(piece.Part, System.Numerics.Matrix4x4.Identity);
                    var clean = PartAssembler.BuildModel(catalog, "work", placed);
                    return (Clean: clean, Grime: Looks.PartGrime.Model(catalog, placed));
                });
                if (version != _workVersion || renderer != Renderer) return;

                _workMin = built.Clean.Min;
                _workMax = built.Clean.Max;
                _workOnBench = true;
                _workLeaving = false;
                _workTime = 0f;
                renderer.SetWork(built.Clean.Kn5, built.Grime.Kn5, BenchPlacement(), false);
                renderer.WorkOpacity = 0f;
                ApplyWorkGrime();

                renderer.AutoAdjustTarget = false;
                _autoAdjustWhenSettled = false;
                _cameraOnWork = true;
                var top = WorkbenchLayout.TopOf(bench.Placement);
                _targetGoal = new SlimDX.Vector3(top.X, top.Y + Math.Min(0.25f, (_workMax.Y - _workMin.Y) / 2f), top.Z);
                _radiusGoal = BenchRadius;
                _alphaGoal = BenchAlpha - bench.Heading * MathF.PI / 180f;
                _betaGoal = BenchBeta;
                AnimateFor(TimeSpan.FromSeconds(WorkArriveSeconds) + AnimationWindow);
                return;
            }

            // A mounted part: once the parts are built again after the job (queued to the dispatcher by the new engine
            // and gear, then building), its place among them, and its grime there
            for (var waited = 0; (_applyPartsQueued || _isLoadingParts) && waited < 3000 && version == _workVersion; waited += 30) await Task.Delay(30);
            if (version != _workVersion || renderer != Renderer) return;

            var mounted = _partWorlds.FirstOrDefault(p => piece.Part.InstanceId != Guid.Empty && p.Key.InstanceId == piece.Part.InstanceId);
            if (mounted.Key == null) return;

            var grimeModel = await Task.Run(() => Looks.PartGrime.Model(catalog, [new PlacedPart(mounted.Key.Definition, mounted.Value)]));
            if (version != _workVersion || renderer != Renderer) return;

            _workOnBench = false;
            renderer.SetWork(null, grimeModel.Kn5, SlimDX.Matrix.Identity, true);
            ApplyWorkGrime();

            if (renderer.WorkCentre is { } centre)
            {
                renderer.AutoAdjustTarget = false;
                _autoAdjustWhenSettled = false;
                _cameraOnWork = true;
                _targetGoal = centre;
                _radiusGoal = WorkRadius;
                _betaGoal = WorkBeta;
            }

            AnimateFor(AnimationWindow);
        }
        catch (Exception ex)
        {
            // Only the showing: the job is done whatever the view makes of it
            Logger.Warning("Could not show the part being worked on: {Error}", ex.Message);
        }
    }

    private void ApplyWorkGrime()
    {
        if (Renderer is not { } renderer) return;

        renderer.WorkGrime = (float)Math.Clamp(WorkGrime, 0, 1);
        Invalidate();
    }

    /// <summary>How high above the bench the loose part is now: coming down, or where it was when it began to go</summary>
    private float WorkLift() =>
        _workLeaving ? _workLeaveLift : WorkDrop * (1f - EaseWork(Math.Clamp(_workTime / WorkArriveSeconds, 0f, 1f)));

    /// <summary>The loose part on the bench as it is now: coming down, turning</summary>
    private SlimDX.Matrix BenchPlacement() =>
        _bench is { } bench
            ? ToSlim(WorkbenchLayout.PlacementOf(bench.Placement, _workMin, _workMax, _workTime * WorkTurnRate, WorkLift()))
            : SlimDX.Matrix.Identity;

    private static float EaseWork(float t) => t * t * (3f - 2f * t);

    /// <summary>The loose part on the bench, a frame on; true while it moves</summary>
    private bool StepWork(GarageRenderer renderer, float dt)
    {
        if (!_workOnBench) return false;

        _workTime += dt;
        if (_workLeaving)
        {
            var gone = Math.Clamp((_workTime - _workLeftAt) / WorkLeaveSeconds, 0f, 1f);
            renderer.WorkOpacity = _workLeaveOpacity * (1f - gone);
            renderer.MoveWork(BenchPlacement());
            if (gone < 1f) return true;

            renderer.SetWork(null, null, SlimDX.Matrix.Identity, false);
            renderer.WorkOpacity = 1f;
            _workOnBench = false;
            _workLeaving = false;
            return false;
        }

        renderer.WorkOpacity = EaseWork(Math.Clamp(_workTime / (WorkArriveSeconds * 0.6f), 0f, 1f));
        renderer.MoveWork(BenchPlacement());
        return true;
    }

    private static readonly DependencyPropertyKey HasCarPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasCar), typeof(bool), typeof(CarViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty HasCarProperty = HasCarPropertyKey.DependencyProperty;

    /// <summary>True if a car model is loaded (false when only the empty showroom is shown)</summary>
    public bool HasCar
    {
        get => (bool)GetValue(HasCarProperty);
        private set => SetValue(HasCarPropertyKey, value);
    }

    private static readonly DependencyPropertyKey HasDoorsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasDoors), typeof(bool), typeof(CarViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty HasDoorsProperty = HasDoorsPropertyKey.DependencyProperty;

    /// <summary>True if the loaded car model has door animations</summary>
    public bool HasDoors
    {
        get => (bool)GetValue(HasDoorsProperty);
        private set => SetValue(HasDoorsPropertyKey, value);
    }

    private static readonly DependencyPropertyKey IsMovingEnginePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsMovingEngine), typeof(bool), typeof(CarViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty IsMovingEngineProperty = IsMovingEnginePropertyKey.DependencyProperty;

    /// <summary>
    /// True while an engine goes between the car and the stand: from the first part that comes off for it until it is
    /// down. Nothing is to be done in the garage in the meantime.
    /// </summary>
    public bool IsMovingEngine
    {
        get => (bool)GetValue(IsMovingEngineProperty);
        private set => SetValue(IsMovingEnginePropertyKey, value);
    }

    #endregion

    #region Loading

    // An empty directory means "no car": only the showroom is rendered
    private string RequestedCarDirectory => CarDirectory ?? string.Empty;

    protected override bool IsUpToDate => Renderer != null && RequestedCarDirectory == _loadedCarDirectory;

    protected override string LoadDescription => RequestedCarDirectory;

    protected override async Task LoadAsync()
    {
        var carDirectory = RequestedCarDirectory;
        var hasCar = carDirectory.Length > 0;
        if (hasCar && !Directory.Exists(carDirectory))
            throw new DirectoryNotFoundException($"Car folder not found: {carDirectory}");

        var started = DateTime.Now;
        var car = hasCar ? CarDescription.FromDirectory(carDirectory) : null;
        var skinId = SkinId;

        // The car's wear is made while its model loads, so it comes with the car
        if (hasCar) _ = Looks.WearLayer.WarmAsync(carDirectory, Shown);

        var renderer = Renderer;
        if (renderer == null)
        {
            var showroom = ShowroomKn5;
            if (showroom != null && !File.Exists(showroom))
            {
                Logger.Warning("Showroom not found, rendering without it: {Showroom}", showroom);
                showroom = null;
            }

            if (car == null && showroom == null)
                throw new InvalidOperationException("Nothing to render: no car and no showroom");

            renderer = NewRenderer(car, showroom);
            if (!await StartRendererAsync(renderer)) return;

            ResetCamera(immediate: true);
        }
        else
        {
            // The car going cannot be left on the move, nor the camera held off it for the next one
            StopScript();
            ReleaseRock();
            await TrackDeviceWork(renderer.MainSlot.SetCarAsync(car, skinId ?? Kn5RenderableCar.DefaultSkin, RendererToken));

            // The control may have been unloaded while the car was loading, and the renderer is gone then
            if (renderer != Renderer) return;

            ResetCamera();
        }

        _loadedCarDirectory = carDirectory;
        HasCar = renderer.CarNode != null;
        HasDoors = renderer.CarNode?.HasLeftDoorAnimation == true || renderer.CarNode?.HasRightDoorAnimation == true;
        ApplySkin();
        ApplyCarState();
        ApplyParts();
        ApplyShown();
        ApplyScenery();
        ApplyRadio();
        ApplyBench();
        ApplyLift();
        SnapLift();
        Invalidate();

        Logger.Information("Loaded {Car} in {Ms} ms", hasCar ? Path.GetFileName(carDirectory) : "(empty garage)",
            (int)(DateTime.Now - started).TotalMilliseconds);
    }

    /// <summary>
    /// Frames the car. <paramref name="immediate"/> puts the camera there at once, which is right for the
    /// first car of a session; otherwise it moves there over the next moment.
    /// </summary>
    private void ResetCamera(bool immediate = false)
    {
        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        _radiusGoal = DefaultRadius;
        _alphaGoal = DefaultAlpha;
        _betaGoal = DefaultBeta;
        _targetGoal = null;
        _autoAdjustWhenSettled = false;
        _followCarry = false;
        _cameraWaitsOnStand = false;

        if (IsOnStand)
        {
            // Close in on the engine on the stand, a little above the crankshaft
            Renderer!.AutoAdjustTarget = false;
            var spot = EngineStandLayout.Spot;
            _targetGoal = new SlimDX.Vector3(spot.X, EngineStandLayout.CrankHeight - 0.05f, spot.Z - 0.1f);
            _radiusGoal = StandRadius;
            _alphaGoal = StandAlpha;
            _betaGoal = StandBeta;
        }
        else if (Renderer!.CarNode == null)
        {
            // Empty garage: nothing to frame, so look across the room at eye level instead of at the floor
            Renderer.AutoAdjustTarget = false;
            _targetGoal = new SlimDX.Vector3(0f, EmptyGarageEyeHeight, 0f);
            _radiusGoal = MaxRadius;
            _betaGoal = MinBeta;
        }
        else if (!immediate && !Renderer.AutoAdjustTarget && Renderer.MainSlot.GetCarBoundingBox() is { } box)
        {
            // Coming back from the stand, or from an empty garage
            _targetGoal = Renderer.FramedTarget(_alphaGoal, _betaGoal, _radiusGoal, (box.Minimum + box.Maximum) / 2f);
            _autoAdjustWhenSettled = true;
        }
        else
        {
            Renderer.AutoAdjustTarget = true;
        }

        if (immediate)
        {
            orbit.Radius = _radiusGoal;
            orbit.Alpha = _alphaGoal;
            orbit.Beta = _betaGoal;
            if (_targetGoal is { } target) orbit.Target = target;
        }

        AnimateFor(AnimationWindow);
    }

    /// <summary>
    /// Eases the camera toward its framing. Returns true while it is still moving, so the render loop knows
    /// to keep drawing. A share of what is left every second, so it comes out the same at any frame rate.
    /// </summary>
    private bool StepCamera(float dt)
    {
        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return false;

        var k = 1f - (float)Math.Exp(-CameraSettleRate * dt);
        var moving = false;

        if (Math.Abs(_radiusGoal - orbit.Radius) > CameraSettled)
        {
            orbit.Radius += (_radiusGoal - orbit.Radius) * k;
            moving = true;
        }

        if (Math.Abs(_betaGoal - orbit.Beta) > CameraSettled)
        {
            orbit.Beta += (_betaGoal - orbit.Beta) * k;
            moving = true;
        }

        if (Renderer!.AutoRotate)
        {
            // The renderer is turning the car itself, a little further every tick. Easing alpha back to a
            // goal at the same time would cancel that out and leave the car shivering on the spot instead of
            // going round, so the goal follows the renderer until auto-rotate is switched off.
            _alphaGoal = orbit.Alpha;
        }
        else
        {
            // Round the short way
            var delta = (float)Math.IEEERemainder(_alphaGoal - orbit.Alpha, Math.PI * 2);
            if (Math.Abs(delta) > CameraSettled)
            {
                orbit.Alpha += delta * k;
                moving = true;
            }
        }

        if (_targetGoal is { } target)
        {
            var toTarget = target - orbit.Target;
            if (toTarget.LengthSquared() > CameraSettled * CameraSettled)
            {
                orbit.Target += toTarget * k;
                moving = true;
            }
            else if (_autoAdjustWhenSettled && !moving)
            {
                _autoAdjustWhenSettled = false;
                _targetGoal = null;
                Renderer.AutoAdjustTarget = true;
            }
        }

        return moving;
    }

    /// <summary>
    /// The bindings land one at a time (the catalog, then the engine, then the gear, each a change of its own),
    /// so the layout waits for the lot and is built once
    /// </summary>
    private void QueueApplyParts()
    {
        if (_applyPartsQueued) return;

        _applyPartsQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _applyPartsQueued = false;
            ApplyParts();
        });
    }

    /// <summary>
    /// Builds the parts view for what is bound now. Runs from property callbacks, so nothing may escape it: a throw
    /// anywhere in it is logged and the garage goes on without the parts view.
    /// </summary>
    private async void ApplyParts()
    {
        try
        {
            await ApplyPartsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not lay out the parts");
        }
    }

    private async Task ApplyPartsAsync()
    {
        var renderer = Renderer;
        if (renderer == null || _isLoadingParts) return;

        var carDirectory = _loadedCarDirectory;
        var carNode = renderer.CarNode;
        var catalog = PartsCatalog;
        if (IsOnStand && catalog != null)
        {
            _carryFromStand = null;
            await ApplyStandAsync(renderer, catalog);
            return;
        }

        // Off the stand, whatever comes next: the engine on it goes back into the scenery first
        if (_partsOnStand)
        {
            await LeaveStandAsync(renderer);
            if (Renderer != null) ApplyParts();
            return;
        }

        if (!PartsVisible || carNode == null || catalog == null || string.IsNullOrEmpty(carDirectory))
        {
            _carryFromStand = null;
            if (_cameraWaitsOnStand) ResetCamera();

            // Without a car there is nothing to dissolve back into
            if (carNode == null)
            {
                renderer.GhostCar = false;
                renderer.SetProp(null, SlimDX.Matrix.Identity);
            }
            else
            {
                renderer.HideParts();
            }

            renderer.SetCandidates(null);
            _partsCarDirectory = null;
            _partsEngine = null;
            _partsGear = null;
            ShowPartLabel(null, default);
            AnimateFor(AnimationWindow);
            return;
        }

        // The layout depends on the car and on what is in it, so either changing rebuilds it
        var engine = Engine;
        var gear = Gear;
        var layout = EngineLayout;
        if (renderer.HasProp && _partsCarDirectory == carDirectory && ReferenceEquals(_partsEngine, engine) && ReferenceEquals(_partsGear, gear) && _partsLayout == layout) return;

        // What this run does about an engine going between the car and the stand. The garage is locked from the moment
        // one sets off; whatever way the run ends, the lock is let go of here unless the carry is really under way.
        var keptStandProp = _carryFromStand != null;
        var pulling = false;
        var pulledDown = false;
        var carryingIn = false;
        var propReplaced = false;

        _isLoadingParts = true;
        try
        {
            var anchors = GetAnchors(carNode) is { } hubs ? hubs with { EngineOffset = EngineOffset } : null;
            if (anchors == null)
            {
                Logger.Warning("Car has no wheel nodes to place the parts by: {Car}", carDirectory);
                return;
            }

            var started = DateTime.Now;

            // Same car, other parts: what is no longer there lifts off while the new model is put together
            var sameCar = renderer.HasProp && _partsCarDirectory == carDirectory;
            var before = sameCar ? IdsOf(_partNodes) : null;
            var roots = (gear ?? Array.Empty<CarPart>()).Select(g => g.Root).Concat(engine == null ? Array.Empty<InstalledPart>() : new[] { engine });
            var after = roots.SelectMany(r => r.SelfAndDescendants()).Select(p => p.InstanceId).Where(id => id != Guid.Empty).ToHashSet();
            var leaving = before == null ? new List<int>() : NodesWhere(_partNodes, id => !after.Contains(id));

            // The engine pulled onto the stand: what is in the way of bolting it on comes off first, one part after
            // another, then the hoist lifts the engine out and takes it over. The stand stays empty until it is there.
            var pulled = before != null && _partsEngine is { } pulledEngine && StandEngine is { } standEngine && standEngine.InstanceId == pulledEngine.InstanceId
                         && pulledEngine.InstanceId != Guid.Empty && _partWorlds.TryGetValue(pulledEngine, out var bayWorld)
                ? (Engine: standEngine, Bay: ToSlim(bayWorld), Ids: standEngine.SelfAndDescendants().Select(p => p.InstanceId).ToHashSet())
                : ((InstalledPart Engine, SlimDX.Matrix Bay, HashSet<Guid> Ids)?)null;
            if (pulled is { } pull)
            {
                var strips = MovesOf(catalog, _partNodes, leaving.Where(n => !pull.Ids.Contains(_partNodes[n].Source!.InstanceId)).ToList(), StripStagger);
                renderer.MoveParts(strips, arriving: false, NodeOf(_partNodes, _partsEngine));
                var carriedFor = renderer.CarryParts(NodesWhere(_partNodes, pull.Ids.Contains), pull.Bay, pull.Bay,
                    ToSlim(EngineStandLayout.EnginePlacement(catalog, pull.Engine.Definition)), ClearHeight(renderer), strips.Count * StripStagger);
                _standKeptEmpty = true;
                pulling = true;
                _followCarry = true;
                QueueApplyScenery();
                AnimateFor(TimeSpan.FromSeconds(carriedFor) + AnimationWindow);

                // The car comes up on its springs as the hoist takes the engine's weight off them
                BumpCar(lighter: true, strips.Count * StripStagger + LiftTakesWeight, InFront(_partsLayout));
            }
            else if (leaving.Count > 0)
            {
                renderer.MoveParts(MovesOf(catalog, _partNodes, leaving), arriving: false, NodeOf(_partNodes, _partsEngine));
                AnimateFor(AnimationWindow);
                if (_partsEngine is { InstanceId: var outId } && outId != Guid.Empty && !after.Contains(outId)) BumpCar(lighter: true, atFront: InFront(_partsLayout));
            }

            var (placed, model, landed) = await Task.Run(() =>
            {
                var parts = CarPartsLayout.Build(catalog, anchors, engine, gear, layout);
                return (parts, PartAssembler.BuildModel(catalog, "parts", parts), pulled is { } p ? BuildScenery(catalog, p.Engine, withEngine: true) : null);
            });

            await WaitForMotionsAsync(renderer, () => PartsVisible && !IsOnStand);

            // The renderer or the car may have been replaced, or the toggle flipped back, while loading
            if (renderer == Renderer && PartsVisible && !IsOnStand && _loadedCarDirectory == carDirectory)
            {
                renderer.SetProp(model.Kn5, SlimDX.Matrix.Identity);
                renderer.SetUnsprung(PartLayer.Mounted, NodesHanging(model.Nodes, Unsprung(catalog, gear)));
                propReplaced = true;

                // The engine off the hoist and onto the stand, in the same frame
                if (pulled is { } down && landed != null && ReferenceEquals(StandEngine, down.Engine))
                {
                    _standKeptEmpty = false;
                    PutScenery(renderer, landed, SceneryKey);
                }

                pulledDown = pulling;

                // And what is new comes flying in, or over from the stand on the hoist
                IEnumerable<int> arrived = [];
                if (before != null)
                {
                    arrived = NodesWhere(model.Nodes, id => !before.Contains(id));
                    renderer.MoveParts(MovesOf(catalog, model.Nodes, NodesWhere(model.Nodes, id => !before.Contains(id))), arriving: true, NodeOf(model.Nodes, engine));

                    // An engine put in settles the car on its springs as it gets there
                    if (engine is { InstanceId: var inId } && inId != Guid.Empty && !before.Contains(inId)) BumpCar(lighter: false, GarageRenderer.PartMotionSeconds, InFront(layout));
                }
                else if (_carryFromStand is { } fromStand && engine?.InstanceId == fromStand.Id && placed.FirstOrDefault(p => ReferenceEquals(p.Source, engine)) is { } block)
                {
                    var ids = engine.SelfAndDescendants().Select(p => p.InstanceId).ToHashSet();
                    arrived = NodesWhere(model.Nodes, ids.Contains);
                    var bay = ToSlim(block.World);
                    var landsIn = renderer.CarryParts(NodesWhere(model.Nodes, ids.Contains), bay, fromStand.From, bay, ClearHeight(renderer), 0f);
                    if (landsIn > 0f)
                    {
                        BumpCar(lighter: false, landsIn, InFront(layout));
                        _cameraWaitsOnStand = false;
                        _followCarry = true;
                        carryingIn = true;
                        FollowUntilDown(renderer);
                    }
                }

                renderer.GhostCar = true;
                _partsCarDirectory = carDirectory;
                _partsEngine = engine;
                _partsGear = gear;
                _partsLayout = layout;
                _anchors = anchors;
                _partNodes = model.Nodes;
                _partWorlds = placed.Where(p => p.Source != null).ToDictionary(p => p.Source!, p => p.World);
                RevealShowsOf(arrived);
                ApplyHiddenParts(fade: false);
                AnimateFor(AnimationWindow);

                // The tree may be made anew: placement goes on with the selected part in it
                FollowSelection();

                Logger.Information("Parts of {Car} laid out in {Ms} ms ({Count} parts, engine {Engine})", Path.GetFileName(carDirectory),
                    (int)(DateTime.Now - started).TotalMilliseconds, placed.Count, engine?.Definition.Id ?? "none");

                ApplySelection();
                ApplyCandidates();
            }
        }
        catch (Exception ex)
        {
            // The garage works fine without the parts view, so just leave it off. What is on screen is the layout from
            // before, maybe with an engine on its way out of it: it goes too, and the next change lays the parts out anew.
            Logger.Error(ex, "Could not lay out the parts");
            if (renderer == Renderer)
            {
                renderer.SetProp(null, SlimDX.Matrix.Identity);
                propReplaced = true;
                ForgetParts();
            }

            pulledDown = false;
            carryingIn = false;
        }
        finally
        {
            _isLoadingParts = false;
            _carryFromStand = null;

            // The engine from the stand was kept in sight for a carry that never came: nothing is to show it there now
            if (keptStandProp && !propReplaced && renderer == Renderer && !IsOnStand) renderer.SetProp(null, SlimDX.Matrix.Identity);

            // The engine from the stand did not go over on the hoist after all: the camera goes to the car
            if (_cameraWaitsOnStand) ResetCamera();
            if (_standKeptEmpty)
            {
                // Put down short of the stand: the stand gets its engine back all the same
                _standKeptEmpty = false;
                QueueApplyScenery();
            }
        }

        // Down on the stand: the work goes over there, the camera already is
        if (pulledDown && _partsEngine == null && PartsVisible && !IsOnStand)
        {
            _followCarry = false;
            EngineOnStand?.Invoke();
            if (!IsOnStand) ResetCamera();
        }

        // Anything else that set off with the engine let go of it on the way (the parts view shut, the layout failed,
        // another car): back to the car, and the garage is free again. A carry into the car lets go once it is down.
        else if (pulling && !carryingIn) ResetCamera();

        // Catch up with whatever changed in the meantime, a renderer rebuilt under it included (device lost, or a
        // quick unload and load): its own load found this run in progress and left the parts to it
        if (Renderer != null && (renderer != Renderer || !PartsVisible || IsOnStand || _loadedCarDirectory != carDirectory || !ReferenceEquals(Engine, engine) || !ReferenceEquals(Gear, gear) || EngineLayout != layout)) ApplyParts();

        // The engine moved while this run laid it out where it was: placed again where it is now
        else if (_anchors is { } laidOut && laidOut.EngineOffset != EngineOffset) OnEngineOffsetChanged();
    }

    /// <summary>The parts on screen are none: whatever is bound next is laid out from scratch</summary>
    private void ForgetParts()
    {
        _partsCarDirectory = null;
        _partsEngine = null;
        _partsGear = null;
        _partNodes = Array.Empty<PlacedPart>();
        _partWorlds = new();
        ShowPartLabel(null, default);
        if (_placing != null)
        {
            _placing = null;
            PlacementChanged?.Invoke(null);
        }
    }

    /// <summary>
    /// Waits for the parts on their way to get there, while <paramref name="stillWanted"/> and the renderer is the same.
    /// They move only while the renderer draws, and slower than the clock when the frames come slowly: the wait is
    /// given up on only once they have stopped getting anywhere for a while (the viewport out of sight).
    /// </summary>
    private Task WaitForMotionsAsync(GarageRenderer renderer, Func<bool> stillWanted) =>
        WaitForRendererAsync(renderer, () => renderer.MotionSecondsLeft, stillWanted);

    /// <summary>Waits for the engine on the stand to be turned where it is to be, as <see cref="WaitForMotionsAsync"/> does</summary>
    private Task WaitForTurnAsync(GarageRenderer renderer, Func<bool> stillWanted) =>
        WaitForRendererAsync(renderer, () => renderer.TurnSecondsLeft, stillWanted);

    /// <summary>Waits while the renderer has <paramref name="secondsLeft"/> of drawing to go and keeps getting through it</summary>
    private async Task WaitForRendererAsync(GarageRenderer renderer, Func<float> secondsLeft, Func<bool> stillWanted)
    {
        var left = secondsLeft();
        for (var stalled = 0; stalled < MotionPatienceMs && left > 0f && renderer == Renderer && stillWanted(); )
        {
            await Task.Delay(30);
            var now = secondsLeft();
            stalled = now < left ? 0 : stalled + 30;
            left = now;
        }
    }

    /// <summary>The camera goes after the engine on the hoist until it is down in the car, then frames the car again</summary>
    private async void FollowUntilDown(GarageRenderer renderer)
    {
        while (renderer == Renderer && _followCarry && renderer.IsMovingParts) await Task.Delay(30);

        if (renderer != Renderer || !_followCarry) return;

        _followCarry = false;
        ResetCamera();
    }

    /// <summary>How high the bottom of a load on the hoist has to be to go over the car</summary>
    private static float ClearHeight(GarageRenderer renderer) =>
        renderer.MainSlot.GetCarBoundingBox() is { } box ? box.Maximum.Y * WingLine : 1f;

    /// <summary>
    /// The stand's parts view: <see cref="Engine"/>, the one on the stand, becomes the prop to pick and mount on, and
    /// leaves the scenery in the same frame, so that it is never gone nor there twice. The car stays solid.
    /// </summary>
    private async Task ApplyStandAsync(GarageRenderer renderer, PartsCatalog catalog)
    {
        var engine = Engine;
        if (_partsOnStand && renderer.StandActive && ReferenceEquals(_partsEngine, engine)) return;

        _isLoadingParts = true;
        try
        {
            var started = DateTime.Now;

            // Same stand, other parts: what is no longer there lifts off while the new model is put together
            var before = renderer.HasProp && _partsOnStand ? IdsOf(_partNodes) : null;
            var after = engine == null ? new HashSet<Guid>() : engine.SelfAndDescendants().Select(p => p.InstanceId).Where(id => id != Guid.Empty).ToHashSet();

            // An engine the stand did not have (off the shelf) comes down onto it from above: the stand stays empty
            // until it does
            var dropIn = before == null && engine != null && _sceneryKey is { } shown && shown.Engine?.InstanceId != engine.InstanceId;
            if (dropIn) _standKeptEmpty = true;
            var leaving = before == null ? new List<int>() : NodesWhere(_partNodes, id => !after.Contains(id));
            // An engine turning, over or back upright (off the stand), gets there before parts come off it: their ways
            // are the engine's
            if (leaving.Count > 0)
            {
                await WaitForTurnAsync(renderer, () => IsOnStand);
                if (renderer == Renderer && IsOnStand) renderer.MoveParts(MovesOf(catalog, _partNodes, leaving), arriving: false, NodeOf(_partNodes, _partsEngine));
                AnimateFor(AnimationWindow);
            }

            // The engine alone, and the stand without it as the scenery to go with it
            var (placed, kn5, nodes, scenery) = await Task.Run(() =>
            {
                var standAlone = BuildScenery(catalog, engine, withEngine: false);
                var parts = EngineStandLayout.Build(catalog, engine);
                if (parts.Count == 0) return (parts, (AcTools.Kn5File.IKn5?)null, (IReadOnlyList<PlacedPart>)Array.Empty<PlacedPart>(), standAlone);

                var model = PartAssembler.BuildModel(catalog, "stand", parts);
                return (parts, model.Kn5, model.Nodes, standAlone);
            });

            await WaitForMotionsAsync(renderer, () => true);
            await WaitForTurnAsync(renderer, () => IsOnStand);

            if (renderer == Renderer && IsOnStand)
            {
                // The car stays solid: the work is beside it. An engine new to the view is shown turned as it is.
                renderer.StandActive = true;
                renderer.GhostCar = false;
                renderer.EngineTurnedOver = EngineTurnedOver;
                renderer.SetProp(kn5, SlimDX.Matrix.Identity);
                if (before == null) renderer.SettleEngineTurn();
                if (before != null) renderer.MoveParts(MovesOf(catalog, nodes, NodesWhere(nodes, id => !before.Contains(id))), arriving: true, NodeOf(nodes, engine));
                else if (dropIn) renderer.MoveParts(new[] { new PartMove(Enumerable.Range(0, nodes.Count).ToList(), new[] { PartWay.Up }) }, arriving: true, NodeOf(nodes, engine));
                PutScenery(renderer, scenery, (engine, false, catalog));
                _partsOnStand = true;
                _partsCarDirectory = null;
                _partsEngine = engine;
                _partsGear = null;
                _anchors = null;
                _partNodes = nodes;
                _partWorlds = placed.Where(p => p.Source != null).ToDictionary(p => p.Source!, p => p.World);
                AnimateFor(AnimationWindow);

                FollowSelection();

                Logger.Information("Engine stand laid out in {Ms} ms ({Count} parts, engine {Engine})",
                    (int)(DateTime.Now - started).TotalMilliseconds, placed.Count, engine?.Definition.Id ?? "none");

                ApplySelection();
                ApplyCandidates();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not lay out the engine stand");
        }
        finally
        {
            _isLoadingParts = false;
            _standKeptEmpty = false;
        }

        if (Renderer != null && (renderer != Renderer || !IsOnStand || !ReferenceEquals(Engine, engine))) ApplyParts();

        // The stand's engine may not be the one worked on here yet (the two are bound one after the other)
        QueueApplyScenery();
    }

    /// <summary>
    /// Out of the stand's parts view: the engine goes back into the scenery, and leaves the prop, in the same frame.
    /// The caller lays out whatever comes next.
    /// </summary>
    private async Task LeaveStandAsync(GarageRenderer renderer)
    {
        var catalog = PartsCatalog;
        var engine = StandEngine;

        // Only an engine going in the car keeps the camera on the stand: it goes after it once the hoist has it
        var installing = _partsEngine is { InstanceId: var goingId } && goingId != Guid.Empty && engine == null && Engine?.InstanceId == goingId && PartsVisible && catalog != null;
        if (_cameraWaitsOnStand && !installing) ResetCamera();
        AcTools.Kn5File.IKn5? scenery = null;
        var built = false;
        _isLoadingParts = true;
        try
        {
            // An engine turned over turns back first: it goes into the scenery, and on the hoist, the right way up. The
            // garage is locked while it does, as it is for the hoist after it.
            var building = catalog == null ? Task.FromResult<AcTools.Kn5File.IKn5?>(null) : Task.Run<AcTools.Kn5File.IKn5?>(() => BuildScenery(catalog, engine, withEngine: true));
            if (renderer.IsTurningEngine)
            {
                _turningBack = true;
                AnimateFor(AnimationWindow);
                await WaitForTurnAsync(renderer, () => !IsOnStand);
            }

            scenery = await building;
            built = true;
        }
        catch (Exception ex)
        {
            // The scenery catches up by itself once the prop has let go
            Logger.Error(ex, "Could not put the engine back on the stand");
        }
        finally
        {
            _isLoadingParts = false;
        }

        // Back on the stand in the meantime, or another renderer: the next run sorts it out
        if (renderer != Renderer || IsOnStand)
        {
            _turningBack = false;
            return;
        }

        // The engine on the stand has gone in the car: it stays in sight until the car's parts are ready, and goes
        // over on the hoist from where it is. The garage stays locked from the turn back on to that.
        _carryFromStand = null;
        if (_partsEngine is { InstanceId: var id } standEngine && id != Guid.Empty && StandEngine == null && Engine?.InstanceId == id && PartsVisible && catalog != null)
            _carryFromStand = (id, ToSlim(EngineStandLayout.EnginePlacement(catalog, standEngine.Definition)));
        _turningBack = false;

        renderer.StandActive = false;
        if (_carryFromStand == null) renderer.SetProp(null, SlimDX.Matrix.Identity);
        ClearCandidates(renderer);
        if (built && ReferenceEquals(catalog, PartsCatalog)) PutScenery(renderer, scenery, (engine, true, catalog));
        _partsOnStand = false;
        _partsEngine = null;
        _partNodes = Array.Empty<PlacedPart>();
        _partWorlds = new();
        if (_placing != null)
        {
            _placing = null;
            PlacementChanged?.Invoke(null);
        }

        ShowPartLabel(null, default);
        AnimateFor(AnimationWindow);
        QueueApplyScenery();
    }

    private void QueueApplyScenery()
    {
        if (_applySceneryQueued) return;

        _applySceneryQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _applySceneryQueued = false;
            ApplyScenery();
        });
    }

    /// <summary>What the car was last asked to show, being put on</summary>
    private Task _shownApplied = Task.CompletedTask;

    /// <summary>
    /// Waits, at most <paramref name="most"/>, until the car asked for is up with what it shows on (its wheels, its wear):
    /// a screen holds the car back until then, so its wear does not come on in front of the player (the first time for a
    /// model it takes a moment to make)
    /// </summary>
    public async Task ShownSettledAsync(TimeSpan most)
    {
        var until = DateTime.Now + most;
        while (DateTime.Now < until && (!IsUpToDate || !_shownApplied.IsCompleted)) await Task.Delay(40);
    }

    /// <summary>Puts <see cref="Shown"/> on the car; from property callbacks too, so nothing escapes: the car keeps its own</summary>
    private void ApplyShown() => _shownApplied = ApplyShownAsync();

    private async Task ApplyShownAsync()
    {
        try
        {
            if (Renderer is not { } renderer) return;
            if (await ShowPartsAsync(renderer, renderer.CarNode, PartsVisible ? Shown?.WithoutExtras() : Shown) && renderer == Renderer)
            {
                renderer.RefreshShadows();
                Invalidate();
                // A sagging corner leans the body
                OnPoseChanged();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "The parts the car shows could not be put on");
        }
    }

    /// <summary>Runs from property callbacks, so nothing may escape it: the garage goes on without the stand</summary>
    private async void ApplyScenery()
    {
        try
        {
            await ApplySceneryAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not put the engine stand in the garage");
        }
    }

    /// <summary>
    /// The engine stand beside the car, with the engine on it but while the stand's parts view has it as the prop to
    /// pick (<see cref="ApplyStandAsync"/>, which swaps it over with a scenery of its own)
    /// </summary>
    private async Task ApplySceneryAsync()
    {
        var renderer = Renderer;
        if (renderer == null || _isLoadingScenery) return;

        var key = SceneryKey;
        if (ReferenceEquals(renderer, _sceneryRenderer) && _sceneryKey == key) return;

        var (engine, withEngine, catalog) = key;
        var version = _sceneryVersion;
        _isLoadingScenery = true;
        try
        {
            var kn5 = catalog == null ? null : await Task.Run(() => BuildScenery(catalog, engine, withEngine));

            // The parts view put up a scenery of its own in the meantime: this one is out of date
            if (renderer == Renderer && version == _sceneryVersion)
            {
                PutScenery(renderer, kn5, key);
                AnimateFor(AnimationWindow);
            }
        }
        finally
        {
            _isLoadingScenery = false;
        }

        if (Renderer != null && (Renderer != _sceneryRenderer || _sceneryKey != SceneryKey)) ApplyScenery();
    }

    // The renderer the radio was put into: once for each renderer, whatever cars come and go in it
    private GarageRenderer? _radioRenderer;

    /// <summary>
    /// The radio on its bench, where the garage's props put it (<see cref="Services.Garage.GarageProps"/>): read off the UI
    /// thread, set into the renderer once. A garage with no place for it has none; a model that cannot be read is logged.
    /// </summary>
    private async void ApplyRadio()
    {
        var renderer = Renderer;
        if (renderer == null || ReferenceEquals(renderer, _radioRenderer)) return;
        _radioRenderer = renderer;

        var problems = new List<string>();
        var radio = Services.Garage.GarageProps.Find(Mods.ModSet.Installed, "radio", ShowroomKn5, problems);
        foreach (var problem in problems) Logger.Warning("Garage props: {Problem}", problem);
        Mods.ModProblems.Add("Garage props", problems);
        if (radio == null) return;

        AcTools.Kn5File.IKn5 kn5;
        try
        {
            kn5 = await Task.Run(() => AcTools.Kn5File.Kn5.FromFile(radio.Model));
        }
        catch (Exception ex)
        {
            Logger.Warning("The garage's radio could not be read from {Model}: {Error}", radio.Model, ex.Message);
            return;
        }

        if (renderer != Renderer) return;
        renderer.SetFixture(kn5, ToSlim(radio.Placement));
        Invalidate();
    }

    // The renderer the workbench was put into, and where it stands in the garage; null in a garage without one
    private GarageRenderer? _benchRenderer;
    private Services.Garage.PlacedProp? _bench;

    /// <summary>
    /// The workbench, where the garage's props put it (<see cref="WorkbenchLayout.PropId"/>): the game's own model drawn
    /// from boxes unless a mod names one, set into the renderer once. A garage with no place for it has none, and a
    /// loose part's job is shown there without the part.
    /// </summary>
    private async void ApplyBench()
    {
        var renderer = Renderer;
        if (renderer == null || ReferenceEquals(renderer, _benchRenderer)) return;
        _benchRenderer = renderer;
        _bench = null;

        var problems = new List<string>();
        var bench = Services.Garage.GarageProps.Find(Mods.ModSet.Installed, WorkbenchLayout.PropId, ShowroomKn5, problems, builtIn: true);
        foreach (var problem in problems) Logger.Warning("Garage props: {Problem}", problem);
        Mods.ModProblems.Add("Garage props", problems);
        if (bench == null) return;

        AcTools.Kn5File.IKn5 kn5;
        try
        {
            kn5 = await Task.Run(() => bench.Model.Length > 0 ? AcTools.Kn5File.Kn5.FromFile(bench.Model) : WorkbenchLayout.CreateModel());
        }
        catch (Exception ex)
        {
            Logger.Warning("The garage's workbench could not be read from {Model}: {Error}", bench.Model, ex.Message);
            return;
        }

        if (renderer != Renderer) return;
        renderer.SetBench(kn5, ToSlim(bench.Placement));
        _bench = bench;
        Invalidate();
    }

    private bool HitsRadio(System.Windows.Point position) =>
        Renderer != null && ActualWidth > 0 && ActualHeight > 0
        && Renderer.HitsFixture((float)(position.X / ActualWidth), (float)(position.Y / ActualHeight));

    /// <summary>What the scenery is to show now: the engine is in it unless the stand's parts view has it as the prop</summary>
    private (InstalledPart? Engine, bool WithEngine, PartsCatalog? Catalog) SceneryKey => (StandEngine, !_partsOnStand && !_standKeptEmpty, PartsCatalog);

    /// <summary>The game has an engine stand (<see cref="Rules.SystemRules.EngineStand"/>): none is drawn without</summary>
    private static bool HasStand => Rules.RuleBook.Current.Systems.EngineStand;

    private void PutScenery(GarageRenderer renderer, AcTools.Kn5File.IKn5? kn5, (InstalledPart? Engine, bool WithEngine, PartsCatalog? Catalog) key)
    {
        _sceneryVersion++;
        renderer.SetScenery(HasStand ? kn5 : null);
        _sceneryRenderer = renderer;
        _sceneryKey = key;
    }

    /// <summary>The stand, with the engine on it or not, as one model; off the UI thread</summary>
    private static AcTools.Kn5File.IKn5 BuildScenery(PartsCatalog catalog, InstalledPart? engine, bool withEngine)
    {
        var headZ = EngineStandLayout.HeadZ(catalog, engine);
        var parts = withEngine ? EngineStandLayout.Build(catalog, engine) : new List<PlacedPart>();
        if (parts.Count == 0) return EngineStandLayout.CreateModel(headZ);

        AssemblyModel model;
        try
        {
            model = PartAssembler.BuildModel(catalog, "stand", parts);
        }
        catch (InvalidOperationException)
        {
            // No part of the engine has a model: the stand alone
            return EngineStandLayout.CreateModel(headZ);
        }

        EngineStandLayout.AddTo(model.Kn5, headZ);
        return model.Kn5;
    }

    /// <summary>Every mounted part where it belongs now: on the stand, or in the car by its hubs</summary>
    private List<PlacedPart>? LayOut(PartsCatalog catalog)
    {
        if (_partsOnStand) return EngineStandLayout.Build(catalog, Engine);

        return _anchors is { } anchors ? CarPartsLayout.Build(catalog, anchors, Engine, Gear, _partsLayout) : null;
    }

    /// <summary>An engine the nose carries, rather than the tail</summary>
    private static bool InFront(EngineLayout layout) => layout.IsFront();

    private static HashSet<Guid> IdsOf(IReadOnlyList<PlacedPart> nodes) =>
        nodes.Select(n => n.Source?.InstanceId ?? Guid.Empty).Where(id => id != Guid.Empty).ToHashSet();

    /// <summary>Nodes of the parts somebody owns whose id passes the test; the fixed running gear has no id and never moves</summary>
    private static List<int> NodesWhere(IReadOnlyList<PlacedPart> nodes, Func<Guid, bool> test) =>
        Enumerable.Range(0, nodes.Count)
            .Where(i => nodes[i].Source is { } source && source.InstanceId != Guid.Empty && test(source.InstanceId))
            .ToList();

    /// <summary>
    /// Nodes on their way, as the moves they make: a part comes off the way its kind does, and what is on it goes
    /// along, so an engine lifts out whole. With a <paramref name="stagger"/> they go one after another, in the order
    /// an engine is stripped for the stand. A part that is not the engine's (the running gear) goes along the car's axes.
    /// </summary>
    private static List<PartMove> MovesOf(PartsCatalog catalog, IReadOnlyList<PlacedPart> nodes, List<int> moving, float stagger = 0f)
    {
        var order = catalog.WorkOrder;
        var ids = moving.Select(i => nodes[i].Source!.InstanceId).ToHashSet();
        return moving
            .GroupBy(i => TopOf(nodes[i].Source!))
            .OrderBy(g => order.KindOf(g.Key.Definition) is { } kind ? order.StandStripTurnOf(kind) : int.MaxValue)
            .Select((g, turn) => new PartMove(g.ToList(), order.WayOf(g.Key.Definition), turn * stagger, order.ToolOf(order.KindOf(g.Key.Definition)),
                CarAxes: !IsEngines(g.Key)))
            .ToList();

        InstalledPart TopOf(InstalledPart part)
        {
            while (part.Parent is { } parent && ids.Contains(parent.InstanceId)) part = parent;
            return part;
        }

        // The engine's parts hang off its block, whatever the block is on
        static bool IsEngines(InstalledPart part)
        {
            while (part.Parent is { } parent) part = parent;
            return PartKinds.IsBlock(part.Definition);
        }
    }


    /// <summary>The node of a part; null when it has none, or there is no part</summary>
    private static int? NodeOf(IReadOnlyList<PlacedPart> nodes, InstalledPart? part)
    {
        for (var i = 0; part != null && i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i].Source, part)) return i;
        }

        return null;
    }

    #region Placement mode

    /// <summary>What a nudge in placement mode moves: the part's own mounting slot, or the slot it sits on</summary>
    public enum PlacementTarget { Part, Pad }

    /// <summary>Told what the placement mode is doing, for the screen to show; null when the mode is off</summary>
    public event Action<string?>? PlacementChanged;

    private InstalledPart? _placing;
    private PlacementTarget _placementTarget;

    public bool IsPlacing => _placing != null;

    /// <summary>
    /// Enters placement mode on the selected part: nudges move it (or the pad it sits on) and are written to the
    /// catalog's slot shifts; on the engine block in the car, they move the whole engine and are kept for the car
    /// (<see cref="EngineMoved"/>). Leaves the mode when there is no selected part or it is already on.
    /// </summary>
    public void TogglePlacement()
    {
        // The engine block itself, in the car: moved with everything on it, kept per car (EngineMoved)
        if (_placing == null && SelectedPart is { Parent: null } block && ReferenceEquals(block, Engine) && !StandMode && PartsCatalog != null)
        {
            _placing = block;
            ReportPlacement();
            return;
        }

        if (_placing != null || SelectedPart is not { Parent: not null } part || PartsCatalog == null)
        {
            _placing = null;
            PlacementChanged?.Invoke(null);
            return;
        }

        // A part's own slot is the part's wherever it goes: nudging it moves every copy, the two carburettors of a
        // dual quad along with each other. One of several on the same parent is placed by its pad, which is its own
        _placing = part;
        _placementTarget = IsOneOfSeveral(part) ? PlacementTarget.Pad : PlacementTarget.Part;
        ReportPlacement();
    }

    /// <summary>
    /// Keeps placement on the selected part: another part picked is placed from then on, the same part in a tree made
    /// anew (known by its id) goes on being placed; nothing placeable selected ends the mode
    /// </summary>
    private void FollowSelection()
    {
        if (_placing == null) return;

        var selected = SelectedPart;
        var next = selected == null ? null
            : _partWorlds.ContainsKey(selected) ? selected
            : selected.InstanceId == Guid.Empty ? null
            : _partWorlds.Keys.FirstOrDefault(p => p.InstanceId == selected.InstanceId);
        if (next == null || !(next.Parent != null || (ReferenceEquals(next, Engine) && !StandMode)))
        {
            _placing = null;
            PlacementChanged?.Invoke(null);
            return;
        }

        var samePart = ReferenceEquals(next, _placing) || (next.InstanceId != Guid.Empty && next.InstanceId == _placing.InstanceId);
        _placing = next;
        if (!samePart && next.Parent != null) _placementTarget = IsOneOfSeveral(next) ? PlacementTarget.Pad : PlacementTarget.Part;
        ReportPlacement();
    }

    /// <summary>True when the parent holds the same part on another slot too (a set of carburettors, their filters)</summary>
    private static bool IsOneOfSeveral(InstalledPart part) =>
        part.Parent != null && part.Parent.Children.Values.Count(c => c.Definition.Id.Equals(part.Definition.Id, StringComparison.OrdinalIgnoreCase)) > 1;

    public void TogglePlacementTarget()
    {
        if (_placing == null) return;

        _placementTarget = _placementTarget == PlacementTarget.Part ? PlacementTarget.Pad : PlacementTarget.Part;
        ReportPlacement();
    }

    /// <summary>
    /// Moves the placed part by a step in the engine's axes: right, up, and forward (towards the radiator). The
    /// step becomes a move of a slot in that slot's own part's space, so it is what the packs store.
    /// </summary>
    public void NudgePlacement(float right, float up, float forward)
    {
        // The block: the whole engine moves, in the car's space, by the engine's axes
        if (_placing is { Parent: null } block)
        {
            if (!_partWorlds.TryGetValue(block, out var blockWorld)) return;
            var axes = blockWorld;
            axes.Translation = System.Numerics.Vector3.Zero;
            // Kept to the millimetre (back at the start is zero, not a residue) and within what the car book takes
            var next = EngineOffset + System.Numerics.Vector3.TransformNormal(new System.Numerics.Vector3(right, up, -forward), axes);
            static float Kept(float v) => Math.Clamp(MathF.Round(v * 1000) / 1000, -CarEntry.MaxEngineOffset, CarEntry.MaxEngineOffset);
            EngineMoved?.Invoke(new System.Numerics.Vector3(Kept(next.X), Kept(next.Y), Kept(next.Z)));
            return;
        }

        var catalog = PartsCatalog;
        if (_placing is not { Parent: { } parent } part || catalog == null || Engine == null) return;
        if (!_partWorlds.TryGetValue(Engine, out var engineWorld) || !_partWorlds.TryGetValue(part, out var partWorld)
            || !_partWorlds.TryGetValue(parent, out var parentWorld)) return;

        // SLRR parts look down their +Z at the firewall: forward is -Z of the engine
        var engineAxes = engineWorld;
        engineAxes.Translation = System.Numerics.Vector3.Zero;
        var worldDelta = System.Numerics.Vector3.TransformNormal(new System.Numerics.Vector3(right, up, -forward), engineAxes);

        // The part hangs so that its own slot lands on the pad: moving its slot moves the part the other way
        var (definition, slotId) = PlacedSlot(part, parent);
        var (frame, sign) = _placementTarget == PlacementTarget.Part ? (partWorld, -1f) : (parentWorld, 1f);
        frame.Translation = System.Numerics.Vector3.Zero;
        if (!System.Numerics.Matrix4x4.Invert(frame, out var toLocal)) return;
        var local = System.Numerics.Vector3.TransformNormal(worldDelta, toLocal) * sign;

        Shift(catalog, definition, slotId, new[] { local.X, local.Y, local.Z });
    }

    /// <summary>Takes the slot being placed back to where the packs put it</summary>
    public void ResetPlacement()
    {
        if (_placing is { Parent: null })
        {
            EngineMoved?.Invoke(System.Numerics.Vector3.Zero);
            return;
        }

        var catalog = PartsCatalog;
        if (_placing is not { Parent: { } parent } part || catalog == null) return;

        var (definition, slotId) = PlacedSlot(part, parent);
        var offset = catalog.Shifts.Of(definition.Id, slotId);
        Shift(catalog, definition, slotId, new[] { -offset[0], -offset[1], -offset[2] });
    }

    /// <summary>The slot a nudge moves: the part's own mounting slot, or the pad of its parent it sits on</summary>
    private (PartDefinition Definition, int SlotId) PlacedSlot(InstalledPart part, InstalledPart parent) =>
        _placementTarget == PlacementTarget.Part ? (part.Definition, part.OwnSlot) : (parent.Definition, part.ParentSlot);

    private void Shift(PartsCatalog catalog, PartDefinition definition, int slotId, float[] delta)
    {
        if (!catalog.ShiftSlot(definition, slotId, delta))
            Logger.Warning("Slot {Slot} of {Part} moved in the garage, but the move could not be written to {File}", slotId, definition.Id, catalog.Shifts.Path);

        Relayout();
        ReportPlacement();
    }

    private void ReportPlacement()
    {
        var catalog = PartsCatalog;
        if (_placing is { Parent: null } block)
        {
            var o = EngineOffset;
            PlacementChanged?.Invoke(
                $"PLACEMENT: moving the engine, everything on it with it (kept for this car in cars.json)\n{block.Definition.Id}\n" +
                $"moved so far: x {o.X * 100:+0.0;-0.0} cm  y {o.Y * 100:+0.0;-0.0} cm  z {o.Z * 100:+0.0;-0.0} cm\n" +
                "arrows: across / fore-aft   PgUp PgDn: height   Ctrl 1 mm  Shift 2 cm\nR: back to its place   F5 or Esc: done");
            return;
        }

        if (_placing is not { Parent: { } parent } part || catalog == null)
        {
            PlacementChanged?.Invoke(null);
            return;
        }

        var (definition, slotId) = PlacedSlot(part, parent);
        var offset = catalog.Shifts.Of(definition.Id, slotId);
        var what = _placementTarget == PlacementTarget.Part
            ? "the part's own slot (every copy of the part)"
            : IsOneOfSeveral(part) ? "the pad it sits on (this one of the set)" : "the pad it sits on";
        PlacementChanged?.Invoke(
            $"PLACEMENT: moving {what}\n{definition.Id} slot {slotId}\n" +
            $"shift so far: x {offset[0] * 100:+0.0;-0.0} cm  y {offset[1] * 100:+0.0;-0.0} cm  z {offset[2] * 100:+0.0;-0.0} cm\n" +
            "arrows: across / fore-aft   PgUp PgDn: height   Ctrl 1 mm  Shift 2 cm\nTab: part / pad   R: reset   F5 or Esc: done");
    }

    /// <summary>Puts every mounted part where the slots say now, without rebuilding the model</summary>
    private void Relayout()
    {
        var renderer = Renderer;
        var catalog = PartsCatalog;
        if (renderer == null || catalog == null || !renderer.HasProp || LayOut(catalog) is not { } placed) return;

        var worlds = placed.Where(p => p.Source != null).ToDictionary(p => p.Source!, p => p.World);
        var moves = new List<(int Node, SlimDX.Matrix World)>();
        for (var i = 0; i < _partNodes.Count; i++)
        {
            if (_partNodes[i].Source is { } source && worlds.TryGetValue(source, out var m))
            {
                moves.Add((i, ToSlim(m)));
            }
        }

        renderer.PlaceParts(moves);
        _partWorlds = worlds;
        AnimateFor(AnimationWindow);

        // The places a loose part could go were worked out from where its parent was
        if (_shownCandidates is { Count: > 0 }) ApplyCandidates();
    }

    #endregion

    /// <summary>
    /// Shows the loose part of <see cref="Candidates"/> in the places it could go. Runs from property callbacks, so
    /// nothing may escape it.
    /// </summary>
    private async void ApplyCandidates()
    {
        try
        {
            await ApplyCandidatesAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not show where the part goes");
        }
    }

    private async Task ApplyCandidatesAsync()
    {
        var renderer = Renderer;
        if (renderer == null) return;

        // The places of the part picked before are no places for this one: they go at once, not when the
        // new ones are built, or a click in between would mount this part where the other one fits
        var candidates = Candidates;
        if (!ReferenceEquals(candidates, _shownCandidates)) ClearCandidates(renderer);
        if (_isLoadingCandidates) return;

        var catalog = PartsCatalog;
        var anchors = _anchors;

        // The layout the parts on screen were built with: the candidates hang off them
        var layout = _partsLayout;
        if (candidates == null || candidates.Count == 0 || catalog == null || (anchors == null && !_partsOnStand) || !renderer.HasProp)
        {
            ClearCandidates(renderer);
            return;
        }

        _isLoadingCandidates = true;
        try
        {
            var worlds = _partWorlds;
            var (model, owners) = await Task.Run<(AssemblyModel? Model, MountCandidate[] Owners)>(() =>
            {
                var placed = new List<PlacedPart>();
                var ownerOf = new Dictionary<InstalledPart, MountCandidate>();
                foreach (var candidate in candidates)
                {
                    var world = candidate.Parent == null
                        ? anchors == null ? null : CarPartsLayout.CarLevelPlacement(catalog, anchors, candidate.Part.Definition, candidate.ParentSlot, layout)
                        : worlds.TryGetValue(candidate.Parent, out var parentWorld)
                            ? CarPartsLayout.PlacementOn(catalog, anchors, candidate.Parent.Definition, candidate.ParentSlot, parentWorld,
                                candidate.Part.Definition, candidate.OwnSlot, layout)
                            : null;
                    if (world == null) continue;

                    foreach (var part in PartAssembler.Assemble(candidate.Part, world.Value))
                    {
                        placed.Add(part);
                        ownerOf[part.Source!] = candidate;
                    }
                }

                if (placed.Count == 0) return (null, Array.Empty<MountCandidate>());

                AssemblyModel built;
                try
                {
                    built = PartAssembler.BuildModel(catalog, "candidates", placed);
                }
                catch (InvalidOperationException)
                {
                    // BuildModel's "no part has a model": nothing made it into the model, nothing to show or click
                    return (null, Array.Empty<MountCandidate>());
                }

                return (built, built.Nodes.Select(n => ownerOf[n.Source!]).ToArray());
            });

            if (renderer == Renderer && ReferenceEquals(candidates, Candidates))
            {
                renderer.SetCandidates(model?.Kn5);
                var hanging = Unsprung(catalog, Gear);
                renderer.SetUnsprung(PartLayer.Candidate, Enumerable.Range(0, owners.Length).Where(i =>
                    owners[i].Parent is { } parent ? hanging.Contains(parent) : HangsOnLift(catalog, owners[i].ParentSlot, owners[i].Part.Definition)));
                _candidateNodes = owners;
                _shownCandidates = candidates;
                AnimateFor(AnimationWindow);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not show where the part goes");
        }
        finally
        {
            _isLoadingCandidates = false;
        }

        // Likewise a renderer rebuilt while the candidates were put together
        if (Renderer != null && (renderer != Renderer || !ReferenceEquals(candidates, Candidates))) ApplyCandidates();
    }

    private void ClearCandidates(GarageRenderer renderer)
    {
        renderer.SetCandidates(null);
        _candidateNodes = Array.Empty<MountCandidate>();
        _shownCandidates = null;
        Invalidate();
    }

    /// <summary>Runs from property callbacks, so nothing may escape it</summary>
    private void ApplySelection()
    {
        try
        {
            if (Renderer == null) return;

            // The model on screen may still be the one of the tree before: the same part is known by its id there
            var selected = SelectedPart;
            var node = -1;
            for (var i = 0; selected != null && i < _partNodes.Count; i++)
            {
                var source = _partNodes[i].Source;
                if (ReferenceEquals(source, selected) || (source != null && selected.InstanceId != Guid.Empty && source.InstanceId == selected.InstanceId)) node = i;
            }

            Renderer.SelectedPart = node < 0 ? null : node;
            InvalidateIfDirty();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not show the selected part");
        }
    }

    /// <summary>The car's hubs where they are with the car on the floor: the lift's height taken off (the parts go up with it)</summary>
    private CarAnchors? GetAnchors(Kn5RenderableCar carNode)
    {
        var hubs = WheelFit.WheelNodes
            .Select(name => carNode.RootObject.GetDummyByName(name)?.Matrix)
            .ToArray();
        if (hubs.Any(h => h == null)) return null;

        // The wheels hang below the lifted body as well: they are back up on it, where the car on the floor has them
        var lifted = LiftedBy(carNode) - LiftLayout.Droop(LiftedBy(carNode));
        var points = hubs.Select(h => new System.Numerics.Vector3(h!.Value.M41, h.Value.M42 - lifted, h.Value.M43)).ToArray();
        return new CarAnchors(points[0], points[1], points[2], points[3]);
    }

    /// <summary>Runs from property callbacks, so nothing may escape it: a broken skin folder leaves the skin as it was</summary>
    private void ApplySkin()
    {
        try
        {
            if (Renderer == null || string.IsNullOrEmpty(_loadedCarDirectory)) return;

            var skinId = SkinId;
            if (string.IsNullOrEmpty(skinId)) return;

            if (Directory.Exists(Path.Combine(_loadedCarDirectory, "skins", skinId)))
            {
                Renderer.SelectSkin(skinId);
                Invalidate();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not put on skin {Skin}", SkinId ?? "(none)");
        }
    }

    /// <summary>Runs from property callbacks, so nothing may escape it</summary>
    private void ApplyCarState()
    {
        try
        {
            var renderer = Renderer;
            if (renderer == null) return;

            renderer.AutoRotate = AutoRotate;

            var carNode = renderer.CarNode;
            if (carNode != null)
            {
                carNode.HeadlightsEnabled = HeadlightsOn;
                if (carNode.HasLeftDoorAnimation) carNode.LeftDoorOpen = DoorsOpen;
                if (carNode.HasRightDoorAnimation) carNode.RightDoorOpen = DoorsOpen;
            }

            AnimateFor(AnimationWindow);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not set the lights, doors or turntable");
        }
    }

    #endregion

    #region Body rock

    protected override bool LeansParts => true;

    /// <summary>The engine moved the body: the car leans, and the parts in it with it</summary>
    protected override void OnPoseChanged() => RockCar(Renderer?.CarNode);

    protected override bool EngineTurnedRound => EngineLayout == EngineLayout.Rear;

    protected override bool EngineAcross => EngineLayout.IsTransverse();

    #endregion

    #region Driving in and out

    /// <summary>The car, standing on its wheels in the plain view: not with its parts out or an engine on the move</summary>
    private Kn5RenderableCar? CarToDrive => Renderer?.CarNode is { } node && !PartsVisible && !IsMovingEngine ? node : null;

    /// <summary>
    /// A car taken out of the lot comes in: the script for <see cref="ScriptTriggers.GarageArrive"/> plays with it, its
    /// engine <paramref name="engine"/> (worked out while the garage comes up), on the garage's marks. Null when there
    /// is no car in the plain view, or no scene.
    /// </summary>
    public ScriptRun? Arrive(Func<Audio.EngineRunner?> engine, ScriptFacts facts) => Play(ScriptTriggers.GarageArrive, engine, facts, null);

    /// <summary>The car leaves for somewhere: the script for <see cref="ScriptTriggers.GarageLeave"/>, the screen's curtain its to bring down</summary>
    public ScriptRun? Leave(Func<Audio.EngineRunner?> engine, ScriptFacts facts, Func<float, TimeSpan, FadeEase, Task> fade) =>
        Play(ScriptTriggers.GarageLeave, engine, facts, fade);

    /// <summary>A script of the flow's (screen.&lt;id&gt;.enter or .leave) with the car on show; null with no car or no scene</summary>
    public ScriptRun? PlayTrigger(string trigger, Func<Audio.EngineRunner?> engine, ScriptFacts facts, Func<float, TimeSpan, FadeEase, Task>? fade) =>
        Play(trigger, engine, facts, fade);

    /// <summary>The car back in its place, its lights off: the garage stays after all</summary>
    public void PutBack()
    {
        StopScript();
        HeadlightsOn = false;
        Invalidate();
    }

    private ScriptRun? Play(string trigger, Func<Audio.EngineRunner?> engine, ScriptFacts facts, Func<float, TimeSpan, FadeEase, Task>? fade)
    {
        if (CarToDrive is not { } node || Renderer is not { } renderer) return null;

        // No script at this moment: the camera and the car are left as they are
        if (!HasScript(trigger, engine, facts)) return ScriptRun.Nothing();

        // The camera holds still while the car moves, so it is framed on the car's place first: the renderer's own
        // framing moves only part of the way each frame, and would be stopped short of it
        if (renderer.AutoAdjustTarget && renderer.CameraOrbit is { } orbit)
        {
            orbit.Target = renderer.FramedTarget(orbit.Alpha, orbit.Beta, orbit.Radius, orbit.Target);
            orbit.UpdateViewMatrix();
        }

        return PlayScript(trigger, node, engine, facts, on =>
        {
            HeadlightsOn = on;
            AnimateFor(TimeSpan.FromSeconds(1));
        }, GarageScene.For(AppSettings.Instance.GarageShowroomId), fade);
    }

    #endregion

    #region Lift

    // How high the lift has the car now, and the pump heard while it goes
    private float _liftNow;
    private Audio.PlayingSound? _liftHum;

    // The renderer the lift was put into, and where its prop stands in the garage; null in a garage without one
    private GarageRenderer? _liftRenderer;
    private Services.Garage.PlacedProp? _liftProp;

    // The lift slows over its last few centimetres into a stop, and no slower than this share of its speed
    private const float LiftEaseDistance = 0.06f;
    private const float LiftSlowest = 0.25f;

    // While the car is up, the camera may look up from under it: this far under level at most, its eye kept off the floor
    private const float UnderCarBeta = 0.35f;
    private const float EyeOffFloor = 0.12f;

    private float LiftGoal => LiftLayout.HeightOf(LiftStop);

    protected override float LiftHeight => _liftNow;

    /// <summary>The lowest the camera may look from: under level only while the car is up off its wheels</summary>
    private float LowestBeta(float radius, float targetY)
    {
        if (_liftNow < LiftLayout.WheelsClearHeight / 2f || radius <= 0f) return MinBeta;
        return -MathF.Asin(Math.Clamp((targetY - EyeOffFloor) / radius, 0f, MathF.Sin(UnderCarBeta)));
    }

    /// <summary>The lift a step nearer its stop, the car and the post with it; false when it is there</summary>
    private bool StepLift(GarageRenderer renderer, float dt)
    {
        var goal = LiftGoal;
        if (_liftNow == goal || renderer.CarNode == null) return false;

        var left = goal - _liftNow;
        var step = LiftLayout.Speed * Math.Min(dt, GarageRenderer.MaxTickSeconds) * Math.Clamp(Math.Abs(left) / LiftEaseDistance, LiftSlowest, 1f);
        if (step >= Math.Abs(left))
        {
            _liftNow = goal;
            Audio.AudioHub.Shared.StopLoop(_liftHum);
            _liftHum = null;
            Audio.Sfx.Play(Audio.Sfx.LiftStops);
            IsLiftMoving = false;

            // Down on the floor, the camera comes back up out from under it
            if (_betaGoal < MinBeta) _betaGoal = MinBeta;
        }
        else
        {
            _liftNow += Math.Sign(left) * step;
        }

        renderer.LiftSaddle = LiftLayout.SaddleTop(_liftNow);
        renderer.Droop = LiftLayout.Droop(_liftNow);
        OnPoseChanged();
        return true;
    }

    /// <summary>
    /// Whether a part on this car slot hangs with the wheels as the lift takes the body up: a wheel, a brake, the rear axle
    /// (with all it carries), a leaf spring (with its traction bar); not a coil, a shock or a torsion bar on the body
    /// </summary>
    private static bool HangsOnLift(PartsCatalog catalog, int carSlot, PartDefinition part)
    {
        if (carSlot == RearAxle.CarSlot) return true;
        var corner = RunningGear.CornerOf(carSlot);
        return corner >= 0 && (carSlot == RunningGear.WheelSlot(corner) || carSlot == RunningGear.BrakeSlot(corner)
                               || carSlot == RunningGear.SpringSlot(corner) && string.Equals(catalog.WorkOrder.KindOf(part), "leaf_spring", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The running gear that hangs with the wheels (<see cref="HangsOnLift"/>), with everything on it</summary>
    private static HashSet<InstalledPart> Unsprung(PartsCatalog catalog, IReadOnlyList<CarPart>? gear)
    {
        var hanging = new HashSet<InstalledPart>(ReferenceEqualityComparer.Instance);
        foreach (var part in gear ?? Array.Empty<CarPart>())
            if (HangsOnLift(catalog, part.CarSlot, part.Root.Definition)) hanging.UnionWith(part.Root.SelfAndDescendants());
        return hanging;
    }

    private static IEnumerable<int> NodesHanging(IReadOnlyList<PlacedPart> nodes, HashSet<InstalledPart> hanging) =>
        Enumerable.Range(0, nodes.Count).Where(i => nodes[i].Source is { } source && hanging.Contains(source));

    /// <summary>A car just loaded comes at its stop, the lift under it with no move to hear</summary>
    private void SnapLift()
    {
        Audio.AudioHub.Shared.StopLoop(_liftHum);
        _liftHum = null;
        IsLiftMoving = false;
        _liftNow = Renderer?.CarNode == null ? 0f : LiftGoal;
        if (Renderer is { } renderer)
        {
            renderer.LiftSaddle = LiftLayout.SaddleTop(_liftNow);
            renderer.Droop = LiftLayout.Droop(_liftNow);
            PlaceLift(renderer);
        }

        if (_liftNow < LiftLayout.WheelsClearHeight / 2f && _betaGoal < MinBeta) _betaGoal = MinBeta;
        OnPoseChanged();
    }

    /// <summary>
    /// The lift, where the garage's props put it (<see cref="LiftLayout.PropId"/>): the game's own model drawn from boxes
    /// unless a mod names one, set into the renderer once, and under the middle of whatever car is in the bay. A garage
    /// with no place for it lifts the car with no lift to see.
    /// </summary>
    private async void ApplyLift()
    {
        var renderer = Renderer;
        if (renderer == null || ReferenceEquals(renderer, _liftRenderer)) return;
        _liftRenderer = renderer;
        _liftProp = null;

        var problems = new List<string>();
        var lift = Services.Garage.GarageProps.Find(Mods.ModSet.Installed, LiftLayout.PropId, ShowroomKn5, problems, builtIn: true);
        foreach (var problem in problems) Logger.Warning("Garage props: {Problem}", problem);
        Mods.ModProblems.Add("Garage props", problems);
        if (lift == null) return;

        AcTools.Kn5File.IKn5 kn5;
        try
        {
            kn5 = await Task.Run(() => lift.Model.Length > 0 ? AcTools.Kn5File.Kn5.FromFile(lift.Model) : LiftLayout.CreateModel());
        }
        catch (Exception ex)
        {
            Logger.Warning("The garage's lift could not be read from {Model}: {Error}", lift.Model, ex.Message);
            return;
        }

        if (renderer != Renderer) return;
        _liftProp = lift;
        renderer.SetLift(kn5, ToSlim(LiftPlacement(renderer, lift)));
        Invalidate();
    }

    /// <summary>The lift moved under the car now in the bay</summary>
    private void PlaceLift(GarageRenderer renderer)
    {
        if (_liftProp is { } lift && ReferenceEquals(renderer, _liftRenderer)) renderer.MoveLift(ToSlim(LiftPlacement(renderer, lift)));
    }

    /// <summary>Under the middle of the car in the bay, at the prop's floor height; where the prop stands with no car</summary>
    private System.Numerics.Matrix4x4 LiftPlacement(GarageRenderer renderer, Services.Garage.PlacedProp lift) =>
        renderer.CarNode is { } car && GetAnchors(car) is { } anchors ? LiftLayout.UnderCar(anchors, lift.Placement) : lift.Placement;

    #endregion

    #region Rendering

    protected override bool StepFrame(GarageRenderer renderer, float dt)
    {
        if (_followCarry && renderer.CarriedCentre is { } load)
        {
            renderer.AutoAdjustTarget = false;
            _autoAdjustWhenSettled = false;
            _targetGoal = load;
            _radiusGoal = CarryRadius;
            _alphaGoal = StandAlpha;
            _betaGoal = CarryBeta;
        }

        // The renderer turns the car by itself a little further every tick: it has to keep drawing for that
        var lifting = StepLift(renderer, dt);
        var cameraMoving = StepCamera(dt);
        var working = StepWork(renderer, dt);
        return lifting || cameraMoving || working || renderer.AutoRotate || renderer.IsTurningEngine;
    }

    protected override void OnFirstFrame()
    {
        if (FadeInOnReady) FadeIn();
        else ShowPicture();
    }

    protected override void OnRendererDisposed()
    {
        _followCarry = false;
        _carryFromStand = null;
        _turningBack = false;
        _workOnBench = false;
        _workLeaving = false;
        _workVersion++;
        _cameraOnWork = false;
        _benchRenderer = null;
        _liftRenderer = null;
        _liftProp = null;
        Audio.AudioHub.Shared.StopLoop(_liftHum);
        _liftHum = null;
        _sceneryRenderer = null;
        _sceneryKey = null;
        _partsOnStand = false;
        _loadedCarDirectory = null;
        HasCar = false;
        HasDoors = false;
    }

    #endregion

    #region Camera input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Renderer == null) return;

        _isDragging = true;
        _lastMouse = _pressedAt = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void ForgetPress() => _isDragging = false;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var wasDragging = _isDragging;
        _isDragging = false;
        ReleaseMouseCapture();

        var position = e.GetPosition(this);
        if (wasDragging && (position - _pressedAt).Length <= ClickSlack) OnClick(position);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        var position = e.GetPosition(this);
        if (!_isDragging)
        {
            UpdateHover(position);
            return;
        }

        orbit.Alpha += (float)(position.X - _lastMouse.X) * 0.01f;
        orbit.Beta = Math.Clamp(orbit.Beta + (float)(position.Y - _lastMouse.Y) * 0.01f, LowestBeta(orbit.Radius, orbit.Target.Y), MaxBeta);
        _alphaGoal = orbit.Alpha;
        _betaGoal = orbit.Beta;
        _lastMouse = position;

        if ((position - _pressedAt).Length > ClickSlack) ShowPartLabel(null, default);
        Invalidate();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (Renderer != null)
        {
            Renderer.HoveredPart = null;
            Renderer.SceneryHovered = false;
            Renderer.CarHovered = false;
            Renderer.FixtureHovered = false;
        }

        Cursor = null;
        InvalidateIfDirty();
        ShowPartLabel(null, default);
    }

    private PartHit? PickAt(System.Windows.Point position) =>
        Renderer == null || ActualWidth <= 0 || ActualHeight <= 0
            ? null
            : Renderer.Pick((float)(position.X / ActualWidth), (float)(position.Y / ActualHeight));

    private void UpdateHover(System.Windows.Point position)
    {
        if (Renderer == null) return;

        // Picking tests triangles on the CPU; the pointer moves more often than it needs to be asked
        var now = DateTime.Now;
        if (now - _lastHoverPick < HoverInterval) return;
        _lastHoverPick = now;

        var hit = Renderer.HasProp && PartsVisible ? PickAt(position) : null;

        // The radio on its bench: its panel is a click away
        var onRadio = hit == null && !IsOnStand && HitsRadio(position);
        Renderer.FixtureHovered = onRadio;
        if (onRadio)
        {
            Renderer.SceneryHovered = false;
            Renderer.CarHovered = false;
            Renderer.HoveredPart = null;
            Cursor = System.Windows.Input.Cursors.Hand;
            InvalidateIfDirty();
            ShowLabel(GameText.Say("garage.label.radio"), position);
            return;
        }

        // The stand beside the car, while it is not the one being worked on, and the car while the stand is: a way over
        var onStand = hit == null && !IsOnStand && HitsStand(position);
        var onCar = hit == null && IsOnStand && HitsCar(position);
        Renderer.SceneryHovered = onStand;
        Renderer.CarHovered = onCar;
        Cursor = onStand || onCar ? System.Windows.Input.Cursors.Hand : null;
        if (onStand || onCar)
        {
            Renderer.HoveredPart = null;
            InvalidateIfDirty();
            ShowLabel(onStand ? GameText.Say("garage.label.stand") : GameText.Say("garage.label.back_to_car"), position);
            return;
        }

        if (Renderer is not { HasProp: true } || !PartsVisible)
        {
            InvalidateIfDirty();
            ShowPartLabel(null, default);
            return;
        }

        Renderer.HoveredPart = hit;
        InvalidateIfDirty();
        ShowPartLabel(hit == null ? null : DefinitionOf(hit.Value), position);
    }

    private bool HitsStand(System.Windows.Point position) =>
        Renderer != null && ActualWidth > 0 && ActualHeight > 0
        && Renderer.HitsScenery((float)(position.X / ActualWidth), (float)(position.Y / ActualHeight));

    private bool HitsCar(System.Windows.Point position) =>
        Renderer != null && ActualWidth > 0 && ActualHeight > 0
        && Renderer.HitsCar((float)(position.X / ActualWidth), (float)(position.Y / ActualHeight));

    private void OnClick(System.Windows.Point position)
    {
        if (Renderer == null) return;

        var partHit = Renderer.HasProp && PartsVisible ? PickAt(position) : null;
        if (partHit == null && !IsOnStand && HitsRadio(position))
        {
            Renderer.FixtureHovered = false;
            Cursor = null;
            ShowPartLabel(null, default);
            Audio.Radio.RadioPanelViewModel.Shared.IsOpen = true;
            return;
        }

        if (partHit == null && !IsOnStand && HasStand && HitsStand(position))
        {
            Renderer.SceneryHovered = false;
            Cursor = null;
            ShowPartLabel(null, default);
            StandClicked?.Invoke();
            return;
        }

        if (partHit == null && IsOnStand && HitsCar(position))
        {
            Renderer.CarHovered = false;
            Cursor = null;
            ShowPartLabel(null, default);
            CarClicked?.Invoke();
            return;
        }

        if (Renderer is not { HasProp: true } || !PartsVisible) return;

        var hit = partHit;
        if (hit is { Layer: PartLayer.Candidate } candidate && candidate.Node < _candidateNodes.Count)
        {
            if (ReferenceEquals(_shownCandidates, Candidates)) CandidateClicked?.Invoke(_candidateNodes[candidate.Node]);
            return;
        }

        var part = hit is { Layer: PartLayer.Mounted } mounted && mounted.Node < _partNodes.Count ? _partNodes[mounted.Node].Source : null;
        PartClicked?.Invoke(part);
    }

    private PartDefinition? DefinitionOf(PartHit hit) => hit.Layer switch
    {
        PartLayer.Mounted when hit.Node < _partNodes.Count => _partNodes[hit.Node].Part,
        PartLayer.Candidate when hit.Node < _candidateNodes.Count => _candidateNodes[hit.Node].Part.Definition,
        _ => null
    };

    private void ShowPartLabel(PartDefinition? part, System.Windows.Point position) =>
        ShowLabel(part == null ? null : part.DisplayName ?? part.Name, position);

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        var minRadius = IsOnStand ? StandMinRadius : Renderer!.HasProp ? PartsMinRadius : MinRadius;
        orbit.Radius = Math.Clamp(orbit.Radius - e.Delta * 0.002f, minRadius, MaxRadius);
        _radiusGoal = orbit.Radius;

        // Looking up from under the lifted car, the eye goes no lower than the floor further out either
        if (orbit.Beta < LowestBeta(orbit.Radius, orbit.Target.Y)) _betaGoal = orbit.Beta = LowestBeta(orbit.Radius, orbit.Target.Y);
        Invalidate();
        e.Handled = true;
    }

    #endregion
}
