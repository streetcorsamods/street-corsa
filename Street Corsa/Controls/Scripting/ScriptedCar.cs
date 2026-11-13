using StreetCorsa.Audio;
using StreetCorsa.Controls.Street;
using StreetCorsa.Services.Scripting;

namespace StreetCorsa.Controls.Scripting;

/// <summary>An <see cref="EngineRunner"/> as a script works it; the runner may come and go (the garage's is worked out late)</summary>
internal sealed class ScriptEngine(Func<EngineRunner?> runner) : IScriptEngine
{
    public EngineRunner? Runner => runner();

    public bool Runs => Runner?.Spec is { Runs: true };

    public bool IsRunning => Runner?.State == EngineState.Running;

    /// <summary>A drive's top is the redline when the engine has one under its limiter: the shifts come short of where it hurts</summary>
    public (double Idle, double Limiter) Revs => Runner is { } r
        ? (r.Spec?.Idle ?? r.IdleRpm, r.Spec?.Redline ?? 6000)
        : (800, 6000);

    /// <summary>Nothing while it is cranking or running already: a step that keeps trying never turns it off again</summary>
    public void Start(bool crank)
    {
        if (Runner is not { } r || r.IsRunning) return;
        if (crank) r.Toggle();
        else r.StartRunning();
    }

    public void Stop()
    {
        if (Runner is { IsRunning: true } r) r.Toggle();
    }

    public void Pedal(double? depth) => Runner?.SetPedal(depth);

    /// <summary>On top of the level the screen has it at (and where it has it heard from), never in place of it</summary>
    public void Volume(float scale)
    {
        if (Runner is { } r) r.Fade = scale;
    }
}

/// <summary>
/// A car a script moves on a viewport: where it stands (<see cref="Home"/>) and where it is (<see cref="Pose"/>), on
/// the scene's ground; the drive under way, a frame at a time (<see cref="Step"/>); how far its wheels have turned and
/// its front wheels are steered; how its body squats, dives and leans on its springs; its engine driven by the wheels
/// and its brake lights. The viewport puts the car where this says, its own way (a garage's floor, a station's ground).
/// </summary>
internal sealed class ScriptedCar : IScriptCar
{
    // Squat and dive, and the body leaning out of a bend (the same springs, sideways)
    private readonly ChassisPitch _pitch = new();
    private readonly ChassisPitch _lean = new();
    private readonly Action<bool?, bool?> _lights;

    private DrivePlan? _plan;
    private float _time;
    private float _lastDistance;
    private float _leanShare;
    private bool _braking;
    private TaskCompletionSource? _driving;

    // Whoever waits for the drive under way to have this many seconds left (a script setting off on the next one early)
    private readonly List<(float Seconds, TaskCompletionSource Done)> _leftWaits = [];

    // How far past its end the last frame of the drive that just ended went, seconds, and where it ended: a drive that
    // goes on from there before the car has stood a frame starts that far into itself (Drive)
    private float _overshoot;

    /// <param name="lights">The car's headlights and brake lights on or off (null: as they are)</param>
    public ScriptedCar(GroundPose home, ScriptEngine? engine, Action<bool?, bool?> lights)
    {
        Home = home;
        Pose = home;
        Engine = engine;
        _lights = lights;
    }

    public GroundPose Home { get; }

    public GroundPose Pose { get; private set; }

    public ScriptEngine? Engine { get; }

    IScriptEngine? IScriptCar.Engine => Engine;

    /// <summary>The drive under way, and how far into it</summary>
    public DrivePlan? Plan => _plan;

    /// <summary>Metres a second, now</summary>
    public float Speed { get; private set; }

    /// <summary>How far the wheels have turned about their axles, radians</summary>
    public float WheelSpin { get; private set; }

    /// <summary>How far the front wheels are turned, radians, to the left positive</summary>
    public float Steer { get; private set; }

    /// <summary>What the moves add to the body's roll and pitch: leaning out of a bend, squatting and diving</summary>
    public (float Roll, float Pitch) Body => (_leanShare * _lean.Pitch, _pitch.Pitch);

    public bool IsSettled => _plan == null && _pitch.IsSettled && _lean.IsSettled;

    /// <summary>Standing where it stands (a drive ends there to within its sums), at rest: the viewport can let go of it</summary>
    public bool IsHome => IsSettled && MathF.Abs(Pose.X - Home.X) < 1e-3f && MathF.Abs(Pose.Z - Home.Z) < 1e-3f
                          && MathF.Abs(GroundPose.Turn(Home.Heading, Pose.Heading)) < 1e-3f;

    public float DriveLeft => _plan == null ? 0f : Math.Max(0f, _plan.Duration - _time);

    /// <summary>The drive under way, <paramref name="seconds"/> on from now (the steering wheel turns into a bend early); null when not driving</summary>
    public DriveMoment? Ahead(float seconds) => _plan?.At(_time + seconds);

    /// <summary>A move started: the viewport draws frames for it</summary>
    public event Action? Started;

    public void Lights(bool? head, bool? brake)
    {
        if (brake is { } on) _braking = on;
        _lights(head, brake);
    }

    /// <summary>The brake lights as the drive has them, switched only when they change</summary>
    private void Brakes(bool on)
    {
        if (on == _braking) return;
        _braking = on;
        _lights(null, on);
    }

    public Task Drive(DrivePlan plan)
    {
        // A drive on from where the last one ended, rolling, in the frame after: the time the last frame ran past that end
        // is this drive's, or each such join lost up to a frame of travel (a hitch at speed)
        var carried = _plan == null && _overshoot > 0f && SamePlace(plan.Start.Pose, Pose) ? _overshoot : 0f;
        EndDrive();
        _plan = plan;
        _time = carried;
        _leanShare = plan.Feel.Lean;
        var start = plan.Start;
        _lastDistance = start.Distance;
        Pose = start.Pose;
        Steer = start.Steer;
        Speed = start.Speed;
        Brakes(start.Braking);
        _driving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = _driving.Task;
        Started?.Invoke();
        return done;
    }

    public void Jump(DrivePlan plan)
    {
        EndDrive();
        _overshoot = 0f;
        var end = plan.End;
        WheelSpin += Sign(plan) * plan.Feel.WheelAngle(end.Distance - plan.Start.Distance);
        Pose = end.Pose;
        Steer = end.Steer;
        Speed = 0f;
        _pitch.Reset();
        _lean.Reset();
        Started?.Invoke();
    }

    public void Place(GroundPose pose)
    {
        EndDrive();
        _overshoot = 0f;
        Pose = pose;
        Speed = 0f;
        Started?.Invoke();
    }

    /// <summary>Back where it stands at once, at rest, its drive over (the screen stays after all)</summary>
    public void GoHome()
    {
        EndDrive();
        _overshoot = 0f;
        Pose = Home;
        Steer = 0f;
        Speed = 0f;
        _pitch.Reset();
        _lean.Reset();
    }

    /// <summary>Moves on by <paramref name="dt"/> seconds; true while it moves or its body has still to settle</summary>
    public bool Step(float dt)
    {
        if (_plan is not { } plan)
        {
            // A frame stood still: the next drive starts from its own beginning
            _overshoot = 0f;
            _pitch.Tick(dt, 0);
            _lean.Tick(dt, 0);
            return !IsSettled;
        }

        _time += dt;
        var now = plan.At(_time);
        WheelSpin += Sign(plan) * plan.Feel.WheelAngle(now.Distance - _lastDistance);
        _lastDistance = now.Distance;
        Pose = now.Pose;
        Steer = now.Steer;
        Speed = now.Speed;
        _pitch.Tick(dt, now.Acceleration);

        // Across the car: v^2 over the radius, towards the inside of the bend; the body leans the other way
        _lean.Tick(dt, now.Speed * now.Speed * now.Curvature);

        if (Engine?.Runner is { IsRunning: true } runner) runner.Drive(now.Rpm, now.Throttle);
        Brakes(now.Braking);

        if (now.Done)
        {
            _overshoot = now.Speed > 0.01f ? Math.Max(0f, _time - plan.Duration) : 0f;
            EndDrive();
        }

        TellDriveLeft();
        return true;
    }

    /// <summary>
    /// Done once the drive under way has <paramref name="seconds"/> left or less, or is over (at once when it has): on the
    /// frame it gets there, so a script sets off on its next drive then, not on a poll's tick after
    /// </summary>
    public Task DriveLeftAtMost(float seconds)
    {
        if (DriveLeft <= seconds) return Task.CompletedTask;

        // Let go of on the dispatcher, after the frame's step: the next drive is not started from inside this one
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _leftWaits.Add((seconds, done));
        return done.Task;
    }

    /// <summary>The waits for the drive to have so long left that it has now let go</summary>
    private void TellDriveLeft()
    {
        if (_leftWaits.Count == 0) return;

        var left = DriveLeft;
        for (var i = _leftWaits.Count - 1; i >= 0; i--)
        {
            if (left > _leftWaits[i].Seconds) continue;
            _leftWaits[i].Done.TrySetResult();
            _leftWaits.RemoveAt(i);
        }
    }

    private static bool SamePlace(GroundPose a, GroundPose b) =>
        MathF.Abs(a.X - b.X) < 1e-3f && MathF.Abs(a.Z - b.Z) < 1e-3f && MathF.Abs(GroundPose.Turn(a.Heading, b.Heading)) < 1e-3f;

    /// <summary>The drive under way over where it is: out of gear, off the brakes, and whoever waited on it let go</summary>
    private void EndDrive()
    {
        if (_plan == null) return;

        _plan = null;
        Speed = 0f;
        Engine?.Runner?.Drive(null);
        Brakes(false);
        _driving?.TrySetResult();
        _driving = null;
        TellDriveLeft();
    }

    /// <summary>Lets go of whoever waits on the drive, the car gone with its scene</summary>
    public void Drop()
    {
        _plan = null;
        _overshoot = 0f;
        Engine?.Runner?.Drive(null);
        _driving?.TrySetResult();
        _driving = null;
        TellDriveLeft();
    }

    private static float Sign(DrivePlan plan) => plan.Reverse ? -1f : 1f;
}
