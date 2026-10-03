using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using StreetCorsa.Logging;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Race
{
    /// <summary>
    /// The kinds of race, from every mod's <c>formats</c> folder (<c>formats.json</c>: <c>{ "formats": [ ... ] }</c>),
    /// merged by id. A race, an event and a street offer name their format by id; one written before formats (the
    /// base game's race types, <c>DragRace</c>) is found by its alias.
    ///
    /// A format runs in the sc_race mode, which loads its Lua module (<c>format_&lt;module&gt;.lua</c>): the mode's own,
    /// or one a mod ships as <c>formats/&lt;module&gt;.lua</c>, put into the mode's folder in AC as
    /// <c>format_&lt;mod&gt;_&lt;module&gt;.lua</c>. A mod may instead ship a whole CSP mode (<c>formats/&lt;mode&gt;/</c>),
    /// put into AC as <c>&lt;mod&gt;_&lt;mode&gt;</c>; it writes its result where sc_race does, as sc_race does.
    /// </summary>
    public sealed partial class RaceFormats
    {
        /// <summary>A module or mode's name: lower case letters, digits and '_'</summary>
        [GeneratedRegex("^[a-z0-9_]{1,40}$")]
        private static partial Regex NamePattern();

        /// <summary>The prefix of a format module's file in the mode's folder</summary>
        public const string ModulePrefix = "format_";

        private static readonly Lazy<RaceFormats> InstalledFormats = new(() => Load(ModSet.Installed, ScRaceMode.DefaultSourceFolder));
        private static RaceFormats? _current;

        // The logger only when the formats are read: a test that never reads them never makes one
        private static IAppLogger Logger => LazyLogger.Value;
        private static readonly Lazy<IAppLogger> LazyLogger = new(() => AppLoggerFactory.CreateLogger("RaceFormats"));

        /// <summary>The formats of the mods next to the game, read once</summary>
        public static RaceFormats Installed => InstalledFormats.Value;

        /// <summary>The formats in use: the installed ones, or ones a test sets</summary>
        public static RaceFormats Current
        {
            get => _current ?? Installed;
            set => _current = value;
        }

        /// <summary>A road race as the game ran every race before formats: what a race of a format no mod has any more is run as</summary>
        public static RaceFormat Fallback { get; } = new() { Id = "circuit", ModId = ModIds.BaseId, Family = RaceFormatFamilies.Road, Module = "road" };

        private readonly List<RaceFormat> _formats;
        private readonly Dictionary<string, RaceFormat> _byId;

        private RaceFormats(List<RaceFormat> formats, Dictionary<string, RaceFormat> byId, List<string> problems)
        {
            _formats = formats;
            _byId = byId;
            Problems = problems;
        }

        public IReadOnlyList<RaceFormat> All => _formats;

        public IReadOnlyList<string> Problems { get; }

        /// <summary>A format by its id or an alias, whatever the case; null when no mod has it</summary>
        public RaceFormat? Get(string? id) => id != null && _byId.TryGetValue(ModIds.Normalize(id.Trim()), out var format) ? format : null;

        /// <summary>A format by its id, or the first road format (else <see cref="Fallback"/>) for one no mod has any more</summary>
        public RaceFormat Of(string? id) => Get(id) ?? First(RaceFormatFamilies.Road) ?? Fallback;

        /// <summary>The id a format is kept by, for an id or an alias; the id as it is when no mod has it</summary>
        public string IdOf(string? id) => Get(id)?.Id ?? id ?? string.Empty;

        /// <summary>Whether a format is a drag race; one no mod has is not</summary>
        public bool IsDrag(string? id) => Get(id)?.IsDrag == true;

        /// <summary>The first format of a family, in the mods' order</summary>
        public RaceFormat? First(string family) => _formats.FirstOrDefault(f => f.Family == family);

        /// <summary>What a street race of a family is run as: the first format of it raced on the street, else its first</summary>
        public RaceFormat? Street(string family) => _formats.FirstOrDefault(f => f.Family == family && f.Street) ?? First(family);

        /// <summary>The formats the diner offers on a family's tracks, the first the one it starts on</summary>
        public IReadOnlyList<RaceFormat> ForDiner(string family) => _formats.Where(f => f.Family == family && f.Diner).ToList();

        /// <summary>The ids of the modes a result may come from: sc_race and every format's own</summary>
        public IEnumerable<string> ModeIds => _formats.Where(f => f.Mode != null).Select(ModeIdOf).Prepend(Configuration.IniModificationService.RaceModeId).Distinct();

        /// <summary>The CSP mode a format runs in: sc_race, or the mod's own (its folder name in AC)</summary>
        public static string ModeIdOf(RaceFormat format) =>
            format.Mode is { } mode ? format.ModId + "_" + mode : Configuration.IniModificationService.RaceModeId;

        /// <summary>The module sc_race loads for a format (its file's name without .lua)</summary>
        public static string ModuleOf(RaceFormat format) =>
            OwnModule(format) != null ? ModulePrefix + format.ModId + "_" + format.Module : ModulePrefix + format.Module;

        /// <summary>A format's module in its mod's folder; null when it is one of the mode's own</summary>
        public static string? OwnModule(RaceFormat format)
        {
            if (ModIds.IsBase(format.ModId) || format.Mode != null || string.IsNullOrEmpty(format.ModFolder)) return null;

            var path = Path.Combine(format.ModFolder, ContentTypes.Formats, format.Module + ".lua");
            return File.Exists(path) ? path : null;
        }

        /// <summary>The files sc_race needs besides its own: every mod's format module, by the name it has in the mode's folder</summary>
        public IEnumerable<(string Name, string Source)> ModuleFiles =>
            _formats.Select(f => (Format: f, Source: OwnModule(f))).Where(m => m.Source != null)
                .Select(m => (ModuleOf(m.Format) + ".lua", m.Source!)).DistinctBy(m => m.Item1, StringComparer.OrdinalIgnoreCase);

        /// <summary>The folder a format's own mode comes from; null for one run in sc_race</summary>
        public static string? ModeFolder(RaceFormat format) =>
            format.Mode is { } mode && !string.IsNullOrEmpty(format.ModFolder) ? Path.Combine(format.ModFolder, ContentTypes.Formats, mode) : null;

        /// <param name="modeFolder">sc_race as the game ships it: where the mode's own modules are</param>
        public static RaceFormats Load(ModSet mods, string modeFolder)
        {
            var problems = new List<string>();
            var registry = ModJson.Load(mods, ContentTypes.Formats, "formats", "race format", problems, entry => Read(entry, problems, modeFolder));

            var formats = new List<RaceFormat>();
            var byId = new Dictionary<string, RaceFormat>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, format, mod) in registry.Entries)
            {
                format.Id = id;
                format.ModId = mod.Id;
                format.ModFolder = mod.Folder;
                formats.Add(format);
                byId[id] = format;
            }

            // The aliases after every id: an id always means its own format
            foreach (var format in formats)
            {
                foreach (var alias in format.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()))
                {
                    if (byId.TryGetValue(alias, out var other) && !ReferenceEquals(other, format))
                        problems.Add($"{format.ModId}: race format {format.Id}'s alias \"{alias}\" is {other.Id}'s already; left out");
                    else
                        byId[alias] = format;
                }
            }

            foreach (var line in registry.Replaced) Logger.Information("{Line}", line);
            foreach (var problem in problems) Logger.Warning("Race formats: {Problem}", problem);
            ModProblems.Add("Race formats", problems);
            Logger.Information("Race formats: {Formats}", string.Join(", ", formats.Select(f => f.Id)));
            return new RaceFormats(formats, byId, problems);
        }

        /// <summary>A book of the given formats, for tests and tools</summary>
        public static RaceFormats Of(IEnumerable<RaceFormat> formats)
        {
            var list = formats.ToList();
            var byId = new Dictionary<string, RaceFormat>(StringComparer.OrdinalIgnoreCase);
            foreach (var format in list) byId[format.Id] = format;
            foreach (var format in list)
                foreach (var alias in format.Aliases) byId.TryAdd(alias, format);
            return new RaceFormats(list, byId, []);
        }

        private static RaceFormat? Read(ModEntry entry, List<string> problems, string modeFolder)
        {
            var format = entry.Element.Deserialize<RaceFormat>(ModJson.Options);
            if (format == null) return null;

            var where = $"{entry.Source}: race format {entry.Id}";
            format.Family = format.Family?.Trim().ToLowerInvariant() ?? string.Empty;
            if (format.Family is not (RaceFormatFamilies.Drag or RaceFormatFamilies.Road))
            {
                problems.Add($"{where}: family \"{format.Family}\" is neither drag nor road; left out");
                return null;
            }

            format.Win = format.Win?.Trim().ToLowerInvariant() ?? RaceFormatWins.Position;
            if (format.Win is not (RaceFormatWins.Position or RaceFormatWins.Score))
            {
                problems.Add($"{where}: win \"{format.Win}\" is neither position nor score; left out");
                return null;
            }

            format.Better = string.IsNullOrWhiteSpace(format.Better) ? "lower" : format.Better.Trim().ToLowerInvariant();
            if (format.Better is not ("lower" or "higher"))
            {
                problems.Add($"{where}: better \"{format.Better}\" is neither lower nor higher; lower wins");
                format.Better = "lower";
            }

            format.ScoreShows = string.IsNullOrWhiteSpace(format.ScoreShows) ? "points" : format.ScoreShows.Trim().ToLowerInvariant();
            if (format.ScoreShows is not ("time" or "points"))
            {
                problems.Add($"{where}: scoreShows \"{format.ScoreShows}\" is neither time nor points; points");
                format.ScoreShows = "points";
            }

            format.Module = string.IsNullOrWhiteSpace(format.Module) ? "road" : format.Module.Trim();
            if (!NamePattern().IsMatch(format.Module))
            {
                problems.Add($"{where}: module \"{format.Module}\" is not a name (a-z, 0-9, _); left out");
                return null;
            }

            format.Mode = string.IsNullOrWhiteSpace(format.Mode) ? null : format.Mode.Trim();
            if (format.Mode != null)
            {
                if (ModIds.IsBase(entry.Mod.Id) || !NamePattern().IsMatch(format.Mode)
                    || !File.Exists(Path.Combine(entry.Mod.Folder, ContentTypes.Formats, format.Mode, "mode.lua")))
                {
                    problems.Add($"{where}: mode \"{format.Mode}\" is not a folder with a mode.lua in the mod's formats folder; left out");
                    return null;
                }
            }
            else if (!File.Exists(Path.Combine(entry.Mod.Folder, ContentTypes.Formats, format.Module + ".lua"))
                     && Directory.Exists(modeFolder) && !File.Exists(Path.Combine(modeFolder, ModulePrefix + format.Module + ".lua")))
            {
                problems.Add($"{where}: no module \"{format.Module}\" (formats/{format.Module}.lua in the mod, or one of the race mode's own); left out");
                return null;
            }

            if (format.Laps is < 1 or > MaxLaps)
            {
                problems.Add($"{where}: {format.Laps} laps; it has {Math.Clamp(format.Laps, 1, MaxLaps)}");
                format.Laps = Math.Clamp(format.Laps, 1, MaxLaps);
            }
            if (format.SessionLaps is { } sessionLaps) format.SessionLaps = Math.Clamp(sessionLaps, 1, MaxSessionLaps);

            if (format.Bracket && format.Family != RaceFormatFamilies.Drag)
            {
                problems.Add($"{where}: only a drag race can be a bracket race");
                format.Bracket = false;
            }

            format.Session = string.IsNullOrWhiteSpace(format.Session) ? "Quick Race" : format.Session.Trim();
            format.Aliases = (format.Aliases ?? []).OfType<string>().ToList();
            return format;
        }

        /// <summary>The most laps a race may be over</summary>
        public const int MaxLaps = 50;

        /// <summary>The most laps AC's session may be given</summary>
        public const int MaxSessionLaps = 99;
    }
}
