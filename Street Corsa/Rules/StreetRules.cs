namespace StreetCorsa.Rules;

/// <summary>
/// The street (<c>"street"</c> in the rules): who comes by while cruising and how soon, what they offer, grudge rematches,
/// and the diner's talk
/// </summary>
public sealed class StreetRules
{
    /// <summary>Game minutes, on average, before somebody pulls up: by day, at dusk, and from 20:00</summary>
    public double DayWait { get; set; } = 50;

    public double DuskWait { get; set; } = 35;

    public double NightWait { get; set; } = 18;

    public int ShortestWait { get; set; } = 5;

    public int LongestWait { get; set; } = 120;

    /// <summary>A rival out to get their car back is this many times likelier to be the one who shows</summary>
    public double GrudgeWeight { get; set; } = 4;

    /// <summary>A boss cruises too, but seldom</summary>
    public double KingWeight { get; set; } = 0.25;

    /// <summary>A rival of about the player's standing (<c>wagers.matchedReputationBand</c>) is likelier than one far off</summary>
    public double MatchedWeight { get; set; } = 1.5;

    /// <summary>A drag race most of the time: the street is a straight line from light to light, more so at night</summary>
    public double DragShareByDay { get; set; } = 0.65;

    public double DragShareAtNight { get; set; } = 0.8;

    /// <summary>How often a rival of middling aggression, on Normal, offers pink slips unasked</summary>
    public double PinkSlipOffer { get; set; } = 0.06;

    /// <summary>How long a rival waits for the rematch</summary>
    public int RematchDays { get; set; } = 14;

    /// <summary>Reputation apart for a racer to talk up or down to the player; pink slips are talked of at 15</summary>
    public int TalkReputationGap { get; set; } = 20;

    public int TalkPinkSlipReputationGap { get; set; } = 15;

    /// <summary>Aggression of a hothead; a bold one takes pink slips at 60</summary>
    public int TalkAggressive { get; set; } = 70;

    public int TalkBold { get; set; } = 60;

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(ShortestWait), 1, 1440, problems);
        Figures.Within(this, path, nameof(LongestWait), 1, 1440, problems);
    }
}
