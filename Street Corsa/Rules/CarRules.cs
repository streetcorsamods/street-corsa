namespace StreetCorsa.Rules;

/// <summary>
/// What a car is worth new and how common it is (<c>"cars"</c> in the rules; how common a car is may be cars.json's, its
/// badge's makes.json's). A car costs what its body and its parts cost (<see cref="Parts.Cars.CarPricing"/>): the body by
/// its weight, here, the parts by what they do (part_groups.json). The catalog's prices are the program's, not a career's:
/// they follow the mods next to the game (<see cref="RuleBook.Installed"/>), worked out again at every start.
/// </summary>
public sealed class CarRules
{
    /// <summary>What the bare body of a car of <see cref="BodyKg"/> costs, and one whose weight is not known</summary>
    public double BodyPrice { get; set; } = Parts.Cars.CarPricing.Defaults.BodyPrice;

    public double BodyKg { get; set; } = Parts.Cars.CarPricing.Defaults.BodyKg;

    /// <summary>
    /// How much more a lighter body costs, and less a heavier one: as <see cref="BodyKg"/> over its weight to this power.
    /// A racer is its power to weight; the power is in the price of its engine.
    /// </summary>
    public double BodyWeightPower { get; set; } = Parts.Cars.CarPricing.Defaults.WeightPower;

    /// <summary>The cheapest and dearest a body costs</summary>
    public double MinBodyPrice { get; set; } = Parts.Cars.CarPricing.Defaults.MinBody;

    public double MaxBodyPrice { get; set; } = Parts.Cars.CarPricing.Defaults.MaxBody;

    /// <summary>The cheapest and dearest a car is worth new</summary>
    public decimal MinPrice { get; set; } = 500m;

    public decimal MaxPrice { get; set; } = 500000m;

    /// <summary>Prices are rounded to this</summary>
    public decimal PriceStep { get; set; } = 100m;

    /// <summary>
    /// Weights outside this are misprints ("1.250" read the wrong way, a weight in tonnes or pounds): no road car weighs
    /// under 300 kg or over 5 t. The body is then priced as one whose weight is not known.
    /// </summary>
    public double MinPlausibleWeightKg { get; set; } = Parts.Cars.CarPricing.Defaults.MinPlausibleKg;

    public double MaxPlausibleWeightKg { get; set; } = Parts.Cars.CarPricing.Defaults.MaxPlausibleKg;

    /// <summary>How common a car is before its year, source, badge and power (0 rare to 1 common)</summary>
    public float Common { get; set; } = 0.5f;

    /// <summary>Added to how common a car is for its year: the first step it is in, newest first</summary>
    public YearStep[] CommonByYear { get; set; } =
    [
        new() { From = 2010, Value = 0.3 },
        new() { From = 2000, Value = 0.2 },
        new() { From = 1990, Value = 0.1 },
        new() { From = 1970, Value = 0.0 },
        new() { Value = -0.1 }
    ];

    /// <summary>Added to how common a car is for where it comes from</summary>
    public Dictionary<string, double> CommonBySource { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kunos"] = 0.2,
        ["dlc"] = 0.2
    };

    /// <summary>A car quicker than this (hp per tonne) is an exotic: <see cref="FastCommon"/> is added</summary>
    public double FastHpPerTonne { get; set; } = 300;

    public double FastCommon { get; set; } = -0.2;

    /// <summary>A car slower than this (hp per tonne) is an economy car: <see cref="SlowCommon"/> is added</summary>
    public double SlowHpPerTonne { get; set; } = 100;

    public double SlowCommon { get; set; } = 0.2;

    /// <summary>The value of the first step a year is in; 0 for none (no step, or no year)</summary>
    public static double ForYear(IEnumerable<YearStep> steps, int? year, double none)
    {
        if (!year.HasValue) return none;
        foreach (var step in steps.OrderByDescending(s => s.From ?? int.MinValue))
        {
            if (step.From is not { } from || year.Value >= from) return step.Value;
        }

        return none;
    }

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Above0(this, path, nameof(BodyPrice), (double)Parts.Cars.CarBook.MaxCarPrice, problems);
        Figures.Above0(this, path, nameof(BodyKg), 100_000, problems);
        Figures.Within(this, path, nameof(BodyWeightPower), 0, 10, problems);
        Figures.Above0(this, path, nameof(PriceStep), 1_000_000, problems);
        Figures.Money(this, path, problems, (double)Parts.Cars.CarBook.MaxCarPrice, nameof(MinBodyPrice), nameof(MaxBodyPrice), nameof(MinPrice),
            nameof(MaxPrice));
    }
}

/// <summary>A step by the car's year: from that year on (none: any year), the value</summary>
public sealed class YearStep
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? From { get; set; }

    public double Value { get; set; }
}
