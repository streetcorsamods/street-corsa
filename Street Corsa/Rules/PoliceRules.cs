namespace StreetCorsa.Rules;

/// <summary>
/// The street's police (<c>"police"</c> in the rules): how likely a street race draws them, what getting caught costs, and
/// how long the impound keeps a car. The chase itself is the race mode's.
/// </summary>
public sealed class PoliceRules
{
    /// <summary>From this hour the streets are dark and the patrols busier (the diner's night stakes start then too)</summary>
    public int NightFromHour { get; set; } = 20;

    /// <summary>The chance of a patrol by day and by night, before the racers' names and the stakes</summary>
    public double DayChance { get; set; } = 0.08;

    public double NightChance { get; set; } = 0.30;

    /// <summary>A known name draws attention: up to this much more at 100 reputation</summary>
    public double ReputationChance { get; set; } = 0.15;

    /// <summary>A pink slip, or a wager of <see cref="BigWager"/> or more, gets talked about beforehand</summary>
    public double PinkSlipChance { get; set; } = 0.05;

    public double BigWagerChance { get; set; } = 0.05;

    public decimal BigWager { get; set; } = 1000m;

    public double MaxChance { get; set; } = 0.5;

    /// <summary>The share of patrols that are speed traps parked round the track; the rest drive up from behind</summary>
    public double TrapShare { get; set; } = 2.0 / 3.0;

    /// <summary>The fine for a first bust, what each earlier one adds, and the most it gets to</summary>
    public decimal BaseFine { get; set; } = 750m;

    public decimal FinePerBust { get; set; } = 500m;

    public decimal MaxFine { get; set; } = 5000m;

    /// <summary>Days in the impound for a first bust, one more for each earlier one, up to the most</summary>
    public int BaseImpoundDays { get; set; } = 2;

    public int MaxImpoundDays { get; set; } = 7;

    /// <summary>What the impound charges for each day it keeps the car</summary>
    public decimal ImpoundFeePerDay { get; set; } = 100m;

    /// <summary>The impound opens at this hour</summary>
    public int ImpoundOpensHour { get; set; } = 8;

    /// <summary>
    /// The police send one car for each racer: each sticks to its prey and tries to get past it (the race mode).
    /// A track needs a pit box for each, after the racers' two.
    /// </summary>
    public int CopsPerRace { get; set; } = 2;

    /// <summary>A rival's car left in the impound this many days past its date is auctioned off</summary>
    public int AuctionAfterDays { get; set; } = 14;

    /// <summary>
    /// AC cars the police like best, by a word in the car's folder name, the first best: any car with a police skin
    /// (<c>"street_corsa_police": true</c> in its ui_skin.json) can be one, these are picked first
    /// </summary>
    public string[] PreferredCars { get; set; } = ["monaco"];
}
