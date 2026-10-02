using StreetCorsa.Services.Station;

namespace StreetCorsa.Services.Scripting
{
    /// <summary>Where a car stands on the ground of a scene: metres from its middle, and the way it faces</summary>
    /// <param name="Heading">Radians about the vertical, 0 facing +z, growing towards +x (a turn to the left)</param>
    public readonly record struct GroundPose(float X, float Z, float Heading)
    {
        public GroundPoint Point => new(X, Z);

        /// <summary>A point <paramref name="ahead"/> metres in front of it and <paramref name="left"/> to its left</summary>
        public GroundPoint Offset(float ahead, float left) =>
            new(X + MathF.Sin(Heading) * ahead + MathF.Cos(Heading) * left, Z + MathF.Cos(Heading) * ahead - MathF.Sin(Heading) * left);

        /// <summary>The turn from one heading to another, radians, the short way round (within half a turn either way)</summary>
        public static float Turn(float from, float to) => (float)Math.IEEERemainder(to - from, 2 * Math.PI);

        /// <summary>The way from one point to another, as a heading</summary>
        public static float HeadingTo(GroundPoint from, GroundPoint to) => MathF.Atan2(to.X - from.X, to.Z - from.Z);
    }

    /// <summary>
    /// A way across the ground through a scene's points: a smooth curve through them (a centripetal Catmull-Rom, which
    /// never overshoots into a loop or a kink where a short stretch meets a long one, as the uniform one did, and the car
    /// spun on the spot there), sampled every <see cref="Step"/> metres with the way it faces and how sharply it bends.
    /// </summary>
    public sealed class DrivePath
    {
        /// <summary>Samples along the path, metres apart</summary>
        public const float Step = 0.25f;

        // How far either side of a sample its bend is measured over, in samples
        private const int BendAround = 14;

        private readonly float[] _x;
        private readonly float[] _z;
        private readonly float[] _heading;
        private readonly float[] _distance;
        private readonly float[] _curvature;

        private DrivePath(List<GroundPoint> curve)
        {
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
                _heading[i] = MathF.Atan2(_x[b] - _x[a], _z[b] - _z[a]);
            }

            // How sharply it bends: the heading's turn over a few metres either side
            _curvature = new float[n];
            for (var i = 0; i < n; i++)
            {
                var (a, b) = (Math.Max(0, i - BendAround), Math.Min(n - 1, i + BendAround));
                var length = Math.Max(0.01f, _distance[b] - _distance[a]);
                _curvature[i] = GroundPose.Turn(_heading[a], _heading[b]) / length;
            }
        }

        /// <summary>The way through <paramref name="points"/>, in order; one point is a path that goes nowhere</summary>
        public static DrivePath Through(IReadOnlyList<GroundPoint> points)
        {
            if (points.Count == 0) throw new ArgumentException("A path needs a point at least", nameof(points));
            return new DrivePath(points.Count == 1 ? [points[0]] : Resample(Spline(points)));
        }

        public int Count => _x.Length;

        public float Length => _distance[^1];

        public GroundPoint Start => new(_x[0], _z[0]);

        public GroundPoint End => new(_x[^1], _z[^1]);

        internal float X(int i) => _x[i];

        internal float Z(int i) => _z[i];

        internal float Heading(int i) => _heading[i];

        internal float Distance(int i) => _distance[i];

        internal float Curvature(int i) => _curvature[i];

        /// <summary>Where the path is <paramref name="distance"/> metres along it (held at its ends), the way it faces and how it bends there</summary>
        public (float X, float Z, float Heading, float Curvature) At(float distance)
        {
            var n = _x.Length;
            if (n == 1 || distance <= 0f) return (_x[0], _z[0], _heading[0], _curvature[0]);
            if (distance >= _distance[^1]) return (_x[^1], _z[^1], _heading[^1], _curvature[^1]);

            var i = Array.BinarySearch(_distance, distance);
            if (i >= 0) return (_x[i], _z[i], _heading[i], _curvature[i]);
            i = ~i;
            var f = (distance - _distance[i - 1]) / Math.Max(1e-6f, _distance[i] - _distance[i - 1]);
            return (Lerp(_x[i - 1], _x[i], f), Lerp(_z[i - 1], _z[i], f),
                _heading[i - 1] + GroundPose.Turn(_heading[i - 1], _heading[i]) * f, Lerp(_curvature[i - 1], _curvature[i], f));
        }

        private static float Sq(float v) => v * v;

        private static float Lerp(float a, float b, float f) => a + (b - a) * f;

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
                var steps = Math.Max(4, (int)(Between(p1, p2) / 0.1f));
                for (var s = 1; s <= steps; s++) result.Add(Centripetal(p0, p1, p2, p3, (float)s / steps));
            }

            return result;
        }

        private static GroundPoint Mirror(GroundPoint about, GroundPoint point) => new(2 * point.X - about.X, 2 * point.Z - about.Z);

        private static float Between(GroundPoint a, GroundPoint b) => MathF.Sqrt(Sq(b.X - a.X) + Sq(b.Z - a.Z));

        /// <summary>
        /// The point <paramref name="f"/> of the way from p1 to p2, the knots spaced by the square root of the distances
        /// (Barry and Goldman's pyramid)
        /// </summary>
        private static GroundPoint Centripetal(GroundPoint p0, GroundPoint p1, GroundPoint p2, GroundPoint p3, float f)
        {
            static float Knot(float t, GroundPoint a, GroundPoint b) => t + Math.Max(1e-3f, MathF.Sqrt(Between(a, b)));
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
}
