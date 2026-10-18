using Newtonsoft.Json;

namespace StreetCorsa.Parts;

/// <summary>
/// The cars a factory part came in, as its pack says (<c>"came_in"</c>): the makes and the corporate families of
/// makes.json, their countries, the engine torque it was put behind (Nm, the car's peak), the car's weight (kg), the
/// model years, the axle a running gear part goes on (<c>"front"</c>, <c>"rear"</c>) and the suspension it hangs from
/// (Assetto Corsa's types: a leaf spring <c>["AXLE"]</c>). All of it is optional: an empty one is a part any car may have
/// come with. A car's factory running gear is the part that fits it best
/// (<see cref="Cars.RunningGearGuess"/>), so a mod's axle or brake for its own make is guessed onto its cars without code.
/// </summary>
public sealed class CameIn
{
    [JsonProperty("families", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Families { get; set; }

    [JsonProperty("makes", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Makes { get; set; }

    /// <summary>The countries of makes.json (a make's <c>country</c>): American drums came in American cars</summary>
    [JsonProperty("countries", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Countries { get; set; }

    /// <summary>The least and the most the car weighs, kg; either may be left out</summary>
    [JsonProperty("mass", NullValueHandling = NullValueHandling.Ignore)]
    public List<double?>? Mass { get; set; }

    /// <summary><c>"front"</c> or <c>"rear"</c>: the axle a running gear part came on; none for either</summary>
    [JsonProperty("axle", NullValueHandling = NullValueHandling.Ignore)]
    public string? Axle { get; set; }

    /// <summary>The axle's suspension types (suspensions.ini TYPE) the part hangs from; none for any</summary>
    [JsonProperty("suspension", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Suspension { get; set; }

    /// <summary>
    /// A dealer's option (the Kelsey-Hayes discs of a '65 Mustang): it came in the cars it names, but a car is only
    /// guessed onto it when nothing standard fits; cars.json picks it for a car that had it
    /// </summary>
    [JsonProperty("option", NullValueHandling = NullValueHandling.Ignore)]
    public bool Option { get; set; }

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

    /// <summary>Whether the car's country is one it came in (or it names none)</summary>
    public bool FitsCountry(string? country) =>
        Countries is not { Count: > 0 } || (country != null && Countries.Contains(country, StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether it goes on this axle and this suspension (or names none)</summary>
    public bool FitsAxle(bool front, string? suspension) =>
        (string.IsNullOrEmpty(Axle) || string.Equals(Axle, front ? "front" : "rear", StringComparison.OrdinalIgnoreCase)) &&
        (Suspension is not { Count: > 0 } || (suspension != null && Suspension.Contains(suspension, StringComparer.OrdinalIgnoreCase)));

    public double MassOutside(double? mass) => Outside(mass is > 0 ? mass : null, Mass?.ElementAtOrDefault(0), Mass?.ElementAtOrDefault(1));

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
