using System.Globalization;
using StreetCorsa.Helpers;

namespace StreetCorsa.Parts.Export;

/// <summary>
/// A car's torque curve as Assetto Corsa reads it: the .lut engine.ini names (power.lut unless it says another), with
/// a "rpm|Nm" a line. The name comes from a car mod, so it is a plain .lut name in the data folder or none: never one
/// that leaves the folder, reaches another machine or lands on one of the .ini files.
/// </summary>
public static class AcPowerCurve
{
    public const string DefaultFile = "power.lut";

    /// <summary>The curve file <paramref name="engine"/> (engine.ini) names, when it is a plain .lut name; else <see cref="DefaultFile"/></summary>
    public static string FileName(IniText engine) =>
        engine.Get("HEADER", "POWER_CURVE")?.Trim() is { Length: > 0 } name && PathNames.IsSafeSegment(name)
            && name.EndsWith(".lut", StringComparison.OrdinalIgnoreCase)
            ? name
            : DefaultFile;

    /// <summary>
    /// The points of a .lut in file order: what is after a ';' is a comment, and a line that is not two finite numbers
    /// either side of a '|' is skipped. None for no file.
    /// </summary>
    public static List<(double Rpm, double Torque)> Points(string? lut)
    {
        var points = new List<(double Rpm, double Torque)>();
        if (lut == null) return points;
        foreach (var raw in lut.Split('\n'))
        {
            var line = raw.Split(';')[0];
            var bar = line.IndexOf('|');
            if (bar <= 0) continue;
            if (double.TryParse(line[..bar].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rpm) && double.IsFinite(rpm)
                && double.TryParse(line[(bar + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var torque) && double.IsFinite(torque))
                points.Add((rpm, torque));
        }

        return points;
    }
}
