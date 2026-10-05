namespace StreetCorsa.Rules;

/// <summary>The paper (<c>"news"</c> in the rules): what it prints, how long, and what is worth a story</summary>
public sealed class NewsRules
{
    /// <summary>How far back the paper goes</summary>
    public int DaysPrinted { get; set; } = 3;

    /// <summary>Pieces on the page</summary>
    public int OnThePage { get; set; } = 5;

    /// <summary>How long a piece is kept at all</summary>
    public int DaysKept { get; set; } = 14;

    public int MaxKept { get; set; } = 40;

    /// <summary>A cash race worth writing about, at the least</summary>
    public decimal BigWager { get; set; } = 1000m;

    /// <summary>How much higher the rival's name has to be for the player's win to be an upset</summary>
    public int UpsetReputation { get; set; } = 20;

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(DaysPrinted), 0, 365, problems);
        Figures.Within(this, path, nameof(OnThePage), 0, 100, problems);
        Figures.Within(this, path, nameof(DaysKept), 0, 3650, problems);
        Figures.Within(this, path, nameof(MaxKept), 0, 10_000, problems);
        Figures.Money(this, path, problems, 10_000_000, nameof(BigWager));
    }
}
