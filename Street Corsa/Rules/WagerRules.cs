namespace StreetCorsa.Rules;

/// <summary>What can be wagered on a race (<c>"wagers"</c> in the rules), and who counts as a fair match</summary>
public sealed class WagerRules
{
    /// <summary>The smallest cash wager: a drag race is a small bet, a road race a bigger one</summary>
    public decimal DragMinimum { get; set; } = 10m;

    public decimal RoadMinimum { get; set; } = 25m;

    /// <summary>The house limit on a cash wager in the day, against a rival of ordinary standing, before either racer's money is counted</summary>
    public decimal DragMaximum { get; set; } = 100m;

    public decimal RoadMaximum { get; set; } = 250m;

    /// <summary>Reputation points either way within which an opponent counts as a fair match</summary>
    public int MatchedReputationBand { get; set; } = 15;

    /// <summary>From this hour of the game's day the street is racing for real money</summary>
    public int NightFromHour { get; set; } = 20;

    /// <summary>How much more the stakes go at night</summary>
    public decimal NightStakes { get; set; } = 2m;

    /// <summary>Reputation above which a rival plays for more: the stakes grow by one for every <see cref="ReputationPerStake"/> points over it</summary>
    public int StakesReputation { get; set; } = 50;

    public int ReputationPerStake { get; set; } = 25;

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Money(this, path, problems, 10_000_000, nameof(DragMinimum), nameof(RoadMinimum), nameof(DragMaximum), nameof(RoadMaximum));
        Figures.Within(this, path, nameof(NightStakes), 0, 100, problems);
        Figures.Above0(this, path, nameof(ReputationPerStake), 1000, problems);
    }
}
