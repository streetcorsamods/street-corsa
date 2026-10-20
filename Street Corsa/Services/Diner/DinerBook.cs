using System.Text.Json;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.Diners;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Diner
{
    /// <summary>
    /// The diners of the scene by level, from every mod's <c>diners</c> folder (<c>diners.json</c>: <c>{ "diners": [ ... ] }</c>),
    /// in the order of their floors (<see cref="Models.Diners.Diner.PowerFrom"/>): the first takes every car, each next one the
    /// cars from its floor up. A racer is a regular of the diner their car's power puts them in (<see cref="Of"/>); a boss
    /// of the top one, whatever he drives. The regulars of a diner above the player's car will not race them
    /// (<see cref="Snubs"/>). With no diners listed at all, every street with a diner on it is a diner of the first level,
    /// so a mod that ships a street with a diner.json and nothing more has its diner.
    /// </summary>
    public sealed class DinerBook
    {
        private static readonly Lazy<IAppLogger> LazyLogger = new(() => AppLoggerFactory.CreateLogger("Diners"));
        private static IAppLogger Logger => LazyLogger.Value;

        private static readonly Lazy<DinerBook> InstalledBook = new(() => Load(ModSet.Installed, DinerScenes.Installed));
        private static DinerBook? _current;

        /// <summary>The diners of the mods next to the game, read once</summary>
        public static DinerBook Installed => InstalledBook.Value;

        /// <summary>The book in play: the installed one unless a test or a tool put another in</summary>
        public static DinerBook Current
        {
            get => _current ?? Installed;
            set => _current = value;
        }

        private readonly List<Models.Diners.Diner> _diners;
        private readonly Dictionary<string, Models.Diners.Diner> _byId;
        private readonly Dictionary<string, DinerScene> _scenes;

        private DinerBook(List<Models.Diners.Diner> diners, IReadOnlyList<DinerScene> scenes, List<string> problems)
        {
            _diners = diners;
            _byId = new Dictionary<string, Models.Diners.Diner>(StringComparer.OrdinalIgnoreCase);
            foreach (var diner in diners) _byId[diner.Id] = diner;
            _scenes = new Dictionary<string, DinerScene>(StringComparer.OrdinalIgnoreCase);
            foreach (var scene in scenes) _scenes[scene.Id] = scene;
            Problems = problems;
        }

        /// <summary>Every diner, the beginners' first, then up the levels</summary>
        public IReadOnlyList<Models.Diners.Diner> All => _diners;

        public int Count => _diners.Count;

        /// <summary>The beginners' diner, where everybody starts out; null in a game with no diner</summary>
        public Models.Diners.Diner? First => _diners.Count == 0 ? null : _diners[0];

        /// <summary>The top diner, the bosses' own; null in a game with no diner</summary>
        public Models.Diners.Diner? Top => _diners.Count == 0 ? null : _diners[^1];

        /// <summary>What the files got wrong, one line each</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>The diner by id, whatever its case; null when there is none</summary>
        public Models.Diners.Diner? Get(string? id) => id != null && _byId.TryGetValue(id, out var diner) ? diner : null;

        /// <summary>The diner by id, else the first: where a way to "the diner" leads when it names none, or one that is gone</summary>
        public Models.Diners.Diner? GetOrFirst(string? id) => Get(id) ?? First;

        /// <summary>
        /// The diner a car of <paramref name="power"/> horsepower (the base game's unit) belongs at: the highest whose floor it
        /// reaches; a car with no figure, or none at all, the first. Null in a game with no diner.
        /// </summary>
        public Models.Diners.Diner? Of(double? power)
        {
            if (_diners.Count == 0) return null;
            var hp = power is { } p && double.IsFinite(p) ? Math.Max(0, p) : 0;
            var at = _diners[0];
            foreach (var diner in _diners)
            {
                if (hp >= diner.PowerFrom) at = diner;
            }

            return at;
        }

        /// <summary>The diner a racer is a regular of: the one their car's power puts them in, a boss's the top one</summary>
        public Models.Diners.Diner? Of(Racer racer, Car? car, CarDefinition? definition) =>
            racer is Opponent { IsBoss: true } ? Top : Of(CarPower.Of(car, definition));

        /// <summary>Whether <paramref name="racer"/> in <paramref name="car"/> is a regular of <paramref name="diner"/></summary>
        public bool IsRegular(Models.Diners.Diner diner, Racer racer, Car? car, CarDefinition? definition) =>
            Of(racer, car, definition) is { } at && string.Equals(at.Id, diner.Id, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the regulars of <paramref name="diner"/> will not race a player in a car of <paramref name="power"/>
        /// horsepower: it is below the diner's floor. Never at the first diner.
        /// </summary>
        public static bool Snubs(Models.Diners.Diner diner, double? power)
        {
            if (diner.IsFirst || diner.PowerFrom <= 0) return false;
            var hp = power is { } p && double.IsFinite(p) ? p : 0;
            return hp < diner.PowerFrom;
        }

        /// <summary>
        /// The street the diner is seen on, wearing the diner's look (<see cref="Dressed"/>): its own street when it has one
        /// with a diner on it, else the first diner's, else the last street with a diner; null with none
        /// </summary>
        public DinerScene? SceneOf(Models.Diners.Diner? diner)
        {
            var scene = StreetOf(diner);
            return scene == null || diner == null ? scene : Dressed(scene, diner);
        }

        private DinerScene? StreetOf(Models.Diners.Diner? diner)
        {
            if (diner?.Street is { } street && _scenes.TryGetValue(street, out var own)) return own;
            if (First?.Street is { } first && _scenes.TryGetValue(first, out var fallback)) return fallback;
            return _scenes.Count == 0 ? null : _scenes.Values.Last();
        }

        /// <summary>
        /// The street as the diner wears it: its look groups shown (its own, else the one named after it: LOOK_HIWAY for
        /// hiway), its sun, its lights over the street's by light of the day (the street's lamps kept at that hour,
        /// recoloured when the look says, the look's own added), its glow over the street's material by material
        /// </summary>
        public static DinerScene Dressed(DinerScene scene, Models.Diners.Diner diner)
        {
            var look = diner.Look;
            var shown = look is { Show.Count: > 0 } ? look.Show : [ModIds.Local(diner.Id)];

            var lights = scene.Lights;
            if (look is { Lights.Count: > 0 })
            {
                var merged = new Dictionary<StreetLight, StreetLighting>(scene.Lights);
                foreach (var (light, lighting) in look.Lights)
                {
                    var lamps = new List<StreetLamp>();
                    if (scene.Lights.TryGetValue(light, out var streets))
                        lamps.AddRange(look.LampTints.TryGetValue(light, out var tint) ? streets.Lamps.Select(l => l with { Color = tint }) : streets.Lamps);
                    lamps.AddRange(lighting.Lamps);
                    merged[light] = lighting with { Lamps = lamps };
                }

                lights = merged;
            }

            var glow = scene.Glow;
            if (look is { Glow.Count: > 0 })
            {
                var merged = new Dictionary<StreetLight, IReadOnlyDictionary<string, float>>(scene.Glow);
                foreach (var (light, materials) in look.Glow)
                {
                    var at = new Dictionary<string, float>(scene.GlowAt(light), StringComparer.Ordinal);
                    foreach (var (material, level) in materials) at[material] = level;
                    merged[light] = at;
                }

                glow = merged;
            }

            return scene with { ShownLooks = SceneLooks.Prefixes(shown), Sun = look?.Sun ?? scene.Sun, Lights = lights, Glow = glow };
        }

        /// <summary>The diner's name in the player's words: its key, else <c>diner.name.&lt;id&gt;</c>, else its street's title, else "the diner"</summary>
        public string NameOf(Models.Diners.Diner diner)
        {
            var text = GameText.Installed;
            foreach (var key in NameKeys(diner))
            {
                if (text.Has(key)) return GameText.Say(key);
            }

            return SceneOf(diner)?.Title ?? GameText.Say("diner.title");
        }

        /// <summary>The line about the place for the racing hub; empty with none</summary>
        public string AboutOf(Models.Diners.Diner diner)
        {
            var text = GameText.Installed;
            foreach (var key in AboutKeys(diner))
            {
                if (text.Has(key)) return GameText.Say(key);
            }

            return string.Empty;
        }

        /// <summary>The part of town the diner is in, for the map's card; empty with none</summary>
        public string RegionOf(Models.Diners.Diner diner)
        {
            var text = GameText.Installed;
            foreach (var key in RegionKeys(diner))
            {
                if (text.Has(key)) return GameText.Say(key);
            }

            return string.Empty;
        }

        /// <summary>The keys a diner's name may be under, the file's own first</summary>
        public static IEnumerable<string> NameKeys(Models.Diners.Diner diner) => OwnKeys("diner.name", diner.Name, diner.Id);

        public static IEnumerable<string> AboutKeys(Models.Diners.Diner diner) => OwnKeys("diner.about", diner.About, diner.Id);

        public static IEnumerable<string> RegionKeys(Models.Diners.Diner diner) => OwnKeys("diner.region", diner.Region, diner.Id);

        private static IEnumerable<string> OwnKeys(string prefix, string? own, string id)
        {
            if (!string.IsNullOrWhiteSpace(own)) yield return own.Trim();
            yield return prefix + "." + id;
            if (ModIds.IsQualified(id)) yield return prefix + "." + ModIds.Local(id);
        }

        /// <summary>
        /// The diners of <paramref name="mods"/> over the diner streets <paramref name="scenes"/>. A diner whose floor is no
        /// number, or under 0, is left out; one naming a street with no diner on it is kept and seen on the first's street
        /// (told in the log). No diner listed: one of the first level per diner street.
        /// </summary>
        public static DinerBook Load(ModSet mods, IReadOnlyList<DinerScene> scenes)
        {
            var problems = new List<string>();
            var registry = ModJson.Load(mods, ContentTypes.Diners, "diners", "diner", problems, entry => Read(entry, problems));

            var diners = new List<Models.Diners.Diner>();
            foreach (var (id, diner, mod) in registry.Entries)
            {
                diner.Id = id;
                diner.ModId = mod.Id;
                diners.Add(diner);
            }

            foreach (var line in registry.Replaced) Logger.Information("{Line}", line);

            if (diners.Count == 0)
            {
                foreach (var scene in scenes)
                    diners.Add(new Models.Diners.Diner { Id = scene.Id, ModId = ModIds.ModOf(scene.Id), Street = scene.Id, PowerFrom = 0 });
            }

            // The levels from the floors: the lowest floor is the beginners' diner (its floor is 0 whatever it said)
            diners = diners.OrderBy(d => d.PowerFrom).ThenBy(d => d.Order).ToList();
            for (var i = 0; i < diners.Count; i++) diners[i].Level = i;
            if (diners.Count > 0 && diners[0].PowerFrom > 0)
            {
                problems.Add($"the first diner {diners[0].Id} starts at {diners[0].PowerFrom} hp; it takes every car, from 0");
                diners[0].PowerFrom = 0;
            }

            // A street named as its mod wrote it is the mod's own when it has one, else the base game's. One with no diner
            // on it (not built yet, as the base game's upper diners' until step 37's phase 4) is no problem to show the
            // player: the diner is seen on the first's street, and the log says so
            var streets = new HashSet<string>(scenes.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var diner in diners)
            {
                if (diner.Street == null) continue;
                diner.Street = ModIds.Resolve(diner.ModId, diner.Street, streets.Contains);
                if (!streets.Contains(diner.Street))
                    Logger.Information("Diners: {Mod}'s diner {Id} is on the street {Street}, which has no diner on it (no streets/{Folder}/diner.json): seen on the first diner's street", diner.ModId, diner.Id, diner.Street, ModIds.Local(diner.Street));
            }

            foreach (var problem in problems) Logger.Warning("Diners: {Problem}", problem);
            ModProblems.Add("Diners", problems);
            Logger.Information("Diners: {Diners}", string.Join(", ", diners.Select(d => $"{d.Id} from {d.PowerFrom} hp")));
            return new DinerBook(diners, scenes, problems);
        }

        /// <summary>A book of the given diners over the given streets, for tests and tools; the levels from the floors as <see cref="Load"/> gives them</summary>
        public static DinerBook From(IEnumerable<Models.Diners.Diner> diners, IReadOnlyList<DinerScene>? scenes = null)
        {
            var list = diners.OrderBy(d => d.PowerFrom).ThenBy(d => d.Order).ToList();
            for (var i = 0; i < list.Count; i++) list[i].Level = i;
            return new DinerBook(list, scenes ?? [], []);
        }

        /// <summary>A diner's look as its file gives it: the groups shown, and the sun, lights and glow as a street's file gives them</summary>
        private sealed record LookEntry(List<string?>? Show, float[]? Sun, Dictionary<string, SceneLighting.LightEntry?>? Lights,
            Dictionary<string, Dictionary<string, float>?>? Glow);

        /// <summary>What a look's light of the day says besides a street's: the colour the street's lamps take at that hour</summary>
        private sealed record LookLightEntry(int[]? LampColor);

        private sealed record LookLampsEntry(Dictionary<string, LookLightEntry?>? Lights);

        private static Models.Diners.Diner? Read(ModEntry entry, List<string> problems)
        {
            var diner = entry.Element.Deserialize<Models.Diners.Diner>(ModJson.Options);
            if (diner == null) return null;

            var where = $"{entry.Source}: diner {entry.Id}";
            if (ModJson.TryGet(entry.Element, "look", out var lookElement) && lookElement.ValueKind == JsonValueKind.Object)
            {
                var look = lookElement.Deserialize<LookEntry>(ModJson.Options);
                var lampTints = new Dictionary<StreetLight, byte[]>();
                foreach (var (name, light) in lookElement.Deserialize<LookLampsEntry>(ModJson.Options)?.Lights ?? [])
                {
                    if (light?.LampColor is { Length: 3 } tint && SceneLighting.TryParse(name, out var which))
                        lampTints[which] = SceneLighting.Rgb(tint, 255);
                }

                diner.Look = new Models.Diners.DinerLook(
                    (look?.Show ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList(),
                    look?.Sun is { Length: 3 } sun && sun.All(float.IsFinite) ? sun : null,
                    SceneLighting.Read(look?.Lights, entry.Id, Logger),
                    lampTints,
                    SceneLighting.MaterialGlows(look?.Glow));
            }

            if (!double.IsFinite(diner.PowerFrom) || diner.PowerFrom < 0)
            {
                problems.Add($"{where}: powerFrom {diner.PowerFrom} is no horsepower; left out");
                return null;
            }

            if (diner.Stakes <= 0m || diner.Stakes > 100m)
            {
                problems.Add($"{where}: stakes {diner.Stakes} is out of 0 to 100; 1");
                diner.Stakes = 1m;
            }

            // A spot off the picture is no spot: the diner is not marked
            if ((diner.MapX != null || diner.MapY != null) && !diner.IsOnMap)
            {
                problems.Add($"{where}: mapX {diner.MapX} / mapY {diner.MapY} is not on the map (fractions of the picture, 0 to 1); not marked");
                diner.MapX = diner.MapY = null;
            }

            diner.Street = string.IsNullOrWhiteSpace(diner.Street) ? null : diner.Street.Trim();
            return diner;
        }
    }
}
