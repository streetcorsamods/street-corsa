using System.IO;
using System.Text.Json;

namespace StreetCorsa.Services.Race.Traffic
{
    /// <summary>
    /// A road's traffic plan as far as the car count goes: CSP's <c>traffic.json</c>, a list of lanes, each a polyline of
    /// world-space points. Two lengths come out of it. <see cref="LaneKm"/> is every lane added up: what the plan covers
    /// (a two-way road twice). <see cref="ReachKm"/> is the lane length within <see cref="ReachMetres"/> of a typical point
    /// on the road, averaged along it: CSP's traffic spawns its cars 400-1000 m from the camera and takes them away past
    /// 1500 m, so every car it is given lives within that reach, however long the road. Sizing the count by the whole
    /// length put 262 cars in the kilometre round the player on a long loop; sized by the reach, a compact loop and a
    /// 33 km one get the same traffic where the player is.
    /// </summary>
    public sealed record TrafficRoad(int Lanes, double LaneKm, double ReachKm)
    {
        /// <summary>How far from the camera CSP's traffic keeps its cars, in metres (its <c>despawnDistance</c>)</summary>
        public const double ReachMetres = 1500;

        /// <summary>The file a plan is: CSP's traffic tool reads it from a track's (or a layout's) <c>data</c> folder</summary>
        public const string PlanFileName = "traffic.json";

        // About how many places along the road to stand and look round from
        private const int ReachSamples = 80;

        /// <summary>
        /// The track's own plan: the layout's <c>data\traffic.json</c>, else the track's; null when it has none
        /// </summary>
        /// <param name="trackFolder">The track's folder in AC's content\tracks</param>
        public static string? PlanOf(string trackFolder, string? layout)
        {
            if (string.IsNullOrEmpty(trackFolder)) return null;
            var candidates = string.IsNullOrEmpty(layout)
                ? [Path.Combine(trackFolder, "data", PlanFileName)]
                : new[] { Path.Combine(trackFolder, layout, "data", PlanFileName), Path.Combine(trackFolder, "data", PlanFileName) };
            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>The road a plan file describes; null when it cannot be read or holds nothing to drive on</summary>
        public static TrafficRoad? Read(string file)
        {
            try
            {
                return Measure(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The road a plan's JSON describes; null when it is not a plan or holds nothing to drive on</summary>
        public static TrafficRoad? Measure(string json)
        {
            JsonDocument document;
            try
            {
                // Hand-drawn and generated files that went through several tools: a BOM is likely, and comments may be
                document = JsonDocument.Parse(json.TrimStart('﻿'), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (JsonException)
            {
                return null;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("lanes", out var lanes) || lanes.ValueKind != JsonValueKind.Array)
                    return null;

                var count = 0;
                var metres = 0.0;
                var segments = new List<Segment>();
                foreach (var lane in lanes.EnumerateArray())
                {
                    if (lane.ValueKind != JsonValueKind.Object || !lane.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
                        continue;

                    var loop = lane.TryGetProperty("loop", out var loopValue) && loopValue.ValueKind == JsonValueKind.True;
                    var pieces = Segments(points.EnumerateArray().Select(Point).OfType<(double X, double Y, double Z)>().ToList(), loop);
                    var length = pieces.Sum(p => p.Length);
                    if (length <= 0) continue;

                    count++;
                    metres += length;
                    segments.AddRange(pieces);
                }

                return count == 0 ? null : new TrafficRoad(count, metres / 1000, Reachable(segments, ReachMetres) / 1000);
            }
        }

        /// <summary>One straight piece of a lane: where its middle is in plan view, and how long it is, in metres</summary>
        private readonly record struct Segment(double X, double Z, double Length);

        private static (double X, double Y, double Z)? Point(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 3) return null;
            var components = new double[3];
            var i = 0;
            foreach (var component in value.EnumerateArray())
            {
                if (component.ValueKind != JsonValueKind.Number || !component.TryGetDouble(out var figure) || !double.IsFinite(figure)) return null;
                if (i < 3) components[i] = figure;
                i++;
            }
            return (components[0], components[1], components[2]);
        }

        /// <summary>A lane's pieces, the loop closed when the lane says it is one</summary>
        private static List<Segment> Segments(List<(double X, double Y, double Z)> points, bool loop)
        {
            var segments = new List<Segment>();
            if (points.Count < 2) return segments;

            var last = loop ? points.Count : points.Count - 1;
            for (var i = 0; i < last; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var dx = a.X - b.X;
                var dy = a.Y - b.Y;
                var dz = a.Z - b.Z;
                segments.Add(new Segment((a.X + b.X) / 2, (a.Z + b.Z) / 2, Math.Sqrt(dx * dx + dy * dy + dz * dz)));
            }
            return segments;
        }

        /// <summary>
        /// Lane metres within <paramref name="reach"/> of a typical point on the road: stands at evenly spaced places along
        /// every lane (in proportion to its length), adds up the lane within the radius in plan view (a road folding back
        /// 100 m below is still in reach), and averages
        /// </summary>
        private static double Reachable(List<Segment> segments, double reach)
        {
            if (segments.Count == 0) return 0;

            var stride = Math.Max(1, segments.Count / ReachSamples);
            var reachSquared = reach * reach;
            var total = 0.0;
            var samples = 0;
            for (var i = 0; i < segments.Count; i += stride)
            {
                var here = segments[i];
                var within = 0.0;
                foreach (var segment in segments)
                {
                    var dx = segment.X - here.X;
                    var dz = segment.Z - here.Z;
                    if (dx * dx + dz * dz <= reachSquared) within += segment.Length;
                }
                total += within;
                samples++;
            }
            return total / samples;
        }
    }
}
