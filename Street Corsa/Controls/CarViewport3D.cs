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
/// The camera and the pointer are in CarViewport3D.Camera.cs, the lift in CarViewport3D.Lift.cs, the placement mode
/// and the candidates in CarViewport3D.Placement.cs, the work on a part and the workbench in CarViewport3D.Work.cs.
/// </summary>
public partial class CarViewport3D : D3DViewportBase
{
    // Orbit camera limits: stay above the floor and inside the showroom walls
    private const float MinBeta = 0.02f;
    private const float MaxBeta = 1.2f;
    private const float MinRadius = 3.2f;
    private const float MaxRadius = 10f;

    // Where the camera starts from until it has a car to fit (ResetCamera: the car's own fit is worked out from its box)
    private const float DefaultRadius = 7.5f;
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

    // Under the player's hand the orbit follows its goals closely: a drag is felt at once, and the picture still never
    // jumps a mouse event at a time; a wheel notch eases quicker than a framing, slower than a drag
    private const float HandSettleRate = 18f;
    private const float ZoomSettleRate = 9f;
    private const float DragPerPixel = 0.01f;
    private const float WheelPerNotch = 0.002f;

    // Keep drawing for a while after a toggle so door/light animations play out
    private static readonly TimeSpan AnimationWindow = TimeSpan.FromSeconds(3);

    // Where the camera is headed, eased toward each frame
    private float _radiusGoal = DefaultRadius;
    private float _alphaGoal = DefaultAlpha;
    private float _betaGoal = DefaultBeta;
    private SlimDX.Vector3? _targetGoal;

    // The camera on the car: what it looks at is the car's middle, worked out again every frame (the lift takes it up),
    // and it stands as far back as fits the car in what the screen's panels leave, until the player zooms. The fit
    // remembers what it was made for, to be made again as a panel comes or goes.
    private bool _framingCar;
    private bool _fitRadius;
    private (double Left, double Right, double Width, double Height)? _fittedTo;
    private Showcase.CarFrame? _carFrame;

    // What the camera has got to looking at, eased after _targetGoal, and where the orbit was last pointed (that, moved
    // to the camera's left by the side shift, clear of the panels): a script moving the target between frames is seen
    // as the difference
    private SlimDX.Vector3? _lookAt;
    private SlimDX.Vector3 _placedTarget;
    private float _shiftNow;

    // The player's hand: a drag turns the goals, a let-go on the move throws them on a little, the wheel sets the distance
    private readonly OrbitHand _hand = new(DragPerPixel);
    private bool _handHeld;
    private bool _zooming;

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

    // A press that does not turn into a drag is a click; the pointer is asked what it is on this often at most
    // (Environment.TickCount64 ms: a clock change never holds the picks up)
    private const double ClickSlack = 4.0;
    private const long HoverIntervalMs = 30;
    private System.Windows.Point _pressedAt;
    private readonly PollThrottle _hoverPolls = new(HoverIntervalMs);

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

    public static readonly DependencyProperty RoomProperty = DependencyProperty.Register(
        nameof(Room), typeof(Services.Garage.GarageRoom), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewport3D)d).ApplyRoom()));

    /// <summary>
    /// The garage's room (its scene.json): the walls the camera stays inside and the light at each light of the day. A
    /// garage that says nothing of it (an Assetto Corsa showroom) is lit as the renderer lights it, the camera held by its
    /// own limits.
    /// </summary>
    public Services.Garage.GarageRoom? Room
    {
        get => (Services.Garage.GarageRoom?)GetValue(RoomProperty);
        set => SetValue(RoomProperty, value);
    }

    public static readonly DependencyProperty LightProperty = DependencyProperty.Register(
        nameof(Light), typeof(Services.Street.StreetLight), typeof(CarViewport3D),
        new PropertyMetadata(Services.Street.StreetLight.Day, (d, _) => ((CarViewport3D)d).ApplyRoom()));

    /// <summary>The light of the day in the garage (<see cref="Services.Street.StreetLights.At"/>): its lights and the sky past its door</summary>
    public Services.Street.StreetLight Light
    {
        get => (Services.Street.StreetLight)GetValue(LightProperty);
        set => SetValue(LightProperty, value);
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
            // Out of the parts view the camera goes back from a part to the car
            viewport.FocusSelection();
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
            viewport.FocusSelection();
        }));

    /// <summary>The mounted part that is marked as selected</summary>
    public InstalledPart? SelectedPart
    {
        get => (InstalledPart?)GetValue(SelectedPartProperty);
        set => SetValue(SelectedPartProperty, value);
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

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
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
        ApplyRoom();
        ApplyGarageDressing();
        ApplyScenery();
        ApplyRadio();
        ApplyBench();
        ApplyLift();
        SnapLift();
        Invalidate();

        Logger.Information("Loaded {Car} in {Ms} ms", hasCar ? Path.GetFileName(carDirectory) : "(empty garage)",
            (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <summary>
    /// The bindings land one at a time (the catalog, then the engine, then the gear, each a change of its own),
    /// so the layout waits for the lot and is built once
    /// </summary>
    private void QueueApplyParts()
    {
        if (_applyPartsQueued) return;

        _applyPartsQueued = true;
        var queued = new TaskCompletionSource();
        _partsApplied = Both(_partsApplied, queued.Task);
        Dispatcher.InvokeAsync(() =>
        {
            _applyPartsQueued = false;
            ApplyParts();

            // The run is under way and recorded (its task is in _partsApplied now): whoever waited on the queue looks again
            queued.SetResult();
        });
    }

    /// <summary>
    /// The parts view being built for what is bound, or queued to be (<see cref="QueueApplyParts"/>): done once no run
    /// is under way, so what waits for the parts to be laid out (<see cref="ApplyWorkAsync"/>) awaits this rather than
    /// asking every few milliseconds. A run begun while this waited is a newer task: look again once it is done.
    /// </summary>
    private Task _partsApplied = Task.CompletedTask;

    // How long a wait on the parts being laid out gives them before it goes on without them
    private const int PartsPatienceMs = 3000;

    /// <summary>Both done, however either ends: the one before (done already, as a rule) and the one begun now</summary>
    private static Task Both(Task before, Task now) => before.IsCompleted ? now : Task.WhenAll(before, now);

    /// <summary>
    /// Waits until the task <paramref name="latest"/> gives is done, asking again for a newer one each time (a run that
    /// queued another), while <paramref name="stillWanted"/> and for <paramref name="most"/> at the longest. True when the
    /// latest task was done in time; false when the wait gave up, or was no longer wanted.
    /// </summary>
    internal static async Task<bool> AwaitLatestAsync(Func<Task> latest, Func<bool> stillWanted, TimeSpan most)
    {
        var patience = Task.Delay(most);
        while (stillWanted())
        {
            var task = latest();
            if (task.IsCompleted) return true;
            if (await Task.WhenAny(task, patience) == patience) return false;
        }

        return false;
    }

    /// <summary>
    /// Builds the parts view for what is bound now. Runs from property callbacks, so nothing may escape it: a throw
    /// anywhere in it is logged and the garage goes on without the parts view. The run is recorded in
    /// <see cref="_partsApplied"/> for whoever waits on the parts.
    /// </summary>
    private void ApplyParts() => _partsApplied = Both(_partsApplied, ApplyPartsGuardedAsync());

    private async Task ApplyPartsGuardedAsync()
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

            var started = System.Diagnostics.Stopwatch.GetTimestamp();

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
                renderer.SetUnsprung(PartLayer.Mounted, NodesHanging(model.Nodes, Unsprung(gear)));
                renderer.SetStandIns(StandIns(model.Nodes, gear, Shown));
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
                    (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, placed.Count, engine?.Definition.Id ?? "none");

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

    /// <summary>
    /// The camera goes after the engine on the hoist until it is down in the car, then frames the car again. The wait is
    /// the one every motion is waited on with: given up on once the load has stopped getting anywhere for a while.
    /// </summary>
    private async void FollowUntilDown(GarageRenderer renderer)
    {
        await WaitForMotionsAsync(renderer, () => _followCarry);

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
            var started = System.Diagnostics.Stopwatch.GetTimestamp();

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
                    (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, placed.Count, engine?.Definition.Id ?? "none");

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
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (System.Diagnostics.Stopwatch.GetElapsedTime(started) < most && (!IsUpToDate || !_shownApplied.IsCompleted)) await Task.Delay(40);
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

    // The lamps the garage's room hung, taken down before it is lit for another hour
    private readonly List<AcTools.Render.Kn5SpecificForwardDark.Lights.DarkLightBase> _roomLamps = new();

    /// <summary>
    /// Lights the garage as its room says for <see cref="Light"/>: the sun, the ambient and the lamps (the ones hung before
    /// taken down first), its glass glowing, the sky of the hour past its door (where its model has one). A room that
    /// gives no light leaves the renderer's own.
    /// </summary>
    private void ApplyRoom()
    {
        var renderer = Renderer;
        if (renderer == null || Room is not { IsLit: true } room) return;

        var dressing = room.Dress(Light);
        SceneDressing.Apply(renderer, new LotDressing(dressing.Lighting, dressing.Sun, new Dictionary<string, string>(), dressing.Glow), _roomLamps, Logger);
        SceneDressing.ShowSky(renderer, Services.Garage.GarageRoom.SkyPrefix, dressing.Sky);
        Invalidate();
    }

    // The props the game draws in places of its own: everything else with a place in the garage is its dressing
    private static readonly string[] OwnPlaceProps = ["radio", WorkbenchLayout.PropId, LiftLayout.PropId, EngineStandLayout.PropId];

    // The renderer the dressing was put into
    private GarageRenderer? _dressingRenderer;

    /// <summary>
    /// The garage's dressing, every prop with a place in it (<see cref="Services.Garage.GarageProps.FindAll"/>): the models
    /// read off the UI thread, each once however many places it has, set into the renderer once. A model that cannot be
    /// read is logged and left out. The engine stand is put where the garage has it first.
    /// </summary>
    private async void ApplyGarageDressing()
    {
        var renderer = Renderer;
        if (renderer == null || ReferenceEquals(renderer, _dressingRenderer)) return;
        _dressingRenderer = renderer;

        // The engine stand where the garage has it, before the stand is first drawn
        var problems = new List<string>();
        var stand = Services.Garage.GarageProps.Find(Mods.ModSet.Installed, EngineStandLayout.PropId, ShowroomKn5, problems, builtIn: true);
        EngineStandLayout.PlaceAt(stand?.Position, stand?.Model);

        var props = Services.Garage.GarageProps.FindAll(Mods.ModSet.Installed, ShowroomKn5, OwnPlaceProps, problems);
        foreach (var problem in problems) Logger.Warning("Garage props: {Problem}", problem);
        Mods.ModProblems.Add("Garage props", problems);
        if (props.Count == 0) return;

        var models = await Task.Run(() =>
        {
            var read = new Dictionary<string, AcTools.Kn5File.IKn5>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in props.Select(p => p.Model).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    read[model] = AcTools.Kn5File.Kn5.FromFile(model);
                }
                catch (Exception ex)
                {
                    Logger.Warning("A garage prop could not be read from {Model}: {Error}", model, ex.Message);
                }
            }

            return read;
        });

        if (renderer != Renderer) return;
        renderer.SetDressing(props.Where(p => models.ContainsKey(p.Model)).Select(p => (models[p.Model], ToSlim(DressingPlacement(p, models[p.Model])))));
        Invalidate();
    }

    /// <summary>Where a prop of the dressing goes: a laid one stood back on its base (<see cref="Services.Garage.PlacedProp.PlacementOf"/>)</summary>
    internal static System.Numerics.Matrix4x4 DressingPlacement(Services.Garage.PlacedProp prop, AcTools.Kn5File.IKn5 kn5) =>
        prop.IsLaid && Services.Garage.GarageProps.BoundsOf(kn5) is { } bounds ? prop.PlacementOf(bounds.Min, bounds.Max) : prop.Placement;

    /// <summary>
    /// The radio on its bench, where the garage's props put it (<see cref="Services.Garage.GarageProps"/>): read off the UI
    /// thread, set into the renderer once. A garage with no place for it has none; a model that cannot be read is logged.
    /// </summary>
    private async void ApplyRadio()
    {
        var renderer = Renderer;
        if (renderer == null || ReferenceEquals(renderer, _radioRenderer)) return;
        _radioRenderer = renderer;

        await LoadPropAsync(renderer, "radio", builtIn: false, own: null, (kn5, radio) => renderer.SetFixture(kn5, ToSlim(radio.Placement)));
    }

    /// <summary>
    /// One of the garage's props (<see cref="Services.Garage.GarageProps.Find"/>) put into <paramref name="renderer"/>
    /// by <paramref name="set"/>: its model read off the UI thread (a mod's, or the game's <paramref name="own"/> drawn from
    /// boxes when the prop is <paramref name="builtIn"/> and no mod names a model). Runs from the load, so nothing may
    /// escape it: a garage whose prop cannot be put in goes on without it, and says so in the log. A place the garage
    /// has not got for it is no prop at all; a renderer gone in the meantime gets nothing.
    /// </summary>
    private async Task LoadPropAsync(GarageRenderer renderer, string propId, bool builtIn, Func<AcTools.Kn5File.IKn5>? own,
        Action<AcTools.Kn5File.IKn5, Services.Garage.PlacedProp> set)
    {
        try
        {
            var problems = new List<string>();
            var prop = Services.Garage.GarageProps.Find(Mods.ModSet.Installed, propId, ShowroomKn5, problems, builtIn);
            foreach (var problem in problems) Logger.Warning("Garage props: {Problem}", problem);
            Mods.ModProblems.Add("Garage props", problems);
            if (prop == null) return;

            AcTools.Kn5File.IKn5 kn5;
            try
            {
                kn5 = await Task.Run(() => prop.Model.Length > 0 || own == null ? AcTools.Kn5File.Kn5.FromFile(prop.Model) : own());
            }
            catch (Exception ex)
            {
                // A mod's model the renderer's reader refuses: the mod's problem, not the garage's
                Logger.Warning(ex, "The garage's {Prop} could not be read from {Model}", propId, prop.Model);
                return;
            }

            if (renderer != Renderer) return;
            set(kn5, prop);
            Invalidate();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "The garage's {Prop} could not be put in", propId);
        }
    }

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

    /// <summary>
    /// The car drawn once unseen, its pictures and its lights' sent to the card, ahead of a show the screen knows is coming
    /// (<see cref="Arrive"/>, the flow's enter script): done while the screen is still hidden, the show's first frame comes
    /// on time. Nothing with no car in the plain view.
    /// </summary>
    public void WarmForShow()
    {
        if (CarToDrive is { } node) WarmCar(node);
    }

    /// <summary>A script plays for <paramref name="trigger"/> at this moment with the car on show: a screen warms the car ahead of it (<see cref="WarmForShow"/>)</summary>
    public bool HasShowFor(string trigger, Func<Audio.EngineRunner?> engine, ScriptFacts facts) => CarToDrive != null && HasScript(trigger, engine, facts);

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

        // The camera holds still while the car moves, so it is settled on the car's place first: easing on toward it
        // under the script, it would be stopped short
        SettleOnTarget();

        return PlayScript(trigger, node, engine, facts, on =>
        {
            HeadlightsOn = on;
            AnimateFor(TimeSpan.FromSeconds(1));
        }, GarageScene.For(AppSettings.Instance.GarageId), fade);
    }

    #endregion

    #region Rendering

    protected override bool StepFrame(GarageRenderer renderer, float dt)
    {
        if (_followCarry && renderer.CarriedCentre is { } load)
        {
            HoldCamera();
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
        _cameraOnPart = false;
        _framingCar = false;
        _carFrame = null;
        _lookAt = null;
        _handHeld = false;
        _hand.Stop();
        _benchRenderer = null;
        _liftRenderer = null;
        _liftProp = null;
        QuietLift();
        _sceneryRenderer = null;
        _sceneryKey = null;
        _partsOnStand = false;
        _loadedCarDirectory = null;
        HasCar = false;
        HasDoors = false;
    }

    #endregion
}
