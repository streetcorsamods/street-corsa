namespace StreetCorsa.Rules;

/// <summary>How a racer's reputation moves (<c>"reputation"</c> in the rules)</summary>
public sealed class ReputationRules
{
    /// <summary>Where every racer starts, before any race</summary>
    public int Base { get; set; } = 50;

    /// <summary>What winning every race adds; a racer gets the share of it they win</summary>
    public int WinRateBonus { get; set; } = 30;

    /// <summary>Each pink slip won adds this, up to the most; each lost takes off this, up to the most</summary>
    public int PinkSlipWin { get; set; } = 5;

    public int MaxPinkSlipWins { get; set; } = 15;

    public int PinkSlipLoss { get; set; } = 5;

    public int MaxPinkSlipLosses { get; set; } = 15;

    /// <summary>A racer with this many races who wins less than this share loses this much more</summary>
    public int LoserRaces { get; set; } = 5;

    public double LoserWinRate { get; set; } = 0.3;

    public int LoserPenalty { get; set; } = 10;

    /// <summary>A racer with this many races who wins more than this share gains this much more</summary>
    public int StarRaces { get; set; } = 10;

    public double StarWinRate { get; set; } = 0.8;

    public int StarBonus { get; set; } = 10;

    /// <summary>Each race called off for jumping the start costs this, up to the most</summary>
    public int FalseStartPenalty { get; set; } = 3;

    public int MaxFalseStartPenalty { get; set; } = 15;

    /// <summary>Each time the police are outrun adds this, up to the most</summary>
    public int PoliceEscapeBonus { get; set; } = 2;

    public int MaxPoliceEscapeBonus { get; set; } = 10;
}
