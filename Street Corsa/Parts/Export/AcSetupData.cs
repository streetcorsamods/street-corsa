using StreetCorsa.Helpers;
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

    /// <summary>
    /// How an item's range is set: around the car-wide item, by the parts' adjustable spec, held to the mounted carrier
    /// (the diff is the part's, no setup moves it), or only widened
    /// </summary>
    private enum Kind
    {
        CarItem,
        PartSpec,
        Carrier,
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

    /// <summary>An axle's car-wide items</summary>
    private static (string Camber, string Toe, string RideHeight, string Pressure) AxleItems(bool front) => front
        ? (CarSetup.CamberFront, CarSetup.ToeFront, CarSetup.RideHeightFront, CarSetup.PressureFront)
        : (CarSetup.CamberRear, CarSetup.ToeRear, CarSetup.RideHeightRear, CarSetup.PressureRear);

    private static readonly (string Axle, bool Front)[] Axles = [("FRONT", true), ("REAR", false)];

    private static IEnumerable<Item> Items(IniText suspensions)
    {
        foreach (var (axle, front) in Axles)
        {
            var end = front ? "front" : "rear";
            var (camber, toe, rideHeight, pressure) = AxleItems(front);
            var (rateSection, rateKey) = AcCarIni.SpringRateKey(suspensions, axle);
            foreach (var corner in front ? new[] { "LF", "RF" } : ["LR", "RR"])
            {
                yield return new Item($"PRESSURE_{corner}", TyresFile, axle, "PRESSURE_STATIC", 1, Kind.CarItem, pressure, PsiPerBar);
                yield return new Item($"CAMBER_{corner}", SuspensionsFile, axle, "STATIC_CAMBER", 1, Kind.CarItem, camber, StepScale: 0.1);
                yield return new Item($"TOE_OUT_{corner}", SuspensionsFile, axle, "TOE_OUT", 100_000, Kind.CarItem, toe, 100_000 * Unit.MetresPerMm);
                yield return new Item($"ROD_LENGTH_{corner}", SuspensionsFile, axle, "ROD_LENGTH", 10_000, Kind.CarItem, rideHeight, 10_000 * Unit.MetresPerMm);
                yield return new Item($"SPRING_RATE_{corner}", SuspensionsFile, rateSection, rateKey, 0.001, Kind.PartSpec, SpringSpec + end);
                foreach (var key in new[] { "DAMP_BUMP", "DAMP_FAST_BUMP" })
                    yield return new Item($"{key}_{corner}", SuspensionsFile, axle, key, 1, Kind.PartSpec, BumpSpec + end);
                foreach (var key in new[] { "DAMP_REBOUND", "DAMP_FAST_REBOUND" })
                    yield return new Item($"{key}_{corner}", SuspensionsFile, axle, key, 1, Kind.PartSpec, ReboundSpec + end);
            }

            yield return new Item($"ARB_{axle}", SuspensionsFile, "ARB", axle, 1, Kind.PartSpec, SwayBarSpec + end);
        }

        yield return new Item("FRONT_BIAS", BrakesFile, "DATA", "FRONT_SHARE", 100, Kind.CarItem, CarSetup.BrakeBias, 100);
        yield return new Item("DIFF_POWER", DrivetrainFile, "DIFFERENTIAL", "POWER", 100, Kind.Carrier);
        yield return new Item("DIFF_COAST", DrivetrainFile, "DIFFERENTIAL", "COAST", 100, Kind.Carrier);
        yield return new Item("DIFF_PRELOAD", DrivetrainFile, "DIFFERENTIAL", "PRELOAD", 1, Kind.Carrier);
    }

    // The part figures an axle's setup items are moved by, keyed with the axle ("spring:front"): a spring's rate, a
    // shock's bump and rebound (both off its damping), a sway bar's
    private const string SpringSpec = "spring:";
    private const string BumpSpec = "bump:";
    private const string ReboundSpec = "rebound:";
    private const string SwayBarSpec = "arb:";

    // How far the axle's springs and what is on them may move its height (a coilover's adjustable drop), mm of ride height
    private const string DropSpec = "drop:";

    /// <summary>The camber and toe of each live axle the car has: AC reads none for one, so there is nothing to set</summary>
    private static HashSet<string> NoPlace(IniText? suspensions) =>
        suspensions == null
            ? []
            : Axles.Where(a => AcCarIni.IsLiveAxle(suspensions, a.Axle)).SelectMany(a => AxleItems(a.Front) is var items ? new[] { items.Camber, items.Toe } : [])
                .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A setup as this car's data can take it, for the export and for the Tune screen alike: no camber or toe on a live
    /// axle, the brake bias held to a front share of nothing to all of it from the share the parts make, and the steering
    /// lock to what <see cref="MostSteerGain"/> lets it do to the car's own
    /// </summary>
    /// <param name="readFile">The car's data as the parts left it, before any setup</param>
    public static SetupChoice ForData(SetupChoice setup, Func<string, string?> readFile)
    {
        var gone = NoPlace(readFile(SuspensionsFile) is { } text ? new IniText(text) : null);
        var allowed = setup.Allowed.Where(a => !gone.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);

        if (allowed.TryGetValue(CarSetup.BrakeBias, out var bias) && readFile(BrakesFile) is { } brakes &&
            new IniText(brakes).GetNumber("DATA", "FRONT_SHARE") is { } share && double.IsFinite(share))
        {
            var (min, max) = (Math.Max(bias.Min, -share), Math.Min(bias.Max, 1 - share));
            allowed[CarSetup.BrakeBias] = min <= max ? bias with { Min = min, Max = max } : SetupRange.Fixed;
        }

        if (allowed.TryGetValue(CarSetup.SteerLock, out var steer) && readFile(CarFile) is { } car &&
            new IniText(car).GetNumber("CONTROLS", "STEER_LOCK") is > 0 and var steerLock && double.IsFinite(steerLock))
        {
            var most = MostSteerGain();
            var (min, max) = (Math.Max(steer.Min, steerLock * (1 / most - 1)), Math.Min(steer.Max, steerLock * (most - 1)));
            allowed[CarSetup.SteerLock] = min <= max ? steer with { Min = min, Max = max } : SetupRange.Fixed;
        }

        if (gone.Count == 0 && allowed.All(a => setup.Allowed[a.Key] == a.Value)) return setup;
        var values = setup.Values.Where(v => allowed.ContainsKey(v.Key)).ToDictionary(v => v.Key, v => allowed[v.Key].Snap(v.Value), StringComparer.Ordinal);
        return new SetupChoice { Values = values, Allowed = allowed };
    }

    /// <summary>The car-wide items' values into the data: file name to new content, for the files that change</summary>
    /// <param name="setup">The setup as the data takes it (<see cref="ForData"/>)</param>
    public static Dictionary<string, string> Apply(SetupChoice setup, Func<string, string?> readFile)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        double Value(string item) => setup.Values.TryGetValue(item, out var value) && double.IsFinite(value) ? value : 0;
        void Keep(string name, IniText ini) { if (ini.Changed) files[name] = ini.ToString(); }

        if (readFile(SuspensionsFile) is { } suspensionsText)
        {
            var ini = new IniText(suspensionsText);
            var noPlace = NoPlace(ini);
            foreach (var (axle, front) in Axles)
            {
                var (camber, toe, rideHeight, _) = AxleItems(front);
                if (!noPlace.Contains(camber)) Add(ini, axle, "STATIC_CAMBER", Value(camber), "0.00", absentIsZero: true);
                if (!noPlace.Contains(toe)) Add(ini, axle, "TOE_OUT", Value(toe) * Unit.MetresPerMm, "0.000000", absentIsZero: true);
                Add(ini, axle, "ROD_LENGTH", Value(rideHeight) * Unit.MetresPerMm, "0.0000");
            }

            Keep(SuspensionsFile, ini);
        }

        if (readFile(TyresFile) is { } tyresText)
        {
            // Every compound the car offers, as the running gear goes over them
            var ini = new IniText(tyresText);
            foreach (var (section, occurrence, front) in AcCarIni.TyreCompounds(ini))
            {
                if (Value(AxleItems(front).Pressure) is not 0 and var bar && ini.GetNumber(section, "PRESSURE_STATIC", occurrence) is { } psi)
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
            if (SteerRatio(ini, lockBy) is { } ratio) ini.Set("CONTROLS", "STEER_RATIO", ratio, "0.00");
            Keep(CarFile, ini);
        }

        return files;
    }

    /// <summary>
    /// The share of the road wheels' lock a setup may add or take away, the rules' <c>tuning.mostSteerGain</c>
    /// (<see cref="TuningLimits"/>); 1 holds it to the car's own, and a figure under 1 is none, the core's
    /// </summary>
    private static double MostSteerGain() =>
        TuningLimits.Now.MostSteerGain is >= 1 and var given && double.IsFinite(given) ? given : TuningLimits.Core.MostSteerGain;

    /// <summary>
    /// car.ini's <c>STEER_RATIO</c> for the steering lock moved by this many degrees of the steering wheel: AC turns the
    /// road wheels by the rack's travel, the steering wheel's degrees over the ratio, so a lock of 450 + 150 at a ratio of
    /// 14 is a ratio of 10.5 at the car's own 450 (the Kunos AE86 Drift's: 35 degrees at the wheels go to 49, seen with the
    /// setup probe 2026-10-28), and a wheel of 900 degrees reaches it. A bigger <c>STEER_LOCK</c> instead put the Nova's
    /// geometry past its centre. Held to <see cref="MostSteerGain"/> either way; the sign kept (Kunos writes it
    /// negative). Null for a car that gives no lock or ratio.
    /// </summary>
    public static double? SteerRatio(IniText car, double lockBy)
    {
        if (car.GetNumber("CONTROLS", "STEER_LOCK") is not (> 0 and var steerLock) || !double.IsFinite(steerLock)) return null;
        if (car.GetNumber("CONTROLS", "STEER_RATIO") is not { } ratio || ratio == 0 || !double.IsFinite(ratio)) return null;

        var most = MostSteerGain();
        var gain = Math.Clamp(Math.Max(1, steerLock + lockBy) / steerLock, 1 / most, most);
        return ratio / gain;
    }

    /// <summary>The tyre pressures of every compound the car offers on each axle, the one it starts on first</summary>
    private static Dictionary<string, List<double>> Pressures(IniText tyres)
    {
        var first = (int)Math.Max(0, tyres.GetNumber("COMPOUND_DEFAULT", "INDEX") ?? 0);
        var pressures = Axles.ToDictionary(a => a.Axle, _ => new List<double>(), StringComparer.Ordinal);
        foreach (var (section, occurrence, front) in AcCarIni.TyreCompounds(tyres))
        {
            if (tyres.GetNumber(section, "PRESSURE_STATIC", occurrence) is { } psi) pressures[front ? "FRONT" : "REAR"].Add(psi);
        }

        foreach (var compounds in pressures.Values)
        {
            if (first > 0 && first < compounds.Count) (compounds[0], compounds[first]) = (compounds[first], compounds[0]);
        }

        return pressures;
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
    /// <param name="setup">The setup as the data takes it (<see cref="ForData"/>)</param>
    /// <param name="parts">The car's build, for what is on the wheel slots and the rear axle; null leaves the
    /// suspension's and the diff's items only widened</param>
    /// <param name="readFile">The car's data as it races, before the ride height is held to the travel (<see cref="AcRunningGearData.HeldToTravel"/>)</param>
    /// <param name="readOwn">The car's own data as its author left it, for how far its axles travel: the ride height's range
    /// goes no further than <see cref="CarStance.Held"/> holds it; null holds it to nothing</param>
    public static Dictionary<string, string> Range(SetupChoice setup, PartsCatalog catalog, CarBuild? parts, Func<string, string?> readFile,
        Func<string, string?>? readOwn = null)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (readFile(SetupFile) is not { } setupText) return files;

        var ini = new IniText(setupText);
        var sections = ini.Sections.ToHashSet(StringComparer.Ordinal);
        var data = new Dictionary<string, IniText?>(StringComparer.OrdinalIgnoreCase);
        IniText? Data(string name) => data.TryGetValue(name, out var read) ? read : data[name] = readFile(name) is { } text ? new IniText(text) : null;

        var authors = readOwn?.Invoke(SuspensionsFile) is { } ownText ? new IniText(ownText) : null;
        var reaches = PartReaches(catalog, parts);
        var noPlace = NoPlace(Data(SuspensionsFile));
        var pressures = Data(TyresFile) is { } tyres ? Pressures(tyres) : null;
        var carrier = parts is { RearAxle.Carrier: { } mounted, FactoryAxle: not null } && catalog.Get(mounted.DefinitionId) != null;
        foreach (var item in Items(Data(SuspensionsFile) ?? new IniText(null)))
        {
            if (!sections.Contains(item.Name) || Data(item.File)?.GetNumber(item.Section, item.Key) is not { } figure) continue;

            // Each tyre compound has its pressure, and AC holds whichever the car races on to the one range: it takes them all
            var others = item.File == TyresFile && pressures?.GetValueOrDefault(item.Section) is { Count: > 0 } compounds ? compounds : [figure];
            var value = others[0] * item.Scale;
            var spread = (Low: others.Min() * item.Scale - value, High: others.Max() * item.Scale - value);
            var (min, max) = (ini.GetNumber(item.Name, "MIN"), ini.GetNumber(item.Name, "MAX"));

            var kind = item.Kind switch
            {
                Kind.CarItem when noPlace.Contains(item.Setting!) => Kind.Widen,
                Kind.PartSpec when !reaches.ContainsKey(item.Setting!) => Kind.Widen,
                Kind.Carrier when !carrier => Kind.Widen,
                var own => own
            };
            double low, high;
            switch (kind)
            {
                case Kind.CarItem:
                {
                    var at = setup.Values.GetValueOrDefault(item.Setting!);
                    var range = setup.Allowed.GetValueOrDefault(item.Setting!) ?? SetupRange.Fixed;
                    (low, high) = (value + (Math.Min(range.Min, at) - at) * item.ItemScale, value + (Math.Max(range.Max, at) - at) * item.ItemScale);

                    // The ride height moves by the springs' own adjustable drop too
                    if (item.Key == "ROD_LENGTH" && reaches.TryGetValue(DropSpec + (item.Section == "FRONT" ? "front" : "rear"), out var drop))
                        (low, high) = (low + drop.Lo * item.ItemScale, high + drop.Hi * item.ItemScale);

                    // No further than the axle's travel allows from the author's own, as the race holds it
                    if (item.Key == "ROD_LENGTH" && authors?.GetNumber(item.Section, item.Key) is { } ownRod && CarStance.Most(CarStance.Travel(authors, item.Section)) is { } most)
                    {
                        var (lowest, highest) = ((ownRod - most * Unit.MetresPerMm) * item.Scale, (ownRod + most * Unit.MetresPerMm) * item.Scale);
                        (low, high) = (Math.Clamp(low, lowest, highest), Math.Clamp(high, lowest, highest));
                    }
                    break;
                }
                case Kind.PartSpec:
                    (low, high) = reaches[item.Setting!].Around(value, item.Scale);
                    break;
                case Kind.Carrier:
                    (low, high) = (value, value);
                    break;
                default:
                    (low, high) = (Math.Min(min ?? value, value), Math.Max(max ?? value, value));
                    break;
            }

            if (item.Setting == CarSetup.BrakeBias) (low, high) = (Math.Max(0, low), Math.Min(100, high));

            // On the grid of the item's steps from the figure, inside the range: AC's screen steps from MIN. The other
            // compounds' pressures widen it by whole steps, so it stays on the grid of the one the car starts on
            var step = kind != Kind.Widen && ini.GetNumber(item.Name, "STEP") is > 0 and var s ? s * item.StepScale : 0;
            if (step > 0)
            {
                low = value - Math.Floor((value - low) / step + 1e-6) * step + Math.Floor(Math.Min(0, spread.Low) / step + 1e-6) * step;
                high = value + Math.Floor((high - value) / step + 1e-6) * step + Math.Ceiling(Math.Max(0, spread.High) / step - 1e-6) * step;
            }
            else (low, high) = (low + Math.Min(0, spread.Low), high + Math.Max(0, spread.High));
            if (low > high) low = high;

            if (min is { } oldMin && Math.Abs(oldMin - low) < 1e-9 && max is { } oldMax && Math.Abs(oldMax - high) < 1e-9) continue;
            ini.Set(item.Name, "MIN", Figure(low));
            ini.Set(item.Name, "MAX", Figure(high));
        }

        if (ini.Changed) files[SetupFile] = ini.ToString();
        return files;
    }

    private static string Figure(double value) => Math.Round(value, 6).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// How far a figure of the data may move, from where it is now: by a ratio (<see cref="Adds"/> false: the figure times
    /// <see cref="Lo"/> to times <see cref="Hi"/>) or by an amount of the data's units added
    /// </summary>
    private readonly record struct Reach(double Lo, double Hi, bool Adds = false)
    {
        public static readonly Reach None = new(1, 1);

        /// <summary>The setup item's range around its figure now, in setup units</summary>
        public (double Low, double High) Around(double value, double scale)
        {
            var (a, b) = Adds ? (value + Lo * scale, value + Hi * scale) : (value * Lo, value * Hi);
            return double.IsFinite(a) && double.IsFinite(b) ? (Math.Min(a, b), Math.Max(a, b)) : (value, value);
        }
    }

    /// <summary>
    /// How far the axles' springs, shocks and sway bars let their figures in the data move: each part's <c>adjust</c> on
    /// its own spec, at its ends worked through the same mapping the running gear writes the data by (an axle's figure is
    /// the author's times the average of its corners' parts over the factory's; a sway bar's its ratio to the factory
    /// bar, or the bar's own rate added where the factory gave none). An axle whose parts adjust nothing stays where it
    /// is; none at all without the running gear and the factory's.
    /// </summary>
    private static Dictionary<string, Reach> PartReaches(PartsCatalog catalog, CarBuild? parts)
    {
        var reaches = new Dictionary<string, Reach>(StringComparer.Ordinal);
        if (parts is not { RunningGear: { Length: RunningGear.Corners } runningGear, FactoryRunningGear: var (frontFactory, rearFactory) }) return reaches;

        // The part's figure as tuned now, and at the ends of its spec's adjust (the figure now for one that adjusts nothing)
        (double Now, double Lo, double Hi) Ends(PartInstance? part, string spec, Func<PartDefinition, double> figure)
        {
            if (part == null || catalog.Get(part.DefinitionId) is not { } definition) return (0, 0, 0);
            var now = figure(definition.WithTuning(part.Tuning));
            if (!CarSetup.AdjustsOf(definition).TryGetValue(spec, out var range)) return (now, now, now);

            double At(double end) => figure(definition.WithTuning(new Dictionary<string, double>(part.Tuning ?? []) { [spec] = end }));
            return (now, At(range.Min), At(range.Max));
        }

        foreach (var (end, corners, factory) in new[] { ("front", runningGear.Take(2), frontFactory), ("rear", runningGear.Skip(2), rearFactory) })
        {
            var axle = corners.ToList();
            void Over(string key, Func<CornerParts, PartInstance?> part, PartDefinition factoryPart, string spec, Func<PartDefinition, double> figure)
            {
                var factoryFigure = figure(factoryPart);
                var (now, lo, hi) = (0.0, 0.0, 0.0);
                foreach (var corner in axle)
                {
                    var ends = Ends(part(corner), spec, figure);
                    now += AcRunningGearData.Ratio(ends.Now, factoryFigure);
                    lo += AcRunningGearData.Ratio(ends.Lo, factoryFigure);
                    hi += AcRunningGearData.Ratio(ends.Hi, factoryFigure);
                }

                reaches[key] = now > 0 ? new Reach(lo / now, hi / now) : Reach.None;
            }

            Over(SpringSpec + end, c => c.Spring, factory.Spring, "force", RunningGear.SpringRate);
            Over(BumpSpec + end, c => c.Shock, factory.Shock, "damping", d => RunningGear.Damping(d).Bump);
            Over(ReboundSpec + end, c => c.Shock, factory.Shock, "damping", d => RunningGear.Damping(d).Rebound);

            // A drop that adjusts, on a spring or on what is on it: the axle's mean, as the running gear lowers it (more drop is less height)
            var (dropLo, dropHi) = (0.0, 0.0);
            foreach (var spring in axle.Select(c => c.Spring).OfType<PartInstance>())
            {
                foreach (var part in spring.SelfAndDescendants())
                {
                    var (now, lo, hi) = Ends(part, "drop", RunningGear.Drop);
                    (dropLo, dropHi) = (dropLo + Math.Min(lo, hi) - now, dropHi + Math.Max(lo, hi) - now);
                }
            }

            var n = Math.Max(1, axle.Count);
            if (dropLo != 0 || dropHi != 0) reaches[DropSpec + end] = new Reach(-dropHi / n, -dropLo / n, Adds: true);

            if (parts.SwayBars is var (frontBar, rearBar))
            {
                var (bar, factoryBar) = (end == "front" ? frontBar : rearBar, factory.SwayBar);
                var (now, lo, hi) = Ends(bar, "arb_rate", RunningGear.SwayBarRate);
                var factoryRate = factoryBar == null ? 0 : RunningGear.SwayBarRate(factoryBar);
                reaches[SwayBarSpec + end] = (now > 0, factoryRate > 0) switch
                {
                    (true, true) => new Reach(lo / now, hi / now),
                    (true, false) => new Reach(lo - now, hi - now, Adds: true),
                    _ => Reach.None
                };
            }
        }

        return reaches;
    }
}
