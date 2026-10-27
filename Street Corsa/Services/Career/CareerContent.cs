using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Logging;
using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.Career.Filters;
using StreetCorsa.Models.Career.Milestones;
using StreetCorsa.Models.Career.Victory;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// The career as the mods define it: the invitations of the paper (<c>events</c>), the milestones and the ways to
    /// win (<c>victories</c>), each merged from every mod by id. A mod's ids get its namespace; what an event names
    /// (the milestones it asks for) is the mod's own first, then the game's (the core's or the setting's). Read once.
    /// </summary>
    public sealed class CareerContent
    {
        // The logger only when the content is read, not in a static field: a save loaded before the log is up (a tool,
        // a test) makes a new career, which touches this type
        private static IAppLogger Logger => LazyLogger.Value;

        private static readonly Lazy<IAppLogger> LazyLogger = new(() => AppLoggerFactory.CreateLogger("Career"));

        private static readonly ContentCache<CareerContent> InstalledContent = new(() => Load(ModSet.Installed));

        private static readonly JsonSerializerOptions Options = new(ModJson.Options)
        {
            Converters = { new JsonStringEnumConverter(), new CarFilterJsonConverter() }
        };

        public IReadOnlyDictionary<string, RaceEventDefinition> Events { get; private init; } = new Dictionary<string, RaceEventDefinition>();
        public IReadOnlyDictionary<string, MilestoneDefinition> Milestones { get; private init; } = new Dictionary<string, MilestoneDefinition>();

        /// <summary>In the order the mods list them: with no path picked, the first reached wins</summary>
        public IReadOnlyList<VictoryDefinition> Victories { get; private init; } = [];

        /// <summary>The victory paths a new game has open from the start: those with nothing to unlock</summary>
        public IEnumerable<string> OpenFromStart => Victories.Where(v => v.Unlock.Count == 0).Select(v => v.Id);

        public IReadOnlyList<string> Problems { get; private init; } = [];

        /// <summary>The career of the mods next to the game</summary>
        public static CareerContent Installed => InstalledContent.Value;

        /// <param name="raceFormats">The race formats its events name; none: those of the same mods</param>
        public static CareerContent Load(ModSet mods, Race.RaceFormats? raceFormats = null)
        {
            var formats = raceFormats ?? (ReferenceEquals(mods, ModSet.Installed) ? Race.RaceFormats.Installed
                : Race.RaceFormats.Load(mods, ScRaceMode.DefaultSourceFolder));
            var problems = new List<string>();

            var victories = ModJson.Load(mods, ContentTypes.Victories, "victories", "victory path", problems, entry =>
            {
                var victory = Tidy(entry.Element.Deserialize<VictoryDefinition>(Options));
                var bad = victory?.Unlock.Concat(victory.Goal).FirstOrDefault(s => !IsKnownStep(s));
                if (bad == null) return victory;

                problems.Add($"{entry.Source}: victory path {entry.Id} has a step of type \"{bad.Type}\" (counter, beatBoss, beatAll, mostWins), or a counter step without a counter; left out");
                return null;
            });
            var victoryList = new List<VictoryDefinition>();
            foreach (var (id, victory, mod) in victories.Entries)
            {
                victory.Id = id;
                // The boss a step names is the mod's own racer, else the game's: the racers are not read yet, so
                // that is told once the game has its bosses (DataVictory)
                victory.ModId = mod.Id;
                victoryList.Add(victory);
            }

            var milestones = ModJson.Load(mods, ContentTypes.Milestones, "milestones", "milestone", problems,
                entry => Tidy(entry.Element.Deserialize<MilestoneDefinition>(Options)));
            var milestoneMap = new Dictionary<string, MilestoneDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, milestone, mod) in milestones.Entries)
            {
                milestone.Id = id;
                milestone.Unlocks = milestone.Unlocks.Select(u => ResolveUnlock(mod, u, victories)).ToList();
                milestoneMap[id] = milestone;
            }

            var events = ModJson.Load(mods, ContentTypes.Events, "events", "event", problems, entry =>
            {
                var evt = Tidy(entry.Element.Deserialize<RaceEventDefinition>(Options));
                if (evt == null) return null;

                // The format the mod's own, else the game's; an event that names none is a drag race, as every
                // event without a race type was. One no mod has leaves the event out.
                var written = (string.IsNullOrWhiteSpace(evt.Format) ? evt.RaceType : evt.Format)?.Trim();
                var format = string.IsNullOrEmpty(written)
                    ? formats.First(Models.Race.RaceFormatFamilies.Drag)
                    : formats.Get(ModIds.Resolve(entry.Mod.Id, written, id => formats.Get(id) != null));
                if (format == null)
                {
                    problems.Add($"{entry.Source}: event {entry.Id} is a \"{written}\" race, a format no mod has; left out");
                    return null;
                }

                evt.Format = format.Id;
                evt.RaceType = null;
                if (evt.Laps is { } laps) evt.Laps = Math.Clamp(laps, 1, Race.RaceFormats.MaxLaps);
                if (format.IsDrag && evt.Laps is > 1)
                {
                    problems.Add($"{entry.Source}: event {entry.Id} is a drag race over {evt.Laps} laps; a drag race is one run");
                    evt.Laps = null;
                }
                TidyField(evt, format, $"{entry.Source}: event {entry.Id}", problems);
                return evt;
            });
            var eventMap = new Dictionary<string, RaceEventDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, evt, mod) in events.Entries)
            {
                evt.Id = id;
                evt.RequiredMilestones = evt.RequiredMilestones.Select(m => milestones.Resolve(mod, m)).ToList();
                foreach (var missing in evt.RequiredMilestones.Where(m => !milestoneMap.ContainsKey(m)))
                    problems.Add($"{mod.Id}: event {id} asks for milestone {missing}, which no mod has: it never opens");
                if (evt.Reward.SpecialItem is { } item && item.StartsWith(PartPrizePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    // The parts are not read yet: the mod's own id here, the game's when the prize is given (RaceResultProcessor)
                    var part = item[PartPrizePrefix.Length..];
                    evt.Reward.SpecialItem = PartPrizePrefix + (ModIds.IsQualified(part) ? ModIds.Normalize(part) : ModIds.Qualify(mod.Id, part));
                }
                eventMap[id] = evt;
            }

            foreach (var line in milestones.Replaced.Concat(victories.Replaced).Concat(events.Replaced)) Logger.Information("{Line}", line);
            foreach (var problem in problems) Logger.Warning("Career content: {Problem}", problem);
            ModProblems.Add("Career content", problems);
            Logger.Information("Career: {Events} events, {Milestones} milestones, {Victories} victory paths",
                eventMap.Count, milestoneMap.Count, victoryList.Count);

            return new CareerContent { Events = eventMap, Milestones = milestoneMap, Victories = victoryList, Problems = problems };
        }

        /// <summary>A prize that is a part: "part:" and its id</summary>
        public const string PartPrizePrefix = "part:";

        // A null in a mod's file where the game counts on a value ("unlock": null, a null step) reads as nothing written
        private static VictoryDefinition? Tidy(VictoryDefinition? victory)
        {
            if (victory == null) return null;

            victory.Name ??= string.Empty;
            victory.Description ??= string.Empty;
            victory.Unlock = (victory.Unlock ?? []).OfType<VictoryStep>().ToList();
            victory.Goal = (victory.Goal ?? []).OfType<VictoryStep>().ToList();
            foreach (var step in victory.Unlock.Concat(victory.Goal))
            {
                step.Type ??= VictoryStep.CounterType;
                if (step.Type == VictoryStep.BeatKingType) step.Type = VictoryStep.BeatBossType; // as written before bosses
                step.Boss = string.IsNullOrWhiteSpace(step.Boss) ? null : step.Boss.Trim();
                step.Step ??= string.Empty;
            }

            victory.Progress ??= new VictoryTexts();
            victory.Progress.Achieved ??= string.Empty;
            victory.Progress.Unlocked ??= string.Empty;
            victory.Progress.Locked ??= string.Empty;
            return victory;
        }

        private static MilestoneDefinition? Tidy(MilestoneDefinition? milestone)
        {
            if (milestone == null) return null;

            milestone.Name ??= string.Empty;
            milestone.Description ??= string.Empty;
            milestone.Unlocks = (milestone.Unlocks ?? []).OfType<string>().ToList();
            return milestone;
        }

        private static RaceEventDefinition? Tidy(RaceEventDefinition? evt)
        {
            if (evt == null) return null;

            evt.Name ??= string.Empty;
            evt.Description ??= string.Empty;
            evt.RequiredMilestones = (evt.RequiredMilestones ?? []).OfType<string>().ToList();
            evt.Reward ??= new EventReward();
            evt.Reward.Places = (evt.Reward.Places ?? []).OfType<PlaceReward>().ToList();
            evt.SpecialOpponents = evt.SpecialOpponents?.OfType<EventOpponent>().ToList();
            if (evt.Field != null)
            {
                evt.Field.Racers = (evt.Field.Racers ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList();
                evt.Field.From = string.IsNullOrWhiteSpace(evt.Field.From) ? EventFieldFrom.Pool : evt.Field.From.Trim().ToLowerInvariant();
                evt.Field.Grid = string.IsNullOrWhiteSpace(evt.Field.Grid) ? EventFieldGrids.Random : evt.Field.Grid.Trim().ToLowerInvariant();
            }
            return evt;
        }

        /// <summary>
        /// An event's field, the purse and the odds held to what the game runs: the rivals between 1 and what the format
        /// takes (a format of one on one drops the field), no pink slip in a field, a grid the game knows, a share of the
        /// pot between nothing and all of it
        /// </summary>
        private static void TidyField(RaceEventDefinition evt, Models.Race.RaceFormat format, string where, List<string> problems)
        {
            if (evt.Entry < 0)
            {
                problems.Add($"{where}: an entry of {evt.Entry} is no fee; free to enter");
                evt.Entry = 0;
            }
            if (evt.Chance is { } chance) evt.Chance = double.IsFinite(chance) ? Math.Clamp(chance, 0, 1) : null;
            foreach (var place in evt.Reward.Places)
            {
                if (place.Share is { } share) place.Share = double.IsFinite(share) ? Math.Clamp(share, 0, 1) : 0;
                if (place.Cash is < 0) place.Cash = 0;
            }
            if (evt.Reward.Places.Sum(p => p.Share ?? 0) > 1.0001)
                problems.Add($"{where}: its places share out more than the whole pot");

            if (evt.Field is not { } field) return;
            if (field.Count is { } count) field.Min = field.Max = count;
            field.Min = Math.Clamp(field.Min, 1, Race.RaceFormats.MaxRivals);
            field.Max = Math.Clamp(field.Max, field.Min, Race.RaceFormats.MaxRivals);
            if (field.Max > format.Rivals)
            {
                problems.Add(format.Rivals <= 1
                    ? $"{where}: a field of {field.Max} on {format.Id}, which races one on one; one rival"
                    : $"{where}: a field of {field.Max} on {format.Id}, which takes {format.Rivals} at most; cut to that");
                field.Max = format.Rivals;
                field.Min = Math.Min(field.Min, field.Max);
            }
            if (field.Max <= 1)
            {
                evt.Field = null;
                return;
            }

            if (evt.IsPinkSlip)
            {
                problems.Add($"{where}: a field races for cash only; no pink slip");
                evt.IsPinkSlip = false;
            }
            if (field.From is not (EventFieldFrom.Pool or EventFieldFrom.Racers))
            {
                problems.Add($"{where}: a field from \"{field.From}\" is nothing the game knows; from the pool");
                field.From = EventFieldFrom.Pool;
            }
            if (field.Grid is not (EventFieldGrids.Random or EventFieldGrids.Reputation or EventFieldGrids.PlayerLast or EventFieldGrids.PlayerFirst))
            {
                problems.Add($"{where}: a \"{field.Grid}\" grid is nothing the game knows; a random one");
                field.Grid = EventFieldGrids.Random;
            }
            field.Stagger = double.IsFinite(field.Stagger) ? Math.Clamp(field.Stagger, 0, Configuration.RaceIniBuilder.MaxStagger) : 0;
        }

        private static bool IsKnownStep(VictoryStep step) => step.Type switch
        {
            VictoryStep.BeatBossType or VictoryStep.BeatAllType or VictoryStep.MostWinsType => true,
            VictoryStep.CounterType => step.Counter != null,
            _ => false
        };

        // "victory:King" in a mod's milestone means its own path King, or the game's (the core's or the setting's)
        private static string ResolveUnlock(ModInfo mod, string unlock, ContentRegistry<VictoryDefinition> victories)
        {
            const string prefix = "victory:";
            return unlock.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? prefix + victories.Resolve(mod, unlock[prefix.Length..])
                : unlock;
        }
    }
}
