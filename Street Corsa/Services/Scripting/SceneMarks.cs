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

        /// <summary>The marks and paths of a scene's file; <paramref name="where"/> names it in the problems</summary>
        public static SceneMarks Read(JsonElement scene, string where, List<string> problems)
        {
            var marks = new Dictionary<string, MarkSpec>(StringComparer.OrdinalIgnoreCase);
            var paths = new Dictionary<string, IReadOnlyList<GroundPoint>>(StringComparer.OrdinalIgnoreCase);

            if (ModJson.TryGet(scene, "marks", out var m) && m.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in m.EnumerateObject())
                {
                    if (ReadMark(property.Value) is { } mark) marks[property.Name] = mark;
                    else problems.Add($"{where}: the mark \"{property.Name}\" is no point [x, z] nor {{ \"behind\": metres }} and the like; left out");
                }
            }

            if (ModJson.TryGet(scene, "paths", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in p.EnumerateObject())
                {
                    var points = property.Value.ValueKind == JsonValueKind.Array
                        ? property.Value.EnumerateArray().Select(Point).ToList()
                        : null;
                    if (points is { Count: > 0 } && points.All(x => x != null)) paths[property.Name] = points.Select(x => x!.Value).ToList();
                    else problems.Add($"{where}: the path \"{property.Name}\" is not a list of points [x, z]; left out");
                }
            }

            return new SceneMarks(marks, paths);
        }

        /// <summary>The marks and paths of the scene file <paramref name="file"/>; none when it is missing or does not read</summary>
        public static SceneMarks ReadFile(string file, List<string> problems)
        {
            if (!File.Exists(file)) return None;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
                return Read(document.RootElement, file, problems);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{file} could not be read ({ex.Message}); its marks left out");
                return None;
            }
        }

        private static MarkSpec? ReadMark(JsonElement value)
        {
            if (Point(value) is { } point) return new MarkSpec(X: point.X, Z: point.Z);
            if (value.ValueKind != JsonValueKind.Object) return null;

            float ahead = 0, left = 0;
            foreach (var property in value.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Number || !float.IsFinite(property.Value.GetSingle())) return null;
                var v = property.Value.GetSingle();
                switch (property.Name.ToLowerInvariant())
                {
                    case "ahead": ahead += v; break;
                    case "behind": ahead -= v; break;
                    case "left": left += v; break;
                    case "right": left -= v; break;
                    default: return null;
                }
            }

            return new MarkSpec(Ahead: ahead, Left: left);
        }

        private static GroundPoint? Point(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2) return null;
            var xz = value.EnumerateArray().ToList();
            return xz.All(v => v.ValueKind == JsonValueKind.Number && float.IsFinite(v.GetSingle()))
                ? new GroundPoint(xz[0].GetSingle(), xz[1].GetSingle())
                : null;
        }
    }
}
