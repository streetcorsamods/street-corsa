using System.Text.Json.Serialization;
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
        StartMoneyMultiplier = 1.5,
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
        StartMoneyMultiplier = 0.75,
        OpponentSkillModifier = 3,
        OpponentAggressionModifier = 15,
        CarWearMultiplier = 1.3,
        PinkSlipFrequency = PinkSlipFrequency.High
    };

    /// <summary>How much more or less often pink slips come up than at the medium frequency, as a factor on the chance</summary>
    public double LowPinkSlips { get; set; } = 0.5;

    public double HighPinkSlips { get; set; } = 1.75;

    /// <summary>
    /// The figures the New Game screen lets the player adjust past the preset, in its order, each with the range it
    /// spans: a setting offers what fits its game (a list given is taken whole). A figure left out stays the preset's.
    /// </summary>
    public AdjustableFigure[] Adjust { get; set; } = AdjustableFigure.Every();

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
        Adjust = AdjustableFigure.Checked(Adjust ?? [], $"{path}.adjust", problems);
    }
}

/// <summary>
/// A figure of the difficulty the New Game screen offers to adjust (<c>difficulty.adjust</c>): which one, and for a
/// number the range and the step its slider takes (in percent for a multiplier); the core's range where none is given
/// </summary>
public sealed class AdjustableFigure
{
    /// <summary>The figure's name, as a preset names it: <c>carPriceMultiplier</c>, <c>pinkSlipFrequency</c>...</summary>
    public string Figure { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Min { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Max { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Step { get; set; }

    /// <summary>The figures there are, in the core's order, each with its range and step (0, 0, 0: a choice or a switch)</summary>
    public static IReadOnlyList<(string Figure, double Min, double Max, double Step)> Known { get; } =
    [
        ("carPriceMultiplier", 50, 200, 5),
        ("partPriceMultiplier", 50, 200, 5),
        ("racePrizeMultiplier", 50, 200, 5),
        ("startMoneyMultiplier", 25, 300, 5),
        ("carWearMultiplier", 25, 200, 5),
        ("opponentSkillModifier", -5, 5, 1),
        ("opponentAggressionModifier", -50, 50, 5),
        ("pinkSlipFrequency", 0, 0, 0),
        ("raceSimulationEnabled", 0, 0, 0),
        ("seasonalRacingEnabled", 0, 0, 0),
        ("marketRefreshEnabled", 0, 0, 0)
    ];

    // The most a multiplier may be, in percent: what the game plays by (Multiplier.Sane)
    private const double MostPercent = 10000;

    /// <summary>Every figure there is, at the core's ranges</summary>
    public static AdjustableFigure[] Every() => Known.Select(k => new AdjustableFigure { Figure = k.Figure }).ToArray();

    /// <summary>The range filled in from the core's where not given</summary>
    [JsonIgnore]
    public (double Min, double Max, double Step) Range
    {
        get
        {
            var known = Known.First(k => k.Figure.Equals(Figure, StringComparison.OrdinalIgnoreCase));
            return (Min ?? known.Min, Max ?? known.Max, Step ?? known.Step);
        }
    }

    /// <summary>
    /// The figures that can be offered: one the game does not have, or named twice, is left out; a range that is
    /// not one (min not below max, a step not above 0 or wider than the range, a figure not whole, a multiplier that
    /// reaches 0% or past 10000%, which the game would not play) is the core's; a range given a choice or a switch is
    /// dropped; each told
    /// </summary>
    internal static AdjustableFigure[] Checked(AdjustableFigure[] figures, string path, List<string> problems)
    {
        var kept = new List<AdjustableFigure>();
        foreach (var figure in figures)
        {
            if (figure == null) continue;
            var known = Known.FirstOrDefault(k => k.Figure.Equals(figure.Figure, StringComparison.OrdinalIgnoreCase));
            if (known.Figure == null)
            {
                problems.Add($"{path}: \"{figure.Figure}\" is no figure of the difficulty; left out");
                continue;
            }

            if (kept.Any(k => k.Figure.Equals(known.Figure, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"{path}: \"{known.Figure}\" is named twice; the first is kept");
                continue;
            }

            figure.Figure = known.Figure;
            if (known.Step == 0)
            {
                if (figure.Min != null || figure.Max != null || figure.Step != null)
                    problems.Add($"{path}: {known.Figure} is a choice or a switch, with no range; its min, max and step are left out");
                figure.Min = figure.Max = figure.Step = null;
                kept.Add(figure);
                continue;
            }

            var (min, max, step) = figure.Range;
            var multiplier = known.Figure.EndsWith("Multiplier", StringComparison.Ordinal);
            if (!(double.IsFinite(min) && double.IsFinite(max) && double.IsFinite(step) && min < max && step > 0 && step <= max - min)
                || min != Math.Floor(min) || max != Math.Floor(max) || step != Math.Floor(step)
                || (multiplier && (min < 1 || max > MostPercent)))
            {
                problems.Add(FormattableString.Invariant(
                    $"{path}: {known.Figure} from {min} to {max} by {step} is no range; the core's ({known.Min} to {known.Max} by {known.Step}) is kept"));
                figure.Min = figure.Max = figure.Step = null;
            }

            kept.Add(figure);
        }

        return kept.ToArray();
    }
}

/// <summary>One difficulty's figures (see <see cref="GameRules"/> for what each does)</summary>
public sealed class DifficultyPreset
{
    public double CarPriceMultiplier { get; set; } = 1.0;
    public double PartPriceMultiplier { get; set; } = 1.0;
    public double RacePrizeMultiplier { get; set; } = 1.0;

    /// <summary>On the rules' <c>start.money</c>: what the player starts with at this difficulty</summary>
    public double StartMoneyMultiplier { get; set; } = 1.0;

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
        Figures.Within(this, path, nameof(StartMoneyMultiplier), 0, 100, problems);
        Figures.Within(this, path, nameof(CarWearMultiplier), 0, 100, problems);
    }
}
