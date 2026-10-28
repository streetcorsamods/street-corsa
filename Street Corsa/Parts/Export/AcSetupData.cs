using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Parts.Export;

/// <summary>A car's setup as the export takes it (<see cref="CarSetup.For"/>)</summary>
public sealed class SetupChoice
{
    /// <summary>The car-wide items set, each an offset from what the parts make of it; an item not here is at nothing</summary>
    public IReadOnlyDictionary<string, double> Values { get; init; } = new Dictionary<string, double>();

    /// <summary>How far each car-wide item may move, from the rules and the parts on the car; an item not here may not</summary>
    public IReadOnlyDictionary<string, SetupRange> Allowed { get; init; } = new Dictionary<string, SetupRange>();
}

/// <summary>
/// A car's setup in its Assetto Corsa data. AC starts a race on the car's data (not on a setup file), but holds every
/// figure inside its setup.ini's range for it, quietly (phase 0 of chassis tuning: a camber of -5 against a MIN of -3
/// races at -3). So the game writes its values into the data (<see cref="Apply"/>, before the damage: the damage goes on
/// top), and when everything is written gives each setup.ini item the game knows the range the game allows around the
/// figure written (<see cref="Range"/>): AC's screen opens on the game's figures and moves only as far as the rules and
/// the parts would. An item the game does not set itself is only widened to take the figure in, the damage's own among
/// them (a bent rear corner on a car whose rear toe the author held to one click).
/// </summary>
public static class AcSetupData
{
    private const string SetupFile = "setup.ini";
    private const string SuspensionsFile = "suspensions.ini";
    private const string TyresFile = "tyres.ini";
    private const string BrakesFile = "brakes.ini";
    private const string CarFile = "car.ini";
    private const string DrivetrainFile = "drivetrain.ini";

    /// <summary>psi, AC's tyre pressure, to a bar, the game's</summary>
    private const double PsiPerBar = 14.5038;

    /// <summary>Metres of the data per mm of the game's toe and ride height</summary>
    private const double MetresPerMm = 0.001;

    /// <summary>How an item's range is set: around the car-wide item, by the parts' adjustable spec, or only widened</summary>
    private enum Kind
    {
        CarItem,
        PartSpec,
        Widen
    }

    /// <summary>
    /// One setup.ini item over the data's figure it holds: <paramref name="Scale"/> setup units to the data's one (the
    /// units each is in, read off what the cars installed write: pressure psi, camber degrees, toe 1/100000 m of steering
    /// rod, rod length 1/10000 m, front bias percent, spring rate N/mm, diff percent), <paramref name="StepScale"/> setup
    /// units to a STEP (camber's are tenths of a degree), <paramref name="ItemScale"/> setup units to the car-wide item's
    /// </summary>
    private sealed record Item(string Name, string File, string Section, string Key, double Scale, Kind Kind, string? Setting = null,
        double ItemScale = 1, double StepScale = 1);

    private static IEnumerable<Item> Items(IniText suspensions)
    {
        foreach (var (axle, front, corners) in new[] { ("FRONT", true, new[] { "LF", "RF" }), ("REAR", false, new[] { "LR", "RR" }) })
        {
            var end = front ? "front" : "rear";
            var (rateSection, rateKey) = AcCarIni.SpringRateKey(suspensions, axle);
            foreach (var corner in corners)
            {
                yield return new Item($"PRESSURE_{corner}", TyresFile, axle, "PRESSURE_STATIC", 1, Kind.CarItem, "pressure_" + end, PsiPerBar);
                yield return new Item($"CAMBER_{corner}", SuspensionsFile, axle, "STATIC_CAMBER", 1, Kind.CarItem, "camber_" + end, StepScale: 0.1);
                yield return new Item($"TOE_OUT_{corner}", SuspensionsFile, axle, "TOE_OUT", 100_000, Kind.CarItem, "toe_" + end, 100_000 * MetresPerMm);
                yield return new Item($"ROD_LENGTH_{corner}", SuspensionsFile, axle, "ROD_LENGTH", 10_000, Kind.CarItem, "ride_height_" + end, 10_000 * MetresPerMm);
                yield return new Item($"SPRING_RATE_{corner}", SuspensionsFile, rateSection, rateKey, 0.001, Kind.PartSpec, SpringSpec + end);
                foreach (var key in new[] { "DAMP_BUMP", "DAMP_FAST_BUMP", "DAMP_REBOUND", "DAMP_FAST_REBOUND" })
                    yield return new Item($"{key}_{corner}", SuspensionsFile, axle, key, 1, Kind.PartSpec, ShockSpec + end);
            }

            yield return new Item($"ARB_{axle}", SuspensionsFile, "ARB", axle, 1, Kind.PartSpec, SwayBarSpec + end);
        }

        yield return new Item("FRONT_BIAS", BrakesFile, "DATA", "FRONT_SHARE", 100, Kind.CarItem, CarSetup.BrakeBias, 100);
        yield return new Item("DIFF_POWER", DrivetrainFile, "DIFFERENTIAL", "POWER", 100, Kind.Widen);
        yield return new Item("DIFF_COAST", DrivetrainFile, "DIFFERENTIAL", "COAST", 100, Kind.Widen);
        yield return new Item("DIFF_PRELOAD", DrivetrainFile, "DIFFERENTIAL", "PRELOAD", 1, Kind.Widen);
    }

    // The part specs an axle's setup items are moved by, keyed with the axle ("force:front"): a spring's rate, a shock's damping, a sway bar's
    private const string SpringSpec = "force:";
    private const string ShockSpec = "damping:";
    private const string SwayBarSpec = "arb_rate:";

    /// <summary>The car-wide items' values into the data: file name to new content, for the files that change</summary>
    public static Dictionary<string, string> Apply(SetupChoice setup, Func<string, string?> readFile)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        double Value(string item) => setup.Values.TryGetValue(item, out var value) && double.IsFinite(value) ? value : 0;
        void Keep(string name, IniText ini) { if (ini.Changed) files[name] = ini.ToString(); }

        if (readFile(SuspensionsFile) is { } suspensionsText)
        {
            var ini = new IniText(suspensionsText);
            foreach (var (axle, end) in new[] { ("FRONT", "front"), ("REAR", "rear") })
            {
                Add(ini, axle, "STATIC_CAMBER", Value("camber_" + end), "0.00", absentIsZero: true);
                Add(ini, axle, "TOE_OUT", Value("toe_" + end) * MetresPerMm, "0.000000", absentIsZero: true);
                Add(ini, axle, "ROD_LENGTH", Value("ride_height_" + end) * MetresPerMm, "0.0000");
            }

            Keep(SuspensionsFile, ini);
        }

        if (readFile(TyresFile) is { } tyresText)
        {
            // Every compound the car offers, as the running gear goes over them
            var ini = new IniText(tyresText);
            var seen = new Dictionary<string, int>();
            foreach (var section in ini.Sections.ToList())
            {
                var occurrence = seen.GetValueOrDefault(section);
                seen[section] = occurrence + 1;
                var end = section.StartsWith("FRONT", StringComparison.Ordinal) ? "front" : section.StartsWith("REAR", StringComparison.Ordinal) ? "rear" : null;
                if (end == null || section.Contains("THERMAL", StringComparison.Ordinal)) continue;

                if (Value("pressure_" + end) is not 0 and var bar && ini.GetNumber(section, "PRESSURE_STATIC", occurrence) is { } psi)
                    ini.Set(section, "PRESSURE_STATIC", Math.Max(1, psi + bar * PsiPerBar), "0.0", occurrence);
            }

            Keep(TyresFile, ini);
        }

        if (Value(CarSetup.BrakeBias) is not 0 and var bias && readFile(BrakesFile) is { } brakesText)
        {
            var ini = new IniText(brakesText);
            if (ini.GetNumber("DATA", "FRONT_SHARE") is { } share) ini.Set("DATA", "FRONT_SHARE", Math.Clamp(share + bias, 0, 1), "0.000");
            Keep(BrakesFile, ini);
        }

        if (Value(CarSetup.SteerLock) is not 0 and var lockBy && readFile(CarFile) is { } carText)
        {
            var ini = new IniText(carText);
            if (ini.GetNumber("CONTROLS", "STEER_LOCK") is { } steerLock) ini.Set("CONTROLS", "STEER_LOCK", Math.Max(1, steerLock + lockBy), "0");
            Keep(CarFile, ini);
        }

        return files;
    }

    /// <summary>A value added to the data's figure; a key the car does not have is 0 when <paramref name="absentIsZero"/> (AC's own), else left out</summary>
    private static void Add(IniText ini, string section, string key, double by, string format, bool absentIsZero = false)
    {
        if (by == 0 || !double.IsFinite(by)) return;

        if (ini.GetNumber(section, key) is { } value) ini.Set(section, key, value + by, format);
        else if (absentIsZero && ini.Sections.Contains(section)) ini.Set(section, key, by, format);
    }

    /// <summary>
    /// setup.ini's items ranged to the figures the data ends up with, the damage's included: file name to new content,
    /// nothing when the car has no setup.ini or every range already holds
    /// </summary>
    /// <param name="runningGear">What is on the wheel slots, for the springs' and shocks' adjustable specs; null leaves
    /// the suspension's items only widened</param>
    public static Dictionary<string, string> Range(SetupChoice setup, PartsCatalog catalog, CornerParts[]? runningGear,
        (PartInstance? Front, PartInstance? Rear)? swayBars, Func<string, string?> readFile)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (readFile(SetupFile) is not { } setupText) return files;

        var ini = new IniText(setupText);
        var sections = ini.Sections.ToHashSet(StringComparer.Ordinal);
        var data = new Dictionary<string, IniText?>(StringComparer.OrdinalIgnoreCase);
        IniText? Data(string name) => data.TryGetValue(name, out var read) ? read : data[name] = readFile(name) is { } text ? new IniText(text) : null;

        var ratios = PartRatios(catalog, runningGear, swayBars);
        foreach (var item in Items(Data(SuspensionsFile) ?? new IniText(null)))
        {
            if (!sections.Contains(item.Name) || Data(item.File)?.GetNumber(item.Section, item.Key) is not { } figure) continue;
            var value = figure * item.Scale;
            var (min, max) = (ini.GetNumber(item.Name, "MIN"), ini.GetNumber(item.Name, "MAX"));

            var kind = item.Kind == Kind.PartSpec && !ratios.ContainsKey(item.Setting!) ? Kind.Widen : item.Kind;
            double low, high;
            switch (kind)
            {
                case Kind.CarItem:
                {
                    var at = setup.Values.GetValueOrDefault(item.Setting!);
                    var range = setup.Allowed.GetValueOrDefault(item.Setting!) ?? SetupRange.Fixed;
                    (low, high) = (value + (Math.Min(range.Min, at) - at) * item.ItemScale, value + (Math.Max(range.Max, at) - at) * item.ItemScale);
                    break;
                }
                case Kind.PartSpec:
                {
                    var (lo, hi) = ratios[item.Setting!];
                    (low, high) = (Math.Min(value * lo, value * hi), Math.Max(value * lo, value * hi));
                    break;
                }
                default:
                    (low, high) = (Math.Min(min ?? value, value), Math.Max(max ?? value, value));
                    break;
            }

            if (kind != Kind.Widen)
            {
                // On the grid of the item's steps from the figure, inside the range: AC's screen steps from MIN
                var step = ini.GetNumber(item.Name, "STEP") is > 0 and var s ? s * item.StepScale : 0;
                if (step > 0)
                {
                    low = value - Math.Floor((value - low) / step + 1e-6) * step;
                    high = value + Math.Floor((high - value) / step + 1e-6) * step;
                }
            }

            if (min is { } oldMin && Math.Abs(oldMin - low) < 1e-9 && max is { } oldMax && Math.Abs(oldMax - high) < 1e-9) continue;
            ini.Set(item.Name, "MIN", Figure(low));
            ini.Set(item.Name, "MAX", Figure(high));
        }

        if (ini.Changed) files[SetupFile] = ini.ToString();
        return files;
    }

    private static string Figure(double value) => Math.Round(value, 6).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// How far the axles' springs, shocks and sway bars let their figures move, as ratios to where they are now: each
    /// part's <c>adjust</c> on its own spec over the spec as tuned (a coilover's damping from 2000 to 6000, at 3000 now:
    /// half to twice). An axle whose parts adjust nothing stays at 1 to 1; none at all without the running gear.
    /// </summary>
    private static Dictionary<string, (double Lo, double Hi)> PartRatios(PartsCatalog catalog, CornerParts[]? runningGear,
        (PartInstance? Front, PartInstance? Rear)? swayBars)
    {
        var ratios = new Dictionary<string, (double Lo, double Hi)>(StringComparer.Ordinal);
        if (runningGear is not { Length: RunningGear.Corners }) return ratios;

        void Over(string key, IEnumerable<PartInstance?> parts, string spec, Func<PartDefinition, double> figure)
        {
            var (lo, hi) = (1.0, 1.0);
            foreach (var definition in parts.Select(p => RunningGear.Definition(catalog, p)).OfType<PartDefinition>())
            {
                var now = figure(definition);
                if (now <= 0 || !double.IsFinite(now) || !CarSetup.AdjustsOf(definition).TryGetValue(spec, out var range)) continue;
                lo = Math.Min(lo, Math.Max(0, range.Min) / now);
                hi = Math.Max(hi, range.Max / now);
            }

            ratios[key] = (lo, hi);
        }

        foreach (var (end, corners) in new[] { ("front", runningGear.Take(2)), ("rear", runningGear.Skip(2)) })
        {
            var axle = corners.ToList();
            Over(SpringSpec + end, axle.Select(c => c.Spring), "force", RunningGear.SpringRate);
            Over(ShockSpec + end, axle.Select(c => c.Shock), "damping", d => RunningGear.Damping(d).Bump);
        }

        if (swayBars is var (front, rear))
        {
            Over(SwayBarSpec + "front", [front], "arb_rate", RunningGear.SwayBarRate);
            Over(SwayBarSpec + "rear", [rear], "arb_rate", RunningGear.SwayBarRate);
        }

        return ratios;
    }
}
