using System.IO;
using System.Windows;
using System.Windows.Input;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Logging;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.Text;
using static StreetCorsa.Helpers.Matrices;

namespace StreetCorsa.Controls;

/// <summary>
/// The garage's lift: the car goes up to its stop with its parts, the wheels hanging, the post rising under it; where
/// the lift stands, and the car's hubs with the lift's height taken off, which the parts are laid out by
/// </summary>
public partial class CarViewport3D
{
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

        // Already there (a stop changed and changed back before a frame moved the lift): a move begun is over, or
        // the pump would never hear it stop (StepLift) and the lift's buttons would stay off with the hum on
        if (_saddleNow == SaddleGoal)
        {
            QuietLift();
            return;
        }

        _liftHum ??= Audio.AudioHub.Shared.PlayLoop(Audio.Sfx.LiftMoves);
        IsLiftMoving = true;
        AnimateFor(AnimationWindow);
    }

    /// <summary>The lift is not on its way: its hum stops, and the car may be worked on</summary>
    private void QuietLift()
    {
        Audio.AudioHub.Shared.StopLoop(_liftHum);
        _liftHum = null;
        IsLiftMoving = false;
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

    #region Lift

    // How high the lift has the car now (worked out from the saddle: LiftLayout.CarHeight), where the saddle's top is,
    // where the car's body is over the pads with the car on its wheels, and the pump heard while it goes
    private float _liftNow;
    private float _saddleNow = LiftLayout.SaddleDown;
    private float _underBody = LiftLayout.UnderBody;
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

    // Where the saddle's top goes for the stop asked for: down where the lift rests, else the car's height over the body's underside
    private float SaddleGoal => LiftLayout.SaddleGoal(LiftStop, _underBody, Renderer?.LiftSaddleDown ?? LiftLayout.SaddleDown);

    protected override float LiftHeight => _liftNow;

    /// <summary>The lowest the camera may look from: under level only while the car is up off its wheels</summary>
    private float LowestBeta(float radius, float targetY)
    {
        if (_liftNow < LiftLayout.WheelsClearHeight / 2f || radius <= 0f) return MinBeta;
        return -MathF.Asin(Math.Clamp((targetY - EyeOffFloor) / radius, 0f, MathF.Sin(UnderCarBeta)));
    }

    /// <summary>
    /// The lift a step nearer its stop: the saddle comes up to the body first, then carries the car with its parts; down,
    /// the car settles on its wheels and the saddle goes on down to where it rests. False when it is there.
    /// </summary>
    private bool StepLift(GarageRenderer renderer, float dt)
    {
        var goal = SaddleGoal;
        if (_saddleNow == goal || renderer.CarNode == null) return false;

        var left = goal - _saddleNow;
        var step = LiftLayout.Speed * Math.Min(dt, GarageRenderer.MaxTickSeconds) * Math.Clamp(Math.Abs(left) / LiftEaseDistance, LiftSlowest, 1f);
        if (step >= Math.Abs(left))
        {
            _saddleNow = goal;
            QuietLift();
            Audio.Sfx.Play(Audio.Sfx.LiftStops);

            // Down on the floor, the camera comes back up out from under it; up at a stop, it stays under
            if (LiftStop == Models.GameState.LiftStop.Down && _betaGoal < MinBeta) _betaGoal = MinBeta;
        }
        else
        {
            _saddleNow += Math.Sign(left) * step;
        }

        renderer.LiftSaddle = _saddleNow;
        var height = LiftLayout.CarHeight(_saddleNow, _underBody);
        if (height != _liftNow)
        {
            _liftNow = height;
            renderer.Droop = LiftLayout.Droop(_liftNow);
            OnPoseChanged();
        }

        return true;
    }

    /// <summary>
    /// Whether a part on this car slot hangs with the wheels as the lift takes the body up: a wheel, a brake, the rear axle
    /// (with all it carries), a leaf spring (with its traction bar); not a coil, a shock or a torsion bar on the body
    /// </summary>
    private static bool HangsOnLift(int carSlot, PartDefinition part)
    {
        if (carSlot == RearAxle.CarSlot) return true;
        var corner = RunningGear.CornerOf(carSlot);
        return corner >= 0 && (carSlot == RunningGear.WheelSlot(corner) || carSlot == RunningGear.BrakeSlot(corner)
                               || carSlot == RunningGear.SpringSlot(corner) && RunningGear.IsLeafSpring(part));
    }

    /// <summary>The running gear that hangs with the wheels (<see cref="HangsOnLift"/>), with everything on it</summary>
    private static HashSet<InstalledPart> Unsprung(IReadOnlyList<CarPart>? gear)
    {
        var hanging = new HashSet<InstalledPart>(ReferenceEqualityComparer.Instance);
        foreach (var part in gear ?? Array.Empty<CarPart>())
            if (HangsOnLift(part.CarSlot, part.Root.Definition)) hanging.UnionWith(part.Root.SelfAndDescendants());
        return hanging;
    }

    /// <summary>
    /// The nodes of the wheels the car's own model stands in for, with their corners: a corner on its factory rim and tyre,
    /// where the car shows its own wheel (<see cref="ShownSet.Wheels"/> has none there), shows it in the parts view too
    /// </summary>
    private static List<(int Node, int Corner)> StandIns(IReadOnlyList<PlacedPart> nodes, IReadOnlyList<CarPart>? gear, ShownSet? shown)
    {
        var standIns = new List<(int, int)>();
        foreach (var part in gear ?? Array.Empty<CarPart>())
        {
            if (!RunningGear.IsWheelSlot(part.CarSlot)) continue;
            var corner = RunningGear.CornerOf(part.CarSlot);
            if (shown != null && corner < shown.Wheels.Count && shown.Wheels[corner] != null) continue;

            var wheel = new HashSet<InstalledPart>(part.Root.SelfAndDescendants(), ReferenceEqualityComparer.Instance);
            for (var i = 0; i < nodes.Count; i++)
                if (nodes[i].Source is { } source && wheel.Contains(source)) standIns.Add((i, corner));
        }

        return standIns;
    }

    private static IEnumerable<int> NodesHanging(IReadOnlyList<PlacedPart> nodes, HashSet<InstalledPart> hanging) =>
        Enumerable.Range(0, nodes.Count).Where(i => nodes[i].Source is { } source && hanging.Contains(source));

    /// <summary>A car just loaded comes at its stop, the lift under it with no move to hear</summary>
    private void SnapLift()
    {
        QuietLift();
        var renderer = Renderer;
        _underBody = renderer == null ? LiftLayout.UnderBody : UnderBodyOf(renderer);
        _saddleNow = renderer?.CarNode == null ? renderer?.LiftSaddleDown ?? LiftLayout.SaddleDown : SaddleGoal;
        _liftNow = renderer?.CarNode == null ? 0f : LiftLayout.CarHeight(_saddleNow, _underBody);
        if (renderer != null)
        {
            renderer.LiftSaddle = _saddleNow;
            renderer.Droop = LiftLayout.Droop(_liftNow);
            PlaceLift(renderer);
        }

        if (_liftNow < LiftLayout.WheelsClearHeight / 2f && _betaGoal < MinBeta) _betaGoal = MinBeta;
        OnPoseChanged();
    }

    /// <summary>
    /// Where the car in the bay has its body's underside over the lift's pads, on its wheels (whatever the lift has it
    /// at now): what the saddle comes up to before the car goes up with it. Never under where the saddle rests: a lift
    /// whose arms lying on the floor reach a low body already takes it as they move, and the car stays on its wheels
    /// with the lift down.
    /// </summary>
    private float UnderBodyOf(GarageRenderer renderer)
    {
        var down = renderer.LiftSaddleDown;
        if (renderer.CarNode is not { } car || GetAnchors(car) is not { } anchors) return Math.Max(LiftLayout.UnderBody, down);

        var under = LiftLayout.UnderCar(anchors, _liftProp?.Placement ?? System.Numerics.Matrix4x4.Identity);
        return Math.Max(LiftLayout.UnderBodyOf(renderer.BodyUnderside(ToSlim(under)) - LiftedBy(car)), down);
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

        await LoadPropAsync(renderer, LiftLayout.PropId, builtIn: true, LiftLayout.CreateModel, (kn5, lift) =>
        {
            _liftProp = lift;
            renderer.SetLift(kn5, ToSlim(LiftPlacement(renderer, lift)));

            // The model came after the car: its saddle rests where the model has it, not where the game's own would
            if (!IsLiftMoving) SnapLift();
        });
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
}
