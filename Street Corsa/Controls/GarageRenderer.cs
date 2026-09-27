using AcTools.Kn5File;
using AcTools.Render.Base;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Objects;
using AcTools.Render.Base.PostEffects;
using AcTools.Render.Base.TargetTextures;
using AcTools.Render.Base.Utils;
using AcTools.Render.Kn5Specific.Objects;
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
/// </summary>
public sealed record PartMove(IReadOnlyList<int> Nodes, IReadOnlyList<PartWay> Way, float Delay = 0f);

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

    private readonly record struct Glow(float Red, float Green, float Blue, float Strength = 1f);

    private static readonly Glow CarGlow = new(1f, 1f, 1f);
    private static readonly Glow CandidateGlow = new(0.35f, 1f, 0.45f);
    private static readonly Glow SelectedGlow = new(1f, 0.62f, 0.15f);
    private static readonly Glow HoverGlow = new(1f, 0.9f, 0.55f);

    private Kn5RenderableFile? _prop;
    private Kn5RenderableFile? _scenery;
    private List<IKn5RenderableObject> _sceneryMeshes = new();
    private bool _sceneryHovered;
    private bool _carHovered;
    private bool _standActive;
    private Kn5RenderableFile? _candidates;
    private List<PartNode> _propNodes = new();
    private List<PartNode> _candidateNodes = new();
    private PartHit? _hovered;
    private int? _selected;
    private readonly List<Motion> _motions = new();

    // The part nodes drawn see-through over the scene right now, and the filter that leaves them out of every other pass
    private HashSet<RenderableList>? _fading;
    private Func<IRenderableObject, bool>? _settled;

    // Whether the prop and the candidates lean with the car's body: not the ones made for the stand
    private bool _propLeans;
    private bool _candidatesLean;
    private bool _ghostCar;
    private bool _removePropWhenSolid;
    private float _ghostProgress;
    private float _ghostOpacity = 0.3f;

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
                _prop.LocalMatrix = value;
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
            _standActive = value;
            _hovered = null;
            IsDirty = true;
        }
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

        var moving = groups.SelectMany(g => g.Nodes).ToHashSet();
        var staying = CentreOf(_propNodes.Where(n => !moving.Contains(n)));
        var frame = frameNode is { } f && f >= 0 && f < _propNodes.Count ? _propNodes[f] : null;

        // Part models face -Z; with no block to go by, one set in the car the way the car faces (+Z)
        var axes = frame?.List.LocalMatrix ?? Matrix.RotationY((float)Math.PI);
        var right = Direction(Vector3.TransformNormal(Vector3.UnitX, axes), Vector3.UnitX);
        var up = Direction(Vector3.TransformNormal(Vector3.UnitY, axes), Vector3.UnitY);
        var forward = Direction(Vector3.TransformNormal(-Vector3.UnitZ, axes), Vector3.UnitZ);

        foreach (var (move, nodes) in groups)
        {
            if (CentreOf(nodes) is not { } centre) continue;

            // Out is to the side of the engine the part is on; the block itself has none, so it goes by what stays
            var middle = frame == null || nodes.Contains(frame) ? staying : CentreOf(new[] { frame });
            var outward = middle is { } m && Vector3.Dot(centre - m, right) < 0f ? -right : right;

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

            foreach (var node in nodes)
            {
                _motions.RemoveAll(motion => ReferenceEquals(motion.Node, node));
                var motion = new PartMotion(node, node.List.LocalMatrix, legs, arriving, move.Delay);
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
        // floor is right there
        Vector3 AwayFrom(Vector3 centre)
        {
            if (staying is not { } middle) return Vector3.UnitY;

            var away = centre - middle;
            away.Y = Math.Max(away.Y, 0f) + 0.15f;
            return away.LengthSquared() > 1e-6f ? Vector3.Normalize(away) : Vector3.UnitY;
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

        foreach (var node in carried)
        {
            _motions.RemoveAll(m => ReferenceEquals(m.Node, node));
            var motion = new CarryMotion(this, node, node.List.LocalMatrix, hoist, delay);
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
        if (_motions.Count == 0) return;

        var step = Math.Min(dt, MaxTickSeconds);
        foreach (var motion in _motions) motion.Advance(step);

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
        var fading = _motions.Count == 0 ? null : _motions.Where(IsSeeThrough).Select(m => m.Node.List).ToHashSet();
        if (fading is { Count: 0 }) fading = null;
        if (fading == null ? _fading == null : _fading != null && _fading.SetEquals(fading)) return;

        _fading = fading;
        _settled = fading == null ? null : o => o is not RenderableList list || !fading.Contains(list);
        SetShadowsDirty();
    }

    /// <summary>Whether a motion is drawn see-through over the scene, not with the rest (<see cref="DrawFading"/>)</summary>
    private static bool IsSeeThrough(Motion motion) =>
        motion is PartMotion part && part.Node.List.IsEnabled && Math.Round(part.Opacity * FadeSteps) < FadeSteps;

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

    /// <summary>Replaces the prop; pass null to remove it. Must be called on the thread that draws.</summary>
    public void SetProp(IKn5? kn5, Matrix placement)
    {
        _prop?.Dispose();
        _propLeans = !_standActive;
        _prop = kn5 == null ? null : new Kn5RenderableFile(kn5, _propLeans ? placement * _bodyLean : placement, asyncTexturesLoading: false);
        _propNodes = PartNode.Of(_prop);

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
        _candidates = kn5 == null ? null : new Kn5RenderableFile(kn5, _candidatesLean ? _bodyLean : Matrix.Identity, asyncTexturesLoading: false);
        _candidateNodes = PartNode.Of(_candidates);
        if (_hovered?.Layer == PartLayer.Candidate) _hovered = null;
        SetShadowsDirty();
        IsDirty = true;
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

        var cover = new[] { Nearest(ray, _sceneryMeshes), _standActive ? Nearest(_propNodes, ray) : null }.Min();
        return cover == null || cover > car;
    }

    private static float? Nearest(List<PartNode> nodes, Ray ray) =>
        nodes.Select(n => n.Intersect(ray)).Where(d => d != null).Min();

    /// <summary>
    /// True when a point of the picture is on the scenery (the engine stand, or the engine on it), and the car is not
    /// in front of it there. A ghost car is no cover.
    /// </summary>
    public bool HitsScenery(float x, float y)
    {
        if (_scenery == null || Camera == null) return false;

        var ray = Camera.GetPickingRay(new Vector2(x * ActualWidth, y * ActualHeight), new Vector2(ActualWidth, ActualHeight));
        if (Nearest(ray, _sceneryMeshes) is not { } scenery) return false;
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

        /// <summary>Distance along the ray to the part's surface; null when the ray misses it</summary>
        public float? Intersect(Ray ray) => Nearest(ray, _meshes);
    }

    protected override void DrawCars(DeviceContextHolder holder, ICamera camera, SpecialRenderMode mode)
    {
        _scenery?.Draw(holder, camera, mode);

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

        var context = holder.DeviceContext;
        var scene = InnerBuffer;
        var copy = holder.GetHelper<CopyHelper>();
        _fadeBuffer ??= TargetResourceTexture.Create(scene.Format);
        _fadeBuffer.Resize(holder, Width, Height, null);
        _ghostBlend ??= CreateGhostBlend(holder.Device);

        foreach (var group in _motions.Where(IsSeeThrough).Cast<PartMotion>().GroupBy(m => (float)Math.Round(m.Opacity * FadeSteps)))
        {
            var opacity = group.Key / FadeSteps;
            if (opacity <= 0f) continue;

            var lists = group.Select(m => m.Node.List).ToHashSet();
            var siblings = group.First().Node.Siblings;
            bool Only(IRenderableObject o) => o is not RenderableList list || !siblings.Contains(list) || lists.Contains(list);

            // In the scene's depth, so what is in front of them still is
            copy.Draw(holder, scene.View, _fadeBuffer.TargetView);
            context.OutputMerger.SetTargets(DepthStencilView, _fadeBuffer.TargetView);
            context.OutputMerger.BlendState = null;
            context.OutputMerger.DepthStencilState = holder.States.LessEqualDepthState;
            _prop.Draw(holder, camera, SpecialRenderMode.Simple, Only);
            context.OutputMerger.DepthStencilState = holder.States.ReadOnlyDepthState;
            _prop.Draw(holder, camera, SpecialRenderMode.SimpleTransparent, Only);

            context.OutputMerger.BlendState = _ghostBlend;
            context.OutputMerger.BlendFactor = new Color4(opacity, opacity, opacity, opacity);
            context.OutputMerger.DepthStencilState = holder.States.DisabledDepthState;
            context.Rasterizer.State = null;
            copy.Draw(holder, _fadeBuffer.View, scene.TargetView);
        }

        // Back to what the scene pass expects
        context.OutputMerger.BlendState = null;
        context.OutputMerger.SetTargets(DepthStencilView, scene.TargetView);
        context.OutputMerger.DepthStencilState = holder.States.LessEqualDepthState;
    }

    protected override void DrawPrepare()
    {
        base.DrawPrepare();
        Array.Clear(_glowDrawn);
        var sceneryGlow = _sceneryHovered && _scenery != null;
        if (!IsWorking && !sceneryGlow) return;

        var holder = DeviceContextHolder;
        _outlineDepth ??= TargetResourceDepthTexture.Create();
        _outlineDepth.Resize(holder, Width, Height, null);

        if (sceneryGlow) DrawGlow(3, HoverGlow, 1f, () => _scenery!.Draw(holder, ActualCamera, SpecialRenderMode.Outline));
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
