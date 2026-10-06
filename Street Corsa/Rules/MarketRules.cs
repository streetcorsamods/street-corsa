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

    /// <summary>
    /// A buyer from the paper haggles every ask: the offer is drawn between this share of the asking price and all of it,
    /// so a car bought cheap and put straight back in the paper leaves little over
    /// </summary>
    public double PaperHaggleShare { get; set; } = 0.85;

    /// <summary>The least a paper buyer offers, as a share of what the car is worth (never more than the asking price)</summary>
    public double PaperFloorShare { get; set; } = 0.8;

    /// <summary>
    /// A car the player has had fewer than <see cref="PaperFreshDays"/> days draws buyers this much less often: people
    /// are wary of a car that is back in the paper the week after it was sold
    /// </summary>
    public double PaperFreshChanceFactor { get; set; } = 0.5;

    public int PaperFreshDays { get; set; } = 14;

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

    /// <summary>
    /// Share of its worth a car all rust loses (cutting it out and welding in new metal costs that much), one with its
    /// paint worn all over, and one with a cracked windscreen; each in proportion to how far it goes
    /// </summary>
    public decimal RustLoss { get; set; } = 0.3m;

    public decimal PaintWearLoss { get; set; } = 0.08m;

    public decimal CrackedScreenLoss { get; set; } = 0.02m;

    /// <summary>
    /// How many cars private people have in the paper at once, least to most: most of the classifieds are theirs, a few
    /// are the dealers' (<see cref="DealerAdsInPaper"/>)
    /// </summary>
    public int PrivateAdsLow { get; set; } = 24;

    public int PrivateAdsHigh { get; set; } = 34;

    /// <summary>The condition of a private seller's car, worst to best: as its owner left it, rougher than a lot's</summary>
    public float PrivateMinCondition { get; set; } = 0.3f;

    public float PrivateMaxCondition { get; set; } = 0.85f;

    /// <summary>The mileage of a private seller's car, least to most, km</summary>
    public int PrivateMinMileage { get; set; } = 30000;

    public int PrivateMaxMileage { get; set; } = 150000;

    /// <summary>What a private seller asks against what a lot would for the same car: no showroom to pay for</summary>
    public decimal PrivatePriceShare { get; set; } = 0.85m;

    /// <summary>How many of the cars on its lot each dealer puts in the paper</summary>
    public int DealerAdsInPaper { get; set; } = 1;

    /// <summary>
    /// How many cars each dealer keeps out back, least to most: not good enough for the lot, advertised in the paper only
    /// </summary>
    public int BackLotLow { get; set; } = 0;

    public int BackLotHigh { get; set; } = 2;

    /// <summary>The condition of a dealer's car out back, worst to best</summary>
    public float BackLotMinCondition { get; set; } = 0.25f;

    public float BackLotMaxCondition { get; set; } = 0.55f;

    /// <summary>
    /// The paper buyers' figures held to what makes sense, told: a share outside 0 to 1 (or not a number) and a negative
    /// number of days are the base game's
    /// </summary>
    public void Check(List<string> problems)
    {
        var fresh = new MarketRules();
        PaperHaggleShare = Share(PaperHaggleShare, fresh.PaperHaggleShare, "paperHaggleShare", problems);
        PaperFloorShare = Share(PaperFloorShare, fresh.PaperFloorShare, "paperFloorShare", problems);
        PaperFreshChanceFactor = Share(PaperFreshChanceFactor, fresh.PaperFreshChanceFactor, "paperFreshChanceFactor", problems);
        if (PaperFreshDays < 0)
        {
            problems.Add($"market.paperFreshDays ({PaperFreshDays}) is below 0; the base game's {fresh.PaperFreshDays} is kept");
            PaperFreshDays = fresh.PaperFreshDays;
        }
    }

    private static double Share(double value, double baseFigure, string name, List<string> problems)
    {
        if (double.IsFinite(value) && value is >= 0 and <= 1) return value;

        problems.Add(FormattableString.Invariant($"market.{name} ({value}) is no share between 0 and 1; the base game's {baseFigure} is kept"));
        return baseFigure;
    }
}
