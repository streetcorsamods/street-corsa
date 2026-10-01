namespace StreetCorsa.Rules;

/// <summary>What parts cost, and the paper's parts ads (<c>"economy"</c> in the rules)</summary>
public sealed class EconomyRules
{
    /// <summary>
    /// From the money of the part scripts to the money of the game: the scripts price parts in the dollars of the early
    /// 2000s, the base game's cars cost what cars cost in 1970, about a fifth
    /// </summary>
    public double PartsPriceScale { get; set; } = 0.2;

    /// <summary>What a shop asks for a used part, against the same part new in the same shape</summary>
    public double UsedShopFactor { get; set; } = 0.6;

    /// <summary>What a shop pays when it buys a part, against what the part is worth</summary>
    public double TradeInFactor { get; set; } = 0.4;

    /// <summary>The mail order sells everything there is, for this much over list, and it takes <see cref="MailOrderDays"/> to come</summary>
    public double MailOrderPremium { get; set; } = 1.2;

    public int MailOrderDays { get; set; } = 3;

    /// <summary>How many parts the paper's private sellers advertise at once, least to most</summary>
    public int PartAdsLow { get; set; } = 25;

    public int PartAdsHigh { get; set; } = 40;

    /// <summary>Days a part's ad stays in the paper</summary>
    public int PartAdDays { get; set; } = 10;

    /// <summary>How worn the advertised parts are, least to most (0 new, 1 worn out)</summary>
    public double PartAdMinWear { get; set; } = 0.35;

    public double PartAdMaxWear { get; set; } = 0.92;

    /// <summary>Now and then somebody sells a whole engine instead of its parts: the chance of each ad</summary>
    public double PartAdEngineChance { get; set; } = 0.08;

    /// <summary>What a prize camshaft is worth in cash when no better one fits the car that won it</summary>
    public decimal CamshaftPrizeCash { get; set; } = 500m;
}
