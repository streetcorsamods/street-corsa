using StreetCorsa.Services.Station;

namespace StreetCorsa.Controls.Street;

/// <summary>Where a car driving a path is, a moment at a time</summary>
/// <param name="X">Metres from the scene's middle</param>
/// <param name="Z">Metres from the scene's middle</param>
/// <param name="Heading">Degrees about the vertical, 0 facing +z (as a street's spots)</param>
/// <param name="Distance">Metres driven since the start, for the wheels</param>
/// <param name="Speed">Metres a second</param>
/// <param name="Acceleration">Metres a second squared along the path; negative under the brakes</param>
/// <param name="Rpm">The engine's speed while the wheels turn it; null with the clutch in (it idles on its own)</param>
/// <param name="Throttle">0..1</param>
/// <param name="Braking">The brake lights are on</param>
/// <param name="Done">At the end of the path</param>
/// <param name="Curvature">How sharply the path bends here, per metre (one over the radius); to the left positive</param>
public readonly record struct PathState(float X, float Z, float Heading, float Distance, float Speed, float Acceleration, double? Rpm,
    double Throttle, bool Braking, bool Done, float Curvature = 0f);

/// <summary>
/// A car driven along a path through a scene: a smooth curve through the scene's points (a centripetal Catmull-Rom,
/// which never overshoots into a loop or a kink where a short stretch meets a long one, as the uniform one did, and the
/// car spun on the spot there), slow enough in the bends to take them gently (<see cref="CornerAccel"/>), cruising
/// between. It arrives already rolling and brakes to a stop at the last
/// point, or leaves from a standstill and is still going at the end. A function of time, worked out once: the same
/// moment always looks the same. The engine goes up through a four-speed as the speed does.
/// </summary>
public sealed class PathDrive
{
    // About 30 mph on the road; a bend is taken at no more than this sideways pull
    public const float CruiseSpeed = 13.5f;
    public const float CornerAccel = 1.5f;
    private const float Accel = 2.2f;
    private const float BrakeDecel = 3.2f;

    // The clutch goes in below this on the way to a stop
    private const float ClutchSpeed = 3f;

    // Samples along the path, metres apart
    private const float Step = 0.25f;

    private readonly bool _leaving;
    private readonly double _idle;
    private readonly float[] _x;
    private readonly float[] _z;
    private readonly float[] _heading;
    private readonly float[] _distance;
    private readonly float[] _speed;
    private readonly float[] _time;
    private readonly float[] _curvature;

    private PathDrive(IReadOnlyList<GroundPoint> points, bool leaving, double idleRpm)
    {
        _leaving = leaving;
        _idle = idleRpm is > 200 and < 3000 ? idleRpm : 800;

        var curve = Resample(Spline(points));
        var n = curve.Count;
        _x = curve.Select(p => p.X).ToArray();
        _z = curve.Select(p => p.Z).ToArray();
        _distance = new float[n];
        _heading = new float[n];
        for (var i = 1; i < n; i++) _distance[i] = _distance[i - 1] + MathF.Sqrt(Sq(_x[i] - _x[i - 1]) + Sq(_z[i] - _z[i - 1]));
        for (var i = 0; i < n; i++)
        {
            // A path that goes nowhere (its points all on one spot) is one sample, facing +z
            var (a, b) = n < 2 ? (0, 0) : i == n - 1 ? (n - 2, n - 1) : (i, i + 1);
            _heading[i] = MathF.Atan2(_x[b] - _x[a], _z[b] - _z[a]) * 180f / MathF.PI;
        }

        // The fastest each point can be taken: the bend there (its heading's turn over a few metres either side)
        var limit = new float[n];
        _curvature = new float[n];
        const int around = 14;
        for (var i = 0; i < n; i++)
        {
            var (a, b) = (Math.Max(0, i - around), Math.Min(n - 1, i + around));

            // A heading growing is a turn to the left: 0 faces +z, and a car's left is +x
            var turn = Turn(_heading[a], _heading[b]) * MathF.PI / 180f;
            var length = Math.Max(0.01f, _distance[b] - _distance[a]);
            _curvature[i] = turn / length;
            var curvature = MathF.Abs(_curvature[i]);
            limit[i] = curvature < 1e-4f ? CruiseSpeed : Math.Min(CruiseSpeed, MathF.Sqrt(CornerAccel / curvature));
        }

        // Speeding up where it can, braking in time for what comes: rolling in at the start of an arrival, from
        // standstill for a departure; to a stop at the end of an arrival
        _speed = new float[n];
        _speed[0] = leaving ? 0f : limit[0];
        for (var i = 1; i < n; i++)
            _speed[i] = Math.Min(limit[i], MathF.Sqrt(Sq(_speed[i - 1]) + 2 * Accel * (_distance[i] - _distance[i - 1])));
        if (!leaving) _speed[n - 1] = 0f;
        for (var i = n - 2; i >= 0; i--)
            _speed[i] = Math.Min(_speed[i], MathF.Sqrt(Sq(_speed[i + 1]) + 2 * BrakeDecel * (_distance[i + 1] - _distance[i])));

        _time = new float[n];
        for (var i = 1; i < n; i++)
        {
            var ds = _distance[i] - _distance[i - 1];
            var mean = (_speed[i] + _speed[i - 1]) / 2;
            _time[i] = _time[i - 1] + (mean > 0.05f ? ds / mean : MathF.Sqrt(2 * ds / Accel));
        }
    }

    /// <summary>In off the road: rolling at the start, stopped at the last point</summary>
    public static PathDrive Arrive(IReadOnlyList<GroundPoint> points, double idleRpm) => new(points, false, idleRpm);

    /// <summary>Away from a standstill at the first point, still going at the last</summary>
    public static PathDrive Leave(IReadOnlyList<GroundPoint> points, double idleRpm) => new(points, true, idleRpm);

    public bool IsLeaving => _leaving;

    public float Duration => _time[^1];

    public PathState At(float t)
    {
        var n = _time.Length;
        if (t >= _time[n - 1]) return State(n - 1, n - 1, 0f, true);
        if (t <= 0f) return State(0, 0, 0f, false);

        var i = Array.BinarySearch(_time, t);
        if (i >= 0) return State(i, i, 0f, false);
        i = ~i;
        var f = (t - _time[i - 1]) / Math.Max(1e-6f, _time[i] - _time[i - 1]);
        return State(i - 1, i, f, false);
    }

    private PathState State(int a, int b, float f, bool done)
    {
        var x = Lerp(_x[a], _x[b], f);
        var z = Lerp(_z[a], _z[b], f);
        var heading = _heading[a] + Turn(_heading[a], _heading[b]) * f;
        var distance = Lerp(_distance[a], _distance[b], f);
        var speed = Lerp(_speed[a], _speed[b], f);

        // The pull along the path between the samples round the point
        var (p, q) = (Math.Max(0, a - 1), Math.Min(_time.Length - 1, b + 1));
        var accel = _time[q] > _time[p] ? (_speed[q] - _speed[p]) / (_time[q] - _time[p]) : 0f;
        if (done) accel = 0f;

        var braking = accel < -0.6f;
        double? rpm = done && !_leaving || !_leaving && braking && speed < ClutchSpeed ? null : EngineRpm(speed);
        var throttle = accel > 0.2f ? Math.Min(1.0, 0.3 + accel / 3) : braking ? 0 : 0.18;
        return new PathState(x, z, heading, distance, speed, accel, rpm, throttle, braking && !done, done, Lerp(_curvature[a], _curvature[b], f));
    }

    /// <summary>What the engine turns at: the gear the speed wants (a slipping clutch off the line), never under the idle</summary>
    private double EngineRpm(float speed)
    {
        var gear = speed < 4.5f ? 0 : speed < 9f ? 1 : speed < 15f ? 2 : 3;
        var rpm = speed / (2 * Math.PI * RivalDrive.TyreRadius) * 60 * RivalDrive.Ratios[gear];
        return Math.Max(speed < ClutchSpeed && _leaving ? _idle * 1.8 : _idle, rpm);
    }

    private static float Sq(float v) => v * v;

    private static float Lerp(float a, float b, float f) => a + (b - a) * f;

    /// <summary>The turn from one heading to another, degrees, the short way round</summary>
    private static float Turn(float from, float to)
    {
        var d = (to - from) % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return d;
    }

    /// <summary>A centripetal Catmull-Rom curve through the points, finely</summary>
    private static List<GroundPoint> Spline(IReadOnlyList<GroundPoint> points)
    {
        var p = points.ToList();
        var result = new List<GroundPoint> { p[0] };
        for (var i = 0; i < p.Count - 1; i++)
        {
            var p0 = i == 0 ? Mirror(p[1], p[0]) : p[i - 1];
            var (p1, p2) = (p[i], p[i + 1]);
            var p3 = i + 2 < p.Count ? p[i + 2] : Mirror(p[i], p[i + 1]);
            var steps = Math.Max(4, (int)(Distance(p1, p2) / 0.1f));
            for (var s = 1; s <= steps; s++) result.Add(Centripetal(p0, p1, p2, p3, (float)s / steps));
        }

        return result;
    }

    private static GroundPoint Mirror(GroundPoint about, GroundPoint point) => new(2 * point.X - about.X, 2 * point.Z - about.Z);

    private static float Distance(GroundPoint a, GroundPoint b) => MathF.Sqrt(Sq(b.X - a.X) + Sq(b.Z - a.Z));

    /// <summary>
    /// The point <paramref name="f"/> of the way from p1 to p2, the knots spaced by the square root of the distances
    /// (Barry and Goldman's pyramid)
    /// </summary>
    private static GroundPoint Centripetal(GroundPoint p0, GroundPoint p1, GroundPoint p2, GroundPoint p3, float f)
    {
        static float Knot(float t, GroundPoint a, GroundPoint b) => t + Math.Max(1e-3f, MathF.Sqrt(Distance(a, b)));
        static GroundPoint Mix(GroundPoint a, GroundPoint b, float ta, float tb, float t) =>
            new(a.X + (b.X - a.X) * (t - ta) / (tb - ta), a.Z + (b.Z - a.Z) * (t - ta) / (tb - ta));

        const float t0 = 0f;
        var t1 = Knot(t0, p0, p1);
        var t2 = Knot(t1, p1, p2);
        var t3 = Knot(t2, p2, p3);
        var t = t1 + (t2 - t1) * f;

        var a1 = Mix(p0, p1, t0, t1, t);
        var a2 = Mix(p1, p2, t1, t2, t);
        var a3 = Mix(p2, p3, t2, t3, t);
        var b1 = Mix(a1, a2, t0, t2, t);
        var b2 = Mix(a2, a3, t1, t3, t);
        return Mix(b1, b2, t1, t2, t);
    }

    /// <summary>The curve at even steps along it</summary>
    private static List<GroundPoint> Resample(List<GroundPoint> curve)
    {
        var result = new List<GroundPoint> { curve[0] };
        var carried = 0f;
        for (var i = 1; i < curve.Count; i++)
        {
            var (a, b) = (curve[i - 1], curve[i]);
            var length = MathF.Sqrt(Sq(b.X - a.X) + Sq(b.Z - a.Z));
            var at = Step - carried;
            while (at <= length)
            {
                var f = at / length;
                result.Add(new GroundPoint(a.X + (b.X - a.X) * f, a.Z + (b.Z - a.Z) * f));
                at += Step;
            }

            carried = length - (at - Step);
        }

        // The last point exactly, never a sliver of a step before it (a step that short has no heading)
        var end = curve[^1];
        if (MathF.Sqrt(Sq(end.X - result[^1].X) + Sq(end.Z - result[^1].Z)) < Step / 2 && result.Count > 1) result[^1] = end;
        else if (result[^1] != end) result.Add(end);
        return result;
    }
}
