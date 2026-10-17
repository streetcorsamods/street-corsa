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


        /// <summary>The rules' figures for the shop's kind of shop (<c>partShops.kinds</c>), for what the shop's own file leaves out</summary>
        private static Rules.PartShopKindRules KindRules(PartShopDefinition shop) =>
            Rules.RuleBook.Current.PartShops.For(shop.KindOf.ToString());

        public static double PriceFactor(PartShopDefinition shop) => Positive(shop.PriceFactor) ?? Positive(KindRules(shop).PriceFactor) ?? 1.0;

        /// <summary>
        /// What it pays for a part, against what the part is worth: a shop's trade-in, a little more at a junkyard. Less
        /// than any shop sells a new part for in its closing-down sale to a good customer, in the easy game's prices
        /// (a wheel shop's: 0.9 × 0.85 × 0.65 × 0.9 ≈ 0.45), so nothing bought new sells on to a junkyard at a profit.
        /// A used part in the paper is held over it the other way (<see cref="MostPaidFactor"/>).
        /// </summary>
        public static double BuyFactor(PartShopDefinition shop) =>
            Positive(shop.BuyFactor) ?? Positive(KindRules(shop).BuyFactor) ?? StreetCorsa.Parts.Cars.PartPricing.TradeInFactor;

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
            var kind = KindRules(shop);
            var (low, high) = (kind.StockLow, kind.StockHigh);
            low = Math.Max(1, shop.StockLow ?? low);
            return (low, Math.Max(low, shop.StockHigh ?? high));
        }

        public static int RestockDays(PartShopDefinition shop) => Math.Max(1, shop.RestockDays ?? KindRules(shop).RestockDays);

        /// <summary>How many of one part a delivery puts on the shelf, least to most: a speed shop gets one or two of anything</summary>
        public static (int Low, int High) PerShelf(PartShopDefinition shop)
        {
            var kind = KindRules(shop);
            var low = Math.Max(1, kind.PerShelfLow);
            return (low, Math.Max(low, kind.PerShelfHigh));
        }

        /// <summary>
        /// Whether the rivals' tuners buy there: the shops with engine parts on their shelves. Not a junkyard (the
        /// tuners buy used out of the paper) nor a wheel shop (they tune engines).
        /// </summary>
        public static bool SellsToTuners(PartShopDefinition shop) => shop.KindOf is PartShopKind.Specialist or PartShopKind.Speed;

        /// <summary>What the player gets off at a shop, having spent <paramref name="spent"/> there</summary>
        public static decimal RegularDiscountFor(decimal spent) =>
            spent >= GoodCustomerSpent ? GoodCustomerDiscount : spent >= RegularSpent ? RegularDiscount : 0m;

        /// <summary>Whether a delivery is due: never stocked, or <see cref="RestockDays"/> since the last</summary>
        public static bool RestockDue(PartShopDefinition shop, PartShopLocation location, DateTime today) =>
            location.RestockedOn is not { } last || (today.Date - last.Date).TotalDays >= RestockDays(shop);

        /// <summary>
        /// Whether the shop deals in the part: its makes and kinds, and for its kind of shop what it sells. The running
        /// gear only at a wheel shop (or a junkyard); a speed shop only what makes power and no car came with.
        /// </summary>
        public static bool Deals(PartShopDefinition shop, PartDefinition part, IReadOnlySet<string> factoryParts)
        {
            var group = PartKinds.GroupOf(part);
            if (shop.Groups.Count > 0 && !shop.Groups.Contains(group, StringComparer.OrdinalIgnoreCase)) return false;

            var runningGear = StreetCorsa.Parts.Cars.RunningGear.IsRunningGear(group);
            var ofItsMakes = shop.Makes.Count == 0 || (part.Make != null && shop.Makes.Contains(part.Make, StringComparer.OrdinalIgnoreCase));
            return shop.KindOf switch
            {
                PartShopKind.Specialist => !runningGear && part.Make != null && ofItsMakes,
                PartShopKind.Speed => !runningGear && ofItsMakes && PartGroups.Current.SoldBySpeedShops(group) && !factoryParts.Contains(part.Id),
                PartShopKind.Wheels => runningGear,
                PartShopKind.Machine => false,
                _ => ofItsMakes || runningGear
            };
        }

        private static double? Positive(double? value) => value is { } v && double.IsFinite(v) && v > 0 ? v : null;
    }
}
