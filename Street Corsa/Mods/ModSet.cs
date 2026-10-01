using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreetCorsa.Mods;

/// <summary>The folder a kind of content lives in, in every mod</summary>
public static class ContentTypes
{
    public const string Parts = "parts";
    public const string Sounds = "sounds";
    public const string Dealers = "dealers";
    public const string PartShops = "partshops";
    public const string Racers = "racers";
    public const string Events = "events";
    public const string Milestones = "milestones";
    public const string Victories = "victories";
    public const string Streets = "streets";
    public const string Stations = "stations";
    public const string Showcase = "showcase";
    public const string Garages = "garages";
    public const string Warehouses = "warehouses";
    public const string Text = "text";
    public const string Sfx = "sfx";
    public const string Props = "props";

    /// <summary>The rules of a career (rules/*.json): figures and tables merged key by key, see <c>Rules.RuleBook</c></summary>
    public const string Rules = "rules";
}

/// <summary>What a mod is: <c>"type"</c> in its mod.json</summary>
public static class ModTypes
{
    /// <summary>Adds to the game (the default)</summary>
    public const string Addon = "addon";

    /// <summary>A whole game of its own: a total conversion</summary>
    public const string Game = "game";
}

/// <summary>
/// A mod's mod.json, and the folder it was found in. Read as every other file of a mod is (<see cref="ModJson.Options"/>:
/// any case, comments, trailing commas), and as forgiving as it always was: a number where text goes (<c>"version": 1.1</c>)
/// is that text, a whole number in quotes (<c>"order": "10"</c>) that number.
/// </summary>
public sealed class ModInfo
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(TextOrNumber))]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    [JsonConverter(typeof(TextOrNumber))]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    [JsonConverter(typeof(TextOrNumber))]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    [JsonConverter(typeof(TextOrNumber))]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonConverter(typeof(TextOrNumber))]
    public string Description { get; set; } = string.Empty;

    /// <summary>Mods load in this order after the base game, lowest first; ties by id (a mod always loads after those it requires)</summary>
    [JsonPropertyName("order")]
    [JsonConverter(typeof(WholeNumber))]
    public int Order { get; set; }

    /// <summary>
    /// <see cref="ModTypes.Addon"/> (the default: it adds to the game) or <see cref="ModTypes.Game"/> (a whole game of
    /// its own, a total conversion)
    /// </summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(TextOrNumber))]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The mods this one needs, by id, each with the lowest version it works with ("" for any): without them it is
    /// left out, and it loads after them
    /// </summary>
    [JsonPropertyName("requires")]
    [JsonConverter(typeof(TextMap))]
    public Dictionary<string, string> Requires { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The lowest version of the game this mod works with; empty for any</summary>
    [JsonPropertyName("gameVersion")]
    [JsonConverter(typeof(TextOrNumber))]
    public string GameVersion { get; set; } = string.Empty;

    /// <summary>
    /// Entries of the mods before it, the base game's included, that this one takes out of the game: by kind of content
    /// (the content folder's name: "racers", "streets", "text"...), full ids (<c>"racers": ["king"]</c>)
    /// </summary>
    [JsonPropertyName("removes")]
    public Dictionary<string, List<string>> Removes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string Folder { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsBase => ModIds.IsBase(Id);

    /// <summary>A whole game of its own, not an add-on</summary>
    [JsonIgnore]
    public bool IsGame => ModTypes.Game.Equals(Type, StringComparison.OrdinalIgnoreCase);

    /// <summary>The mod's folder for a kind of content, null when it has none</summary>
    public string? Dir(string type)
    {
        var dir = Path.Combine(Folder, type);
        return Directory.Exists(dir) ? dir : null;
    }

    public override string ToString() => string.IsNullOrEmpty(Version) ? Id : $"{Id} {Version}";

    /// <summary>Text as written, or a number or true/false as the text it reads as; null is empty</summary>
    private sealed class TextOrNumber : JsonConverter<string>
    {
        public override bool HandleNull => true;

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => string.Empty,
            _ => throw new JsonException($"expected text, found {reader.TokenType}")
        };

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }

    /// <summary>Names to text (a version written as a number is that text); null is none</summary>
    private sealed class TextMap : JsonConverter<Dictionary<string, string>>
    {
        private static readonly TextOrNumber Value = new();

        public override bool HandleNull => true;

        public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (reader.TokenType == JsonTokenType.Null) return map;
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException($"expected names and versions, found {reader.TokenType}");

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString() ?? string.Empty;
                reader.Read();
                map[name] = Value.Read(ref reader, typeof(string), options);
            }

            return map;
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (name, text) in value) writer.WriteString(name, text);
            writer.WriteEndObject();
        }
    }

    /// <summary>A whole number, written as one, as one with nothing after the point (10.0), or in quotes</summary>
    private sealed class WholeNumber : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            double value;
            if (reader.TokenType == JsonTokenType.Number) value = reader.GetDouble();
            else if (reader.TokenType == JsonTokenType.String
                     && double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) value = parsed;
            else throw new JsonException($"expected a whole number, found {reader.TokenType}");

            if (value != Math.Floor(value) || value < int.MinValue || value > int.MaxValue) throw new JsonException($"{value} is not a whole number");
            return (int)value;
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }
}

/// <summary>
/// The mods under the Content folder, in load order: the base game first (Content\streetcorsa), then every folder
/// with a mod.json. A folder whose name starts with '_' or '.' is switched off. Each kind of content is read from
/// every mod that has its folder, in this order, and merged (see <see cref="ContentRegistry{T}"/>).
///
/// Plain BCL on purpose: the tools compile it by link.
/// </summary>
public sealed class ModSet
{
    public const string FolderName = "Content";
    public const string ManifestFileName = "mod.json";

    /// <summary>The game's own settings in a mod's folder, merged key by key (see <c>Rules.GameFile</c>)</summary>
    public const string GameFileName = "game.json";

    private static readonly Lazy<ModSet> InstalledSet = new(() => Load(DefaultRoot));

    // What the mods take out of the game: kind of content -> full id -> the mod that took it out
    private readonly Dictionary<string, Dictionary<string, ModInfo>> _removed = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _problems = new();

    // A mod's folders are listed as its content is read, from any thread: one the game may not open is passed over,
    // one that cannot be listed at all is a line in the problems once, and the rest of the mods stand
    private static readonly EnumerationOptions Listing = new() { IgnoreInaccessible = true, AttributesToSkip = 0, MatchType = MatchType.Win32 };

    private ModSet(string root, List<ModInfo> mods)
    {
        Root = root;
        Mods = mods;
    }

    /// <summary>The Content folder next to the game</summary>
    public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, FolderName);

    /// <summary>The mods next to the game, read once</summary>
    public static ModSet Installed => InstalledSet.Value;

    public string Root { get; }

    /// <summary>Base game first, then the others in load order</summary>
    public IReadOnlyList<ModInfo> Mods { get; }

    /// <summary>The base game; a stand-in pointing at a folder that is not there when the install has lost it</summary>
    public ModInfo Base => Mods.FirstOrDefault(m => m.IsBase)
        ?? new ModInfo { Id = ModIds.BaseId, Name = "Street Corsa", Folder = Path.Combine(Root, ModIds.BaseId) };

    /// <summary>Mods that could not be read or were left out, and why: one line each</summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (_problems) return _problems.ToList();
        }
    }

    public ModInfo? Get(string id) => Mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Only the base game, from a folder of its own (tests, tools)</summary>
    public static ModSet BaseOnly(string baseFolder)
    {
        var info = ReadManifest(baseFolder, out _) ?? new ModInfo();
        info.Id = ModIds.BaseId;
        info.Folder = baseFolder;
        if (string.IsNullOrEmpty(info.Name)) info.Name = "Street Corsa";
        return new ModSet(Path.GetDirectoryName(baseFolder) ?? baseFolder, new List<ModInfo> { info });
    }

    /// <summary>The version of the game (the program): a mod's <c>gameVersion</c> is held against it</summary>
    public static ModVersion ProgramVersion { get; } = ReadProgramVersion();

    private static ModVersion ReadProgramVersion()
    {
        var assembly = typeof(ModSet).Assembly;
        var informational = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        if (ModVersion.TryParse(informational, out var version)) return version;

        var name = assembly.GetName().Version;
        return name == null ? ModVersion.Zero : new ModVersion(name.Major, name.Minor, Math.Max(0, name.Build));
    }

    /// <remarks>Never throws for what is in the folder: a mod that does not read is left out, with a line in <see cref="Problems"/></remarks>
    /// <param name="gameVersion">The game's version, for the mods' <c>gameVersion</c>; <see cref="ProgramVersion"/> when not given</param>
    public static ModSet Load(string root, ModVersion? gameVersion = null)
    {
        var game = gameVersion ?? ProgramVersion;
        var problems = new List<string>();
        var mods = new List<ModInfo>();

        if (Directory.Exists(root))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in List(root, dirs: true, "*", problems).Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith('_') || name.StartsWith('.')) continue;

                var isBase = ModIds.IsBase(name);
                var info = ReadManifest(folder, out var problem);
                if (problem != null)
                {
                    problems.Add($"{name}: {problem}");
                    if (!isBase) continue;
                }

                if (info == null)
                {
                    // The base game is the base game with or without its mod.json; any other folder needs one
                    if (!isBase)
                    {
                        problems.Add($"{name}: no {ManifestFileName}, not a mod; left out");
                        continue;
                    }

                    info = new ModInfo();
                }

                if (isBase) info.Id = ModIds.BaseId;
                if (string.IsNullOrWhiteSpace(info.Id)) info.Id = name.ToLowerInvariant();
                if (!ModIds.IsValidModId(info.Id))
                {
                    problems.Add($"{name}: \"{info.Id}\" is no mod id (lower case letters, digits, '_' and '-'; not \"car\"); left out");
                    continue;
                }

                if (!isBase && ModIds.IsBase(info.Id))
                {
                    problems.Add($"{name}: the id \"{info.Id}\" is the base game's; left out");
                    continue;
                }

                if (!seen.Add(info.Id))
                {
                    problems.Add($"{name}: another folder is already mod \"{info.Id}\"; left out");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(info.Type) && !info.IsGame && !ModTypes.Addon.Equals(info.Type, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{name}: \"type\": \"{info.Type}\" is neither \"{ModTypes.Addon}\" nor \"{ModTypes.Game}\"; taken as an add-on");

                if (!isBase && !string.IsNullOrWhiteSpace(info.GameVersion))
                {
                    if (!ModVersion.TryParse(info.GameVersion, out var wanted))
                    {
                        problems.Add($"{name}: \"gameVersion\": \"{info.GameVersion}\" is no version; not checked");
                    }
                    else if (game < wanted)
                    {
                        problems.Add($"{name}: needs the game at {wanted} or later, this is {game}; left out");
                        continue;
                    }
                }

                info.Folder = folder;
                if (string.IsNullOrWhiteSpace(info.Name)) info.Name = isBase ? "Street Corsa" : info.Id;
                mods.Add(info);
            }
        }

        if (!mods.Any(m => m.IsBase)) problems.Add($"The base game's content ({Path.Combine(root, ModIds.BaseId)}) is missing");

        var others = WithWhatTheyNeed(mods.Where(m => !m.IsBase).ToList(), problems)
            .OrderBy(m => m.Order).ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ordered = mods.Where(m => m.IsBase).Concat(AfterWhatTheyNeed(others, problems)).ToList();
        var set = new ModSet(root, ordered);
        set._problems.AddRange(problems);
        set.ReadRemovals();
        return set;
    }

    /// <summary>
    /// The mods whose <c>requires</c> are there, in a version new enough: one that is not is left out, and so is what
    /// needs it in turn
    /// </summary>
    private static List<ModInfo> WithWhatTheyNeed(List<ModInfo> mods, List<string> problems)
    {
        var kept = mods.ToList();
        while (true)
        {
            var byId = kept.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
            var lacking = kept.Select(m => (Mod: m, Why: Lacks(m, byId))).FirstOrDefault(x => x.Why != null);
            if (lacking.Mod == null) return kept;

            problems.Add($"{Path.GetFileName(lacking.Mod.Folder)}: {lacking.Why}; left out");
            kept.Remove(lacking.Mod);
        }

        static string? Lacks(ModInfo mod, Dictionary<string, ModInfo> byId)
        {
            foreach (var (id, version) in mod.Requires)
            {
                if (ModIds.IsBase(id) || id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase)) continue;
                if (!byId.TryGetValue(id, out var needed)) return $"needs the mod \"{id}\", which is not installed (or was left out)";
                if (string.IsNullOrWhiteSpace(version) || version.Trim() == "*" || !ModVersion.TryParse(version, out var wanted)) continue;
                if (!ModVersion.TryParse(needed.Version, out var has) || has < wanted)
                    return $"needs \"{id}\" at {wanted} or later, which is at {(string.IsNullOrWhiteSpace(needed.Version) ? "no version" : needed.Version)}";
            }

            return null;
        }
    }

    /// <summary>
    /// The mods in their order, each moved after the mods it requires (the order kept otherwise); mods that require
    /// each other round in a circle keep their order, told once
    /// </summary>
    private static List<ModInfo> AfterWhatTheyNeed(List<ModInfo> mods, List<string> problems)
    {
        var ids = new HashSet<string>(mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var waiting = mods.ToList();
        var result = new List<ModInfo>();
        while (waiting.Count > 0)
        {
            var next = waiting.FirstOrDefault(m => m.Requires.Keys.All(id =>
                !ids.Contains(id) || placed.Contains(id) || id.Equals(m.Id, StringComparison.OrdinalIgnoreCase)));
            if (next == null)
            {
                problems.Add($"{string.Join(", ", waiting.Select(m => m.Id))} require each other round in a circle; they load in their own order");
                result.AddRange(waiting);
                break;
            }

            waiting.Remove(next);
            placed.Add(next.Id);
            result.Add(next);
        }

        return result;
    }

    /// <summary>What each mod takes out (its <c>removes</c>): the latest mod to name an entry is the one said to</summary>
    private void ReadRemovals()
    {
        foreach (var mod in Mods)
        {
            foreach (var (type, ids) in mod.Removes)
            {
                if (ids == null) continue;
                if (!_removed.TryGetValue(type, out var removed))
                    _removed[type] = removed = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in ids.Where(i => !string.IsNullOrWhiteSpace(i))) removed[ModIds.Normalize(id.Trim())] = mod;
            }
        }
    }

    private static readonly Dictionary<string, ModInfo> NothingRemoved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The full ids of a kind of content that mods take out of the game, and which mod did</summary>
    public IReadOnlyDictionary<string, ModInfo> Removed(string type) =>
        _removed.TryGetValue(type, out var removed) ? removed : NothingRemoved;

    /// <summary>True when a mod takes the entry of this full id out of the game</summary>
    public bool IsRemoved(string type, string id) => Removed(type).ContainsKey(ModIds.Normalize(id));

    /// <summary>The full ids of a kind of content one mod takes out (its <c>removes</c>), as it lists them</summary>
    public static IReadOnlyList<string> RemovesOf(ModInfo mod, string type) =>
        mod.Removes.TryGetValue(type, out var ids) && ids != null
            ? ids.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => ModIds.Normalize(i.Trim())).ToList()
            : [];

    /// <summary>
    /// The mods' removals of a kind of content, applied in load order as it is read (<see cref="ModRemovals"/>): a mod
    /// takes out entries of the mods before it, never its own or a later mod's
    /// </summary>
    /// <param name="remove">Takes an entry out by its full id; false when there was none</param>
    /// <param name="what">What the entries are, for the report ("racer", "the text")</param>
    public ModRemovals Removals(string type, Func<string, ModInfo, bool> remove, string what, List<string> problems) =>
        new(this, type, remove, what, problems);

    private static ModInfo? ReadManifest(string folder, out string? problem)
    {
        problem = null;
        var file = Path.Combine(folder, ManifestFileName);
        if (!File.Exists(file)) return null;

        try
        {
            var info = JsonSerializer.Deserialize<ModInfo>(File.ReadAllText(file), ModJson.Options) ?? new ModInfo();
            // "removes": null reads as no table at all
            info.Removes ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            info.Requires ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return info;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = $"{ManifestFileName} could not be read ({ex.Message})";
            return null;
        }
    }

    /// <summary>Every mod that has a folder for this kind of content, in load order</summary>
    public IEnumerable<(ModInfo Mod, string Dir)> Folders(string type)
    {
        foreach (var mod in Mods)
        {
            if (mod.Dir(type) is { } dir) yield return (mod, dir);
        }
    }

    /// <summary>The .json files directly in each mod's folder for this kind of content: mods in load order, files by name</summary>
    public IEnumerable<(ModInfo Mod, string File)> JsonFiles(string type)
    {
        foreach (var (mod, dir) in Folders(type))
        {
            foreach (var file in JsonFilesIn(dir)) yield return (mod, file);
        }
    }

    /// <summary>The .json files directly in one folder, by name; none for a folder that cannot be listed (told)</summary>
    public IEnumerable<string> JsonFilesIn(string dir) =>
        List(dir, dirs: false, "*.json", _problems).Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folders in each mod's folder for this kind of content (a street, a sound, a garage)</summary>
    public IEnumerable<(ModInfo Mod, string Dir)> SubFolders(string type)
    {
        foreach (var (mod, dir) in Folders(type))
        {
            foreach (var sub in List(dir, dirs: true, "*", _problems).Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(sub);
                if (!name.StartsWith('_') && !name.StartsWith('.')) yield return (mod, sub);
            }
        }
    }

    /// <summary>
    /// The folders or files directly in a folder, whole; none for a folder that cannot be listed, told once in
    /// <paramref name="problems"/> (locked: the set's own are added to from any thread)
    /// </summary>
    private static List<string> List(string folder, bool dirs, string pattern, List<string> problems)
    {
        try
        {
            return (dirs ? Directory.EnumerateDirectories(folder, pattern, Listing) : Directory.EnumerateFiles(folder, pattern, Listing)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var line = $"{folder}: could not be read ({ex.Message}); left out";
            lock (problems)
            {
                if (!problems.Contains(line)) problems.Add(line);
            }

            return new List<string>();
        }
    }

    /// <summary>Id and version of each mod, for a save to remember what it was made with</summary>
    public List<ModRef> References() => Mods.Select(m => new ModRef { Id = m.Id, Name = m.Name, Version = m.Version }).ToList();
}

/// <summary>A mod as a save remembers it</summary>
public sealed class ModRef
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    public override string ToString() => string.IsNullOrEmpty(Version) ? Name : $"{Name} {Version}";
}
