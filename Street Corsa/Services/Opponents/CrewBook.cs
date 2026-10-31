using System.Text.RegularExpressions;
using StreetCorsa.Logging;
using StreetCorsa.Models.Career;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// The crews of the scene, from every mod's <c>crews</c> folder (<c>crews.json</c>: <c>{ "crews": [ ... ] }</c>, or one
    /// crew per file), merged by id as every content is: a mod's crews get its namespace, one that <c>replaces</c> another
    /// takes its place, <c>removes</c> takes one out. A racer rides with the crew their <c>"crew"</c> names
    /// (<see cref="Opponent.Crew"/>, put on them as the racers are read); a crew's leader (<see cref="Crew.Boss"/>) rides
    /// with it by being named, and is known by his full racer id once the racers are read (<see cref="OpponentRepository"/>
    /// resolves the id as the mod wrote it, its own racer first, then the game's, and keeps it on the crew).
    /// </summary>
    public sealed partial class CrewBook
    {
        private static readonly Lazy<IAppLogger> LazyLogger = new(() => AppLoggerFactory.CreateLogger("Crews"));
        private static IAppLogger Logger => LazyLogger.Value;

        private static readonly ContentCache<CrewBook> InstalledBook = new(() => Load(ModSet.Installed));
        private static CrewBook? _current;

        /// <summary>The crews of the mods next to the game, read once per content in play</summary>
        public static CrewBook Installed => InstalledBook.Value;

        /// <summary>The book in play: the installed one unless a test or a tool put another in</summary>
        public static CrewBook Current
        {
            get => _current ?? Installed;
            set => _current = value;
        }

        private readonly List<Crew> _crews;
        private readonly Dictionary<string, Crew> _byId;

        private CrewBook(List<Crew> crews, List<string> problems)
        {
            _crews = crews;
            _byId = new Dictionary<string, Crew>(StringComparer.OrdinalIgnoreCase);
            foreach (var crew in crews) _byId[crew.Id] = crew;
            Problems = problems;
        }

        /// <summary>Every crew, in the order the mods list them</summary>
        public IReadOnlyList<Crew> All => _crews;

        public int Count => _crews.Count;

        /// <summary>What the files got wrong, one line each</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>The crew by its full id, whatever its case; null when there is none</summary>
        public Crew? Get(string? id) => id != null && _byId.TryGetValue(id, out var crew) ? crew : null;

        public bool Has(string id) => _byId.ContainsKey(id);

        /// <summary>The crew <paramref name="racer"/> rides with (<see cref="Opponent.Crew"/>); null when they ride alone, or their crew is gone</summary>
        public Crew? CrewOf(Racer? racer) => racer is Opponent { Crew.Length: > 0 } opponent ? Get(opponent.Crew) : null;

        /// <summary>
        /// Whether <paramref name="racer"/> is the leader of <paramref name="crew"/>: <see cref="Crew.Boss"/> is their full racer
        /// id (it is, once the racers are read; a leader no racer is stays as the mod wrote him, and is nobody's id, so another
        /// mod's racer of the same local id is never him)
        /// </summary>
        public static bool IsLeader(Crew? crew, Racer? racer) =>
            crew?.Boss is { Length: > 0 } boss && racer is Opponent { DefinitionId.Length: > 0 } opponent
            && string.Equals(boss, opponent.DefinitionId, StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether <paramref name="racer"/> rides with <paramref name="crew"/>: a member by their crew, or its leader</summary>
        public static bool Rides(Crew crew, Racer? racer) =>
            racer is Opponent opponent && (string.Equals(opponent.Crew, crew.Id, StringComparison.OrdinalIgnoreCase) || IsLeader(crew, opponent));

        /// <summary>The crews of <paramref name="mods"/>. A crew without a name is called by its id; a colour that is no colour is told and dropped.</summary>
        public static CrewBook Load(ModSet mods)
        {
            var problems = new List<string>();
            var registry = ModJson.Load(mods, ContentTypes.Crews, "crews", "crew", problems, entry => Read(entry, problems));

            var crews = new List<Crew>();
            foreach (var (id, crew, mod) in registry.Entries)
            {
                crew.Id = id;
                crew.ModId = mod.Id;
                if (crew.Name.Length == 0) crew.Name = ModIds.Local(id);
                crews.Add(crew);
            }

            foreach (var line in registry.Replaced) Logger.Information("{Line}", line);
            foreach (var problem in problems) Logger.Warning("Crews: {Problem}", problem);
            ModProblems.Add("Crews", problems);
            if (crews.Count > 0) Logger.Information("Crews: {Count}", crews.Count);
            return new CrewBook(crews, problems);
        }

        private static Crew? Read(ModEntry entry, List<string> problems)
        {
            var crew = entry.As<Crew>();
            if (crew == null) return null;

            crew.Name = (crew.Name ?? string.Empty).Trim();
            crew.Description = (crew.Description ?? string.Empty).Trim();
            crew.Home = string.IsNullOrWhiteSpace(crew.Home) ? null : crew.Home.Trim();
            crew.Boss = string.IsNullOrWhiteSpace(crew.Boss) ? null : crew.Boss.Trim();
            crew.Setup = string.IsNullOrWhiteSpace(crew.Setup) ? null : crew.Setup.Trim();
            if (crew.Setup != null && !StreetCorsa.Rules.RivalTuning.Has(crew.Setup))
            {
                problems.Add($"{entry.Source}: crew {entry.Id}'s setup \"{crew.Setup}\" is a setup no rules have; its racers go by the rules'");
                crew.Setup = null;
            }
            crew.Colour = string.IsNullOrWhiteSpace(crew.Colour) ? null : crew.Colour.Trim();
            if (crew.Colour != null && !ColourPattern().IsMatch(crew.Colour))
            {
                problems.Add($"{entry.Source}: crew {entry.Id}'s colour \"{crew.Colour}\" is no colour (#RRGGBB); no colour");
                crew.Colour = null;
            }

            return crew;
        }

        [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
        private static partial Regex ColourPattern();
    }
}
