using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Parts.Export;

/// <summary>
/// The running gear of a car as Assetto Corsa physics data. The car's own figures are the factory parts',
/// whatever the units either side thinks in: what is mounted moves each figure by its ratio to the factory
/// part (a brake with twice the torque doubles the car's brake torque, a tyre with a fifth more grip lifts
/// the car's grip by a fifth). A car on its factory parts comes out byte for byte as its author left it.
/// The rear springs' wrap (a leaf spring's, less with a traction bar on it) moves <c>[AXLE] TORQUE_REACTION</c>, its sign
/// kept; the sway bars move <c>[ARB]</c>: by their ratio to the factory bar, the bar's own rate on an axle the factory gave
/// none, nothing left of it with the bar taken off. A spring that lowers the car (its <c>drop</c>, a lowering block's on
/// it) shortens the axle's <c>ROD_LENGTH</c> by its corners' mean over the factory spring's (<see cref="CarStance.Drop"/>);
/// the car's whole height is held to the axle's travel once the setup and the damage are on it too (<see cref="HeldToTravel"/>).
/// </summary>
public static class AcRunningGearData
{
    private const string TyresFile = "tyres.ini";
    private const string BrakesFile = "brakes.ini";
    private const string SuspensionsFile = "suspensions.ini";
    private const string CarFile = "car.ini";

    /// <summary>The ratios of one axle: what is mounted over what came from the factory</summary>
    private sealed class AxleChange
    {
        public double TyreWidth = 1, TyreRadius = 1, RimRadius = 1, Grip = 1, LoadCapacity = 1, RollingResistance = 1, Pressure = 1;
        public double BrakeTorque = 1, SpringRate = 1, Bump = 1, Rebound = 1, Wrap = 1;
        public double TrackDelta, HubMassDelta, MassDelta;

        /// <summary>How far the axle's springs lower it from the factory's, mm (negative: lift)</summary>
        public double Drop;

        /// <summary>The sway bar's rate as mounted and the factory's (0 for none); <see cref="SwayBars"/> false leaves [ARB] as it is</summary>
        public double SwayBar, FactorySwayBar;
        public bool SwayBars;

        /// <summary>How the axle's brakes heat and fade as mounted, and as the factory's; whether they are the factory's</summary>
        public BrakeHeat? Heat, FactoryHeat;
        public bool FactoryBrakes = true;
    }

    /// <param name="mounted">What is on the car's wheel slots, by corner</param>
    /// <param name="factory">The parts the car's data describes</param>
    /// <param name="problems">What keeps the car from being driven: a corner without a wheel, brake, spring or shock, a
    /// spring on a suspension it does not fit (a leaf spring under a car whose rear is not a live axle)</param>
    /// <param name="swayBars">The sway bars on the car, front and rear; null leaves the car's [ARB] as its data has it</param>
    /// <returns>File name to new content, for the files that change</returns>
    public static Dictionary<string, string> Generate(PartsCatalog catalog, CornerParts[] mounted, (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear) factory,
        Func<string, string?> readFile, List<string> problems, (PartInstance? Front, PartInstance? Rear)? swayBars = null)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var suspensionsText = readFile(SuspensionsFile);
        var types = new IniText(suspensionsText);
        var front = Change(catalog, mounted.Take(2).ToList(), factory.Front, 0, types.Get("FRONT", "TYPE")?.Trim(), problems);
        var rear = Change(catalog, mounted.Skip(2).Take(2).ToList(), factory.Rear, 2, types.Get("REAR", "TYPE")?.Trim(), problems);

        if (swayBars is var (frontBar, rearBar))
        {
            foreach (var (change, bar, factoryBar) in new[] { (front, frontBar, factory.Front.SwayBar), (rear, rearBar, factory.Rear.SwayBar) })
            {
                change.SwayBars = true;
                change.SwayBar = RunningGear.Definition(catalog, bar) is { } definition ? Positive(RunningGear.SwayBarRate(definition)) : 0;
                change.FactorySwayBar = factoryBar == null ? 0 : Positive(RunningGear.SwayBarRate(factoryBar));
            }
        }

        // A car on its factory parts changes nothing: only files with an edit are handed back
        void Keep(string name, IniText ini) { if (ini.Changed) files[name] = ini.ToString(); }

        if (readFile(TyresFile) is { } tyres) Keep(TyresFile, Tyres(new IniText(tyres), front, rear));
        if (readFile(BrakesFile) is { } brakes) Keep(BrakesFile, Brakes(new IniText(brakes), front, rear));
        if (suspensionsText != null) Keep(SuspensionsFile, Suspensions(new IniText(suspensionsText), front, rear));
        if (readFile(CarFile) is { } car)
        {
            var ini = new IniText(car);
            AcCarIni.AddMass(ini, front.MassDelta + rear.MassDelta);
            Keep(CarFile, ini);
        }

        return files;
    }

    /// <param name="suspension">The axle's suspensions.ini TYPE, for the springs that need one; null when the data does not say</param>
    private static AxleChange Change(PartsCatalog catalog, List<CornerParts> corners, RunningGearFactory.AxleParts factory, int firstCorner, string? suspension,
        List<string> problems)
    {
        var change = new AxleChange { FactoryHeat = RunningGear.Heat(factory.Brake, factory.Shoes) };
        var factoryBrake = factory.BrakeTorque;
        var factoryGrip = RunningGear.TyreGrip(factory.Tyre);
        var (factoryBump, factoryRebound) = RunningGear.Damping(factory.Shock);
        var factoryMass = AcCarIni.MassOf(factory.Rim) + AcCarIni.MassOf(factory.Tyre) + AcCarIni.MassOf(factory.Brake) + AcCarIni.MassOf(factory.Spring) + AcCarIni.MassOf(factory.Shock);

        double tyreWidth = 0, tyreRadius = 0, rimRadius = 0, grip = 0, load = 0, rolling = 0, pressure = 0;
        double brake = 0, spring = 0, bump = 0, rebound = 0, wrap = 0, track = 0, hubMass = 0, mass = 0;

        for (var i = 0; i < corners.Count; i++)
        {
            var corner = corners[i];
            var name = RunningGear.CornerIds[firstCorner + i];

            var rim = RunningGear.Definition(catalog, corner.Rim);
            var tyre = RunningGear.Definition(catalog, corner.Tyre);
            var brakeDef = RunningGear.Definition(catalog, corner.Brake);
            var springDef = RunningGear.Definition(catalog, corner.Spring);
            var shockDef = RunningGear.Definition(catalog, corner.Shock);

            if (rim == null) problems.Add($"no wheel {name}");
            else if (tyre == null) problems.Add($"no tyre on the {name} wheel");
            if (brakeDef == null) problems.Add($"no brake {name}");
            else
            {
                // What the brake cannot do without (a drum's shoes, wheel cylinder and hub, on its backing plate or itself), in
                // its pack's words
                Required(catalog, corner.BackingPlate, name, "the brake is missing a part", problems);
                Required(catalog, corner.Brake, name, "the brake is missing a part", problems);

                change.Heat ??= RunningGear.Heat(catalog, corner);
                var shoes = corner.Shoes?.DefinitionId;
                if (brakeDef.Id != factory.Brake.Id || !string.Equals(shoes, factory.Shoes?.Id, StringComparison.OrdinalIgnoreCase)) change.FactoryBrakes = false;
            }
            if (springDef == null) problems.Add($"no spring {name}");
            else if (suspension != null && RunningGear.NeedsSuspension(springDef) is { } needs && !string.Equals(needs, suspension, StringComparison.OrdinalIgnoreCase))
                problems.Add($"the {Lower(springDef.DisplayName ?? springDef.Name) ?? "spring"} does not fit this car's suspension ({name})");
            if (shockDef == null) problems.Add($"no shock absorber {name}");

            // A corner that lacks a part is measured as the factory part, the problem says what is missing
            tyreWidth += Ratio(tyre == null ? 0 : RunningGear.TyreWidth(tyre), RunningGear.TyreWidth(factory.Tyre));
            tyreRadius += Ratio(tyre == null ? 0 : RunningGear.TyreRadius(tyre), RunningGear.TyreRadius(factory.Tyre));
            rimRadius += Ratio(tyre == null ? 0 : RunningGear.TyreRimRadius(tyre), RunningGear.TyreRimRadius(factory.Tyre));
            grip += Ratio(tyre == null ? 0 : RunningGear.TyreGrip(tyre, corner.Tyre!.Wear), factoryGrip);
            load += Ratio(tyre == null ? 0 : RunningGear.TyreLoadCapacity(tyre), RunningGear.TyreLoadCapacity(factory.Tyre));
            rolling += Ratio(tyre == null ? 0 : RunningGear.TyreRollingResistance(tyre), RunningGear.TyreRollingResistance(factory.Tyre));
            pressure += Ratio(tyre == null ? 0 : RunningGear.TyrePressure(tyre), RunningGear.TyrePressure(factory.Tyre));
            brake += Ratio(brakeDef == null ? 0 : RunningGear.BrakeTorque(catalog, corner), factoryBrake);
            spring += Ratio(springDef == null ? 0 : RunningGear.SpringRate(springDef), RunningGear.SpringRate(factory.Spring));
            wrap += Ratio(springDef == null ? 0 : RunningGear.Wrap(catalog, corner.Spring!), RunningGear.Wrap(factory.Spring));
            bump += Ratio(shockDef == null ? 0 : RunningGear.Damping(shockDef).Bump, factoryBump);
            rebound += Ratio(shockDef == null ? 0 : RunningGear.Damping(shockDef).Rebound, factoryRebound);

            // Offset is where the rim puts the wheel; negative moves it out, and the track grows by twice that. A pack's
            // absurd offset or weight is no figure: the part counts as the factory one (AcCarIni.Bounded), as the ratios do
            var factoryOffset = AcCarIni.Bounded(RunningGear.RimOffset(factory.Rim), AcCarIni.MaxRimOffset);
            if (rim != null) track += -2 * (AcCarIni.Bounded(RunningGear.RimOffset(rim), AcCarIni.MaxRimOffset, factoryOffset) - factoryOffset);

            var (factoryRim, factoryTyre, factoryBrakeMass) = (AcCarIni.MassOf(factory.Rim), AcCarIni.MassOf(factory.Tyre), AcCarIni.MassOf(factory.Brake));
            var unsprung = AcCarIni.MassOf(rim, factoryRim) + AcCarIni.MassOf(tyre, factoryTyre) + AcCarIni.MassOf(brakeDef, factoryBrakeMass);
            hubMass += unsprung - (factoryRim + factoryTyre + factoryBrakeMass);
            mass += unsprung + AcCarIni.MassOf(springDef, AcCarIni.MassOf(factory.Spring)) + AcCarIni.MassOf(shockDef, AcCarIni.MassOf(factory.Shock)) - factoryMass;
        }

        var n = Math.Max(1, corners.Count);
        change.TyreWidth = tyreWidth / n;
        change.TyreRadius = tyreRadius / n;
        change.RimRadius = rimRadius / n;
        change.Grip = grip / n;
        change.LoadCapacity = load / n;
        change.RollingResistance = rolling / n;
        change.Pressure = pressure / n;
        change.BrakeTorque = brake / n;
        change.SpringRate = spring / n;
        change.Wrap = wrap / n;
        change.Bump = bump / n;
        change.Rebound = rebound / n;
        change.TrackDelta = track / n;
        change.Drop = CarStance.Drop(catalog, corners, factory.Spring);
        change.HubMassDelta = hubMass / n;
        change.MassDelta = mass;
        return change;
    }

    /// <summary>What a part cannot do without (its pack's <c>needs</c>), in its pack's words</summary>
    private static void Required(PartsCatalog catalog, PartInstance? part, string corner, string otherwise, List<string> problems)
    {
        if (RunningGear.Definition(catalog, part) is not { } definition) return;
        foreach (var required in definition.Needs.Where(r => part!.Children.All(c => c.ParentSlot != r.Slot)))
            problems.Add($"{Lower(required.Message) ?? otherwise} ({corner})");
    }

    /// <summary>
    /// The car's suspensions.ini with each axle's <c>ROD_LENGTH</c> held to its travel (<see cref="CarStance.Held"/>) from
    /// the data's own: what the parts, the setup and the damage moved it by together. Null when every axle already holds.
    /// </summary>
    /// <param name="own">The car's own suspensions.ini, as its author left it</param>
    /// <param name="changed">The suspensions.ini the car is to race on</param>
    public static string? HeldToTravel(string own, string changed)
    {
        var data = new IniText(own);
        var ini = new IniText(changed);
        foreach (var axle in new[] { "FRONT", "REAR" })
        {
            if (data.GetNumber(axle, "ROD_LENGTH") is not { } from || ini.GetNumber(axle, "ROD_LENGTH") is not { } to) continue;
            var moved = (to - from) / Unit.MetresPerMm;
            var held = CarStance.Held(moved, CarStance.Travel(data, axle));
            if (Math.Abs(held - moved) > 1e-6) ini.Set(axle, "ROD_LENGTH", from + held * Unit.MetresPerMm, "0.0000");
        }

        return ini.Changed ? ini.ToString() : null;
    }

    private static double Positive(double value) => value > 0 && double.IsFinite(value) ? value : 0;

    /// <summary>A part's message as part of a sentence: lower case first, no full stop; null for none</summary>
    internal static string? Lower(string? message)
    {
        var text = message?.Trim().TrimEnd('.');
        return string.IsNullOrEmpty(text) ? null : char.ToLowerInvariant(text[0]) + text[1..];
    }

    /// <summary>Mounted over factory; a part without a figure, or none at all, counts as the factory part</summary>
    // Figures from part files may be anything Newtonsoft reads, "Infinity" and 1e400 among them: no figure is no change
    internal static double Ratio(double mounted, double factory) =>
        mounted > 0 && factory > 0 && double.IsFinite(mounted / factory) ? mounted / factory : 1;

    private static IniText Tyres(IniText ini, AxleChange front, AxleChange rear)
    {
        // Every compound the car offers
        foreach (var (section, occurrence, isFront) in AcCarIni.TyreCompounds(ini))
        {
            var change = isFront ? front : rear;
            ini.Scale(section, "WIDTH", change.TyreWidth, "0.000", occurrence);
            ini.Scale(section, "RADIUS", change.TyreRadius, "0.0000", occurrence);
            ini.Scale(section, "RIM_RADIUS", change.RimRadius, "0.0000", occurrence);
            ini.Scale(section, "ANGULAR_INERTIA", change.TyreRadius * change.TyreRadius, "0.00", occurrence);
            foreach (var key in new[] { "DX_REF", "DY_REF", "DX0", "DY0" }) ini.Scale(section, key, change.Grip, "0.0000", occurrence);
            ini.Scale(section, "FZ0", change.LoadCapacity, "0", occurrence);
            ini.Scale(section, "ROLLING_RESISTANCE_0", change.RollingResistance, "0.00", occurrence);
            ini.Scale(section, "ROLLING_RESISTANCE_1", change.RollingResistance, "0.000000", occurrence);
            ini.Scale(section, "PRESSURE_STATIC", change.Pressure, "0", occurrence);
            ini.Scale(section, "PRESSURE_IDEAL", change.Pressure, "0", occurrence);
        }

        return ini;
    }

    private static IniText Brakes(IniText ini, AxleChange front, AxleChange rear)
    {
        // How each axle's brakes heat and fade: on the factory's, the car's author's if they wrote it, else the factory
        // brakes' (the one change a stock car gets: drums that fade); on others, theirs, or the car's own for a brake that
        // says none (the stock discs)
        foreach (var (section, change) in new[] { ("TEMPS_FRONT", front), ("TEMPS_REAR", rear) })
        {
            var heat = change.FactoryBrakes ? ini.Sections.Contains(section) ? null : change.FactoryHeat : change.Heat;
            if (heat == null) continue;
            ini.Set(section, "TORQUE_K", heat.TorqueK, "0.000");
            ini.Set(section, "COOL_TRANSFER", heat.CoolTransfer, "0.0000");
            ini.Set(section, "COOL_SPEED_FACTOR", heat.CoolSpeedFactor, "0.0000");
            ini.Set(section, "PERF_CURVE", heat.Curve);
        }

        if (Math.Abs(front.BrakeTorque - 1) < 1e-9 && Math.Abs(rear.BrakeTorque - 1) < 1e-9) return ini;

        var max = ini.GetNumber("DATA", "MAX_TORQUE") ?? 0;
        var share = ini.GetNumber("DATA", "FRONT_SHARE") ?? 0.6;
        var frontTorque = max * share * front.BrakeTorque;
        var rearTorque = max * (1 - share) * rear.BrakeTorque;

        ini.Set("DATA", "MAX_TORQUE", frontTorque + rearTorque, "0");
        if (frontTorque + rearTorque > 0) ini.Set("DATA", "FRONT_SHARE", frontTorque / (frontTorque + rearTorque), "0.000");
        ini.Scale("DATA", "HANDBRAKE_TORQUE", rear.BrakeTorque, "0");
        return ini;
    }

    private static IniText Suspensions(IniText ini, AxleChange front, AxleChange rear)
    {
        foreach (var (section, change) in new[] { ("FRONT", front), ("REAR", rear) })
        {
            var (rateSection, rateKey) = AcCarIni.SpringRateKey(ini, section);
            ini.Scale(rateSection, rateKey, change.SpringRate, "0");
            ini.Scale(section, "DAMP_BUMP", change.Bump, "0");
            ini.Scale(section, "DAMP_FAST_BUMP", change.Bump, "0");
            ini.Scale(section, "DAMP_REBOUND", change.Rebound, "0");
            ini.Scale(section, "DAMP_FAST_REBOUND", change.Rebound, "0");
            if (ini.GetNumber(section, "TRACK") is { } track && Math.Abs(change.TrackDelta) > 0.0005) ini.Set(section, "TRACK", track + change.TrackDelta, "0.000");
            if (ini.GetNumber(section, "HUB_MASS") is { } hub && Math.Abs(change.HubMassDelta) > 0.05) ini.Set(section, "HUB_MASS", Math.Max(hub * 0.5, hub + change.HubMassDelta), "0.0");
            if (Math.Abs(change.Drop) >= 0.5 && ini.GetNumber(section, "ROD_LENGTH") is { } rod)
                ini.Set(section, "ROD_LENGTH", rod - change.Drop * Unit.MetresPerMm, "0.0000");

            // The sway bar: by its ratio to the factory's, its own rate where the factory gave none, none with it taken off
            if (!change.SwayBars) continue;
            var own = Math.Max(0, ini.GetNumber("ARB", section) ?? 0);
            double? arb = (change.SwayBar, change.FactorySwayBar) switch
            {
                ( > 0, > 0) when Math.Abs(change.SwayBar - change.FactorySwayBar) < 1e-9 => null,
                ( > 0, > 0) => own * change.SwayBar / change.FactorySwayBar,
                ( > 0, _) => own + change.SwayBar,
                (_, > 0) => 0,
                _ => null
            };
            if (arb is { } rate) ini.Set("ARB", section, rate, "0");
        }

        // A live axle winds its springs up under power: the leaves' wrap against the factory's, its sign as the author wrote it
        if (Math.Abs(rear.Wrap - 1) > 1e-9 && ini.GetNumber("AXLE", "TORQUE_REACTION") is { } reaction)
            ini.Set("AXLE", "TORQUE_REACTION", reaction * rear.Wrap, "0.000");

        return ini;
    }
}
