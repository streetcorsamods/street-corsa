using System.Reflection;
using AcTools.Kn5File;
using AcTools.Render.Base;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Objects;
using AcTools.Render.Base.PostEffects;
using AcTools.Render.Base.TargetTextures;
using AcTools.Render.Base.Shadows;
using AcTools.Render.Base.Utils;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5SpecificForward;
using AcTools.Render.Kn5SpecificForwardDark;
using AcTools.Render.Shaders;
using SlimDX;
using SlimDX.Direct3D11;
using SlimDX.DXGI;
using StreetCorsa.Parts;

namespace StreetCorsa.Controls;

/// <summary>Which of the two part models something belongs to</summary>
public enum PartLayer
{
    /// <summary>Parts that are on the car</summary>
    Mounted,

    /// <summary>A loose part shown in the places it could go</summary>
    Candidate
}

/// <summary>A part node of one of the part models, by its index (<see cref="PartAssembler.NodeName"/>)</summary>
public readonly record struct PartHit(PartLayer Layer, int Node);

/// <summary>
/// Part nodes that move as one, off the car or onto it, the way the part they are on comes off
/// (<see cref="WorkOrder.WayOf"/>), after <paramref name="Delay"/> seconds. An empty way is away from what stays.
/// <paramref name="Tool"/> is the tool its work-order kind is heard to take (<see cref="WorkOrder.ToolOf"/>). A part of the
/// car's running gear (<paramref name="CarAxes"/>) goes along the car's own axes, an engine's along its block's.
/// </summary>
public sealed record PartMove(IReadOnlyList<int> Nodes, IReadOnlyList<PartWay> Way, float Delay = 0f, string? Tool = null, bool CarAxes = false);

/// <summary>
/// Dark renderer with one extra model (the car's parts) in the scene, and an x-ray mode for the car.
/// The base renderer only knows about the showroom and car slots, so the prop rides along with the cars:
/// every pass that draws them (shadows, g-buffer, opaque, transparent) draws the prop too.
/// </summary>
public class GarageRenderer : DarkKn5ObjectRenderer
{
    // Dissolving the car also reveals the parts: at full opacity the solid car simply covers them
    private const float GhostTransitionSeconds = 0.8f;

    /// <summary>
    /// The most a frame moves anything on: the viewport only draws on demand, so the first tick after a pause can span
    /// seconds. Below 20 frames a second things go slower than the clock; whatever waits on them goes by them too.
    /// </summary>
    public const float MaxTickSeconds = 0.05f;

    // A part on its way off the car or onto it: it travels this far from its place, all its legs together, and
    // is gone at the far end
    public const float PartMotionSeconds = 0.7f;
    private const float PartTravel = 0.5f;

    // Opacities a fade is drawn at: fine enough not to be seen stepping
    private const float FadeSteps = 32f;

    // The hoist: slow up and down on the chain, quicker across the floor; the hook is this far above the block, and the
    // load swings on it by this share of what a free pendulum would as it speeds up and slows down
    private const float HoistLiftSpeed = 0.8f;
    private const float HoistTravelSpeed = 1.5f;
    private const float HoistMinLegSeconds = 0.6f;
    private const float HoistHookAbove = 0.7f;
    private const float HoistSwing = 0.25f;

    // The engine on the stand turns over on the stand's spindle, by hand: a half turn takes this long, a shorter one
    // less. It stays this far above the stand's base, looked for at every one of this many steps of the half turn.
    private const float EngineTurnSeconds = 1.6f;
    private const float TurnClearance = 0.02f;
    private const int TurnLimitSteps = 90;

    private readonly record struct Glow(float Red, float Green, float Blue, float Strength = 1f);

    private static readonly Glow CarGlow = new(1f, 1f, 1f);
    private static readonly Glow CandidateGlow = new(0.35f, 1f, 0.45f);
    private static readonly Glow SelectedGlow = new(1f, 0.62f, 0.15f);
    private static readonly Glow HoverGlow = new(1f, 0.9f, 0.55f);

    private Kn5RenderableFile? _prop;
    private Kn5RenderableFile? _scenery;
    private List<IKn5RenderableObject> _sceneryMeshes = new();

    // A prop set into the garage for good (the radio on the welder): drawn with the rest, and picked on its own
    private Kn5RenderableFile? _fixture;
    private List<IKn5RenderableObject> _fixtureMeshes = new();
    private bool _fixtureHovered;
    private Kn5RenderableFile? _bench;
    private List<IKn5RenderableObject> _benchMeshes = new();

    // The part a job of the parts view's "Work on it" is being done on: a loose one lying on the workbench (drawn whole,
    // at how far it is in sight), and over it, or over the mounted part itself, its grime, fading as the work goes
    private Kn5RenderableFile? _work;
    private Kn5RenderableFile? _workGrime;
    private bool _workFollowsParts;
    private float _workOpacity = 1f;
    private float _workGrimeAmount;
    private bool _sceneryHovered;
    private bool _carHovered;
    private bool _standActive;
    private Kn5RenderableFile? _candidates;
    private List<PartNode> _propNodes = new();

    // Mounted parts the player has put out of sight by their kind (the parts view's show buttons): not drawn, not picked.
    // Kept apart from the nodes' own on/off, which the motions own: a part hidden on its way somewhere goes on its way,
    // out of sight. Those fading out or back in, at how far they are in sight (0 gone, 1 in sight).
    private HashSet<PartNode> _hiddenNodes = new();
    private readonly Dictionary<PartNode, float> _hideFades = new();
    private List<PartNode> _candidateNodes = new();
    private PartHit? _hovered;
    private int? _selected;
    private readonly List<Motion> _motions = new();

    // The part nodes drawn see-through over the scene right now, at their step of the fade (0: not drawn at all, a
    // hidden part), and the filter that leaves them out of every other pass
    private Dictionary<RenderableList, float>? _fading;
    private Func<IRenderableObject, bool>? _settled;

    // UpdateFading's working, kept from frame to frame so a fade makes no garbage: the nodes fading as worked out now,
    // the nodes looked at already, and how far each moving part is in sight on its way (the first motion of a node's)
    private Dictionary<RenderableList, float> _fadingNow = new();
    private readonly HashSet<PartNode> _fadeLooked = new();
    private readonly Dictionary<PartNode, float> _travel = new();

    // Whether the prop and the candidates lean with the car's body: not the ones made for the stand
    private bool _propLeans;
    private bool _candidatesLean;
    private bool _ghostCar;
    private bool _removePropWhenSolid;
    private float _ghostProgress;
    private float _ghostOpacity = 0.3f;
    private bool _engineTurnedOver;

    // Where the prop was put, before the body's lean or the stand's turn
    private Matrix _propPlacement = Matrix.Identity;

    // How far the engine on the stand is turned over, in radians, and the leg of the turn it is on: from where, to where,
    // how long it has been on it and how long it takes
    private float _turnAngle;
    private float _turnFrom;
    private float _turnTo;
    private float _turnElapsed;
    private float _turnSeconds;

    // As far as the engine on the stand turns before any of it would go into the stand's base: a blown one only goes over
    // on its side
    private float _turnLimit = System.MathF.PI;

    // The stand's head in the scenery, which the engine is bolted to and turns with, and where it is upright
    private RenderableList? _standHead;
    private Matrix _standHeadHome = Matrix.Identity;

    // Scene copy with the solid car drawn over it, blended back at GhostOpacity
    private TargetResourceTexture? _ghostBuffer;

    // Scene copy with parts on their way drawn over it, blended back at how far gone they are
    private TargetResourceTexture? _fadeBuffer;
    private TargetResourceDepthTexture? _ghostDepth;
    private BlendState? _ghostBlend;
    private BlendState? _scaleBlend;

    // Glows: around the car's silhouette, so the shell reads even where the paint is as dark as the garage,
    // around the places a loose part could go, the selected part and the part the pointer is on.
    // One buffer each, all made from the same depth buffer in turn.
    private readonly TargetResourceTexture?[] _glowBuffers = new TargetResourceTexture?[4];
    private readonly bool[] _glowDrawn = new bool[4];
    private TargetResourceDepthTexture? _outlineDepth;

    public GarageRenderer(CarDescription? car, string? showroomKn5) : base(car, showroomKn5) { }

    /// <summary>
    /// Marks the shadow map as needing redrawing.
    ///
    /// The renderer works this out for itself when the car in its main slot changes, but it only listens to
    /// that one slot. On a lot every other car is in a slot it is not listening to, so the last car to
    /// arrive stands there without a shadow until something else happens to ask for one.
    /// </summary>
    public void RefreshShadows() => SetShadowsDirty();

    /// <summary>
    /// Where the sun's shadows centre instead of the main car's middle (the diner's lot, so the racers' cars parked
    /// there get theirs); null for the car's. Changed, the shadows are asked for again (<see cref="RefreshShadows"/>).
    /// </summary>
    public Vector3? ShadowsCentre { get; set; }

    // The base draws the shadows again whenever the main car's middle is not where they were last centred: told the
    // car's middle after drawing them round another point, it only does so when the car moves or the scene changes
    internal static readonly FieldInfo? PreviousShadowsTarget =
        typeof(ForwardKn5ObjectRenderer).GetField("_previousShadowsTarget", BindingFlags.NonPublic | BindingFlags.Instance);

    // A renderer built from an AcTools without that field draws the shadows round the car: said once, not every frame
    private static bool _shadowsFieldMissingTold;

    protected override void UpdateShadows(ShadowsDirectional shadows, Vector3 center)
    {
        if (ShadowsCentre is not { } wanted || PreviousShadowsTarget == null)
        {
            if (ShadowsCentre != null && PreviousShadowsTarget == null && !_shadowsFieldMissingTold)
            {
                _shadowsFieldMissingTold = true;
                Logging.AppLoggerFactory.CreateLogger("Renderer").Warning(
                    "AcTools.Render has no _previousShadowsTarget: the shadows stay centred on the main car, and cars parked away from it may stand without theirs");
            }

            base.UpdateShadows(shadows, center);
            return;
        }

        base.UpdateShadows(shadows, wanted);
        PreviousShadowsTarget.SetValue(this, (Vector3?)center);
    }

    /// <summary>
    /// Works the scene's boxes out again after cars were moved by hand. Setting a slot's matrix leaves its box where
    /// the car was, and the shadows (centred on the main slot's box, drawn only for cars whose box a shadow cascade
    /// takes in) and the reflections follow the boxes: a moved car can end up casting no shadow at all.
    /// </summary>
    public void RefreshBounds()
    {
        Scene.UpdateBoundingBox();
        foreach (var slot in CarSlots) slot.ResetCarBoundingBox();
        SetShadowsDirty();
    }

    public bool HasProp => _prop != null;

    /// <summary>
    /// Where <c>AutoAdjustTarget</c> would settle the camera's target for an orbit, starting from a point: it moves a
    /// third of the way to the point that centres the car in the picture every frame, which is a jump after a long
    /// way. The viewport eases there instead and hands over once there. The camera is left as it was.
    /// </summary>
    public Vector3 FramedTarget(float alpha, float beta, float radius, Vector3 start)
    {
        var orbit = CameraOrbit;
        if (orbit == null) return start;

        var (oldAlpha, oldBeta, oldRadius, oldTarget) = (orbit.Alpha, orbit.Beta, orbit.Radius, orbit.Target);
        try
        {
            (orbit.Alpha, orbit.Beta, orbit.Radius, orbit.Target) = (alpha, beta, radius, start);
            for (var i = 0; i < 40; i++)
            {
                var next = orbit.Target + (AutoAdjustedTarget - orbit.Target) / 3f;
                if ((next - orbit.Target).LengthSquared() < 1e-8f) break;
                orbit.Target = next;
            }

            return orbit.Target;
        }
        finally
        {
            (orbit.Alpha, orbit.Beta, orbit.Radius, orbit.Target) = (oldAlpha, oldBeta, oldRadius, oldTarget);
            orbit.UpdateViewMatrix();
        }
    }

    private Matrix _bodyLean = Matrix.Identity;

    /// <summary>
    /// How the running engine leans the body right now. The parts sit in the car without being part of its model, so
    /// they are leaned along with it here; the stand's are not, nor is a load hanging on the hoist.
    /// </summary>
    public Matrix BodyLean
    {
        get => _bodyLean;
        set
        {
            _bodyLean = value;
            var leaned = false;
            if (_prop != null && _propLeans)
            {
                _prop.LocalMatrix = _propPlacement * value;
                leaned = true;
            }

            if (_candidates != null && _candidatesLean)
            {
                _candidates.LocalMatrix = value;
                leaned = true;
            }

            if (leaned) IsDirty = true;
        }
    }

    /// <summary>What undoes the body's lean for a node of the prop: a load off the car's springs hangs still</summary>
    private Matrix OffBody => _propLeans ? Matrix.Invert(_bodyLean) : Matrix.Identity;

    /// <summary>
    /// The prop is the engine on the stand beside the car, not the car's parts: they are picked and glow with the car
    /// solid, and do not lean with the car's body
    /// </summary>
    public bool StandActive
    {
        get => _standActive;
        set
        {
            if (_standActive == value) return;

            // Off the stand the engine is upright at once, whatever was asked of it: a prop kept for the hoist goes from
            // there. This is the one place that holds to it; everything else only asks.
            if (!value && _turnAngle != 0f)
            {
                _turnAngle = _turnFrom = _turnTo = 0f;
                ApplyEngineTurn();
            }

            _standActive = value;
            _hovered = null;
            IsDirty = true;
        }
    }

    /// <summary>
    /// The engine on the stand is to be upside down, its oil pan up: it turns over on the stand's spindle, the
    /// crankshaft's line, with whatever is on it and the stand's head it is bolted to. As far as the stand's base lets
    /// it, and only while the stand is worked on; off it, the engine is upright at once.
    /// </summary>
    public bool EngineTurnedOver
    {
        get => _engineTurnedOver;
        set
        {
            if (_engineTurnedOver == value) return;
            _engineTurnedOver = value;
            IsDirty = true;
        }
    }

    /// <summary>How far the engine on the stand is to be turned, in radians</summary>
    private float TurnTarget => _engineTurnedOver && _standActive ? _turnLimit : 0f;

    /// <summary>True while the engine on the stand is turning over, or back, or waits for parts to come off first</summary>
    public bool IsTurningEngine => _turnAngle != TurnTarget;

    /// <summary>Seconds of drawing until the engine on the stand is turned where it is to be; 0 once it is</summary>
    public float TurnSecondsLeft
    {
        get
        {
            var target = TurnTarget;
            if (_turnAngle == target) return 0f;

            var leg = target == _turnTo ? Math.Max(0f, _turnSeconds - _turnElapsed) : TurnSeconds(_turnAngle, target);
            return leg + (IsHoldingTurn ? MotionSecondsLeft : 0f);
        }
    }

    /// <summary>The engine on the stand where it is to be, without turning it there: a stand laid out anew</summary>
    public void SettleEngineTurn()
    {
        _turnAngle = _turnFrom = _turnTo = TurnTarget;
        ApplyEngineTurn();
    }

    /// <summary>
    /// How far the engine on the stand is turned, as a matrix for the stand's models: about the spindle, which runs
    /// along the crankshaft through the stand's head
    /// </summary>
    private Matrix StandTurn
    {
        get
        {
            if (_turnAngle == 0f) return Matrix.Identity;

            var spindle = Spindle;
            return Matrix.Translation(-spindle) * Matrix.RotationZ(_turnAngle) * Matrix.Translation(spindle);
        }
    }

    private static Vector3 Spindle => new(EngineStandLayout.Spindle.X, EngineStandLayout.Spindle.Y, EngineStandLayout.Spindle.Z);

    private static float TurnSeconds(float from, float to) => EngineTurnSeconds * Math.Abs(to - from) / System.MathF.PI;

    /// <summary>
    /// Parts coming off the engine on the stand go their way from where it is: it does not turn under them, or their up
    /// would become down on the way
    /// </summary>
    private bool IsHoldingTurn => _motions.Any(m => m is PartMotion { IsArriving: false });

    // The crank has been heard for the leg the engine is turning on
    private bool _turnHeard;

    private void TickEngineTurn(float dt)
    {
        // Asked for somewhere else: a new leg from where it is, eased at both ends
        var target = TurnTarget;
        if (target != _turnTo)
        {
            _turnFrom = _turnAngle;
            _turnTo = target;
            _turnElapsed = 0f;
            _turnSeconds = TurnSeconds(_turnFrom, _turnTo);
            _turnHeard = false;
        }

        if (_turnAngle == _turnTo || IsHoldingTurn) return;

        // The stand's crank, as the engine starts to turn (not while it waits for parts to come off)
        if (!_turnHeard)
        {
            _turnHeard = true;
            Audio.Sfx.Play(Audio.Sfx.EngineTurns);
        }

        _turnElapsed += Math.Min(dt, MaxTickSeconds);
        _turnAngle = _turnElapsed >= _turnSeconds ? _turnTo : _turnFrom + (_turnTo - _turnFrom) * Ease(_turnElapsed / _turnSeconds);
        ApplyEngineTurn();
    }

    /// <summary>The stand's models turned as far as the engine is; the car's parts never are</summary>
    private void ApplyEngineTurn()
    {
        var turn = StandTurn;
        var turned = false;
        if (_prop != null && !_propLeans)
        {
            _prop.LocalMatrix = _propPlacement * turn;

            // Parts on their way work the box out again this tick anyway
            if (_motions.Count == 0) _prop.UpdateBoundingBox();
            turned = true;
        }

        if (_candidates != null && !_candidatesLean)
        {
            _candidates.LocalMatrix = turn;
            _candidates.UpdateBoundingBox();
            turned = true;
        }

        if (_standHead != null)
        {
            _standHead.LocalMatrix = _standHeadHome * turn;
            _scenery?.UpdateBoundingBox();
            turned = true;
        }

        if (!turned) return;

        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>
    /// How far the engine on the stand, upright now, can turn over before any of its parts would go into the stand's
    /// base or the floor, on its way or at the end: all the way for most, on its side for one with a blower on top.
    /// Goes by the parts' boxes, so it errs on the safe side.
    /// </summary>
    private float TurnLimit()
    {
        if (_prop == null) return System.MathF.PI;

        _prop.UpdateBoundingBox();
        var spindle = Spindle;
        var floor = EngineStandLayout.BaseTop + TurnClearance - spindle.Y;
        var corners = new List<(float X, float Y)>();
        foreach (var box in _propNodes.Select(n => n.List.BoundingBox).Where(b => b.HasValue).Select(b => b!.Value))
        {
            foreach (var x in new[] { box.Minimum.X, box.Maximum.X })
            foreach (var y in new[] { box.Minimum.Y, box.Maximum.Y })
                corners.Add((x - spindle.X, y - spindle.Y));
        }

        // Turned by an angle about Z, a point is x sin + y cos above the spindle
        for (var step = 1; step <= TurnLimitSteps; step++)
        {
            var (sin, cos) = System.MathF.SinCos(System.MathF.PI * step / TurnLimitSteps);
            if (corners.Any(c => c.X * sin + c.Y * cos < floor)) return System.MathF.PI * (step - 1) / TurnLimitSteps;
        }

        return System.MathF.PI;
    }

    /// <summary>True while the parts can be picked and glow: the car is a ghost, or the stand is being worked on</summary>
    private bool IsWorking => IsGhosting || _standActive;

    /// <summary>
    /// A model that is always there and never picked: the engine stand beside the car, with the engine on it.
    /// Must be called on the thread that draws.
    /// </summary>
    public void SetScenery(IKn5? kn5)
    {
        _scenery?.Dispose();
        _scenery = kn5 == null ? null : new Kn5RenderableFile(kn5, Matrix.Identity, asyncTexturesLoading: false);
        _sceneryMeshes = _scenery?.GetAllChildren().OfType<IKn5RenderableObject>().ToList() ?? new();

        // The stand's head turns with the engine bolted to it
        _standHead = _scenery?.GetDummyByName(EngineStandLayout.HeadNode);
        _standHeadHome = _standHead?.LocalMatrix ?? Matrix.Identity;
        if (_standHead != null && _turnAngle != 0f)
        {
            _standHead.LocalMatrix = _standHeadHome * StandTurn;
            _scenery!.UpdateBoundingBox();
        }

        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>
    /// Draws the car as a faint shell over whatever is inside it. Materials force their own blend state,
    /// so instead of fading them the solid car goes to a separate buffer that is blended over the scene;
    /// as a bonus only the nearest surface shows, not every layer of the interior.
    /// </summary>
    public bool GhostCar
    {
        get => _ghostCar;
        set
        {
            if (_ghostCar == value) return;
            _ghostCar = value;
            if (value) _removePropWhenSolid = false;
            IsDirty = true;
        }
    }

    /// <summary>True while the car is anything but solid, transitions included</summary>
    private bool IsGhosting => _ghostProgress > 0f;

    /// <summary>Opacity of the shell right now: from solid down to GhostOpacity as the transition runs</summary>
    private float CurrentGhostOpacity => 1f + (_ghostOpacity - 1f) * Ease(_ghostProgress);

    private static float Ease(float t) => t * t * (3f - 2f * t);

    public float GhostOpacity
    {
        get => _ghostOpacity;
        set
        {
            _ghostOpacity = Math.Clamp(value, 0f, 1f);
            IsDirty = true;
        }
    }

    /// <summary>Brings the car back to solid, then drops the prop it was covering up again</summary>
    public void HideParts()
    {
        if (!IsGhosting)
        {
            GhostCar = false;
            SetProp(null, Matrix.Identity);
            return;
        }

        GhostCar = false;
        _removePropWhenSolid = true;
    }

    /// <summary>True while parts are on their way off the car or onto it</summary>
    public bool IsMovingParts => _motions.Count > 0;

    /// <summary>Seconds until every part on its way is there</summary>
    public float MotionSecondsLeft => _motions.Count == 0 ? 0f : _motions.Max(m => m.SecondsLeft);

    /// <summary>
    /// Sets mounted parts in motion, given by node: arriving parts start away from their place, see-through, and settle
    /// into it; leaving parts fade out on their way off and are gone at the end (until the model is replaced by one
    /// without them). Each move goes its own way, all its nodes together.
    /// </summary>
    /// <param name="frameNode">The engine block, whose axes the ways are along; null for the car's</param>
    public void MoveParts(IReadOnlyList<PartMove> moves, bool arriving, int? frameNode)
    {
        if (_prop == null) return;

        _prop.UpdateBoundingBox();
        var groups = moves
            .Select(m => (Move: m, Nodes: m.Nodes.Where(n => n >= 0 && n < _propNodes.Count).Select(n => _propNodes[n]).Distinct().ToList()))
            .Where(g => g.Nodes.Count > 0)
            .ToList();
        if (groups.Count == 0) return;

        // The ways are in the prop's own space, as the nodes' matrices are, the boxes in the garage's: an engine turned
        // over on the stand has its oil pan come off upwards, and out still on the side the part is on
        var toProp = Matrix.Invert(_prop.LocalMatrix);
        var moving = groups.SelectMany(g => g.Nodes).ToHashSet();
        var staying = CentreOf(_propNodes.Where(n => !moving.Contains(n)));
        var frame = frameNode is { } f && f >= 0 && f < _propNodes.Count ? _propNodes[f] : null;

        // Part models face -Z; with no block to go by, one set in the car the way the car faces (+Z). The running gear goes
        // by the car's axes whatever way its engine sits
        var carAxes = Matrix.RotationY((float)Math.PI);
        var blockAxes = frame?.List.LocalMatrix ?? carAxes;

        foreach (var (move, nodes) in groups)
        {
            if (CentreOf(nodes) is not { } centre) continue;

            var axes = move.CarAxes ? carAxes : blockAxes;
            var right = Direction(Vector3.TransformNormal(Vector3.UnitX, axes), Vector3.UnitX);
            var up = Direction(Vector3.TransformNormal(Vector3.UnitY, axes), Vector3.UnitY);
            var forward = Direction(Vector3.TransformNormal(-Vector3.UnitZ, axes), Vector3.UnitZ);

            // Out is to the side of the engine the part is on, or of the car for its running gear; the block itself has
            // none, so it goes by what stays
            var middle = frame == null || move.CarAxes || nodes.Contains(frame) ? staying : CentreOf(new[] { frame });
            var outward = middle is { } m && Vector3.Dot(Vector3.TransformNormal(centre - m, toProp), right) < 0f ? -right : right;

            var legs = move.Way.Count == 0
                ? new[] { AwayFrom(centre) * PartTravel }
                : move.Way.Select(way => PartTravel / move.Way.Count * way switch
                {
                    PartWay.Up => up,
                    PartWay.Down => -up,
                    PartWay.Forward => forward,
                    PartWay.Back => -forward,
                    _ => outward
                }).ToArray();

            // One sound a part, however many nodes it is: the tool as it comes off; the clank as it goes into its
            // place, and the tool turning it tight
            var tool = Audio.Sfx.ToolFor(move.Tool);
            var first = true;
            foreach (var node in nodes)
            {
                _motions.RemoveAll(motion => ReferenceEquals(motion.Node, node));
                var motion = new PartMotion(node, node.List.LocalMatrix, legs, arriving, move.Delay);
                if (first && !arriving) motion.StartCues = [(tool, 0f)];
                if (first && arriving) motion.EndCues = [(Audio.Sfx.Clank, 0f), (tool, 0.18f)];
                first = false;
                _motions.Add(motion);
                motion.Apply();
            }
        }

        if (!arriving && _selected is { } selected && selected < _propNodes.Count && moving.Contains(_propNodes[selected])) _selected = null;
        _hovered = null;

        // Parts on their way are drawn on their own, without a shadow
        SetShadowsDirty();
        UpdateFading();
        IsDirty = true;

        // A part of no known kind goes away from the middle of what stays: mostly up and out, never far down, the
        // floor is right there. Up is the garage's, whichever way the engine is turned: it does not turn while parts come
        // off (IsHoldingTurn).
        Vector3 AwayFrom(Vector3 centre)
        {
            var away = staying is { } middle ? centre - middle : Vector3.Zero;
            away.Y = Math.Max(away.Y, 0f) + 0.15f;
            return Direction(Vector3.TransformNormal(Vector3.Normalize(away), toProp), Vector3.UnitY);
        }
    }

    /// <summary>
    /// Carries part nodes on the hoist: an engine out of its bay onto the stand, or back. The nodes are where
    /// <paramref name="home"/> puts the block now; they go from <paramref name="from"/> to <paramref name="to"/> and
    /// stay there, whole and solid, until the model is replaced.
    /// </summary>
    /// <param name="clearHeight">How high the bottom of the load has to be on the way across: over the car's wings</param>
    /// <param name="delay">Seconds to wait first: what comes off before the engine does</param>
    /// <returns>Seconds until the load is down; 0 when there is nothing to carry</returns>
    public float CarryParts(IReadOnlyList<int> nodes, Matrix home, Matrix from, Matrix to, float clearHeight, float delay)
    {
        if (_prop == null) return 0f;

        _prop.UpdateBoundingBox();
        var carried = nodes.Where(n => n >= 0 && n < _propNodes.Count).Select(n => _propNodes[n]).Distinct().ToList();
        var boxes = carried.Select(n => n.List.BoundingBox).Where(b => b.HasValue).Select(b => b!.Value).ToList();
        if (boxes.Count == 0) return 0f;

        var box = boxes.Aggregate(BoundingBox.Merge);
        var origin = new Vector3(home.M41, home.M42, home.M43);
        var height = Math.Max(Math.Max(from.M42, to.M42), clearHeight + origin.Y - box.Minimum.Y);
        var hoist = new Hoist(home, from, to, height, (box.Minimum + box.Maximum) / 2f - origin);

        // The chain as the hoist takes the weight, and the load landing where it goes
        var first = true;
        foreach (var node in carried)
        {
            _motions.RemoveAll(m => ReferenceEquals(m.Node, node));
            var motion = new CarryMotion(this, node, node.List.LocalMatrix, hoist, delay);
            if (first)
            {
                motion.StartCues = [(Audio.Sfx.Hoist, 0f)];
                motion.EndCues = [(Audio.Sfx.EngineLands, 0f)];
                first = false;
            }
            _motions.Add(motion);
            motion.Apply();
        }

        if (_selected is { } selected && selected < _propNodes.Count && carried.Contains(_propNodes[selected])) _selected = null;
        _hovered = null;
        SetShadowsDirty();
        UpdateFading();
        IsDirty = true;
        return delay + hoist.Seconds;
    }

    /// <summary>The middle of what the hoist is carrying right now; null while it carries nothing, or waits to</summary>
    public Vector3? CarriedCentre => _motions.OfType<CarryMotion>().FirstOrDefault(m => m.HasStarted && !m.IsDone)?.Centre;

    private static Vector3? CentreOf(IEnumerable<PartNode> nodes)
    {
        var centres = nodes.Select(n => n.List.BoundingBox).Where(b => b.HasValue).Select(b => b!.Value.GetCenter()).ToList();
        return centres.Count == 0 ? null : centres.Aggregate(Vector3.Zero, (sum, c) => sum + c) / centres.Count;
    }

    private static Vector3 Direction(Vector3 vector, Vector3 fallback) =>
        vector.LengthSquared() > 1e-6f ? Vector3.Normalize(vector) : fallback;

    /// <summary>
    /// Puts mounted parts' nodes where they now belong, at once: the placement mode nudging a part. One pass over
    /// the bounds and shadows for the lot, a nudge moves every part downstream of the slot.
    /// </summary>
    public void PlaceParts(IEnumerable<(int Node, Matrix World)> moves)
    {
        if (_prop == null) return;

        var moved = false;
        foreach (var (node, world) in moves)
        {
            if (node < 0 || node >= _propNodes.Count) continue;

            _motions.RemoveAll(m => ReferenceEquals(m.Node, _propNodes[node]));
            _propNodes[node].List.LocalMatrix = world;
            moved = true;
        }

        if (!moved) return;

        _prop.UpdateBoundingBox();
        SetShadowsDirty();
        UpdateFading();
        IsDirty = true;
    }

    private void TickPartMotions(float dt)
    {
        var step = Math.Min(dt, MaxTickSeconds);
        TickHideFades(step);
        if (_motions.Count == 0) return;

        foreach (var motion in _motions)
        {
            motion.Advance(step);
            motion.PlayCues();
        }

        // A load on the hoist casts its shadow all the way; the shadows change otherwise only as a part gets somewhere,
        // or starts or stops fading (UpdateFading)
        var carrying = _motions.Any(m => m is CarryMotion && m.HasStarted);
        if (_motions.RemoveAll(m => m.IsDone) > 0 || carrying) SetShadowsDirty();
        UpdateFading();

        _prop?.UpdateBoundingBox();
        IsDirty = true;
    }

    /// <summary>
    /// Works out which nodes are drawn see-through now, once a frame rather than for every pass. A part that starts to
    /// fade drops its shadow then, not when it is gone.
    /// </summary>
    private void UpdateFading()
    {
        _travel.Clear();
        foreach (var motion in _motions)
        {
            if (motion is PartMotion part) _travel.TryAdd(part.Node, part.Opacity);
        }

        var fading = _fadingNow;
        fading.Clear();
        _fadeLooked.Clear();
        foreach (var motion in _motions) LookAt(motion.Node);
        foreach (var node in _hiddenNodes) LookAt(node);
        foreach (var node in _hideFades.Keys) LookAt(node);

        if (fading.Count == 0)
        {
            if (_fading == null) return;
            _fading = null;
            _settled = null;
            SetShadowsDirty();
            return;
        }

        // The same nodes fading: only how far each is along changes, and the filter on every other pass stays
        if (_fading != null && _fading.Count == fading.Count && SameKeys(fading, _fading))
        {
            foreach (var (list, step) in fading) _fading[list] = step;
            return;
        }

        // The worked-out set becomes the one drawn; the one drawn before is the working for the next frame
        _fadingNow = _fading ?? new();
        _fading = fading;
        _settled = LeavingOut(fading);
        SetShadowsDirty();

        void LookAt(PartNode node)
        {
            if (!_fadeLooked.Add(node) || !node.List.IsEnabled) return;
            var step = (float)Math.Round(OpacityOf(node) * FadeSteps);
            if (step < FadeSteps) fading[node.List] = step;
        }

        static bool SameKeys(Dictionary<RenderableList, float> a, Dictionary<RenderableList, float> b)
        {
            foreach (var key in a.Keys)
            {
                if (!b.ContainsKey(key)) return false;
            }
            return true;
        }
    }

    /// <summary>The filter that leaves the <paramref name="fading"/> nodes out of every other pass</summary>
    private static Func<IRenderableObject, bool> LeavingOut(Dictionary<RenderableList, float> fading) =>
        o => o is not RenderableList list || !fading.ContainsKey(list);

    /// <summary>
    /// How much of a part node is in sight, drawn see-through over the scene below 1 (<see cref="DrawFading"/>): the way
    /// it is going (a part fades as it travels, as UpdateFading has found its motion) and whether the player hid it
    /// </summary>
    private float OpacityOf(PartNode node)
    {
        var travel = _travel.TryGetValue(node, out var opacity) ? opacity : 1f;
        var shown = _hideFades.TryGetValue(node, out var fade) ? fade : _hiddenNodes.Contains(node) ? 0f : 1f;
        return travel * shown;
    }

    /// <summary>The parts going out of sight or coming back, a step further; each is done with once it gets there</summary>
    private void TickHideFades(float step)
    {
        if (_hideFades.Count == 0) return;

        foreach (var node in _hideFades.Keys.ToList())
        {
            var to = _hiddenNodes.Contains(node) ? 0f : 1f;
            var at = _hideFades[node];
            at = to > at ? Math.Min(to, at + step / PartMotionSeconds) : Math.Max(to, at - step / PartMotionSeconds);
            if (at == to) _hideFades.Remove(node);
            else _hideFades[node] = at;
        }

        if (_motions.Count == 0)
        {
            UpdateFading();
            IsDirty = true;
        }
    }

    /// <summary>A part node on its way somewhere, once it has waited its turn</summary>
    private abstract class Motion
    {
        private readonly float _delay;
        private readonly float _seconds;
        private float _elapsed;

        protected Motion(PartNode node, Matrix home, float delay, float seconds)
        {
            Node = node;
            Home = home;
            _delay = Math.Max(0f, delay);
            _seconds = seconds;
        }

        public PartNode Node { get; }

        /// <summary>The node where it was put</summary>
        protected Matrix Home { get; }

        public bool HasStarted => _elapsed > _delay;

        public bool IsDone => _elapsed >= _delay + _seconds;

        public float SecondsLeft => Math.Max(0f, _delay + _seconds - _elapsed);

        /// <summary>Seconds on its way: 0 while it waits</summary>
        protected float Time => Math.Clamp(_elapsed - _delay, 0f, _seconds);

        /// <summary>0 while it waits, 1 when it is there</summary>
        protected float Progress => _seconds > 0f ? Time / _seconds : 1f;

        public void Advance(float dt)
        {
            _elapsed += dt;
            Apply();
        }

        /// <summary>Sounds when it sets off, and when it gets there, each after its delay in seconds</summary>
        public (string Cue, float Delay)[] StartCues { get; set; } = [];

        public (string Cue, float Delay)[] EndCues { get; set; } = [];

        private bool _startHeard;
        private bool _endHeard;

        /// <summary>The sounds that are due, each once: after every advance</summary>
        public void PlayCues()
        {
            if (HasStarted && !_startHeard)
            {
                _startHeard = true;
                foreach (var (cue, delay) in StartCues) Audio.Sfx.Play(cue, delay);
            }

            if (IsDone && !_endHeard)
            {
                _endHeard = true;
                foreach (var (cue, delay) in EndCues) Audio.Sfx.Play(cue, delay);
            }
        }

        public abstract void Apply();
    }

    /// <summary>One part node travelling between its place and a point away from it, leg by leg, fading as it goes</summary>
    private sealed class PartMotion : Motion
    {
        private readonly Vector3[] _legs;
        private readonly float _length;
        private readonly bool _arriving;

        public PartMotion(PartNode node, Matrix home, Vector3[] legs, bool arriving, float delay)
            : base(node, home, delay, PartMotionSeconds)
        {
            _legs = legs;
            _length = legs.Sum(l => l.Length());
            _arriving = arriving;
        }

        public bool IsArriving => _arriving;

        /// <summary>1 in its place, 0 at the far end</summary>
        public float Opacity { get; private set; }

        public override void Apply()
        {
            // 0 = in its place, 1 = away. An arriving part comes in the way a leaving one goes, backwards.
            var away = _arriving ? 1f - Ease(Progress) : Ease(Progress);
            Opacity = 1f - away;
            if (IsDone)
            {
                Node.List.LocalMatrix = Home;
                Node.List.IsEnabled = _arriving;
                return;
            }

            Node.List.IsEnabled = true;
            Node.List.LocalMatrix = Home * Matrix.Translation(Along(away * _length));
        }

        /// <summary>The point that far along the legs, from the part's place</summary>
        private Vector3 Along(float distance)
        {
            var offset = Vector3.Zero;
            foreach (var leg in _legs)
            {
                var length = leg.Length();
                if (distance < length)
                {
                    offset += leg * (distance / length);
                    break;
                }

                offset += leg;
                distance -= length;
            }

            return offset;
        }
    }

    /// <summary>One part node of a load on the hoist</summary>
    private sealed class CarryMotion : Motion
    {
        private readonly GarageRenderer _owner;
        private readonly Hoist _hoist;

        public CarryMotion(GarageRenderer owner, PartNode node, Matrix home, Hoist hoist, float delay) : base(node, home, delay, hoist.Seconds)
        {
            _owner = owner;
            _hoist = hoist;
        }

        public Vector3 Centre => _hoist.CentreAt(Time);

        public override void Apply()
        {
            Node.List.IsEnabled = true;
            var pose = Home * _hoist.OffsetAt(Time);

            // In the car it goes with the body; on the chain, and on the stand, the car's bouncing is none of its business
            var inCar = IsDone ? _hoist.EndsHome : !HasStarted && _hoist.StartsHome;
            Node.List.LocalMatrix = inCar ? pose : pose * _owner.OffBody;
        }
    }

    /// <summary>
    /// The way a load goes on the hoist: straight up out of where it sits, across at a height clear of the car, and
    /// straight down into its new place, each leg from a standstill to a standstill. It turns to its new heading on the
    /// way across, and swings back a little on the chain as it speeds up, forward as it slows down.
    /// </summary>
    private sealed class Hoist
    {
        private readonly Vector3[] _points;
        private readonly float[] _seconds;
        private readonly Quaternion _from;
        private readonly Quaternion _to;
        private readonly Matrix _inverseHome;
        private readonly Vector3 _centre;

        /// <param name="home">Where the block is as the nodes are placed</param>
        /// <param name="centre">The middle of the load, from the block's origin</param>
        public Hoist(Matrix home, Matrix from, Matrix to, float height, Vector3 centre)
        {
            _inverseHome = Matrix.Invert(home);
            _centre = centre;
            _from = Quaternion.RotationMatrix(from);
            _to = Quaternion.RotationMatrix(to);

            var start = new Vector3(from.M41, from.M42, from.M43);
            var end = new Vector3(to.M41, to.M42, to.M43);
            _points = new[] { start, new Vector3(start.X, height, start.Z), new Vector3(end.X, height, end.Z), end };
            _seconds = new[]
            {
                Math.Max(HoistMinLegSeconds, (height - start.Y) / HoistLiftSpeed),
                Math.Max(HoistMinLegSeconds, Vector3.Distance(_points[1], _points[2]) / HoistTravelSpeed),
                Math.Max(HoistMinLegSeconds, (height - end.Y) / HoistLiftSpeed)
            };
            Seconds = _seconds.Sum();
            StartsHome = from.Equals(home);
            EndsHome = to.Equals(home);
        }

        public float Seconds { get; }

        /// <summary>The load starts where the nodes are placed (in the car, for the parts in it)</summary>
        public bool StartsHome { get; }

        /// <summary>The load ends where the nodes are placed</summary>
        public bool EndsHome { get; }

        /// <summary>What to put after a node's own placement to have the block where the hoist has it</summary>
        public Matrix OffsetAt(float time)
        {
            var (position, turn, leg, share) = At(time);
            var pose = Matrix.RotationQuaternion(Quaternion.Slerp(_from, _to, turn)) * Matrix.Translation(position);

            // Across, the load lags behind the hook as it speeds up and runs ahead of it as it slows down. The ease
            // speeds up then slows down, so it is swung by its second derivative: 6 (1 - 2u) D / T^2.
            if (leg == 1)
            {
                var direction = _points[2] - _points[1];
                var distance = direction.Length();
                if (distance > 1e-3f)
                {
                    direction /= distance;
                    var acceleration = 6f * (1f - 2f * share) * distance / (_seconds[1] * _seconds[1]);
                    var angle = HoistSwing * (float)Math.Atan(acceleration / 9.81f);
                    var hook = position + Vector3.UnitY * HoistHookAbove;
                    pose *= Matrix.Translation(-hook) * Matrix.RotationAxis(Vector3.Cross(Vector3.UnitY, direction), angle) * Matrix.Translation(hook);
                }
            }

            return _inverseHome * pose;
        }

        /// <summary>The middle of the load, for the camera to follow</summary>
        public Vector3 CentreAt(float time) => At(time).Position + _centre;

        /// <summary>Where the block is, how far it has turned, on which leg and how far along it</summary>
        private (Vector3 Position, float Turn, int Leg, float Share) At(float time)
        {
            for (var leg = 0; leg < _seconds.Length; leg++)
            {
                if (time > _seconds[leg] && leg < _seconds.Length - 1)
                {
                    time -= _seconds[leg];
                    continue;
                }

                var share = Math.Clamp(time / _seconds[leg], 0f, 1f);
                var eased = Ease(share);
                var turn = leg == 0 ? 0f : leg == 1 ? eased : 1f;
                return (Vector3.Lerp(_points[leg], _points[leg + 1], eased), turn, leg, share);
            }

            return (_points[^1], 1f, _seconds.Length - 1, 1f);
        }
    }

    protected override void OnTickOverride(float dt)
    {
        base.OnTickOverride(dt);
        TickEngineTurn(dt);
        TickPartMotions(dt);

        var target = _ghostCar ? 1f : 0f;
        if (_ghostProgress == target) return;

        // The viewport only draws on demand, so the first tick after a pause can span seconds
        var step = Math.Min(dt, MaxTickSeconds) / GhostTransitionSeconds;
        var wasGhosting = IsGhosting;
        _ghostProgress = _ghostCar ? Math.Min(1f, _ghostProgress + step) : Math.Max(0f, _ghostProgress - step);

        // A ghost casts no shadow, or the parts inside would sit in the dark
        if (wasGhosting != IsGhosting) SetShadowsDirty();

        if (!IsGhosting && _removePropWhenSolid)
        {
            _removePropWhenSolid = false;
            SetProp(null, Matrix.Identity);
        }

        IsDirty = true;
    }

    /// <summary>
    /// The mounted parts out of sight, given by node: those newly hidden fade out wherever they are, those shown again
    /// fade back in, at once without <paramref name="fade"/>. A part on its way somewhere goes on its way, in sight or
    /// not. No sound: nothing comes off, they are only not looked at. A new prop shows everything until it is told again.
    /// </summary>
    public void SetHiddenParts(IReadOnlyCollection<int> nodes, bool fade)
    {
        var hidden = nodes.Where(n => n >= 0 && n < _propNodes.Count).Select(n => _propNodes[n]).ToHashSet();
        var going = hidden.Where(n => !_hiddenNodes.Contains(n)).ToList();
        var coming = _hiddenNodes.Where(n => !hidden.Contains(n) && _propNodes.Contains(n)).ToList();
        _hiddenNodes = hidden;
        if (going.Count == 0 && coming.Count == 0) return;

        foreach (var node in going.Concat(coming))
        {
            node.IsHidden = hidden.Contains(node);
            if (!fade) _hideFades.Remove(node);
            else if (!_hideFades.ContainsKey(node)) _hideFades[node] = node.IsHidden ? 1f : 0f;
        }

        if (_selected is { } selected && selected < _propNodes.Count && hidden.Contains(_propNodes[selected])) _selected = null;
        _hovered = null;
        SetShadowsDirty();
        UpdateFading();
        IsDirty = true;
    }

    /// <summary>Replaces the prop; pass null to remove it. Must be called on the thread that draws.</summary>
    public void SetProp(IKn5? kn5, Matrix placement)
    {
        // The parts first shown in the car are put in while it is still solid and only seen as it fades to a ghost
        // (GhostTransitionSeconds): their textures can load off this thread meanwhile rather than hold up the frame.
        // A prop that replaces one, or goes on the stand, is in plain sight at once and must come with its textures.
        var hiddenAtFirst = _prop == null && !_standActive && !IsGhosting;

        _prop?.Dispose();
        _propLeans = !_standActive;
        _propPlacement = placement;
        _prop = kn5 == null ? null : new Kn5RenderableFile(kn5, placement * (_propLeans ? _bodyLean : Matrix.Identity), asyncTexturesLoading: hiddenAtFirst);
        _propNodes = PartNode.Of(_prop);
        _hiddenNodes = new();
        _hideFades.Clear();

        // An engine for the stand: how far it can turn over is worked out upright, then it is turned as far as the one
        // before it was, and goes on from there to where it can
        _turnLimit = _propLeans ? System.MathF.PI : TurnLimit();
        if (_prop != null && !_propLeans) _prop.LocalMatrix = placement * StandTurn;

        // A new prop is not the one a fade back to solid was going to drop
        _removePropWhenSolid = false;
        _motions.Clear();
        UpdateFading();
        _hovered = null;
        _selected = null;
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>Replaces the model of the places a loose part could go; pass null to remove it</summary>
    public void SetCandidates(IKn5? kn5)
    {
        _candidates?.Dispose();
        _candidatesLean = !_standActive;
        _candidates = kn5 == null ? null : new Kn5RenderableFile(kn5, _candidatesLean ? _bodyLean : StandTurn, asyncTexturesLoading: false);
        _candidateNodes = PartNode.Of(_candidates);
        if (_hovered?.Layer == PartLayer.Candidate) _hovered = null;
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>
    /// A prop that stays in the garage whatever the car and the stand do (the radio), put where
    /// <paramref name="placement"/> says; null takes it away. Must be called on the thread that draws.
    /// </summary>
    public void SetFixture(IKn5? kn5, Matrix placement)
    {
        _fixture?.Dispose();
        _fixture = kn5 == null ? null : new Kn5RenderableFile(kn5, placement, asyncTexturesLoading: false);
        _fixture?.UpdateBoundingBox();
        _fixtureMeshes = _fixture?.GetAllChildren().OfType<IKn5RenderableObject>().ToList() ?? new();
        _fixtureHovered = false;
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>
    /// The workbench (<see cref="Parts.WorkbenchLayout"/>), put where <paramref name="placement"/> says; null takes it
    /// away. Drawn and in the way of what is behind it, never picked nor lit up. Must be called on the thread that draws.
    /// </summary>
    public void SetBench(IKn5? kn5, Matrix placement)
    {
        _bench?.Dispose();
        _bench = kn5 == null ? null : new Kn5RenderableFile(kn5, placement, asyncTexturesLoading: false);
        _bench?.UpdateBoundingBox();
        _benchMeshes = _bench?.GetAllChildren().OfType<IKn5RenderableObject>().ToList() ?? new();
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>
    /// The part a job is being done on: <paramref name="clean"/> drawn where <paramref name="placement"/> puts it (a loose
    /// part on the workbench; null for a mounted part, which is drawn already), <paramref name="grime"/> over it at
    /// <see cref="WorkGrime"/>. With <paramref name="followsParts"/> both are models of the mounted parts' space and go
    /// where the mounted parts go (the body's lean, the stand's turn). Nulls take it away. Must be called on the thread
    /// that draws.
    /// </summary>
    public void SetWork(IKn5? clean, IKn5? grime, Matrix placement, bool followsParts)
    {
        _work?.Dispose();
        _workGrime?.Dispose();
        _workFollowsParts = followsParts;
        var where = followsParts && _prop != null ? _prop.LocalMatrix : placement;
        // The part's own textures come in off the thread that draws, while it fades in; the grime has one small texture
        _work = clean == null ? null : new Kn5RenderableFile(clean, where, asyncTexturesLoading: true);
        _workGrime = grime == null ? null : new Kn5RenderableFile(grime, where, asyncTexturesLoading: false);
        _work?.UpdateBoundingBox();
        _workGrime?.UpdateBoundingBox();
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>The loose part on the workbench put somewhere else (on its way down to the bench, turning on it)</summary>
    public void MoveWork(Matrix placement)
    {
        if (_workFollowsParts) return;

        foreach (var file in new[] { _work, _workGrime })
        {
            if (file == null) continue;
            file.LocalMatrix = placement;
            file.UpdateBoundingBox();
        }

        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>How far the part on the workbench is in sight, 0 to 1</summary>
    public float WorkOpacity
    {
        get => _workOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            if (clamped == _workOpacity) return;
            _workOpacity = clamped;
            SetShadowsDirty();
            IsDirty = true;
        }
    }

    /// <summary>How much of the part's grime is drawn over it, 0 clean to 1</summary>
    public float WorkGrime
    {
        get => _workGrimeAmount;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            if (clamped == _workGrimeAmount) return;
            _workGrimeAmount = clamped;
            IsDirty = true;
        }
    }

    /// <summary>The middle of the part a job is being done on, in the garage's space; null without one</summary>
    public Vector3? WorkCentre => (_work ?? _workGrime) is { } file ? (file.BoundingBox is { } box ? (box.Minimum + box.Maximum) / 2f : null) : null;

    /// <summary>The fixture as it is drawn, to move its nodes (the paint booth's painter swings his arm); null without one</summary>
    public Kn5RenderableFile? Fixture => _fixture;

    /// <summary>The fixture stood somewhere else (the paint booth's painter walks round the car). Must be called on the thread that draws.</summary>
    public void MoveFixture(Matrix placement)
    {
        if (_fixture == null) return;
        _fixture.LocalMatrix = placement;

        // Its box where it stands now: the shadows' cameras leave out what their box is not in
        _fixture.UpdateBoundingBox();
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>True when a point of the picture is on the fixture, and nothing solid of the car or the stand is in front of it</summary>
    public bool HitsFixture(float x, float y)
    {
        if (_fixture == null || Camera == null) return false;

        var ray = Camera.GetPickingRay(new Vector2(x * ActualWidth, y * ActualHeight), new Vector2(ActualWidth, ActualHeight));
        if (Nearest(ray, _fixtureMeshes) is not { } fixture) return false;
        if (Nearest(ray, _sceneryMeshes) is { } scenery && scenery < fixture) return false;
        if (Nearest(ray, _benchMeshes) is { } bench && bench < fixture) return false;

        return IsGhosting || CarNode == null || CarDistance(ray) is not { } car || car > fixture;
    }

    /// <summary>The pointer is on the fixture: it glows, a click on it is the viewport's to answer</summary>
    public bool FixtureHovered
    {
        get => _fixtureHovered;
        set
        {
            if (_fixtureHovered == value) return;
            _fixtureHovered = value;
            IsDirty = true;
        }
    }

    /// <summary>The pointer is on the scenery (the engine stand): it glows, a click on it goes to the stand</summary>
    public bool SceneryHovered
    {
        get => _sceneryHovered;
        set
        {
            if (_sceneryHovered == value) return;
            _sceneryHovered = value;
            IsDirty = true;
        }
    }

    /// <summary>The pointer is on the car while the stand is worked on: it glows, a click on it goes back to the car</summary>
    public bool CarHovered
    {
        get => _carHovered;
        set
        {
            if (_carHovered == value) return;
            _carHovered = value;
            IsDirty = true;
        }
    }

    /// <summary>
    /// True when a point of the picture is on the car, and neither the stand nor the engine on it (the prop, while the
    /// stand is worked on) is in front of it there
    /// </summary>
    public bool HitsCar(float x, float y)
    {
        if (CarNode == null || Camera == null) return false;

        var ray = Camera.GetPickingRay(new Vector2(x * ActualWidth, y * ActualHeight), new Vector2(ActualWidth, ActualHeight));
        if (CarDistance(ray) is not { } car) return false;

        var cover = new[] { Nearest(ray, _sceneryMeshes), Nearest(ray, _fixtureMeshes), Nearest(ray, _benchMeshes), _standActive ? Nearest(_propNodes, ray) : null }.Min();
        return cover == null || cover > car;
    }

    private static float? Nearest(List<PartNode> nodes, Ray ray) =>
        nodes.Select(n => n.Intersect(ray)).Where(d => d != null).Min();

    /// <summary>
    /// True when a point of the picture is on the scenery (the engine stand, or the engine on it), and neither the car
    /// nor the workbench is in front of it there. A ghost car is no cover.
    /// </summary>
    public bool HitsScenery(float x, float y)
    {
        if (_scenery == null || Camera == null) return false;

        var ray = Camera.GetPickingRay(new Vector2(x * ActualWidth, y * ActualHeight), new Vector2(ActualWidth, ActualHeight));
        if (Nearest(ray, _sceneryMeshes) is not { } scenery) return false;
        if (Nearest(ray, _benchMeshes) is { } bench && bench < scenery) return false;
        if (IsGhosting || CarNode == null) return true;

        return CarDistance(ray) is not { } car || car > scenery;
    }

    /// <summary>
    /// Distance along the ray to the car's bounding box; null when it misses. Near enough to aim at the car, or to
    /// stand in the way of what is behind it, and cheap enough for every move of the pointer: the car's own
    /// triangles run to the hundred thousand.
    /// </summary>
    private float? CarDistance(Ray ray) =>
        CarNode != null && MainSlot.GetCarBoundingBox() is { } box && Ray.Intersects(ray, box, out float distance) ? distance : null;

    /// <summary>Distance along the ray to the nearest of the meshes; null when it misses them all</summary>
    private static float? Nearest(Ray ray, IEnumerable<IKn5RenderableObject> meshes)
    {
        float? nearest = null;
        foreach (var mesh in meshes)
        {
            // Triangle tests run per vertex on the CPU: only for meshes whose box the ray goes through
            if (mesh.BoundingBox is { } box && !Ray.Intersects(ray, box, out float _)) continue;

            if (mesh.CheckIntersection(ray) is { } hit && (nearest == null || hit < nearest)) nearest = hit;
        }

        return nearest;
    }

    /// <summary>The part the pointer is on, glowing so it can be told from its neighbours</summary>
    public PartHit? HoveredPart
    {
        get => _hovered;
        set
        {
            if (_hovered == value) return;
            _hovered = value;
            IsDirty = true;
        }
    }

    /// <summary>Node of the mounted part that is selected</summary>
    public int? SelectedPart
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            IsDirty = true;
        }
    }

    /// <summary>
    /// The part under a point of the picture: the nearest place shown for a loose part, else the nearest
    /// mounted part. The places glow through whatever is in front of them, and some sit inside another part
    /// (a fuel rail in its manifold), so they are reached through the mounted parts.
    /// The car itself is never hit: in the parts view it is a ghost one reaches through. On the stand it is solid, and
    /// hides what is behind it.
    /// </summary>
    /// <param name="x">0 at the left edge, 1 at the right</param>
    /// <param name="y">0 at the top, 1 at the bottom</param>
    public PartHit? Pick(float x, float y)
    {
        if (!IsWorking || Camera == null) return null;

        var ray = Camera.GetPickingRay(new Vector2(x * ActualWidth, y * ActualHeight), new Vector2(ActualWidth, ActualHeight));
        if ((Nearest(PartLayer.Candidate, _candidateNodes) ?? Nearest(PartLayer.Mounted, _propNodes)) is not { } found) return null;

        var carInTheWay = _standActive && !IsGhosting && CarDistance(ray) is { } car && car < found.Distance;
        return carInTheWay ? null : found.Hit;

        (PartHit Hit, float Distance)? Nearest(PartLayer layer, List<PartNode> nodes)
        {
            (PartHit, float)? nearest = null;
            var distance = float.MaxValue;
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Intersect(ray) is { } hit && hit < distance)
                {
                    distance = hit;
                    nearest = (new PartHit(layer, i), hit);
                }
            }

            return nearest;
        }
    }

    /// <summary>The meshes of one part in a part model</summary>
    private sealed class PartNode
    {
        private readonly List<IKn5RenderableObject> _meshes;

        private PartNode(Kn5RenderableFile file, RenderableList list)
        {
            File = file;
            List = list;
            _meshes = list.GetAllChildren().OfType<IKn5RenderableObject>().ToList();
        }

        public Kn5RenderableFile File { get; }

        public RenderableList List { get; }

        /// <summary>The part nodes of the model, this one included</summary>
        public HashSet<RenderableList> Siblings { get; private set; } = new();

        public static List<PartNode> Of(Kn5RenderableFile? file)
        {
            var nodes = new List<PartNode>();
            if (file == null) return nodes;

            // Part nodes are numbered without gaps
            while (file.GetDummyByName(PartAssembler.NodeName(nodes.Count)) is { } list) nodes.Add(new PartNode(file, list));

            var lists = nodes.Select(n => n.List).ToHashSet();
            foreach (var node in nodes) node.Siblings = lists;
            return nodes;
        }

        /// <summary>Put out of sight by the player (its kind's show button): not drawn, not picked</summary>
        public bool IsHidden { get; set; }

        /// <summary>Distance along the ray to the part's surface; null when the ray misses it</summary>
        public float? Intersect(Ray ray) => List.IsEnabled && !IsHidden ? Nearest(ray, _meshes) : null;
    }

    protected override void DrawCars(DeviceContextHolder holder, ICamera camera, SpecialRenderMode mode)
    {
        _scenery?.Draw(holder, camera, mode);
        _fixture?.Draw(holder, camera, mode);
        _bench?.Draw(holder, camera, mode);
        FollowParts();

        // The part on the workbench, once it is all there; on its way in or out it is drawn see-through after the rest
        if (_work != null && _workOpacity >= 1f) _work.Draw(holder, camera, mode);

        // Parts on their way are left out of every pass, shadows included, and drawn over the scene see-through
        if (!IsGhosting)
        {
            base.DrawCars(holder, camera, mode);
            _prop?.Draw(holder, camera, mode, _settled);
            if (_standActive) _candidates?.Draw(holder, camera, mode);
        }
        else
        {
            // Shadows, g-buffer (AO, reflections) and the rest see the parts only
            _prop?.Draw(holder, camera, mode, _settled);
            _candidates?.Draw(holder, camera, mode);
        }

        // Once every surface is in the scene, glass included: the depth the parts on their way leave behind hides
        // nothing drawn after them then. They go before the shell, so they are inside it as the rest are.
        if (mode != SpecialRenderMode.SimpleTransparent) return;

        DrawFading(holder, camera);
        DrawWork(holder, camera);
        if (IsGhosting) DrawGhost(holder, camera);
        holder.DeviceContext.OutputMerger.DepthStencilState = holder.States.ReadOnlyDepthState;
    }

    /// <summary>
    /// Parts on their way, at how far gone they are. As with the ghost car, materials force their own blend state:
    /// the parts go over a copy of the scene, blended back at their opacity. Parts that move together fade together,
    /// so there is a pass for each step of the fade there is: one mostly, two or three while an engine is stripped for
    /// the stand, one part starting before the one ahead of it is gone.
    /// </summary>
    private void DrawFading(DeviceContextHolder holder, ICamera camera)
    {
        if (_prop == null || _fading == null) return;

        var prop = _prop;
        var siblings = _propNodes.Count > 0 ? _propNodes[0].Siblings : new HashSet<RenderableList>();
        foreach (var group in _fading.GroupBy(f => f.Value))
        {
            var opacity = group.Key / FadeSteps;
            if (opacity <= 0f) continue;

            var lists = group.Select(f => f.Key).ToHashSet();
            bool Only(IRenderableObject o) => o is not RenderableList list || !siblings.Contains(list) || lists.Contains(list);

            DrawSeeThrough(holder, opacity, mode => prop.Draw(holder, camera, mode, Only));
        }
    }

    /// <summary>The grime over a mounted part goes where the mounted parts go: the car's body leans, the stand turns</summary>
    private void FollowParts()
    {
        if (!_workFollowsParts || _prop == null) return;

        foreach (var file in new[] { _work, _workGrime })
        {
            if (file == null || file.LocalMatrix == _prop.LocalMatrix) continue;
            file.LocalMatrix = _prop.LocalMatrix;
            file.UpdateBoundingBox();
        }
    }

    /// <summary>The part on the workbench on its way in or out, and the grime over the part a job is being done on</summary>
    private void DrawWork(DeviceContextHolder holder, ICamera camera)
    {
        if (_work != null && _workOpacity is > 0f and < 1f) DrawOver(holder, camera, _work, _workOpacity);

        var grime = _workGrimeAmount * (_work != null ? _workOpacity : 1f);
        if (_workGrime != null && grime > 0.004f) DrawOver(holder, camera, _workGrime, grime);
    }

    /// <summary>
    /// A model drawn over the scene see-through, as the parts on their way are (<see cref="DrawFading"/>): over a copy of
    /// the scene in its depth, blended back at <paramref name="opacity"/>. A surface the scene has at the same depth (the
    /// mounted part under its grime) is drawn over, not hidden.
    /// </summary>
    private void DrawOver(DeviceContextHolder holder, ICamera camera, Kn5RenderableFile file, float opacity) =>
        DrawSeeThrough(holder, opacity, mode => file.Draw(holder, camera, mode));

    /// <summary>
    /// One see-through pass: <paramref name="draw"/>'s solid then transparent surfaces over a copy of the scene, in the
    /// scene's depth so what is in front of them still is, the copy blended back at <paramref name="opacity"/>; the
    /// output left as the scene pass expects it
    /// </summary>
    private void DrawSeeThrough(DeviceContextHolder holder, float opacity, Action<SpecialRenderMode> draw)
    {
        var context = holder.DeviceContext;
        var scene = InnerBuffer;
        var copy = holder.GetHelper<CopyHelper>();
        _fadeBuffer ??= TargetResourceTexture.Create(scene.Format);
        _fadeBuffer.Resize(holder, Width, Height, null);
        _ghostBlend ??= CreateGhostBlend(holder.Device);

        copy.Draw(holder, scene.View, _fadeBuffer.TargetView);
        context.OutputMerger.SetTargets(DepthStencilView, _fadeBuffer.TargetView);
        context.OutputMerger.BlendState = null;
        context.OutputMerger.DepthStencilState = holder.States.LessEqualDepthState;
        draw(SpecialRenderMode.Simple);
        context.OutputMerger.DepthStencilState = holder.States.ReadOnlyDepthState;
        draw(SpecialRenderMode.SimpleTransparent);

        context.OutputMerger.BlendState = _ghostBlend;
        context.OutputMerger.BlendFactor = new Color4(opacity, opacity, opacity, opacity);
        context.OutputMerger.DepthStencilState = holder.States.DisabledDepthState;
        context.Rasterizer.State = null;
        copy.Draw(holder, _fadeBuffer.View, scene.TargetView);

        context.OutputMerger.BlendState = null;
        context.OutputMerger.SetTargets(DepthStencilView, scene.TargetView);
        context.OutputMerger.DepthStencilState = holder.States.LessEqualDepthState;
    }

    protected override void DrawPrepare()
    {
        base.DrawPrepare();
        Array.Clear(_glowDrawn);
        var sceneryGlow = _sceneryHovered && _scenery != null;
        var fixtureGlow = _fixtureHovered && _fixture != null;
        if (!IsWorking && !sceneryGlow && !fixtureGlow) return;

        var holder = DeviceContextHolder;
        _outlineDepth ??= TargetResourceDepthTexture.Create();
        _outlineDepth.Resize(holder, Width, Height, null);

        if (sceneryGlow) DrawGlow(3, HoverGlow, 1f, () => _scenery!.Draw(holder, ActualCamera, SpecialRenderMode.Outline));

        // The fixture shares the stand's glow: the pointer is only ever on one of them
        else if (fixtureGlow) DrawGlow(3, HoverGlow, 1f, () => _fixture!.Draw(holder, ActualCamera, SpecialRenderMode.Outline));
        if (!IsWorking) return;

        // The solid car beside the stand, pointed at: the way back to it
        if (_standActive && _carHovered && !IsGhosting && CarNode != null)
            DrawGlow(0, CarGlow, 1f, () => base.DrawCars(holder, ActualCamera, SpecialRenderMode.Outline));

        // Everything comes in with the transition; the stand's parts have none, but the car's outline fades with the
        // car even then, on its way back to solid
        var fade = _standActive ? 1f : Ease(_ghostProgress);
        if (IsGhosting && CarNode != null) DrawGlow(0, CarGlow, Ease(_ghostProgress), () => base.DrawCars(holder, ActualCamera, SpecialRenderMode.Outline));

        if (_candidates != null)
            DrawGlow(1, CandidateGlow, fade, () => _candidates.Draw(holder, ActualCamera, SpecialRenderMode.Outline));

        if (_selected is { } selected && selected < _propNodes.Count)
            DrawGlow(2, SelectedGlow, fade, () => DrawNode(_propNodes[selected]));

        var hoverIsSelected = _hovered is { Layer: PartLayer.Mounted } && _hovered.Value.Node == _selected;
        if (_hovered is { } hovered && !hoverIsSelected && NodeOf(hovered) is { } hoveredNode)
            DrawGlow(3, HoverGlow, fade, () => DrawNode(hoveredNode));

        // The model hands its materials down as it draws, so a single part is drawn by keeping the others out
        void DrawNode(PartNode node) => node.File.Draw(holder, ActualCamera, SpecialRenderMode.Outline,
            o => o is not RenderableList list || ReferenceEquals(list, node.List) || !node.Siblings.Contains(list));
    }

    private PartNode? NodeOf(PartHit hit)
    {
        var nodes = hit.Layer == PartLayer.Mounted ? _propNodes : _candidateNodes;
        return hit.Node >= 0 && hit.Node < nodes.Count ? nodes[hit.Node] : null;
    }

    /// <summary>The edge of whatever <paramref name="drawDepth"/> draws, tinted, into a glow buffer of its own</summary>
    private void DrawGlow(int index, Glow tint, float strength, Action drawDepth)
    {
        var holder = DeviceContextHolder;
        var context = DeviceContext;

        var buffer = _glowBuffers[index] ??= TargetResourceTexture.Create(Format.R8G8B8A8_UNorm);
        buffer.Resize(holder, Width, Height, null);

        // Depth of the thing alone, then the edge of what it covers
        context.ClearDepthStencilView(_outlineDepth!.DepthView, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1f, 0);
        context.OutputMerger.SetTargets(_outlineDepth.DepthView);

        // The previous frame ends on full-screen passes: depth off, some other viewport
        context.Rasterizer.SetViewports(Viewport);
        context.OutputMerger.DepthStencilState = null;
        context.OutputMerger.BlendState = null;
        context.Rasterizer.State = holder.States.DoubleSidedState;
        drawDepth();

        context.ClearRenderTargetView(buffer.TargetView, new Color4(0f, 0f, 0f, 0f));
        context.OutputMerger.SetTargets(buffer.TargetView);

        var effect = holder.GetEffect<EffectPpOutline>();
        effect.FxDepthMap.SetResource(_outlineDepth.View);
        effect.FxScreenSize.Set(new Vector4(ActualWidth, ActualHeight, 1f / ActualWidth, 1f / ActualHeight));
        holder.PrepareQuad(effect.LayoutPT);
        effect.TechOutline.DrawAllPasses(context, 6);
        context.Rasterizer.State = null;

        // The edge comes out white: scale it to the tint, and its alpha to how far the transition is.
        // This pass writes nothing of its own, so what it samples does not matter.
        _scaleBlend ??= CreateScaleBlend(holder.Device);
        context.OutputMerger.BlendState = _scaleBlend;
        // SlimDX puts alpha first
        context.OutputMerger.BlendFactor = new Color4(tint.Strength * strength, tint.Red, tint.Green, tint.Blue);
        holder.GetHelper<CopyHelper>().Draw(holder, _outlineDepth.View, buffer.TargetView);
        context.OutputMerger.BlendState = null;

        _glowDrawn[index] = true;
    }

    protected override void DrawAfter()
    {
        base.DrawAfter();
        if (!_glowDrawn.Any(d => d)) return;

        var holder = DeviceContextHolder;
        var context = DeviceContext;

        context.OutputMerger.BlendState = holder.States.TransparentBlendState;
        context.OutputMerger.DepthStencilState = holder.States.DisabledDepthState;
        context.Rasterizer.State = null;
        for (var i = 0; i < _glowBuffers.Length; i++)
        {
            if (_glowDrawn[i]) holder.GetHelper<CopyHelper>().Draw(holder, _glowBuffers[i]!.View, InnerBuffer.TargetView);
        }

        context.OutputMerger.BlendState = null;
        context.OutputMerger.SetTargets(DepthStencilView, InnerBuffer.TargetView);
    }

    private void DrawGhost(DeviceContextHolder holder, ICamera camera)
    {
        var context = holder.DeviceContext;
        var scene = InnerBuffer;

        _ghostBuffer ??= TargetResourceTexture.Create(scene.Format);
        _ghostDepth ??= TargetResourceDepthTexture.Create();
        _ghostBuffer.Resize(holder, Width, Height, null);
        _ghostDepth.Resize(holder, Width, Height, null);

        // Scene so far, then the car on top with a depth buffer of its own
        var copy = holder.GetHelper<CopyHelper>();
        copy.Draw(holder, scene.View, _ghostBuffer.TargetView);

        context.ClearDepthStencilView(_ghostDepth.DepthView, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1f, 0);
        context.OutputMerger.SetTargets(_ghostDepth.DepthView, _ghostBuffer.TargetView);

        // The scenery first, for its depth: where the stand is in front of the car, the ghost buffer holds the stand
        // as the scene does, and blending changes nothing there
        context.OutputMerger.DepthStencilState = holder.States.LessEqualDepthState;
        _scenery?.Draw(holder, camera, SpecialRenderMode.Simple);
        _fixture?.Draw(holder, camera, SpecialRenderMode.Simple);
        _bench?.Draw(holder, camera, SpecialRenderMode.Simple);
        base.DrawCars(holder, camera, SpecialRenderMode.Simple);
        context.OutputMerger.DepthStencilState = holder.States.ReadOnlyDepthState;
        base.DrawCars(holder, camera, SpecialRenderMode.SimpleTransparent);

        // Constant-factor blend, so the result does not depend on what the materials wrote to alpha
        _ghostBlend ??= CreateGhostBlend(holder.Device);
        context.OutputMerger.BlendState = _ghostBlend;
        var opacity = CurrentGhostOpacity;
        context.OutputMerger.BlendFactor = new Color4(opacity, opacity, opacity, opacity);
        context.OutputMerger.DepthStencilState = holder.States.DisabledDepthState;
        context.Rasterizer.State = null;
        copy.Draw(holder, _ghostBuffer.View, scene.TargetView);

        // Back to what the scene pass expects
        context.OutputMerger.BlendState = null;
        context.OutputMerger.SetTargets(DepthStencilView, scene.TargetView);
        context.OutputMerger.DepthStencilState = holder.States.LessEqualDepthState;
    }

    private static BlendState CreateGhostBlend(SlimDX.Direct3D11.Device device)
    {
        var description = new BlendStateDescription { AlphaToCoverageEnable = false, IndependentBlendEnable = false };
        description.RenderTargets[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = BlendOption.BlendFactor,
            DestinationBlend = BlendOption.InverseBlendFactor,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = BlendOption.Zero,
            DestinationBlendAlpha = BlendOption.One,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteMaskFlags.All
        };
        return BlendState.FromDescription(device, description);
    }

    /// <summary>Multiplies what is in the target by the blend factor, colour and alpha alike</summary>
    private static BlendState CreateScaleBlend(SlimDX.Direct3D11.Device device)
    {
        var description = new BlendStateDescription { AlphaToCoverageEnable = false, IndependentBlendEnable = false };
        description.RenderTargets[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = BlendOption.Zero,
            DestinationBlend = BlendOption.BlendFactor,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = BlendOption.Zero,
            DestinationBlendAlpha = BlendOption.BlendFactor,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteMaskFlags.All
        };
        return BlendState.FromDescription(device, description);
    }

    protected override void DisposeOverride()
    {
        _prop?.Dispose();
        _prop = null;
        _scenery?.Dispose();
        _scenery = null;
        _fixture?.Dispose();
        _fixture = null;
        _bench?.Dispose();
        _bench = null;
        _work?.Dispose();
        _work = null;
        _workGrime?.Dispose();
        _workGrime = null;
        _candidates?.Dispose();
        _candidates = null;
        _ghostBuffer?.Dispose();
        _fadeBuffer?.Dispose();
        _ghostDepth?.Dispose();
        _ghostBlend?.Dispose();
        _scaleBlend?.Dispose();
        foreach (var buffer in _glowBuffers) buffer?.Dispose();
        _outlineDepth?.Dispose();
        base.DisposeOverride();
    }
}
