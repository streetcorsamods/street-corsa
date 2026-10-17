using Newtonsoft.Json;

namespace StreetCorsa.Parts;

/// <summary>
/// The cars a factory part came in, as its pack says (<c>"came_in"</c>): the makes and the corporate families of
/// makes.json, the engine torque it was put behind (Nm, the car's peak) and the model years. All of it is optional: an
/// empty one is a part any car may have come with. A car's factory running gear is the part that fits it best
/// (<see cref="Cars.RunningGearGuess"/>), so a mod's axle or brake for its own make is guessed onto its cars without code.
/// </summary>
public sealed class CameIn
{
    [JsonProperty("families", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Families { get; set; }

    [JsonProperty("makes", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Makes { get; set; }

    /// <summary>The least and the most engine torque, Nm; either may be left out</summary>
    [JsonProperty("torque", NullValueHandling = NullValueHandling.Ignore)]
    public List<double?>? Torque { get; set; }

    /// <summary>The first and the last model year; either may be left out</summary>
    [JsonProperty("years", NullValueHandling = NullValueHandling.Ignore)]
    public List<int?>? Years { get; set; }

    public bool NamesMake(string? make) => make != null && Makes?.Contains(make, StringComparer.OrdinalIgnoreCase) == true;

    public bool NamesFamily(string? family) => !string.IsNullOrEmpty(family) && Families?.Contains(family, StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>Whether it names any make or family: one that names none came in any car</summary>
    public bool IsForMakes => Makes is { Count: > 0 } || Families is { Count: > 0 };

    /// <summary>How far a figure is outside a band, 0 inside it (or with no band, or no figure)</summary>
    public static double Outside(double? value, double? least, double? most)
    {
        if (value is not { } v || !double.IsFinite(v)) return 0;
        if (least is { } low && v < low) return low - v;
        if (most is { } high && v > high) return v - high;
        return 0;
    }

    public double TorqueOutside(double? torque) => Outside(torque, Torque?.ElementAtOrDefault(0), Torque?.ElementAtOrDefault(1));

    public double YearsOutside(int? year) => Outside(year, Years?.ElementAtOrDefault(0), Years?.ElementAtOrDefault(1));
}
