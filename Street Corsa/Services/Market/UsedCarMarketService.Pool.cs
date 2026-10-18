using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Catalog;

namespace StreetCorsa.Services.Market
{
    /// <summary>
    /// The pool the market draws from and the draw: every sellable car with its place in the market, each dealer's slice
    /// of it filled to its own target, the private people's cars from the whole of it
    /// </summary>
    public partial class UsedCarMarketService
    {
        /// <summary>
        /// The cars for sale, still without their engines.
        ///
        /// Each dealer is filled to its own target rather than the market being spawned whole and then cut
        /// down. Cutting a list built in catalog order kept whichever cars happened to come first and starved
        /// every dealer whose kind of car came later - a lot with nothing on it, while another held a hundred.
        /// </summary>
        private (List<UsedCarListing> Listings, List<PoolEntry> Pool) CreateListings(List<DealerLocation> dealers, DateTime currentDate, bool privateAds)
        {
            _logger.Information("Spawning used car market listings for date {Date}", currentDate);

            var activeCars = _catalogRepo.GetCarsByStatus(ContentStatus.Active);
            _logger.Information("Found {CarCount} active cars to spawn from", activeCars.Count);

            var pool = BuildPool(activeCars);
            if (pool.Count == 0)
            {
                _logger.Warning("No car has a profile to price it; the market stays empty");
                return ([], pool);
            }

            var listings = new List<UsedCarListing>();
            foreach (var dealer in dealers)
            {
                listings.AddRange(FillDealer(dealer, StockWantedBy(dealer), Between(BackLotLow, BackLotHigh), pool, currentDate));
            }

            if (privateAds) listings.AddRange(FillPrivate(Between(PrivateAdsLow, PrivateAdsHigh), pool, currentDate));

            _logger.Information("Spawned {ListingCount} listings across {DealerCount} dealers and the paper",
                listings.Count, dealers.Count);

            return (listings, pool);
        }

        /// <summary>A car that may turn up for sale, and where it sits in the market</summary>
        private readonly record struct PoolEntry(CarDefinition Car, CarProfile Profile, float Rank);

        /// <summary>
        /// Every sellable car with its place in the market worked out once: the share of cars it is dearer
        /// than. A share rather than a position between the cheapest and the dearest, because one
        /// half-million-dollar car in the install would otherwise push everything else into the bottom tenth
        /// and leave the smart showroom with an empty floor.
        /// </summary>
        private List<PoolEntry> BuildPool(List<CarDefinition> activeCars)
        {
            // The catalog outlives the install: a car deleted from content/cars stays on its books, and one that
            // is not there cannot be sold. It would take a place on a lot, show a card, and stand as an invisible
            // gap - which is what emptied the dearest lots, since the cars that were cleared out were the
            // expensive ones.
            var installed = InstalledCars.Only(activeCars, out var uninstalled);
            if (uninstalled > 0)
            {
                _logger.Warning("{Count} cars in the catalog are not installed and cannot be sold", uninstalled);
            }

            // One visit to the catalog for every profile, not one per car: this runs every game day
            var profiles = _profileRepo.GetAllProfiles().ToDictionary(p => p.CarDefinitionId, StringComparer.OrdinalIgnoreCase);

            var priced = new List<(CarDefinition Car, CarProfile Profile)>();
            foreach (var car in installed)
            {
                if (profiles.TryGetValue(car.Id, out var profile) && profile.BasePrice > 0) priced.Add((car, profile));
            }

            return Ranked(priced.Select(p => new PoolEntry(p.Car, p.Profile, 0f)).ToList());
        }

        /// <summary>The cars with their places among each other: the share of them each is dearer than</summary>
        private static List<PoolEntry> Ranked(List<PoolEntry> cars)
        {
            if (cars.Count == 0) return [];

            var ladder = cars.Select(e => e.Profile.BasePrice).OrderBy(p => p).ToList();
            var pool = new List<PoolEntry>(cars.Count);

            foreach (var entry in cars)
            {
                var below = CountBelow(ladder, entry.Profile.BasePrice);
                var rank = ladder.Count == 1 ? 0.5f : (float)below / (ladder.Count - 1);
                pool.Add(entry with { Rank = Math.Clamp(rank, 0f, 1f) });
            }

            return pool;
        }

        /// <summary>How many prices of the sorted ladder are below <paramref name="price"/>: the first index not below it</summary>
        private static int CountBelow(List<decimal> ladder, decimal price)
        {
            int low = 0, high = ladder.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (ladder[middle] < price) low = middle + 1;
                else high = middle;
            }

            return low;
        }

        /// <summary>How many cars this dealer means to have out, from its own definition</summary>
        private int StockWantedBy(DealerLocation dealer)
        {
            var definition = _dealerCatalog?.Get(dealer.Id);
            if (definition == null) return _random.Next(DefaultStockLow, DefaultStockHigh + 1);

            var low = Math.Max(1, definition.StockLow);
            var high = Math.Max(low, definition.StockHigh);
            return _random.Next(low, high + 1);
        }

        /// <summary>
        /// Stocks one lot with <paramref name="wanted"/> cars, and <paramref name="outBack"/> the dealer keeps out back for
        /// the paper. Cars are drawn from the slice of the market the dealer deals in, worked out once for both, weighted by
        /// how common they are, and no model turns up more than twice on the same lot.
        /// </summary>
        private List<UsedCarListing> FillDealer(DealerLocation dealer, int wanted, int outBack, List<PoolEntry> pool, DateTime currentDate)
        {
            if (wanted <= 0 && outBack <= 0) return [];

            var definition = _dealerCatalog?.Get(dealer.Id);

            // A lot that deals in one kind of car (imports) takes its price band of the cars of that kind, the share of
            // them it is dearer than; none of that kind installed, it deals in any
            // A model is weighed at its price new and its spec sheet; each car drawn, at what the lot asks for it
            var kind = definition?.Cars;
            if (kind != null)
            {
                var ofKind = pool.Where(e => kind.MatchesOffer(e.Car, null, e.Profile.BasePrice, null)).ToList();
                if (ofKind.Count > 0) pool = Ranked(ofKind);
                else
                {
                    _logger.Warning("{Dealer} deals in {Kind} and no installed car is one; it takes any", dealer.Name, kind.DisplayDescription);
                    kind = null;
                }
            }

            var candidates = definition == null
                ? pool
                : pool.Where(e => e.Rank >= definition.PriceBandLow && e.Rank <= definition.PriceBandHigh).ToList();

            if (candidates.Count == 0 && definition != null)
            {
                // Nothing installed sits in this dealer's slice: let it take whatever comes nearest instead of
                // standing empty
                var middle = (definition.PriceBandLow + definition.PriceBandHigh) / 2f;
                candidates = pool.OrderBy(e => Math.Abs(e.Rank - middle)).Take(Math.Max(4, wanted + outBack)).ToList();
                _logger.Warning("{Dealer} deals in {Low:0.00}-{High:0.00} of the market and nothing installed fits; " +
                    "taking the nearest {Count}", dealer.Name, definition.PriceBandLow, definition.PriceBandHigh, candidates.Count);
            }

            UsedCarListing? Make(PoolEntry entry, bool backLot)
            {
                var listing = CreateListing(entry.Car, entry.Profile, dealer, currentDate, backLot);
                return kind != null && !kind.MatchesOffer(entry.Car, null, listing.Price, listing.PowerHp) ? null : listing;
            }

            return [.. Draw(candidates, wanted, e => Make(e, backLot: false)), .. Draw(candidates, outBack, e => Make(e, backLot: true))];
        }

        /// <summary>
        /// The private people's cars in the paper: any car of the market, weighted by how common it is, as its owner left
        /// it and asking less than a lot would
        /// </summary>
        private List<UsedCarListing> FillPrivate(int wanted, List<PoolEntry> pool, DateTime currentDate) =>
            wanted <= 0 ? [] : Draw(pool, wanted, entry => CreatePrivateListing(entry.Car, entry.Profile, currentDate));

        /// <summary>
        /// <paramref name="wanted"/> cars drawn from <paramref name="candidates"/> by how common each is, no model more than
        /// twice; <paramref name="make"/> lists one, or turns it down (null)
        /// </summary>
        private List<UsedCarListing> Draw(List<PoolEntry> candidates, int wanted, Func<PoolEntry, UsedCarListing?> make)
        {
            if (candidates.Count == 0 || wanted <= 0) return [];

            var listings = new List<UsedCarListing>(wanted);
            var used = new Dictionary<string, int>();

            // A car's precedence is how likely it is to turn up at all, so it is the weight to draw by
            var weights = candidates.Select(e => Math.Max(0.02f, e.Profile.DealerPrecedence)).ToList();
            var total = weights.Sum();

            for (var attempt = 0; attempt < wanted * 8 && listings.Count < wanted; attempt++)
            {
                var roll = (float)(_random.NextDouble() * total);
                var index = 0;
                while (index < weights.Count - 1 && roll > weights[index])
                {
                    roll -= weights[index];
                    index++;
                }

                var entry = candidates[index];
                used.TryGetValue(entry.Car.Id, out var already);
                if (already >= MaxSameModelPerDealer) continue;

                if (make(entry) is not { } listing) continue;

                used[entry.Car.Id] = already + 1;
                listings.Add(listing);
            }

            return listings;
        }
    }
}
