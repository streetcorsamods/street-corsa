using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Race.Traffic;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// The Challenges of the mods (<c>challenges/*.json</c>): runs of roads driven in order in a format run alone for the
    /// score, each road one run, the Challenge's score the sum of its runs. Merged from every mod by id, a mod's ids
    /// namespaced as the rest. This reads them and tells what does not read; the screens, the medals and the unlocks that
    /// play them build on it.
    /// </summary>
    public sealed class ChallengeBook
    {
        private static IAppLogger Logger => LazyLogger.Value;

        private static readonly Lazy<IAppLogger> LazyLogger = new(() => AppLoggerFactory.CreateLogger("Challenges"));

        private static readonly ContentCache<ChallengeBook> InstalledBook = new(() => Load(ModSet.Installed));

        /// <summary>The Challenges in the order the mods list them</summary>
        public IReadOnlyList<ChallengeDefinition> All { get; private init; } = [];

        public IReadOnlyList<string> Problems { get; private init; } = [];

        /// <summary>The Challenges of the mods next to the game</summary>
        public static ChallengeBook Installed => InstalledBook.Value;

        public ChallengeDefinition? Get(string? id) =>
            id == null ? null : All.FirstOrDefault(c => c.Id.Equals(ModIds.Normalize(id.Trim()), StringComparison.OrdinalIgnoreCase));

        /// <param name="raceFormats">The formats the Challenges name; none: those of the same mods</param>
        public static ChallengeBook Load(ModSet mods, RaceFormats? raceFormats = null)
        {
            var formats = raceFormats ?? (ReferenceEquals(mods, ModSet.Installed) ? RaceFormats.Installed
                : RaceFormats.Load(mods, ScRaceMode.DefaultSourceFolder));
            var problems = new List<string>();

            var registry = ModJson.Load(mods, ContentTypes.Challenges, "challenges", "challenge", problems, entry =>
            {
                var challenge = entry.Element.Deserialize<ChallengeDefinition>(ModJson.Options);
                return challenge == null ? null : Tidy(challenge, entry, formats, problems);
            });

            var list = new List<ChallengeDefinition>();
            foreach (var (id, challenge, mod) in registry.Entries)
            {
                challenge.Id = id;
                challenge.ModId = mod.Id;
                if (challenge.Unlock?.Challenge is { Length: > 0 } after)
                {
                    challenge.Unlock.Challenge = registry.Resolve(mod, after);
                    if (!registry.Contains(challenge.Unlock.Challenge))
                        problems.Add($"{mod.Id}: challenge {id} opens after challenge {after}, which no mod has: it never opens");
                }

                list.Add(challenge);
            }

            foreach (var line in registry.Replaced) Logger.Information("{Line}", line);
            foreach (var problem in problems) Logger.Warning("Challenges: {Problem}", problem);
            ModProblems.Add("Challenges", problems);
            Logger.Information("Challenges: {Count}, {Roads} roads", list.Count, list.Sum(c => c.Roads.Count));
            return new ChallengeBook { All = list, Problems = problems };
        }

        private static ChallengeDefinition? Tidy(ChallengeDefinition challenge, ModEntry entry, RaceFormats formats, List<string> problems)
        {
            var where = $"{entry.Source}: challenge {entry.Id}";

            // The format the mod's own, else the game's; none written: the last format run alone, as the hub's Free Drive
            var written = challenge.Format?.Trim();
            var format = string.IsNullOrEmpty(written) ? formats.Solo
                : formats.Get(ModIds.Resolve(entry.Mod.Id, written, id => formats.Get(id) != null));
            if (format == null)
            {
                problems.Add(string.IsNullOrEmpty(written)
                    ? $"{where} names no format and no mod has one run alone; left out"
                    : $"{where} is run as \"{written}\", a format no mod has; left out");
                return null;
            }

            if (!format.Solo)
            {
                problems.Add($"{where} is run as {format.Id}, which is not run alone for the score (\"solo\"); left out");
                return null;
            }

            challenge.Format = format.Id;
            if (string.IsNullOrWhiteSpace(challenge.Name))
            {
                problems.Add($"{where} has no name; its id is shown");
                challenge.Name = entry.Id ?? string.Empty;
            }

            var roads = new List<ChallengeRoad>();
            foreach (var (road, i) in (challenge.Roads ?? []).Select((r, i) => (r, i + 1)))
            {
                if (road == null) continue;
                var at = $"{where}, road {i}";
                if (string.IsNullOrWhiteSpace(road.Track))
                {
                    problems.Add($"{at} names no track; left out");
                    continue;
                }

                road.Track = road.Track.Trim();
                road.Layout = string.IsNullOrWhiteSpace(road.Layout) ? null : road.Layout.Trim();
                road.Weather = string.IsNullOrWhiteSpace(road.Weather) ? null : road.Weather.Trim();
                if (road.Laps is { } laps && laps is < 1 or > RaceFormats.MaxLaps)
                {
                    problems.Add($"{at}: {laps} laps; it has {Math.Clamp(laps, 1, RaceFormats.MaxLaps)}");
                    road.Laps = Math.Clamp(laps, 1, RaceFormats.MaxLaps);
                }

                if (road.Time is { Length: > 0 } time)
                {
                    road.Minute = ChallengeRoad.ReadTime(time);
                    if (road.Minute == null) problems.Add($"{at}: time \"{time}\" is not a time of day (\"17:30\"); the game's hour is driven");
                }

                if (road.TrafficPerLaneKm is { } rate && !(double.IsFinite(rate) && rate is >= ChallengeRoad.LeastPerLaneKm and <= ChallengeRoad.MostPerLaneKm))
                {
                    problems.Add($"{at}: trafficPerLaneKm {rate} is not {ChallengeRoad.LeastPerLaneKm} to {ChallengeRoad.MostPerLaneKm}; the rules' is used");
                    road.TrafficPerLaneKm = null;
                }

                if (road.TrafficCars is { } cars && cars is < TrafficSetup.LeastCars or > TrafficSetup.MostCars)
                {
                    problems.Add($"{at}: trafficCars {cars} is not {TrafficSetup.LeastCars} to {TrafficSetup.MostCars}; it has {Math.Clamp(cars, TrafficSetup.LeastCars, TrafficSetup.MostCars)}");
                    road.TrafficCars = Math.Clamp(cars, TrafficSetup.LeastCars, TrafficSetup.MostCars);
                }

                if (road.Medals is { } medals && !medals.InOrder)
                {
                    problems.Add($"{at}: its medals' scores are not above 0, bronze to gold rising; worked out from the road");
                    road.Medals = null;
                }

                roads.Add(road);
            }

            if (roads.Count == 0)
            {
                problems.Add($"{where} has no road; left out");
                return null;
            }

            challenge.Roads = roads;
            if (challenge.Unlock is { } unlock)
            {
                if (unlock.Score is < 0)
                {
                    problems.Add($"{where}: unlock score {unlock.Score} is below 0; 0 is used");
                    unlock.Score = 0;
                }

                if (unlock.Gold is < 0)
                {
                    problems.Add($"{where}: unlock gold {unlock.Gold} is below 0; 0 is used");
                    unlock.Gold = 0;
                }

                unlock.Challenge = string.IsNullOrWhiteSpace(unlock.Challenge) ? null : unlock.Challenge.Trim();
                if (unlock.IsOpen) challenge.Unlock = null;
            }

            return challenge;
        }
    }

    /// <summary>A Challenge as a mod writes it (challenges/&lt;id&gt;.json)</summary>
    public sealed class ChallengeDefinition
    {
        /// <summary>The full id (the mod's namespace on it), set when read</summary>
        [JsonIgnore]
        public string Id { get; set; } = string.Empty;

        [JsonIgnore]
        public string ModId { get; set; } = string.Empty;

        /// <summary>What the screens call it</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The part of the world its roads are in ("Europe"), as the list groups them</summary>
        public string? Region { get; set; }

        /// <summary>A line or two about it</summary>
        public string? Blurb { get; set; }

        /// <summary>Its picture, a path in the mod's folder</summary>
        public string? Picture { get; set; }

        /// <summary>The format its roads are run in (a solo one), resolved when read; none written: the last solo format</summary>
        public string? Format { get; set; }

        /// <summary>The roads in the order they are driven</summary>
        public List<ChallengeRoad> Roads { get; set; } = [];

        /// <summary>What opens it; none: open from the start</summary>
        public ChallengeUnlock? Unlock { get; set; }
    }

    /// <summary>One road of a Challenge: one run</summary>
    public sealed class ChallengeRoad
    {
        /// <summary>The traffic per lane-km a road may ask for: the rules' own bounds</summary>
        public const double LeastPerLaneKm = 0.5;

        public const double MostPerLaneKm = 20;

        /// <summary>The AC track's folder</summary>
        public string Track { get; set; } = string.Empty;

        /// <summary>Its layout's folder; none: the track's only one (or its first)</summary>
        public string? Layout { get; set; }

        /// <summary>The laps; none: the format's</summary>
        public int? Laps { get; set; }

        /// <summary>The time of day, "17:30"; none: the game's hour</summary>
        public string? Time { get; set; }

        /// <summary><see cref="Time"/> read, in minutes from midnight</summary>
        [JsonIgnore]
        public int? Minute { get; set; }

        /// <summary>AC's weather (its folder, "3_clear"); none: the rules'</summary>
        public string? Weather { get; set; }

        /// <summary>Traffic per lane-km, over the rules' <c>race.trafficPerLaneKm</c></summary>
        public double? TrafficPerLaneKm { get; set; }

        /// <summary>The traffic's size, over what the road's reach makes of it (the player's density still scales it)</summary>
        public int? TrafficCars { get; set; }

        /// <summary>The medals' scores pinned by hand; none: worked out from the road</summary>
        public ChallengeMedals? Medals { get; set; }

        /// <summary>"17:30" (or "7:05") in minutes from midnight; null when it is no time of day</summary>
        public static int? ReadTime(string text) =>
            TimeOnly.TryParseExact(text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                ? time.Hour * 60 + time.Minute
                : null;
    }

    /// <summary>A road's medals, each the score that wins it</summary>
    public sealed class ChallengeMedals
    {
        public long Bronze { get; set; }

        public long Silver { get; set; }

        public long Gold { get; set; }

        [JsonIgnore]
        public bool InOrder => Bronze > 0 && Silver >= Bronze && Gold >= Silver;
    }

    /// <summary>What opens a Challenge: all that is given, together</summary>
    public sealed class ChallengeUnlock
    {
        /// <summary>A Challenge finished first (its id, resolved when read)</summary>
        public string? Challenge { get; set; }

        /// <summary>The career's banked score</summary>
        public long? Score { get; set; }

        /// <summary>Gold medals won</summary>
        public int? Gold { get; set; }

        [JsonIgnore]
        public bool IsOpen => Challenge == null && Score is null or 0 && Gold is null or 0;
    }
}
