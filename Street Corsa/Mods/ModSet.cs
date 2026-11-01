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

    /// <summary>The crews the racers ride with (crews/*.json: <c>{ "crews": [ ... ] }</c>); see <c>Services.Opponents.CrewBook</c></summary>
    public const string Crews = "crews";
    public const string Events = "events";
    public const string Milestones = "milestones";
    public const string Victories = "victories";
    public const string Streets = "streets";

    /// <summary>The pieces a street is laid out with (streetkit/&lt;piece&gt;/piece.json and its model); see <c>Services.Street.StreetKit</c></summary>
    public const string StreetKit = "streetkit";

    /// <summary>The diners by level (diners/*.json: <c>{ "diners": [ ... ] }</c>), each on a street with a diner on it; see <c>Services.Diner.DinerBook</c></summary>
    public const string Diners = "diners";
    public const string Stations = "stations";

    /// <summary>The paint booths (booths/&lt;folder&gt;/booth.json, its model and its painter's); see <c>Services.PaintBooth.PaintBooths</c></summary>
    public const string Booths = "booths";

    public const string Showcase = "showcase";
    public const string Garages = "garages";
    public const string Warehouses = "warehouses";

    /// <summary>The inside of the part shops (shopscenes/&lt;folder&gt;/shop.json and its model); see <c>Services.PartShops.ShopScenes</c></summary>
    public const string ShopScenes = "shopscenes";
    public const string Text = "text";
    public const string Sfx = "sfx";
    public const string Props = "props";

    /// <summary>The cars' moves on the screens (scripts/*.json: scripts and drive feels); see <c>Services.Scripting.ScriptBook</c></summary>
    public const string Scripts = "scripts";

    /// <summary>What the AI is asked (ai/prompts.json, merged key by key) and the ComfyUI workflows (ai/comfy_*.json); see <c>Services.Ai.AiPrompts</c></summary>
    public const string Ai = "ai";

    /// <summary>The makes and the cars (makes.json, cars.json): merged key by key, see <c>Parts.Cars.CarBook</c></summary>
    public const string Cars = "cars";

    /// <summary>The look: theme/*.json (colour, size and typeface keys), theme/fonts, theme/images; see <c>Styles.Theme</c></summary>
    public const string Theme = "theme";

    /// <summary>The rules of a career (rules/*.json): figures and tables merged key by key, see <c>Rules.RuleBook</c></summary>
    public const string Rules = "rules";

    /// <summary>How the screens hang together (flow/*.json: screens, exits, tiles, the main menu's cards), merged key by key; see <c>Navigation.FlowBook</c></summary>
    public const string Flow = "flow";

    /// <summary>The kinds of race (formats/*.json) and their Lua modules or modes (formats/*.lua, formats/&lt;mode&gt;/); see <c>Services.Race.RaceFormats</c></summary>
    public const string Formats = "formats";
}

/// <summary>What a mod is: <c>"type"</c> in its mod.json</summary>
public static class ModTypes
{
    /// <summary>Adds to the game (the default)</summary>
    public const string Addon = "addon";

    /// <summary>
    /// A setting: a whole game over the core (its cars, places, people, words and look). A career is played in one,
    /// picked at New Game; while it plays the others rest, and so do the add-ons that require them or play in others only
    /// </summary>
    public const string Setting = "setting";

    /// <summary>The older word for a setting (E1's total conversion, recorded only then): read as one</summary>
    public const string Game = "game";

    /// <summary>The type is a setting's (<see cref="Setting"/>, or the older <see cref="Game"/>)</summary>
    public static bool IsSetting(string? type) =>
        Setting.Equals(type, StringComparison.OrdinalIgnoreCase) || Game.Equals(type, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What a mod.json may hold besides the manifest, so a mod can be the one file (and its models and banks): each section
/// is what the file of its name in the content type's folder would hold, read after that file (the parts are files of
/// their own under the mod's parts folder)
/// </summary>
public static class ModSections
{
    /// <summary>The engine builds: what parts\engine_builds.json holds</summary>
    public const string Builds = "builds";

    /// <summary>Parts that changed ids: what parts\part_aliases.json holds</summary>
    public const string PartAliases = "part_aliases";

    /// <summary>Builds that changed ids: what parts\build_aliases.json holds</summary>
    public const string BuildAliases = "build_aliases";

    /// <summary>The cars: what cars\cars.json holds under "cars"</summary>
    public const string Cars = "cars";

    /// <summary>The makes: what cars\makes.json holds under "makes"</summary>
    public const string Makes = "makes";

    /// <summary>The countries of the makes: what cars\makes.json holds under "countries"</summary>
    public const string Countries = "countries";

    /// <summary>The words a car's name is read by (aliases, noise): what cars\makes.json holds under "words"</summary>
    public const string Words = "words";

    /// <summary>The engine sounds' pins and car facts: what sounds\sounds.json holds</summary>
    public const string Sounds = "sounds";

    public static readonly IReadOnlyList<string> All = [Builds, PartAliases, BuildAliases, Cars, Makes, Countries, Words, Sounds];

    /// <summary>The sections of a kind of content (the folder's name)</summary>
    public static IReadOnlyList<string> Of(string type) => type switch
    {
        ContentTypes.Parts => [Builds, PartAliases, BuildAliases],
        ContentTypes.Cars => [Cars, Makes, Countries, Words],
        ContentTypes.Sounds => [Sounds],
        _ => []
    };
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

    /// <summary>Mods load in this order after the core, lowest first; ties by id (a mod always loads after those it requires)</summary>
    [JsonPropertyName("order")]
    [JsonConverter(typeof(WholeNumber))]
    public int Order { get; set; }

    /// <summary>
    /// <see cref="ModTypes.Addon"/> (the default: it adds to the game) or <see cref="ModTypes.Setting"/> (a whole game
    /// over the core; <see cref="ModTypes.Game"/> the same)
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

    /// <summary>
    /// An add-on's: the settings it plays in, by id (an engine pack of 1960s V8s in the 1960s settings); it rests while
    /// any other plays, or none. Empty for every setting. A setting's own is not read.
    /// </summary>
    [JsonPropertyName("settings")]
    [JsonConverter(typeof(TextList))]
    public List<string> ForSettings { get; set; } = [];

    /// <summary>True when the add-on plays in <paramref name="setting"/> (<see cref="ForSettings"/>): any, when it lists none</summary>
    public bool PlaysIn(ModInfo? setting) =>
        ForSettings.Count == 0 || (setting != null && ForSettings.Contains(setting.Id, StringComparer.OrdinalIgnoreCase));

    /// <summary>The lowest version of the game this mod works with; empty for any</summary>
    [JsonPropertyName("gameVersion")]
    [JsonConverter(typeof(TextOrNumber))]
    public string GameVersion { get; set; } = string.Empty;

    /// <summary>
    /// Entries of the mods before it, the core's and the setting's included, that this one takes out of the game: by kind of content
    /// (the content folder's name: "racers", "streets", "text"...), full ids (<c>"racers": ["king"]</c>)
    /// </summary>
    [JsonPropertyName("removes")]
    public Dictionary<string, List<string>> Removes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A setting's picture on the New Game screen: a file in the mod's folder; <c>preview.jpg</c> or <c>preview.png</c>
    /// there when none is named
    /// </summary>
    [JsonPropertyName("preview")]
    [JsonConverter(typeof(TextOrNumber))]
    public string Preview { get; set; } = string.Empty;

    /// <summary>The picture's file, null when it has none (or names one outside its folder, or one not there)</summary>
    [JsonIgnore]
    public string? PreviewFile
    {
        get
        {
            if (string.IsNullOrEmpty(Folder)) return null;
            var names = string.IsNullOrWhiteSpace(Preview) ? ["preview.jpg", "preview.png"] : new[] { Preview };
            foreach (var name in names)
            {
                try
                {
                    if (Helpers.PathNames.TryCombineUnder(Folder, name, out var file) && File.Exists(file)) return file;
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // A name no path can have: no picture by it
                }
            }

            return null;
        }
    }

    [JsonIgnore]
    public string Folder { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsBase => ModIds.IsBase(Id);

    /// <summary>A setting (a whole game over the core), not an add-on; never the core</summary>
    [JsonIgnore]
    public bool IsSetting => !IsBase && ModTypes.IsSetting(Type);

    /// <summary>The mod's folder for a kind of content, null when it has none</summary>
    public string? Dir(string type)
    {
        var dir = Path.Combine(Folder, type);
        return Directory.Exists(dir) ? dir : null;
    }

    [JsonIgnore]
    public string ManifestPath => Path.Combine(Folder, ModSet.ManifestFileName);

    /// <summary>
    /// The JSON text of a section of the mod's mod.json (<see cref="ModSections"/>), null when it has none (or says null).
    /// From any thread. A content load reads about ten sections of each mod, one after another, so the file is parsed
    /// once for them (<see cref="ManifestSections"/>) and let go soon after: an engine mod's mod.json is hundreds of KB
    /// that would otherwise be held for as long as the game runs. A mod.json that does not read has no sections: the
    /// mod was left out for it anyway.
    /// </summary>
    public string? Section(string name) => string.IsNullOrEmpty(Folder) ? null : ManifestSections.Get(ManifestPath, name);

    /// <summary>The fields of the manifest itself, by the names they are written in</summary>
    private static readonly IReadOnlyList<string> ManifestFields = typeof(ModInfo).GetProperties()
        .Select(p => p.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false).OfType<JsonPropertyNameAttribute>().FirstOrDefault()?.Name)
        .OfType<string>().ToList();

    /// <summary>
    /// The names at the top of a mod.json that are neither a field of the manifest nor a section (<c>"build"</c> for
    /// <c>"builds"</c>): nothing reads them. A note (<c>"_comment"</c>) is none of these.
    /// </summary>
    internal static List<string> UnknownNames(string json)
    {
        using var document = JsonDocument.Parse(json, ModJson.DocumentOptions);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return [];

        return document.RootElement.EnumerateObject().Select(p => p.Name)
            .Where(n => !n.StartsWith('_') && !ManifestFields.Contains(n, StringComparer.OrdinalIgnoreCase) && !ModSections.All.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToList();
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

    /// <summary>Texts in a list, or one text alone (<c>"settings": "mysetting"</c>); a number is the text it reads as, null is none</summary>
    private sealed class TextList : JsonConverter<List<string>>
    {
        private static readonly TextOrNumber Value = new();

        public override bool HandleNull => true;

        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return [];
            if (reader.TokenType != JsonTokenType.StartArray) return [Value.Read(ref reader, typeof(string), options)];

            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) list.Add(Value.Read(ref reader, typeof(string), options));
            return list;
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var text in value) writer.WriteStringValue(text);
            writer.WriteEndArray();
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
/// The mods under the Content folder, in load order: the core first (Content\streetcorsa), then every folder
/// with a mod.json, there or one level down in a group (Content\engines\myengine: <see cref="ModFolders"/>). A folder
/// whose name starts with '_' or '.' is switched off (<see cref="IsSwitchedOff"/>). Each kind of content is read from
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

    // The content in play: the core alone until a game is played (Play), the core with its setting and add-ons then
    private static ModSet? _inPlay;
    private static readonly object PlayLock = new();

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

    /// <summary>
    /// The content in play: the core alone (the intro, the main screen, Settings) until a game is played, then the
    /// core, the game's setting and the add-ons (<see cref="Play(ModSet)"/>). What is read from it is kept by
    /// <see cref="ContentCache{T}"/>, read again when it changes.
    /// </summary>
    public static ModSet Installed
    {
        get
        {
            lock (PlayLock) return _inPlay ??= CoreOf(DefaultRoot);
        }
    }

    /// <summary>Makes <paramref name="mods"/> the content in play</summary>
    public static void Play(ModSet mods)
    {
        lock (PlayLock)
        {
            _inPlay = mods;
            // Only a game in a setting has others resting beside it: the core alone plays none, and a setting read while
            // it plays (to be played next) goes bare
            ModIds.Resting(mods.Setting == null ? [] : mods.Resting.Where(m => m.IsSetting).Select(m => m.Id));
        }
    }

    /// <summary>
    /// The content of a game in <paramref name="setting"/> (by id): the bare core for none, and for one that is not
    /// installed or cannot be played (<see cref="IsPlayable"/>)
    /// </summary>
    public static ModSet PlayedIn(string? setting) => Load(DefaultRoot, setting: setting, exactSetting: true);

    /// <summary>The setting of this id is installed and can be played (what it requires is there); always so for none</summary>
    public bool IsPlayable(string? setting) =>
        setting == null || Settings.Any(m => m.Id.Equals(setting, StringComparison.OrdinalIgnoreCase));

    /// <summary>The core alone, from a Content folder: what plays until a game does</summary>
    public static ModSet CoreOf(string root) => Load(root, coreOnly: true);

    /// <summary>
    /// Every mod next to the game as a game would play them, read now (their mod.json only): the settings to pick
    /// from at New Game, the languages
    /// </summary>
    public static ModSet Available => Load(DefaultRoot);

    /// <summary>
    /// Every mod next to the game, every setting among them, as no game plays them: what is listed whatever is in play
    /// (the languages to pick, the credits). Read now (their mod.json only); never played.
    /// </summary>
    public static ModSet Everything => Load(DefaultRoot, everything: true);

    public string Root { get; }

    /// <summary>The core first, then the others in load order: the setting played, the add-ons</summary>
    public IReadOnlyList<ModInfo> Mods { get; }

    /// <summary>The setting played; null for the bare core (none installed)</summary>
    public ModInfo? Setting { get; private init; }

    /// <summary>
    /// The mod that holds the game's own content (its cars, racers, places): the setting played, else the core. Where
    /// the developer's tools save what they edit.
    /// </summary>
    public ModInfo Game => Setting ?? Base;

    /// <summary>
    /// Every setting installed that can be played (what it requires is there, in a version new enough), the one played
    /// among them, in the order a pick lists them; one that cannot is a line in the problems
    /// </summary>
    public IReadOnlyList<ModInfo> Settings { get; private init; } = [];

    /// <summary>
    /// Mods installed but not played this run: the other settings, the add-ons for other settings only, and the add-ons
    /// that require them. Not problems: they come in with their setting
    /// </summary>
    public IReadOnlyList<ModInfo> Resting { get; private init; } = [];

    /// <summary>The core; a stand-in pointing at a folder that is not there when the install has lost it</summary>
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

    /// <summary>Only the core, from a folder of its own (tests, tools)</summary>
    public static ModSet BaseOnly(string baseFolder)
    {
        var info = ReadManifest(baseFolder, out _, out _) ?? new ModInfo();
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
    /// <param name="alsoFrom">
    /// Mod folders from elsewhere, read after the root's as if they were in it (a mod being made, the example mod in the
    /// repo's examples folder): tests and tools
    /// </param>
    /// <param name="setting">
    /// The setting to play, by id; the first installed when null or not installed (<see cref="PickSetting"/>), unless
    /// <paramref name="exactSetting"/>
    /// </param>
    /// <param name="coreOnly">The core alone, every other mod left as if it were not there (the settings still listed)</param>
    /// <param name="exactSetting">
    /// The setting asked for or none: the bare core when <paramref name="setting"/> is null, not installed or cannot be
    /// played, never another setting (a game's own)
    /// </param>
    /// <param name="everything">Every mod, every setting among them, none played and none resting: for lists, never to play</param>
    /// <param name="only">
    /// The mods to read, by id, the core always among them; the others left as if they were not there (tools and tests:
    /// a setting and the core without the developer's own mods)
    /// </param>
    public static ModSet Load(string root, ModVersion? gameVersion = null, IEnumerable<string>? alsoFrom = null, string? setting = null,
        bool coreOnly = false, bool exactSetting = false, bool everything = false, IReadOnlyCollection<string>? only = null)
    {
        var game = gameVersion ?? ProgramVersion;
        var problems = new List<string>();
        var mods = new List<ModInfo>();

        if (Directory.Exists(root))
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (folder, grouped) in ModFolders(root, problems).Concat((alsoFrom ?? []).Select(f => (f, false))))
            {
                var name = Path.GetFileName(folder);
                if (IsSwitchedOff(folder)) continue;

                // The core is the folder of its name at the top, never one in a group
                var isBase = !grouped && ModIds.IsBase(name);
                var info = ReadManifest(folder, out var problem, out var unknown);
                // A mod not asked for is not there: none of its problems told either
                if (only != null && !isBase && !only.Contains(string.IsNullOrWhiteSpace(info?.Id) ? name : info.Id, StringComparer.OrdinalIgnoreCase)) continue;
                if (problem != null)
                {
                    problems.Add($"{name}: {problem}");
                    if (!isBase) continue;
                }

                if (unknown.Count > 0)
                    problems.Add($"{name}: {ManifestFileName}: {string.Join(", ", unknown.Select(n => $"\"{n}\""))} is none of its fields or sections ({string.Join(", ", ModSections.All)}); not read");

                if (info == null)
                {
                    // The core is the core with or without its mod.json; any other folder needs one
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
                    problems.Add($"{name}: the id \"{info.Id}\" is the core's; left out");
                    continue;
                }

                if (seen.TryGetValue(info.Id, out var first))
                {
                    problems.Add($"{InContent(root, folder)}: {InContent(root, first)} is already mod \"{info.Id}\"; this one left out");
                    continue;
                }

                seen[info.Id] = folder;

                if (!string.IsNullOrWhiteSpace(info.Type) && !ModTypes.Addon.Equals(info.Type, StringComparison.OrdinalIgnoreCase) && !ModTypes.IsSetting(info.Type))
                    problems.Add($"{name}: \"type\": \"{info.Type}\" is neither \"{ModTypes.Addon}\" nor \"{ModTypes.Setting}\"; taken as an add-on");
                if (info.ForSettings.Count > 0 && (isBase || ModTypes.IsSetting(info.Type)))
                {
                    problems.Add($"{name}: \"settings\" is for an add-on (the settings it plays in); not read");
                    info.ForSettings = [];
                }
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
                ModIds.MarkSetting(info.Id, info.IsSetting);
                mods.Add(info);
            }
        }

        // An add-on's settings name installed ones: one naming none (a typo) would rest it in every game, untold
        var settingIds = new HashSet<string>(mods.Where(m => m.IsSetting).Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var addon in mods.Where(m => m.ForSettings.Count > 0 && !m.ForSettings.Any(settingIds.Contains)))
            problems.Add($"{Path.GetFileName(addon.Folder)}: \"settings\" names {string.Join(", ", addon.ForSettings.Select(i => $"\"{i}\""))}, no setting installed; it rests in every game");

        if (!mods.Any(m => m.IsBase)) problems.Add($"The core's content ({Path.Combine(root, ModIds.BaseId)}) is missing");

        // One setting plays; the others rest, and so does what plays in them only or requires them. A setting without what it requires is no
        // pick at all: picked, it would be left out and the game would play on the bare core
        var settings = mods.Where(m => m.IsSetting)
            .OrderBy(m => m.Order).ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Where(s => Playable(s, mods, problems))
            .ToList();
        var played = coreOnly || everything ? null
            : exactSetting ? settings.FirstOrDefault(m => m.Id.Equals(setting, StringComparison.OrdinalIgnoreCase))
            : PickSetting(settings, setting);
        var resting = everything ? []
            : coreOnly ? mods.Where(m => !m.IsBase).ToList()
            : RestingWith(mods, played);

        var others = WithWhatTheyNeed(mods.Where(m => !m.IsBase && !resting.Contains(m)).ToList(), problems)
            .OrderBy(m => m.Order).ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ordered = mods.Where(m => m.IsBase).Concat(AfterWhatTheyNeed(others, problems)).ToList();
        var set = new ModSet(root, ordered)
        {
            Setting = played != null && ordered.Contains(played) ? played : null,
            Settings = settings,
            Resting = resting
        };
        set._problems.AddRange(problems);
        set.ReadRemovals();
        return set;
    }

    /// <summary>
    /// The mod folders under the Content folder, by name: each folder with a mod.json (and the core's, with or without
    /// one), and the folders in a group, a folder with no mod.json of its own holding mod folders (<c>engines</c>,
    /// <c>settings</c>): one level, there only to keep the folder tidy. A group switched off is so with everything in it,
    /// and an empty one is passed over. A folder with no mod.json and no mod in it is passed on as it is, to be told it
    /// is not a mod; one with a mod.json and mods in it too (a pack unpacked without its own folder) is told.
    /// </summary>
    private static IEnumerable<(string Folder, bool Grouped)> ModFolders(string root, List<string> problems)
    {
        foreach (var folder in List(root, dirs: true, "*", problems).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (IsSwitchedOff(folder)) continue;

            var name = Path.GetFileName(folder);
            if (ModIds.IsBase(name))
            {
                yield return (folder, false);
                continue;
            }

            var inside = List(folder, dirs: true, "*", problems).Order(StringComparer.OrdinalIgnoreCase).ToList();
            var mods = inside.Where(f => !IsSwitchedOff(f) && File.Exists(Path.Combine(f, ManifestFileName))).ToList();
            if (File.Exists(Path.Combine(folder, ManifestFileName)))
            {
                if (mods.Count > 0)
                    problems.Add($"{name}: has a {ManifestFileName} and mods in it too ({string.Join(", ", mods.Select(Path.GetFileName))}); " +
                                 $"read as one mod, the mods in it left out (a {ManifestFileName} unpacked into the group?)");
                yield return (folder, false);
                continue;
            }

            if (mods.Count == 0)
            {
                // An empty group (its last mod taken out, or made for a pack still to come) is no problem
                if (inside.Count > 0 || List(folder, dirs: false, "*", problems).Count > 0) yield return (folder, false);
                continue;
            }

            foreach (var mod in inside) yield return (mod, true);
        }
    }

    /// <summary>A folder whose name starts with '_' or '.' is switched off: a mod, a group, a folder of a mod's content</summary>
    private static bool IsSwitchedOff(string folder)
    {
        var name = Path.GetFileName(folder);
        return name.StartsWith('_') || name.StartsWith('.');
    }

    /// <summary>A folder as it is in the Content folder (<c>engines\myengine</c>), for a problem line</summary>
    private static string InContent(string root, string folder)
    {
        var relative = Path.GetRelativePath(root, folder);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? folder : relative;
    }

    /// <summary>
    /// The setting a run plays: the one asked for when it is installed, else the first in the settings' order (their
    /// <c>order</c>, then id); null when none is installed (the bare core)
    /// </summary>
    public static ModInfo? PickSetting(IReadOnlyList<ModInfo> settings, string? wanted) =>
        settings.FirstOrDefault(m => m.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase)) ?? settings.FirstOrDefault();

    /// <summary>
    /// True when <paramref name="setting"/> has what it requires, with the add-ons that play in it but no other setting
    /// beside it (as <see cref="RestingWith"/> leaves them); told
    /// in <paramref name="problems"/> when it has not
    /// </summary>
    private static bool Playable(ModInfo setting, List<ModInfo> mods, List<string> problems)
    {
        var lines = new List<string>();
        if (WithWhatTheyNeed(mods.Where(m => !m.IsBase && (m.IsSetting ? m == setting : m.PlaysIn(setting))).ToList(), lines).Contains(setting)) return true;

        var name = Path.GetFileName(setting.Folder);
        var why = (lines.FirstOrDefault(l => l.StartsWith(name + ": ", StringComparison.Ordinal)) ?? lines.FirstOrDefault() ?? string.Empty)
            .Replace(name + ": ", string.Empty).Replace("; left out", string.Empty);
        problems.Add($"{name}: the setting cannot be played ({why}); not offered");
        return false;
    }

    /// <summary>
    /// The mods that rest while <paramref name="played"/> plays: every other setting and the add-ons for other settings
    /// only (<see cref="ModInfo.ForSettings"/>), then whatever requires a resting mod, in turn
    /// </summary>
    private static List<ModInfo> RestingWith(List<ModInfo> mods, ModInfo? played)
    {
        var resting = mods.Where(m => !m.IsBase && (m.IsSetting ? m != played : !m.PlaysIn(played))).ToList();
        if (resting.Count == 0) return resting;

        var ids = new HashSet<string>(resting.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        while (mods.FirstOrDefault(m => !m.IsBase && m != played && !ids.Contains(m.Id) && m.Requires.Keys.Any(ids.Contains)) is { } needsOne)
        {
            resting.Add(needsOne);
            ids.Add(needsOne.Id);
        }

        return resting;
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
    /// The mods in their order, each moved after the mods it requires and the settings it plays in (the order kept
    /// otherwise), so what it takes out or sets over theirs is theirs to take; mods that require each other round in a
    /// circle keep their order, told once
    /// </summary>
    private static List<ModInfo> AfterWhatTheyNeed(List<ModInfo> mods, List<string> problems)
    {
        var ids = new HashSet<string>(mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var waiting = mods.ToList();
        var result = new List<ModInfo>();
        while (waiting.Count > 0)
        {
            var next = waiting.FirstOrDefault(m => m.Requires.Keys.Concat(m.ForSettings).All(id =>
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

    private static ModInfo? ReadManifest(string folder, out string? problem, out List<string> unknown)
    {
        problem = null;
        unknown = [];
        var file = Path.Combine(folder, ManifestFileName);
        if (!File.Exists(file)) return null;

        try
        {
            var json = File.ReadAllText(file);
            var info = JsonSerializer.Deserialize<ModInfo>(json, ModJson.Options) ?? new ModInfo();
            unknown = ModInfo.UnknownNames(json);
            // "removes": null reads as no table at all. The reader makes its own tables, which match names in their case
            // only: they are made again whatever the case, so "Racers" is "racers" (a kind given twice keeps every id)
            var removes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (type, ids) in info.Removes ?? [])
            {
                if (ids == null) continue;
                if (removes.TryGetValue(type, out var listed)) listed.AddRange(ids);
                else removes[type] = [.. ids];
            }

            info.Removes = removes;
            var requires = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, version) in info.Requires ?? []) requires[id] = version;
            info.Requires = requires;
            info.ForSettings = (info.ForSettings ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
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

    /// <summary>
    /// Every mod with content of this kind, in load order: a folder for it, or sections of its mod.json for it
    /// (<see cref="ModSections.Of"/>), in which case the folder may not be there
    /// </summary>
    public IEnumerable<(ModInfo Mod, string Dir)> ContentOf(string type)
    {
        var sections = ModSections.Of(type);
        foreach (var mod in Mods)
        {
            if (mod.Dir(type) is { } dir) yield return (mod, dir);
            else if (sections.Any(s => mod.Section(s) != null)) yield return (mod, Path.Combine(mod.Folder, type));
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
                if (!IsSwitchedOff(sub)) yield return (mod, sub);
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

/// <summary>
/// A value read from the content in play (<see cref="ModSet.Installed"/>), kept until another content is played and
/// then read again on its next use. The static books of the mods' content keep theirs in one, so none outlives a
/// switch from the main screen's core to a game's setting and back. Thread-safe; the value is read once per content.
/// </summary>
public sealed class ContentCache<T>
{
    private readonly Func<T> _load;
    private readonly object _lock = new();
    private ModSet? _of;
    private T? _value;

    public ContentCache(Func<T> load) => _load = load;

    public T Value
    {
        get
        {
            lock (_lock)
            {
                while (true)
                {
                    var mods = ModSet.Installed;
                    if (ReferenceEquals(_of, mods)) return _value!;

                    // The load reads the content in play itself: one switched while it read is read again, so a value is
                    // never kept for a content it was not read from
                    var value = _load();
                    if (!ReferenceEquals(ModSet.Installed, mods)) continue;

                    _value = value;
                    _of = mods;
                    return value;
                }
            }
        }
    }

    /// <summary>The value when it is read already for the content in play; the default (it is not read) otherwise</summary>
    public T? IfMade
    {
        get
        {
            var mods = ModSet.Installed;
            lock (_lock) return ReferenceEquals(_of, mods) ? _value : default;
        }
    }

}
