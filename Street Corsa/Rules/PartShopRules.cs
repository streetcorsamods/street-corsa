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
    /// <summary>Parts or jobs a shop's clerk (the machinist too) is told about for one ask at most</summary>
    public int MostPicks { get; set; } = 5;

    /// <summary>
    /// Parts tried on the dyno for one ask of a part shop's clerk at most: the cheapest that fit and take the place of a
    /// part on the engine, so an ask is answered in a moment
    /// </summary>
    public int MostDynoRuns { get; set; } = 40;

    /// <summary>Whole engines run on the dyno for one ask of the machinist at most (a whole engine's run is the dearer)</summary>
    public int MachineDynoRuns { get; set; } = 12;

    /// <summary>Other customers' work standing round a machine shop, besides the player's</summary>
    public int OthersWork { get; set; } = 5;

    /// <summary>Each figure the game loops over or picks by held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(MostPicks), 0, 100, problems);
        Figures.Within(this, path, nameof(MostDynoRuns), 0, 1000, problems);
        Figures.Within(this, path, nameof(MachineDynoRuns), 0, 1000, problems);
        Figures.Within(this, path, nameof(OthersWork), 0, 100, problems);
        Figures.Within(this, path, nameof(TopUpChance), 0, 1, problems);
        Figures.Within(this, path, nameof(KitDiscount), 0, 1, problems);
        Figures.Within(this, path, nameof(KitLinesLow), 0, 100, problems);
        Figures.Within(this, path, nameof(KitLinesHigh), 0, 100, problems);
        Figures.Within(this, path, nameof(KitsPerLineLow), 0, 100, problems);
        Figures.Within(this, path, nameof(KitsPerLineHigh), 0, 100, problems);
        Figures.Within(this, path, nameof(JunkyardKitChance), 0, 1, problems);
        // The other kind first: a mod's own kind falls back on it, a core kind on the core's own for it
        OtherKind?.Check($"{path}.otherKind", problems, new PartShopKindRules());
        var baseKinds = new PartShopRules().Kinds;
        foreach (var (kind, rules) in Kinds)
            rules?.Check($"{path}.kinds.{kind}", problems, baseKinds.GetValueOrDefault(kind) ?? OtherKind ?? new PartShopKindRules());
    }

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
    /// Each kind of shop (speed, wheels, junkyard, machine), the kind a shop's file names: what it does (what it stocks,
    /// which parts, kits, machining) and, where the shop's file says nothing, what it asks against a part's price, what it
    /// pays for one, how full its shelves are, how often it restocks. A mod adds a kind of its own here (a tuning house,
    /// a body shop), starting from <see cref="OtherKind"/>; a shop of a kind the table has not is a specialist.
    /// </summary>
    public Dictionary<string, PartShopKindRules> Kinds { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["speed"] = new()
        {
            PriceFactor = 1.1, StockLow = 25, StockHigh = 35, RestockDays = 10, PerShelfLow = 1, PerShelfHigh = 2,
            Parts = PartShopKindRules.SpeedParts, Kits = PartShopKindRules.AftermarketKits, DearRarer = true
        },
        ["wheels"] = new()
        {
            PriceFactor = 0.9, StockLow = 40, StockHigh = 60, RestockDays = 5, PerShelfLow = 2, PerShelfHigh = 6,
            Parts = PartShopKindRules.RunningGearParts, Kits = PartShopKindRules.NoKits, Tuners = false
        },
        // Against what the used part is worth: the paper's private sellers ask about 0.6; and a little more than a
        // trade-in for the player's old parts, still less than any new part sells for in a closing-down sale (and less
        // than any used part in the paper, which is held over the most a shop pays at every difficulty)
        ["junkyard"] = new()
        {
            PriceFactor = 0.55, BuyFactor = 0.42, StockLow = 20, StockHigh = 35, RestockDays = 3, PerShelfLow = 2, PerShelfHigh = 5,
            Stock = PartShopKindRules.YardStock, Parts = PartShopKindRules.AnyParts, Kits = PartShopKindRules.NoKits, Tuners = false
        },
        // A machine shop: what it asks against the work's list price (rebuilds.json); it stocks and buys nothing
        ["machine"] = new()
        {
            PriceFactor = 1.0, StockLow = 1, StockHigh = 1, RestockDays = 30, PerShelfLow = 1, PerShelfHigh = 1,
            Stock = PartShopKindRules.NoStock, Parts = PartShopKindRules.NoParts, Kits = PartShopKindRules.NoKits, Tuners = false, Machining = true
        }
    };

    /// <summary>The kind a shop of a kind <see cref="Kinds"/> has not is</summary>
    public const string SpecialistKind = "specialist";

    /// <summary>A kind of shop <see cref="Kinds"/> leaves out (a specialist, in the core), and where a mod's new kind starts</summary>
    public PartShopKindRules OtherKind { get; set; } = new();

    public PartShopKindRules For(string kind) => Kinds.TryGetValue(kind, out var rules) && rules != null ? rules : OtherKind;

    /// <summary>
    /// A kind the core's table does not have, given in part in <paramref name="figures"/> (this part of the rules as
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

    /// <summary>
    /// What an engine kit is cheaper than its parts at the shop's price, as a share: a bundle, still
    /// well over what a junkyard pays for the parts
    /// </summary>
    public double KitDiscount { get; set; } = 0.1;

    /// <summary>How many lines of engine kits (a build in a size) a delivery leaves on a shop's shelves, least to most</summary>
    public int KitLinesLow { get; set; } = 2;

    public int KitLinesHigh { get; set; } = 4;

    /// <summary>How many of one kit a delivery brings, least to most</summary>
    public int KitsPerLineLow { get; set; } = 1;

    public int KitsPerLineHigh { get; set; } = 2;

    /// <summary>A junkyard's wrecks: one in so many gives an engine taken apart as a used kit, besides its loose parts</summary>
    public double JunkyardKitChance { get; set; } = 0.05;
}

/// <summary>
/// A kind of part shop: what it does, built from the behaviours there are, and its figures where a shop's own file
/// says nothing. The class's own is a make specialist's.
/// </summary>
public sealed class PartShopKindRules
{
    public const string ShelvesStock = "shelves";
    public const string YardStock = "yard";
    public const string NoStock = "none";

    public const string MakesParts = "makes";
    public const string SpeedParts = "speed";
    public const string RunningGearParts = "running_gear";
    public const string AnyParts = "any";
    public const string NoParts = "none";

    public const string FactoryKits = "factory";
    public const string AftermarketKits = "aftermarket";
    public const string NoKits = "none";

    /// <summary>
    /// What it sells from: <c>shelves</c>, new parts a delivery restocks; <c>yard</c>, used parts and whole engines out of
    /// wrecks, cheap, and it buys the player's old parts; <c>none</c>, it sells nothing (a machine shop)
    /// </summary>
    public string Stock { get; set; } = ShelvesStock;

    /// <summary>
    /// Which parts: <c>makes</c>, any part a make's pack has, of its makes (no running gear); <c>speed</c>, the shelves a
    /// speed shop sells (part_groups.json <c>speed_shop</c>) and no part a car came with; <c>running_gear</c>, tyres,
    /// rims, brakes, springs and shocks; <c>any</c>, any part of its makes and any running gear; <c>none</c>
    /// </summary>
    public string Parts { get; set; } = MakesParts;

    /// <summary>New engine kits: <c>factory</c>, its makes' builds the cars came with; <c>aftermarket</c>, the speed shops' builds; <c>none</c></summary>
    public string Kits { get; set; } = FactoryKits;

    /// <summary>The rivals' tuners buy engine parts there</summary>
    public bool Tuners { get; set; } = true;

    /// <summary>It bores, grinds, mills and ports the player's own parts, left there for a few days (<c>MachineShopService</c>)</summary>
    public bool Machining { get; set; }

    /// <summary>A delivery brings a dear part less often than a cheap one (a speed shop's few dear pieces)</summary>
    public bool DearRarer { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Yard => Stock == YardStock;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Sells => Stock != NoStock;

    /// <summary>
    /// A behaviour named that is none there is: <paramref name="fallback"/>'s (the kind it starts from), told. And what
    /// the counters cannot serve, told and taken off: a machine shop's counter sells nothing (machining with stock), a
    /// yard's shows no new kits.
    /// </summary>
    internal void Check(string path, List<string> problems, PartShopKindRules fallback)
    {
        Stock = Figures.OneOf(Stock, $"{path}.stock", fallback.Stock, [ShelvesStock, YardStock, NoStock], problems);
        Parts = Figures.OneOf(Parts, $"{path}.parts", fallback.Parts, [MakesParts, SpeedParts, RunningGearParts, AnyParts, NoParts], problems);
        Kits = Figures.OneOf(Kits, $"{path}.kits", fallback.Kits, [FactoryKits, AftermarketKits, NoKits], problems);

        if (Machining && Stock != NoStock)
        {
            problems.Add($"{path}: a machine shop's counter sells nothing (machining, with stock \"{Stock}\"); stock {NoStock}");
            Stock = NoStock;
        }
        if (Yard && Kits != NoKits)
        {
            problems.Add($"{path}: a yard sells no new kits (kits \"{Kits}\"); kits {NoKits}");
            Kits = NoKits;
        }
    }

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
