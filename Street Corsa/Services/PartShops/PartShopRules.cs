using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;

namespace StreetCorsa.Services.PartShops
{
    /// <summary>
    /// The numbers of the part shops (see <see cref="PartShopService"/>): what each kind asks and pays, how much it
    /// carries and how often it restocks, the regulars' discount and the mail order's premium. A shop's own file may set
    /// its numbers; what it leaves out is its kind's. No state, so the rules can be checked on their own.
    /// </summary>
    public static class PartShopRules
    {
        /// <summary>A junkyard's whole engines: one yard item in so many</summary>
        public static double JunkyardEngineChance => Rules.RuleBook.Current.PartShops.JunkyardEngineChance;

        /// <summary>How worn what lies in a junkyard is: a fair bit more than what the paper's sellers have</summary>
        public static double JunkyardMinWear => Rules.RuleBook.Current.PartShops.JunkyardMinWear;
        public static double JunkyardMaxWear => Rules.RuleBook.Current.PartShops.JunkyardMaxWear;

        /// <summary>A part lies in the yard this long before it goes to the crusher</summary>
        public static int JunkyardKeepsDays => Rules.RuleBook.Current.PartShops.JunkyardKeepsDays;

        /// <summary>
        /// A junkyard lets nothing go for less than this share over what it would pay for it: no sale, discount or
        /// haggle takes a part under it, so a part cannot be bought there and sold straight back at a profit
        /// </summary>
        public static double JunkyardMargin => Rules.RuleBook.Current.PartShops.JunkyardMargin;

        /// <summary>Haggling at a junkyard: the odds the owner comes down, and by how much (a share of the price, least to most)</summary>
        public static double HaggleChance => Rules.RuleBook.Current.PartShops.HaggleChance;
        public static double HaggleMinCut => Rules.RuleBook.Current.PartShops.HaggleMinCut;
        public static double HaggleMaxCut => Rules.RuleBook.Current.PartShops.HaggleMaxCut;

        /// <summary>
        /// What the player has spent at a shop to be a regular, and what a regular gets off: a first step, and a second
        /// for a good customer
        /// </summary>
        public static decimal RegularSpent => Rules.RuleBook.Current.PartShops.RegularSpent;
        public static decimal GoodCustomerSpent => Rules.RuleBook.Current.PartShops.GoodCustomerSpent;
        public static decimal RegularDiscount => Rules.RuleBook.Current.PartShops.RegularDiscount;
        public static decimal GoodCustomerDiscount => Rules.RuleBook.Current.PartShops.GoodCustomerDiscount;

        /// <summary>What a part sold to the player or a rival counts toward a shop's trade (a car off a lot counts one)</summary>
        public static double TradePerSale => Rules.RuleBook.Current.PartShops.TradePerSale;

        /// <summary>
        /// The trade every open shop gets from the town's other customers each day, the same for all: what tells the
        /// shops apart when one has to go is what the player and the rivals buy there
        /// </summary>
        public static double TownTradePerDay => Rules.RuleBook.Current.PartShops.TownTradePerDay;

        /// <summary>The town's other customers buy this share of what is on the shelves (and in the yard) each day</summary>
        public static double TownBuysShare => Rules.RuleBook.Current.PartShops.TownBuysShare;

        /// <summary>A delivery drops this share of the lines on the shelves for others: what a shop carries changes over the weeks</summary>
        public static double DropLineChance => Rules.RuleBook.Current.PartShops.DropLineChance;

        /// <summary>A closing-down sale: the rivals and everybody else clear out this share of what is left each day</summary>
        public static double SaleClearOutShare => Rules.RuleBook.Current.PartShops.SaleClearOutShare;

        /// <summary>A machine shop's least price, as a share of what its work adds to the part's worth</summary>
        public static double MachineValueShare => Rules.RuleBook.Current.PartShops.MachineValueShare;

        /// <summary>A delivery tops up this share of the lines it keeps on the shelves</summary>
        public static double TopUpChance => Rules.RuleBook.Current.PartShops.TopUpChance;

        /// <summary>What an engine kit is cheaper than its parts at the shop's price, as a share</summary>
        public static double KitDiscount => Math.Clamp(Rules.RuleBook.Current.PartShops.KitDiscount, 0, 1);

        /// <summary>How many lines of kits a delivery leaves on the shelves, least to most</summary>
        public static (int Low, int High) KitLines
        {
            get
            {
                var rules = Rules.RuleBook.Current.PartShops;
                var low = Math.Max(0, rules.KitLinesLow);
                return (low, Math.Max(low, rules.KitLinesHigh));
            }
        }

        /// <summary>How many of one kit a delivery brings, least to most</summary>
        public static (int Low, int High) KitsPerLine
        {
            get
            {
                var rules = Rules.RuleBook.Current.PartShops;
                var low = Math.Max(1, rules.KitsPerLineLow);
                return (low, Math.Max(low, rules.KitsPerLineHigh));
            }
        }

        /// <summary>A junkyard's wrecks: one in so many gives a used kit</summary>
        public static double JunkyardKitChance => Rules.RuleBook.Current.PartShops.JunkyardKitChance;

        /// <summary>
        /// Whether the shop sells new engine kits of <paramref name="build"/>, as its kind says (<see cref="Rules.PartShopKindRules.Kits"/>):
        /// a make specialist those of its makes a car left the factory with or a rating holds to, a speed shop the
        /// speed-shop builds (of its makes, when it names any). Nobody else sells kits new.
        /// </summary>
        public static bool DealsKitsOf(PartShopDefinition shop, EngineBuild build, PartDefinition block)
        {
            var ofItsMakes = shop.Makes.Count == 0 || (block.Make != null && shop.Makes.Contains(block.Make, StringComparer.OrdinalIgnoreCase));
            return shop.KindRules.Kits switch
            {
                Rules.PartShopKindRules.FactoryKits => !build.Aftermarket && block.Make != null && ofItsMakes,
                Rules.PartShopKindRules.AftermarketKits => build.Aftermarket && ofItsMakes,
                _ => false
            };
        }

        /// <summary>Whether the shop sells new engine kits at all</summary>
        public static bool SellsKits(PartShopDefinition shop) => shop.KindRules is { Sells: true, Kits: not Rules.PartShopKindRules.NoKits };

        /// <summary>
        /// Whether a shop sells a kit of <paramref name="parts"/> for the groups of part it names in its file
        /// (<c>groups</c>): every part of the kit is of them, as <see cref="Deals"/> asks of each part on its own, so a
        /// shop kept to carburettors and intakes sells no engine kits. A shop that names no groups sells any kit of its makes.
        /// </summary>
        public static bool DealsKitParts(PartShopDefinition shop, IEnumerable<PartDefinition> parts) =>
            shop.Groups.Count == 0 || parts.All(p => shop.Groups.Contains(PartKinds.GroupOf(p), StringComparer.OrdinalIgnoreCase));

        public static double PriceFactor(PartShopDefinition shop) => Positive(shop.PriceFactor) ?? Positive(shop.KindRules.PriceFactor) ?? 1.0;

        /// <summary>
        /// What it pays for a part, against what the part is worth: a shop's trade-in, a little more at a junkyard. Less
        /// than any shop sells a new part for in its closing-down sale to a good customer, in the easy game's prices
        /// (a wheel shop's: 0.9 × 0.85 × 0.65 × 0.9 ≈ 0.45), so nothing bought new sells on to a junkyard at a profit.
        /// A used part in the paper is held over it the other way (<see cref="MostPaidFactor"/>).
        /// </summary>
        public static double BuyFactor(PartShopDefinition shop) =>
            Positive(shop.BuyFactor) ?? Positive(shop.KindRules.BuyFactor) ?? StreetCorsa.Parts.Cars.PartPricing.TradeInFactor;

        /// <summary>
        /// The most any shop pays for a part, against what the part is worth: the trade-in, each kind of shop's figure in
        /// the rules and each of <paramref name="shops"/>' own. No used part in the paper is asked for under it
        /// (<see cref="Parts.NewspaperPartsService.UsedAsk"/>): the ads' prices fall with the easy game's, the shops'
        /// buying prices do not, and nothing from the paper may sell straight on to a shop at a profit.
        /// </summary>
        public static double MostPaidFactor(IEnumerable<PartShopDefinition>? shops = null)
        {
            var rules = Rules.RuleBook.Current.PartShops;
            var most = StreetCorsa.Parts.Cars.PartPricing.TradeInFactor;
            foreach (var kind in rules.Kinds.Values.Append(rules.OtherKind))
            {
                if (kind != null && Positive(kind.BuyFactor) is { } factor) most = Math.Max(most, factor);
            }

            foreach (var shop in shops ?? []) most = Math.Max(most, BuyFactor(shop));
            return most;
        }

        public static (int Low, int High) Stock(PartShopDefinition shop)
        {
            var kind = shop.KindRules;
            var (low, high) = (kind.StockLow, kind.StockHigh);
            low = Math.Max(1, shop.StockLow ?? low);
            return (low, Math.Max(low, shop.StockHigh ?? high));
        }

        public static int RestockDays(PartShopDefinition shop) => Math.Max(1, shop.RestockDays ?? shop.KindRules.RestockDays);

        /// <summary>How many of one part a delivery puts on the shelf, least to most: a speed shop gets one or two of anything</summary>
        public static (int Low, int High) PerShelf(PartShopDefinition shop)
        {
            var kind = shop.KindRules;
            var low = Math.Max(1, kind.PerShelfLow);
            return (low, Math.Max(low, kind.PerShelfHigh));
        }

        /// <summary>
        /// Whether the rivals' tuners buy there (<see cref="Rules.PartShopKindRules.Tuners"/>): the shops with engine parts
        /// on their shelves. Not a junkyard (the tuners buy used out of the paper) nor a wheel shop (they tune engines).
        /// </summary>
        public static bool SellsToTuners(PartShopDefinition shop) => shop.KindRules is { Tuners: true, Stock: Rules.PartShopKindRules.ShelvesStock };

        /// <summary>What the player gets off at a shop, having spent <paramref name="spent"/> there</summary>
        public static decimal RegularDiscountFor(decimal spent) =>
            spent >= GoodCustomerSpent ? GoodCustomerDiscount : spent >= RegularSpent ? RegularDiscount : 0m;

        /// <summary>Whether a delivery is due: never stocked, or <see cref="RestockDays"/> since the last</summary>
        public static bool RestockDue(PartShopDefinition shop, PartShopLocation location, DateTime today) =>
            location.RestockedOn is not { } last || (today.Date - last.Date).TotalDays >= RestockDays(shop);

        /// <summary>
        /// Whether the shop deals in the part: its makes and kinds, and what its kind sells (<see cref="Rules.PartShopKindRules.Parts"/>).
        /// The running gear only at a wheel shop (or a junkyard); a speed shop only what makes power and no car came with.
        /// </summary>
        public static bool Deals(PartShopDefinition shop, PartDefinition part, IReadOnlySet<string> factoryParts)
        {
            var group = PartKinds.GroupOf(part);
            if (shop.Groups.Count > 0 && !shop.Groups.Contains(group, StringComparer.OrdinalIgnoreCase)) return false;
            // The stock sway bars do nothing in Assetto Corsa, nor a traction bar or lowering block that says nothing: nobody sells them
            if (!StreetCorsa.Parts.Cars.RunningGear.DoesSomething(part)) return false;

            var runningGear = StreetCorsa.Parts.Cars.RunningGear.IsRunningGear(group);
            var ofItsMakes = shop.Makes.Count == 0 || (part.Make != null && shop.Makes.Contains(part.Make, StringComparer.OrdinalIgnoreCase));
            return shop.KindRules.Parts switch
            {
                Rules.PartShopKindRules.MakesParts => !runningGear && part.Make != null && ofItsMakes,
                Rules.PartShopKindRules.SpeedParts => !runningGear && ofItsMakes && PartGroups.Current.SoldBySpeedShops(group) && !factoryParts.Contains(part.Id),
                Rules.PartShopKindRules.RunningGearParts => runningGear,
                Rules.PartShopKindRules.NoParts => false,
                _ => ofItsMakes || runningGear
            };
        }

        private static double? Positive(double? value) => value is { } v && double.IsFinite(v) && v > 0 ? v : null;
    }
}
