namespace StreetCorsa.Rules;

/// <summary>
/// The used-car market (<c>"market"</c> in the rules): the dealers' lots, selling a car (to a dealer, through an ad, for
/// scrap), and what a car is worth: its condition, its history, its mileage, what it lacks
/// </summary>
public sealed class MarketRules
{
    /// <summary>How many cars a dealer's lot holds, least to most, where its own file says nothing</summary>
    public int StockLow { get; set; } = 8;

    public int StockHigh { get; set; } = 14;

    /// <summary>The same model twice on one lot is a coincidence; three times is a car park</summary>
    public int MaxSameModelPerDealer { get; set; } = 2;

    /// <summary>The condition of the cars on the lots, worst to best (0 to 1), before each dealer's own standard</summary>
    public float MinCondition { get; set; } = 0.6f;

    public float MaxCondition { get; set; } = 0.95f;

    /// <summary>The mileage of the cars on the lots, least to most, km</summary>
    public int MinMileage { get; set; } = 5000;

    public int MaxMileage { get; set; } = 60000;

    /// <summary>A lot's asking prices spread this far either side of what the car is worth (0.2 = ±20%)</summary>
    public float PriceSpread { get; set; } = 0.2f;

    /// <summary>How hard a dealer pulls condition toward its own standard. At 1 the shift is the full
    /// distance between that standard and the middle of the range</summary>
    public float DealerConditionPull { get; set; } = 1.4f;

    /// <summary>
    /// The least a car goes back on a lot for. A car the catalog cannot price and nobody paid for (no profile, a
    /// purchase price of 0) is valued at nothing, and a listing at $0 is a free car.
    /// </summary>
    public decimal MinListPrice { get; set; } = 500m;

    /// <summary>The dealer a car traded in goes to when no dealer of the save will have it</summary>
    public string TradeInDealer { get; set; } = "industrial_motors";

    /// <summary>A used car has had an earlier owner for about every this many km on it</summary>
    public double KmPerEarlierOwner { get; set; } = 40_000;

    /// <summary>Share of a car's worth a dealer pays for it</summary>
    public double DealerShare { get; set; } = 0.6;

    /// <summary>Share of a wreck's worth the scrapyard pays, before <see cref="ScrapSpread"/></summary>
    public double ScrapShare { get; set; } = 0.15;

    /// <summary>How far the scrapyard's price strays either way, as a share of its price</summary>
    public double ScrapSpread { get; set; } = 0.2;

    /// <summary>What an ad in the classifieds costs; it runs for <see cref="AdDays"/> days</summary>
    public decimal AdFee { get; set; } = 10m;

    public int AdDays { get; set; } = 14;

    /// <summary>How long a buyer waits for an answer</summary>
    public int OfferDays { get; set; } = 2;

    /// <summary>The least a car can be put in the paper for</summary>
    public decimal MinAskingPrice { get; set; } = 100m;

    /// <summary>How far over its worth a car can be put in the paper: past this nobody would even call</summary>
    public decimal MaxAskingShare { get; set; } = 3m;

    /// <summary>The chance the buyer who calls is one of the rivals, when one of them wants the car and can pay</summary>
    public double RivalBuyerChance { get; set; } = 0.4;

    /// <summary>Share of the money put into an engine beyond the factory one that shows in the car's worth</summary>
    public double ModificationsShare { get; set; } = 0.5;

    /// <summary>What wins add to a car's worth: a point each, two for a car won with it, at most <see cref="MaxHistoryShare"/></summary>
    public decimal WinShare { get; set; } = 0.01m;

    public decimal PinkSlipWinShare { get; set; } = 0.02m;

    /// <summary>What an owner beyond the first <see cref="OwnersWithoutPenalty"/> takes off, at most <see cref="MaxOwnersPenalty"/></summary>
    public decimal OwnerPenalty { get; set; } = 0.02m;

    public int OwnersWithoutPenalty { get; set; } = 2;

    public decimal MaxOwnersPenalty { get; set; } = 0.08m;

    /// <summary>A point off for every <see cref="MileageStepKm"/> past <see cref="MileageWithoutPenaltyKm"/>, at most <see cref="MaxMileagePenalty"/></summary>
    public double MileageWithoutPenaltyKm { get; set; } = 80_000;

    public double MileageStepKm { get; set; } = 10_000;

    public decimal MileagePenalty { get; set; } = 0.01m;

    public decimal MaxMileagePenalty { get; set; } = 0.08m;

    /// <summary>The most a car's history moves its worth, either way</summary>
    public decimal MaxHistoryShare { get; set; } = 0.15m;

    /// <summary>Share of the base price a car without its engine loses: the engine is what most of a car's worth is</summary>
    public decimal MissingEngineShare { get; set; } = 0.4m;

    /// <summary>Share of the base price a corner loses without its rim and tyre (a car on blocks)</summary>
    public decimal MissingWheelShare { get; set; } = 0.03m;

    /// <summary>Share of the base price a corner loses for each of its brake, spring and shock that is missing</summary>
    public decimal MissingCornerPartShare { get; set; } = 0.01m;
}
