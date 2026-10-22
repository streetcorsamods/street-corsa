using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.PartShops
{
    /// <summary>
    /// Engine kits at the part shops (roadmap step 40): a make specialist's of its makes' engines, a speed shop's of the
    /// speed-shop builds, a few lines a delivery, one or two of each, at the parts' prices less a bundle's discount
    /// (<see cref="PartShopRules.KitDiscount"/>); a junkyard's used ones out of its wrecks. A kit bought goes on the
    /// player's shelf in its crate (<see cref="KitCrates"/>).
    /// </summary>
    public partial class PartShopService
    {
        private readonly Dictionary<string, IReadOnlyList<KitLine>> _kitAssortments = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<KitLine> KitAssortmentOf(PartShopDefinition shop)
        {
            if (!IsAvailable || shop.KindOf is not (PartShopKind.Specialist or PartShopKind.Speed)) return [];

            lock (_assortmentsLock)
            {
                if (_kitAssortments.TryGetValue(shop.Id, out var known)) return known;
            }

            var catalog = _parts.Catalog;
            var lines = new List<KitLine>();
            // A short block of one engine is the same crate whichever car's build it is told by: one line of it
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rated in _parts.Builds.Runnable)
            {
                if (catalog.Get(rated.BlockId) is not { } block || !PartShopRules.DealsKitsOf(shop, rated.Build, block)) continue;

                foreach (var size in catalog.Kits.Sizes)
                {
                    var parts = KitWork.PartsOf(catalog, rated.Build, size);
                    if (parts.Count < 2 || !seen.Add(string.Join("|", parts.Select(p => p.Id).Order(StringComparer.OrdinalIgnoreCase)))) continue;

                    lines.Add(new KitLine(rated.Build, size, parts, NameOf(rated.Build, size, block)));
                }
            }

            lock (_assortmentsLock)
            {
                _kitAssortments[shop.Id] = lines;
            }

            _logger.Information("{Shop} deals in {Count} engine kits", shop.Name, lines.Count);
            return lines;
        }

        /// <summary>A complete kit goes by its build's name; a smaller one by its block's, the build its parts are of told beside it</summary>
        private static string NameOf(EngineBuild build, KitSize size, PartDefinition block) =>
            size.All || string.IsNullOrWhiteSpace(block.DisplayName ?? block.Name) ? build.Name : block.DisplayName ?? block.Name;

        public KitLine? LineOf(ShopKit kit)
        {
            if (!IsAvailable) return null;

            var catalog = _parts.Catalog;
            if (catalog.GetBuild(kit.BuildId) is not { } build || catalog.Kits.Get(kit.Size) is not { } size) return null;

            var parts = KitWork.PartsOf(catalog, build, size);
            var block = parts.FirstOrDefault(PartKinds.IsBlock);
            return block == null ? null : new KitLine(build, size, parts, NameOf(build, size, block));
        }

        // ----- prices -----

        /// <summary>The shop's price of a kit's parts together, less the bundle's discount, before any other discount</summary>
        private static decimal KitListPrice(PartShopDefinition shop, KitLine line, double priceMultiplier) =>
            PartPricing.Round((double)line.Parts.Sum(p => ListPrice(shop, p, priceMultiplier)) * (1 - PartShopRules.KitDiscount));

        public decimal PriceOfKit(GameState gameState, PartShopLocation shop, ShopKit kit)
        {
            if (Catalog.Get(shop.Id) is not { } definition || LineOf(kit) is not { } line) return 0;
            return Discounted(KitListPrice(definition, line, gameState.Rules.PartPriceMultiplier),
                DealerRules.DiscountOn(shop, gameState.Date), PartShopRules.RegularDiscountFor(shop.PlayerSpent));
        }

        public decimal MarkedPriceOfKit(GameState gameState, PartShopLocation shop, ShopKit kit) =>
            Catalog.Get(shop.Id) is { } definition && LineOf(kit) is { } line ? KitListPrice(definition, line, gameState.Rules.PartPriceMultiplier) : 0;

        public decimal PriceOfUsedKit(GameState gameState, PartShopLocation shop, KitAd kit)
        {
            var price = Discounted(kit.AskingPrice, DealerRules.DiscountOn(shop, gameState.Date), PartShopRules.RegularDiscountFor(shop.PlayerSpent));
            return Math.Max(price, JunkyardFloor(kit.Parts.Sum(p => BuyingPrice(shop, p))));
        }

        // ----- the player at the counter -----

        public bool BuyKit(GameState gameState, PartShopLocation shop, ShopKit kit)
        {
            if (!shop.IsTrading || kit.Count <= 0 || !shop.Kits.Contains(kit) || LineOf(kit) is not { } line) return false;

            var price = PriceOfKit(gameState, shop, kit);
            var parts = KitWork.NewParts(_parts.Catalog, line.Build, line.Size);
            if (price <= 0 || parts.Count == 0 || gameState.Player.Money < price) return false;

            kit.Count--;
            gameState.Player.Money -= price;
            KitCrates.Pack(gameState.Player, parts, line.Build.Id, line.Size.Id, line.Name, isNew: true);
            Sold(shop, price);
            _logger.Information("Bought a new {Size} kit of {Build} at {Shop} for ${Price}", line.Size.Id, line.Build.Id, shop.Name, price);
            return true;
        }

        public bool BuyUsedKit(GameState gameState, PartShopLocation shop, KitAd kit)
        {
            if (!shop.IsTrading || !shop.YardKits.Contains(kit)) return false;

            var price = PriceOfUsedKit(gameState, shop, kit);
            if (gameState.Player.Money < price) return false;

            shop.YardKits.Remove(kit);
            gameState.Player.Money -= price;
            KitCrates.Pack(gameState.Player, kit.Parts, string.Empty, _parts.Catalog.Kits.Complete?.Id ?? string.Empty, kit.Name, kit.Missing);
            Sold(shop, price);
            _logger.Information("Bought a used {Engine} kit at {Shop} for ${Price}", kit.Name, shop.Name, price);
            return true;
        }

        // ----- deliveries -----

        /// <summary>
        /// A delivery's kits: of what is left some lines are not reordered, the rest stay, and new lines come in up to so
        /// many (<see cref="PartShopRules.KitLines"/>), one or two of each
        /// </summary>
        internal static List<ShopKit> DeliverKits(List<ShopKit> kits, IReadOnlyList<KitLine> assortment, Random random)
        {
            static string Key(string buildId, string size) => buildId + "|" + size;

            var dealsIn = assortment.Select(l => Key(l.Build.Id, l.Size.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var (perLow, perHigh) = PartShopRules.KitsPerLine;
            var kept = kits.Where(k => k.Count > 0 && dealsIn.Contains(Key(k.BuildId, k.Size)) && random.NextDouble() >= PartShopRules.DropLineChance).ToList();

            var (low, high) = PartShopRules.KitLines;
            var lines = Math.Min(assortment.Count, random.Next(low, high + 1));
            var candidates = assortment.Where(l => kept.All(k => !Key(k.BuildId, k.Size).Equals(Key(l.Build.Id, l.Size.Id), StringComparison.OrdinalIgnoreCase))).ToList();
            while (kept.Count < lines && candidates.Count > 0)
            {
                var pick = random.Next(candidates.Count);
                kept.Add(new ShopKit { BuildId = candidates[pick].Build.Id, Size = candidates[pick].Size.Id, Count = random.Next(perLow, perHigh + 1) });
                candidates.RemoveAt(pick);
            }

            return kept;
        }

        /// <summary>An engine out of a wreck taken apart for a used kit, at the yard's price; null when there is no engine to have</summary>
        private KitAd? WreckKit(PartShopDefinition shop, IReadOnlyList<RatedBuild> engines, DateTime date, double priceMultiplier, Random random)
        {
            if (engines.Count == 0) return null;

            var catalog = _parts.Catalog;
            var wear = PartShopRules.JunkyardMinWear + random.NextDouble() * (PartShopRules.JunkyardMaxWear - PartShopRules.JunkyardMinWear);
            if (EngineFactory.CreateStock(catalog, engines[random.Next(engines.Count)], wear, random)?.Root is not { } engine) return null;

            var (parts, kept) = KitWork.TearDown(catalog, engine, random, Rules.RuleBook.Current.Economy.KitMostMissing);
            var asking = PartPricing.Round(KitWork.WorthOf(catalog, parts) * PartShopRules.PriceFactor(shop) * GameRules.Sane(priceMultiplier));
            if (asking <= 0) return null;

            return new KitAd
            {
                Parts = parts,
                Name = KitWork.EngineNameOf(catalog, parts),
                Missing = KitWork.NamesOf(catalog, kept),
                AskingPrice = asking,
                SellerName = shop.Name,
                PostedDate = date.Date.AddDays(-random.Next(0, 3))
            };
        }

        // ----- the rivals -----

        public IReadOnlyList<KitOffer> RivalKits(GameState gameState, DateTime? date = null)
        {
            var offers = new List<KitOffer>();
            if (!IsAvailable) return offers;

            var multiplier = gameState.Rules.PartPriceMultiplier;
            foreach (var shop in PartShopRoster.Trading(gameState))
            {
                if (Catalog.Get(shop.Id) is not { } definition || !PartShopRules.SellsToTuners(definition)) continue;

                var sale = DealerRules.DiscountOn(shop, date ?? gameState.Date);
                foreach (var kit in (shop.Kits ?? []).Where(k => k.Count > 0))
                {
                    // A rival swaps in a whole engine: a complete kit, nothing of theirs to go on it
                    if (LineOf(kit) is not { Size.All: true } line) continue;

                    var price = Discounted(KitListPrice(definition, line, multiplier), sale, 0m);
                    offers.Add(new KitOffer(null, shop.Id, line.Build.Id, line.Size.Id, line.Name, KitWork.NewParts(_parts.Catalog, line.Build, line.Size), price));
                }
            }

            return offers;
        }

        public bool TakeKitForRival(GameState gameState, string shopId, string buildId, string size)
        {
            var shop = PartShopRoster.Find(gameState, shopId);
            var kit = shop?.Kits?.FirstOrDefault(k => k.Count > 0 && k.BuildId.Equals(buildId, StringComparison.OrdinalIgnoreCase)
                                                      && k.Size.Equals(size, StringComparison.OrdinalIgnoreCase));
            if (shop is not { IsTrading: true } || kit == null) return false;

            kit.Count--;
            shop.Trade += PartShopRules.TradePerSale;
            return true;
        }
    }
}
