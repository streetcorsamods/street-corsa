namespace StreetCorsa.Parts.Export;

/// <summary>What the engine's export and the running gear's both do to a car's data files</summary>
public static class AcCarIni
{
    // Figures past any part the game could hold: a part file can make them, finite and absurd, and AC would be handed
    // them as a car it cannot drive (a rim's "offset": 1e300, a part's "mass": 1e38, an axle's "ratio": 1e300). Each is
    // well above anything real, so a real part never meets one. The engine's figures have their own (EngineEvaluator).
    /// <summary>Most a part may weigh, kg</summary>
    public const double MaxPartMass = 10_000;

    /// <summary>Most a rim may set its wheel in or out by, metres</summary>
    public const double MaxRimOffset = 0.3;

    /// <summary>Most a gear ratio may be</summary>
    public const double MaxRatio = 100;

    /// <summary>Most the parts may add to or take off a car's weight, kg: four of the heaviest parts</summary>
    private const double MaxMassDelta = 4 * MaxPartMass;

    /// <summary>
    /// A part's figure as the car's data may take it: NaN, infinities and anything larger either way than
    /// <paramref name="max"/> are no figure, and <paramref name="otherwise"/> stands (the factory part's, so an absurd
    /// part changes nothing, as a part without the figure does)
    /// </summary>
    public static double Bounded(double value, double max, double otherwise = 0) => double.IsFinite(value) && Math.Abs(value) <= max ? value : otherwise;

    /// <summary>A part's weight as the data takes it: its own within <see cref="MaxPartMass"/>, else <paramref name="otherwise"/></summary>
    public static double MassOf(PartDefinition? part, double otherwise = 0) => part == null ? otherwise : Bounded(part.Mass, MaxPartMass, otherwise);

    /// <summary>
    /// The car weighs what it did plus the difference the parts make. Less than half a kilo is no change,
    /// no parts take the car below half its weight, and a difference past what parts can make is none.
    /// </summary>
    public static void AddMass(IniText car, double delta)
    {
        if (!double.IsFinite(delta) || Math.Abs(delta) is < 0.5 or > MaxMassDelta || car.GetNumber("BASIC", "TOTALMASS") is not { } mass) return;

        car.Set("BASIC", "TOTALMASS", Math.Max(mass * 0.5, mass + delta), "0");
    }

    /// <summary>Where an axle keeps its wheel rate: the classic kinds on the axle section, a coil-over on the coil-over</summary>
    public static (string Section, string Key) SpringRateKey(IniText suspensions, string axle) =>
        suspensions.Get(axle, "SPRING_RATE") != null ? (axle, "SPRING_RATE") : (axle + "_COILOVER_0", "RATE");

    /// <summary>Whether a suspensions.ini TYPE is a live axle (AXLE, rear only): leaf springs fit, no camber or toe to set</summary>
    public static bool IsLiveAxle(string? suspensionType) => string.Equals(suspensionType?.Trim(), "AXLE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether an axle of the car ("FRONT", "REAR") is a live axle (<see cref="IsLiveAxle(string?)"/>)</summary>
    public static bool IsLiveAxle(IniText suspensions, string axle) => IsLiveAxle(suspensions.Get(axle, "TYPE"));

    /// <summary>
    /// Every tyre compound the car offers, in tyres.ini's order ([FRONT], [FRONT_1]... and a second [FRONT] as some authors
    /// write it), with the occurrence of its section name and whether it is a front tyre; the thermal sections left out
    /// </summary>
    public static IEnumerable<(string Section, int Occurrence, bool Front)> TyreCompounds(IniText tyres)
    {
        var seen = new Dictionary<string, int>();
        foreach (var section in tyres.Sections.ToList())
        {
            var occurrence = seen.GetValueOrDefault(section);
            seen[section] = occurrence + 1;

            var front = section.StartsWith("FRONT", StringComparison.Ordinal);
            if ((front || section.StartsWith("REAR", StringComparison.Ordinal)) && !section.Contains("THERMAL", StringComparison.Ordinal))
                yield return (section, occurrence, front);
        }
    }
}
