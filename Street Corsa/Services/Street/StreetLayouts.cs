using System.IO;
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
    /// diner's corner is a street of its own.
    /// </summary>
    public static class StreetLayouts
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("StreetLayout");

        // The streets (and diners' streets) whose missing pieces have been told this run
        private static readonly HashSet<string> ToldMissing = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The pieces of <paramref name="street"/> as <paramref name="state"/> has them (for <paramref name="diner"/>, when
        /// its look lays out its own), laid out now (and kept in it) when it has none yet; none for a street without a
        /// recipe. Whichever screen wants it first (the diner, the Cruise screen), the street comes out the same: its recipe
        /// keeps the ways the cars drive clear itself.
        /// </summary>
        public static IReadOnlyList<PlacedPiece> For(GameState state, StreetScene street, StreetKit kit, Models.Diners.Diner? diner = null)
        {
            if (RecipeOf(street, diner) is not { } recipe) return [];

            // A diner whose look lays nothing of its own sees the street as its own recipe lays it out
            var dinerId = diner?.Look?.Layout != null ? diner.Id : string.Empty;
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

            return missing.Count == 0 ? saved.Pieces : saved.Pieces.Where(p => kit.Find(p.Piece) != null).ToList();
        }

        /// <summary>The recipe a street is laid out by for a diner: the street's, with the diner's look's over it when it has one</summary>
        public static StreetRecipe? RecipeOf(StreetScene street, Models.Diners.Diner? diner) =>
            street.Recipe is not { } recipe ? null : diner?.Look?.Layout is { } look ? recipe.Over(look) : recipe;

        /// <summary>
        /// A saved layout to lay out again: of another recipe than the street has now, or laid out with nothing (the kit had
        /// none of its pieces then) while the kit has some of them now
        /// </summary>
        private static bool IsOutdated(StreetLayoutState saved, StreetRecipe recipe, StreetKit kit) =>
            !string.Equals(saved.Recipe, recipe.Fingerprint, StringComparison.Ordinal)
            || saved.Pieces.Count == 0 && recipe.Words.Any(w => kit.Knows(w));

        /// <summary>
        /// The recipes' words no piece of the kit is of or tagged with, a line a street and a diner's look (the mods'
        /// problems): a slot of such a word only stays empty
        /// </summary>
        public static IReadOnlyList<string> UnknownWords(IEnumerable<StreetScene> streets, StreetKit kit, IEnumerable<Models.Diners.Diner>? diners = null) =>
            streets.Where(s => s.Recipe != null).Select(s => (Whose: $"the street {s.Id}'s layout", Recipe: s.Recipe!))
                .Concat((diners ?? []).Where(d => d.Look?.Layout != null).Select(d => (Whose: $"the diner {d.Id}'s look", Recipe: d.Look!.Layout!)))
                .Select(r => (r.Whose, Words: r.Recipe.Unknown(kit)))
                .Where(r => r.Words.Count > 0)
                .Select(r => $"{r.Whose} takes {string.Join(", ", r.Words)}, which no piece of the street kit is or is tagged with")
                .ToList();

        /// <summary>
        /// The street (the Cruise screen's, a seller's curb) with its pieces as the save has them for the diner whose look it
        /// wears (<see cref="For"/>, <see cref="DinerBook.WornBy"/>), its lamp posts lit at night; as it is when it has no recipe
        /// </summary>
        public static StreetScene Laid(StreetScene street, GameState state, Models.Diners.Diner? diner = null, StreetKit? kit = null)
        {
            if (RecipeOf(street, diner) is not { } recipe) return street;
            kit ??= StreetKit.Installed;
            var pieces = For(state, street, kit, diner);
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
            var pieces = For(state, street, kit, diner);
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
            return (int)(StableHash.Fnv($"{save:x8}|{of}") & int.MaxValue);
        }
    }
}
