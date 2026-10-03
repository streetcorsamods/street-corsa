namespace StreetCorsa.Models.Race
{
    /// <summary>
    /// A kind of race, from the mods' <c>formats</c> folder (<see cref="Services.Race.RaceFormats"/>): what the race mode
    /// runs, how many laps, who wins, and where the game offers it. The base game has drag, circuit, sprint and time
    /// attack; a mod adds its own, with a Lua module the race mode loads, or a whole CSP mode of its own.
    /// </summary>
    public sealed class RaceFormat
    {
        /// <summary>The full id (a mod's has its namespace); set when read</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The mod it came from; set when read</summary>
        public string ModId { get; set; } = string.Empty;

        /// <summary>The mod's folder, where its module or mode is; set when read</summary>
        public string ModFolder { get; set; } = string.Empty;

        /// <summary>
        /// <see cref="RaceFormatFamilies.Drag"/> or <see cref="RaceFormatFamilies.Road"/>: which tracks it runs on (the
        /// strips, or the rest), and what the career counts it as (the drag or road record, wagers, milestones)
        /// </summary>
        public string Family { get; set; } = RaceFormatFamilies.Road;

        /// <summary>
        /// The sc_race module that runs it: one of the mode's own (<c>drag</c>, <c>road</c>, <c>time_attack</c>), or a
        /// file in the mod's <c>formats</c> folder (<c>drift</c> is <c>formats/drift.lua</c>)
        /// </summary>
        public string Module { get; set; } = "road";

        /// <summary>
        /// A whole CSP mode of the mod's own instead of a module: its folder in the mod's <c>formats</c> folder, put into
        /// AC as <c>&lt;mod&gt;-&lt;mode&gt;</c>. It reads race.ini as sc_race does and writes the same result.
        /// </summary>
        public string? Mode { get; set; }

        /// <summary>The laps the race is over, unless an event says otherwise</summary>
        public int Laps { get; set; } = 1;

        /// <summary>
        /// The laps AC's own session has, when the format runs past its laps (an out lap, a wait for the rival): more
        /// than the race ever runs, so AC never ends it before the mode does. None: <see cref="Laps"/>.
        /// </summary>
        public int? SessionLaps { get; set; }

        /// <summary>The session's name in AC</summary>
        public string Session { get; set; } = "Quick Race";

        /// <summary>The police may come (a road race on the street)</summary>
        public bool Police { get; set; }

        /// <summary>It can be run as a bracket race (dial-ins): drag only</summary>
        public bool Bracket { get; set; }

        /// <summary><see cref="RaceFormatWins.Position"/> (first over the line) or <see cref="RaceFormatWins.Score"/></summary>
        public string Win { get; set; } = RaceFormatWins.Position;

        /// <summary>For a score: <c>lower</c> wins (a lap time) or <c>higher</c> (points)</summary>
        public string Better { get; set; } = "lower";

        /// <summary>How a score shows: <c>time</c> (milliseconds, shown as a lap time) or <c>points</c></summary>
        public string ScoreShows { get; set; } = "points";

        /// <summary>Its name's text key; none: <c>paper.race_type.&lt;id&gt;</c> (<see cref="Services.Race.RaceFormats.NameOf(RaceFormat?, bool)"/>)</summary>
        public string? Name { get; set; }

        /// <summary>The ids it was known by before (the base game's race types: <c>DragRace</c>, <c>Circuit</c>, <c>Sprint</c>)</summary>
        public List<string> Aliases { get; set; } = [];

        /// <summary>Offered at the diner, on the tracks of its family</summary>
        public bool Diner { get; set; } = true;

        /// <summary>Raced on the street (the cruise's encounters); the first of each family is the one raced</summary>
        public bool Street { get; set; }

        public bool IsDrag => Family == RaceFormatFamilies.Drag;

        public bool ByScore => Win == RaceFormatWins.Score;

        public bool HigherWins => string.Equals(Better, "higher", StringComparison.OrdinalIgnoreCase);

        public bool ScoreIsTime => string.Equals(ScoreShows, "time", StringComparison.OrdinalIgnoreCase);

        /// <summary>The laps AC's session is given</summary>
        public int AcLaps(int laps) => Math.Max(laps, SessionLaps ?? laps);

        /// <summary>
        /// Run over more than one lap of AC's (a time attack's warm-up lap and its laps): only on a layout that comes back
        /// round, never on one raced from A to B (<see cref="AC.TrackInfo.IsPointToPoint"/>). A drag race never laps.
        /// </summary>
        public bool NeedsLoop(int? laps = null) => !IsDrag && AcLaps(laps ?? Laps) > 1;
    }

    /// <summary>What a format counts as</summary>
    public static class RaceFormatFamilies
    {
        public const string Drag = "drag";
        public const string Road = "road";
    }

    /// <summary>How a format is won</summary>
    public static class RaceFormatWins
    {
        /// <summary>The racers' order at the line (or the bracket's rules)</summary>
        public const string Position = "position";

        /// <summary>Each racer's score in the result (participants[].score), <see cref="RaceFormat.Better"/></summary>
        public const string Score = "score";
    }
}
