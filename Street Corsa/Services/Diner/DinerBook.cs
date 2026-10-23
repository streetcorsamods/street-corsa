using System.Text.Json;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.Diners;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Diner
{
    /// <summary>
    /// The diners of the scene by level, from every mod's <c>diners</c> folder (<c>diners.json</c>: <c>{ "diners": [ ... ] }</c>),
    /// in the order of their floors (<see cref="Models.Diners.Diner.PowerFrom"/>): the first takes every car, each next one the
    /// cars from its floor up. A racer is a regular of the diner their car's power puts them in (<see cref="Of"/>); a boss
    /// of the top one, whatever he drives. The regulars of a diner above the player's car will not race them
    /// (<see cref="Snubs(Models.Diners.Diner, Opponent, double?)"/>). With no diners listed at all, every street with a diner on
    /// it is a diner of the first level, and a mod's street with a diner on it that no diner names is where the base game's
    /// first diner is seen: so a mod that ships a street with a diner.json and nothing more has its diner.
    /// </summary>
    public sealed class DinerBook
    {
        private static readonly Lazy<IAppLogger> LazyLogger = new(() => AppLoggerFactory.CreateLogger("Diners"));
        private static IAppLogger Logger => LazyLogger.Value;

        private static readonly ContentCache<DinerBook> InstalledBook = new(() => Load(ModSet.Installed, DinerScenes.Installed));
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
        /// reaches, the first by <see cref="Models.Diners.Diner.Order"/> of two on one floor; a car with no figure, or none
        /// at all, the first. Null in a game with no diner.
        /// </summary>
        public Models.Diners.Diner? Of(double? power) => Of(power, 0);

        /// <summary>
        /// The diner a racer is a regular of: the one their car's power puts them in, a boss's the top one. Two diners on one
        /// floor share its racers, each racer always at the same one, by their name.
        /// </summary>
        public Models.Diners.Diner? Of(Racer racer, Car? car, CarDefinition? definition) =>
            racer is Opponent { IsBoss: true } ? Top : Of(CarPower.Of(car, definition), StableHash.Fnv(racer.Name ?? string.Empty));

        private Models.Diners.Diner? Of(double? power, uint pick)
        {
            if (_diners.Count == 0) return null;
            var hp = power is { } p && double.IsFinite(p) ? Math.Max(0, p) : 0;
            var floor = _diners[0].PowerFrom;
            foreach (var diner in _diners)
            {
                if (hp >= diner.PowerFrom) floor = diner.PowerFrom;
            }

            var tied = _diners.Where(d => d.PowerFrom == floor).ToList();
            return tied[(int)(pick % (uint)tied.Count)];
        }

        /// <summary>Whether <paramref name="racer"/> in <paramref name="car"/> is a regular of <paramref name="diner"/></summary>
        public bool IsRegular(Models.Diners.Diner diner, Racer racer, Car? car, CarDefinition? definition) =>
            Of(racer, car, definition) is { } at && string.Equals(at.Id, diner.Id, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the regulars of <paramref name="diner"/> will not race a player in a car of <paramref name="power"/>
        /// horsepower: it is below the diner's floor. Never at the first diner, and never a car the game has no figure for:
        /// it is not turned away for what nobody has measured.
        /// </summary>
        public static bool Snubs(Models.Diners.Diner diner, double? power)
        {
            if (diner.IsFirst || diner.PowerFrom <= 0) return false;
            return power is { } hp && double.IsFinite(hp) && hp < diner.PowerFrom;
        }

        /// <summary>
        /// Whether <paramref name="racer"/> at <paramref name="diner"/> turns a car of <paramref name="power"/> down for its
        /// class (<see cref="Snubs(Models.Diners.Diner, double?)"/>): a boss takes on anybody who has made it to his table,
        /// and a rival out for a rematch wants it whatever the player drives
        /// </summary>
        public static bool Snubs(Models.Diners.Diner diner, Opponent racer, double? power) =>
            !racer.IsBoss && !Grudges.WantsRematch(racer) && Snubs(diner, power);

        /// <summary>The diner's class in the player's words: the cars it takes, and the stakes when they are up</summary>
        public static string ClassOf(Models.Diners.Diner diner)
        {
            var cars = diner.IsFirst ? GameText.Say("diner.class.any")
                : GameText.Say("diner.class.from", ("power", Measure.Power(diner.PowerFrom).ToString("N0", null)));
            return diner.Stakes > 1m ? GameText.Say("diner.class.stakes", ("cars", cars), ("stakes", diner.Stakes)) : cars;
        }

        /// <summary>
        /// The street the diner is seen on, wearing the diner's look (<see cref="Dressed"/>): its own street when it has one
        /// with a diner on it, else the first diner's, else the last street with a diner; null with none
        /// </summary>
        /// <param name="laid">The street's pieces put on it (<see cref="StreetLayouts"/>), before the look dresses it: a look's lamp
        /// colours are the layout's lamps' too</param>
        public DinerScene? SceneOf(Models.Diners.Diner? diner, Func<DinerScene, DinerScene>? laid = null)
        {
            var scene = StreetOf(diner);
            if (scene != null && laid != null) scene = laid(scene);
            return scene == null || diner == null ? scene : Dressed(scene, diner);
        }

        private DinerScene? StreetOf(Models.Diners.Diner? diner)
        {
            if (diner?.Street is { } street && _scenes.TryGetValue(street, out var own)) return own;
            if (First?.Street is { } first && _scenes.TryGetValue(first, out var fallback)) return fallback;
            return _scenes.Count == 0 ? null : _scenes.Values.Last();
        }

        /// <summary>
        /// The street as the diner wears it: its look groups shown (its own, else the street's), its sun, its lights over the
        /// street's by light of the day (the street's lamps kept at that hour, recoloured when the look says, the look's own
        /// added; a light that gives no brightness keeps the street's light and only does that to its lamps), its glow over
        /// the street's material by material
        /// </summary>
        public static DinerScene Dressed(DinerScene scene, Models.Diners.Diner diner)
        {
            var (shown, sun, lights, glow) = Dress(diner.Look, scene.ShownLooks, scene.Sun, scene.Lights, scene.Glow);
            var dressed = scene with { ShownLooks = shown, Sun = sun, Lights = lights, Glow = glow };

            // The diner's own way in and out, and where the driver looks from its curb: on the street the way is written on
            return diner.Look is not { Approach: { } way } look || !look.FitsOn(scene.Id) ? dressed : dressed with
            {
                Arrive = way.Arrive, BackOut = way.BackOut, Leave = way.Leave,
                LookWhileParked = way.LookWhileParked, LookUpWhileParked = way.LookUpWhileParked
            };
        }

        /// <summary>
        /// The diner whose look a street wears: <paramref name="diner"/> when it is the street that diner is seen on, else
        /// (no diner, or one seen elsewhere) the first diner seen on it, so a curb of Main Street shows the beginners' diner
        /// as it is; null when no diner is seen there
        /// </summary>
        public Models.Diners.Diner? WornBy(StreetScene street, Models.Diners.Diner? diner)
        {
            bool On(Models.Diners.Diner d) => string.Equals(StreetOf(d)?.Id, street.Id, StringComparison.OrdinalIgnoreCase);
            return diner != null && On(diner) ? diner : _diners.FirstOrDefault(On);
        }

        /// <summary>
        /// The Cruise screen's street as the diner the player came out of wears it (<see cref="Dressed"/>) when it is the street
        /// that diner is seen on, so the corner looks the same out on the street; else, or with no diner, as it is
        /// </summary>
        /// <remarks>The street's pieces go on it first (<see cref="StreetLayouts"/>), so the look's lamp colours are theirs too</remarks>
        public StreetScene DressedStreet(StreetScene street, Models.Diners.Diner? diner)
        {
            if (diner == null || !string.Equals(StreetOf(diner)?.Id, street.Id, StringComparison.OrdinalIgnoreCase)) return street;
            var (shown, sun, lights, glow) = Dress(diner.Look, street.ShownLooks, street.Sun, street.Lights, street.Glow);
            return street with { ShownLooks = shown, Sun = sun, Lights = lights, Glow = glow };
        }

        private static (IReadOnlyList<string> Shown, float[] Sun, IReadOnlyDictionary<StreetLight, StreetLighting> Lights,
            IReadOnlyDictionary<StreetLight, IReadOnlyDictionary<string, float>> Glow) Dress(Models.Diners.DinerLook? look,
            IReadOnlyList<string> shownLooks, float[] sun, IReadOnlyDictionary<StreetLight, StreetLighting> streetLights,
            IReadOnlyDictionary<StreetLight, IReadOnlyDictionary<string, float>> streetGlow)
        {
            if (look == null) return (shownLooks, sun, streetLights, streetGlow);
            var shown = look.Show.Count > 0 ? SceneLooks.Prefixes(look.Show) : shownLooks;

            var lights = streetLights;
            if (look.Lights.Count > 0 || look.LampTints.Count > 0)
            {
                var merged = new Dictionary<StreetLight, StreetLighting>(streetLights);
                foreach (var light in look.Lights.Keys.Union(look.LampTints.Keys))
                {
                    // The street's light at that hour as the street shows it: the day's when its file leaves the hour out
                    var streets = SceneLighting.For(streetLights, light);
                    var lamps = new List<StreetLamp>(look.LampTints.TryGetValue(light, out var tint) ? streets.Lamps.Select(l => l with { Color = tint }) : streets.Lamps);
                    var own = look.Lights.TryGetValue(light, out var lighting) ? lighting : null;
                    if (own != null) lamps.AddRange(own.Lamps);
                    merged[light] = (own != null && !look.LampsOnly.Contains(light) ? own : streets) with { Lamps = lamps };
                }

                lights = merged;
            }

            var glow = streetGlow;
            if (look.Glow.Count > 0)
            {
                var merged = new Dictionary<StreetLight, IReadOnlyDictionary<string, float>>(streetGlow);
                foreach (var (light, materials) in look.Glow)
                {
                    var at = new Dictionary<string, float>(SceneLighting.GlowAt(streetGlow, light), StringComparer.Ordinal);
                    foreach (var (material, level) in materials) at[material] = level;
                    merged[light] = at;
                }

                glow = merged;
            }

            return (shown, look.Sun ?? sun, lights, glow);
        }

        /// <summary>The diner's name in the player's words: its key, else <c>diner.name.&lt;id&gt;</c>, else its street's title, else "the diner"</summary>
        public string NameOf(Models.Diners.Diner diner) =>
            SayFirst(NameKeys(diner)) ?? SceneOf(diner)?.Title ?? GameText.Say("diner.title");

        /// <summary>The line about the place for the map's card; empty with none</summary>
        public string AboutOf(Models.Diners.Diner diner) => SayFirst(AboutKeys(diner)) ?? string.Empty;

        /// <summary>The part of town the diner is in, for the map's card; empty with none</summary>
        public string RegionOf(Models.Diners.Diner diner) => SayFirst(RegionKeys(diner)) ?? string.Empty;

        /// <summary>The words of the first of <paramref name="keys"/> the text has; null when it has none of them</summary>
        private static string? SayFirst(IEnumerable<string> keys)
        {
            var text = GameText.Installed;
            return keys.FirstOrDefault(text.Has) is { } key ? GameText.Say(key) : null;
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

            // A look's layout and way in are written on the street the diner is seen on now: its own, else the first's. Kept
            // with the look, so the diners seen on a mod's street that takes the first's place below (written on the base
            // game's) keep their light and glow there, not their spots and ways
            var firstStreet = diners.Count > 0 && diners[0].Street is { } at && streets.Contains(at) ? at : scenes.LastOrDefault()?.Id;
            foreach (var diner in diners)
            {
                if (diner.Look != null)
                    diner.Look = diner.Look with { Street = diner.Street != null && streets.Contains(diner.Street) ? diner.Street : firstStreet };
            }

            // A mod's street with a diner on it that no diner names takes the beginners' diner's place when that one is the base
            // game's, wearing the street's own look: a mod that ships a street with a diner.json and nothing more has its
            // diner where everybody starts out
            var named = new HashSet<string>(diners.Select(d => d.Street).OfType<string>(), StringComparer.OrdinalIgnoreCase);
            if (diners.Count > 0 && ModIds.IsBase(diners[0].ModId)
                && scenes.LastOrDefault(s => !ModIds.IsBase(ModIds.ModOf(s.Id)) && !named.Contains(s.Id)) is { } own)
            {
                Logger.Information("Diners: the mod street {Street} has a diner on it that no diner names: the first diner {Id} is seen there", own.Id, diners[0].Id);
                diners[0].Street = own.Id;
                diners[0].Look = null;
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

        private sealed record ApproachEntry(List<float[]>? Arrive, List<float[]>? BackOut, List<float[]>? Leave, float? LookWhileParked,
            float? LookUpWhileParked);

        /// <summary>A look's own way to the diner; null, and told, when a way of it goes nowhere (two points a metre apart at least)</summary>
        private static Models.Diners.DinerApproach? ApproachOf(JsonElement lookElement, string where, List<string> problems)
        {
            if (!ModJson.TryGet(lookElement, "approach", out var element) || element.ValueKind != JsonValueKind.Object) return null;

            ApproachEntry? entry;
            try
            {
                entry = element.Deserialize<ApproachEntry>(ModJson.Options);
            }
            catch (JsonException ex)
            {
                problems.Add($"{where}: the look's approach does not read ({ex.Message}); the street's own");
                return null;
            }

            var (arrive, backOut, leave) = (ScenePaths.Points(entry?.Arrive), ScenePaths.Points(entry?.BackOut), ScenePaths.Points(entry?.Leave));
            if (!ScenePaths.GoesSomewhere(arrive) || !ScenePaths.GoesSomewhere(backOut) || !ScenePaths.GoesSomewhere(leave))
            {
                problems.Add($"{where}: the look's approach has no way in, back out or out (two points each at least, a metre apart); the street's own");
                return null;
            }

            var look = entry!.LookWhileParked is { } l && float.IsFinite(l) ? l : 0f;
            var up = entry.LookUpWhileParked is { } u && float.IsFinite(u) ? Math.Clamp(u, -15f, 15f) : 0f;
            return new Models.Diners.DinerApproach(arrive, backOut, leave, look, up);
        }

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

                // A light that gives no brightness is no light of its own: it only recolours the street's lamps, or adds some
                var lampsOnly = new HashSet<StreetLight>();
                if (ModJson.TryGet(lookElement, "lights", out var lightsElement) && lightsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var light in lightsElement.EnumerateObject())
                    {
                        if (light.Value.ValueKind == JsonValueKind.Object && !ModJson.TryGet(light.Value, "brightness", out _)
                            && SceneLighting.TryParse(light.Name, out var which))
                            lampsOnly.Add(which);
                    }
                }

                StreetRecipe? layout = null;
                if (ModJson.TryGet(lookElement, "layout", out var layoutElement) && layoutElement.ValueKind != JsonValueKind.Null)
                {
                    if (layoutElement.ValueKind == JsonValueKind.Object) layout = StreetRecipe.Read(layoutElement, $"{where}: the look", problems);
                    else problems.Add($"{where}: the look's layout is no object ({{ \"spots\": [...], ... }}); the street's own");
                }

                diner.Look = new Models.Diners.DinerLook(
                    (look?.Show ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList(),
                    look?.Sun is { Length: 3 } sun && sun.All(float.IsFinite) ? sun : null,
                    SceneLighting.Read(look?.Lights, entry.Id ?? where, Logger),
                    lampTints,
                    SceneLighting.MaterialGlows(look?.Glow))
                {
                    LampsOnly = lampsOnly,
                    Layout = layout,
                    Approach = ApproachOf(lookElement, where, problems)
                };

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
