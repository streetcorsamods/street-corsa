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
}
