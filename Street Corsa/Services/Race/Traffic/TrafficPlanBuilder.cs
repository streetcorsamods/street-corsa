using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StreetCorsa.Services.Race.Traffic
{
    /// <summary>
    /// A traffic plan (CSP's <c>traffic.json</c>) made from a road's AI line, for a track that ships none: ported from
    /// Race Explorer's <c>traffic_plan_from_ai.py</c>, which made the plans driven since 2026-09.
    /// <para>
    /// Two-way by default: the road's centre is the line moved by half the difference of its sides, and a lane runs a
    /// quarter of the road's width either side of it, the one with the racing direction on the driving side (left in
    /// the UK and Japan, right on the continent). The frame is <c>right = forward × normal</c>, the handedness that
    /// script checked against every installed track that ships both its edges and real widths.
    /// </para>
    /// <para>
    /// A two-way plan is only honest on real widths. On flat placeholder widths the offset lane follows the racing line
    /// across the road and leaves the tarmac at every apex, and on a road too narrow for two (lanes under a car's width
    /// apart) the lanes overlap; both get one lane on the line instead. So does a two-way plan that fails its own checks
    /// (<see cref="Check"/>), which the script left to a human to tune and the game cannot.
    /// </para>
    /// </summary>
    /// <summary>How many lanes a plan the game builds gets: the player's choice in Settings (Traffic lanes)</summary>
    public enum PlanLanes
    {
        /// <summary>Two-way where the road allows it (real widths, wide enough, its checks passed), else one lane on the line</summary>
        Auto,

        /// <summary>Two-way, a lane each way on its side of the road, whatever the checks say</summary>
        TwoWay,

        /// <summary>One lane on the racing line, every car going the racing direction</summary>
        Single
    }

    public static class TrafficPlanBuilder
    {
        /// <summary>Metres between a lane's points (the shipped plans run 3-76)</summary>
        public const double Spacing = 10;

        /// <summary>Below this a recorded side is the recorder failing to find an edge, not a measurement</summary>
        public const double MinSide = 0.30;

        /// <summary>
        /// Above this a recorded side is a car park or a beach the recorder ran into (Pacific Coast 0-81 m, Mount Akina
        /// 0-80 m, Ain Diab up to 52 m), not road; the script took it as an option, the game always caps
        /// </summary>
        public const double MaxSide = 10;

        /// <summary>A loop's first and last points are the same place when closer than this</summary>
        public const double LoopGap = 5;

        /// <summary>Room a traffic car needs between its lane's centre and a kerb: half a car and a little</summary>
        public const double Clearance = 1;

        /// <summary>The least distance between two lanes: a car's width. A road whose lanes would typically be closer is too narrow for two</summary>
        public const double LeastSeparation = 2;

        /// <summary>
        /// The floor on the distance between the two lanes' centres where the road pinches (Shutoko's C1 is two lanes
        /// wide for 95% of the lap and 2.9-4.0 m for the rest; Transfăgărășan's hairpins): held a car's width apart and a
        /// little, they give up kerb clearance there instead, which the checks then weigh
        /// </summary>
        public const double SeparationFloor = 2.2;

        // A two-way plan may come within a kerb, or cross a road edge, at this share of its points and still pass: a
        // point or two in a hairpin, which the script's human waved through (--force) and the game cannot ask about
        private const double TightShare = 0.02;
        private const double OffEdgeShare = 0.01;

        // The racing line itself outside the road's shipped edges at more than this share of its points: the edges are
        // not this layout's, or not in the convention read (Mecheria's), and are left out
        private const double EdgesTrustShare = 0.10;

        // A two-way plan may leave its corridor at this share of its points, by this much at worst, and still pass: the
        // nearest racing-line point a lane point is measured against jumps at a hairpin, which the script's human tuned
        // away and the game cannot
        private const double CorridorShare = 0.01;
        private const double CorridorWorst = 1.0;

        // Copied from the New Forest plan, which is hand-drawn and shipped by its author; CSP's own defaults too
        private static readonly (string Name, int SpeedLimit, int Priority)[] LaneRoles =
            [("Parking", 40, -8), ("Secondary", 60, -4), ("Main", 80, 0), ("Highway", 90, 4)];

        /// <summary>A lane of a plan: a name for the log, its points, and whether it runs with the racing direction</summary>
        public sealed record Lane(string Name, IReadOnlyList<Vector> Points, bool WithFlow);

        /// <summary>A plan made: its lanes, whether they loop, how it was made and what its checks found</summary>
        public sealed record Plan(IReadOnlyList<Lane> Lanes, bool Loop, bool TwoWay, string Why, IReadOnlyList<string> Problems);

        /// <summary>A point or a direction in the track's world space</summary>
        public readonly record struct Vector(double X, double Y, double Z)
        {
            public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vector operator *(Vector a, double k) => new(a.X * k, a.Y * k, a.Z * k);
            public double Dot(Vector b) => X * b.X + Y * b.Y + Z * b.Z;
            public double Length() => Math.Sqrt(Dot(this));
            public Vector Unit() => Length() is var l && l > 1e-9 ? this * (1 / l) : this * 1e9;
            public double PlanDistanceSquared(Vector b) => (X - b.X) * (X - b.X) + (Z - b.Z) * (Z - b.Z);
            public double DistanceSquared(Vector b) => (X - b.X) * (X - b.X) + (Y - b.Y) * (Y - b.Y) + (Z - b.Z) * (Z - b.Z);
            public double Distance(Vector b) => (this - b).Length();
        }

        /// <summary>One point of the line in the plan's frame: where it is, its right, and the road's reach either side</summary>
        private readonly record struct Frame(Vector P, Vector Right, double Left, double RightSide);

        /// <summary>The road's shipped edges, in plan view, when the track has them: the only ground truth there is</summary>
        public sealed class Edges
        {
            internal EdgeLine Left { get; }
            internal EdgeLine Right { get; }

            private Edges(EdgeLine left, EdgeLine right)
            {
                Left = left;
                Right = right;
            }

            /// <summary>
            /// A track's <c>data\side_l.csv</c> and <c>side_r.csv</c> (x, y, z, ... a line, Z negated as AC stores it);
            /// null when it has not both or they do not read
            /// </summary>
            public static Edges? Of(string dataFolder)
            {
                var left = Path.Combine(dataFolder, "side_l.csv");
                var right = Path.Combine(dataFolder, "side_r.csv");
                if (!File.Exists(left) || !File.Exists(right)) return null;
                try
                {
                    var l = ReadSide(left);
                    var r = ReadSide(right);
                    return l.Count < 2 || r.Count < 2 ? null : new Edges(new EdgeLine(l), new EdgeLine(r));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
                {
                    return null;
                }
            }

            /// <summary>Edges as given, in plan view (x, z), for tests</summary>
            public static Edges From(IReadOnlyList<(double X, double Z)> left, IReadOnlyList<(double X, double Z)> right) =>
                new(new EdgeLine(left.Select(p => (p.X, double.NaN, p.Z)).ToList()), new EdgeLine(right.Select(p => (p.X, double.NaN, p.Z)).ToList()));

            private static List<(double X, double Y, double Z)> ReadSide(string file)
            {
                var points = new List<(double X, double Y, double Z)>();
                foreach (var line in File.ReadLines(file))
                {
                    var parts = line.Split(',');
                    if (parts.Length < 3) continue;
                    if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                        || !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
                        continue;
                    double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y);
                    // AC stores these files with Z negated and negates it again on reading
                    points.Add((x, y, -z));
                }
                return points;
            }
        }

        /// <summary>
        /// A road edge, a polyline in plan view, with its segments in a grid so the distance from a point is found among
        /// its neighbours; exact up to <see cref="Cell"/>, and "at least that" beyond. Closed when it ends where it
        /// began (a loop's edge); a road from one place to another (a hillclimb) has an open one, with no piece from
        /// its finish back across the map to its start.
        /// </summary>
        internal sealed class EdgeLine
        {
            public const double Cell = 25;

            /// <summary>A piece of edge this far above or below a point is another level of the road (a bridge), not this one's</summary>
            public const double OtherLevel = 4;

            private readonly (double X, double Y, double Z)[] _points;
            private readonly Dictionary<(int, int), List<int>> _grid = new();

            /// <summary>Whether the last point joins the first</summary>
            public bool Closed { get; }

            public EdgeLine(IReadOnlyList<(double X, double Y, double Z)> points)
            {
                _points = points.ToArray();
                Closed = Closes(_points);
                for (var i = 0; i < (Closed ? _points.Length : _points.Length - 1); i++)
                {
                    var a = _points[i];
                    var b = _points[(i + 1) % _points.Length];
                    for (var cx = CellOf(Math.Min(a.X, b.X)); cx <= CellOf(Math.Max(a.X, b.X)); cx++)
                    for (var cz = CellOf(Math.Min(a.Z, b.Z)); cz <= CellOf(Math.Max(a.Z, b.Z)); cz++)
                    {
                        if (!_grid.TryGetValue((cx, cz), out var list)) _grid[(cx, cz)] = list = [];
                        list.Add(i);
                    }
                }
            }

            private static int CellOf(double v) => (int)Math.Floor(v / Cell);

            // The gap from the last point to the first is one more piece of the edge when it is no longer than its
            // pieces are (three of the typical, or a loop's gap): kilometres of it are a road that does not come back
            private static bool Closes((double X, double Y, double Z)[] points)
            {
                if (points.Length < 3) return false;
                var pieces = new double[points.Length - 1];
                for (var i = 0; i < pieces.Length; i++) pieces[i] = Plan(points[i], points[i + 1]);
                Array.Sort(pieces);
                return Plan(points[^1], points[0]) <= Math.Max(LoopGap, 3 * pieces[pieces.Length / 2]);

                static double Plan((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
                    Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
            }

            /// <summary>
            /// The distance from a point to the edge, in plan view, over the pieces of edge at its own level (the height
            /// unknown, NaN: every piece); <see cref="Cell"/> when nothing is that near
            /// </summary>
            public double Distance(double x, double z, double y = double.NaN)
            {
                var best = Cell * Cell;
                var (px, pz) = (CellOf(x), CellOf(z));
                for (var cx = px - 1; cx <= px + 1; cx++)
                for (var cz = pz - 1; cz <= pz + 1; cz++)
                {
                    if (!_grid.TryGetValue((cx, cz), out var list)) continue;
                    foreach (var i in list)
                    {
                        var a = _points[i];
                        var b = _points[(i + 1) % _points.Length];
                        if (double.IsFinite(y) && double.IsFinite(a.Y) && Math.Abs((a.Y + b.Y) / 2 - y) > OtherLevel) continue;
                        var (dx, dz) = (b.X - a.X, b.Z - a.Z);
                        var lengthSquared = Math.Max(dx * dx + dz * dz, 1e-12);
                        var t = Math.Clamp(((x - a.X) * dx + (z - a.Z) * dz) / lengthSquared, 0, 1);
                        var (qx, qz) = (a.X + t * dx - x, a.Z + t * dz - z);
                        best = Math.Min(best, qx * qx + qz * qz);
                    }
                }
                return Math.Sqrt(best);
            }
        }

        /// <summary>Whether a line's widths are a flat placeholder (two distinct widths at most), which no two-way plan can be built on</summary>
        public static bool FlatWidths(AiLine line) =>
            line.Points.Select(p => Math.Round(p.SideLeft + p.SideRight, 3)).Distinct().Take(3).Count() <= 2;

        /// <summary>
        /// The plan for a road. <paramref name="lanes"/> is the player's choice (Settings, Traffic lanes): by default
        /// two-way when its widths are real and the plan passes its checks, else one lane on the racing line; two-way
        /// whatever the checks say (what they found is told in <see cref="Plan.Problems"/>); or one lane on the line.
        /// <paramref name="keepLeft"/>: traffic keeps to the left (the UK, Japan), so the lane with the racing direction
        /// is the left one.
        /// </summary>
        public static Plan Build(AiLine line, bool keepLeft, Edges? edges = null, PlanLanes lanes = PlanLanes.Auto)
        {
            var (frames, loop) = Frames(line);
            if (lanes == PlanLanes.Single) return Single(frames, loop, "one lane on the racing line: the player's choice");

            var edgesNote = "";
            if (edges != null && !Trusted(edges, frames))
            {
                edgesNote = " (the road's shipped edges left out: the racing line is outside them)";
                edges = null;
            }

            if (lanes == PlanLanes.TwoWay)
            {
                // The player's say: built and kept whatever its checks find, which the log is told
                var forced = TwoWay(frames, loop, keepLeft, edges);
                var found = Check(forced, loop, frames, edges);
                if (FlatWidths(line)) found.Insert(0, "the AI line's widths are a flat placeholder");
                return new Plan(forced, loop, true, "two-way, the player's choice" + edgesNote + (found.Count == 0 ? "" : " (its checks: " + string.Join("; ", found.Take(3)) + ")"), found);
            }

            if (FlatWidths(line))
                return Single(frames, loop, "one lane on the racing line: the AI line's widths are a flat placeholder");

            // Too narrow for two: the lanes, a quarter of the road either side of its centre, typically under a car's width apart
            var spacing = frames.Select(f => (f.Left + f.RightSide) / 2).OrderBy(v => v).ElementAt(frames.Count / 2);
            if (spacing < LeastSeparation)
                return Single(frames, loop, $"one lane on the racing line: the road is too narrow for two ({spacing * 2:0.0} m typically)");

            var twoWay = TwoWay(frames, loop, keepLeft, edges);
            var problems = Check(twoWay, loop, frames, edges);
            if (problems.Count == 0)
                return new Plan(twoWay, loop, true, "two-way, from the AI line's widths" + (edges == null ? "" : " and the road's edges") + edgesNote, []);

            var single = Single(frames, loop, "one lane on the racing line: two-way failed its checks (" + string.Join("; ", problems.Take(3)) + ")");
            return single with { Problems = problems };
        }

        /// <summary>Whether the road's edges are this line's: the line itself within them at all but a few of its points</summary>
        private static bool Trusted(Edges edges, List<Frame> frames)
        {
            var outside = frames.Count(f => edges.Left.Distance(f.P.X, f.P.Z, f.P.Y) + edges.Right.Distance(f.P.X, f.P.Z, f.P.Y) - (f.Left + f.RightSide) > 1.0);
            return outside <= frames.Count * EdgesTrustShare;
        }

        private static Plan Single(List<Frame> frames, bool loop, string why) =>
            new([new Lane("racing line", Resample(frames.Select(f => f.P).ToList(), Spacing, loop), true)], loop, false, why, []);

        /// <summary>The line in the plan's frame: a right vector at each point, the sides held to <see cref="MinSide"/>-<see cref="MaxSide"/></summary>
        private static (List<Frame> Frames, bool Loop) Frames(AiLine line)
        {
            var points = line.Points;
            var n = points.Count;
            var positions = points.Select(p => new Vector(p.X, p.Y, p.Z)).ToList();
            var loop = positions[0].Distance(positions[^1]) < LoopGap;
            var frames = new List<Frame>(n);
            for (var i = 0; i < n; i++)
            {
                // Central difference where there is one; the ends wrap on a loop and clamp otherwise
                var a = loop ? positions[(i - 1 + n) % n] : positions[Math.Max(i - 1, 0)];
                var b = loop ? positions[(i + 1) % n] : positions[Math.Min(i + 1, n - 1)];
                var forward = (b - a).Unit();
                var normal = new Vector(points[i].NormalX, points[i].NormalY, points[i].NormalZ);
                normal = normal.Dot(normal) > 1e-9 ? normal.Unit() : new Vector(0, 1, 0);
                // right = forward × normal: (-fz, 0, fx) on level ground
                var right = new Vector(forward.Y * normal.Z - forward.Z * normal.Y, forward.Z * normal.X - forward.X * normal.Z, forward.X * normal.Y - forward.Y * normal.X).Unit();
                frames.Add(new Frame(positions[i], right, Side(points[i].SideLeft), Side(points[i].SideRight)));
            }
            return (frames, loop);

            static double Side(double v) => double.IsFinite(v) ? Math.Clamp(v, MinSide, MaxSide) : MinSide;
        }

        /// <summary>A lane each way, a quarter of the road's width either side of its centre, pulled off the kerbs where the road's edges are known</summary>
        private static List<Lane> TwoWay(List<Frame> frames, bool loop, bool keepLeft, Edges? edges)
        {
            var left = Offset(frames, -1);
            var right = Offset(frames, +1);
            var (withFlow, against) = keepLeft ? (left, right) : (right, left);
            against.Reverse();
            var lanes = new List<Lane>
            {
                new("with racing direction", Resample(withFlow, Spacing, loop), true),
                new("against racing direction", Resample(against, Spacing, loop), false)
            };
            if (edges == null) return lanes;
            var index = new PointIndex(frames.Select(f => f.P).ToList());
            return lanes.Select(l => l with { Points = PullInside(l.Points, frames, index, edges) }).ToList();
        }

        /// <summary>
        /// The lane of one half of the road (<paramref name="side"/> -1 left, +1 right), half that half's width from the
        /// centre, and never nearer it than half the <see cref="SeparationFloor"/>
        /// </summary>
        private static List<Vector> Offset(List<Frame> frames, int side) =>
            frames.Select(f => f.P + f.Right * ((f.RightSide - f.Left) / 2 + side * 0.5 * LaneSpacing(f))).ToList();

        /// <summary>The distance between the two lanes' centres at a point: half the road's width, held to the floor</summary>
        private static double LaneSpacing(Frame f) => Math.Max((f.Left + f.RightSide) / 2, SeparationFloor);

        /// <summary>A polyline walked at a fixed distance between points: the shape kept, the density dropped</summary>
        public static List<Vector> Resample(IReadOnlyList<Vector> points, double spacing, bool loop)
        {
            var n = points.Count;
            var count = loop ? n : n - 1;
            var segments = new double[count];
            for (var i = 0; i < count; i++) segments[i] = points[i].Distance(points[(i + 1) % n]);
            var total = segments.Sum();
            if (total < spacing) return points.ToList();

            var output = new List<Vector>();
            var index = 0;
            var along = 0.0;
            var walked = 0.0;
            while (walked < total - (loop ? 0 : spacing * 0.5))
            {
                while (index < count && along >= segments[index])
                {
                    along -= segments[index];
                    index++;
                }
                if (index >= count) break;
                var a = points[index];
                var b = points[(index + 1) % n];
                var f = segments[index] > 0 ? along / segments[index] : 0;
                output.Add(a + (b - a) * f);
                along += spacing;
                walked += spacing;
            }

            if (!loop) output.Add(points[^1]);
            // The walk lands its last sample just short of where it started: a metre or two on a 10 m lane is noise
            else if (output.Count > 2 && output[^1].Distance(output[0]) < spacing * 0.5) output.RemoveAt(output.Count - 1);
            return output;
        }

        /// <summary>
        /// Lane points too near a kerb nudged back towards the road's centre: the lanes are placed by the line's own
        /// perpendicular sides, and in a hairpin the true inner edge cuts inside that perpendicular. Each pass moves by
        /// the shortfall it can measure, and moving one point changes which edge is nearest (Bannochbrae needed 32).
        /// </summary>
        private static List<Vector> PullInside(IReadOnlyList<Vector> lane, List<Frame> frames, PointIndex index, Edges edges)
        {
            var points = lane.ToList();
            var nearest = points.Select(p => index.Nearest(p).Index).ToArray();
            for (var pass = 0; pass < 40; pass++)
            {
                var moved = false;
                for (var i = 0; i < points.Count; i++)
                {
                    var dl = edges.Left.Distance(points[i].X, points[i].Z, points[i].Y);
                    var dr = edges.Right.Distance(points[i].X, points[i].Z, points[i].Y);
                    if (Math.Min(dl, dr) >= Clearance - 1e-3) continue;
                    var f = frames[nearest[i]];
                    // Away from whichever edge is too near, never past the road's centre, and never nearer it than the
                    // separation floor holds the lane: two lanes the floor keeps apart stay apart, the kerb gives way
                    var room = Math.Min(Clearance - Math.Min(dl, dr), (f.Left + f.RightSide) / 2);
                    var lateral = (points[i] - f.P).Dot(f.Right) - (f.RightSide - f.Left) / 2;
                    room = Math.Min(room, Math.Max(Math.Abs(lateral) - 0.5 * SeparationFloor, 0));
                    if (room <= 1e-3) continue;
                    points[i] += f.Right * (dl < dr ? room : -room);
                    moved = true;
                }
                if (!moved) break;
            }
            return points;
        }

        /// <summary>
        /// Points in a grid in plan view, so the nearest of them to a point is found among its neighbours and not by
        /// going through them all: a 60 km road is 30,000 line points and 6,000 lane points a lane, each asked of the
        /// others. The nearest in 3D (where the road passes over itself, Bannochbrae's bridge 23 m up, the nearest point
        /// in plan view is on the other level), the first of the list among equals: what going through them all gave.
        /// </summary>
        internal sealed class PointIndex
        {
            private const double Cell = 50;

            private readonly IReadOnlyList<Vector> _points;
            private readonly Dictionary<(int, int), List<int>> _grid = new();
            private readonly int _minX, _maxX, _minZ, _maxZ;

            public PointIndex(IReadOnlyList<Vector> points)
            {
                _points = points;
                (_minX, _maxX, _minZ, _maxZ) = (int.MaxValue, int.MinValue, int.MaxValue, int.MinValue);
                for (var i = 0; i < points.Count; i++)
                {
                    // A point that is no place (a NaN in the line) is in no cell: it is nearest to nothing
                    if (!double.IsFinite(points[i].X) || !double.IsFinite(points[i].Z)) continue;
                    var cell = (CellOf(points[i].X), CellOf(points[i].Z));
                    if (!_grid.TryGetValue(cell, out var list)) _grid[cell] = list = [];
                    list.Add(i);
                    (_minX, _maxX) = (Math.Min(_minX, cell.Item1), Math.Max(_maxX, cell.Item1));
                    (_minZ, _maxZ) = (Math.Min(_minZ, cell.Item2), Math.Max(_maxZ, cell.Item2));
                }
            }

            private static int CellOf(double v) => (int)Math.Clamp(Math.Floor(v / Cell), int.MinValue / 2, int.MaxValue / 2);

            /// <summary>The nearest point's place in the list and its distance, squared; the first and no distance when there is none to find</summary>
            public (int Index, double DistanceSquared) Nearest(Vector point)
            {
                if (_grid.Count == 0 || !double.IsFinite(point.X) || !double.IsFinite(point.Z)) return (0, double.MaxValue);

                var (px, pz) = (CellOf(point.X), CellOf(point.Z));
                // Rings of cells outwards: every point of a ring not yet looked at is at least (ring - 1) cells away
                var last = Math.Max(Math.Max(Math.Abs((long)px - _minX), Math.Abs((long)px - _maxX)), Math.Max(Math.Abs((long)pz - _minZ), Math.Abs((long)pz - _maxZ)));
                var best = -1;
                var bestDistance = double.MaxValue;
                for (long ring = 0; ring <= last; ring++)
                {
                    if (best >= 0 && ring > 0 && bestDistance < (ring - 1) * Cell * ((ring - 1) * Cell)) break;
                    // A point kilometres from them all (a stray figure in a line): through the list, not ring after empty ring
                    if (best < 0 && ring > FarRings) return Through(point);
                    for (var cx = px - ring; cx <= px + ring; cx++)
                    for (var cz = pz - ring; cz <= pz + ring; cz += cx == px - ring || cx == px + ring ? 1 : Math.Max(2 * ring, 1))
                    {
                        if (!_grid.TryGetValue(((int)cx, (int)cz), out var list)) continue;
                        foreach (var i in list)
                        {
                            var d = point.DistanceSquared(_points[i]);
                            if (d < bestDistance || d == bestDistance && i < best)
                            {
                                bestDistance = d;
                                best = i;
                            }
                        }
                    }
                }
                return best < 0 ? (0, double.MaxValue) : (best, bestDistance);
            }

            private const int FarRings = 64;

            private (int Index, double DistanceSquared) Through(Vector point)
            {
                var best = 0;
                var bestDistance = double.MaxValue;
                for (var i = 0; i < _points.Count; i++)
                {
                    var d = point.DistanceSquared(_points[i]);
                    if (d < bestDistance) (best, bestDistance) = (i, d);
                }
                return (best, bestDistance);
            }
        }

        /// <summary>
        /// The script's offline checks of a plan against the line it came from: every point inside the road's corridor;
        /// each lane's direction against the racing line; lanes a car's width apart; point spacing as the shipped plans
        /// have it (3-76 m); a loop that closes; and, where the track ships its edges, no point outside them or within
        /// <see cref="Clearance"/> of one. What fails, worded for the log; empty when it passes.
        /// </summary>
        public static List<string> Check(IReadOnlyList<Lane> lanes, bool loop, AiLine line, Edges? edges = null) =>
            Check(lanes, loop, Frames(line).Frames, edges);

        private static List<string> Check(IReadOnlyList<Lane> lanes, bool loop, List<Frame> frames, Edges? edges)
        {
            var problems = new List<string>();
            var index = new PointIndex(frames.Select(f => f.P).ToList());

            foreach (var lane in lanes)
            {
                var nearest = lane.Points.Select(p => index.Nearest(p).Index).ToArray();

                // The corridor of the nearest racing-line point
                var outside = 0;
                var worst = 0.0;
                for (var i = 0; i < lane.Points.Count; i++)
                {
                    var f = frames[nearest[i]];
                    var lateral = (lane.Points[i] - f.P).Dot(f.Right);
                    var over = Math.Max(Math.Max(lateral - f.RightSide, -f.Left - lateral), 0);
                    if (over > 1e-6)
                    {
                        outside++;
                        worst = Math.Max(worst, over);
                    }
                }
                if (outside > lane.Points.Count * CorridorShare || worst > CorridorWorst)
                    problems.Add($"{lane.Name} leaves the road at {outside} point(s), by {worst:0.00} m at worst");

                // The direction against the racing line
                var agree = 0;
                for (var i = 0; i < lane.Points.Count - 1; i++)
                {
                    var here = nearest[i];
                    var next = (here + 1) % frames.Count;
                    var (fx, fz) = (frames[next].P.X - frames[here].P.X, frames[next].P.Z - frames[here].P.Z);
                    var (lx, lz) = (lane.Points[i + 1].X - lane.Points[i].X, lane.Points[i + 1].Z - lane.Points[i].Z);
                    if (fx * lx + fz * lz > 0) agree++;
                }
                var share = 100.0 * agree / Math.Max(lane.Points.Count - 1, 1);
                var want = lane.WithFlow ? 100.0 : 0.0;
                if (Math.Abs(share - want) >= 2) problems.Add($"{lane.Name} runs {share:0.0}% with the racing line, not {want:0}%");

                // Point spacing
                var gaps = new List<double>();
                for (var i = 0; i < lane.Points.Count - 1; i++) gaps.Add(lane.Points[i].Distance(lane.Points[i + 1]));
                if (loop && lane.Points.Count > 1) gaps.Add(lane.Points[^1].Distance(lane.Points[0]));
                if (gaps.Count > 0 && (gaps.Min() < 3 || gaps.Max() > 76))
                    problems.Add($"{lane.Name} has points {gaps.Min():0.00}-{gaps.Max():0.00} m apart, outside 3-76 m");

                // A loop that closes
                if (loop && lane.Points.Count > 1 && lane.Points[^1].Distance(lane.Points[0]) > Spacing * 1.6)
                    problems.Add($"{lane.Name} does not close ({lane.Points[^1].Distance(lane.Points[0]):0.00} m)");

                if (edges != null)
                {
                    var off = 0;
                    var tight = 0;
                    for (var i = 0; i < lane.Points.Count; i++)
                    {
                        var p = lane.Points[i];
                        var dl = edges.Left.Distance(p.X, p.Z, p.Y);
                        var dr = edges.Right.Distance(p.X, p.Z, p.Y);
                        var f = frames[nearest[i]];
                        // Outside the edges, the two distances add up to more than the road is wide there: its width
                        // taken from the edges at the racing line (the script took the line's recorded width, which
                        // fails every point on a road whose widths are recorded short: Misty Loch's 7%)
                        var width = edges.Left.Distance(f.P.X, f.P.Z, f.P.Y) + edges.Right.Distance(f.P.X, f.P.Z, f.P.Y);
                        if (dl + dr - width > 0.35) off++;
                        if (Math.Min(dl, dr) < Clearance - 0.01) tight++;
                    }
                    if (off > lane.Points.Count * OffEdgeShare) problems.Add($"{lane.Name} crosses a road edge at {off} point(s)");
                    if (tight > lane.Points.Count * TightShare) problems.Add($"{lane.Name} runs within {Clearance:0.00} m of a kerb at {tight} point(s)");
                }
            }

            // Every pair of lanes a car's width apart
            for (var i = 0; i < lanes.Count; i++)
            for (var j = i + 1; j < lanes.Count; j++)
            {
                if (lanes[i].Points.Count == 0 || lanes[j].Points.Count == 0) continue;
                var b = new PointIndex(lanes[j].Points);
                var least = lanes[i].Points.Min(p => b.Nearest(p).DistanceSquared);
                if (Math.Sqrt(least) < LeastSeparation)
                    problems.Add($"{lanes[i].Name} and {lanes[j].Name} come within {Math.Sqrt(least):0.00} m, under a car's width");
            }
            return problems;
        }

        /// <summary>A plan as CSP's traffic tool reads it: the lanes, no areas or intersections, the lane roles</summary>
        public static string ToJson(Plan plan)
        {
            var lanes = new JsonArray();
            for (var i = 0; i < plan.Lanes.Count; i++)
            {
                var points = new JsonArray();
                foreach (var p in plan.Lanes[i].Points) points.Add(new JsonArray(Math.Round(p.X, 5), Math.Round(p.Y, 5), Math.Round(p.Z, 5)));
                lanes.Add(new JsonObject
                {
                    ["name"] = $"Lane #{i + 1}",
                    ["id"] = i + 1,
                    ["loop"] = plan.Loop,
                    ["points"] = points,
                    ["priorityOffset"] = 0,
                    // The planner writes empty params as [], not {}; both are taken. The first lane may change lanes to get out of a jam
                    ["params"] = i == 0 ? new JsonObject { ["allowLaneChanges"] = true } : new JsonArray()
                });
            }

            var roles = new JsonArray();
            foreach (var (name, limit, priority) in LaneRoles) roles.Add(new JsonObject { ["name"] = name, ["speedLimit"] = limit, ["priority"] = priority });

            var root = new JsonObject
            {
                ["areas"] = new JsonArray(),
                ["lanes"] = lanes,
                ["intersections"] = new JsonArray(),
                ["rules"] = new JsonObject { ["laneRoles"] = roles }
            };
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
