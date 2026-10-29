using System.Globalization;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Parts.Export;

/// <summary>
/// The chassis parts on the car's own slots as Assetto Corsa data (chassis tuning step 42, phase 2): a spoiler is a
/// wing of its own in aero.ini, whatever the car's own wings are (the Kunos street cars' rear "wing" is a small
/// downforce table, a mod's may be lift), and an electronics unit puts ABS or traction control in electronics.ini or
/// takes it out. They count only where the player's own assists leave the car's to it (AC's "factory" setting).
/// </summary>
public static class AcChassisData
{
    private const string AeroFile = "aero.ini";
    private const string ElectronicsFile = "electronics.ini";

    /// <summary>The spoiler's wing in aero.ini, and the start of its tables' file names</summary>
    public const string SpoilerWing = "SC_SPOILER";

    private const string LiftTable = "sc_spoiler_cl.lut";
    private const string DragTable = "sc_spoiler_cd.lut";

    // Where a spoiler goes on a car whose aero.ini names no rear wing: over the boot, from the centre of gravity
    private const string RearPosition = "0,0.35,-2.00";

    /// <param name="parts">The parts on the car's chassis slots, as tuned (<see cref="RunningGear.Chassis"/>)</param>
    /// <param name="read">The car's files as the parts made them so far</param>
    /// <returns>File name to new content, for the files that change (the spoiler's tables among them)</returns>
    public static Dictionary<string, string> Generate(IReadOnlyList<PartDefinition> parts, Func<string, string?> read)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (parts.FirstOrDefault(p => PartKinds.GroupOf(p) == PartKinds.Spoilers && (RunningGear.Downforce(p) > 0 || RunningGear.SpoilerDrag(p) > 0)) is { } spoiler && read(AeroFile) is { } aeroText)
        {
            var aero = new IniText(aeroText);
            var wings = aero.Sections.Where(s => s.StartsWith("WING_", StringComparison.OrdinalIgnoreCase)).ToList();
            if (wings.All(w => !string.Equals(aero.Get(w, "NAME"), SpoilerWing, StringComparison.OrdinalIgnoreCase)))
            {
                // Next to the car's own rear wing when it has one: the boot lid, where the car's author put the rear's lift
                var rear = wings.FirstOrDefault(w => string.Equals(aero.Get(w, "NAME"), "REAR", StringComparison.OrdinalIgnoreCase));
                var section = "WING_" + Next(wings).ToString(CultureInfo.InvariantCulture);
                aero.Set(section, "NAME", SpoilerWing);
                aero.Set(section, "CHORD", RunningGear.SpoilerChord(spoiler), "0.00");
                aero.Set(section, "SPAN", RunningGear.SpoilerSpan(spoiler), "0.00");
                aero.Set(section, "POSITION", (rear == null ? null : aero.Get(rear, "POSITION")) ?? RearPosition);
                aero.Set(section, "LUT_AOA_CL", LiftTable);
                aero.Set(section, "LUT_GH_CL", string.Empty);
                aero.Set(section, "CL_GAIN", "1");
                aero.Set(section, "LUT_AOA_CD", DragTable);
                aero.Set(section, "LUT_GH_CD", string.Empty);
                aero.Set(section, "CD_GAIN", "1");
                aero.Set(section, "ANGLE", "0");
                files[AeroFile] = aero.ToString();

                // The same figure at every angle of attack: a fixed spoiler. AC's wing CL is downforce when positive (the
                // installed GT and Mines wings run 0.2 to 0.5, the lift-y 60s rears negative)
                files[LiftTable] = Table(RunningGear.Downforce(spoiler));
                files[DragTable] = Table(RunningGear.SpoilerDrag(spoiler));
            }
        }

        var abs = parts.Select(RunningGear.Abs).LastOrDefault(a => a != null);
        var tc = parts.Select(RunningGear.TractionControl).LastOrDefault(t => t != null);
        if (abs != null || tc != null)
        {
            var electronics = new IniText(read(ElectronicsFile) ?? string.Empty);
            if (abs is { } absOn) Switch(electronics, "ABS", absOn, 0.13);
            if (tc is { } tcOn) Switch(electronics, "TRACTION_CONTROL", tcOn, 0.12);
            if (electronics.Changed) files[ElectronicsFile] = electronics.ToString();
        }

        return files;
    }

    /// <summary>The number after the highest WING_ section the car has</summary>
    private static int Next(IEnumerable<string> wings) =>
        wings.Select(w => int.TryParse(w["WING_".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1).DefaultIfEmpty(-1).Max() + 1;

    private static string Table(double value)
    {
        var figure = value.ToString("0.0000", CultureInfo.InvariantCulture);
        return string.Join("\r\n", new[] { -30, -10, 0, 10, 30 }.Select(aoa => $"{aoa}|{figure}")) + "\r\n";
    }

    /// <summary>An aid in or out: present and on, or not there at all; one the car lacks gets AC's plainest figures</summary>
    private static void Switch(IniText ini, string section, bool on, double slip)
    {
        if (on && ini.Get(section, "SLIP_RATIO_LIMIT") == null)
        {
            ini.Set(section, "SLIP_RATIO_LIMIT", slip, "0.00");
            ini.Set(section, "CURVE", string.Empty);
            ini.Set(section, "RATE_HZ", "100");
            if (section == "TRACTION_CONTROL") ini.Set(section, "MIN_SPEED_KMH", "20");
        }

        if (!on && ini.Get(section, "PRESENT") == null) return;
        ini.Set(section, "PRESENT", on ? "1" : "0");
        ini.Set(section, "ACTIVE", on ? "1" : "0");
    }
}
