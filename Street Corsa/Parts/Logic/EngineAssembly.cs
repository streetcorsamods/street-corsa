namespace StreetCorsa.Parts.Logic;

/// <summary>
/// An engine worked out from its parts' specs: the block goes over what is mounted on it (crankshaft, rods and
/// pistons, heads, camshaft, intake, fuel system, chargers, headers, gearbox) and collects what the dyno needs, what
/// the engine weighs on the crank (inertia), how hard it is to turn (friction) and what the gearbox hands the car.
/// Which slot of a part takes what is the part's <see cref="PartDefinition.SlotRoles"/>; what a part is, its class.
/// </summary>
/// <remarks>
/// The model, part by part (docs/systems/parts-system.md, "How an engine is worked out"):
/// <list type="bullet">
/// <item>Block: cylinders, bore, deck height, idle, rev limit, the spark curve.</item>
/// <item>Crankshaft, rods, pistons: stroke; with the deck height, rod length, pin height and dome, the chamber left
/// over the piston, and with the head's chamber the compression.</item>
/// <item>Each head (each bank of a vee, averaged): how well it breathes, from its valves' area times the cam's lift
/// over the cylinder's volume, times the flow of the intake manifold and of the header; the cam's valve events.</item>
/// <item>The intake and what hangs on it: the fuel systems (the mixture and the most fuel and air they pass, the air
/// through their air cleaner or their open horn), a supercharger's or a turbocharger's boost.</item>
/// <item>Every rotating part: the lowest top speed of them is the engine's; their inertia adds up; what they lose to
/// friction, over the oil pan's figure, is how hard the engine turns.</item>
/// <item>The gearbox: gears, reverse, final drive, which wheels it drives and how hard its differential locks.</item>
/// </list>
/// </remarks>
public sealed class EngineAssembly
{
    // Air through a valve's opening per cylinder volume as the dyno takes it
    private const double BreatherFactor = 0.9;

    // A cylinder head's valves pass a tenth less air when worn out
    private const double WornValveFlow = 0.9;

    private const double NoCylinder = 1e15;
    private const double DynoRpmStep = 250;
    private const double CycleDegrees = 720;
    private const double GramsPerKg = 1000;

    // What a part counts as without a figure of its own
    private const double DefaultIdleRpm = 750;
    private const double DefaultRevLimit = 6500;
    private const int DefaultCylinders = 4;
    private const double DefaultPinHeight = 25;
    private const double DefaultAdvanceStep = 0.1;

    // More steps than any cam gear has: the advance is taken as it is
    private const double MaxAdvanceSteps = 10_000;
    private const double DefaultClutchTorque = 1300;

    private readonly InstalledPart _block;
    private DynoSheet _sheet = new();
    private readonly Dictionary<InstalledPart, Mixture> _mixtures = new();
    private double _sliding = 1;

    private EngineAssembly(InstalledPart block)
    {
        _block = block;
    }

    public DynoInputs Inputs { get; private set; } = new();

    /// <summary>The fuel's lowest and highest static compression; 0 when nothing feeds the engine</summary>
    public double MinCompression => _sheet.MinCompression;
    public double MaxCompression => _sheet.MaxCompression;

    /// <summary>The engine's rotating parts, kg m2</summary>
    public double Inertia { get; private set; }

    /// <summary>The engine's own drag: the oil pan's figure over what the moving parts lose; 1 without an oil pan</summary>
    public double Friction { get; private set; } = 1;

    public double IdleRpm { get; private set; }
    public double RpmLimit { get; private set; }

    /// <summary>What the clutch holds, Nm; 0 without one</summary>
    public double ClutchTorque { get; private set; }

    public Gearbox? Transmission { get; private set; }

    /// <summary>The gearbox's figures as it hands them to the car: forward gears first gear first, 1 front, 2 rear, 3 all wheels</summary>
    public sealed record Gearbox(IReadOnlyList<double> Gears, double Reverse, double FinalDrive, int DriveType, double DiffLock);

    public static EngineAssembly Of(InstalledPart block)
    {
        var assembly = new EngineAssembly(block);
        assembly.Assemble();
        return assembly;
    }

    private void Assemble()
    {
        var block = _block;
        var sheet = _sheet;

        sheet.Bore = block.Spec(Specs.Bore) * 0.001;
        var advance = block.Spec(Specs.SparkAdvance);
        sheet.SparkMin = 0.5 - advance / CycleDegrees;
        sheet.SparkInc = (advance - block.Spec(Specs.SparkAdvanceHigh, advance)) / CycleDegrees;
        sheet.SparkRpm0 = block.Spec(Specs.SparkRpmLow);
        sheet.SparkRpm1 = block.Spec(Specs.SparkRpmHigh, 1);
        sheet.RpmLimit = block.Spec(Specs.RevLimit, DefaultRevLimit);
        sheet.Cylinders = (int)block.Spec(Specs.Cylinders, DefaultCylinders);
        IdleRpm = block.Spec(Specs.IdleRpm, DefaultIdleRpm);

        // The bottom end
        var crank = block.OnRole(Roles.Crankshaft, PartClasses.Crankshaft);
        var rod = crank?.OnRole(Roles.ConnectingRods, PartClasses.ConnectingRods);
        var piston = rod?.OnRole(Roles.Pistons, PartClasses.Pistons);
        double inertia = 0;
        if (crank != null)
        {
            _sliding *= Sliding(crank);
            inertia += InertiaOf(crank);
            Fill(crank, sheet);
            sheet.Stroke = crank.Spec(Specs.Stroke) * 0.001;
            if (rod != null)
            {
                _sliding *= Sliding(rod);
                inertia += InertiaOf(rod);
                if (piston != null)
                {
                    _sliding *= Sliding(piston);
                    inertia += InertiaOf(piston);
                }
            }
        }

        if (block.OnRole(Roles.CrankBearing) is { } crankBearing) _sliding *= Sliding(crankBearing);

        if (block.OnRole(Roles.Transmission, PartClasses.Transmission) is { } gearbox)
        {
            Fill(gearbox, sheet);
            Transmission = GearboxOf(gearbox);
        }

        // The top end: the intake on the block feeds every head; each head (each bank of a vee) is filled on its own
        var bore = block.Spec(Specs.Bore);
        var boreArea = bore * bore * 0.01 * 0.25 * Math.PI;
        var cylinderVolume = crank != null ? boreArea * crank.Spec(Specs.Stroke) * 0.1 : NoCylinder;
        var intake = block.OnRole(Roles.IntakeManifold, PartClasses.IntakeManifold);
        if (intake != null) Fill(intake, sheet);

        var camshaft = block.OnRole(Roles.Camshaft, PartClasses.Camshaft);
        var headSlots = HeadRoles.Select(block.RoleSlot).Where(s => s > 0).ToList();
        var banks = new List<DynoSheet>();
        foreach (var slot in headSlots)
        {
            var head = block.OnSlot(slot, PartClasses.CylinderHead);
            if (head == null) continue;

            // An overhead cam turns with its head: its inertia and what it loses go with the head's
            var headCamshaft = camshaft == null ? head.OnRole(Roles.Camshaft, PartClasses.Camshaft) : null;
            var bank = sheet.Clone();
            banks.Add(bank);
            Head(head, bank, camshaft ?? headCamshaft, intake, piston, rod, crank, boreArea, cylinderVolume);
            _sliding *= HeadSliding(head, camshaft);
            inertia += InertiaOf(head) * 2;
            if (headCamshaft != null)
            {
                inertia += 2 * InertiaOf(headCamshaft) * 2;
                _sliding *= Sliding(headCamshaft);
            }
        }

        // An engine with a head missing has no breathing at all (it does not run either); the banks of a vee average
        if (banks.Count == headSlots.Count && banks.Count > 0)
        {
            var engine = banks[0];
            for (var i = 1; i < banks.Count; i++)
            {
                var both = engine.Clone();
                both.BeAverageOf(engine, banks[i]);
                engine = both;
            }

            _sheet = sheet = engine;
        }
        else if (headSlots.Count == 0)
        {
            sheet.ClearanceVolume = NoCylinder;
        }

        if (camshaft != null)
        {
            LowerMaxRpm(camshaft, sheet);
            inertia += 2 * InertiaOf(camshaft) * 2;
            _sliding *= Sliding(camshaft);
        }

        if (block.OnRole(Roles.CamshaftBearing) is { } camBearing) _sliding *= Sliding(camBearing);

        var sliding = Math.Clamp(_sliding, 0.001, 1.0);
        Friction = block.OnRole(Roles.OilPan, PartClasses.OilPan) is { } oilPan ? oilPan.Spec(Specs.Friction, 1) / sliding : 1;
        Inertia = inertia;
        ClutchTorque = block.SelfAndDescendants().FirstOrDefault(p => p.Is(PartClasses.Clutch)) is { } clutch ? clutch.Spec(Specs.Torque, DefaultClutchTorque) : 0;

        // The dyno runs in steps of 250 rpm up to what the weakest part takes, or a quarter over the limiter
        RpmLimit = sheet.RpmLimit;
        if (sheet.MaxRpm < 100) sheet.MaxRpm = sheet.RpmLimit * 1.25;
        sheet.MaxRpm = (int)(sheet.MaxRpm / DynoRpmStep) * DynoRpmStep;
        Inputs = sheet.ToInputs();
    }

    private static readonly string[] HeadRoles = { Roles.LeftHead, Roles.RightHead, Roles.Head };

    /// <summary>One bank: its head's breathing through the intake and the exhaust, its chamber, its valve events</summary>
    private void Head(InstalledPart head, DynoSheet bank, InstalledPart? camshaft, InstalledPart? blockIntake, InstalledPart? piston,
        InstalledPart? rod, InstalledPart? crank, double boreArea, double cylinderVolume)
    {
        var wear = WornValveFlow + (1 - WornValveFlow) * head.Wear;
        var intakeArea = wear * ValveArea(head.Spec(Specs.IntakeValve), head.Spec(Specs.IntakeValves));
        var exhaustArea = wear * ValveArea(head.Spec(Specs.ExhaustValve), head.Spec(Specs.ExhaustValves));

        double inMin = 0, inMax = 0, outMin = 0, outMax = 0;
        var timing = new double[4];
        if (camshaft != null)
        {
            inMin = intakeArea * camshaft.Spec(Specs.IntakeLiftClosed) * 0.1;
            inMax = intakeArea * camshaft.Spec(Specs.IntakeLift) * 0.1;
            outMin = exhaustArea * camshaft.Spec(Specs.ExhaustLiftClosed) * 0.1;
            outMax = exhaustArea * camshaft.Spec(Specs.ExhaustLift) * 0.1;
            timing = ValveEvents(camshaft);
        }

        Fill(head, bank);

        if (piston != null && rod != null && crank != null)
        {
            var deck = (_block.Spec(Specs.DeckHeight) - crank.Spec(Specs.Stroke) * 0.5 - rod.Spec(Specs.RodLength) - piston.Spec(Specs.PinHeight, DefaultPinHeight)) * 0.1;
            bank.ClearanceVolume = (head.Spec(Specs.Chamber) - piston.Spec(Specs.Dome) + boreArea * deck) * 0.000001;
        }
        else bank.ClearanceVolume = NoCylinder;

        var headIntake = blockIntake == null ? head.OnRole(Roles.IntakeManifold, PartClasses.IntakeManifold) : null;
        var header = head.OnRole(Roles.ExhaustHeader, PartClasses.ExhaustHeader);
        var exhaustFlow = header != null ? header.Spec(Specs.Flow, 1) * head.Spec(Specs.ExhaustPort, 1) : 1;
        var intakeFlow = (blockIntake ?? headIntake) is { } manifold ? manifold.Spec(Specs.Flow, 1) * head.Spec(Specs.IntakePort, 1) : 0;
        if (blockIntake == null && headIntake == null)
        {
            bank.MixtureRatio = 1;
            bank.MaxFuelFlow = 0;
            bank.MaxAirFlow = 0;
            bank.MixtureHeat = 1;
            bank.BurnTime = 0.1;
        }

        if (cylinderVolume != 0 && intakeFlow != 0)
        {
            bank.IntakeMin = Math.Clamp(inMin * inMin / cylinderVolume / intakeFlow * BreatherFactor, 0, 1);
            bank.IntakeMax = Math.Clamp(inMax * inMax / cylinderVolume * intakeFlow * BreatherFactor, 0, 1);
            bank.ExhaustMin = Math.Clamp(outMin * outMin / cylinderVolume / exhaustFlow * BreatherFactor, 0, 1);
            bank.ExhaustMax = Math.Clamp(outMax * outMax / cylinderVolume * exhaustFlow * BreatherFactor, 0, 1);
        }
        else
        {
            bank.IntakeMin = bank.IntakeMax = bank.ExhaustMin = bank.ExhaustMax = 0;
        }

        (bank.IntakeOpen, bank.IntakeClose, bank.ExhaustOpen, bank.ExhaustClose) = (timing[0], timing[1], timing[2], timing[3]);
    }

    private static double ValveArea(double diameter, double count) => diameter * diameter * 0.25 * 0.01 * Math.PI * count;

    /// <summary>A camshaft's valve events, as fractions of the cycle: intake open, close, exhaust open, close</summary>
    private static double[] ValveEvents(InstalledPart cam)
    {
        var low = cam.Spec(Specs.AdvanceMin);
        var high = cam.Spec(Specs.AdvanceMax);
        var step = cam.Spec(Specs.AdvanceStep, DefaultAdvanceStep);
        var advance = cam.Spec(Specs.Advance);
        if (advance < low) advance = low;
        else if (advance > high) advance = high;
        else if (!(step > 0)) advance = low;
        else if ((high - low) / step <= MaxAdvanceSteps)
        {
            // The cam turns in steps on its gear, to the first step at or past the advance asked for; a gear of more
            // steps than any is one it turns on smoothly. The steps are counted as well as added: a step too small
            // for the figure to take in (1e6 plus 2e-13 is 1e6 again) would otherwise never get there
            var at = low;
            for (var steps = 0; at < high && at < advance && steps <= MaxAdvanceSteps; steps++) at += step;
            advance = at;
        }

        var separation = cam.Spec(Specs.LobeSeparation);
        var inDuration = cam.Spec(Specs.IntakeDuration);
        var outDuration = cam.Spec(Specs.ExhaustDuration);
        double inOpen = 0, inClose = 0, outOpen = 0, outClose = 0;
        if (inDuration > 0)
        {
            inOpen = advance + 360 - separation - inDuration / 2;
            inClose = inOpen + inDuration;
        }

        if (outDuration > 0)
        {
            outOpen = advance + 360 + separation - outDuration / 2;
            outClose = outOpen + outDuration;
        }

        return new[] { Roll(inOpen) / CycleDegrees, Roll(inClose) / CycleDegrees, Roll(outOpen) / CycleDegrees, Roll(outClose) / CycleDegrees };

        // Into the cycle; a full cycle stays one. A remainder, not a loop: a cam of absurd figures is still a cam
        static double Roll(double degrees)
        {
            if (degrees < 0)
            {
                degrees %= CycleDegrees;
                if (degrees < 0) degrees += CycleDegrees;
            }
            else if (degrees > CycleDegrees)
            {
                degrees %= CycleDegrees;
                if (degrees == 0) degrees = CycleDegrees;
            }

            return degrees;
        }
    }

    /// <summary>
    /// A part and what hangs on it hand the sheet their figures, the last slot first: the weakest rotating part sets
    /// the engine's top speed, a fuel system its mixture and the most it can deliver (the least of several), a charger
    /// its boost
    /// </summary>
    private void Fill(InstalledPart part, DynoSheet sheet)
    {
        if (part.Is(PartClasses.FuelSystem)) FuelSystem(part);
        if (part.Is(PartClasses.CylinderHead)) sheet.HeatLoss = part.Spec(Specs.HeatLoss);

        var slots = part.Definition.Slots;
        for (var i = slots.Count - 1; i >= 0; i--)
        {
            var id = slots[i].Id;
            if (id == part.OwnSlot && part.Parent != null) continue;
            if (part.Children.TryGetValue(id, out var child)) Fill(child, sheet);
        }

        LowerMaxRpm(part, sheet);

        if (_mixtures.TryGetValue(part, out var fuel))
        {
            sheet.MinCompression = fuel.MinCompression;
            sheet.MaxCompression = fuel.MaxCompression;
            sheet.MixtureRatio = fuel.Ratio;
            if (sheet.MaxFuelFlow <= 0 || sheet.MaxFuelFlow > fuel.FuelFlow) sheet.MaxFuelFlow = fuel.FuelFlow;
            if (sheet.MaxAirFlow <= 0 || sheet.MaxAirFlow > fuel.AirFlow) sheet.MaxAirFlow = fuel.AirFlow;
            sheet.MixtureHeat = fuel.Heat;
            sheet.BurnTime = fuel.BurnTime;
        }
        else if (part.Is(PartClasses.Supercharger))
        {
            // Spun faster than its pulleys were made for, a blower boosts more and comes in earlier
            var drive = part.Spec(Specs.DriveRatio, 1);
            var normal = part.Spec(Specs.NormalDriveRatio, drive);
            sheet.ChargerRpmMultiplier = drive;
            sheet.ChargerRpmOptimum = part.Spec(Specs.BoostRpm, 2) * drive * drive;
            sheet.ChargerRpmRange = part.Spec(Specs.BoostRpmRange, 1) * drive * drive;
            sheet.BoostMax = normal > 0 ? part.Spec(Specs.Boost) * drive / normal : part.Spec(Specs.Boost);
            sheet.BoostWastegate = part.Spec(Specs.Wastegate) is > 0 and var gate ? gate : sheet.BoostMax;
        }
        else if (part.Is(PartClasses.Turbocharger) && part.Definition.Specs.ContainsKey(Specs.Boost))
        {
            sheet.ChargerRpmMultiplier = 1;
            sheet.ChargerRpmOptimum = part.Spec(Specs.BoostRpm, 2);
            sheet.ChargerRpmRange = part.Spec(Specs.BoostRpmRange, 1);
            sheet.BoostMax = part.Spec(Specs.Boost);
            if (part.Spec(Specs.Wastegate) is > 0 and var gate) sheet.BoostWastegate = gate;
            else sheet.BoostMax = 0;
        }
    }

    /// <summary>A part's top speed is the engine's when it is the lowest yet; a part without one leaves it as it is</summary>
    private static void LowerMaxRpm(InstalledPart part, DynoSheet sheet)
    {
        if (part.Spec(Specs.MaxRpm, -1) is >= 0 and var maxRpm && (sheet.MaxRpm < 0 || maxRpm < sheet.MaxRpm)) sheet.MaxRpm = maxRpm;
    }

    private sealed record Mixture(double Ratio, double FuelFlow, double AirFlow, double Heat, double BurnTime, double MinCompression, double MaxCompression);

    /// <summary>
    /// A carburettor's or an injection's mixture at its ratio, the fuel it passes and the air it gets: through its air
    /// cleaner, else through its open horn. Off the fuel's best ratio either way the mixture burns cooler and slower.
    /// </summary>
    private void FuelSystem(InstalledPart part)
    {
        var air = part.Spec(Specs.AirFlow, 0.01) / GramsPerKg;
        if (AirCleanerOf(part) is { } cleaner) air = AirCleanerFlow(cleaner);

        var fuel = Fuels.Current.Get(part.Definition.Specs.GetValueOrDefault(Specs.Fuel) as string);
        var ratio = part.Spec(Specs.MixtureRatio, 1);
        var clamped = Math.Clamp(ratio, 8, 20);
        var off = clamped < fuel.BestRatio ? clamped / fuel.BestRatio : fuel.BestRatio / clamped;
        off = 0.45 * off + 0.55;
        off *= off;
        off = off * off * off;
        off = 1 - off;
        var heat = fuel.Heat * 1e6 * (0.8 + 0.2 * (1 - off)) * 1.55;
        var burn = (fuel.BurnTime + fuel.BurnTimeOff * off) * 0.001 * 0.25;
        _mixtures[part] = new Mixture(ratio, part.Spec(Specs.FuelFlow, 0.001) / GramsPerKg, air, heat, burn, heat / 250_000, heat / 100_000);
    }

    /// <summary>The air cleaner a carburettor breathes through: on its own horn, else the one over its row on the manifold</summary>
    private static InstalledPart? AirCleanerOf(InstalledPart carb)
    {
        foreach (var slot in carb.Definition.Slots)
        {
            if (slot.Id == carb.OwnSlot && carb.Parent != null) continue;
            if (carb.Children.TryGetValue(slot.Id, out var child) && child.Is(PartClasses.AirCleaner)) return child;
        }

        if (carb.Parent != null && carb.ParentSlot != PartSlot.SharedAirSlot
            && carb.Parent.Children.TryGetValue(PartSlot.SharedAirSlot, out var shared) && shared.Is(PartClasses.AirCleaner))
            return shared;

        return null;
    }

    /// <summary>
    /// What an air cleaner passes, kg/s: the dirt it lets clog it takes its share as it wears. Clogging 0 never clogs, 1
    /// passes nothing worn out (<see cref="Clogging"/>).
    /// </summary>
    private static double AirCleanerFlow(InstalledPart cleaner)
    {
        var clean = cleaner.Spec(Specs.AirFlow, 0.01) / GramsPerKg;
        var clogging = Clogging(cleaner.Definition);
        return clean * cleaner.Wear * clogging + clean * (1 - clogging) + 0.00001;
    }

    /// <summary>How much of its flow an air cleaner loses to dirt as it wears, 0 (none) to 1 (all of it)</summary>
    public static double Clogging(PartDefinition cleaner) => cleaner.Number(Specs.Clogging) is var clogging && double.IsFinite(clogging) ? Math.Clamp(clogging, 0, 1) : 0;

    /// <summary>What a part keeps of the engine's turning, 0..1: its own share, with what turns with it as one</summary>
    private static double Sliding(InstalledPart part) =>
        (1 - part.Spec(Specs.FrictionLoss)) * part.Children.Values.Where(c => SlidesWith(part, c)).Aggregate(1.0, (product, c) => product * Sliding(c));

    /// <summary>What turns with a part as one: a crankshaft's flywheel and rods, a rod's pistons</summary>
    private static bool SlidesWith(InstalledPart part, InstalledPart child) =>
        (part.Is(PartClasses.Crankshaft) && (child.ParentSlot == part.RoleSlot(Roles.Flywheel) || child.ParentSlot == part.RoleSlot(Roles.ConnectingRods)))
        || (part.Is(PartClasses.ConnectingRods) && child.ParentSlot == part.RoleSlot(Roles.Pistons));

    private static double HeadSliding(InstalledPart head, InstalledPart? blockCamshaft)
    {
        var sliding = 1 - head.Spec(Specs.FrictionLoss);
        if (head.OnRole(Roles.CamshaftBearing) is { } bearing) sliding *= Sliding(bearing);
        if (blockCamshaft != null && head.RoleSlot(Roles.EngineBlock) > 0) sliding *= Sliding(blockCamshaft);
        return sliding;
    }

    /// <summary>A rotating part's inertia with what turns with it: a crankshaft's flywheel and rods, a rod's pistons, a flywheel's clutch</summary>
    private static double InertiaOf(InstalledPart part)
    {
        var own = part.Spec(Specs.Inertia);
        if (part.Is(PartClasses.Crankshaft))
        {
            if (part.OnRole(Roles.Flywheel, PartClasses.Flywheel) is { } flywheel) own += InertiaOf(flywheel);
            if (part.OnRole(Roles.ConnectingRods, PartClasses.ConnectingRods) is { } rod) own += InertiaOf(rod);
        }
        else if (part.Is(PartClasses.ConnectingRods))
        {
            if (part.OnRole(Roles.Pistons, PartClasses.Pistons) is { } piston) own += InertiaOf(piston);
        }
        else if (part.Is(PartClasses.Flywheel))
        {
            if (part.OnRole(Roles.Clutch, PartClasses.Clutch) is { } clutch) own += InertiaOf(clutch);
        }

        return own;
    }

    private static Gearbox GearboxOf(InstalledPart gearbox)
    {
        var count = gearbox.Definition.Specs.GetValueOrDefault(Specs.Gears) is System.Collections.ICollection list ? list.Count : 0;
        var gears = Enumerable.Range(0, count).Select(i => gearbox.SpecAt(Specs.Gears, i)).ToList();
        var drive = (gearbox.Definition.Specs.GetValueOrDefault(Specs.Drive) as string) switch
        {
            "front" => 1,
            "all" => 3,
            _ => 2
        };
        return new Gearbox(gears, gearbox.Spec(Specs.Reverse), gearbox.Spec(Specs.FinalDrive), drive, gearbox.Spec(Specs.DiffLock));
    }

    /// <summary>What the parts call their figures (docs/modding.md, "Specs")</summary>
    public static class Specs
    {
        public const string Bore = "bore";
        public const string Cylinders = "cylinders";
        public const string DeckHeight = "deck_height";
        public const string IdleRpm = "idle_rpm";
        public const string RevLimit = "rev_limit";
        public const string SparkAdvance = "spark_advance";
        public const string SparkAdvanceHigh = "spark_advance_high";
        public const string SparkRpmLow = "spark_rpm_low";
        public const string SparkRpmHigh = "spark_rpm_high";
        public const string Stroke = "stroke";
        public const string RodLength = "rod_length";
        public const string PinHeight = "pin_height";
        public const string Dome = "dome";
        public const string MaxRpm = "max_rpm";
        public const string Inertia = "inertia";
        public const string FrictionLoss = "friction_loss";
        public const string Friction = "friction";
        public const string Capacity = "capacity";
        public const string HeatLoss = "heat_loss";
        public const string Chamber = "chamber";
        public const string IntakeValve = "intake_valve";
        public const string IntakeValves = "intake_valves";
        public const string ExhaustValve = "exhaust_valve";
        public const string ExhaustValves = "exhaust_valves";
        public const string IntakePort = "intake_port";
        public const string ExhaustPort = "exhaust_port";
        public const string Flow = "flow";
        public const string LobeSeparation = "lobe_separation";
        public const string IntakeLift = "intake_lift";
        public const string IntakeLiftClosed = "intake_lift_closed";
        public const string ExhaustLift = "exhaust_lift";
        public const string ExhaustLiftClosed = "exhaust_lift_closed";
        public const string IntakeDuration = "intake_duration";
        public const string ExhaustDuration = "exhaust_duration";
        public const string Advance = "advance";
        public const string AdvanceMin = "advance_min";
        public const string AdvanceMax = "advance_max";
        public const string AdvanceStep = "advance_step";
        public const string MixtureRatio = "mixture_ratio";
        public const string Fuel = "fuel";
        public const string FuelFlow = "fuel_flow";
        public const string AirFlow = "air_flow";
        public const string Clogging = "clogging";
        public const string DriveRatio = "drive_ratio";
        public const string NormalDriveRatio = "normal_drive_ratio";
        public const string BoostRpm = "boost_rpm";
        public const string BoostRpmRange = "boost_rpm_range";
        public const string Boost = "boost";
        public const string Wastegate = "wastegate";
        public const string Gears = "gears";
        public const string Reverse = "reverse";
        public const string FinalDrive = "final_drive";
        public const string Drive = "drive";
        public const string DiffLock = "diff_lock";
        public const string Torque = "torque";
    }

    /// <summary>Which slot of a part takes what: the roles of <see cref="PartDefinition.SlotRoles"/></summary>
    public static class Roles
    {
        public const string Crankshaft = "crankshaft";
        public const string CrankBearing = "crank_bearing";
        public const string Transmission = "transmission";
        public const string OilPan = "oil_pan";
        public const string IntakeManifold = "intake_manifold";
        public const string LeftHead = "left_head";
        public const string RightHead = "right_head";
        public const string Head = "head";
        public const string Camshaft = "camshaft";
        public const string CamshaftBearing = "camshaft_bearing";
        public const string ConnectingRods = "connecting_rods";
        public const string Pistons = "pistons";
        public const string Flywheel = "flywheel";
        public const string Clutch = "clutch";
        public const string ExhaustHeader = "exhaust_header";
        public const string EngineBlock = "engine_block";
    }
}
