using System.Text.RegularExpressions;
using AcTools.Render.Base.Objects;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Audio;
using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// Rocks a car's body on its wheels: the body leans with the engine's torque while the wheels, hubs and suspension
/// stay planted. The whole car is turned, and each wheel group turned back by the same amount, so the model needs no
/// knowledge of which mesh is the body.
///
/// Lean is about the car's long axis at roughly the height of the roll centre, between the wheels; the driver's side
/// lifts on a blip, as it does with an engine that turns clockwise seen from the front.
/// </summary>
internal sealed class CarBodyRock
{
    private const float RollCentreHeight = 0.45f;

    private static readonly Regex WheelGroup = new("^(WHEEL|SUSP|HUB|DISC)_(LF|RF|LR|RR)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Kn5RenderableCar _car;
    private Sx.Matrix _base;
    private readonly List<Held> _held = new();
    private readonly Sx.Vector3 _pivot;
    private readonly float _leftSign;
    private readonly float _frontSign;
    private BodyPose _pose = BodyPose.Rest;
    private float _wheelSpin;
    private float _steer;

    // Where each front wheel's middle is in the car's own space, by corner ("LF"): its disc and hub steer about it too
    private readonly Dictionary<string, Sx.Vector3> _steerAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The lean in world space, for whatever sits in the car without being part of it (the parts view)</summary>
    public Sx.Matrix WorldLean { get; private set; } = Sx.Matrix.Identity;

    /// <summary>
    /// A wheel group as it was, and the way from its parent to the car's own space; its corner ("LF"), and whether it
    /// is a wheel and whether it turns as the front wheels steer (a front wheel, its hub and its disc; not the
    /// suspension's arms, unless they sit in a hub that steers). <c>Parent</c> is the place in <see cref="_held"/> of the
    /// group it sits in, when a model nests them (a wheel in its hub or its suspension), -1 when none: it is then set
    /// against where that group is put, not against the car, and <c>FromParent</c> is the way from that group to the
    /// node it hangs from (the nodes between them as they stand).
    /// </summary>
    private sealed record Held(RenderableList Node, Sx.Matrix Local, Sx.Matrix ToCar, Sx.Matrix FromCar, string Corner, bool Wheel, bool Steers,
        int Parent, Sx.Matrix FromParent);

    // Where each group is put this frame, in the car's own space, by its place in _held: a nested group is set against
    // its parent's, turned back once a frame for the groups in it
    private Sx.Matrix[] _placed = [];
    private Sx.Matrix[] _unplaced = [];
    private bool[] _isParent = [];

    public CarBodyRock(Kn5RenderableCar car)
    {
        _car = car;
        _base = car.LocalMatrix;

        var toWorld = car.Matrix;
        var fromWorld = Sx.Matrix.Invert(toWorld);
        Collect(car, -1, (n, group, parent) =>
        {
            var toCar = n.ParentMatrix * fromWorld;
            var fromCar = Sx.Matrix.Invert(toCar);
            var kind = group.Groups[1].Value.ToUpperInvariant();
            var corner = group.Groups[2].Value.ToUpperInvariant();
            var outer = parent >= 0 ? _held[parent] : null;

            // The wheel itself turns as the car rolls; its hub, disc and suspension do not. What sits in a hub that
            // steers turns with it, its suspension too.
            var wheel = kind == "WHEEL";
            var steers = corner.EndsWith('F') && (kind is ("WHEEL" or "HUB" or "DISC") || outer?.Steers == true);
            var fromParent = outer == null ? Sx.Matrix.Identity : outer.Local * outer.ToCar * fromCar;
            _held.Add(new Held(n, n.LocalMatrix, toCar, fromCar, corner, wheel, steers, parent, fromParent));
            return _held.Count - 1;
        });
        _placed = new Sx.Matrix[_held.Count];
        _unplaced = new Sx.Matrix[_held.Count];
        _isParent = new bool[_held.Count];
        foreach (var held in _held)
            if (held.Parent >= 0) _isParent[held.Parent] = true;

        // Where the wheels are says which way is left and which way is the front
        var hubs = _held.Where(h => h.Wheel)
            .GroupBy(h => h.Corner)
            .ToDictionary(g => g.Key, g => Position(g.First().Local * g.First().ToCar));
        foreach (var (corner, at) in hubs)
            if (corner.EndsWith('F')) _steerAt[corner] = at;

        if (hubs.TryGetValue("LF", out var lf) && hubs.TryGetValue("RR", out var rr))
        {
            _leftSign = lf.X >= rr.X ? 1f : -1f;
            _frontSign = lf.Z >= rr.Z ? 1f : -1f;
            _pivot = new Sx.Vector3((lf.X + rr.X) / 2f, RollCentreHeight, (lf.Z + rr.Z) / 2f);
        }
        else
        {
            _leftSign = 1f;
            _frontSign = 1f;
            _pivot = new Sx.Vector3(0f, RollCentreHeight, 0f);
        }
    }

    public Kn5RenderableCar Car => _car;

    /// <summary>Where the car stands, the lean left out</summary>
    public Sx.Matrix Base => _base;

    /// <summary>+1 when the model's nose is at +z, -1 when it is at -z</summary>
    public float FrontSign => _frontSign;

    private static Sx.Vector3 Position(Sx.Matrix m) => new(m.M41, m.M42, m.M43);

    /// <summary>
    /// The wheel groups under <paramref name="list"/>, parents before the groups nested in them. Some models keep the wheel
    /// inside its hub or its suspension (HUB_LF/WHEEL_LF, SUSP_LF/WHEEL_LF, HUB_LF/SUSP_LF/WHEEL_LF): those are looked
    /// into, or the wheel would never turn. What is inside a wheel turns with it, and is left to.
    /// </summary>
    private void Collect(RenderableList list, int parent, Func<RenderableList, Match, int, int> found)
    {
        foreach (var child in list)
        {
            if (child is not RenderableList node) continue;
            if (node is Kn5RenderableList { OriginalNode.Name: var name } && WheelGroup.Match(name) is { Success: true } group)
            {
                var held = found(node, group, parent);
                if (!_held[held].Wheel) Collect(node, held, found);
                continue;
            }

            Collect(node, parent, found);
        }
    }

    /// <summary>
    /// Puts the body where the pose says, with the wheels turned <paramref name="wheelSpin"/> radians about their
    /// axles (a car rolling along) and the front ones steered <paramref name="steer"/> radians (positive the way the car
    /// turns about +y); false when nothing changed. A car on the move gives where it now stands as
    /// <paramref name="placement"/>: the car's own matrix is where the lean goes, so it cannot be set apart from it.
    /// </summary>
    public bool Apply(BodyPose pose, float wheelSpin = 0f, Sx.Matrix? placement = null, float steer = 0f)
    {
        var moved = placement is { } p && p != _base;
        if (pose == _pose && wheelSpin == _wheelSpin && steer == _steer && !moved) return false;
        if (placement is { } place) _base = place;
        _pose = pose;
        _wheelSpin = wheelSpin;
        _steer = steer;
        var spin = Sx.Matrix.RotationX(wheelSpin * _frontSign);

        var lean = Sx.Matrix.Translation(-_pivot)
                   * Sx.Matrix.RotationZ(pose.Roll * _leftSign)
                   * Sx.Matrix.RotationX(-pose.Pitch * _frontSign)
                   * Sx.Matrix.Translation(_pivot)
                   * Sx.Matrix.Translation(0f, pose.Heave, 0f);
        var back = Sx.Matrix.Invert(lean);

        _car.LocalMatrix = lean * _base;
        WorldLean = Sx.Matrix.Invert(_base) * lean * _base;
        for (var i = 0; i < _held.Count; i++)
        {
            var held = _held[i];
            var local = held.Wheel && wheelSpin != 0f ? spin * held.Local : held.Local;
            var inCar = local * held.ToCar;

            // A front wheel steers about the car's vertical through its own middle, and its hub and disc with it: a
            // wheel model's own axes are not always the car's (its up may lie along the car's length), so they cannot
            // be steered about
            if (held.Steers && steer != 0f)
            {
                var at = _steerAt.TryGetValue(held.Corner, out var middle) ? middle : Position(held.Local * held.ToCar);
                inCar *= Sx.Matrix.Translation(-at) * Sx.Matrix.RotationY(steer) * Sx.Matrix.Translation(at);
            }

            // Where it goes in the car's space, the body's lean undone; set against its parent group's place when it is
            // nested in one (put there already: parents come first), and the nodes between them, else against the car
            _placed[i] = inCar * back;
            if (_isParent[i]) _unplaced[i] = Sx.Matrix.Invert(_placed[i]);
            held.Node.LocalMatrix = held.Parent >= 0 ? _placed[i] * _unplaced[held.Parent] * held.FromParent : _placed[i] * held.FromCar;
        }

        return true;
    }

    // How far the body has to lean or lift from where the shadows were last drawn before they are drawn again: under a
    // third of a degree and 5 mm, more than an idling engine shakes it from one side to the other (a lumpy four: about
    // a quarter of a degree and 4 mm), so a car ticking over does not have its shadow maps drawn again every frame. A
    // blip leans it by more than a degree; a shadow that far behind moves a few millimetres on the floor.
    public const float ShadowLeanStep = 0.005f;
    public const float ShadowHeaveStep = 0.005f;

    /// <summary>
    /// The body has moved far enough from <paramref name="shadowed"/>, the pose its shadows were drawn for, that they
    /// would show it: time to draw them again. Settling back to rest is drawn by the release, whatever the step.
    /// </summary>
    public static bool ShadowsDue(BodyPose shadowed, BodyPose now) =>
        Math.Abs(now.Roll - shadowed.Roll) >= ShadowLeanStep
        || Math.Abs(now.Pitch - shadowed.Pitch) >= ShadowLeanStep
        || Math.Abs(now.Heave - shadowed.Heave) >= ShadowHeaveStep;

    /// <summary>Everything back where it was</summary>
    public void Release()
    {
        _car.LocalMatrix = _base;
        foreach (var held in _held) held.Node.LocalMatrix = held.Local;
        _pose = BodyPose.Rest;
        _wheelSpin = 0f;
        _steer = 0f;
        WorldLean = Sx.Matrix.Identity;
    }
}
