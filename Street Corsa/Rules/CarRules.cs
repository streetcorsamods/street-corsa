namespace StreetCorsa.Rules;

/// <summary>
/// What a car is worth new and how common it is, worked out from its data where the mods give it neither
/// (<c>"cars"</c> in the rules; a car's own figures are cars.json's, its badge's makes.json's). The catalog's prices are
/// the program's, not a career's: they follow the mods next to the game (<see cref="RuleBook.Installed"/>), and a
/// change to these figures works every generated price out again.
/// </summary>
public sealed class CarRules
{
    /// <summary>What a car costs at <see cref="ReferenceHpPerTonne"/>, before its year, badge and source</summary>
    public double ReferencePrice { get; set; } = 7000;

    /// <summary>Power to weight, hp per tonne, of a car worth <see cref="ReferencePrice"/>: an economy car</summary>
    public double ReferenceHpPerTonne { get; set; } = 70;

    /// <summary>
    /// How fast the price climbs with the power to weight: as its ratio to the reference to this power. At 1.3 a muscle
    /// car (150 hp/t) is worth about $18,000 and a supercar (350 hp/t) $57,000. Dearer the quicker, always.
    /// </summary>
    public double PriceExponent { get; set; } = 1.3;

    /// <summary>What a car whose data gives no power or weight is worth, before its year, badge and source</summary>
    public decimal NoSpecsPrice { get; set; } = 15000m;

    /// <summary>The cheapest and dearest a car is worth new</summary>
    public decimal MinPrice { get; set; } = 5000m;

    public decimal MaxPrice { get; set; } = 500000m;

    /// <summary>Prices are rounded to this</summary>
    public decimal PriceStep { get; set; } = 100m;

    /// <summary>
    /// Weights outside this are misprints ("1.250" read the wrong way, a weight in tonnes or pounds): no road car weighs
    /// under 300 kg or over 5 t. The car is then priced as one without specs.
    /// </summary>
    public double MinPlausibleWeightKg { get; set; } = 300;

    public double MaxPlausibleWeightKg { get; set; } = 5000;

    /// <summary>What the car's year does to its price: the first step it is in, newest first; a car with no year has none</summary>
    public YearStep[] PriceByYear { get; set; } =
    [
        new() { From = 2010, Value = 1.1 },
        new() { From = 2000, Value = 1.0 },
        new() { From = 1990, Value = 0.9 },
        new() { From = 1980, Value = 0.7 },
        new() { From = 1970, Value = 0.6 },
        new() { From = 1960, Value = 1.2 },
        new() { Value = 1.5 }
    ];

    /// <summary>What where the car comes from does to its price: <c>kunos</c>, <c>dlc</c>, <c>mod</c>, <c>unknown</c></summary>
    public Dictionary<string, double> PriceBySource { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kunos"] = 1.2,
        ["dlc"] = 1.2,
        ["mod"] = 0.9,
        ["unknown"] = 1.0
    };

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
}

/// <summary>A step by the car's year: from that year on (none: any year), the value</summary>
public sealed class YearStep
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? From { get; set; }

    public double Value { get; set; }
}
