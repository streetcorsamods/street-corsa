using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// The rivals' tuning: a few racers a day put their money into their engines (<see cref="EngineTuner"/>), the rear
    /// axle first once the engine makes the power for it (<see cref="AxleTuner"/>), from the shops' shelves and the paper
    /// </summary>
    public partial class OpponentLifeService
    {
        /// <summary>A few racers with money to spare put it into their engines; the parts are tried on the dyno off the UI thread</summary>
        private async Task TuneAsync(GameState gameState, DateTime date, Review review, List<string> talk)
        {
            if (!_parts.IsAvailable) return;

            // Whether a part is affordable is the tuner's to find out, part by part at the shop's prices
            var bargains = (gameState.PartShops ?? []).Any(s => s.Status == DealerStatus.ClosingDown
                                                             && _partShops?.Catalog.Get(s.Id) is { } shop && PartShopRules.SellsToTuners(shop));
            var chance = bargains ? OpponentRules.SaleTuneChance : OpponentRules.TuneChance;
            var jobs = gameState.Racers.ReadyToRace.Values
                .Concat(gameState.Racers.Inactive.Values.Where(r => r is Opponent { IsBoss: true }))
                .OfType<Opponent>()
                .Where(r => !HasPendingRace(gameState, r))
                .Where(r => r.Cars.FirstOrDefault() is { Engine: not null } car && CanRace(car, review) && _random.NextDouble() < chance)
                .Select(r => (Racer: r, Budget: OpponentRules.TuningBudget(r.Money, ValueOf(r.Cars[0], review))))
                .Where(r => r.Budget > 0)
                .OrderBy(_ => _random.Next())
                .Take(OpponentRules.MaxTunersPerDay)
                .Select(r => new TuneJob(r.Racer, r.Racer.Cars[0], r.Racer.Cars[0].Engine!.InstanceId, PartTrees.Clone(r.Racer.Cars[0].Engine!, keepIds: true),
                    r.Budget, _random.Next(),
                    // The paper as it is this morning, copied: the tuner reads it off this thread
                    RivalPartAds.OffersFor(gameState, r.Racer, date).Select(o => o with { Part = PartTrees.Clone(o.Part, keepIds: true) }).ToList(),
                    // A racer keeps the engine cool as it makes more: the radiator goes up with the power
                    _parts.EngineCoolingOf(r.Racer.Cars[0])))
                .ToList();
            if (jobs.Count == 0) return;

            var catalog = _parts.Catalog;
            var builds = _parts.Builds;
            var prices = gameState.Rules.PartPriceMultiplier;

            // New parts from the shops' shelves as they stand this morning, the cheapest that has each; else by mail
            var shelves = _partShops?.RivalPrices(gameState, date);
            Func<StreetCorsa.Parts.PartDefinition, decimal>? newPrice = shelves == null ? null : shelves.PriceOf;

            // Once the engine makes the power for them, a posi and gears go in before more power: one thing a day
            jobs = jobs.Where(job => !TuneAxle(gameState, job, catalog, newPrice ?? (p => EngineTuner.NewPrice(p, prices)), shelves, date, review, talk)).ToList();
            if (jobs.Count == 0) return;

            var results = await Task.Run(() => jobs.Select(job =>
            {
                try
                {
                    // One thing at a time: a racer works on the car over the weeks, not in a day
                    return EngineTuner.TuneUp(catalog, builds, job.Engine, job.Budget, prices, new Random(job.Seed), maxUpgrades: 1, used: job.Used,
                        coolingOf: job.CoolingOf, newPrice: newPrice);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Tuning {Racer}'s engine failed: {Error}", job.Racer.Name, ex.Message);
                    return null;
                }
            }).ToList());

            for (var i = 0; i < jobs.Count; i++)
            {
                var (racer, car, engineId, _, _, _, _, _) = jobs[i];
                var result = results[i];
                if (result == null) continue;

                // Still their car, still that engine, still the money, still the part in the paper: the day may have
                // moved on meanwhile (another racer bought it this morning)
                var index = car.Parts.FindIndex(p => p.InstanceId == engineId);
                if (!racer.Cars.Contains(car) || index < 0 || racer.Money < result.Cost) continue;
                if (result.Upgrades.Any(u => u.UsedAdId is { } id && gameState.NewspaperAds.Parts.All(a => a.AdId != id))) continue;

                car.Parts[index] = result.Engine;
                racer.Money -= result.Cost;
                if (shelves != null) TakeFromShelves(gameState, racer, car, jobs[i], result, shelves, review, talk);
                foreach (var upgrade in result.Upgrades)
                {
                    if (upgrade.UsedAdId is { } adId) RivalPartAds.Take(gameState, adId);

                    // What came off goes into the paper under their name, paid for when it sells (or at a shop's
                    // trade-in when the ad runs out); a shop takes at once what is worth nothing there
                    foreach (var removed in upgrade.Removed)
                    {
                        if (RivalPartAds.Post(gameState, catalog, racer, removed, date, _random.NextDouble(), _partShops?.Catalog.All) is { } ad)
                            _logger.Information("{Racer} put the {Part} in the paper for ${Price}", racer.Name, removed.DefinitionId, ad.AskingPrice);
                        else
                            racer.Money += PartPricing.TradeIn(catalog, removed);
                    }
                }

                car.PowerHp = MarketPower(result.Report);
                if (review.GroupOf != null) CarCondition.RefreshFigures(car, review.GroupOf);

                foreach (var upgrade in result.Upgrades)
                {
                    var key = upgrade.Group switch
                    {
                        EngineTuner.BlockGroup => "street.tuned_block",
                        EngineTuner.CoolingGroup => "street.tuned_cooling",
                        _ => "street.tuned"
                    };
                    talk.Add(Say(review, key, racer, car,
                        new TextValues { ["change"] = upgrade.Change, ["hpBefore"] = upgrade.PowerBefore, ["hpAfter"] = upgrade.PowerAfter }));
                }

                _logger.Information("{Racer} tuned the {Car}: {Upgrades}; ${Cost} spent, ${Money} left",
                    racer.Name, car.DefinitionId, string.Join(", ", result.Upgrades.Select(u => u.Change)), result.Cost, racer.Money);
            }
        }

        /// <summary>
        /// The new parts a tuned engine got (<see cref="TuneUpgrade.Fitted"/>) came off the shops' shelves: one fewer of
        /// each at the shop it was cheapest at. A part bought used, or a swapped-in engine (bought used whole), took
        /// nothing off a shelf, though a radiator put on with the swap did; one the shop ran out of this morning came by
        /// mail at no more than was paid.
        /// </summary>
        private void TakeFromShelves(GameState gameState, Opponent racer, Car car, TuneJob job, TuneResult result, RivalPriceList shelves, Review review, List<string> talk)
        {
            if (_partShops == null) return;

            var used = job.Used.SelectMany(o => o.Part.SelfAndDescendants()).Select(p => p.InstanceId).ToHashSet();
            foreach (var part in result.Upgrades.SelectMany(u => u.Fitted).Where(p => !used.Contains(p.InstanceId)))
            {
                if (!shelves.TryGetShop(part.DefinitionId, out var shopId, out var price)) continue;
                if (!_partShops.TakeForRival(gameState, shopId, part.DefinitionId)) continue;

                // A bargain at a closing-down sale is worth a word on the street
                if (PartShopRoster.Find(gameState, shopId) is { Status: DealerStatus.ClosingDown } shop)
                {
                    var name = _parts.Catalog.Get(part.DefinitionId) is { } definition ? definition.DisplayName ?? definition.Name : part.DefinitionId;
                    talk.Add(Say(review, "street.partshop_bargain", racer, car, new TextValues { ["part"] = name, ["shop"] = shop.Name, ["price"] = price }));
                }
            }
        }

        /// <summary>
        /// Puts the next part on the racer's rear axle when the engine makes the power for it (<see cref="AxleTuner"/>): a
        /// posi, then shorter gears, from the shops' shelves; what came off goes into the paper. True when it did.
        /// </summary>
        private bool TuneAxle(GameState gameState, TuneJob job, StreetCorsa.Parts.PartsCatalog catalog, Func<StreetCorsa.Parts.PartDefinition, decimal> price,
            RivalPriceList? shelves, DateTime date, Review review, List<string> talk)
        {
            var (racer, car) = (job.Racer, job.Car);
            if (AxleTuner.NextPart(catalog, car.Parts, car.PowerHp ?? 0, Math.Min(job.Budget, racer.Money), price) is not { } part) return false;

            var cost = price(part);
            if (AxleTuner.Fit(catalog, car.Parts, part) is not { } removed) return false;

            racer.Money -= cost;
            if (shelves != null && _partShops != null && shelves.TryGetShop(part.Id, out var shopId, out _)) _partShops.TakeForRival(gameState, shopId, part.Id);
            if (RivalPartAds.Post(gameState, catalog, racer, removed, date, _random.NextDouble(), _partShops?.Catalog.All) is null)
                racer.Money += PartPricing.TradeIn(catalog, removed);

            var name = part.DisplayName ?? part.Name;
            talk.Add(Say(review, "street.tuned_axle", racer, car, new TextValues { ["change"] = name }));
            _logger.Information("{Racer} put a {Part} in the {Car}'s rear axle for ${Cost}, ${Money} left", racer.Name, part.Id, car.DefinitionId, cost, racer.Money);
            return true;
        }

        private sealed record TuneJob(Opponent Racer, Car Car, Guid EngineId, PartInstance Engine, decimal Budget, int Seed, List<UsedPartOffer> Used,
            Func<PartInstance, double, double>? CoolingOf);
    }
}
