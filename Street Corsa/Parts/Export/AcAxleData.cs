using System.Globalization;
using System.Text;
using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Parts.Export;

/// <summary>
/// The rear axle as Assetto Corsa data (step 31). It takes the final drive and the differential over from the gearbox:
/// the ring and pinion is <c>[GEARS] FINAL</c>, the carrier <c>[DIFFERENTIAL] POWER/COAST/PRELOAD</c>. On its factory
/// parts the car keeps its own figures as its author wrote them (over what the engine's gearbox wrote); another gear set
/// writes its ratio, another carrier its lock (a worn posi's preload and lock falling off with its clutches). The axle's
/// weight moves the car's mass, and on a live axle (<c>[REAR] TYPE=AXLE</c>: all of it unsprung) the rear
/// <c>HUB_MASS</c>; the housing's gears take their share off the engine's curve (a 9-inch's pinion bearing a few percent).
/// </summary>
public static class AcAxleData
{
    private const string DrivetrainFile = "drivetrain.ini";
    private const string SuspensionsFile = "suspensions.ini";
    private const string EngineFile = "engine.ini";
    private const string CarFile = "car.ini";

    /// <param name="mounted">The axle's parts on the car</param>
    /// <param name="factory">The axle the car's data describes</param>
    /// <param name="read">The car's files as the parts made them so far (the engine's over the car's own)</param>
    /// <param name="readOwn">The car's own files, as its author left them</param>
    /// <param name="problems">What keeps the car from being driven: no axle, or an axle short of a part</param>
    /// <returns>File name to new content, for the files that change</returns>
    public static Dictionary<string, string> Generate(PartsCatalog catalog, RearAxleParts mounted, RearAxleChoice factory,
        Func<string, string?> read, Func<string, string?> readOwn, List<string> problems)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Keep(string name, IniText ini) { if (ini.Changed) files[name] = ini.ToString(); }

        if (mounted.Housing == null || catalog.Get(mounted.Housing.DefinitionId) is not { } housing)
        {
            problems.Add("no rear axle");
            return files;
        }

        // What the housing must carry, in its pack's words
        foreach (var required in housing.Needs)
        {
            if (mounted.Housing.Children.All(c => c.ParentSlot != required.Slot)) problems.Add(Lower(required.Message) ?? "the rear axle is missing a part");
        }

        var carrier = mounted.Carrier == null ? null : catalog.Get(mounted.Carrier.DefinitionId);
        if (carrier != null)
        {
            foreach (var required in carrier.Needs)
            {
                if (mounted.Carrier!.Children.All(c => c.ParentSlot != required.Slot)) problems.Add(Lower(required.Message) ?? "the differential is missing a part");
            }
        }

        var gears = mounted.Gears == null ? null : catalog.Get(mounted.Gears.DefinitionId);
        if (read(DrivetrainFile) is { } drivetrainText)
        {
            var drivetrain = new IniText(drivetrainText);
            var own = new IniText(readOwn(DrivetrainFile));

            if (gears != null)
            {
                if (gears.Id == factory.Gears.Id && own.Get("GEARS", "FINAL") is { } ownFinal) drivetrain.Set("GEARS", "FINAL", ownFinal);
                else if (AcCarIni.Bounded(RearAxle.Ratio(gears), AcCarIni.MaxRatio) is var ratio and > 0) drivetrain.Set("GEARS", "FINAL", ratio, "0.000");
            }

            if (carrier != null)
            {
                if (carrier.Id == factory.Carrier.Id && own.Sections.Contains("DIFFERENTIAL"))
                {
                    foreach (var key in new[] { "POWER", "COAST", "PRELOAD" })
                    {
                        if (own.Get("DIFFERENTIAL", key) is { } value) drivetrain.Set("DIFFERENTIAL", key, value);
                        else drivetrain.RemoveKey("DIFFERENTIAL", key);
                    }
                }
                else
                {
                    var (power, coast, preload) = Lock(carrier, mounted.Carrier!);
                    drivetrain.Set("DIFFERENTIAL", "POWER", power, "0.00");
                    drivetrain.Set("DIFFERENTIAL", "COAST", coast, "0.00");
                    drivetrain.Set("DIFFERENTIAL", "PRELOAD", preload, "0");
                }
            }

            Keep(DrivetrainFile, drivetrain);
        }

        // The weight: the whole axle against the factory one; unsprung on a live axle, half on each wheel's hub (HUB_MASS is a
        // wheel's). A pack's absurd weight is no figure (AcCarIni.MassOf): the part weighs nothing against the factory's
        var factoryMass = AcCarIni.MassOf(factory.Housing) + AcCarIni.MassOf(factory.Carrier) + AcCarIni.MassOf(factory.Gears) + 2 * AcCarIni.MassOf(factory.Shaft);
        var mass = mounted.All.Sum(p => AcCarIni.MassOf(catalog.Get(p.DefinitionId))) - factoryMass;
        if (Math.Abs(mass) > 0.05)
        {
            if (read(SuspensionsFile) is { } suspensionsText)
            {
                var suspensions = new IniText(suspensionsText);
                if (AcCarIni.IsLiveAxle(suspensions, "REAR") &&
                    suspensions.GetNumber("REAR", "HUB_MASS") is { } hub)
                    suspensions.Set("REAR", "HUB_MASS", Math.Max(hub * 0.5, hub + mass / 2), "0.0");
                Keep(SuspensionsFile, suspensions);
            }

            if (read(CarFile) is { } carText)
            {
                var car = new IniText(carText);
                AcCarIni.AddMass(car, mass);
                Keep(CarFile, car);
            }
        }

        // The gears' drag, against the factory housing's
        var drag = (1 - RearAxle.Drag(housing)) / (1 - RearAxle.Drag(factory.Housing));
        if (Math.Abs(drag - 1) > 1e-6 && read(EngineFile) is { } engineText)
        {
            var curve = AcPowerCurve.FileName(new IniText(engineText));
            if (read(curve) is { } lut) files[curve] = ScaleLut(lut, drag);
        }

        return files;
    }

    /// <summary>
    /// What the carrier locks with as it is: a clutch posi's lock and preload fall off as its clutches wear (to a little
    /// over half by the end), a spool's or a Locker's do not, since nothing in them slips
    /// </summary>
    public static (double Power, double Coast, double Preload) Lock(PartDefinition carrier, PartInstance instance)
    {
        var (power, coast, preload) = RearAxle.Lock(carrier);
        var locked = power >= 0.9;
        var wear = locked ? 1 : 0.55 + 0.45 * Unit.Clamp01(instance.Wear, 1);
        return (Math.Clamp(power * wear, 0, 1), Math.Clamp(coast * wear, 0, 1), Math.Max(0, preload * wear));
    }

    private static string? Lower(string? message) => AcRunningGearData.Lower(message);

    /// <summary>Every torque of a lut times <paramref name="factor"/>, the rest of each line as it was</summary>
    private static string ScaleLut(string lut, double factor)
    {
        var text = new StringBuilder();
        var lines = lut.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var bar = line.IndexOf('|');
            var comment = line.IndexOf(';');
            if (bar > 0 && (comment < 0 || bar < comment))
            {
                var end = comment < 0 ? line.Length : comment;
                if (double.TryParse(line[(bar + 1)..end].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var torque) && double.IsFinite(torque))
                    line = line[..(bar + 1)] + (torque * factor).ToString("0.###", CultureInfo.InvariantCulture) + (comment < 0 ? "" : " " + line[comment..]);
            }

            text.Append(line);
            if (i < lines.Length - 1) text.Append(lines[i].EndsWith('\r') ? "\r\n" : "\n");
        }

        return text.ToString();
    }
}
