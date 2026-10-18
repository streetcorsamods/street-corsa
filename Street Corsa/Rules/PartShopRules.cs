using System.Text.Json;
using System.Text.Json.Nodes;
using StreetCorsa.Mods;

namespace StreetCorsa.Rules;

/// <summary>
/// The part shops (<c>"partShops"</c> in the rules): the junkyard, haggling, the regulars' discount, how trade comes and
/// goes, and each kind of shop's prices and shelves where a shop's own file says nothing
/// </summary>
public sealed class PartShopRules
{
    /// <summary>A junkyard's whole engines: one yard item in so many</summary>
    public double JunkyardEngineChance { get; set; } = 0.15;

    /// <summary>How worn what lies in a junkyard is: a fair bit more than what the paper's sellers have</summary>
    public double JunkyardMinWear { get; set; } = 0.15;

    public double JunkyardMaxWear { get; set; } = 0.75;

    /// <summary>A part lies in the yard this long before it goes to the crusher</summary>
    public int JunkyardKeepsDays { get; set; } = 21;

    /// <summary>
    /// A junkyard lets nothing go for less than this share over what it would pay for it: no sale, discount or
    /// haggle takes a part under it, so a part cannot be bought there and sold straight back at a profit
    /// </summary>
    public double JunkyardMargin { get; set; } = 0.1;

    /// <summary>Haggling at a junkyard: the odds the owner comes down, and by how much (a share of the price, least to most)</summary>
    public double HaggleChance { get; set; } = 0.5;

    public double HaggleMinCut { get; set; } = 0.1;

    public double HaggleMaxCut { get; set; } = 0.25;

    /// <summary>
    /// What the player has spent at a shop to be a regular, and what a regular gets off: a first step, and a second
    /// for a good customer
    /// </summary>
    public decimal RegularSpent { get; set; } = 500m;

    public decimal GoodCustomerSpent { get; set; } = 2000m;

    public decimal RegularDiscount { get; set; } = 0.05m;

    public decimal GoodCustomerDiscount { get; set; } = 0.10m;

    /// <summary>What a part sold to the player or a rival counts toward a shop's trade (a car off a lot counts one)</summary>
    public double TradePerSale { get; set; } = 0.5;

    /// <summary>
    /// The trade every open shop gets from the town's other customers each day, the same for all: what tells the
    /// shops apart when one has to go is what the player and the rivals buy there
    /// </summary>
    public double TownTradePerDay { get; set; } = 0.2;

    /// <summary>The town's other customers buy this share of what is on the shelves (and in the yard) each day</summary>
    public double TownBuysShare { get; set; } = 0.03;

    /// <summary>A delivery drops this share of the lines on the shelves for others: what a shop carries changes over the weeks</summary>
    public double DropLineChance { get; set; } = 0.35;

    /// <summary>
    /// A machine shop asks at least this share of what its work adds to the part's worth (and what comes with it): a
    /// worn engine bored is a little cheaper than a good one, never a giveaway
    /// </summary>
    public double MachineValueShare { get; set; } = 0.85;

    /// <summary>A closing-down sale: the rivals and everybody else clear out this share of what is left each day</summary>
    public double SaleClearOutShare { get; set; } = 0.2;

    /// <summary>
    /// Each kind of shop (speed, wheels, junkyard, specialist) where its file says nothing: what it asks against a
    /// part's price, what it pays for one, how full its shelves are, how often it restocks. A kind the table leaves out
    /// takes <see cref="OtherKind"/>.
    /// </summary>
    public Dictionary<string, PartShopKindRules> Kinds { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["speed"] = new() { PriceFactor = 1.1, StockLow = 25, StockHigh = 35, RestockDays = 10, PerShelfLow = 1, PerShelfHigh = 2 },
        ["wheels"] = new() { PriceFactor = 0.9, StockLow = 40, StockHigh = 60, RestockDays = 5, PerShelfLow = 2, PerShelfHigh = 6 },
        // Against what the used part is worth: the paper's private sellers ask about 0.6; and a little more than a
        // trade-in for the player's old parts, still less than any new part sells for in a closing-down sale (and less
        // than any used part in the paper, which is held over the most a shop pays at every difficulty)
        ["junkyard"] = new() { PriceFactor = 0.55, BuyFactor = 0.42, StockLow = 20, StockHigh = 35, RestockDays = 3, PerShelfLow = 2, PerShelfHigh = 5 },
        // A machine shop: what it asks against the work's list price (rebuilds.json); it stocks and buys nothing
        ["machine"] = new() { PriceFactor = 1.0, StockLow = 1, StockHigh = 1, RestockDays = 30, PerShelfLow = 1, PerShelfHigh = 1 }
    };

    /// <summary>A kind of shop <see cref="Kinds"/> leaves out (a specialist, in the base game)</summary>
    public PartShopKindRules OtherKind { get; set; } = new();

    public PartShopKindRules For(string kind) => Kinds.TryGetValue(kind, out var rules) && rules != null ? rules : OtherKind;

    /// <summary>
    /// A kind the base game's table does not have, given in part in <paramref name="figures"/> (this part of the rules as
    /// the files have it), takes <see cref="OtherKind"/>'s figures for the rest, not the class's
    /// </summary>
    internal void NewKindsFromOtherKind(JsonObject figures)
    {
        if (figures["kinds"] is not JsonObject kinds) return;

        var baseKinds = new PartShopRules().Kinds;
        foreach (var (kind, node) in kinds)
        {
            if (baseKinds.ContainsKey(kind) || node is not JsonObject given) continue;

            var start = ModMerge.Parse(JsonSerializer.Serialize(OtherKind, RuleBook.Options))!;
            ModMerge.Into(start, given);
            if (start.Deserialize<PartShopKindRules>(RuleBook.Options) is { } rules) Kinds[kind] = rules;
        }
    }

    /// <summary>A delivery tops up this share of the lines it keeps on the shelves (to a full shelf's count)</summary>
    public double TopUpChance { get; set; } = 0.5;

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(TopUpChance), 0, 1, problems);
    }
}

/// <summary>A kind of part shop's figures, where a shop's own file says nothing</summary>
public sealed class PartShopKindRules
{
    /// <summary>What it asks, against the part's price</summary>
    public double PriceFactor { get; set; } = 1.0;

    /// <summary>What it pays for a part, against what the part is worth; 0 for the trade-in (<c>economy</c>)</summary>
    public double BuyFactor { get; set; }

    /// <summary>How many parts its shelves hold, least to most</summary>
    public int StockLow { get; set; } = 40;
    public int StockHigh { get; set; } = 60;

    /// <summary>Days between deliveries</summary>
    public int RestockDays { get; set; } = 7;

    /// <summary>How many of one part a delivery puts on the shelf, least to most</summary>
    public int PerShelfLow { get; set; } = 2;
    public int PerShelfHigh { get; set; } = 5;
}
