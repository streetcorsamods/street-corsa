using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>How a car on the move drives its engine this frame, and where it has got to</summary>
/// <param name="Rpm">The engine's speed with the wheels turning it; null with the clutch in (it runs free)</param>
/// <param name="Throttle">0..1</param>
/// <param name="Stopped">A car rolling in is on its mark: off the brake</param>
/// <param name="Done">A car rolling in has stopped and its body has settled: it can be let go</param>
internal readonly record struct PullStep(double? Rpm, double Throttle, bool Stopped, bool Done);

/// <summary>
/// A car on the move in a viewport (<see cref="DriveOff"/>): one pulling away out of the scene in first gear, keeping on
/// until it is put back, or one rolling in from behind to where it stands and braking to a stop there. Where it stands
/// (or stood), how far it is from there and how fast it goes, how its body pitches and rolls, and how far its front
/// wheels are turned. Worked out frame by frame; the viewport puts the car where this says.
/// </summary>
internal sealed class CarPull
{
    // A car pulling away in first gear: gently, to a walking pace and a bit, and the revs with it. The clutch takes up
    // over the first moment, and the pull fades as the car nears the pace it keeps.
    private const float PullAcceleration = 1.8f;
    private const float PullTopSpeed = 5f;
    private const float ClutchSeconds = 0.6f;
    private const double PullRpmPerMetrePerSecond = 260;
    private const double PullThrottle = 0.45;
    private const double CruiseThrottle = 0.2;

    // How much of the springs' lean a bend shows: at walking pace a driver eases off into it
    private const float LeanShare = 0.6f;

    // A car steering for a way out: straight out of its place far enough to clear its neighbours, then round on a
    // tight circle until it points at the way out, how sharply it corrects onto it, and the wheel's lock
    private const float StraightOut = 1.2f;
    private const float TurningRadius = 4.2f;
    private const float SteerGain = 1.5f;

    // A car turns about its rear axle, this far behind its middle: the nose swings round, the tail follows
    private const float HalfWheelbase = 1.4f;

    // The wheel is wound on and off over about this long, not snapped to the lock; the car turns only as far as its
    // wheels are turned. Winding on starts this far before the bend.
    private const float SteerSeconds = 0.45f;
    private const float SteerLead = 0.6f;

    // A car rolling in comes off the brake as it stops, as a driver does, and stands until its body has settled
    private const float RollInSpeed = 5f;
    private const float MostSettling = 1.5f;

    // Squat and dive: nose up on the pull, down on the brakes; and the body leaning out of a bend (the same springs,
    // sideways)
    private readonly Street.ChassisPitch _pitch = new();
    private readonly Street.ChassisPitch _lean = new();

    // Steering for a way out: where the car is (world x, z, from its origin), where it is going and its heading
    // (radians, 0 along +z, growing towards +x), where it started, and the way from world space back to its parent
    private readonly (float X, float Z)? _exit;
    private readonly float _startX;
    private readonly float _startZ;
    private readonly float _startHeading;
    private readonly Sx.Matrix _parent = Sx.Matrix.Identity;
    private float _x;
    private float _z;
    private float _heading;
    private Sx.Matrix? _steered;

    // How far a car rolling in comes from, and over how long; 0 for one pulling away
    private readonly float _rollInFrom;
    private readonly float _rollInSeconds;

    private float _time;
    private float _speed;
    private bool _stopped;

    private CarPull(Sx.Matrix from)
    {
        From = from;
    }

    /// <summary>
    /// A car pulling away from where it stands, <paramref name="from"/> in its parent's space (<paramref name="parent"/>
    /// takes that to world space), nose first; given an <paramref name="exit"/> (a point on the floor, world x and z) it
    /// pulls straight out of its place and turns towards it
    /// </summary>
    /// <param name="frontSign">+1 when the model's nose is at +z, -1 when it is at -z</param>
    public static CarPull Away(Sx.Matrix from, Sx.Matrix parent, float frontSign, (float X, float Z)? exit)
    {
        var world = from * parent;
        var heading = MathF.Atan2(world.M31 * frontSign, world.M33 * frontSign);
        return new CarPull(from, exit, parent, world.M41, world.M43, heading);
    }

    /// <summary>A car rolling in nose first from <paramref name="distance"/> metres behind <paramref name="from"/>, where it stops</summary>
    public static CarPull In(Sx.Matrix from, float distance) => new(from, distance);

    private CarPull(Sx.Matrix from, (float X, float Z)? exit, Sx.Matrix parent, float x, float z, float heading) : this(from)
    {
        _exit = exit;
        _parent = parent;
        _x = _startX = x;
        _z = _startZ = z;
        _heading = _startHeading = heading;
    }

    private CarPull(Sx.Matrix from, float distance) : this(from)
    {
        // distance(t) = -D (1 - t/T)^3: in at RollInSpeed = 3D/T, the braking fading to nothing at the stop
        _rollInFrom = distance;
        _rollInSeconds = 3f * distance / RollInSpeed;
        Distance = -distance;
        _speed = RollInSpeed;
    }

    /// <summary>Where the car stands (or stood), the lean left out, in its parent's space</summary>
    public Sx.Matrix From { get; }

    public bool IsRollingIn => _rollInFrom > 0f;

    /// <summary>How far it has come along its own length from where it stood; negative for one still rolling in</summary>
    public float Distance { get; private set; }

    /// <summary>How far the front wheels are turned, radians, the way the car turns</summary>
    public float Steer { get; private set; }

    /// <summary>How far the wheels have turned about their axles, radians</summary>
    public float WheelSpin => Street.RivalDrive.WheelAngle(Distance);

    /// <summary>What the move adds to the body's roll and pitch: leaning out of a bend, squatting and diving</summary>
    public (float Roll, float Pitch) Body => (LeanShare * _lean.Pitch, _pitch.Pitch);

    /// <summary>Done when a car rolling in has stopped (or the move was called off)</summary>
    public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Where the car stands now, in its parent's space: rolled along its own length, or steered</summary>
    public Sx.Matrix Placement(float frontSign) => _steered ?? Sx.Matrix.Translation(0f, 0f, Distance * frontSign) * From;

    /// <summary>Moves on by <paramref name="dt"/> seconds, with the engine idling at <paramref name="idleRpm"/></summary>
    public PullStep Step(float dt, double idleRpm)
    {
        _time += dt;
        return IsRollingIn ? StepIn(dt, idleRpm) : StepAway(dt, idleRpm);
    }

    private PullStep StepAway(float dt, double idleRpm)
    {
        // The clutch takes up, then the pull fades towards the pace the car keeps
        var clutch = Math.Min(1f, _time / ClutchSeconds);
        clutch = clutch * clutch * (3f - 2f * clutch);
        var pace = _speed / PullTopSpeed;
        var acceleration = PullAcceleration * clutch * Math.Max(0f, 1f - pace * pace);
        _speed = Math.Min(PullTopSpeed, _speed + acceleration * dt);

        var distance = _speed * dt;
        var curvature = SteerOn(distance, dt);
        Distance += distance;
        _pitch.Tick(dt, acceleration);

        // Across the car: v^2 over the radius, towards the inside of the bend; the body leans the other way
        _lean.Tick(dt, _speed * _speed * curvature);

        var throttle = CruiseThrottle + (PullThrottle - CruiseThrottle) * acceleration / PullAcceleration;
        return new PullStep(idleRpm * 1.1 + _speed * PullRpmPerMetrePerSecond, throttle, false, false);
    }

    /// <summary>
    /// A car steering for its way out moves on by <paramref name="distance"/> metres: the wheels wind towards the lock
    /// the bend wants, and the car turns as far as they are turned (about its rear axle). Returns how sharply it is
    /// turning, radians per metre, for the lean.
    /// </summary>
    private float SteerOn(float distance, float dt)
    {
        if (_exit is not { } exit) return 0f;

        var wanted = MathF.Atan2(exit.X - _x, exit.Z - _z);
        var bend = Math.Clamp(Turn(_heading, wanted) * SteerGain, -1f / TurningRadius, 1f / TurningRadius);
        var lockAngle = Distance > StraightOut - SteerLead ? MathF.Atan(2f * HalfWheelbase * bend) : 0f;
        Steer += (lockAngle - Steer) * (1f - MathF.Exp(-dt / SteerSeconds));

        // Where the front wheels point is the bend the car is on
        var curvature = MathF.Tan(Steer) / (2f * HalfWheelbase);

        // The rear axle goes where the car points; the middle is ahead of it
        var rearX = _x - MathF.Sin(_heading) * HalfWheelbase;
        var rearZ = _z - MathF.Cos(_heading) * HalfWheelbase;
        _heading = Turn(0f, _heading + curvature * distance);
        rearX += MathF.Sin(_heading) * distance;
        rearZ += MathF.Cos(_heading) * distance;
        _x = rearX + MathF.Sin(_heading) * HalfWheelbase;
        _z = rearZ + MathF.Cos(_heading) * HalfWheelbase;

        // Turned about its own origin from where it stood, then moved to where it is now; RotationY adds to the heading
        var world = From * _parent
                    * Sx.Matrix.Translation(-_startX, 0f, -_startZ)
                    * Sx.Matrix.RotationY(_heading - _startHeading)
                    * Sx.Matrix.Translation(_x, 0f, _z);
        _steered = world * Sx.Matrix.Invert(_parent);
        return curvature;
    }

    private PullStep StepIn(float dt, double idleRpm)
    {
        var left = Math.Max(0f, 1f - _time / _rollInSeconds);
        Distance = -_rollInFrom * left * left * left;
        _speed = 3f * _rollInFrom / _rollInSeconds * left * left;
        var acceleration = -6f * _rollInFrom / (_rollInSeconds * _rollInSeconds) * left;
        _pitch.Tick(dt, acceleration);

        double? rpm = left > 0f ? idleRpm + _speed * PullRpmPerMetrePerSecond * 0.6 : null;
        if (left > 0f) return new PullStep(rpm, 0, false, false);

        // On its mark: off the brake once; the body settles before the car is let go
        var stopped = !_stopped;
        _stopped = true;
        var done = _pitch.IsSettled || _time >= _rollInSeconds + MostSettling;
        return new PullStep(null, 0, stopped, done);
    }

    /// <summary>The turn from one heading to another, radians, the short way round (within half a turn either way)</summary>
    internal static float Turn(float from, float to) => (float)Math.IEEERemainder(to - from, 2 * Math.PI);
}
