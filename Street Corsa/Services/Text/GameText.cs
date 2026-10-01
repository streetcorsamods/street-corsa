using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Text
{
    /// <summary>
    /// The words the game says, from every mod's <c>text</c> folder: the screens' labels and messages, the paper's
    /// stories, what racers say, street talk, names. Each is a pool of lines under a key ("news.king_beaten.headline");
    /// where the game says something, it picks one line of the pool at random and fills in its {placeholders}. When to
    /// say what stays in the game's code.
    ///
    /// A text file has <c>"lines"</c>, whose pools join those of the mods before it (a mod adds lines to a pool), and
    /// <c>"replace"</c>, whose pools take the place of the ones before (a mod rewrites a pool). A pool is a line or a
    /// list of lines.
    ///
    /// The files directly in <c>text</c> are in the game's own language (game.json's <c>language</c>). A translation
    /// is a folder in <c>text</c> named for its language (<c>text\es</c>), read the same way over every mod; with the
    /// player's language picked, its pools take the place of the game's, and a key it lacks says the game's own words.
    /// </summary>
    public sealed partial class GameText
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Text");

        private static readonly Lazy<GameText> InstalledText = new(() => Load(ModSet.Installed, Language));

        private readonly Dictionary<string, List<string>> _pools = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _missingReported = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        // The player's language over the game's own; null when they play in the game's own
        private GameText? _translation;

        /// <summary>
        /// The player's language (a folder in the mods' <c>text</c>), set from their settings at start-up before any
        /// word is said; null or empty for the game's own. A change takes effect at the next start.
        /// </summary>
        public static string? Language { get; set; }

        /// <summary>The words of the mods next to the game, read once, in the player's language</summary>
        public static GameText Installed => InstalledText.Value;

        public IReadOnlyList<string> Problems { get; private set; } = [];

        /// <summary>The keys of the game's own words (a translation says some of them its own way)</summary>
        public IEnumerable<string> Keys => _pools.Keys;

        /// <summary>The language over the game's own words; null when there is none</summary>
        public string? Translation { get; private set; }

        /// <summary>
        /// The game's words, and over them those of <paramref name="language"/> when it is not the game's own. A
        /// language no mod has is told: the game's own words are said.
        ///
        /// The mods are read in load order, each its own words and then its words in the language: what a mod takes
        /// out (its mod.json's "removes") or rewrites (its "replace") goes from the language of the mods before it
        /// too, so a translation of the base game brings none of it back.
        /// </summary>
        public static GameText Load(ModSet mods, string? language = null)
        {
            var problems = new List<string>();
            var text = new GameText();
            var translate = !string.IsNullOrWhiteSpace(language) && !IsOwnLanguage(mods, language);
            var translation = translate ? new GameText() : null;
            var found = false;

            // A pool a mod takes out says nothing, in any language, unless the mod or a later one gives it lines again
            var removals = mods.Removals(ContentTypes.Text,
                (key, _) => text._pools.Remove(key) | (translation?._pools.Remove(key) ?? false), "the text", problems);
            foreach (var (mod, dir) in mods.Folders(ContentTypes.Text))
            {
                removals.Before(mod);
                foreach (var file in mods.JsonFilesIn(dir))
                {
                    // A pool the mod rewrites says the mod's words: an earlier mod's language of it goes
                    text.ReadFile(file, $"{mod.Id}/{ContentTypes.Text}/{System.IO.Path.GetFileName(file)}", problems, null,
                        replaced: key => translation?._pools.Remove(key));
                }

                if (translation != null && ReadLanguageOf(mod, dir, language!, translation, text, problems)) found = true;
            }

            removals.Rest();

            if (translate)
            {
                if (found)
                {
                    text._translation = translation;
                    text.Translation = language;
                }
                else
                {
                    text.LanguageMissing = true;
                    problems.Add($"no mod has the language {language}; the game's own words are said");
                }
            }

            foreach (var problem in problems) Logger.Warning("Text: {Problem}", problem);
            ModProblems.Add("Text", problems);
            text.Problems = problems;
            return text;
        }

        /// <summary>A language was asked for that no mod has (its mod was taken away): the game's own words are said</summary>
        public bool LanguageMissing { get; private set; }

        /// <summary>
        /// One mod's words in a language (its <c>text\&lt;language&gt;</c>) over <paramref name="translation"/>; false
        /// when it has no such folder, no files in it, or they cannot be listed
        /// </summary>
        private static bool ReadLanguageOf(ModInfo mod, string dir, string language, GameText translation, GameText under, List<string> problems)
        {
            var folder = System.IO.Path.Combine(dir, language);
            if (!System.IO.Directory.Exists(folder)) return false;

            string[] files;
            try
            {
                files = System.IO.Directory.GetFiles(folder, "*.json");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                problems.Add($"{mod.Id}/{ContentTypes.Text}/{language}: {ex.Message}; left out");
                return false;
            }

            foreach (var file in files.Order(StringComparer.OrdinalIgnoreCase))
                translation.ReadFile(file, $"{mod.Id}/{ContentTypes.Text}/{language}/{System.IO.Path.GetFileName(file)}", problems, under);
            return files.Length > 0;
        }

        private static readonly Lazy<IReadOnlyList<(string Id, string Name)>> InstalledLanguages = new(() => Languages(ModSet.Installed));

        /// <summary>The languages of the mods next to the game (<see cref="Languages(ModSet)"/>), read once</summary>
        public static IReadOnlyList<(string Id, string Name)> AvailableLanguages => InstalledLanguages.Value;

        /// <summary>
        /// The languages the player can pick: the game's own (game.json's <c>language</c>) first, then every folder
        /// of the mods' <c>text</c>, each by the name its <c>language.name</c> pool gives it, else its folder's. Only
        /// that pool is looked for, not every word read.
        /// </summary>
        public static IReadOnlyList<(string Id, string Name)> Languages(ModSet mods)
        {
            var own = OwnLanguage(mods);
            var ownName = ReferenceEquals(mods, ModSet.Installed)
                ? NameOf(Installed._pools)
                : LanguageName(mods.JsonFiles(ContentTypes.Text).Select(f => f.File));
            var languages = new List<(string Id, string Name)> { (own, ownName ?? own) };

            var byId = mods.SubFolders(ContentTypes.Text)
                .GroupBy(f => System.IO.Path.GetFileName(f.Dir), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.Equals(g.Key, own, StringComparison.OrdinalIgnoreCase))
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var folders in byId)
            {
                var files = folders.SelectMany(f => JsonFilesOrNone(f.Dir));
                languages.Add((folders.Key, LanguageName(files) ?? folders.Key));
            }

            return languages;
        }

        private static IEnumerable<string> JsonFilesOrNone(string dir)
        {
            try
            {
                return System.IO.Directory.GetFiles(dir, "*.json").Order(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        private const string LanguageNameKey = "language.name";

        private static string? NameOf(Dictionary<string, List<string>> pools) =>
            pools.TryGetValue(LanguageNameKey, out var name) && name.Count > 0 ? name[0] : null;

        /// <summary>The first line of the <c>language.name</c> pool across the files, read in their order; null when none has it</summary>
        private static string? LanguageName(IEnumerable<string> files)
        {
            string? name = null;
            foreach (var file in files)
            {
                try
                {
                    using var document = JsonDocument.Parse(System.IO.File.ReadAllText(file),
                        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    if (name == null && ModJson.TryGet(document.RootElement, "lines", out var lines) && FirstLine(lines) is { } added) name = added;
                    if (ModJson.TryGet(document.RootElement, "replace", out var replace) && FirstLine(replace) is { } replaced) name = replaced;
                }
                catch (Exception ex) when (ex is JsonException or System.IO.IOException or UnauthorizedAccessException)
                {
                    // Told when the words are read; here the folder's name stands in
                }
            }

            return name;

            static string? FirstLine(JsonElement section)
            {
                if (section.ValueKind != JsonValueKind.Object || !ModJson.TryGet(section, LanguageNameKey, out var pool)) return null;
                return pool.ValueKind switch
                {
                    JsonValueKind.String => pool.GetString(),
                    JsonValueKind.Array => pool.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()).FirstOrDefault(),
                    _ => null
                };
            }
        }

        /// <summary>The language the files directly in the mods' <c>text</c> are written in: their game.json's</summary>
        private static string OwnLanguage(ModSet mods) =>
            ReferenceEquals(mods, ModSet.Installed) ? Rules.GameFile.Installed.Language : Rules.GameFile.Load(mods).Language;

        private static bool IsOwnLanguage(ModSet mods, string language) =>
            string.Equals(language, OwnLanguage(mods), StringComparison.OrdinalIgnoreCase);

        /// <param name="under">The words a translation is over: a pool it rewrites that they have is no new pool</param>
        /// <param name="replaced">Told the key of each pool the file rewrites</param>
        private void ReadFile(string file, string source, List<string> problems, GameText? under, Action<string>? replaced = null)
        {
            try
            {
                using var document = JsonDocument.Parse(System.IO.File.ReadAllText(file),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (ModJson.TryGet(document.RootElement, "lines", out var lines)) Read(lines, replace: false, source, problems, under, null);
                if (ModJson.TryGet(document.RootElement, "replace", out var replace)) Read(replace, replace: true, source, problems, under, replaced);
            }
            catch (Exception ex) when (ex is JsonException or System.IO.IOException or UnauthorizedAccessException)
            {
                problems.Add($"{source}: {ex.Message}; left out");
            }
        }

        private void Read(JsonElement section, bool replace, string source, List<string> problems, GameText? under, Action<string>? replaced)
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
                    if (!_pools.ContainsKey(pool.Name) && under?._pools.ContainsKey(pool.Name) != true) problems.Add($"{source}: replaces {pool.Name}, which no mod before it has; taken as new lines");
                    _pools[pool.Name] = lines;
                    replaced?.Invoke(pool.Name);
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
            if (_translation != null && _translation._pools.TryGetValue(key, out var translated)) return translated;
            if (_pools.TryGetValue(key, out var lines)) return lines;

            lock (_lock)
            {
                if (_missingReported.Add(key)) Logger.Warning("No mod has the text {Key}: nothing is said there", key);
            }

            return [];
        }

        public bool Has(string key) => _pools.ContainsKey(key) || _translation?._pools.ContainsKey(key) == true;

        /// <summary>
        /// What the game says at <paramref name="key"/> (a label, a message, a title): the first line of its pool in
        /// the player's language, its {placeholders} filled from <paramref name="values"/>. A key no mod has says
        /// itself, so a word left out shows where it goes.
        /// </summary>
        public static string Say(string key, params (string Name, object? Value)[] values)
        {
            var lines = Installed.Lines(key);
            if (lines.Count == 0) return key;
            if (values.Length == 0) return lines[0];

            var filled = new TextValues();
            foreach (var (name, value) in values) filled[name] = value;
            return Fill(lines[0], filled);
        }

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
