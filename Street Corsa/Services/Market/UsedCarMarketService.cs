using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.Market
{
    /// <summary>
    /// Implements used car market spawning and management
    /// </summary>
    public partial class UsedCarMarketService(IContentCatalogRepository catalogRepo, ICarProfileRepository profileRepo, ICarPartsService? partsService = null, IDealerCatalog? dealerCatalog = null) : IUsedCarMarketService
    {
        private readonly IContentCatalogRepository _catalogRepo = catalogRepo;
        private readonly ICarProfileRepository _profileRepo = profileRepo;
        private readonly ICarPartsService? _partsService = partsService;
        private readonly IDealerCatalog? _dealerCatalog = dealerCatalog;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Market");
        private readonly Random _random = new();

        // Market configuration
        private static int DefaultStockLow => Rules.RuleBook.Current.Market.StockLow;
        private static int DefaultStockHigh => Rules.RuleBook.Current.Market.StockHigh;

        /// <summary>The same model twice on one lot is a coincidence; three times is a car park</summary>
        private static int MaxSameModelPerDealer => Rules.RuleBook.Current.Market.MaxSameModelPerDealer;
        private static float MinCondition => Rules.RuleBook.Current.Market.MinCondition;
        private static float MaxCondition => Rules.RuleBook.Current.Market.MaxCondition;
        private static int MinMileage => Rules.RuleBook.Current.Market.MinMileage;
        private static int MaxMileage => Rules.RuleBook.Current.Market.MaxMileage;
        private static float PriceVariationPercent => Rules.RuleBook.Current.Market.PriceSpread;
        private static int PrivateAdsLow => Rules.RuleBook.Current.Market.PrivateAdsLow;
        private static int PrivateAdsHigh => Rules.RuleBook.Current.Market.PrivateAdsHigh;
        private static int BackLotLow => Rules.RuleBook.Current.Market.BackLotLow;
        private static int BackLotHigh => Rules.RuleBook.Current.Market.BackLotHigh;
        private static float BackLotMinCondition => Rules.RuleBook.Current.Market.BackLotMinCondition;
        private static float BackLotMaxCondition => Rules.RuleBook.Current.Market.BackLotMaxCondition;

        /// <summary>How hard a dealer pulls condition toward its own standard. At 1 the shift is the full
        /// distance between that standard and the middle of the range</summary>
        private static float DealerConditionPull => Rules.RuleBook.Current.Market.DealerConditionPull;

        public async Task<List<UsedCarListing>> SpawnListingsAsync(List<DealerLocation> dealers, DateTime currentDate, double priceMultiplier,
            bool privateAds = true)
        {
            var (listings, pool) = CreateListings(dealers, currentDate, privateAds);
            await AddEnginesAsync(listings, pool);
            ApplyPriceMultiplier(listings, priceMultiplier);
            Describe(listings);
            return listings;
        }

        /// <summary>The ads' few words (<see cref="AdBlurbs"/>), once the cars have their engines to talk about</summary>
        private void Describe(List<UsedCarListing> listings)
        {
            var catalog = _partsService is { IsAvailable: true } parts ? parts.Catalog : null;
            var factories = new Dictionary<string, IReadOnlySet<string>?>(StringComparer.OrdinalIgnoreCase);
            foreach (var listing in listings)
                AdBlurbs.Describe(listing, catalog, Remembered(factories, listing.CarDefinitionId, FactoryParts));
        }

        public IReadOnlySet<string>? FactoryParts(string carDefinitionId)
        {
            if (_partsService is not { IsAvailable: true } parts || _catalogRepo.GetCar(carDefinitionId) is not { } carDef) return null;

            try
            {
                return parts.GetStockBuild(carDef) is { } stock
                    ? stock.Build.Parts.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : null;
            }
            catch (Exception ex)
            {
                // The ad still gets its words; it just cannot tell the factory's parts from the rest
                _logger.Warning("Could not look up the factory build of a {CarId}: {Error}", carDefinitionId, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The difficulty's say in what the sellers ask (<see cref="GameRules.CarPriceMultiplier"/>): on the whole price,
        /// the engine's modifications included, once the listing is complete
        /// </summary>
        private static void ApplyPriceMultiplier(List<UsedCarListing> listings, double priceMultiplier)
        {
            foreach (var listing in listings)
                listing.Price = CarValuation.RoundToHundred(GameRules.Scale(listing.Price, priceMultiplier));
        }

        /// <summary>A number from <paramref name="low"/> to <paramref name="high"/>, both in; nothing below 0</summary>
        private int Between(int low, int high) => Math.Max(0, _random.Next(Math.Min(low, high), Math.Max(low, high) + 1));

        public async Task<List<UsedCarListing>> RefreshMarketAsync(List<UsedCarListing> currentListings, List<DealerLocation> dealers, DateTime currentDate, double priceMultiplier)
        {
            _logger.Information("Refreshing used car market for date {Date}", currentDate);

            // Remove old sold listings (older than 7 days)
            var oldSoldCutoff = currentDate.AddDays(-7);
            var filtered = currentListings.Where(l =>
                !l.IsSold || (l.SoldDate.HasValue && l.SoldDate.Value > oldSoldCutoff)).ToList();

            var removedSold = currentListings.Count - filtered.Count;
            if (removedSold > 0)
            {
                _logger.Information("Removed {Count} old sold listings", removedSold);
            }

            // Despawn old unsold listings (older than 14 days). Not a car in a closing-down sale: it stays until its lot
            // shuts, and goes with it (DealerLifeService)
            var unsoldCutoff = currentDate.AddDays(-14);
            var kept = filtered.Where(l =>
                l.IsSold || l.PriceBeforeSale != null || l.ListedDate > unsoldCutoff).ToList();

            var removedUnsold = filtered.Count - kept.Count;
            if (removedUnsold > 0)
            {
                _logger.Information("Despawned {Count} old unsold listings", removedUnsold);
            }

            // The cars in the paper only come and go like the rest: one that ran its time came down, new ones go in. The
            // day's count is drawn afresh, and on a day there are fewer the oldest went to somebody else, so the paper
            // holds anything from the least to the most
            var outBack = dealers.Select(_ => Between(BackLotLow, BackLotHigh)).ToList();
            for (var i = 0; i < dealers.Count; i++)
                TakeDown(kept, kept.Where(l => MayComeDown(l) && l.DealerLocation == dealers[i].Id).ToList(), outBack[i]);
            var privateAds = Between(PrivateAdsLow, PrivateAdsHigh);
            TakeDown(kept, kept.Where(l => MayComeDown(l) && l.IsPrivate).ToList(), privateAds);

            // Every lot is topped back up to its own target. Spawning a whole market and cutting it down to
            // size used to keep whichever cars came first in the catalog, which left some lots bare.
            // Engines only for the ones that make it into the market: putting one together is the costly part.
            var pool = BuildPool(_catalogRepo.GetCarsByStatus(ContentStatus.Active));
            if (pool.Count > 0)
            {
                var fresh = new List<UsedCarListing>();

                for (var i = 0; i < dealers.Count; i++)
                {
                    var dealer = dealers[i];
                    var onTheLot = kept.Count(l => l.IsOnLotOf(dealer.Id));
                    var keptBack = kept.Count(l => !l.IsSold && l.PaperOnly && l.DealerLocation == dealer.Id);
                    fresh.AddRange(FillDealer(dealer, StockWantedBy(dealer) - onTheLot, outBack[i] - keptBack, pool, currentDate));
                }

                fresh.AddRange(FillPrivate(privateAds - kept.Count(l => !l.IsSold && l.IsPrivate), pool, currentDate));

                if (fresh.Count > 0)
                {
                    await AddEnginesAsync(fresh, pool);
                    ApplyPriceMultiplier(fresh, priceMultiplier);
                    Describe(fresh);
                    kept.AddRange(fresh);
                }

                _logger.Information("Topped {Count} cars up across {Dealers} lots", fresh.Count, dealers.Count);
            }

            _logger.Information("Market refresh complete. Total listings: {Total}, Available: {Available}",
                kept.Count, kept.Count(l => !l.IsSold));

            return kept;
        }

        /// <summary>A car in the paper only that may come down: not one in its dealer's closing-down sale</summary>
        private static bool MayComeDown(UsedCarListing listing) => !listing.IsSold && listing.PaperOnly && listing.PriceBeforeSale == null;

        /// <summary>
        /// The oldest of <paramref name="ads"/> past <paramref name="wanted"/> come out of <paramref name="kept"/>: bought by
        /// somebody else, so the paper holds anything from the least to the most, not ever the most
        /// </summary>
        private void TakeDown(List<UsedCarListing> kept, List<UsedCarListing> ads, int wanted)
        {
            var surplus = ads.OrderBy(l => l.ListedDate).ThenBy(l => l.Id, StringComparer.Ordinal).Take(ads.Count - wanted).ToList();
            if (surplus.Count == 0) return;

            foreach (var listing in surplus) kept.Remove(listing);
            _logger.Information("Took {Count} cars out of the paper: bought by somebody else", surplus.Count);
        }

        public async Task<List<UsedCarListing>> SpawnPaperOnlyAsync(List<DealerLocation> dealers, DateTime currentDate, double priceMultiplier)
        {
            var pool = BuildPool(_catalogRepo.GetCarsByStatus(ContentStatus.Active));
            if (pool.Count == 0) return [];

            var listings = new List<UsedCarListing>();
            foreach (var dealer in dealers) listings.AddRange(FillDealer(dealer, 0, Between(BackLotLow, BackLotHigh), pool, currentDate));
            listings.AddRange(FillPrivate(Between(PrivateAdsLow, PrivateAdsHigh), pool, currentDate));

            await AddEnginesAsync(listings, pool);
            ApplyPriceMultiplier(listings, priceMultiplier);
            Describe(listings);
            _logger.Information("Put {Count} private people's and dealers' back-lot cars in the paper", listings.Count);
            return listings;
        }

        public List<UsedCarListing> GetAvailableListings(List<UsedCarListing> allListings)
        {
            return allListings.Where(l => !l.IsSold).ToList();
        }

        public List<UsedCarListing> GetListingsByDealer(List<UsedCarListing> allListings, string dealerLocationId)
        {
            return allListings.Where(l => l.IsOnLotOf(dealerLocationId)).ToList();
        }

        public List<DealerLocation> GetDefaultDealers()
        {
            // The dealers of the mods' dealer files: the same the map shows. The game has none of its own (the content is
            // the mods'): no dealer in any mod is a mod problem, for the player to see, and lots that stay empty
            if (_dealerCatalog == null) return [];

            try
            {
                var known = _dealerCatalog.ToLocations();
                if (known.Count > 0) return known;

                _logger.Warning("No mod has a car dealer: the lots stay empty");
                Mods.ModProblems.Add("Dealers", "no mod has a car dealer (a dealers folder); the lots stay empty");
                return known;
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not read the dealers: the lots stay empty ({Error})", ex.Message);
                return [];
            }
        }

        /// <summary>
        /// The least a car goes back on a lot for. A car the catalog cannot price and nobody paid for (no profile, a
        /// purchase price of 0) is valued at nothing, and a listing at $0 is a free car.
        /// </summary>
        private static decimal MinListPrice => Rules.RuleBook.Current.Market.MinListPrice;

        public UsedCarListing ListCar(Car car, decimal price, string location, DateTime listedDate, bool describeEngine = true)
        {
            // What a race did to the car is not always sane: a NaN odometer casts to int.MinValue, and an opponent's
            // health values carry a little noise above 1. The buyer gets the car at these numbers, so they are made sane here.
            var condition = CarValuation.ConditionOf(car);
            var listing = new UsedCarListing
            {
                Id = Guid.NewGuid().ToString(),
                CarDefinitionId = car.DefinitionId,
                Price = CarValuation.RoundToHundred(Math.Max(price, MinListPrice)),
                Mileage = double.IsFinite(car.OdometerKM) ? (int)Math.Clamp(car.OdometerKM, 0, int.MaxValue) : 0,
                Condition = (float)Unit.Clamp01(condition, ifNotFinite: 0),
                SkinId = string.IsNullOrEmpty(car.SkinId) ? "default" : car.SkinId,
                PaintColour = car.PaintColour,
                ListedDate = listedDate,
                DealerLocation = location,
                IsSold = false,

                // The car goes with everything on it; the buyer gets exactly this car
                Parts = car.Parts,
                HasRunningGearAssigned = car.HasRunningGearAssigned,
                GearVersion = car.GearVersion,
                BodyDamageKmh = CarCondition.BodyTotal(car) > 0 ? CarCondition.Body(car) : null,

                // Its past goes with it: who had it, how it raced
                History = car.History?.Copy() ?? new CarHistory(),
                CarInstanceId = car.InstanceId
            };
            CarLooks.CopyTo(car, listing);

            if (describeEngine && listing.Parts.Count > 0 && DescribeEngine(car) is { } engine)
            {
                listing.EngineSummary = engine.Summary;
                listing.PowerHp = engine.PowerHp;
                listing.IsModified = engine.IsModified;
            }

            // A car relisted gets its ad's words; one shown for a rival's ad has the ad's own
            if (describeEngine) Describe([listing]);
            return listing;
        }

        public EngineDescription? DescribeEngine(Car car)
        {
            if (car.Engine is not { } engine || _partsService is not { IsAvailable: true } parts) return null;

            try
            {
                var report = parts.Evaluate(car);
                var modified = _catalogRepo.GetCar(car.DefinitionId) is { } carDef && parts.GetStockBuild(carDef) is { } stock
                               && CarValuation.IsModified(engine, stock);
                return new EngineDescription(parts.Describe(engine, report), PowerOf(report), modified);
            }
            catch (Exception ex)
            {
                // The car still sells with its parts; the seller just has less to say about the engine
                _logger.Warning("Could not describe the engine of a {CarId}: {Error}", car.DefinitionId, ex.Message);
                return null;
            }
        }

        /// <summary>The dyno's horsepower; 0 for an engine that does not run</summary>
        public static double PowerOf(StreetCorsa.Parts.Logic.EngineReport? report) => report is { Runs: true } ? report.Dyno!.MaxPowerHp : 0;

        public decimal ValueOf(Car car) => CarValuation.WorthOf(car, _profileRepo.GetProfile, _partsService);

        public Func<Car, decimal> Valuer()
        {
            // Each model's profile read once, not once per car and per ask. Only that: the cars themselves change as the
            // review goes (a repair, a new engine) and are weighed each time.
            var profiles = new Dictionary<string, CarProfile?>(StringComparer.OrdinalIgnoreCase);
            return car => CarValuation.WorthOf(car, id => Remembered(profiles, id, _profileRepo.GetProfile), _partsService);
        }

        private static T? Remembered<T>(Dictionary<string, T?> seen, string key, Func<string, T?> read)
        {
            if (seen.TryGetValue(key, out var known)) return known;
            var value = read(key);
            seen[key] = value;
            return value;
        }

        private static string TradeInFallbackId => Rules.RuleBook.Current.Market.TradeInDealer;

        public string TradeInLocation(IReadOnlyList<DealerLocation>? dealers, Car? car = null)
        {
            // Only a lot that is open takes a car in: not one selling off its stock, closed, or not open yet
            var all = dealers is { Count: > 0 } ? dealers : GetDefaultDealers();
            var open = DealerRoster.Restocking(all);
            var known = open.Count > 0 ? open : all;

            // A lot that deals in one kind of car takes only its kind in
            var model = car == null ? null : _catalogRepo.GetCar(car.DefinitionId);
            bool Takes(DealerDefinition definition) =>
                definition.Cars is not { } kind || (model != null && kind.MatchesOffer(model, car, ValueOf(car!), car!.PowerHp));

            // The lot that keeps the roughest stock takes the cars nobody asked about: the trade-ins
            var roughest = known
                .Select(d => (Location: d, Definition: _dealerCatalog?.Get(d.Id)))
                .Where(d => d.Definition != null && Takes(d.Definition))
                .OrderBy(d => d.Definition!.ConditionCenter)
                .Select(d => d.Location)
                .FirstOrDefault();

            // No dealer the catalog knows: the one the rules name for the trade-ins, when this game has it, rather than
            // whichever dealer happens to be last; with no dealer at all (no mod has one) its id, a lot nobody visits
            return (roughest
                ?? known.FirstOrDefault(d => string.Equals(d.Id, TradeInFallbackId, StringComparison.OrdinalIgnoreCase))
                ?? known.LastOrDefault())?.Id ?? TradeInFallbackId;
        }

        /// <param name="backLot">One the dealer keeps out back, for the paper only: rougher than its lot's, and not cleaned up</param>
        private UsedCarListing CreateListing(CarDefinition carDef, CarProfile profile, DealerLocation dealer, DateTime currentDate, bool backLot = false)
        {
            // A cheap lot's cars are rougher, and that is what the price is worked out from
            var condition = backLot ? Roll(BackLotMinCondition, BackLotMaxCondition) : GenerateCondition(_dealerCatalog?.Get(dealer.Id));

            // A dealer cleans its cars up for the lot: some rust and worn paint for a rough one, washed
            var listing = NewListing(carDef, profile, currentDate, condition, GenerateMileage(), backLot ? LookSource.Private : LookSource.Dealer, 1m);
            listing.DealerLocation = dealer.Id;
            listing.PaperOnly = backLot;
            return listing;
        }

        /// <summary>A private person's car in the paper: as its owner left it, more miles on it, asking less than a lot would</summary>
        private UsedCarListing CreatePrivateListing(CarDefinition carDef, CarProfile profile, DateTime currentDate)
        {
            var market = Rules.RuleBook.Current.Market;
            var condition = Roll(market.PrivateMinCondition, market.PrivateMaxCondition);
            var mileage = _random.Next(Math.Min(market.PrivateMinMileage, market.PrivateMaxMileage),
                Math.Max(market.PrivateMinMileage, market.PrivateMaxMileage) + 1);
            var listing = NewListing(carDef, profile, currentDate, condition, mileage, LookSource.Private, market.PrivatePriceShare);
            listing.PaperOnly = true;
            return listing;
        }

        /// <summary>
        /// A car for sale as it looks (<paramref name="source"/>) and priced as it looks, by its condition and its past, at
        /// <paramref name="priceShare"/> of what a lot would ask; nobody's yet
        /// </summary>
        private UsedCarListing NewListing(CarDefinition carDef, CarProfile profile, DateTime currentDate, float condition, int mileage,
            LookSource source, decimal priceShare)
        {
            var history = new CarHistory { EarlierOwners = GenerateEarlierOwners(mileage) };
            var looks = CarLooks.Roll(condition, source, _random);
            var price = CalculatePrice(profile, condition, CarValuation.LooksFactor(looks.Rust, looks.Paint, looks.Cracks),
                CarValuation.HistoryFactor(history, mileage));
            if (priceShare != 1m) price = CarValuation.RoundToHundred(Math.Max(MinListPrice, price * priceShare));

            // Get random skin from available skins
            var skinId = "default";
            if (carDef.AvailableSkins != null && carDef.AvailableSkins.Count > 0)
            {
                skinId = carDef.AvailableSkins[_random.Next(carDef.AvailableSkins.Count)];
            }

            var listing = new UsedCarListing
            {
                Id = Guid.NewGuid().ToString(),
                CarDefinitionId = carDef.Id,
                Price = price,
                Mileage = mileage,
                Condition = condition,
                SkinId = skinId,
                ListedDate = currentDate,
                IsSold = false,
                History = history
            };

            CarLooks.Give(listing, looks);
            return listing;
        }

        /// <summary>A condition from <paramref name="low"/> to <paramref name="high"/></summary>
        private float Roll(float low, float high) =>
            Math.Clamp(Math.Min(low, high) + (float)(_random.NextDouble() * Math.Abs(high - low)), 0f, 1f);

        /// <summary>
        /// A worked-on engine is tried out on the dyno a couple of dozen times: not on the caller's thread.
        /// The listings are new and nobody else's yet, so they can be filled in from there. The cars' catalog entries
        /// come from the pool they were drawn from, and each model's factory build is looked up once: not a visit to
        /// the catalog per listing.
        /// </summary>
        private Task AddEnginesAsync(List<UsedCarListing> listings, List<PoolEntry> pool) => Task.Run(() =>
        {
            if (_partsService is not { IsAvailable: true } parts) return;

            var models = new Dictionary<string, PoolEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in pool) models.TryAdd(entry.Car.Id, entry);
            var stockBuilds = new Dictionary<string, RatedBuild?>(StringComparer.OrdinalIgnoreCase);

            foreach (var listing in listings)
            {
                if (models.TryGetValue(listing.CarDefinitionId, out var model))
                    AddEngine(parts, listing, model.Car, model.Profile, () => Remembered(stockBuilds, model.Car.Id, _ => parts.GetStockBuild(model.Car)));
            }
        });

        /// <summary>
        /// The car is sold with the engine it has: mostly the factory one, now and then worked on. The price was worked
        /// out with the factory engine in the car's shape: the engine it has takes its place, worth what its parts are.
        /// </summary>
        /// <param name="stockBuildOf">The model's factory build (<see cref="ICarPartsService.GetStockBuild"/>)</param>
        private void AddEngine(ICarPartsService parts, UsedCarListing listing, CarDefinition carDef, CarProfile profile, Func<RatedBuild?> stockBuildOf)
        {
            try
            {
                var engine = parts.CreateUsedEngine(carDef, listing.Condition);
                if (engine == null) return;

                listing.Parts.Add(engine.Root);
                listing.EngineSummary = parts.Describe(engine.Root, engine.Report);
                listing.PowerHp = PowerOf(engine.Report);

                if (stockBuildOf() is { } stockBuild) listing.IsModified = CarValuation.IsModified(engine.Root, stockBuild);

                // A car whose engine was worked on had its rear axle seen to now and then: a posi, shorter gears. Off the
                // UI thread, which has the service's own Random: the shared one, as the engine's
                if (listing.IsModified && parts.CreateTunedRunningGear(carDef.Id, listing.Condition, listing.PowerHp ?? 0, Random.Shared) is { } gear)
                {
                    listing.Parts.AddRange(gear);
                    listing.HasRunningGearAssigned = true;
                    listing.GearVersion = StreetCorsa.Parts.Cars.RunningGearFactory.GearVersion;
                }

                // The engine it has in the place of the factory one in the car's shape, by the same sum the car is valued
                // by once it is somebody's; a private seller asks the same share of it as of the rest of the car
                // (CreatePrivateListing), the car's looks and past count on it as on the rest
                var share = listing.IsPrivate ? Rules.RuleBook.Current.Market.PrivatePriceShare : 1m;
                var over = CarValuation.EngineWorthOver(parts.Catalog, engine.Root, profile, listing.Condition)
                           * CarValuation.LooksFactor(listing.Rust, listing.PaintWear, listing.GlassCracks)
                           * CarValuation.HistoryFactor(listing.History, listing.Mileage);
                listing.Price = Math.Max(MinListPrice, CarValuation.RoundToHundred(listing.Price + share * over));
            }
            catch (Exception ex)
            {
                // A listing without parts still sells; the buyer gets the factory engine then
                _logger.Warning("Could not build the engine of a {CarId}: {Error}", carDef.Id, ex.Message);
            }
        }



        /// <summary>
        /// How straight the car is. A dealer pulls the roll toward the sort of stock it keeps, but the roll
        /// still has the bigger say, so a gem turns up on the dirt lot now and then and a dog turns up in the
        /// smart showroom. That is what makes looking round worth the drive.
        /// </summary>
        private float GenerateCondition(DealerDefinition? dealer)
        {
            // Generate condition between min and max
            var range = MaxCondition - MinCondition;
            var roll = MinCondition + (float)(_random.NextDouble() * range);

            if (dealer == null) return roll;

            // One car in twelve is not what the lot usually keeps: the trade-in nobody looked at properly,
            // or the tired one that slipped into the smart showroom. This is what makes the drive worth it.
            if (_random.Next(12) == 0) return roll;

            // The roll leads; the dealer shifts it by how far its own standard sits from the middle of the
            // range. A weighted average instead would squeeze every lot into the same narrow band.
            var middle = (MinCondition + MaxCondition) / 2f;
            var shifted = roll + (dealer.ConditionCenter - middle) * DealerConditionPull;
            return Math.Clamp(shifted, 0.15f, 1f);
        }

        private int GenerateMileage()
        {
            return _random.Next(MinMileage, MaxMileage + 1);
        }

        /// <summary>
        /// Who had the car before the lot did: one owner for a car that has not gone far, one more for every
        /// <see cref="KmPerEarlierOwner"/> or so
        /// </summary>
        private int GenerateEarlierOwners(int mileage) =>
            1 + (int)Math.Floor(mileage / KmPerEarlierOwner * _random.NextDouble() * 2);

        private static double KmPerEarlierOwner => Rules.RuleBook.Current.Market.KmPerEarlierOwner;

        private decimal CalculatePrice(CarProfile profile, float condition, decimal looks, decimal history)
        {
            // What the car is worth (the one formula every price in the game uses: its body and parts in its condition,
            // its looks and a little for its past), at what this dealer makes of the car on the day: a random variation of ±20%
            var variation = 1.0m + ((decimal)_random.NextDouble() * (decimal)PriceVariationPercent * 2) - (decimal)PriceVariationPercent;
            return CarValuation.ListingValue(profile, condition, looks * variation, history);
        }
    }
}
