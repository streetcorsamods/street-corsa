using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// The rivals in the car market: what is for sale to them (the lots and the other rivals' ads), buying a first car,
    /// trading up, shopping a dealer's closing-down sale, and buying itself
    /// </summary>
    public partial class OpponentLifeService
    {
        /// <summary>A car for sale as a buyer weighs it: off a lot, or out of a rival's ad</summary>
        private sealed record ForSale(decimal Price, double Hp, double Condition, UsedCarListing? Listing, RivalCarAds.Offer? Ad);

        /// <summary>
        /// Everything for sale a racer could buy with <paramref name="budget"/>: the lots and the other rivals' ads. The
        /// private people's cars and the dealers' out back (<see cref="UsedCarListing.PaperOnly"/>) are the player's finds:
        /// the rivals' living was tuned against the lots, and is still worked out from them.
        /// </summary>
        private List<ForSale> CarsForSale(GameState gameState, Opponent racer, decimal budget, Review review)
        {
            List<ForSale> all =
            [
                .. gameState.UsedCarMarket
                    .Where(l => l.IsOnALot && l.Price > 0 && l.Price <= budget)
                    .Select(l => new ForSale(l.Price, l.PowerHp ?? 0, l.Condition, l, null)),
                .. LiveAds(gameState, review)
                    .Where(o => !ReferenceEquals(o.Seller, racer) && o.Ad.AskingPrice > 0 && o.Ad.AskingPrice <= budget)
                    .Where(o => !HasPendingRace(gameState, o.Seller) && RivalCarAds.StillStands(gameState, o))
                    .Select(o => new ForSale(o.Ad.AskingPrice, o.Car.PowerHp ?? 0, CarValuation.ConditionOf(o.Car), null, o))
            ];
            if (racer.Drives is not { } kind) return all;

            // A racer who drives one kind of car buys one of that kind; with none for sale, one with no car of his kind
            // that runs takes what he can get, and one who has one keeps it
            var liked = all.Where(c => c.Listing is { } listing ? Likes(kind, listing, review)
                : Definition(c.Ad!.Car.DefinitionId, review) is { } car && kind.MatchesOffer(car, c.Ad.Car, c.Price, c.Ad.Car.PowerHp)).ToList();
            return liked.Count > 0 || racer.Cars.Any(c => Likes(kind, c, review) && CanRace(c, review)) ? liked : all;
        }

        /// <summary>A car for sale is of the racer's kind at what it is offered for and the power it is quoted at</summary>
        private bool Likes(Models.Career.Filters.ICarFilter kind, UsedCarListing listing, Review review) =>
            Definition(listing.CarDefinitionId, review) is { } car && kind.MatchesOffer(car, null, listing.Price, listing.PowerHp);

        /// <summary>A car of his own, or out of an ad, at what it is worth</summary>
        private bool Likes(Models.Career.Filters.ICarFilter kind, Car owned, Review review) =>
            Definition(owned.DefinitionId, review) is { } car && kind.MatchesOffer(car, owned, ValueOf(owned, review), owned.PowerHp);

        /// <summary>A car's definition, found once in a review</summary>
        private Models.Catalog.CarDefinition? Definition(string id, Review review)
        {
            var known = review.Definitions ??= new Dictionary<string, Models.Catalog.CarDefinition?>(StringComparer.OrdinalIgnoreCase);
            if (!known.TryGetValue(id, out var car)) known[id] = car = _catalogRepo.GetCar(id);
            return car;
        }

        /// <summary>
        /// The rivals' ads that stood when first asked for in this review: found once, not per racer who shops. An ad
        /// that has come down since is checked where it is used (<see cref="RivalCarAds.StillStands"/>); one put up
        /// since starts the list over.
        /// </summary>
        private static List<RivalCarAds.Offer> LiveAds(GameState gameState, Review review) => review.LiveAds ??= RivalCarAds.Live(gameState);

        /// <summary>The best car on the lots and in the paper for the racer's money, as <see cref="OpponentRules.PurchaseScore"/> weighs them</summary>
        private bool TryBuy(GameState gameState, Opponent racer, DateTime date, Review review, List<string> talk)
        {
            var budget = racer.Money;
            if (budget <= 0) return false;

            var pick = CarsForSale(gameState, racer, budget, review)
                .OrderByDescending(c => OpponentRules.PurchaseScore(c.Hp, c.Condition, c.Hp > 0, c.Price, budget))
                .FirstOrDefault();
            return pick != null && Buy(gameState, racer, pick, date, review, talk);
        }

        /// <summary>
        /// Now and then a racer with money to spare buys a clearly stronger car (<see cref="OpponentRules.WorthTradingUp"/>)
        /// and drives it; the old one goes into the paper
        /// </summary>
        private void TryTradeUp(GameState gameState, Opponent racer, DateTime date, Review review, List<string> talk)
        {
            if (racer.IsBoss || racer.Cars.FirstOrDefault() is not { } current) return;

            var currentHp = current.PowerHp ?? 0;
            var pick = CarsForSale(gameState, racer, racer.Money, review)
                .Where(c => c.Hp > 0 && OpponentRules.WorthTradingUp(currentHp, c.Hp, c.Price, racer.Money))
                // A rival's car is there to look at: one that can't race is no step up
                .Where(c => c.Ad == null || CanRace(c.Ad.Car, review))
                .OrderByDescending(c => OpponentRules.PurchaseScore(c.Hp, c.Condition, true, c.Price, racer.Money))
                .FirstOrDefault();
            if (pick == null || !Buy(gameState, racer, pick, date, review, talk)) return;

            // A car off a lot that turns out not fit to race: they keep driving the old one, and it stays out of the paper
            PickBestCar(racer, review);
            if (ReferenceEquals(racer.Cars[0], current)) return;

            if (RivalCarAds.AdFor(gameState, current) == null && !current.IsImpounded) Advertise(gameState, racer, current, date, review, talk);
        }

        // ----- closing-down sales -----

        /// <summary>
        /// A dealer's closing-down sale draws the racers with money (<see cref="OpponentRules.BargainChance"/>): one buys
        /// a stronger car cheap and puts the old one in the paper, another buys one to put straight back in the paper
        /// for more (<see cref="OpponentRules.WorthFlipping"/>, in the sale's last days). A few a day at most
        /// (<see cref="OpponentRules.MaxBargainsPerDay"/>), so there is something left when the player gets there.
        /// </summary>
        private void ShopTheSales(GameState gameState, DateTime date, Review review, List<string> talk)
        {
            if (!AnyOnSale(gameState)) return;

            var shoppers = gameState.Racers.ReadyToRace.Values.OfType<Opponent>()
                .Where(r => !r.IsBoss && !r.IsBroke && r.Money > 0 && !HasPendingRace(gameState, r))
                .OrderBy(_ => _random.Next())
                .ToList();

            var bought = 0;
            foreach (var racer in shoppers)
            {
                if (bought >= OpponentRules.MaxBargainsPerDay || !AnyOnSale(gameState)) break;

                // One car a day is enough for anybody, and most days they have other things to do
                if (racer.Cars.Any(c => c.PurchaseDate.Date == date.Date) || _random.NextDouble() >= OpponentRules.BargainChance) continue;

                try
                {
                    if (TryBargain(gameState, racer, date, review, talk)) bought++;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "{Racer} could not shop the closing-down sale", racer.Name);
                }
            }
        }

        private static bool AnyOnSale(GameState gameState) => OnSale(gameState).Any();

        /// <summary>The cars marked down on the lots of the dealers closing down</summary>
        private static IEnumerable<UsedCarListing> OnSale(GameState gameState)
        {
            var closing = Services.Dealers.DealerRoster.ClosingDown(gameState).Select(d => d.Id).ToHashSet();
            return closing.Count == 0
                ? []
                : (gameState.UsedCarMarket ?? []).Where(l => l.IsOnALot && l.PriceBeforeSale != null && closing.Contains(l.DealerLocation));
        }

        private bool TryBargain(GameState gameState, Opponent racer, DateTime date, Review review, List<string> talk)
        {
            var current = racer.Cars.FirstOrDefault();
            var currentHp = current?.PowerHp ?? 0;
            var affordable = OnSale(gameState).Where(l => OpponentRules.Affordable(l.Price, racer.Money)).ToList();
            if (affordable.Count == 0) return false;

            // A stronger car than theirs, cheap, of the kind they drive (one bought to sell on can be any)
            var step = affordable
                .Where(l => (l.PowerHp ?? 0) >= Math.Max(1, currentHp) * OpponentRules.BargainTradeUpGain)
                .Where(l => racer.Drives is not { } kind || Likes(kind, l, review))
                .OrderByDescending(l => OpponentRules.PurchaseScore(l.PowerHp ?? 0, l.Condition, (l.PowerHp ?? 0) > 0, l.Price, racer.Money))
                .FirstOrDefault();
            if (step != null)
            {
                if (!Buy(gameState, racer, ForSaleOf(step), date, review, talk, "street.bargain")) return false;

                PickBestCar(racer, review);
                if (current != null && !ReferenceEquals(racer.Cars[0], current) && RivalCarAds.AdFor(gameState, current) == null && !current.IsImpounded)
                {
                    Advertise(gameState, racer, current, date, review, talk);
                }

                return true;
            }

            // Or one to sell on, with a car of their own to keep driving and room for another: the deepest cut first
            if (current == null || racer.Cars.Count >= OpponentRules.MaxCars) return false;
            var flip = affordable
                .Where(l => OpponentRules.WorthFlipping(l.Price, LeastAsked(gameState, l, date, review)))
                .OrderByDescending(l => (l.PriceBeforeSale ?? 0) - l.Price)
                .FirstOrDefault();
            if (flip == null || !Buy(gameState, racer, ForSaleOf(flip), date, review, talk, "street.bargain_flip")) return false;

            // Bought to sell, as the street was told: they keep driving their own, and this one goes in the paper
            var bought = racer.Cars[0];
            racer.Cars.RemoveAt(0);
            racer.Cars.Add(bought);
            Advertise(gameState, racer, bought, date, review, talk, quiet: true);
            return true;
        }

        /// <summary>The least a racer would ask in the paper for a car on a lot: what a flip has to beat</summary>
        private decimal LeastAsked(GameState gameState, UsedCarListing listing, DateTime date, Review review) =>
            AskingFor(gameState, ValueOf(CarPurchaseService.CarFrom(listing, date, string.Empty), review), 0);

        private static ForSale ForSaleOf(UsedCarListing listing) => new(listing.Price, listing.PowerHp ?? 0, listing.Condition, listing, null);

        /// <summary>
        /// The car is theirs. One off a lot that came without parts (an older listing) gets its factory ones with the rest of
        /// the morning's cars (<see cref="GiveCarsTheirPartsAsync"/>), on a worker, not here on the caller's thread.
        /// </summary>
        /// <param name="lotLine">What the street says about a car off a lot, when not the usual (a bargain at a sale)</param>
        private bool Buy(GameState gameState, Opponent racer, ForSale pick, DateTime date, Review review, List<string> talk, string? lotLine = null)
        {
            if (pick.Ad is { } offer)
            {
                var seller = offer.Seller;
                var bought = RivalCarAds.HandOver(gameState, offer.Ad, racer.Name, date);
                if (bought == null) return false;

                racer.Money -= offer.Ad.AskingPrice;
                racer.Cars.Insert(0, bought);
                racer.Stats.CarsOwned++;
                Grudges.CarBack(racer, bought);

                talk.Add(Say(review, "street.bought_ad", racer, bought, new TextValues { ["seller"] = seller.Name, ["price"] = offer.Ad.AskingPrice }));
                _logger.Information("{Racer} bought {Seller}'s {Car} for ${Price} ({Hp:0} hp), ${Money} left",
                    racer.Name, seller.Name, bought.DefinitionId, offer.Ad.AskingPrice, bought.PowerHp ?? 0, racer.Money);
                return true;
            }

            var listing = pick.Listing!;

            // Off the lot, exactly as the player would have had it
            listing.IsSold = true;
            listing.SoldDate = date;
            racer.Money -= listing.Price;

            var car = CarPurchaseService.CarFrom(listing, date, racer.Name);
            listing.Parts = [];

            racer.Cars.Insert(0, car);
            racer.Stats.CarsOwned++;
            Grudges.CarBack(racer, car);

            var dealer = gameState.DealerLocations?.FirstOrDefault(d => d.Id == listing.DealerLocation)?.Name;
            talk.Add(Say(review, dealer == null ? "street.bought" : lotLine ?? "street.bought_dealer", racer, car, new TextValues { ["dealer"] = dealer, ["price"] = listing.Price }));
            _logger.Information("{Racer} bought the {Car} for ${Price} ({Hp:0} hp), ${Money} left", racer.Name, car.DefinitionId, listing.Price, car.PowerHp ?? 0, racer.Money);
            return true;
        }
    }
}
