using StreetCorsa.Models.GameState;

namespace StreetCorsa.Rules;

/// <summary>
/// The difficulty presets the New Game screen offers (<c>"difficulty"</c> in the rules), and how often pink slips come
/// up at each setting. A career keeps the figures it was started on (its <see cref="GameRules"/>).
/// </summary>
public sealed class DifficultyRules
{
    public DifficultyPreset Easy { get; set; } = new()
    {
        CarPriceMultiplier = 0.85,
        PartPriceMultiplier = 0.85,
        RacePrizeMultiplier = 1.25,
        OpponentSkillModifier = -3,
        OpponentAggressionModifier = -15,
        CarWearMultiplier = 0.6,
        PinkSlipFrequency = PinkSlipFrequency.Low
    };

    public DifficultyPreset Normal { get; set; } = new();

    public DifficultyPreset Hard { get; set; } = new()
    {
        CarPriceMultiplier = 1.15,
        PartPriceMultiplier = 1.15,
        RacePrizeMultiplier = 0.85,
        OpponentSkillModifier = 3,
        OpponentAggressionModifier = 15,
        CarWearMultiplier = 1.3,
        PinkSlipFrequency = PinkSlipFrequency.High
    };

    /// <summary>How much more or less often pink slips come up than at the medium frequency, as a factor on the chance</summary>
    public double LowPinkSlips { get; set; } = 0.5;

    public double HighPinkSlips { get; set; } = 1.75;

    public DifficultyPreset For(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => Easy ?? new DifficultyPreset(),
        Difficulty.Hard => Hard ?? new DifficultyPreset(),
        _ => Normal ?? new DifficultyPreset()
    };

    public double PinkSlipFactor(PinkSlipFrequency frequency) => frequency switch
    {
        PinkSlipFrequency.Low => LowPinkSlips,
        PinkSlipFrequency.High => HighPinkSlips,
        _ => 1.0
    };

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Easy.Check($"{path}.easy", problems);
        Normal.Check($"{path}.normal", problems);
        Hard.Check($"{path}.hard", problems);
    }
}

/// <summary>One difficulty's figures (see <see cref="GameRules"/> for what each does)</summary>
public sealed class DifficultyPreset
{
    public double CarPriceMultiplier { get; set; } = 1.0;
    public double PartPriceMultiplier { get; set; } = 1.0;
    public double RacePrizeMultiplier { get; set; } = 1.0;
    public int OpponentSkillModifier { get; set; }
    public int OpponentAggressionModifier { get; set; }
    public double CarWearMultiplier { get; set; } = 1.0;
    public PinkSlipFrequency PinkSlipFrequency { get; set; } = PinkSlipFrequency.Medium;

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Above0(this, path, nameof(CarPriceMultiplier), 100, problems);
        Figures.Above0(this, path, nameof(PartPriceMultiplier), 100, problems);
        Figures.Within(this, path, nameof(RacePrizeMultiplier), 0, 100, problems);
        Figures.Within(this, path, nameof(CarWearMultiplier), 0, 100, problems);
    }
}
