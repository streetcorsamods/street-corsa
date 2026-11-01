namespace StreetCorsa.Rules;

/// <summary>What parts cost, and the paper's parts ads (<c>"economy"</c> in the rules)</summary>
public sealed class EconomyRules
{
    /// <summary>A factor on every part's new price: part_groups.json's prices (what the parts do) are the game's dollars at 1</summary>
    public double PartsPriceFactor { get; set; } = 1.0;

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

    /// <summary>What a private seller asks for a part in the paper, as a share of what a used parts shop asks, least to most: some more, some less</summary>
    public double PartAdMinAskShare { get; set; } = 0.8;

    public double PartAdMaxAskShare { get; set; } = 1.2;

    /// <summary>What a rival asks for a part they took off, as a share of what a used parts shop asks, least to most: around the same</summary>
    public double RivalPartMinAskShare { get; set; } = 0.85;

    public double RivalPartMaxAskShare { get; set; } = 1.1;

    /// <summary>Now and then somebody sells an engine taken apart, as a used kit: the chance of each ad</summary>
    public double PartAdKitChance { get; set; } = 0.05;

    /// <summary>The most parts a used kit is short of: what whoever took the engine apart kept back</summary>
    public int KitMostMissing { get; set; } = 3;

    /// <summary>A rival who swaps an engine sells the old one as a used kit rather than whole: the chance of it</summary>
    public double RivalTearDownChance { get; set; } = 0.5;

    /// <summary>What a prize camshaft is worth in cash when no better one fits the car that won it</summary>
    public decimal CamshaftPrizeCash { get; set; } = 500m;

    /// <summary>
    /// How much of its new price a thing worn out is still worth (0 to 1): wear takes the rest off, a part's wear and a
    /// car's body in its condition alike (<see cref="Parts.Cars.PartPricing.Shape"/>)
    /// </summary>
    public double WearFloor { get; set; } = 0.3;

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Above0(this, path, nameof(PartsPriceFactor), 1000, problems);
        Figures.Within(this, path, nameof(UsedShopFactor), 0, 10, problems);
        Figures.Within(this, path, nameof(PartAdKitChance), 0, 1, problems);
        Figures.Within(this, path, nameof(KitMostMissing), 0, 100, problems);
        Figures.Within(this, path, nameof(RivalTearDownChance), 0, 1, problems);
        Figures.Within(this, path, nameof(TradeInFactor), 0, 1, problems);
        Figures.Within(this, path, nameof(PartAdMinAskShare), 0, 10, problems);
        Figures.Within(this, path, nameof(PartAdMaxAskShare), 0, 10, problems);
        Figures.Within(this, path, nameof(WearFloor), 0, 1, problems);
        Figures.Within(this, path, nameof(MailOrderPremium), 0, 100, problems);
        Figures.Within(this, path, nameof(MailOrderDays), 0, 365, problems);
        Figures.Within(this, path, nameof(PartAdsLow), 0, 1000, problems);
        Figures.Within(this, path, nameof(PartAdsHigh), 0, 1000, problems);
        Figures.Money(this, path, problems, 10_000_000, nameof(CamshaftPrizeCash));
    }
}
