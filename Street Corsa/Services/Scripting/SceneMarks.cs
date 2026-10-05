using System.Globalization;
using System.IO;
using System.Text.Json;
using StreetCorsa.Mods;
using StreetCorsa.Services.Station;

namespace StreetCorsa.Services.Scripting
{
    /// <summary>
    /// The named places of a scene its scripts drive to and along: <c>"marks"</c> (a name each: a point
    /// <c>[x, z]</c>, or one measured from the car's home, <c>{ "behind": 7 }</c>) and <c>"paths"</c> (a name each: a
    /// list of points). Read from a scene's JSON next to what the scene already has; what does not read is skipped
    /// with a line in the mods' problems.
    /// </summary>
    public sealed record SceneMarks(IReadOnlyDictionary<string, MarkSpec> Marks, IReadOnlyDictionary<string, IReadOnlyList<GroundPoint>> Paths)
    {
        public static readonly SceneMarks None = new(new Dictionary<string, MarkSpec>(), new Dictionary<string, IReadOnlyList<GroundPoint>>());

        /// <summary>These, with <paramref name="marks"/> and <paramref name="paths"/> as well where they have no such name</summary>
        public SceneMarks With(IReadOnlyDictionary<string, MarkSpec>? marks = null, IReadOnlyDictionary<string, IReadOnlyList<GroundPoint>>? paths = null)
        {
            var allMarks = new Dictionary<string, MarkSpec>(Marks, StringComparer.OrdinalIgnoreCase);
            foreach (var (name, mark) in marks ?? new Dictionary<string, MarkSpec>()) allMarks.TryAdd(name, mark);
            var allPaths = new Dictionary<string, IReadOnlyList<GroundPoint>>(Paths, StringComparer.OrdinalIgnoreCase);
            foreach (var (name, path) in paths ?? new Dictionary<string, IReadOnlyList<GroundPoint>>()) allPaths.TryAdd(name, path);
            return new SceneMarks(allMarks, allPaths);
        }

        /// <summary>
        /// Where a car stands that the scripts bring to <paramref name="mark"/> along <paramref name="path"/>: at the mark
        /// when it is a point (else the path's end), facing the way the path ends; null when the scene has no such path.
        /// The scripts and the screen resolve them the same way, so a mod's own path or mark moves both.
        /// </summary>
        public GroundPose? Stand(string mark, string path)
        {
            if (!Paths.TryGetValue(path, out var points) || points.Count == 0) return null;
            var (x, z, heading, _) = DrivePath.Through(points).At(float.MaxValue);
            return Marks.TryGetValue(mark, out var m) && m.IsPoint ? new GroundPose(m.X!.Value, m.Z!.Value, heading) : new GroundPose(x, z, heading);
        }

        /// <summary>Where the path <paramref name="path"/> starts, facing along it; null when the scene has no such path</summary>
        public GroundPose? PathStart(string path)
        {
            if (!Paths.TryGetValue(path, out var points) || points.Count == 0) return null;
            var (x, z, heading, _) = DrivePath.Through(points).At(0f);
            return new GroundPose(x, z, heading);
        }

        // How far a point may be from a scene's middle, and a mark from the car's home, in metres
        public const float MostPoint = 10000f;
        public const float MostOffset = 1000f;

        private static readonly JsonDocumentOptions FileOptions = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>The marks and paths of a scene's file; <paramref name="where"/> names it in the problems</summary>
        public static SceneMarks Read(JsonElement scene, string where, List<string> problems)
        {
            var marks = new Dictionary<string, MarkSpec>(StringComparer.OrdinalIgnoreCase);
            var paths = new Dictionary<string, IReadOnlyList<GroundPoint>>(StringComparer.OrdinalIgnoreCase);

            if (ModJson.TryGet(scene, "marks", out var m) && m.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in m.EnumerateObject())
                {
                    try
                    {
                        marks[property.Name] = ParseMark(property.Value, $"the mark \"{property.Name}\"", names: false);
                    }
                    catch (FormatException ex)
                    {
                        problems.Add($"{where}: {ex.Message}; left out");
                    }
                }
            }

            if (ModJson.TryGet(scene, "paths", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in p.EnumerateObject())
                {
                    try
                    {
                        var points = property.Value.ValueKind == JsonValueKind.Array
                            ? property.Value.EnumerateArray().Select((v, i) => ParsePoint(v, $"the path \"{property.Name}\"[{i}]")).ToList()
                            : [];
                        if (points.Count == 0) throw new FormatException($"the path \"{property.Name}\" is not a list of points [x, z]");
                        paths[property.Name] = points;
                    }
                    catch (FormatException ex)
                    {
                        problems.Add($"{where}: {ex.Message}; left out");
                    }
                }
            }

            return new SceneMarks(marks, paths);
        }

        /// <summary>A scene's file, as its loader and its marks read it (comments and trailing commas let by); throws as the parser does</summary>
        public static JsonDocument Parse(string file) => JsonDocument.Parse(File.ReadAllText(file), FileOptions);

        /// <summary>The marks and paths of the scene file <paramref name="file"/>; none when it is missing or does not read</summary>
        public static SceneMarks ReadFile(string file, List<string> problems)
        {
            if (!File.Exists(file)) return None;
            try
            {
                using var document = Parse(file);
                return Read(document.RootElement, file, problems);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{file} could not be read ({ex.Message}); its marks left out");
                return None;
            }
        }

        /// <summary>
        /// A mark as a scene's file and a script both write it: a point <c>[x, z]</c>, one measured from the car's home
        /// (<c>{ "behind": 7 }</c>, with <c>ahead</c>, <c>left</c>, <c>right</c>), or, where <paramref name="names"/>
        /// lets it, a name. Whatever is wrong with it throws, saying what and where.
        /// </summary>
        public static MarkSpec ParseMark(JsonElement m, string where, bool names)
        {
            switch (m.ValueKind)
            {
                case JsonValueKind.String when names && !string.IsNullOrWhiteSpace(m.GetString()):
                    return new MarkSpec(m.GetString()!.Trim());
                case JsonValueKind.Array:
                {
                    var point = ParsePoint(m, where);
                    return new MarkSpec(X: point.X, Z: point.Z);
                }
                case JsonValueKind.Object:
                {
                    float ahead = 0, left = 0;
                    foreach (var property in m.EnumerateObject())
                    {
                        var v = Number(property.Value, $"{where}.{property.Name}", -MostOffset, MostOffset);
                        switch (property.Name.ToLowerInvariant())
                        {
                            case "ahead": ahead += v; break;
                            case "behind": ahead -= v; break;
                            case "left": left += v; break;
                            case "right": left -= v; break;
                            default: throw new FormatException($"{where}: \"{property.Name}\" is not one of ahead, behind, left, right");
                        }
                    }

                    return new MarkSpec(Ahead: ahead, Left: left);
                }
                default:
                    throw new FormatException(names
                        ? $"{where} is no mark: a name, a point [x, z], or {{ \"behind\": metres }} and the like"
                        : $"{where} is no point [x, z] nor {{ \"behind\": metres }} and the like");
            }
        }

        /// <summary>A point <c>[x, z]</c> on a scene's ground, within <see cref="MostPoint"/> of its middle</summary>
        public static GroundPoint ParsePoint(JsonElement m, string where)
        {
            var xz = m.ValueKind == JsonValueKind.Array ? m.EnumerateArray().Select(v => Number(v, where, -MostPoint, MostPoint)).ToList() : [];
            return xz.Count == 2 ? new GroundPoint(xz[0], xz[1]) : throw new FormatException($"{where} is not a point [x, z]");
        }

        /// <summary>A finite number from <paramref name="min"/> to <paramref name="max"/>, else a problem naming <paramref name="where"/></summary>
        internal static float Number(JsonElement v, string where, float min, float max)
        {
            if (v.ValueKind != JsonValueKind.Number || !float.IsFinite(v.GetSingle())) throw new FormatException($"{where}: {v} is not a number");
            var value = v.GetSingle();
            return value >= min && value <= max
                ? value
                : throw new FormatException(string.Format(CultureInfo.InvariantCulture, "{0}: {1} is not between {2} and {3}", where, value, min, max));
        }
    }
}
