namespace StreetCorsa.Parts.Logic;

/// <summary>
/// An engine worked out from its parts: the block goes over what is mounted on it (crankshaft, rods and pistons,
/// heads, camshaft, intake, fuel system, chargers, headers, gearbox) and collects what the dyno needs, what the
/// engine weighs on the crank (inertia), how hard it is to turn (friction) and what the gearbox hands the car.
/// </summary>
public sealed class EngineAssembly
{
    // Air through a valve's opening per cylinder volume as the dyno takes it
    private const double BreatherFactor = 0.9;

    // A cylinder head's valves pass a tenth less air when worn out
    private const double WornValveFlow = 0.9;

    private const double NoCylinder = 1e15;
    private const double DynoRpmStep = 250;

    private readonly InstalledPart _block;
    private readonly DynoSheet _sheet = new();
    private double _sliding = 1;

    private EngineAssembly(InstalledPart block)
    {
        _block = block;
    }

    public DynoInputs Inputs { get; private set; } = new();

    /// <summary>The fuel's lowest and highest static compression; 0 when nothing feeds the engine</summary>
    public double MinCompression => _sheet.MinCompression;
    public double MaxCompression => _sheet.MaxCompression;

    /// <summary>The engine's rotating parts, in the parts' inertia units (<see cref="PartFigures.KgM2PerInertiaUnit"/>)</summary>
    public double Inertia { get; private set; }

    /// <summary>The engine's own drag: the oil pan's figure over what the moving parts lose; 1 without an oil pan</summary>
    public double Friction { get; private set; } = 1;

    public double IdleRpm { get; private set; }
    public double RpmLimit { get; private set; }

    public Gearbox? Transmission { get; private set; }

    /// <summary>The gearbox's figures as it hands them to the car</summary>
    public sealed record Gearbox(int Gears, IReadOnlyList<double> Ratios, double FinalDrive, int DriveType, double DiffLock);

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

        sheet.Bore = block.Spec(Keys.Bore) * 0.001;
        sheet.SparkMin = block.Spec(Keys.SparkMin, 0.5);
        sheet.SparkInc = block.Spec(Keys.SparkInc);
        sheet.SparkRpm0 = block.Spec(Keys.SparkRpm0);
        sheet.SparkRpm1 = block.Spec(Keys.SparkRpm1, 1);
        sheet.RpmLimit = block.Spec(Keys.RpmLimit, 6500);
        sheet.Cylinders = (int)block.Spec(Keys.Cylinders, 4);
        IdleRpm = block.Spec(Keys.IdleRpm, 750);

        // The bottom end
        var crank = block.OnRole(Roles.Crankshaft, Classes.Crankshaft);
        var rod = crank?.OnRole(Roles.ConnectingRod, Classes.ConnectingRod);
        var piston = rod?.OnRole(Roles.Piston, Classes.Piston);
        double inertia = 0;
        if (crank != null)
        {
            _sliding *= Sliding(crank);
            inertia += InertiaOf(crank);
            Fill(crank, sheet);
            sheet.Stroke = crank.Spec(Keys.Stroke) * 0.001;
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

        if (block.OnRole(Roles.CrankshaftBearing, Classes.Sliding) is { } crankBearing) _sliding *= Sliding(crankBearing);

        if (block.OnRole(Roles.Transmission, Classes.Transmission) is { } gearbox)
        {
            Fill(gearbox, sheet);
            Transmission = GearboxOf(gearbox);
        }

        // The top end: the intake on the block feeds every head; each head (each bank of a vee) is filled on its own
        var boreArea = block.Spec(Keys.Bore) * block.Spec(Keys.Bore) * 0.01 * 0.25 * 3.1416;
        var cylinderVolume = crank != null ? boreArea * crank.Spec(Keys.Stroke) * 0.1 : NoCylinder;
        var intake = block.OnRole(Roles.IntakeManifold, Classes.IntakeManifold);
        if (intake != null) Fill(intake, sheet);

        var camshaft = block.OnRole(Roles.Camshaft, Classes.Camshaft);
        var headSlots = HeadRoles.Select(block.RoleSlot).Where(s => s > 0).ToList();
        var banks = new List<DynoSheet>();
        foreach (var slot in headSlots)
        {
            var head = block.OnSlot(slot, Classes.CylinderHead);
            if (head == null) continue;

            var bank = sheet.Clone();
            banks.Add(bank);
            Head(head, bank, camshaft ?? head.OnRole(Roles.Camshaft, Classes.Camshaft), intake, piston, rod, crank, boreArea, cylinderVolume);
            _sliding *= HeadSliding(head, camshaft);
            inertia += InertiaOf(head) * 2;
        }

        // A vee with a head missing has no breathing at all (it does not run either)
        if (banks.Count == headSlots.Count && banks.Count > 0)
        {
            if (banks.Count == 1) Copy(banks[0], sheet);
            else
            {
                var average = banks[0];
                for (var i = 1; i < banks.Count; i++)
                {
                    var next = banks[0].Clone();
                    next.BeAverageOf(average, banks[i]);
                    average = next;
                }

                sheet.BeAverageOf(banks[0], banks.Count == 2 ? banks[1] : average);
                if (banks.Count > 2) Copy(average, sheet);
            }
        }
        else if (headSlots.Count == 0)
        {
            sheet.ClearanceVolume = NoCylinder;
        }

        if (camshaft != null)
        {
            if (camshaft.Spec(Keys.MaxRpm, -1) is var camRpm && camRpm < sheet.MaxRpm) sheet.MaxRpm = camRpm;
            inertia += 2 * InertiaOf(camshaft) * 2;
            _sliding *= Sliding(camshaft);
        }

        if (block.OnRole(Roles.CamshaftBearing, Classes.Sliding) is { } camBearing) _sliding *= Sliding(camBearing);

        var sliding = Math.Clamp(_sliding, 0.001, 1.0);
        Friction = block.OnRole(Roles.OilPan, Classes.OilPan) is { } oilPan ? oilPan.Spec(Keys.OilPanFriction, 1) / sliding : 1;
        Inertia = inertia;

        // The dyno runs in steps of 250 rpm up to what the weakest part takes, or a quarter over the limiter
        RpmLimit = sheet.RpmLimit;
        if (sheet.MaxRpm < 100) sheet.MaxRpm = sheet.RpmLimit * 1.25;
        sheet.MaxRpm = Math.Floor((int)(sheet.MaxRpm / DynoRpmStep) * DynoRpmStep);
        Inputs = sheet.ToInputs();
    }

    private static readonly string[] HeadRoles = { Roles.LeftHead, Roles.RightHead, Roles.Head };

    /// <summary>One bank: its head's breathing through the intake and the exhaust, its chamber, its valve events</summary>
    private void Head(InstalledPart head, DynoSheet bank, InstalledPart? camshaft, InstalledPart? blockIntake, InstalledPart? piston,
        InstalledPart? rod, InstalledPart? crank, double boreArea, double cylinderVolume)
    {
        var wear = WornValveFlow + (1 - WornValveFlow) * head.Wear;
        var intakeArea = wear * ValveArea(head.Spec(Keys.IntakeValve), head.Spec(Keys.IntakeValves));
        var exhaustArea = wear * ValveArea(head.Spec(Keys.ExhaustValve), head.Spec(Keys.ExhaustValves));

        double inMin = 0, inMax = 0, outMin = 0, outMax = 0;
        var timing = new double[4];
        if (camshaft != null)
        {
            var cam = Cam(camshaft);
            inMin = intakeArea * camshaft.Spec(Keys.LiftInClose) * 0.1;
            inMax = intakeArea * camshaft.Spec(Keys.LiftInOpen) * 0.1;
            outMin = exhaustArea * camshaft.Spec(Keys.LiftOutClose) * 0.1;
            outMax = exhaustArea * camshaft.Spec(Keys.LiftOutOpen) * 0.1;
            timing = cam;
        }

        bank.HeatLoss = head.Spec(Keys.HeatLoss);
        Fill(head, bank);

        if (piston != null && rod != null && crank != null)
        {
            var deck = (_block.Spec(Keys.DeckHeight) - crank.Spec(Keys.Stroke) * 0.5 - rod.Spec(Keys.RodLength) - piston.Spec(Keys.PinHeight, 25)) * 0.1;
            bank.ClearanceVolume = (head.Spec(Keys.Chamber) - piston.Spec(Keys.Dome) + boreArea * deck) * 0.000001;
        }
        else bank.ClearanceVolume = NoCylinder;

        var headIntake = blockIntake == null ? head.OnRole(Roles.IntakeManifold, Classes.IntakeManifold) : null;
        var header = head.OnRole(Roles.ExhaustHeader, Classes.ExhaustHeader);
        var exhaustEfficiency = header != null ? header.Spec(Keys.Efficiency, 1) * head.Spec(Keys.ExhaustTuning, 1) : 1;
        var intakeEfficiency = (blockIntake ?? headIntake) is { } manifold ? manifold.Spec(Keys.Efficiency, 1) * head.Spec(Keys.IntakeTuning, 1) : 0;
        if (blockIntake == null && headIntake == null)
        {
            bank.MixtureRatio = 1;
            bank.MaxFuelFlow = 0;
            bank.MaxAirFlow = 0;
            bank.MixtureHeat = 1;
            bank.BurnTime = 0.1;
        }

        if (cylinderVolume != 0 && intakeEfficiency != 0)
        {
            bank.IntakeMin = Math.Clamp(inMin * inMin / cylinderVolume / intakeEfficiency * BreatherFactor, 0, 1);
            bank.IntakeMax = Math.Clamp(inMax * inMax / cylinderVolume * intakeEfficiency * BreatherFactor, 0, 1);
            bank.ExhaustMin = Math.Clamp(outMin * outMin / cylinderVolume / exhaustEfficiency * BreatherFactor, 0, 1);
            bank.ExhaustMax = Math.Clamp(outMax * outMax / cylinderVolume * exhaustEfficiency * BreatherFactor, 0, 1);
        }
        else
        {
            bank.IntakeMin = bank.IntakeMax = bank.ExhaustMin = bank.ExhaustMax = 0;
        }

        (bank.IntakeOpen, bank.IntakeClose, bank.ExhaustOpen, bank.ExhaustClose) = (timing[0], timing[1], timing[2], timing[3]);
    }

    private static double ValveArea(double diameter, double count) => diameter * diameter * 0.25 * 0.01 * 3.1416 * count;

    /// <summary>A camshaft's valve events, as fractions of the cycle: intake open, close, exhaust open, close</summary>
    private static double[] Cam(InstalledPart cam)
    {
        var low = cam.Spec(Keys.AdvanceMin);
        var high = cam.Spec(Keys.AdvanceMax);
        var step = cam.Spec(Keys.AdvanceStep, 0.1);
        var advance = cam.Spec(Keys.Advance);
        if (advance < low) advance = low;
        else if (advance > high) advance = high;
        else
        {
            // The cam turns in steps on its gear
            var at = low;
            while (step > 0 && at < high && at < advance) at += step;
            advance = at;
        }

        var separation = cam.Spec(Keys.LobeSeparation);
        var inDuration = cam.Spec(Keys.IntakeDuration);
        var outDuration = cam.Spec(Keys.ExhaustDuration);
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

        return new[] { Roll(inOpen) / 720, Roll(inClose) / 720, Roll(outOpen) / 720, Roll(outClose) / 720 };

        static double Roll(double degrees)
        {
            while (degrees < 0) degrees += 720;
            while (degrees > 720) degrees -= 720;
            return degrees;
        }
    }

    /// <summary>
    /// A part and what hangs on it hand the sheet their figures: the weakest rotating part sets the engine's top speed,
    /// a fuel system its mixture and the most it can deliver, a charger its boost
    /// </summary>
    private void Fill(InstalledPart part, DynoSheet sheet)
    {
        if (part.Is(Classes.FuelSystem)) FuelSystem(part);
        else if (part.Is(Classes.Charger) || part.Is(Classes.Nitrous)) Mixture(part, part.Spec(Keys.MixtureRatio, 1));

        if (part.Is(Classes.CylinderHead)) sheet.HeatLoss = part.Spec(Keys.HeatLoss);

        // What hangs on it, the last slot first
        var slots = part.Definition.Slots;
        for (var i = slots.Count - 1; i >= 0; i--)
        {
            var id = slots[i].Id;
            if (id == part.OwnSlot && part.Parent != null) continue;
            if (part.Children.TryGetValue(id, out var child)) Fill(child, sheet);
        }

        if (part.Is(Classes.Reciprocating) && part.Spec(Keys.MaxRpm, -1) is >= 0 and var maxRpm && (sheet.MaxRpm < 0 || maxRpm < sheet.MaxRpm))
            sheet.MaxRpm = maxRpm;

        if (part.Is(Classes.FuelSystem) && _fuel.TryGetValue(part, out var fuel))
        {
            sheet.MinCompression = fuel.MinCompression;
            sheet.MaxCompression = fuel.MaxCompression;
            sheet.MixtureRatio = fuel.Ratio;
            if (sheet.MaxFuelFlow <= 0 || sheet.MaxFuelFlow > fuel.FuelFlow) sheet.MaxFuelFlow = fuel.FuelFlow;
            if (sheet.MaxAirFlow <= 0 || sheet.MaxAirFlow > fuel.AirFlow) sheet.MaxAirFlow = fuel.AirFlow;
            sheet.MixtureHeat = fuel.Heat;
            sheet.BurnTime = fuel.BurnTime;
        }
        else if (part.Is(Classes.Supercharger))
        {
            var drive = part.Spec(Keys.ChargerRpmMultiplier, 1);
            var normal = part.Spec(Keys.DefaultChargerRpmMultiplier, 1);
            sheet.ChargerRpmMultiplier = drive;
            sheet.ChargerRpmOptimum = part.Spec(Keys.ChargerRpmOptimum, 2) * drive * drive;
            sheet.ChargerRpmRange = part.Spec(Keys.ChargerRpmRange, 1) * drive * drive;
            sheet.BoostMax = normal > 0 ? part.Spec(Keys.BoostMax) * drive / normal : part.Spec(Keys.BoostMax);
            sheet.BoostWastegate = part.Spec(Keys.BoostWastegate) is > 0 and var gate ? gate : sheet.BoostMax;
        }
        else if (part.Is(Classes.Turbocharger))
        {
            sheet.ChargerRpmMultiplier = 1;
            sheet.ChargerRpmOptimum = part.Spec(Keys.ChargerRpmOptimum, 2);
            sheet.ChargerRpmRange = part.Spec(Keys.ChargerRpmRange, 1);
            sheet.BoostMax = part.Spec(Keys.BoostMax);
            if (part.Spec(Keys.BoostWastegate) is > 0 and var gate) sheet.BoostWastegate = gate;
            else sheet.BoostMax = 0;
        }
    }

    private readonly Dictionary<InstalledPart, Fuel> _fuel = new();

    private sealed record Fuel(double Ratio, double FuelFlow, double AirFlow, double Heat, double BurnTime, double MinCompression, double MaxCompression);

    /// <summary>A carburettor's or an injection's mixture, and the air it gets: through its air cleaner, else its open horn</summary>
    private void FuelSystem(InstalledPart part)
    {
        var air = part.Spec(Keys.OpenAirFlow, part.Spec(Keys.AirFlow));
        if (AirCleanerOf(part) is { } cleaner) air = AirFilterFlow(cleaner);

        var mixture = Mixture(part, part.Spec(Keys.MixtureRatio, 1));
        _fuel[part] = mixture with { FuelFlow = part.Spec(Keys.FuelFlow, 0.000001), AirFlow = air };
    }

    /// <summary>The air cleaner a carburettor breathes through: on its own horn, else the one over its row on the manifold</summary>
    private static InstalledPart? AirCleanerOf(InstalledPart carb)
    {
        foreach (var slot in carb.Definition.Slots)
        {
            if (slot.Id == carb.OwnSlot && carb.Parent != null) continue;
            if (carb.Children.TryGetValue(slot.Id, out var child) && child.Is(Classes.AirFilter)) return child;
        }

        if (carb.Parent != null && carb.ParentSlot != PartSlot.SharedAirSlot
            && carb.Parent.Children.TryGetValue(PartSlot.SharedAirSlot, out var shared) && shared.Is(Classes.AirFilter))
            return shared;

        return null;
    }

    private static double AirFilterFlow(InstalledPart filter)
    {
        var stock = filter.Spec(Keys.FilterAirFlow, 0.00001);
        var dust = filter.Spec(Keys.DustEffect);
        return stock * filter.Wear * dust + stock * (1 - dust) + 0.00001;
    }

    /// <summary>
    /// What a mixture of the fuel burns to at its ratio: heat per kg, burn time and the compression it takes. Off its
    /// best ratio either way it burns cooler and slower.
    /// </summary>
    private static Fuel Mixture(InstalledPart part, double ratio)
    {
        var fuel = FuelType.Of((int)part.Spec(Keys.FuelType));
        var clamped = Math.Clamp(ratio, 8, 20);
        var off = clamped < fuel.BestRatio ? clamped / fuel.BestRatio : fuel.BestRatio / clamped;
        off = 0.45 * off + 0.55;
        off *= off;
        off = off * off * off;
        off = 1 - off;
        var heat = fuel.Heat * (0.8 + 0.2 * (1 - off)) * 1.55;
        var burn = (fuel.BurnBase + fuel.BurnOffset * off) * 0.25;
        return new Fuel(ratio, 0, 0, heat, burn, heat / 250_000, heat / 100_000);
    }

    private static double Sliding(InstalledPart part) => part.Is(Classes.Transmission)
        ? Math.Clamp(part.Spec(Keys.WorstSliding, 1) + part.Wear * (part.Spec(Keys.BestSliding, 1) - part.Spec(Keys.WorstSliding, 1)), 0, 1)
        : part.Spec(Keys.Sliding, 1) * part.Children.Values.Where(c => SlidesWith(part, c)).Aggregate(1.0, (product, c) => product * Sliding(c));

    /// <summary>What turns with a part as one: a crankshaft's flywheel and rods, a rod's piston</summary>
    private static bool SlidesWith(InstalledPart part, InstalledPart child) =>
        (part.Is(Classes.Crankshaft) && (child.ParentSlot == part.RoleSlot(Roles.Flywheel) || child.ParentSlot == part.RoleSlot(Roles.ConnectingRod)))
        || (part.Is(Classes.ConnectingRod) && child.ParentSlot == part.RoleSlot(Roles.Piston));

    private double HeadSliding(InstalledPart head, InstalledPart? blockCamshaft)
    {
        var sliding = head.Spec(Keys.Sliding, 1);
        if (head.OnRole(Roles.CamshaftBearing, Classes.Sliding) is { } bearing) sliding *= Sliding(bearing);
        if (blockCamshaft != null && head.RoleSlot(Roles.EngineBlock) > 0) sliding *= Sliding(blockCamshaft);
        return sliding;
    }

    private static double InertiaOf(InstalledPart part)
    {
        var own = part.Spec(Keys.Inertia);
        if (part.Is(Classes.Crankshaft))
        {
            if (part.OnRole(Roles.Flywheel, Classes.Flywheel) is { } flywheel) own += InertiaOf(flywheel);
            if (part.OnRole(Roles.ConnectingRod, Classes.ConnectingRod) is { } rod) own += InertiaOf(rod);
        }
        else if (part.Is(Classes.ConnectingRod))
        {
            if (part.OnRole(Roles.Piston, Classes.Piston) is { } piston) own += InertiaOf(piston);
        }
        else if (part.Is(Classes.Flywheel))
        {
            if (part.OnRole(Roles.Clutch, Classes.Clutch) is { } clutch) own += InertiaOf(clutch);
        }

        return part.Is(Classes.CylinderHead) || part.Is(Classes.Camshaft) ? own : own;
    }

    private static Gearbox GearboxOf(InstalledPart gearbox)
    {
        var ratios = Enumerable.Range(0, 8).Select(i => gearbox.SpecAt(Keys.Ratio, i)).ToList();
        return new Gearbox((int)gearbox.Spec(Keys.Gears), ratios, gearbox.Spec(Keys.FinalDrive), (int)gearbox.Spec(Keys.DriveType), gearbox.Spec(Keys.DiffLock));
    }

    private static void Copy(DynoSheet from, DynoSheet to)
    {
        var copy = from.Clone();
        foreach (var field in typeof(DynoSheet).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)) field.SetValue(to, field.GetValue(copy));
    }

    /// <summary>The fuels a fuel system can run on, by the number its specs give</summary>
    private sealed record FuelType(double Heat, double BestRatio, double BurnBase, double BurnOffset)
    {
        private static readonly FuelType[] Types =
        {
            new(1_099_500, 12.5, 0.00250, 0.00080),
            new(1_100_000, 13.5, 0.00247, 0.00075),
            new(1_100_250, 14.0, 0.00242, 0.00065),
            new(1_400_000, 16.0, 0.00225, 0.00040),
            new(1_600_000, 11.0, 0.00340, 0.00020),
            new(1_600_000, 11.0, 0.00340, 0.00030)
        };

        public static FuelType Of(int number) => number >= 0 && number < Types.Length ? Types[number] : Types[0];
    }

    /// <summary>What the parts call their figures</summary>
    internal static class Keys
    {
        public const string Bore = "bore";
        public const string Cylinders = "cylinders";
        public const string IdleRpm = "rpm_idle";
        public const string RpmLimit = "RPM_limit";
        public const string SparkMin = "time_spark_min";
        public const string SparkInc = "time_spark_inc";
        public const string SparkRpm0 = "time_spark_RPM0";
        public const string SparkRpm1 = "time_spark_RPM1";
        public const string DeckHeight = "crank_center_to_cylinder_top";
        public const string Stroke = "stroke";
        public const string RodLength = "length";
        public const string PinHeight = "length_from_wrist_pin_center_to_crown_base";
        public const string Dome = "crown_volume";
        public const string MaxRpm = "maxRPM";
        public const string Inertia = "inertia";
        public const string Sliding = "sliction_loss_multiplier";
        public const string BestSliding = "best_lubrication_sliction";
        public const string WorstSliding = "worst_lubrication_sliction";
        public const string OilPanFriction = "friction_fwd";
        public const string HeatLoss = "T_loss";
        public const string Chamber = "Vmin";
        public const string IntakeValve = "intake_valve_diam";
        public const string IntakeValves = "number_of_intake_valves_per_cylinder";
        public const string ExhaustValve = "exhaust_valve_diam";
        public const string ExhaustValves = "number_of_exhaust_valves_per_cylinder";
        public const string IntakeTuning = "intake_efficiency_tuning";
        public const string ExhaustTuning = "exhaust_efficiency_tuning";
        public const string Efficiency = "efficiency";
        public const string LobeSeparation = "static_input_lobe_separation";
        public const string LiftInOpen = "static_input_lift_in_open";
        public const string LiftInClose = "static_input_lift_in_close";
        public const string LiftOutOpen = "static_input_lift_out_open";
        public const string LiftOutClose = "static_input_lift_out_close";
        public const string IntakeDuration = "static_input_time_in_duration";
        public const string ExhaustDuration = "static_input_time_out_duration";
        public const string Advance = "advance";
        public const string AdvanceMin = "advance_negative_peak";
        public const string AdvanceMax = "advance_positive_peak";
        public const string AdvanceStep = "advance_minimum_step";
        public const string MixtureRatio = "mixture_ratio";
        public const string FuelType = "fuel_type";
        public const string FuelFlow = "max_fuel_consumption";
        public const string AirFlow = "max_air_consumption";
        public const string OpenAirFlow = "open_air_flow";
        public const string FilterAirFlow = "stock_max_air_consumption";
        public const string DustEffect = "dusted_efficiency";
        public const string ChargerRpmMultiplier = "rpm_turbo_mul";
        public const string DefaultChargerRpmMultiplier = "default_rpm_turbo_mul";
        public const string ChargerRpmOptimum = "rpm_turbo_opt";
        public const string ChargerRpmRange = "rpm_turbo_range";
        public const string BoostMax = "P_turbo_max";
        public const string BoostWastegate = "P_turbo_waste";
        public const string Gears = "gears";
        public const string Ratio = "ratio";
        public const string FinalDrive = "end_ratio";
        public const string DriveType = "drive_type";
        public const string DiffLock = "diff_lock";
    }

    /// <summary>Which slot of a part takes what: a part's slot roles</summary>
    internal static class Roles
    {
        public const string Crankshaft = "crankshaft";
        public const string CrankshaftBearing = "crankshaft_bearing";
        public const string Transmission = "transmission";
        public const string OilPan = "oil_pan";
        public const string IntakeManifold = "intake_manifold";
        public const string LeftHead = "L_cylinder_head";
        public const string RightHead = "R_cylinder_head";
        public const string Head = "cylinder_head";
        public const string Camshaft = "camshaft";
        public const string CamshaftBearing = "camshaft_bearing";
        public const string ConnectingRod = "con_rod";
        public const string Piston = "piston";
        public const string Flywheel = "flywheel";
        public const string Clutch = "clutch";
        public const string ExhaustHeader = "exhaust_header";
        public const string EngineBlock = "engine_block";
    }

    /// <summary>The classes of part the engine is made of</summary>
    internal static class Classes
    {
        public const string Block = "Block";
        public const string Crankshaft = "Crankshaft";
        public const string ConnectingRod = "ConnectingRod";
        public const string Piston = "Piston";
        public const string Flywheel = "Flywheel";
        public const string Clutch = "Clutch";
        public const string CylinderHead = "CylinderHead";
        public const string Camshaft = "Camshaft";
        public const string Sliding = "SlidingEnginePart";
        public const string Reciprocating = "ReciprocatingEnginePart";
        public const string Transmission = "Transmission";
        public const string OilPan = "OilPan";
        public const string IntakeManifold = "IntakeManifold";
        public const string FuelSystem = "FuelInjectorSystem";
        public const string AirFilter = "AirFilter";
        public const string Charger = "Charger";
        public const string Supercharger = "SuperCharger";
        public const string Turbocharger = "TurboCharger";
        public const string Nitrous = "NOSInjectorSystem";
        public const string ExhaustHeader = "ExhaustHeader";
    }
}
