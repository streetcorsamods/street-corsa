using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Text
{
    /// <summary>
    /// The words the game says, from every mod's <c>text</c> folder: the paper's stories, what racers say, street talk,
    /// names. Each is a pool of lines under a key ("news.king_beaten.headline"); where the game says something, it
    /// picks one line of the pool at random and fills in its {placeholders}. When to say what stays in the game's code.
    ///
    /// A text file has <c>"lines"</c>, whose pools join those of the mods before it (a mod adds lines to a pool), and
    /// <c>"replace"</c>, whose pools take the place of the ones before (a mod rewrites a pool). A pool is a line or a
    /// list of lines.
    /// </summary>
    public sealed partial class GameText
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Text");

        private static readonly Lazy<GameText> InstalledText = new(() => Load(ModSet.Installed));

        private readonly Dictionary<string, List<string>> _pools = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _missingReported = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        /// <summary>The words of the mods next to the game, read once</summary>
        public static GameText Installed => InstalledText.Value;

        public IReadOnlyList<string> Problems { get; private set; } = [];

        public IEnumerable<string> Keys => _pools.Keys;

        public static GameText Load(ModSet mods)
        {
            var problems = new List<string>();
            var text = new GameText();
            foreach (var (mod, file) in mods.JsonFiles(ContentTypes.Text))
            {
                var source = $"{mod.Id}/{ContentTypes.Text}/{System.IO.Path.GetFileName(file)}";
                try
                {
                    using var document = JsonDocument.Parse(System.IO.File.ReadAllText(file),
                        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    if (ModJson.TryGet(document.RootElement, "lines", out var lines)) text.Read(lines, replace: false, source, problems);
                    if (ModJson.TryGet(document.RootElement, "replace", out var replace)) text.Read(replace, replace: true, source, problems);
                }
                catch (Exception ex) when (ex is JsonException or System.IO.IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{source}: {ex.Message}; left out");
                }
            }

            foreach (var problem in problems) Logger.Warning("Text: {Problem}", problem);
            text.Problems = problems;
            return text;
        }

        private void Read(JsonElement section, bool replace, string source, List<string> problems)
        {
            if (section.ValueKind != JsonValueKind.Object) return;

            foreach (var pool in section.EnumerateObject())
            {
                var lines = pool.Value.ValueKind switch
                {
                    JsonValueKind.String => [pool.Value.GetString()!],
                    JsonValueKind.Array => pool.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList(),
                    _ => new List<string>()
                };

                if (lines.Count == 0)
                {
                    problems.Add($"{source}: {pool.Name} has no lines; left out");
                    continue;
                }

                if (replace)
                {
                    if (!_pools.ContainsKey(pool.Name)) problems.Add($"{source}: replaces {pool.Name}, which no mod before it has; taken as new lines");
                    _pools[pool.Name] = lines;
                }
                else if (_pools.TryGetValue(pool.Name, out var existing))
                {
                    existing.AddRange(lines);
                }
                else
                {
                    _pools[pool.Name] = lines;
                }
            }
        }

        /// <summary>The lines of a pool; none when no mod has it</summary>
        public IReadOnlyList<string> Lines(string key)
        {
            if (_pools.TryGetValue(key, out var lines)) return lines;

            lock (_lock)
            {
                if (_missingReported.Add(key)) Logger.Warning("No mod has the text {Key}: nothing is said there", key);
            }

            return [];
        }

        public bool Has(string key) => _pools.ContainsKey(key);

        /// <summary>A line of the pool at random, filled in; empty when no mod has the pool</summary>
        public string Pick(string key, Random random, IReadOnlyDictionary<string, object?>? values = null)
        {
            var lines = Lines(key);
            // A pool of one draws nothing: text a mod has not added to reads the same for the same seed
            return lines.Count switch
            {
                0 => string.Empty,
                1 => Fill(lines[0], values),
                _ => Fill(lines[random.Next(lines.Count)], values)
            };
        }

        /// <summary>The first line of the pool, filled in: for text that is said one way</summary>
        public string Get(string key, IReadOnlyDictionary<string, object?>? values = null)
        {
            var lines = Lines(key);
            return lines.Count == 0 ? string.Empty : Fill(lines[0], values);
        }

        [GeneratedRegex(@"\{(\w+)(?::([^{}]+))?\}")]
        private static partial Regex Placeholder();

        /// <summary>
        /// {name} is the value of that name, {name:N0} with a format; a name without a value stays as it is written,
        /// so a typo shows rather than vanishes
        /// </summary>
        public static string Fill(string template, IReadOnlyDictionary<string, object?>? values)
        {
            if (values == null || template.IndexOf('{') < 0) return template;

            return Placeholder().Replace(template, match =>
            {
                if (!values.TryGetValue(match.Groups[1].Value, out var value)) return match.Value;

                return value switch
                {
                    null => string.Empty,
                    IFormattable formattable when match.Groups[2].Success => formattable.ToString(match.Groups[2].Value, CultureInfo.CurrentCulture),
                    _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
                };
            });
        }
    }

    /// <summary>Placeholder values for <see cref="GameText"/>, by name whatever the case</summary>
    public sealed class TextValues : Dictionary<string, object?>
    {
        public TextValues() : base(StringComparer.OrdinalIgnoreCase)
        {
        }
    }
}
