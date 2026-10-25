using StreetCorsa.Mods;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.PartShops
{
    /// <summary>Buying at and selling to the part shops on the map; see <see cref="PartShopService"/></summary>
    public interface IPartShopService
    {
        IPartShopCatalog Catalog { get; }

        /// <summary>False when there are no parts: the shops have nothing to sell</summary>
        bool IsAvailable { get; }

        /// <summary>Every part the shop deals in, whether it has it today or not</summary>
        IReadOnlyList<PartDefinition> AssortmentOf(PartShopDefinition shop);

        /// <summary>What the shop asks the player for a new part today: its prices, a sale, the regulars' discount</summary>
        decimal PriceOfNew(GameState gameState, PartShopLocation shop, PartDefinition part);

        /// <summary>The shop's own price of a new part, with nothing off: the price a sale or a regular's discount is taken from</summary>
        decimal MarkedPriceOfNew(GameState gameState, PartShopLocation shop, PartDefinition part);

        /// <summary>
        /// What a junkyard asks the player for a part in its yard today; never under what it would pay for the part and
        /// its margin (<see cref="PartShopRules.JunkyardMargin"/>)
        /// </summary>
        decimal PriceOfUsed(GameState gameState, PartShopLocation shop, PartAd item);

        /// <summary>What the shop pays the player for a part of theirs, with whatever is mounted on it</summary>
        decimal BuyingPrice(PartShopLocation shop, PartInstance part);

        /// <summary>False when the shop has none left, is shut, or the player cannot pay</summary>
        bool BuyNew(GameState gameState, PartShopLocation shop, PartDefinition part);

        /// <summary>False when the part has gone from the yard, the yard is shut, or the player cannot pay</summary>
        bool BuyUsed(GameState gameState, PartShopLocation shop, PartAd item);

        /// <summary>
        /// The player asks a junkyard to come down on a part: true when the owner does (the part's price is lower from
        /// now on). Once a part: asked again, the answer is the same.
        /// </summary>
        bool Haggle(PartShopLocation shop, PartAd item, Random random);

        /// <summary>The player sells a part from the shelf to the shop; a junkyard puts it in its yard</summary>
        bool Sell(GameState gameState, PartShopLocation shop, PartInstance part);

        /// <summary>
        /// Fills the shelves of every open shop whose delivery is due; a junkyard's wrecks are taken apart off the UI
        /// thread. <paramref name="firstOnly"/>: only the shops never stocked (a game just begun, a shop just opened).
        /// </summary>
        Task RestockAsync(GameState gameState, DateTime date, Random random, bool firstOnly = false);

        /// <summary>
        /// The cheapest a rival can get each new part today (at a shop that has it, sales counted, else the mail order),
        /// with the shop it comes from: a copy, for a tuner working off the UI thread
        /// </summary>
        /// <param name="date">The day the prices are for (a sale's discount goes by it); the save's date when null</param>
        RivalPriceList RivalPrices(GameState gameState, DateTime? date = null);

        /// <summary>A rival bought the part at the shop: one fewer on its shelf. False when it had none left.</summary>
        bool TakeForRival(GameState gameState, string shopId, string partId);

        /// <summary>The machine shops' work on the player's parts</summary>
        IMachineShopService Machine { get; }

        // ----- engine kits -----

        /// <summary>Every kit the shop deals in, a build in a size, whether it has it today or not; worked out off the UI thread the first time</summary>
        IReadOnlyList<KitLine> KitAssortmentOf(PartShopDefinition shop);

        /// <summary>A kit on the shop's shelves as it sells: its build, size, parts and name; null when the catalog has lost its build or size</summary>
        KitLine? LineOf(ShopKit kit);

        /// <summary>What the shop asks the player for a new kit today: its parts at the shop's prices less the bundle's discount, a sale, the regulars' discount</summary>
        decimal PriceOfKit(GameState gameState, PartShopLocation shop, ShopKit kit);

        /// <summary>The same, for a kit's line looked up once (<see cref="LineOf"/>): a list prices every row many times</summary>
        decimal PriceOfKit(GameState gameState, PartShopLocation shop, KitLine line);

        /// <summary>The shop's own price of a new kit, with nothing off but the bundle's discount</summary>
        decimal MarkedPriceOfKit(GameState gameState, PartShopLocation shop, ShopKit kit);

        /// <summary>The same, for a kit's line looked up once</summary>
        decimal MarkedPriceOfKit(GameState gameState, PartShopLocation shop, KitLine line);

        /// <summary>What a junkyard asks for a used kit in its yard today, never under what it would pay for the parts and its margin</summary>
        decimal PriceOfUsedKit(GameState gameState, PartShopLocation shop, KitAd kit);

        /// <summary>A new kit off the shelf onto the player's, in its crate; false when the shop has none left, is shut, or the player cannot pay</summary>
        bool BuyKit(GameState gameState, PartShopLocation shop, ShopKit kit);

        /// <summary>A used kit out of a junkyard's yard onto the player's shelf, in its crate</summary>
        bool BuyUsedKit(GameState gameState, PartShopLocation shop, KitAd kit);

        /// <summary>The complete engine kits on the tuners' shops' shelves today, at their price to a rival: copies, for a tuner off the UI thread</summary>
        IReadOnlyList<KitOffer> RivalKits(GameState gameState, DateTime? date = null);

        /// <summary>A rival bought the kit at the shop: one fewer on its shelf. False when it had none left.</summary>
        bool TakeKitForRival(GameState gameState, string shopId, string buildId, string size);
    }

    /// <summary>A kit a shop deals in: a build's parts in a size, with the engine's name the shelf gives it</summary>
    public sealed record KitLine(EngineBuild Build, KitSize Size, IReadOnlyList<PartDefinition> Parts, string Name);

    /// <summary>
    /// The part shops' counters. A shop sells what its kind deals in (<see cref="PartShopRules.Deals"/>), as far as it
    /// has it: a new part's shelf runs down with every one sold, to the player or a rival, until the next delivery; a
    /// junkyard's yard holds so many used parts and whole engines, each at its own price, and turns over as wrecks come
    /// in. Prices are the shop's (<see cref="PartShopRules.PriceFactor"/>) in the save's difficulty, less a closing-down
    /// sale and, for the player, the regulars' discount (<see cref="PartShopRules.RegularDiscountFor"/>). Every shop buys
    /// the player's parts, a junkyard for more than a trade-in. Everything bought goes to the player's shelf.
    /// </summary>
    public partial class PartShopService : IPartShopService
    {
        private readonly ICarPartsService _parts;
        private readonly INewspaperPartsService _paper;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("PartShops");
        private readonly object _assortmentsLock = new();

        // What the shops stock, worked out from the content in play: worked out again once another plays
        private readonly ContentCache<Stock> _stock = new(() => new Stock());

        private sealed class Stock
        {
            public readonly Dictionary<string, IReadOnlyList<PartDefinition>> Assortments = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, IReadOnlyList<KitLine>> Kits = new(StringComparer.OrdinalIgnoreCase);
            public IReadOnlySet<string>? FactoryParts;
        }

        public PartShopService(IPartShopCatalog catalog, ICarPartsService parts, INewspaperPartsService paper)
        {
            Catalog = catalog;
            _parts = parts;
            _paper = paper;
            Machine = new MachineShopService(catalog, parts);
        }

        public IPartShopCatalog Catalog { get; }

        public IMachineShopService Machine { get; }

        public bool IsAvailable => _parts.IsAvailable;

        public IReadOnlyList<PartDefinition> AssortmentOf(PartShopDefinition shop)
        {
            if (!IsAvailable) return [];

            lock (_assortmentsLock)
            {
                // One content's stock throughout: a switch meanwhile must not mix two contents' stock
                var stock = _stock.Value;
                if (stock.Assortments.TryGetValue(shop.Id, out var known)) return known;

                // What any car came with from the factory: not a speed shop's line
                stock.FactoryParts ??= _parts.Catalog.EngineBuilds
                    .Where(b => string.Equals(b.Origin, EngineBuild.OriginCar, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(b => b.Parts)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var assortment = _paper.Assortment.Where(p => PartShopRules.Deals(shop, p, stock.FactoryParts)).ToList();
                stock.Assortments[shop.Id] = assortment;
                _logger.Information("{Shop} deals in {Count} parts", shop.Name, assortment.Count);
                return assortment;
            }
        }

        // ----- prices -----

        /// <summary>The shop's own price of a new part, before any discount</summary>
        private static decimal ListPrice(PartShopDefinition shop, PartDefinition part, double priceMultiplier) =>
            PartPricing.Round(PartPricing.NewPrice(part) * PartShopRules.PriceFactor(shop) * GameRules.Sane(priceMultiplier));

        /// <summary>A price less what is off it today: the sale, and the player's discount as a regular</summary>
        private static decimal Discounted(decimal price, decimal saleDiscount, decimal regularDiscount) =>
            price <= 0 ? 0 : Math.Max(1m, PartPricing.Round((double)(price * (1 - saleDiscount) * (1 - regularDiscount))));

        public decimal PriceOfNew(GameState gameState, PartShopLocation shop, PartDefinition part)
        {
            if (Catalog.Get(shop.Id) is not { } definition) return 0;
            return Discounted(ListPrice(definition, part, gameState.Rules.PartPriceMultiplier),
                DealerRules.DiscountOn(shop, gameState.Date), PartShopRules.RegularDiscountFor(shop.PlayerSpent));
        }

        public decimal MarkedPriceOfNew(GameState gameState, PartShopLocation shop, PartDefinition part) =>
            Catalog.Get(shop.Id) is { } definition ? ListPrice(definition, part, gameState.Rules.PartPriceMultiplier) : 0;

        public decimal PriceOfUsed(GameState gameState, PartShopLocation shop, PartAd item)
        {
            var price = Discounted(item.AskingPrice, DealerRules.DiscountOn(shop, gameState.Date), PartShopRules.RegularDiscountFor(shop.PlayerSpent));
            return Math.Max(price, JunkyardFloor(BuyingPrice(shop, item.Part)));
        }

        /// <summary>
        /// The least a junkyard sells a used part for, against what it pays for one: its margin, and never less than the
        /// next price up, as the margin of a cheap part rounds away ($4 and 10% is $4)
        /// </summary>
        public static decimal JunkyardFloor(decimal paid) =>
            Math.Max(PartPricing.Round((double)paid * (1 + PartShopRules.JunkyardMargin)), PartPricing.Above(paid));

        public decimal BuyingPrice(PartShopLocation shop, PartInstance part)
        {
            if (!IsAvailable || Catalog.Get(shop.Id) is not { } definition) return 0;
            return PartPricing.Round(PartPricing.WorthOfAssembly(_parts.Catalog, part) * PartShopRules.BuyFactor(definition));
        }

        /// <summary>
        /// The part shops' shelves under the ids their parts go by now: a shelf is sold from by its part's id. Two old ids
        /// aliased to one part (packs merged) leave two shelves of it: they become one, their counts together (the counter
        /// sells from the first shelf with stock, a delivery counts the lines). The kit lines the same, under their
        /// builds' ids of today: a rival buys a kit by its build's id, and a delivery keeps the lines it deals in by it.
        /// </summary>
        /// <returns>How many shelves and kit lines changed</returns>
        public static int RenameShelves(PartsCatalog catalog, IEnumerable<PartShopLocation> shops)
        {
            var changed = 0;
            foreach (var shop in shops)
            {
                if (shop.Shelves != null)
                {
                    foreach (var shelf in shop.Shelves)
                    {
                        var current = catalog.CurrentId(shelf.DefinitionId);
                        if (current == shelf.DefinitionId) continue;

                        shelf.DefinitionId = current;
                        changed++;
                    }

                    var first = new Dictionary<string, ShopShelf>(StringComparer.OrdinalIgnoreCase);
                    changed += shop.Shelves.RemoveAll(shelf =>
                    {
                        if (first.TryAdd(shelf.DefinitionId, shelf)) return false;

                        first[shelf.DefinitionId].Count += shelf.Count;
                        return true;
                    });
                }

                if (shop.Kits == null) continue;

                foreach (var kit in shop.Kits)
                {
                    var current = catalog.CurrentBuildId(kit.BuildId);
                    if (current == kit.BuildId) continue;

                    kit.BuildId = current;
                    changed++;
                }

                var firstKit = new Dictionary<string, ShopKit>(StringComparer.OrdinalIgnoreCase);
                changed += shop.Kits.RemoveAll(kit =>
                {
                    var key = kit.BuildId + "|" + kit.Size;
                    if (firstKit.TryAdd(key, kit)) return false;

                    firstKit[key].Count += kit.Count;
                    return true;
                });
            }

            return changed;
        }

        // ----- the player at the counter -----

        public bool BuyNew(GameState gameState, PartShopLocation shop, PartDefinition part)
        {
            if (!shop.IsTrading) return false;

            var shelf = shop.Shelves.FirstOrDefault(s => s.Count > 0 && string.Equals(s.DefinitionId, part.Id, StringComparison.OrdinalIgnoreCase));
            var price = PriceOfNew(gameState, shop, part);
            if (shelf == null || price <= 0 || gameState.Player.Money < price) return false;

            shelf.Count--;
            gameState.Player.Money -= price;
            gameState.Player.Parts.Add(new PartInstance(part.Id));
            Sold(shop, price);
            _logger.Information("Bought new {Part} at {Shop} for ${Price}", part.Id, shop.Name, price);
            return true;
        }

        public bool BuyUsed(GameState gameState, PartShopLocation shop, PartAd item)
        {
            if (!shop.IsTrading) return false;

            var price = PriceOfUsed(gameState, shop, item);
            if (gameState.Player.Money < price || !shop.Yard.Contains(item)) return false;

            shop.Yard.Remove(item);
            gameState.Player.Money -= price;
            gameState.Player.Parts.Add(item.Part);
            Sold(shop, price);
            _logger.Information("Bought used {Part} at {Shop} for ${Price}", item.Part.DefinitionId, shop.Name, price);
            return true;
        }

        /// <summary>The player's money counts toward being a regular, and every sale toward the shop's trade</summary>
        private static void Sold(PartShopLocation shop, decimal price)
        {
            shop.PlayerSpent += price;
            shop.Trade += PartShopRules.TradePerSale;
        }

        public bool Haggle(PartShopLocation shop, PartAd item, Random random)
        {
            if (item.Haggled || !shop.Yard.Contains(item)) return false;

            item.Haggled = true;
            if (random.NextDouble() >= PartShopRules.HaggleChance) return false;

            // Down at least one step of the prices: a cheap part's cut rounded back to what was asked is no cut
            var cut = PartShopRules.HaggleMinCut + random.NextDouble() * (PartShopRules.HaggleMaxCut - PartShopRules.HaggleMinCut);
            item.AskingPrice = Math.Max(1m, Math.Min(PartPricing.Round((double)item.AskingPrice * (1 - cut)), PartPricing.Below(item.AskingPrice)));
            return true;
        }

        public bool Sell(GameState gameState, PartShopLocation shop, PartInstance part)
        {
            // A machine shop works on parts; it buys none
            if (!shop.IsTrading || Catalog.Get(shop.Id) is not { } definition || definition.KindOf == PartShopKind.Machine) return false;

            var price = BuyingPrice(shop, part);
            if (!gameState.Player.Parts.Remove(part)) return false;

            gameState.Player.Money += price;

            // A junkyard sells on what it buys, at its own price; the other shops send it back to their wholesaler
            if (definition.KindOf == PartShopKind.Junkyard && shop.Status == DealerStatus.Open && price > 0)
            {
                var asking = PartPricing.Round(PartPricing.WorthOfAssembly(_parts.Catalog, part) * PartShopRules.PriceFactor(definition)
                                               * GameRules.Sane(gameState.Rules.PartPriceMultiplier));
                shop.Yard.Add(new PartAd(part, Math.Max(asking, JunkyardFloor(price)), shop.Name) { PostedDate = gameState.Date });
            }

            _logger.Information("Sold {Part} to {Shop} for ${Price}", part.DefinitionId, shop.Name, price);
            return true;
        }

        // ----- deliveries -----

        public async Task RestockAsync(GameState gameState, DateTime date, Random random, bool firstOnly = false)
        {
            if (!IsAvailable) return;

            foreach (var shop in gameState.PartShops.Where(s => s.Status == DealerStatus.Open && (!firstOnly || s.RestockedOn == null)).ToList())
            {
                if (Catalog.Get(shop.Id) is not { } definition || definition.KindOf == PartShopKind.Machine || !PartShopRules.RestockDue(definition, shop, date)) continue;

                // Marked before the wrecks are waited for: a second caller meanwhile (the map opened during the morning's
                // review) finds the shop stocked and leaves it be, rather than filling the yard twice
                var restockedBefore = shop.RestockedOn;
                shop.RestockedOn = date.Date;

                var assortment = AssortmentOf(definition);
                if (definition.KindOf == PartShopKind.Junkyard)
                {
                    // Worn out in the yard: gone to the crusher
                    shop.Yard.RemoveAll(i => (date.Date - i.PostedDate.Date).TotalDays > PartShopRules.JunkyardKeepsDays
                                             || _parts.Catalog.Get(i.Part.DefinitionId) == null);
                    shop.YardKits ??= [];
                    shop.YardKits.RemoveAll(k => (date.Date - k.PostedDate.Date).TotalDays > PartShopRules.JunkyardKeepsDays
                                                 || k.Parts.Any(p => _parts.Catalog.Get(p.DefinitionId) == null));
                    var (low, high) = PartShopRules.Stock(definition);
                    var wanted = random.Next(low, high + 1) - shop.Yard.Count;
                    var seed = random.Next();
                    var priceMultiplier = gameState.Rules.PartPriceMultiplier;
                    var engines = WreckEngines(definition);
                    List<PartAd> fresh;
                    List<KitAd> freshKits;
                    try
                    {
                        fresh = wanted <= 0 ? [] : await Task.Run(() => Wrecks(definition, assortment, engines, wanted, date, priceMultiplier, new Random(seed)));
                        // Now and then a wreck's engine is taken apart whole, for a used kit
                        freshKits = wanted <= 0 ? [] : await Task.Run(() => WreckKits(definition, engines, wanted, date, priceMultiplier, new Random(seed + 1)));
                    }
                    catch
                    {
                        // Not stocked after all: the next caller tries again
                        shop.RestockedOn = restockedBefore;
                        throw;
                    }

                    // The shop may have closed while the wrecks were taken apart: a sale takes in nothing
                    if (shop.Status == DealerStatus.Open)
                    {
                        shop.Yard.AddRange(fresh);
                        shop.YardKits.AddRange(freshKits);
                    }
                }
                else
                {
                    var shelvesBefore = shop.Shelves;
                    shop.Shelves = Deliver(definition, shop.Shelves, assortment, random);
                    var kitSeed = random.Next();
                    IReadOnlyList<KitLine> kits;
                    try
                    {
                        // The kits the shop deals in are worked out off the UI thread the first time, and known from then on
                        kits = KnownKitAssortmentOf(definition) ?? await Task.Run(() => KitAssortmentOf(definition));
                    }
                    catch
                    {
                        // Not stocked after all: the next caller tries again
                        shop.RestockedOn = restockedBefore;
                        shop.Shelves = shelvesBefore;
                        throw;
                    }

                    // The shop may have closed while the kits were worked out: its shelves were emptied, and stay so
                    if (shop.Status == DealerStatus.Open) shop.Kits = DeliverKits(shop.Kits ?? [], kits, new Random(kitSeed));
                }
            }
        }

        /// <summary>
        /// A delivery: of what is left, some lines are not reordered (<see cref="PartShopRules.DropLineChance"/>) and the
        /// rest stay, each maybe topped up; new lines come in until the shop carries as many different parts as it does.
        /// A speed shop's dear parts come in less often than its cheap ones.
        /// </summary>
        internal static List<ShopShelf> Deliver(PartShopDefinition shop, List<ShopShelf> shelves, IReadOnlyList<PartDefinition> assortment, Random random)
        {
            var dealsIn = assortment.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var (perLow, perHigh) = PartShopRules.PerShelf(shop);
            var kept = shelves.Where(s => s.Count > 0 && dealsIn.Contains(s.DefinitionId) && random.NextDouble() >= PartShopRules.DropLineChance).ToList();
            foreach (var shelf in kept.Where(_ => random.NextDouble() < PartShopRules.TopUpChance)) shelf.Count = Math.Max(shelf.Count, random.Next(perLow, perHigh + 1));

            var (low, high) = PartShopRules.Stock(shop);
            var lines = Math.Min(assortment.Count, random.Next(low, high + 1));
            var candidates = assortment.Where(p => kept.All(s => !string.Equals(s.DefinitionId, p.Id, StringComparison.OrdinalIgnoreCase))).ToList();
            var weights = candidates.Select(p => shop.KindOf == PartShopKind.Speed ? 1 / Math.Sqrt(Math.Max(1, PartPricing.NewPrice(p))) : 1.0).ToList();
            var total = weights.Sum();

            while (kept.Count < lines && candidates.Count > 0)
            {
                var roll = random.NextDouble() * total;
                var pick = candidates.Count - 1;
                for (var i = 0; i < candidates.Count; i++)
                {
                    roll -= weights[i];
                    if (roll < 0)
                    {
                        pick = i;
                        break;
                    }
                }

                kept.Add(new ShopShelf { DefinitionId = candidates[pick].Id, Count = random.Next(perLow, perHigh + 1) });
                candidates.RemoveAt(pick);
                total -= weights[pick];
                weights.RemoveAt(pick);
            }

            return kept;
        }

        /// <summary>The engines a junkyard's wrecks come with: the engines that run, of its makes when it names any</summary>
        private List<RatedBuild> WreckEngines(PartShopDefinition shop)
        {
            var catalog = _parts.Catalog;
            return _parts.Builds.Runnable
                .Where(b => shop.Makes.Count == 0 || shop.Makes.Contains(catalog.Get(b.BlockId)?.Make ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>The used kits out of a delivery of <paramref name="wrecks"/> wrecks: each one's engine with the odds of <see cref="PartShopRules.JunkyardKitChance"/></summary>
        private List<KitAd> WreckKits(PartShopDefinition shop, IReadOnlyList<RatedBuild> engines, int wrecks, DateTime date, double priceMultiplier, Random random)
        {
            var kits = new List<KitAd>();
            for (var i = 0; i < wrecks; i++)
            {
                if (random.NextDouble() < PartShopRules.JunkyardKitChance && WreckKit(shop, engines, date, priceMultiplier, random) is { } kit) kits.Add(kit);
            }

            return kits;
        }

        /// <summary>A junkyard's new wrecks taken apart: loose parts, worn, and now and then a whole engine (one of <paramref name="engines"/>)</summary>
        private List<PartAd> Wrecks(PartShopDefinition shop, IReadOnlyList<PartDefinition> assortment, IReadOnlyList<RatedBuild> engines, int count, DateTime date,
            double priceMultiplier, Random random)
        {
            var catalog = _parts.Catalog;
            var items = new List<PartAd>();

            for (var tries = 0; items.Count < count && tries < count * 3; tries++)
            {
                var wear = PartShopRules.JunkyardMinWear + random.NextDouble() * (PartShopRules.JunkyardMaxWear - PartShopRules.JunkyardMinWear);
                PartInstance? part = null;
                if (engines.Count > 0 && random.NextDouble() < PartShopRules.JunkyardEngineChance)
                {
                    part = EngineFactory.CreateStock(catalog, engines[random.Next(engines.Count)], wear, random)?.Root;
                    if (part != null) part.ParentSlot = 0;
                }

                if (part == null)
                {
                    if (assortment.Count == 0) break;
                    part = new PartInstance(assortment[random.Next(assortment.Count)].Id) { Wear = wear };
                }

                var asking = PartPricing.Round(PartPricing.WorthOfAssembly(catalog, part) * PartShopRules.PriceFactor(shop) * GameRules.Sane(priceMultiplier));
                if (asking <= 0) continue;

                // In the yard since a day or two before the delivery: the dates of a yard are spread out
                items.Add(new PartAd(part, asking, shop.Name) { PostedDate = date.Date.AddDays(-random.Next(0, 3)) });
            }

            return items;
        }

        // ----- the rivals -----

        public RivalPriceList RivalPrices(GameState gameState, DateTime? date = null)
        {
            var multiplier = gameState.Rules.PartPriceMultiplier;
            var prices = new Dictionary<string, (decimal Price, string ShopId)>(StringComparer.OrdinalIgnoreCase);
            if (!IsAvailable) return new RivalPriceList(prices, multiplier);

            foreach (var shop in PartShopRoster.Trading(gameState))
            {
                if (Catalog.Get(shop.Id) is not { } definition || !PartShopRules.SellsToTuners(definition)) continue;

                var sale = DealerRules.DiscountOn(shop, date ?? gameState.Date);
                foreach (var shelf in shop.Shelves.Where(s => s.Count > 0))
                {
                    if (_parts.Catalog.Get(shelf.DefinitionId) is not { } part) continue;

                    var price = Discounted(ListPrice(definition, part, multiplier), sale, 0m);
                    if (price >= NewspaperPartsService.MailOrderPrice(part, multiplier)) continue;
                    if (!prices.TryGetValue(part.Id, out var best) || price < best.Price) prices[part.Id] = (price, shop.Id);
                }
            }

            return new RivalPriceList(prices, multiplier);
        }

        public bool TakeForRival(GameState gameState, string shopId, string partId)
        {
            var shop = PartShopRoster.Find(gameState, shopId);
            var shelf = shop?.Shelves.FirstOrDefault(s => s.Count > 0 && string.Equals(s.DefinitionId, partId, StringComparison.OrdinalIgnoreCase));
            if (shop is not { IsTrading: true } || shelf == null) return false;

            shelf.Count--;
            shop.Trade += PartShopRules.TradePerSale;
            return true;
        }
    }

    /// <summary>
    /// What new parts cost a rival today: the cheapest shop that has one on its shelf, else the mail order. A copy,
    /// read off the UI thread.
    /// </summary>
    public sealed class RivalPriceList(IReadOnlyDictionary<string, (decimal Price, string ShopId)> shelves, double priceMultiplier)
    {
        public decimal PriceOf(PartDefinition part) =>
            shelves.TryGetValue(part.Id, out var offer) ? offer.Price : NewspaperPartsService.MailOrderPrice(part, priceMultiplier);

        /// <summary>The shop the part is cheapest at and its price there; false when it comes by mail</summary>
        public bool TryGetShop(string partId, out string shopId, out decimal price)
        {
            var found = shelves.TryGetValue(partId, out var offer);
            (shopId, price) = found ? (offer.ShopId, offer.Price) : (string.Empty, 0m);
            return found;
        }
    }
}
