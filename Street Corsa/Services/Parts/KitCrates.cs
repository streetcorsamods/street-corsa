using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Services.Parts
{
    /// <summary>
    /// The engine kits on the player's shelf (<see cref="Player.Crates"/>): a crate names its parts,
    /// which lie on the shelf as loose parts do. Whatever took one of them off the shelf broke the crate open: what is left
    /// of it is loose (<see cref="Tidy"/>).
    /// </summary>
    public static class KitCrates
    {
        /// <summary>
        /// An engine taken apart (<see cref="KitWork.TearDown"/>) for sale as a used kit: the ad with its parts, the
        /// engine's name and what it is missing, for whoever sells it (a junkyard, a rival, anybody in the paper) to price,
        /// sign and date; and what was kept back, for them to trade in. Here and not in <see cref="KitWork"/>, which the
        /// tools compile without the save's ads.
        /// </summary>
        public static (KitAd Ad, List<PartInstance> Kept) UsedKitAd(PartsCatalog catalog, PartInstance engine, Random random, int mostKept)
        {
            var (parts, kept) = KitWork.TearDown(catalog, engine, random, mostKept);
            return (new KitAd { Parts = parts, Name = KitWork.EngineNameOf(catalog, parts), Missing = KitWork.NamesOf(catalog, kept) }, kept);
        }

        /// <summary>The parts on the shelf in one crate, put there with them; the crate</summary>
        public static KitCrate Pack(Player player, IReadOnlyList<PartInstance> parts, string buildId, string size, string name,
            IEnumerable<string>? missing = null, bool isNew = false)
        {
            foreach (var part in parts)
            {
                part.ParentSlot = 0;
                part.OwnSlot = 0;
            }

            player.Parts.AddRange(parts);
            var crate = new KitCrate
            {
                BuildId = buildId,
                Size = size,
                Name = name,
                Missing = missing?.ToList() ?? [],
                PartIds = parts.Select(p => p.InstanceId).ToList(),
                New = isNew
            };
            player.Crates.Add(crate);
            return crate;
        }

        /// <summary>The crate's parts on the shelf, in its order; fewer than it names once it has been broken into</summary>
        public static List<PartInstance> PartsOf(Player player, KitCrate crate)
        {
            var shelf = player.Parts.GroupBy(p => p.InstanceId).ToDictionary(g => g.Key, g => g.First());
            return (crate.PartIds ?? []).Where(shelf.ContainsKey).Select(id => shelf[id]).ToList();
        }

        /// <summary>
        /// The crates still whole: every part they name on the shelf, and none in two crates. The others are opened, their
        /// parts left loose. True when one was.
        /// </summary>
        public static bool Tidy(Player player)
        {
            var crates = player.Crates ??= [];
            var shelf = player.Parts.Select(p => p.InstanceId).ToHashSet();
            var taken = new HashSet<Guid>();
            return crates.RemoveAll(c => c == null || c.PartIds is not { Count: > 0 } ids || !ids.All(shelf.Contains) || !ids.All(taken.Add)) > 0;
        }

        /// <summary>The ids of the parts in whole crates: the shelf shows them in their crates, not loose</summary>
        public static HashSet<Guid> Crated(Player player)
        {
            Tidy(player);
            return player.Crates.SelectMany(c => c.PartIds).ToHashSet();
        }

        /// <summary>The crate opened: its parts are loose on the shelf</summary>
        public static bool Unpack(Player player, KitCrate crate) => player.Crates.Remove(crate);

        /// <summary>
        /// The crate's parts put together into an engine (<see cref="KitWork.Assemble"/>), off the shelf: the crate is gone,
        /// what found no place stays on the shelf. Null when they make no engine (no block among them).
        /// </summary>
        public static PartInstance? Assemble(PartsCatalog catalog, Player player, KitCrate crate)
        {
            var parts = PartsOf(player, crate);
            if (KitWork.Assemble(catalog, parts) is not { } built) return null;

            // Off the shelf what is on the engine; what found no place, or came off again for lacking what it needs, stays
            var placed = built.Engine.SelfAndDescendants().Select(p => p.InstanceId).ToHashSet();
            player.Parts.RemoveAll(p => placed.Contains(p.InstanceId) && parts.Contains(p));

            player.Crates.Remove(crate);
            return built.Engine;
        }
    }
}
