namespace StreetCorsa.Rules;

/// <summary>
/// The car dealers coming and going (<c>"dealers"</c> in the rules): how many are open, how trade moves, closing-down
/// sales, and when a closed one may open again
/// </summary>
public sealed class DealerRules
{
    /// <summary>The share of all the dealers there are that is open at a time: 12 of the street rods' 20</summary>
    public double OpenShare { get; set; } = 0.6;

    /// <summary>Never fewer open than this share, whatever chance says</summary>
    public double MinOpenShare { get; set; } = 0.4;

    /// <summary>A lot closes or opens about this often: five or six times a year, so most last the whole game</summary>
    public double DaysPerChange { get; set; } = 60;

    /// <summary>The closing-down sale: announced in the paper, and the doors shut the morning after its last day</summary>
    public int SaleDays { get; set; } = 7;

    /// <summary>What the sale takes off every car, and off it again in the last <see cref="FinalDays"/> days</summary>
    public decimal SaleDiscount { get; set; } = 0.20m;

    public decimal FinalDiscount { get; set; } = 0.35m;

    public int FinalDays { get; set; } = 3;

    /// <summary>A lot that opened during the game gets this long to find its feet before it can go under</summary>
    public int NewDealerGraceDays { get; set; } = 120;

    /// <summary>Premises stand empty at least this long before somebody opens there again (under the old name)</summary>
    public int ReopenAfterDays { get; set; } = 180;

    /// <summary>How fast a dealer's sales are forgotten: after this many days a sale counts half</summary>
    public double TradeHalfLifeDays { get; set; } = 30;

    /// <summary>A car on the lot this long without a buyer is one that is not selling</summary>
    public int StaleDays { get; set; } = 10;

    /// <summary>What a new lot starts with in <see cref="DealerLocation.Trade"/>: the custom of its first weeks</summary>
    public double OpeningTrade { get; set; } = 1;

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Above0(this, path, nameof(DaysPerChange), 100_000, problems);
        Figures.Above0(this, path, nameof(TradeHalfLifeDays), 100_000, problems);
        Figures.Within(this, path, nameof(SaleDiscount), 0, 1, problems);
        Figures.Within(this, path, nameof(FinalDiscount), 0, 1, problems);
        Figures.Within(this, path, nameof(SaleDays), 0, 3650, problems);
        Figures.Within(this, path, nameof(FinalDays), 0, 3650, problems);
    }
}
