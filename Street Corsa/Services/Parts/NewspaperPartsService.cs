using StreetCorsa.Mods;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Parts
{
    /// <summary>
    /// Buying and selling parts: new ones by mail order (the part's own price and a premium, and a few days to come),
    /// used ones from the ads, and the player's own to whoever takes them. Everything bought goes to the player's shelf,
    /// the mail order's when it comes (<see cref="Deliver"/>).
    /// </summary>
    public class NewspaperPartsService : INewspaperPartsService
    {
        /// <summary>The mail order sells everything there is, for this much over list, and it takes <see cref="MailOrderDays"/> to come</summary>
        public static double MailOrderPremium => Rules.RuleBook.Current.Economy.MailOrderPremium;
        public static int MailOrderDays => Rules.RuleBook.Current.Economy.MailOrderDays;

        private static int MinAds => Rules.RuleBook.Current.Economy.PartAdsLow;
        private static int MaxAds => Rules.RuleBook.Current.Economy.PartAdsHigh;
        private static int AdLifetimeDays => Rules.RuleBook.Current.Economy.PartAdDays;
        private static double MinWear => Rules.RuleBook.Current.Economy.PartAdMinWear;
        private static double MaxWear => Rules.RuleBook.Current.Economy.PartAdMaxWear;

        // Now and then somebody sells a whole engine instead of its parts
        private static double CompleteEngineChance => Rules.RuleBook.Current.Economy.PartAdEngineChance;

        private readonly ICarPartsService _parts;
        private readonly IPartShopCatalog? _shops;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Parts);
        private readonly ContentCache<IReadOnlyList<PartDefinition>> _assortment;

        // Rolled on the caller's thread only: the ads made off it get dice of their own, seeded from these
        private readonly Random _random;

        /// <param name="shops">The part shops of the game, so no ad asks less than one of them pays; the rules' figures alone when null</param>
        /// <param name="random">The dice of the ads; the shared dice when null (a test seeds its own)</param>
        public NewspaperPartsService(ICarPartsService parts, IPartShopCatalog? shops = null, Random? random = null)
        {
            _parts = parts;
            _shops = shops;
            _random = random ?? Random.Shared;
            _assortment = new ContentCache<IReadOnlyList<PartDefinition>>(FindAssortment);
        }

        public bool IsAvailable => _parts.IsAvailable;

        public IReadOnlyList<PartDefinition> Assortment => _assortment.Value;

        public decimal NewPrice(PartDefinition part, double priceMultiplier) => MailOrderPrice(part, priceMultiplier);

        /// <summary>What the mail order asks for a part: the list price and its premium, in the save's difficulty</summary>
        public static decimal MailOrderPrice(PartDefinition part, double priceMultiplier) =>
            PartPricing.Round(PartPricing.NewPrice(part) * MailOrderPremium * GameRules.Sane(priceMultiplier));

        public decimal TradeInPrice(PartInstance part) => PartPricing.TradeIn(_parts.Catalog, part);

        public async Task RefreshAdsAsync(Models.GameState.GameState gameState, DateTime currentDate)
        {
            var ads = gameState.NewspaperAds.Parts;
            var target = _random.Next(MinAds, MaxAds + 1);
            var adSeed = _random.Next();
            var kitSeed = _random.Next();
            var wanted = target - ads.Count(ad => !IsExpired(ad, currentDate));

            // Loose from the game state: the list of ads belongs to the caller's thread, which may be saving it right now
            var priceMultiplier = gameState.Rules.PartPriceMultiplier;
            var mostPaid = PartShopRules.MostPaidFactor(_shops?.All);
            var fresh = await Task.Run(() => IsAvailable ? CreateAds(wanted, currentDate, priceMultiplier, mostPaid, new Random(adSeed)) : null);
            if (fresh == null) return;

            // Now and then one of them sells an engine taken apart, as a used kit. The paper holds so many
            // kits as the kit chance's share of its part ads, the rivals' among them, as it holds so many part ads.
            var kits = gameState.NewspaperAds.Kits ??= [];
            var mostKits = Math.Max(1, (int)Math.Ceiling(target * Rules.RuleBook.Current.Economy.PartAdKitChance));
            var freshKits = await Task.Run(() => CreateKitAds(wanted, currentDate, priceMultiplier, mostPaid, new Random(kitSeed)));

            // A rival's kit nobody wanted, or one a part of which has gone from the game with its mod, goes to a shop for
            // the trade-in of what is left of it
            bool Gone(KitAd ad) => IsExpired(ad.PostedDate, currentDate) || ad.Parts.Any(p => _parts.Catalog.Get(p.DefinitionId) == null);
            foreach (var ad in kits.Where(ad => ad.SellerRival != null && Gone(ad)))
            {
                RivalPartAds.Pay(gameState, ad.SellerRival, ad.Parts.Where(p => _parts.Catalog.Get(p.DefinitionId) != null).Sum(TradeInPrice));
            }

            kits.RemoveAll(Gone);
            kits.AddRange(freshKits.Take(Math.Max(0, mostKits - kits.Count)));

            // A rival whose part nobody wanted lets a shop have it
            foreach (var ad in ads.Where(ad => ad.SellerRival != null && IsExpired(ad, currentDate)))
            {
                RivalPartAds.Pay(gameState, ad, TradeInPrice(ad.Part));
            }

            var expired = ads.RemoveAll(ad => IsExpired(ad, currentDate) || _parts.Catalog.Get(ad.Part.DefinitionId) == null);
            foreach (var ad in ads) ad.DaysActive = Math.Max(0, (int)(currentDate - ad.PostedDate).TotalDays);

            // No more than asked for, whatever else filled the paper while the ads were made
            var added = fresh.Take(Math.Max(0, target - ads.Count)).ToList();
            ads.AddRange(added);

            _logger.Information("Used parts ads: {Expired} gone, {Added} new, {Total} in the paper", expired, added.Count, ads.Count);
        }

        internal static bool IsExpired(PartAd ad, DateTime currentDate) => IsExpired(ad.PostedDate, currentDate);

        internal static bool IsExpired(DateTime postedDate, DateTime currentDate) => (currentDate - postedDate).TotalDays > AdLifetimeDays;

        /// <summary>The used kits among <paramref name="count"/> new ads: each with the odds of the rules' <c>economy.partAdKitChance</c></summary>
        private List<KitAd> CreateKitAds(int count, DateTime currentDate, double priceMultiplier, double mostPaidFactor, Random random)
        {
            var kits = new List<KitAd>();
            if (!IsAvailable || _parts.Builds.Runnable.Count == 0) return kits;

            var economy = Rules.RuleBook.Current.Economy;
            var catalog = _parts.Catalog;
            for (var i = 0; i < count; i++)
            {
                if (random.NextDouble() >= economy.PartAdKitChance) continue;

                var build = _parts.Builds.Runnable[random.Next(_parts.Builds.Runnable.Count)];
                var wear = MinWear + random.NextDouble() * (MaxWear - MinWear);
                if (EngineFactory.CreateStock(catalog, build, wear, random)?.Root is not { } engine) continue;

                var (ad, _) = KitCrates.UsedKitAd(catalog, engine, random, economy.KitMostMissing);
                var asking = KitWork.WorthOf(catalog, ad.Parts) * PartPricing.UsedShopFactor
                             * (economy.PartAdMinAskShare + random.NextDouble() * (economy.PartAdMaxAskShare - economy.PartAdMinAskShare))
                             * GameRules.Sane(priceMultiplier);
                ad.AskingPrice = KitAsk(catalog, ad.Parts, asking, mostPaidFactor);
                ad.SellerName = GameText.Installed.Pick("names.part_sellers", random);
                ad.PostedDate = currentDate.AddDays(-random.Next(0, 4));
                kits.Add(ad);
            }

            return kits;
        }

        /// <summary>What a used kit in the paper is asked: <paramref name="asking"/>, never as little as the most a shop pays for its parts</summary>
        public static decimal KitAsk(PartsCatalog catalog, IEnumerable<PartInstance> parts, double asking, double mostPaidFactor) =>
            Math.Max(PartPricing.Round(asking), PartPricing.Above(PartPricing.Round(KitWork.WorthOf(catalog, parts) * mostPaidFactor)));

        public bool BuyKit(Models.GameState.GameState gameState, KitAd ad)
        {
            var kits = gameState.NewspaperAds.Kits ??= [];
            if (gameState.Player.Money < ad.AskingPrice || !kits.Contains(ad)) return false;

            kits.Remove(ad);
            gameState.Player.Money -= ad.AskingPrice;
            KitCrates.Pack(gameState.Player, ad.Parts, string.Empty, _parts.Catalog.Kits.Complete?.Id ?? string.Empty, ad.Name, ad.Missing);
            RivalPartAds.Pay(gameState, ad.SellerRival, ad.AskingPrice);
            _logger.Information("Bought a used {Engine} kit from {Seller} for ${Price}", ad.Name, ad.SellerName, ad.AskingPrice);
            return true;
        }

        private List<PartAd> CreateAds(int count, DateTime currentDate, double priceMultiplier, double mostPaidFactor, Random random)
        {
            var ads = new List<PartAd>();
            while (ads.Count < count && CreateAd(currentDate, priceMultiplier, mostPaidFactor, random) is { } ad) ads.Add(ad);

            return ads;
        }

        public HashSet<string> FindFittingParts(Car? car)
        {
            var fitting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (car == null || !IsAvailable) return fitting;

            var catalog = _parts.Catalog;

            // With the engine bay empty any engine goes in
            if (car.Engine == null)
            {
                foreach (var part in Assortment.Where(PartKinds.IsBlock)) fitting.Add(part.Id);
            }

            // The running gear goes on the car itself (a drum's backing plate, a leaf's hanger and shackle too); a tyre on a
            // rim it fits; a rear axle under a car whose rear wheels are driven
            var rims = RunningGear.Rims(car.Parts).Select(p => catalog.Get(p.DefinitionId)).Where(r => r != null).ToList();
            var drivesRear = _parts.Specs(car.DefinitionId)?.DrivesRear ?? true;
            foreach (var part in Assortment)
            {
                var group = PartKinds.GroupOf(part);
                if (group == PartKinds.Tyres ? rims.Any(r => RunningGear.TyreFitsRim(part, r!))
                    : RunningGear.IsRunningGear(group) || group == PartKinds.RearAxles && drivesRear || RunningGear.CarSlotsFor(part).Any())
                    fitting.Add(part.Id);
            }

            // Taken slots count as well: what is on them can come off
            foreach (var mounted in car.Parts.SelectMany(p => p.SelfAndDescendants()))
            {
                if (catalog.Get(mounted.DefinitionId) is not { } definition) continue;
                if (RunningGear.IsRunningGear(PartKinds.GroupOf(definition))) continue;

                foreach (var slot in definition.Slots)
                {
                    if (slot.Id == mounted.OwnSlot) continue;
                    foreach (var (part, _) in catalog.FindMountable(definition, slot)) fitting.Add(part.Id);
                }
            }

            return fitting;
        }

        public PartOrder? BuyNew(Models.GameState.GameState gameState, PartDefinition part)
        {
            var price = NewPrice(part, gameState.Rules.PartPriceMultiplier);
            if (gameState.Player.Money < price) return null;

            var order = new PartOrder
            {
                Part = new PartInstance(part.Id),
                Paid = price,
                OrderedOn = gameState.Date,
                ArrivesOn = gameState.Date.Date.AddDays(MailOrderDays)
            };
            gameState.Player.Money -= price;
            gameState.Player.PartOrders.Add(order);
            _logger.Information("Ordered {Part} by mail for ${Price}, to come on {Day:d}", part.Id, price, order.ArrivesOn);
            return order;
        }

        public List<PartOrder> Deliver(Models.GameState.GameState gameState, DateTime currentDate)
        {
            var come = gameState.Player.PartOrders.Where(o => o.ArrivesOn.Date <= currentDate.Date).ToList();
            foreach (var order in come)
            {
                gameState.Player.PartOrders.Remove(order);
                gameState.Player.Parts.Add(order.Part);

                var name = _parts.Catalog.Get(order.Part.DefinitionId) is { } definition ? definition.DisplayName ?? definition.Name : order.Part.DefinitionId;
                gameState.Player.Delivered.Add(name);
            }

            if (come.Count > 0) _logger.Information("The mail order brought {Count} part(s)", come.Count);
            return come;
        }

        public bool BuyUsed(Models.GameState.GameState gameState, PartAd ad)
        {
            if (gameState.Player.Money < ad.AskingPrice || !gameState.NewspaperAds.Parts.Remove(ad)) return false;

            gameState.Player.Money -= ad.AskingPrice;
            gameState.Player.Parts.Add(ad.Part);
            RivalPartAds.Pay(gameState, ad, ad.AskingPrice);

            // The seller remembers it, a rival whether or not the player talked to them
            var what = _parts.Catalog.Get(ad.Part.DefinitionId) is { } definition ? Market.AdBlurbs.NameOf(definition) : ad.Part.DefinitionId;
            var bought = new PersonEvent { Date = gameState.Date, Kind = "bought", What = what, Amount = ad.AskingPrice };
            if (ad.SellerRival is { } name && gameState.Racers.Find(name) is Opponent rival) Talk.TalkPeople.Happened(gameState, rival, bought);
            else Talk.TalkPeople.Happened(gameState, bought, Talk.TalkPeople.PartKey(ad));
            _logger.Information("Bought used {Part} from {Seller} for ${Price}", ad.Part.DefinitionId, ad.SellerName, ad.AskingPrice);
            return true;
        }

        public bool Sell(Models.GameState.GameState gameState, PartInstance part)
        {
            if (!gameState.Player.Parts.Remove(part)) return false;

            var price = TradeInPrice(part);
            gameState.Player.Money += price;
            _logger.Information("Sold {Part} for ${Price}", part.DefinitionId, price);
            return true;
        }

        /// <summary>
        /// What a used part in the paper is asked: <paramref name="asking"/>, though never as little as the most a shop
        /// pays for it (<see cref="PartShopRules.MostPaidFactor"/>), so nobody buys it here and sells it straight on at a profit
        /// </summary>
        public static decimal UsedAsk(PartsCatalog catalog, PartInstance part, double asking, double mostPaidFactor) =>
            Math.Max(PartPricing.Round(asking), PartPricing.Above(PartPricing.Round(PartPricing.WorthOfAssembly(catalog, part) * mostPaidFactor)));

        private PartAd? CreateAd(DateTime currentDate, double priceMultiplier, double mostPaidFactor, Random random)
        {
            var catalog = _parts.Catalog;
            var wear = MinWear + random.NextDouble() * (MaxWear - MinWear);

            PartInstance? part = null;
            if (random.NextDouble() < CompleteEngineChance && _parts.Builds.Runnable.Count > 0)
            {
                var build = _parts.Builds.Runnable[random.Next(_parts.Builds.Runnable.Count)];
                part = EngineFactory.CreateStock(catalog, build, wear, random)?.Root;
                if (part != null) part.ParentSlot = 0;
            }

            if (part == null)
            {
                if (Assortment.Count == 0) return null;
                part = new PartInstance(Assortment[random.Next(Assortment.Count)].Id) { Wear = wear };
            }

            // Private sellers ask around what a shop would, some more, some less (economy.partAdMinAskShare to
            // partAdMaxAskShare), and more in a harder game
            var economy = Rules.RuleBook.Current.Economy;
            var asking = PartPricing.WorthOfAssembly(catalog, part) * PartPricing.UsedShopFactor
                         * (economy.PartAdMinAskShare + random.NextDouble() * (economy.PartAdMaxAskShare - economy.PartAdMinAskShare))
                         * GameRules.Sane(priceMultiplier);
            var ad = new PartAd(part, UsedAsk(catalog, part, asking, mostPaidFactor), GameText.Installed.Pick("names.part_sellers", random))
            {
                PostedDate = currentDate.AddDays(-random.Next(0, 4))
            };
            Market.AdBlurbs.Describe(ad, catalog, random);
            return ad;
        }

        /// <summary>
        /// What is for sale: the parts of the kinds part_groups.json puts up for sale (<see cref="PartGroups.ForSale"/>:
        /// the engine's parts in the core), whatever else the engine builds use (batteries, mufflers), and the
        /// running gear: tyres, rims, brakes, springs and shocks, the rear axle's parts. The fittings (the roots other
        /// parts declare their fit against, no real parts) are nothing to buy, and neither is what has no say in how the
        /// car drives (the stock sway bars and suspension arms are inert). Nor a wheel with no model: put on, it would
        /// take the car's own out of sight and show nothing (the rims and tyres lost theirs, another game's, until ours
        /// come; a car's factory wheel is its own and needs none).
        /// </summary>
        private IReadOnlyList<PartDefinition> FindAssortment()
        {
            var catalog = _parts.Catalog;
            var groups = PartGroups.Current;
            var usedByBuilds = catalog.EngineBuilds
                .SelectMany(b => b.Parts)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return catalog.Parts.Values
                .Where(p => usedByBuilds.Contains(p.Id) ||
                            (p.IsRealPart && groups.ForSale(PartKinds.GroupOf(p))) ||
                            (p.IsRealPart && RunningGear.IsRunningGear(PartKinds.GroupOf(p)) && (PartKinds.GroupOf(p) != PartKinds.SwayBars || RunningGear.IsWorkingSwayBar(p))) ||
                            RearAxle.IsAxleGroup(PartKinds.GroupOf(p)))
                .Where(p => !IsWheel(p) || catalog.GetModelPath(p) != null)
                .OrderBy(p => p.DisplayName ?? p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsWheel(PartDefinition part) => PartKinds.GroupOf(part) is PartKinds.Rims or PartKinds.Tyres;
    }
}
