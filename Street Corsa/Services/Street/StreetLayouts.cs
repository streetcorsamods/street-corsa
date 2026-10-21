using System.Globalization;
using System.IO;
using System.Numerics;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Diner;

namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// The streets as a save has them laid out (step 39): a street with a recipe is laid out the first time it is wanted,
    /// from the save's seed and the street's id, and the layout is kept in the save (<see cref="GameState.Streets"/>), so a
    /// mod's pieces updated later move nothing. A street whose recipe has changed since (<see cref="StreetRecipe.Fingerprint"/>:
    /// a street rebuilt, a mod's recipe fixed), or that was laid out while the kit had none of its pieces, is laid out
    /// again, from the same seed. A piece a removed mod took away is left out of what is drawn, and told once a run; it
    /// stays in the save, and is back with its mod. A diner whose look has a layout of its own
    /// (<see cref="Models.Diners.DinerLook.Layout"/>) sees the street laid out for it: the street's recipe with the look's
    /// over it, from a seed of the diner's own, kept beside the street's (<see cref="StreetLayoutState.Diner"/>), so each
    /// diner's corner is a street of its own. The recipe's daily slots (the cars at the curbs) are not kept: they are drawn
    /// each time the street is, from the layout's seed and the game's date (<see cref="DaySeedOf"/>), so the cars are the
    /// same all day and others the next; the seeds are told in the log alone.
    /// </summary>
    public static class StreetLayouts
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("StreetLayout");

        // The streets (and diners' streets) whose missing pieces have been told this run
        private static readonly HashSet<string> ToldMissing = new(StringComparer.OrdinalIgnoreCase);

        // The day each street's (and diner's street's) layout, by its seed, last had its day's draw told this run
        private static readonly Dictionary<string, DateTime> ToldDay = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The pieces of <paramref name="street"/> as <paramref name="state"/> has them (for <paramref name="diner"/>, when
        /// its look lays out its own), laid out now (and kept in it) when it has none yet; none for a street without a
        /// recipe. Whichever screen wants it first (the diner, the Cruise screen), the street comes out the same: its recipe
        /// keeps the ways the cars drive clear itself. The day's pieces of its daily slots come after the kept ones.
        /// </summary>
        public static IReadOnlyList<PlacedPiece> For(GameState state, StreetScene street, StreetKit kit, Models.Diners.Diner? diner = null) =>
            RecipeOf(street, diner) is { } recipe ? For(state, street, kit, diner, recipe) : [];

        private static IReadOnlyList<PlacedPiece> For(GameState state, StreetScene street, StreetKit kit, Models.Diners.Diner? diner, StreetRecipe recipe)
        {
            // A diner whose look lays nothing of its own on this street sees the street as its own recipe lays it out
            var dinerId = LaysOwn(street, diner) ? diner!.Id : string.Empty;
            var name = dinerId.Length == 0 ? street.Id : $"{street.Id} for {dinerId}";
            var saved = state.Streets.FirstOrDefault(s => string.Equals(s.Street, street.Id, StringComparison.OrdinalIgnoreCase)
                                                          && string.Equals(s.Diner ?? string.Empty, dinerId, StringComparison.OrdinalIgnoreCase));
            if (saved == null || IsOutdated(saved, recipe, kit))
            {
                var seed = saved?.Seed ?? SeedOf(state, street.Id, dinerId);
                var again = saved != null;
                if (saved == null)
                {
                    saved = new StreetLayoutState { Street = street.Id, Diner = dinerId, Seed = seed };
                    state.Streets.Add(saved);
                }

                saved.Recipe = recipe.Fingerprint;
                saved.Pieces = StreetLayout.Generate(recipe, kit, seed);
                Logger.Information("The street {Street} laid out {Again}from seed {Seed}: {Count} pieces", name, again ? "again " : "", seed, saved.Pieces.Count);
            }

            var missing = saved.Pieces.Select(p => p.Piece).Where(p => kit.Find(p) == null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool told;
            lock (ToldMissing) told = !ToldMissing.Add(name);
            if (missing.Count > 0 && !told)
                Logger.Warning("The street {Street} has pieces no mod has now, left out: {Pieces}", name, string.Join(", ", missing));

            var kept = missing.Count == 0 ? saved.Pieces : saved.Pieces.Where(p => kit.Find(p.Piece) != null).ToList();
            if (!recipe.HasDaily) return kept;

            var day = DaySeedOf(saved.Seed, state.Date);
            var today = StreetLayout.Daily(recipe, kit, kept, day);
            bool toldDay;
            lock (ToldDay)
            {
                var key = $"{name}|{saved.Seed}";
                toldDay = ToldDay.TryGetValue(key, out var last) && last == state.Date.Date;
                ToldDay[key] = state.Date.Date;
            }

            if (!toldDay)
                Logger.Information("The street {Street}'s daily pieces for {Date} drawn from seed {Seed}: {Count} pieces", name, state.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), day, today.Count);
            return kept.Concat(today).ToList();
        }

        /// <summary>
        /// The seed a street's daily slots are drawn from on a day: its layout's seed mixed with the date (its day, not the
        /// hour), so a street's curb cars are the same all day long and others the next
        /// </summary>
        public static int DaySeedOf(int seed, DateTime date) => Mix((uint)seed, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        // A seed mixed with a text into one of its own
        private static int Mix(uint seed, string with) => (int)(StableHash.Fnv($"{seed:x8}|{with}") & int.MaxValue);

        /// <summary>
        /// The recipe a street is laid out by for a diner: the street's, with the diner's look's over it when it has one written
        /// on this street (<see cref="Models.Diners.DinerLook.FitsOn"/>), and the look's way in, back out and out kept clear of
        /// curb cars (<see cref="ClearOf"/>; its fingerprint the way's too, so a way moved lays the street out again)
        /// </summary>
        public static StreetRecipe? RecipeOf(StreetScene street, Models.Diners.Diner? diner)
        {
            if (street.Recipe is not { } recipe) return null;
            if (!LaysOwn(street, diner)) return recipe;

            var look = diner!.Look!;
            var over = recipe.Over(look.Layout!);
            if (look.Approach is not { } way) return over;
            var paths = new[] { way.Arrive, way.BackOut, way.Leave };
            var drawn = string.Join(";", paths.Select(p => string.Join(" ", p.Select(g =>
                g.X.ToString("R", CultureInfo.InvariantCulture) + "," + g.Z.ToString("R", CultureInfo.InvariantCulture)))));
            return over with
            {
                Clear = over.Clear.Concat(paths.SelectMany(ClearOf)).ToList(),
                Fingerprint = StableHash.Fnv($"{over.Fingerprint}|{drawn}").ToString("x8")
            };
        }

        // Whether the diner's look lays a layout of its own on the street: it has one, written on this street
        private static bool LaysOwn(StreetScene street, Models.Diners.Diner? diner) =>
            diner?.Look is { Layout: not null } look && look.FitsOn(street.Id);

        // How far either side of a way the curb cars keep (a car's half width and some), and the longest box along it
        private const float WayHalf = 2f;
        private const float WayStep = 4f;

        /// <summary>
        /// The boxes along a way the player's car drives where no curb car is put: one every 4 metres of it at most (a box
        /// round a slanted stretch is no wider than a lane so), 2 metres either side
        /// </summary>
        public static IEnumerable<ClearArea> ClearOf(IReadOnlyList<Services.Station.GroundPoint> way)
        {
            var only = new HashSet<string>(["curb_car"], StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 1 < way.Count; i++)
            {
                var (a, b) = (new Vector2(way[i].X, way[i].Z), new Vector2(way[i + 1].X, way[i + 1].Z));
                var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / WayStep));
                for (var n = 0; n < steps; n++)
                {
                    var (from, to) = (Vector2.Lerp(a, b, n / (float)steps), Vector2.Lerp(a, b, (n + 1) / (float)steps));
                    yield return new ClearArea(Vector2.Min(from, to) - new Vector2(WayHalf), Vector2.Max(from, to) + new Vector2(WayHalf), only);
                }
            }
        }

        /// <summary>
        /// A saved layout to lay out again: of another recipe than the street has now, or laid out with nothing (the kit had
        /// none of its kept slots' pieces then) while the kit has some of them now
        /// </summary>
        private static bool IsOutdated(StreetLayoutState saved, StreetRecipe recipe, StreetKit kit) =>
            !string.Equals(saved.Recipe, recipe.Fingerprint, StringComparison.Ordinal)
            || saved.Pieces.Count == 0 && recipe.KeptWords.Any(w => kit.Knows(w));

        /// <summary>
        /// The recipes' words no piece of the kit is of or tagged with, a line a street and a diner's look (the mods'
        /// problems): a slot of such a word only stays empty
        /// </summary>
        /// <remarks>
        /// And a diner's look's slot names and drops its street has no slot of (<see cref="StreetRecipe.NameProblems"/>), on the
        /// street the look is written on
        /// </remarks>
        public static IReadOnlyList<string> UnknownWords(IEnumerable<StreetScene> streets, StreetKit kit, IEnumerable<Models.Diners.Diner>? diners = null)
        {
            var all = streets.ToList();
            var looks = (diners ?? []).Where(d => d.Look?.Layout != null).ToList();
            var words = all.Where(s => s.Recipe != null).Select(s => (Whose: $"the street {s.Id}'s layout", Recipe: s.Recipe!))
                .Concat(looks.Select(d => (Whose: $"the diner {d.Id}'s look", Recipe: d.Look!.Layout!)))
                .Select(r => (r.Whose, Words: r.Recipe.Unknown(kit)))
                .Where(r => r.Words.Count > 0)
                .Select(r => $"{r.Whose} takes {string.Join(", ", r.Words)}, which no piece of the street kit is or is tagged with");
            var names = looks.SelectMany(d => all.FirstOrDefault(s => s.Recipe != null && d.Look!.Street != null && d.Look.FitsOn(s.Id)) is { } street
                ? street.Recipe!.NameProblems(d.Look!.Layout!).Select(p => $"the diner {d.Id}'s look on the street {street.Id}: {p}")
                : []);
            return words.Concat(names).ToList();
        }

        /// <summary>
        /// Takes the layouts out of the save no screen lays out any more (<see cref="For"/>): of a street that is gone, of a
        /// diner that is gone or whose look lays nothing of its own on that street now, and the street's own once every diner
        /// seen on it lays its own; true when any went. Nothing is lost: a layout is drawn from the save's seed and the
        /// street's and diner's ids, so one wanted again (its mod back) is laid out as it was.
        /// </summary>
        public static bool Prune(GameState state, IEnumerable<StreetScene> streets, DinerBook book)
        {
            var byId = streets.Where(s => s.Recipe != null).GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            bool Wanted(StreetLayoutState saved)
            {
                if (!byId.TryGetValue(saved.Street ?? string.Empty, out var street)) return false;
                var seen = book.All.Where(d => ReferenceEquals(book.WornBy(street, d), d)).ToList();
                return string.IsNullOrEmpty(saved.Diner)
                    ? seen.Count == 0 || seen.Any(d => !LaysOwn(street, d))
                    : seen.Any(d => LaysOwn(street, d) && string.Equals(d.Id, saved.Diner, StringComparison.OrdinalIgnoreCase));
            }

            var gone = state.Streets.RemoveAll(s => !Wanted(s));
            if (gone > 0) Logger.Information("{Count} street layout(s) no screen lays out any more taken out of the save", gone);
            return gone > 0;
        }

        /// <summary>
        /// The street (the Cruise screen's, a seller's curb) with its pieces as the save has them for the diner whose look it
        /// wears (<see cref="For"/>, <see cref="DinerBook.WornBy"/>), its lamp posts lit at night; as it is when it has no recipe
        /// </summary>
        public static StreetScene Laid(StreetScene street, GameState state, Models.Diners.Diner? diner = null, StreetKit? kit = null)
        {
            if (RecipeOf(street, diner) is not { } recipe) return street;
            kit ??= StreetKit.Installed;
            var pieces = For(state, street, kit, diner, recipe);
            return street with { Pieces = pieces, Lights = WithLamps(street.Lights, StreetLayout.Lamps(pieces, kit, recipe.Lamp)) };
        }

        /// <summary>
        /// The diner's scene with the pieces of the street it is on (the street.json beside its diner.json), laid out for
        /// <paramref name="diner"/>, as <see cref="Laid(StreetScene, GameState, Models.Diners.Diner?, StreetKit?)"/>; as it is
        /// when that street has no recipe
        /// </summary>
        public static DinerScene Laid(DinerScene scene, GameState state, Models.Diners.Diner? diner = null, IReadOnlyList<StreetScene>? streets = null,
            StreetKit? kit = null)
        {
            var folder = Path.GetFullPath(scene.Folder);
            var street = (streets ?? StreetScenes.Installed).FirstOrDefault(s => string.Equals(Path.GetFullPath(s.Folder), folder, StringComparison.OrdinalIgnoreCase));
            if (street == null || RecipeOf(street, diner) is not { } recipe) return scene;
            kit ??= StreetKit.Installed;
            var pieces = For(state, street, kit, diner, recipe);
            return scene with { Pieces = pieces, Lights = WithLamps(scene.Lights, StreetLayout.Lamps(pieces, kit, recipe.Lamp)) };
        }

        /// <summary>
        /// The street's lights with the layout's lamps lit too: at night, and at any other light the street lights lamps
        /// at; a street with no light of the night of its own gets none (its night is its day)
        /// </summary>
        public static IReadOnlyDictionary<StreetLight, StreetLighting> WithLamps(IReadOnlyDictionary<StreetLight, StreetLighting> lights,
            IReadOnlyList<StreetLamp> lamps)
        {
            if (lamps.Count == 0) return lights;
            var lit = new Dictionary<StreetLight, StreetLighting>(lights);
            foreach (var (light, lighting) in lights)
                if (light == StreetLight.Night || lighting.Lamps.Count > 0) lit[light] = lighting with { Lamps = lighting.Lamps.Concat(lamps).ToList() };
            return lit;
        }

        /// <summary>
        /// The seed a street of a save is laid out from: the save's own (<see cref="GameState.StreetSeed"/>, drawn when the
        /// career started), else, in a save from before it had one, one of its name; mixed with the street's id (and the
        /// diner's it is laid out for), so each street of a save, and each diner's, is its own
        /// </summary>
        public static int SeedOf(GameState state, string street, string? diner = null)
        {
            var save = state.StreetSeed != 0 ? (uint)state.StreetSeed : StableHash.Fnv(state.SaveName);
            var of = string.IsNullOrEmpty(diner) ? street.ToLowerInvariant() : $"{street.ToLowerInvariant()}|{diner.ToLowerInvariant()}";
            return Mix(save, of);
        }
    }
}
