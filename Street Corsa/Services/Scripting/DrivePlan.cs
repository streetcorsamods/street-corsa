using StreetCorsa.Services.Station;

namespace StreetCorsa.Services.Scripting
{
    /// <summary>Where a car on a drive is, a moment at a time</summary>
    /// <param name="X">Metres from the scene's middle</param>
    /// <param name="Z">Metres from the scene's middle</param>
    /// <param name="Heading">The way the car faces (<see cref="GroundPose.Heading"/>); backwards to its way in reverse</param>
    /// <param name="Distance">Metres travelled since the start, for the wheels</param>
    /// <param name="Speed">Metres a second</param>
    /// <param name="Acceleration">Metres a second squared along the way; negative under the brakes</param>
    /// <param name="Curvature">How sharply the way bends here, per metre (one over the radius); to the left positive</param>
    /// <param name="Steer">How far the front wheels are turned, radians, to the left positive</param>
    /// <param name="Rpm">The engine's speed while the wheels turn it; null with the clutch in (it idles on its own)</param>
    /// <param name="Throttle">0..1</param>
    /// <param name="Braking">The brake lights are on</param>
    /// <param name="Done">At the end of the drive</param>
    public readonly record struct DriveMoment(float X, float Z, float Heading, float Distance, float Speed, float Acceleration,
        float Curvature, float Steer, double? Rpm, double Throttle, bool Braking, bool Done)
    {
        public GroundPose Pose => new(X, Z, Heading);
    }

    /// <summary>
    /// A drive worked out from start to finish before the car moves: a function of time, so the same moment always
    /// looks the same, and the end is known up front (a drive skipped puts the car there). Four kinds
    /// (<see cref="DriveKind"/>), each with its feel's numbers; the engine's revs and the throttle go with the speed.
    /// </summary>
    public sealed class DrivePlan
    {
        // A drive stepped through time is worked out this finely
        private const float TimeStep = 1f / 120f;

        // The longest a launch is followed: one that never gets to the end of its way stops being worked out here
        private const float MostLaunchSeconds = 30f;

        // A road car brakes with its lights on harder than this
        private const float BrakeLightsDecel = 0.6f;

        // A pull steering for a point stops steering for it once it is this close ahead (or behind): it drives on straight
        private const float PassedWithin = 0.5f;

        private readonly float[] _time;
        private readonly DriveMoment[] _moments;

        private DrivePlan(DriveFeel feel, bool reverse, List<(float Time, DriveMoment Moment)> samples)
        {
            Feel = feel;
            Reverse = reverse;
            _time = samples.Select(s => s.Time).ToArray();
            _moments = samples.Select(s => s.Moment).ToArray();
            _moments[^1] = _moments[^1] with { Done = true };
        }

        public DriveFeel Feel { get; }

        /// <summary>The car goes backwards: its wheels turn back, its nose trails</summary>
        public bool Reverse { get; }

        /// <summary>How long it takes, start to finish, in seconds</summary>
        public float Duration => _time[^1];

        public DriveMoment Start => _moments[0];

        public DriveMoment End => _moments[^1];

        public DriveMoment At(float t)
        {
            var n = _time.Length;
            if (t >= _time[n - 1]) return _moments[n - 1];
            if (t <= 0f) return _moments[0];

            var i = Array.BinarySearch(_time, t);
            if (i >= 0) return _moments[i];
            i = ~i;
            var f = (t - _time[i - 1]) / Math.Max(1e-6f, _time[i] - _time[i - 1]);
            var (a, b) = (_moments[i - 1], _moments[i]);
            return new DriveMoment(Lerp(a.X, b.X, f), Lerp(a.Z, b.Z, f), a.Heading + GroundPose.Turn(a.Heading, b.Heading) * f,
                Lerp(a.Distance, b.Distance, f), Lerp(a.Speed, b.Speed, f), Lerp(a.Acceleration, b.Acceleration, f),
                Lerp(a.Curvature, b.Curvature, f), Lerp(a.Steer, b.Steer, f),
                a.Rpm is { } ra && b.Rpm is { } rb ? ra + (rb - ra) * f : a.Rpm, a.Throttle + (b.Throttle - a.Throttle) * f, a.Braking, false);
        }

        /// <summary>
        /// Along <paramref name="path"/>, slow enough in the bends to take them gently, cruising between: rolling at the
        /// start (<paramref name="rolling"/>) or from a standstill, and braking to a stop at the end
        /// (<paramref name="stop"/>) or still going there. The engine goes up through the gears as the speed does (or
        /// stays in the feel's gear).
        /// </summary>
        public static DrivePlan Road(DrivePath path, DriveFeel feel, double idleRpm, bool rolling, bool stop, bool reverse = false)
        {
            var idle = Idle(idleRpm);
            var n = path.Count;

            // The fastest each point can be taken: the bend there
            var limit = new float[n];
            for (var i = 0; i < n; i++)
            {
                var curvature = MathF.Abs(path.Curvature(i));
                limit[i] = curvature < 1e-4f ? feel.CruiseSpeed : Math.Min(feel.CruiseSpeed, MathF.Sqrt(feel.CornerAccel / curvature));
            }

            // Speeding up where it can, braking in time for what comes
            var speed = new float[n];
            speed[0] = rolling ? limit[0] : 0f;
            for (var i = 1; i < n; i++)
                speed[i] = Math.Min(limit[i], MathF.Sqrt(Sq(speed[i - 1]) + 2 * feel.Acceleration * (path.Distance(i) - path.Distance(i - 1))));
            if (stop) speed[n - 1] = 0f;
            for (var i = n - 2; i >= 0; i--)
                speed[i] = Math.Min(speed[i], MathF.Sqrt(Sq(speed[i + 1]) + 2 * feel.Brake * (path.Distance(i + 1) - path.Distance(i))));

            var time = new float[n];
            for (var i = 1; i < n; i++)
            {
                var ds = path.Distance(i) - path.Distance(i - 1);
                var mean = (speed[i] + speed[i - 1]) / 2;
                time[i] = time[i - 1] + (mean > 0.05f ? ds / mean : MathF.Sqrt(2 * ds / Math.Max(0.1f, feel.Acceleration)));
            }

            var samples = new List<(float, DriveMoment)>(n);
            for (var i = 0; i < n; i++)
            {
                var done = i == n - 1;
                var (p, q) = (Math.Max(0, i - 1), Math.Min(n - 1, i + 1));
                var accel = done || time[q] <= time[p] ? 0f : (speed[q] - speed[p]) / (time[q] - time[p]);
                var braking = accel < -BrakeLightsDecel && !done;

                double? rpm = stop && (done || braking && speed[i] < feel.ClutchSpeed) ? null : RoadRpm(feel, speed[i], idle, !rolling);
                var throttle = accel > 0.2f ? Math.Min(1.0, 0.3 + accel / 3) : braking ? 0 : feel.CruiseThrottle;
                samples.Add((time[i], Moment(path.X(i), path.Z(i), path.Heading(i), path.Distance(i), speed[i], accel, path.Curvature(i),
                    feel, rpm, throttle, braking, reverse)));
            }

            return new DrivePlan(feel, reverse, samples);
        }

        /// <summary>What a road car's engine turns at: its gear's revs, never under the idle (a slipping clutch off the line)</summary>
        private static double RoadRpm(DriveFeel feel, float speed, double idle, bool standingStart)
        {
            var gear = feel.Gear > 0 ? feel.Gear - 1 : speed < 4.5f ? 0 : speed < 9f ? 1 : speed < 15f ? 2 : 3;
            return Math.Max(speed < feel.ClutchSpeed && standingStart ? idle * 1.8 : idle, feel.GearRpm(speed, gear));
        }

        /// <summary>
        /// Hard off the line from a standstill along <paramref name="path"/> until its end: the pull falls off with speed,
        /// and each change of gear is a moment off the throttle, at the revs the engine's limiter allows
        /// </summary>
        public static DrivePlan Launch(DrivePath path, DriveFeel feel, double idleRpm, double limiterRpm, bool reverse = false)
        {
            var idle = Idle(idleRpm);
            var limiter = limiterRpm is > 1000 and < 20000 ? limiterRpm : 6000;
            var shiftRpm = Math.Max(idle * 3, limiter * 0.85);
            var ratios = feel.Ratios;

            var position = 0f;
            var speed = 0f;
            var gear = 0;
            var shifting = 0f;
            var accel = 0f;
            var t = 0f;
            var samples = new List<(float, DriveMoment)> { (0f, OnPath(path, 0f, 0f, 0f, feel, idle * 1.6, 1.0, false, reverse)) };
            while (position < path.Length && t < MostLaunchSeconds)
            {
                if (shifting > 0f)
                {
                    shifting -= TimeStep;
                    accel = 0.3f;
                }
                else
                {
                    accel = Math.Min(feel.Acceleration, feel.Acceleration * (1f - speed / feel.TopSpeed) * ratios[gear] / ratios[0] * 1.6f);
                    if (gear + 1 < ratios.Count && feel.GearRpm(speed, gear) >= shiftRpm)
                    {
                        gear++;
                        shifting = feel.ShiftSeconds;
                    }
                }

                speed = Math.Min(feel.TopSpeed, speed + accel * TimeStep);
                position += speed * TimeStep;
                t += TimeStep;
                var rpm = Math.Max(feel.GearRpm(speed, gear), idle * 1.6);
                samples.Add((t, OnPath(path, position, speed, accel, feel, rpm, shifting > 0f ? 0.1 : 1.0, false, reverse)));
            }

            return new DrivePlan(feel, reverse, samples);
        }

        /// <summary>
        /// Rolling in along <paramref name="path"/> at the feel's speed and braking to a stop at its end, the braking
        /// fading to nothing as it stops (the distance left goes as the cube of the time left), the brake lights on until
        /// it has
        /// </summary>
        public static DrivePlan RollIn(DrivePath path, DriveFeel feel, double idleRpm, bool reverse = false)
        {
            var idle = Idle(idleRpm);
            var length = path.Length;
            var seconds = length > 0f ? 3f * length / Math.Max(0.1f, feel.CruiseSpeed) : 0f;
            var samples = new List<(float, DriveMoment)>();
            for (var t = 0f; ; t = Math.Min(seconds, t + TimeStep))
            {
                var left = seconds > 0f ? Math.Max(0f, 1f - t / seconds) : 0f;
                var speed = seconds > 0f ? 3f * length / seconds * left * left : 0f;
                var accel = seconds > 0f ? -6f * length / (seconds * seconds) * left : 0f;
                double? rpm = left > 0f ? idle + speed * feel.RpmPerMetrePerSecond : null;
                samples.Add((t, OnPath(path, length * (1f - left * left * left), speed, accel, feel, rpm, 0, left > 0f, reverse)));
                if (t >= seconds) break;
            }

            return new DrivePlan(feel, reverse, samples);
        }

        /// <summary>
        /// Pulling away in first gear from <paramref name="from"/>, gently, to the feel's pace and keeping on: the clutch
        /// takes up, and the pull fades as the car nears the pace it keeps. Given <paramref name="toward"/> it drives
        /// straight out of its place, then winds the wheel on and turns (about its rear axle, only as far as its wheels
        /// are turned) for that point, and on past it straight.
        /// </summary>
        public static DrivePlan Pull(GroundPose from, GroundPoint? toward, DriveFeel feel, double idleRpm)
        {
            var idle = Idle(idleRpm);
            var (x, z, heading) = (from.X, from.Z, from.Heading);
            var hw = feel.HalfWheelbase;
            var speed = 0f;
            var distance = 0f;
            var steer = 0f;
            var passed = toward == null;

            var samples = new List<(float, DriveMoment)> { (0f, new DriveMoment(x, z, heading, 0f, 0f, 0f, 0f, 0f, idle * 1.1, feel.CruiseThrottle, false, false)) };
            for (var t = TimeStep; t <= feel.Seconds + 1e-4f; t += TimeStep)
            {
                // The clutch takes up, then the pull fades towards the pace the car keeps
                var clutch = Math.Min(1f, t / Math.Max(1e-3f, feel.ClutchSeconds));
                clutch = clutch * clutch * (3f - 2f * clutch);
                var pace = speed / feel.TopSpeed;
                var accel = feel.Acceleration * clutch * Math.Max(0f, 1f - pace * pace);
                speed = Math.Min(feel.TopSpeed, speed + accel * TimeStep);
                var step = speed * TimeStep;

                // Where it is going, until it is there: the wheel wound towards the lock the bend wants
                var lockAngle = 0f;
                if (!passed && toward is { } point)
                {
                    var ahead = MathF.Sin(heading) * (point.X - x) + MathF.Cos(heading) * (point.Z - z);
                    passed = ahead < PassedWithin;
                    if (!passed)
                    {
                        var wanted = MathF.Atan2(point.X - x, point.Z - z);
                        var bend = Math.Clamp(GroundPose.Turn(heading, wanted) * feel.SteerGain, -1f / feel.TurningRadius, 1f / feel.TurningRadius);
                        lockAngle = distance > feel.StraightOut - feel.SteerLead ? MathF.Atan(2f * hw * bend) : 0f;
                    }
                }

                steer += (lockAngle - steer) * (1f - MathF.Exp(-TimeStep / Math.Max(1e-3f, feel.SteerSeconds)));

                // Where the front wheels point is the bend the car is on; the rear axle goes where the car points
                var curvature = MathF.Tan(steer) / (2f * hw);
                var rearX = x - MathF.Sin(heading) * hw;
                var rearZ = z - MathF.Cos(heading) * hw;
                heading = GroundPose.Turn(0f, heading + curvature * step);
                rearX += MathF.Sin(heading) * step;
                rearZ += MathF.Cos(heading) * step;
                x = rearX + MathF.Sin(heading) * hw;
                z = rearZ + MathF.Cos(heading) * hw;
                distance += step;

                var throttle = feel.CruiseThrottle + (feel.Throttle - feel.CruiseThrottle) * accel / Math.Max(1e-3f, feel.Acceleration);
                samples.Add((t, new DriveMoment(x, z, heading, distance, speed, accel, curvature, steer,
                    idle * 1.1 + speed * feel.RpmPerMetrePerSecond, throttle, false, false)));
            }

            return new DrivePlan(feel, false, samples);
        }

        /// <summary>A moment <paramref name="distance"/> metres along a path</summary>
        private static DriveMoment OnPath(DrivePath path, float distance, float speed, float accel, DriveFeel feel, double? rpm,
            double throttle, bool braking, bool reverse)
        {
            var (x, z, heading, curvature) = path.At(distance);
            return Moment(x, z, heading, distance, speed, accel, curvature, feel, rpm, throttle, braking, reverse);
        }

        /// <summary>
        /// The car at a point of its way, facing along it (or away from it in reverse), its front wheels turned for the
        /// bend (the other way in reverse: the front swings out)
        /// </summary>
        private static DriveMoment Moment(float x, float z, float heading, float distance, float speed, float accel, float curvature,
            DriveFeel feel, double? rpm, double throttle, bool braking, bool reverse)
        {
            var steer = MathF.Atan(2f * feel.HalfWheelbase * curvature);
            return new DriveMoment(x, z, reverse ? GroundPose.Turn(0f, heading + MathF.PI) : heading, distance, speed, accel, curvature,
                reverse ? -steer : steer, rpm, throttle, braking, false);
        }

        private static double Idle(double idleRpm) => idleRpm is > 200 and < 3000 ? idleRpm : 800;

        private static float Sq(float v) => v * v;

        private static float Lerp(float a, float b, float f) => a + (b - a) * f;
    }
}
