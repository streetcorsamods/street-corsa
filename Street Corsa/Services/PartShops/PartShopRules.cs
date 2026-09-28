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
        public const double JunkyardEngineChance = 0.15;

        /// <summary>How worn what lies in a junkyard is: a fair bit more than what the paper's sellers have</summary>
        public const double JunkyardMinWear = 0.15;
        public const double JunkyardMaxWear = 0.75;

        /// <summary>A part lies in the yard this long before it goes to the crusher</summary>
        public const int JunkyardKeepsDays = 21;

        /// <summary>
        /// A junkyard lets nothing go for less than this share over what it would pay for it: no sale, discount or
        /// haggle takes a part under it, so a part cannot be bought there and sold straight back at a profit
        /// </summary>
        public const double JunkyardMargin = 0.1;

        /// <summary>Haggling at a junkyard: the odds the owner comes down, and by how much (a share of the price, least to most)</summary>
        public const double HaggleChance = 0.5;
        public const double HaggleMinCut = 0.1;
        public const double HaggleMaxCut = 0.25;

        /// <summary>
        /// What the player has spent at a shop to be a regular, and what a regular gets off: a first step, and a second
        /// for a good customer
        /// </summary>
        public const decimal RegularSpent = 500m;
        public const decimal GoodCustomerSpent = 2000m;
        public const decimal RegularDiscount = 0.05m;
        public const decimal GoodCustomerDiscount = 0.10m;

        /// <summary>What a part sold to the player or a rival counts toward a shop's trade (a car off a lot counts one)</summary>
        public const double TradePerSale = 0.5;

        /// <summary>
        /// The trade every open shop gets from the town's other customers each day, the same for all: what tells the
        /// shops apart when one has to go is what the player and the rivals buy there
        /// </summary>
        public const double TownTradePerDay = 0.2;

        /// <summary>The town's other customers buy this share of what is on the shelves (and in the yard) each day</summary>
        public const double TownBuysShare = 0.03;

        /// <summary>A delivery drops this share of the lines on the shelves for others: what a shop carries changes over the weeks</summary>
        public const double DropLineChance = 0.35;

        /// <summary>A closing-down sale: the rivals and everybody else clear out this share of what is left each day</summary>
        public const double SaleClearOutShare = 0.2;

        /// <summary>The kinds of part a speed shop sells: the ones that make power</summary>
        public static readonly IReadOnlySet<string> PerformanceGroups = new HashSet<string>(StringComparer.Ordinal)
        {
            "Carburettors and injection", "Intake manifolds", "Superchargers and turbos", "Camshafts", "Exhaust",
            "Air filters", "Nitrous", "Cylinder heads"
        };

        public static double PriceFactor(PartShopDefinition shop) => Positive(shop.PriceFactor) ?? shop.KindOf switch
        {
            PartShopKind.Speed => 1.1,
            PartShopKind.Wheels => 0.9,
            // Against what the used part is worth: the paper's private sellers ask about 0.6
            PartShopKind.Junkyard => 0.55,
            _ => 1.0
        };

        /// <summary>
        /// What it pays for a part, against what the part is worth: a shop's trade-in, a little more at a junkyard. Less
        /// than any shop sells a new part for in its closing-down sale to a good customer, in the easy game's prices
        /// (a wheel shop's: 0.9 × 0.85 × 0.65 × 0.9 ≈ 0.45), so nothing bought new sells on to a junkyard at a profit.
        /// </summary>
        public static double BuyFactor(PartShopDefinition shop) => Positive(shop.BuyFactor) ?? shop.KindOf switch
        {
            PartShopKind.Junkyard => 0.42,
            _ => StreetCorsa.Parts.Cars.PartPricing.TradeInFactor
        };

        public static (int Low, int High) Stock(PartShopDefinition shop)
        {
            var (low, high) = shop.KindOf switch
            {
                PartShopKind.Speed => (25, 35),
                PartShopKind.Wheels => (40, 60),
                PartShopKind.Junkyard => (20, 35),
                _ => (40, 60)
            };
            low = Math.Max(1, shop.StockLow ?? low);
            return (low, Math.Max(low, shop.StockHigh ?? high));
        }

        public static int RestockDays(PartShopDefinition shop) => Math.Max(1, shop.RestockDays ?? shop.KindOf switch
        {
            PartShopKind.Speed => 10,
            PartShopKind.Wheels => 5,
            PartShopKind.Junkyard => 3,
            _ => 7
        });

        /// <summary>How many of one part a delivery puts on the shelf, least to most: a speed shop gets one or two of anything</summary>
        public static (int Low, int High) PerShelf(PartShopDefinition shop) => shop.KindOf switch
        {
            PartShopKind.Speed => (1, 2),
            PartShopKind.Wheels => (2, 6),
            _ => (2, 5)
        };

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
                PartShopKind.Speed => !runningGear && ofItsMakes && PerformanceGroups.Contains(group) && !factoryParts.Contains(part.Id),
                PartShopKind.Wheels => runningGear,
                _ => ofItsMakes || runningGear
            };
        }

        private static double? Positive(double? value) => value is { } v && double.IsFinite(v) && v > 0 ? v : null;
    }
}
