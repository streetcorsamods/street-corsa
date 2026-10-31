using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using StreetCorsa.Logging;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Race
{
    /// <summary>
    /// The kinds of race, from every mod's <c>formats</c> folder (<c>formats.json</c>: <c>{ "formats": [ ... ] }</c>),
    /// merged by id. A race, an event and a street offer name their format by id; one written before formats (the
    /// game's old race types, <c>DragRace</c>) is found by its alias.
    ///
    /// A format runs in the sc_race mode, which loads its Lua module (<c>format_&lt;module&gt;.lua</c>): the mode's own,
    /// or one a mod ships as <c>formats/&lt;module&gt;.lua</c>, put into the mode's folder in AC as
    /// <c>format_&lt;mod&gt;-&lt;module&gt;.lua</c>. A mod may instead ship a whole CSP mode (<c>formats/&lt;mode&gt;/</c>),
    /// put into AC as <c>&lt;mod&gt;-&lt;mode&gt;</c>; it writes its result where sc_race does, as sc_race does. The '-'
    /// is in no module or mode name, and in none of the game's own files or modes: the last one splits a name in two
    /// one way only, so no two formats share a file or a folder, and none is the game's.
    /// </summary>
    public sealed partial class RaceFormats
    {
        /// <summary>A module or mode's name: lower case letters, digits and '_'</summary>
        [GeneratedRegex("^[a-z0-9_]{1,40}$")]
        private static partial Regex NamePattern();

        /// <summary>The prefix of a format module's file in the mode's folder</summary>
        public const string ModulePrefix = "format_";

        /// <summary>
        /// The module a test-and-tune runs in: sc_race's own drag module, whatever a mod made of the drag format (the
        /// launcher installs no other mode for one)
        /// </summary>
        public const string TuneModule = ModulePrefix + "drag";

        /// <summary>Between a mod's id and its module or mode's name in AC: in no module or mode name</summary>
        public const char ModSeparator = '-';

        private static readonly ContentCache<RaceFormats> InstalledFormats = new(() => Load(ModSet.Installed, ScRaceMode.DefaultSourceFolder));
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

        /// <summary>
        /// What a street race of a family is run as: the last format of it raced on the street, in the mods' order (a
        /// setting's own over the core's), else its first
        /// </summary>
        public RaceFormat? Street(string family) => _formats.LastOrDefault(f => f.Family == family && f.Street) ?? First(family);

        /// <summary>The formats the diner offers on a family's tracks, the first the one it starts on</summary>
        public IReadOnlyList<RaceFormat> ForDiner(string family) => _formats.Where(f => f.Family == family && f.Diner).ToList();

        /// <summary>
        /// The text key a format's own words go by (<c>key.&lt;id&gt;</c>) when a mod has it: its id in full
        /// ("my_mod:drift"), then as its mod wrote it ("drift") when no other format goes by that; null when neither is
        /// there. Two mods' formats of one name never share their words, nor take the core's.
        /// </summary>
        public string? OwnKey(string key, RaceFormat format)
        {
            var text = GameText.Installed;
            var full = key + "." + format.Id;
            if (text.Has(full)) return full;

            var local = ModIds.Local(format.Id);
            if (local == format.Id || _formats.Any(f => !ReferenceEquals(f, format) && ModIds.Local(f.Id) == local)) return null;
            var written = key + "." + local;
            return text.Has(written) ? written : null;
        }

        /// <summary>The format's own words for a key when a mod has them (<see cref="OwnKey"/>), else the key's own</summary>
        public string SayFor(RaceFormat format, string key, params (string Name, object? Value)[] values) =>
            GameText.Say(OwnKey(key, format) ?? key, values);

        /// <summary>
        /// A format's name as the paper, the diner and the score card write it: its <c>name</c> key, else its own
        /// <c>paper.race_type.&lt;id&gt;</c>; a bracket race is one of its own, and a format without a name is a race
        /// </summary>
        public string NameOf(RaceFormat? format, bool bracket = false)
        {
            if (bracket) return GameText.Say("paper.race_type.bracket");
            var key = format == null ? null
                : !string.IsNullOrWhiteSpace(format.Name) ? (GameText.Installed.Has(format.Name) ? format.Name : null)
                : OwnKey("paper.race_type", format);
            return GameText.Say(key ?? "paper.race_type.other");
        }

        /// <summary>A format's name by its id (<see cref="NameOf(RaceFormat?, bool)"/>)</summary>
        public string NameOf(string? id, bool bracket = false) => NameOf(Get(id), bracket);

        /// <summary>The ids of the modes a result may come from: sc_race and every format's own</summary>
        public IEnumerable<string> ModeIds => _formats.Where(f => f.Mode != null).Select(ModeIdOf).Prepend(Configuration.IniModificationService.RaceModeId).Distinct();

        /// <summary>The CSP mode a format runs in: sc_race, or the mod's own (its folder name in AC)</summary>
        public static string ModeIdOf(RaceFormat format) =>
            format.Mode is { } mode ? format.ModId + ModSeparator + mode : Configuration.IniModificationService.RaceModeId;

        /// <summary>The module sc_race loads for a format (its file's name without .lua)</summary>
        public static string ModuleOf(RaceFormat format) =>
            OwnModule(format) != null ? ModulePrefix + format.ModId + ModSeparator + format.Module : ModulePrefix + format.Module;

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
        public static RaceFormats From(IEnumerable<RaceFormat> formats)
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

            format.Better = StreetCorsa.Rules.Figures.OneOf(format.Better, $"{where}: better", "lower", ["lower", "higher"], problems);
            format.ScoreShows = StreetCorsa.Rules.Figures.OneOf(format.ScoreShows, $"{where}: scoreShows", "points", ["time", "points"], problems);

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
            if (format.Family == RaceFormatFamilies.Drag && format.Laps != 1)
            {
                problems.Add($"{where}: a drag race is one run down the strip, not {format.Laps} laps; it has 1");
                format.Laps = 1;
            }
            if (format.SessionLaps is { } sessionLaps) format.SessionLaps = Math.Clamp(sessionLaps, 1, MaxSessionLaps);

            if (format.Bracket && format.Family != RaceFormatFamilies.Drag)
            {
                problems.Add($"{where}: only a drag race can be a bracket race");
                format.Bracket = false;
            }

            if (format.Rivals > 1 && format.Family == RaceFormatFamilies.Drag)
            {
                problems.Add($"{where}: a drag race is one on one (two lanes)");
                format.Rivals = 1;
            }
            format.Rivals = Math.Clamp(format.Rivals, 1, MaxRivals);

            Marks(format, where, problems);

            format.Session = string.IsNullOrWhiteSpace(format.Session) ? "Quick Race" : format.Session.Trim();
            format.Aliases = (format.Aliases ?? []).OfType<string>().ToList();
            return format;
        }

        /// <summary>The number of a drag strip's timing marks (<see cref="RaceFormat.Marks"/>)</summary>
        public const int DragMarks = 5;

        /// <summary>
        /// A strip's marks as the race mode takes them, or none: five, each past the one before and past the stage beam,
        /// with a trap that starts past the stage beam before the third mark (the trap's start is a mark the mode times
        /// too). Marks without a trap get the quarter mile's, 66 ft. Anything else is the quarter mile, told.
        /// </summary>
        private static void Marks(RaceFormat format, string where, List<string> problems)
        {
            if (format.Marks == null && format.Trap == null) return;

            var marks = format.Marks ?? [];
            var trap = format.Trap ?? (format.Marks != null ? QuarterTrapMetres : 0);
            var fine = format.Family == RaceFormatFamilies.Drag && marks.Count == DragMarks && marks.All(double.IsFinite) && double.IsFinite(trap)
                       && marks[0] > StageMetres && trap > 0 && marks[2] - trap > StageMetres;
            for (var i = 1; fine && i < marks.Count; i++) fine = marks[i] > marks[i - 1];
            if (fine)
            {
                format.Trap = trap;
                return;
            }

            problems.Add(format.Family != RaceFormatFamilies.Drag
                ? $"{where}: only a drag strip has marks; left out"
                : $"{where}: marks are {DragMarks} metres down the strip, each past the one before, with a trap that starts past the stage beam short of the third; the quarter mile");
            format.Marks = null;
            format.Trap = null;
        }

        // The race mode's stage beam, metres past the line (format_drag.lua STAGE_METRES)
        private const double StageMetres = 0.2;

        /// <summary>The quarter-mile strip's trap, 66 ft in metres: the trap of marks that give none</summary>
        public const double QuarterTrapMetres = 66 * 0.3048;

        /// <summary>The most laps a race may be over</summary>
        public const int MaxLaps = 50;

        /// <summary>The most rivals one race may have against the player (a field: the player and these on AC's grid)</summary>
        public const int MaxRivals = 20;

        /// <summary>The most laps AC's session may be given</summary>
        public const int MaxSessionLaps = 99;
    }
}
