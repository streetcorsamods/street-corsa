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
    /// stays in the save, and is back with its mod.
    /// </summary>
    public static class StreetLayouts
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("StreetLayout");

        // The streets whose missing pieces have been told this run
        private static readonly HashSet<string> ToldMissing = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The pieces of <paramref name="street"/> as <paramref name="state"/> has them, laid out now (and kept in it) when
        /// it has none yet; none for a street without a recipe. Whichever screen wants it first (the diner, the Cruise
        /// screen), the street comes out the same: its recipe keeps the ways the cars drive clear itself.
        /// </summary>
        public static IReadOnlyList<PlacedPiece> For(GameState state, StreetScene street, StreetKit kit)
        {
            if (street.Recipe is not { } recipe) return [];

            var saved = state.Streets.FirstOrDefault(s => string.Equals(s.Street, street.Id, StringComparison.OrdinalIgnoreCase));
            if (saved == null || IsOutdated(saved, recipe, kit))
            {
                var seed = saved?.Seed ?? SeedOf(state, street.Id);
                var again = saved != null;
                if (saved == null)
                {
                    saved = new StreetLayoutState { Street = street.Id, Seed = seed };
                    state.Streets.Add(saved);
                }

                saved.Recipe = recipe.Fingerprint;
                saved.Pieces = StreetLayout.Generate(recipe, kit, seed);
                Logger.Information("The street {Street} laid out {Again}from seed {Seed}: {Count} pieces", street.Id, again ? "again " : "", seed, saved.Pieces.Count);
            }

            var missing = saved.Pieces.Select(p => p.Piece).Where(p => kit.Find(p) == null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool told;
            lock (ToldMissing) told = !ToldMissing.Add(street.Id);
            if (missing.Count > 0 && !told)
                Logger.Warning("The street {Street} has pieces no mod has now, left out: {Pieces}", street.Id, string.Join(", ", missing));

            return missing.Count == 0 ? saved.Pieces : saved.Pieces.Where(p => kit.Find(p.Piece) != null).ToList();
        }

        /// <summary>
        /// A saved layout to lay out again: of another recipe than the street has now, or laid out with nothing (the kit had
        /// none of its pieces then) while the kit has some of them now
        /// </summary>
        private static bool IsOutdated(StreetLayoutState saved, StreetRecipe recipe, StreetKit kit) =>
            !string.Equals(saved.Recipe, recipe.Fingerprint, StringComparison.Ordinal)
            || saved.Pieces.Count == 0 && recipe.Words.Any(w => kit.Knows(w));

        /// <summary>
        /// The recipes' words no piece of the kit is of or tagged with, a line a street (the mods' problems): a slot of
        /// such a word only stays empty
        /// </summary>
        public static IReadOnlyList<string> UnknownWords(IEnumerable<StreetScene> streets, StreetKit kit) =>
            streets.Where(s => s.Recipe != null)
                .Select(s => (s.Id, Words: s.Recipe!.Unknown(kit)))
                .Where(s => s.Words.Count > 0)
                .Select(s => $"the street {s.Id}'s layout takes {string.Join(", ", s.Words)}, which no piece of the street kit is or is tagged with")
                .ToList();

        /// <summary>
        /// The street (the Cruise screen's) with its pieces as the save has them (<see cref="For"/>), its lamp posts lit at
        /// night; as it is when it has no recipe
        /// </summary>
        public static StreetScene Laid(StreetScene street, GameState state, StreetKit? kit = null)
        {
            if (street.Recipe is not { } recipe) return street;
            kit ??= StreetKit.Installed;
            var pieces = For(state, street, kit);
            return street with { Pieces = pieces, Lights = WithLamps(street.Lights, StreetLayout.Lamps(pieces, kit, recipe.Lamp)) };
        }

        /// <summary>
        /// The diner's scene with the pieces of the street it is on (the street.json beside its diner.json), as
        /// <see cref="Laid(StreetScene, GameState, StreetKit?)"/>; as it is when that street has no recipe
        /// </summary>
        public static DinerScene Laid(DinerScene scene, GameState state, IReadOnlyList<StreetScene>? streets = null, StreetKit? kit = null)
        {
            var folder = Path.GetFullPath(scene.Folder);
            var street = (streets ?? StreetScenes.Installed).FirstOrDefault(s => string.Equals(Path.GetFullPath(s.Folder), folder, StringComparison.OrdinalIgnoreCase));
            if (street?.Recipe is not { } recipe) return scene;
            kit ??= StreetKit.Installed;
            var pieces = For(state, street, kit);
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
        /// career started), else, in a save from before it had one, one of its name; mixed with the street's id, so each
        /// street of a save is its own
        /// </summary>
        public static int SeedOf(GameState state, string street)
        {
            var save = state.StreetSeed != 0 ? (uint)state.StreetSeed : StableHash.Fnv(state.SaveName);
            return (int)(StableHash.Fnv($"{save:x8}|{street.ToLowerInvariant()}") & int.MaxValue);
        }
    }
}
