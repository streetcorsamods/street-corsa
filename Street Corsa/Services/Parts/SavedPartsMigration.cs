using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Services.Parts
{
    /// <summary>
    /// Brings the parts of a loaded save in step with the catalog (<see cref="ICarPartsService.BringUpToDate"/>): a pack
    /// replaced by a later release gave its parts new ids and moved some slots, a mod the save was played with may not be
    /// installed. Parts of the player that no longer fit where they were go on the shelf; a car whose engine the catalog
    /// no longer has gets its factory engine again; parts and cars for sale that are gone leave the offer, in the paper,
    /// on the lots, in the junkyards; and what is on its way to the player (the mail order, a machine shop) goes by the
    /// parts' ids of today, or is gone with them.
    /// </summary>
    internal sealed class SavedPartsMigration(PartsCatalog catalog, IAppLogger logger)
    {
        private readonly PartsCatalog _catalog = catalog;
        private readonly IAppLogger _logger = logger;

        /// <returns>True when the save changed</returns>
        public bool BringUpToDate(GameState game)
        {
            var changed = 0;

            // The engine on the stand as the engines in the cars: one of a mod that is not installed waits on the
            // shelf, one the catalog has lost for good is gone, and what no longer fits on it comes off to the shelf
            if (game.Player.EngineStand is { } stand)
            {
                if (_catalog.Get(stand.DefinitionId) == null)
                {
                    game.Player.EngineStand = null;
                    changed++;
                    if (Mods.ModIds.IsQualified(stand.DefinitionId)) game.Player.Parts.Add(stand);
                    _logger.Information("The {Block} on the engine stand is not in the catalog: {Where}", stand.DefinitionId,
                        Mods.ModIds.IsQualified(stand.DefinitionId) ? "it waits on the shelf for its mod" : "it is gone");
                }
                else
                {
                    var off = new List<PartInstance>();
                    changed += Renew(new[] { stand }, off);
                    game.Player.Parts.AddRange(off);
                }
            }

            var lost = new List<string>();
            var racers = new[] { game.Player }
                .Concat(game.Racers.Inactive.Values).Concat(game.Racers.Retired.Values).Concat(game.Racers.ReadyToRace.Values)
                .Concat(game.Racers.Departed?.Values ?? Enumerable.Empty<Racer>());
            foreach (var racer in racers)
            {
                // An engine the catalog no longer knows (its parts were left out of the content) is gone from the
                // car, which gets its factory engine the next time it is looked at, worn like the car. One of a mod
                // that is not installed goes on the owner's shelf whole: it is back in the game with the mod.
                var keep = new List<PartInstance>();
                foreach (var car in racer.Cars)
                {
                    if (car.Engine is not { } engine || _catalog.Get(engine.DefinitionId) != null) continue;

                    car.Parts.Remove(engine);
                    car.HasPartsAssigned = false;
                    changed++;
                    if (!Mods.ModIds.IsQualified(engine.DefinitionId))
                    {
                        _logger.Information("{Car} had a {Block} the catalog no longer has: gets its factory engine again", car.DefinitionId, engine.DefinitionId);
                        continue;
                    }

                    engine.ParentSlot = 0;
                    engine.OwnSlot = 0;
                    keep.Add(engine);
                    _logger.Information("{Car} had a {Block} of a mod that is not installed: it waits on the shelf, the car gets its factory engine",
                        car.DefinitionId, engine.DefinitionId);
                }

                // What comes off a car goes on its owner's shelf, once the shelf itself has been gone through. A mod's
                // part the catalog does not know stays there (the workbench does not show it) till the mod is back.
                // A drum of a save of before the backing plates goes onto its plate, its shoes and wheel cylinder with it,
                // before the joints are checked: they would take those off a drum that has no slot for them now
                foreach (var car in racer.Cars) changed += RunningGearFactory.PutDrumsOnPlates(_catalog, car.Parts);

                var shelf = new List<PartInstance>();
                changed += DropLost(racer.Parts, $"{racer.Name}'s shelf", lost);
                racer.Parts.AddRange(keep);
                changed += Renew(racer.Cars.SelectMany(c => c.Parts).Concat(racer.Parts), shelf);
                racer.Parts.AddRange(shelf);
            }

            // Cars and parts for sale: what no longer fits is not part of the offer, in the paper, on the lots or in a yard
            changed += game.UsedCarMarket.RemoveAll(l => l.Parts.Any(p => _catalog.Get(p.DefinitionId) == null));
            changed += game.NewspaperAds.Parts.RemoveAll(ad => _catalog.Get(ad.Part.DefinitionId) == null);
            foreach (var shop in game.PartShops) changed += shop.Yard?.RemoveAll(ad => _catalog.Get(ad.Part.DefinitionId) == null) ?? 0;

            foreach (var listing in game.UsedCarMarket) changed += RunningGearFactory.PutDrumsOnPlates(_catalog, listing.Parts);
            var forSale = game.UsedCarMarket.SelectMany(l => l.Parts)
                .Concat(game.NewspaperAds.Parts.Select(ad => ad.Part));
            changed += Renew(forSale, new List<PartInstance>());

            // The part shops' shelves and yards, and what is on its way to the player, under the ids the parts go by now;
            // an order or a job of a part that is gone for good is gone with it (one of a mod not installed waits)
            changed += PartShops.PartShopService.RenameShelves(_catalog, game.PartShops);
            changed += Renew(game.PartShops.SelectMany(shop => (shop.Yard ?? []).Select(ad => ad.Part)), new List<PartInstance>());

            changed += DropLost(game.Player.PartOrders, o => o.Part, "the mail order", lost);
            changed += DropLost(game.Player.MachineWork, o => o.Part, "the machine shops", lost);
            // What comes off a part on its way to the player was paid for: it goes on the shelf, as off the engine stand
            var coming = new List<PartInstance>();
            changed += Renew(game.Player.PartOrders.Select(o => o.Part).Concat(game.Player.MachineWork.Select(o => o.Part)), coming);
            game.Player.Parts.AddRange(coming);

            if (lost.Count > 0)
            {
                Mods.ModProblems.Add("Saved parts",
                    $"{lost.Count} part(s) of the save are not in the parts catalog and have no alias; they are gone: {string.Join(", ", lost.Distinct(StringComparer.Ordinal))}");
            }

            if (changed > 0) _logger.Information("{Count} change(s) to the save's parts to keep up with the parts catalog", changed);
            return changed > 0;
        }

        /// <summary>
        /// Takes the parts the catalog does not know and no alias names out of <paramref name="parts"/>, each told in the log
        /// and added to <paramref name="lost"/>; a part of a mod that is not installed (a qualified id) stays, for the mod
        /// </summary>
        private int DropLost(List<PartInstance> parts, string where, List<string> lost) => DropLost(parts, p => p, where, lost);

        private int DropLost<T>(List<T> items, Func<T, PartInstance> partOf, string where, List<string> lost)
        {
            return items.RemoveAll(item =>
            {
                var part = partOf(item);
                if (_catalog.Get(part.DefinitionId) != null || Mods.ModIds.IsQualified(part.DefinitionId)) return false;

                _logger.Warning("{Part} ({Id}) on {Where} is not in the parts catalog and has no alias: it is gone", part.DefinitionId, part.InstanceId, where);
                lost.Add(part.DefinitionId);
                return true;
            });
        }

        private int Renew(IEnumerable<PartInstance> roots, List<PartInstance> loose)
        {
            var changed = 0;
            foreach (var root in roots.ToList())
            {
                var before = loose.Count;
                if (!SavedParts.BringUpToDate(_catalog, root, loose)) continue;

                changed++;
                foreach (var part in loose.Skip(before)) _logger.Information("{Part} no longer fits on {Root} and came off", part.DefinitionId, root.DefinitionId);
            }

            return changed;
        }
    }
}
