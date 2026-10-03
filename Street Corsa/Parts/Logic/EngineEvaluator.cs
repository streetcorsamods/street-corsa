using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Scripting;

namespace StreetCorsa.Parts.Logic;

/// <summary>What an engine build comes to: whether it runs, its curve, and what it hands to the drivetrain</summary>
public sealed class EngineReport
{
    /// <summary>Why the engine does not run, in the part scripts' words; null when it does</summary>
    public string? Problem { get; init; }

    public bool Runs => Problem == null && Dyno is { MaxPowerHp: > 0 };

    public DynoInputs? Inputs { get; init; }
    public DynoResult? Dyno { get; init; }

    public double IdleRpm { get; init; }
    public double LimiterRpm { get; init; }

    /// <summary>Rotating inertia of the engine, kg m2</summary>
    public double Inertia { get; init; }

    /// <summary>Forward gear ratios, first gear first</summary>
    public IReadOnlyList<double> GearRatios { get; init; } = Array.Empty<double>();

    public double ReverseRatio { get; init; }
    public double FinalRatio { get; init; }

    /// <summary>1 = front, 2 = rear, 3 = all wheels; 0 when there is no transmission</summary>
    public int DriveType { get; init; }

    /// <summary>Limited slip lock, 0..1</summary>
    public double DiffLock { get; init; }

    /// <summary>
    /// The engine's own drag when it is pushed, the oil pan's figure over the losses of the moving parts,
    /// as the block leaves it on the car; 0 when there is no figure
    /// </summary>
    public double Friction { get; init; }

    /// <summary>The clutch's clamp figure (the script's maxF); 0 without a clutch</summary>
    public double ClutchCapacity { get; init; }

    /// <summary>
    /// Whether an exhaust-driven charger of the scripts is on the engine: boost comes with a lag, and a gauge. The
    /// turbos of the parts' figures are among the <see cref="Inputs"/>' chargers.
    /// </summary>
    public bool Turbocharged { get; init; }

    /// <summary>Assetto Corsa's lag of the scripts' turbo, as the parts' figures leave it (<c>"lag_up"</c>, <c>"lag_down"</c>)</summary>
    public double TurboLagUp { get; init; } = PartFigures.DefaultLagUp;
    public double TurboLagDown { get; init; } = PartFigures.DefaultLagDown;

    /// <summary>Whether the block has no script: the engine is made of its parts' figures alone</summary>
    public bool FromFigures { get; init; }

    /// <summary>Kilograms, all parts of the build</summary>
    public double Mass { get; init; }

    /// <summary>What the parts cost new, in the game's money (<see cref="PartPricing.NewPrice"/>, before a career's price multiplier)</summary>
    public double Value { get; init; }

    public IReadOnlyList<PartDefinition> Unplaced { get; init; } = Array.Empty<PartDefinition>();

    /// <summary>
    /// What went wrong running the part scripts (a script that threw, one that ran out of steps), one line each,
    /// for the log; empty when they ran clean. Any of it makes the engine's <see cref="Problem"/>.
    /// </summary>
    public IReadOnlyList<string> ScriptFaults { get; init; } = Array.Empty<string>();
}

/// <summary>Runs an engine build through the part scripts and the dyno</summary>
public static class EngineEvaluator
{
    private const int ReverseGearIndex = 7;
    private const int MaxForwardGears = 6;

    // Figures past any engine the game could hold: a script (anyone's) or a pack.json can make them, finite and absurd,
    // and AC would be handed them. Each is well above anything real, so a real build never meets one.
    private const double MaxRpm = 30_000;
    private const double MaxPowerHp = 20_000;
    private const double MaxTorqueNm = 50_000;
    private const double MaxRatio = 100;
    private const double MaxFigure = 1e6;

    /// <remarks>
    /// Never throws for what the content does: a part script that faults is a part left out, and the engine's
    /// problem says so (the scripts are third-party code, one bad class must not take every build down with it).
    /// </remarks>
    public static EngineReport Evaluate(PartsCatalog catalog, PartTree tree)
    {
        try
        {
            return Run(catalog, tree);
        }
        catch (Exception ex)
        {
            // Past the runtime's own containment (the figures themselves, a malformed tree): the engine does not run
            var fault = $"evaluating {tree.Root.Definition.Id} failed ({ex.GetType().Name}: {ex.Message})";
            return new EngineReport { Problem = "the engine could not be evaluated: " + fault, ScriptFaults = new[] { fault }, Unplaced = tree.Unplaced };
        }
    }

    private static EngineReport Run(PartsCatalog catalog, PartTree tree)
    {
        var runtime = new PartScriptRuntime(catalog, tree.Root);
        runtime.UpdateCar();

        var block = runtime.ObjectOf(tree.Root);
        var chassis = runtime.Chassis;
        var figures = runtime.Figures;
        var data = block?.Fields.GetValueOrDefault("dynodata")?.AsObject;

        // A block without a script but with figures is an engine of figures alone: no script fills a DynoData, the
        // parts' figures go on the base of such an engine and the dyno runs on them here
        var fromFigures = block == null && tree.Root.Definition.SourceScript == null && tree.Root.Definition.ReadFigures != null;
        var inputs = fromFigures ? PartFigures.Apply(PartFigures.DataEngineBase, figures)
            : data == null ? null : PartFigures.Apply(DynoInputs.From(data), figures);
        var dyno = fromFigures ? EngineDyno.Run(inputs!) : data == null ? null : runtime.DynoResultOf(data);

        // The scripts' own verdict, asked after the dyno so the compression check has its figure. A part whose
        // script is not there looks like a missing part to the others; what they say about it would mislead.
        var problem = runtime.MissingScripts.Count > 0
            ? $"the script of {runtime.MissingScripts[0].Definition.Id} is missing ({runtime.MissingScripts.Count} in all): the parts need converting again."
            : fromFigures ? PartFigures.MissingOf(inputs!) : runtime.Call(tree.Root, "isDynoable").AsText;

        // What the scripts do not ask about: the rules a pack holds its part to itself (the extra carburettor pads of
        // a mod's manifold that runs on a stock script, which never looks at them)
        problem ??= EmptyRequiredSlot(tree.Root);

        // A script that threw or ran out of steps left figures half made: whatever they come to is not the engine
        var faults = runtime.Faults.ToList();
        if (runtime.BudgetExhausted)
            faults.Add($"a part script ran out of steps in {runtime.BudgetExhaustedIn ?? "an unknown method"} (it loops, or does far more than a part should)");
        if (faults.Count > 0 && runtime.MissingScripts.Count == 0)
            problem = $"a part script failed: {faults[0]}{(faults.Count > 1 ? $" ({faults.Count} faults in all)" : "")}.";

        if (problem == null && dyno == null) problem = "the engine could not be evaluated.";
        if (problem == null && dyno is { } made && !(made.MaxPowerHp <= MaxPowerHp && made.MaxTorque <= MaxTorqueNm))
            problem = $"the dyno makes {made.MaxPowerHp:0} hp and {made.MaxTorque:0} Nm, past any engine: a part's figures are not what they should be.";

        // The scripts hold the compression against what the fuel system allows. With no carburettor or injection
        // that limit is 0, and what they say ("too high, not more than 0.0:1") points away from what is wrong.
        if (problem != null && inputs is { MaxFuelFlow: <= 0, MaxAirFlow: <= 0 } && problem.Contains("compression", StringComparison.OrdinalIgnoreCase))
            problem = "nothing feeds the engine: there is no carburettor or injection on the intake.";

        // Figures come out of script math (a square root of a negative, a float overflow) and pack.json: what is not
        // a number is no figure, so nothing that is not one ever reaches the car's data
        var gears = (int)Finite(chassis?.Number("gears"), MaxFigure);
        var ratios = chassis?.Fields.GetValueOrDefault("ratio") as ScriptArray;
        double Ratio(int index) => ratios != null && ratios.Items.GetValueOrDefault(index) is ScriptNumber ratio ? Finite(ratio.Amount, MaxRatio) : 0;
        var gearRatios = Enumerable.Range(1, Math.Clamp(gears, 0, MaxForwardGears)).Select(Ratio).Where(r => r > 0).ToList();
        if (PartFigures.GearsOf(figures) is { } given) gearRatios = given.Select(r => Finite(r, MaxRatio)).Where(r => r > 0).ToList();

        // The engine's own figures, in the units the parts give them in (the report's figure times unit), then back to
        // the report's; a figure no part gives stays exactly what it was
        double Engine(string name, double current, double max, double unit = 1) =>
            PartFigures.ApplyEngine(figures, name, current * unit) is { } given ? Finite(given, max * unit) / unit : current;

        var parts = tree.Root.SelfAndDescendants().ToList();
        var clutch = parts.FirstOrDefault(p => p.Is("Clutch"));
        var clutchCapacity = clutch == null ? 0 : Finite(runtime.ObjectOf(clutch)?.Number("maxF") ?? clutch.Definition.Number("maxF"), MaxFigure * 100);
        var friction = Finite(chassis?.Number("engine_friction_fwd"), MaxFigure);
        var inertia = fromFigures
            ? (dyno?.Displacement ?? 0) * 1000 * PartFigures.InertiaPerLitre / PartFigures.KgM2PerInertiaUnit
            : Finite(block?.Number("inertia"), MaxFigure);
        var limiter = fromFigures || PartFigures.ApplyEngine(figures, "rev_limit", 0) != null ? inputs!.RpmLimit : block?.Number("RPM_limit");
        return new EngineReport
        {
            Problem = problem,
            Inputs = inputs,
            Dyno = dyno,
            FromFigures = fromFigures,
            IdleRpm = Engine("idle_rpm", fromFigures ? PartFigures.DefaultIdleRpm : Finite(block?.Number("rpm_idle"), MaxRpm), MaxRpm),
            LimiterRpm = Finite(limiter, MaxRpm),
            Inertia = Engine("inertia", inertia, MaxFigure, PartFigures.KgM2PerInertiaUnit),
            GearRatios = gearRatios,
            ReverseRatio = Engine("reverse", Math.Abs(Ratio(ReverseGearIndex)), MaxRatio),
            FinalRatio = Engine("final_drive", Finite(chassis?.Number("rearend_ratio"), MaxRatio), MaxRatio),
            DriveType = gears > 0 ? (int)Finite(chassis?.Number("drive_type"), MaxFigure) : 0,
            DiffLock = Engine("diff_lock", Finite(chassis?.Number("diff_lock"), MaxFigure), MaxFigure),
            Friction = PartFigures.ApplyEngine(figures, "friction", friction > 0 ? friction / PartFigures.ReferenceFriction : 1) is { } drag
                ? Finite(drag * PartFigures.ReferenceFriction, MaxFigure)
                : friction,
            ClutchCapacity = Engine("clutch_torque", clutchCapacity, MaxFigure * 100, PartFigures.ClutchNmPerUnit),
            // A part of figures may take a turbo's shelf by its class; only a turbo with a script is the scripts' charger
            Turbocharged = parts.Any(p => p.Is("TurboCharger") && runtime.ObjectOf(p) != null),
            TurboLagUp = LagOf("lag_up", PartFigures.DefaultLagUp),
            TurboLagDown = LagOf("lag_down", PartFigures.DefaultLagDown),
            Mass = Finite(parts.Sum(p => (double)p.Definition.Mass), MaxFigure),
            Value = Finite(parts.Sum(p => PartPricing.NewPrice(p.Definition)), MaxFigure * 1000),
            ScriptFaults = faults,
            Unplaced = tree.Unplaced
        };

        double LagOf(string name, double fallback) => PartFigures.ApplyEngine(figures, name, fallback) is { } lag && lag is > 0 and < 1 ? lag : fallback;
    }

    /// <summary>The first empty slot that a part's pack enforces, in the rule's words; null when there is none</summary>
    private static string? EmptyRequiredSlot(InstalledPart root)
    {
        foreach (var part in root.SelfAndDescendants())
        {
            foreach (var rule in part.Definition.RequiredSlots.Where(r => r.Enforce))
            {
                if (part.Definition.Slots.Any(s => s.Id == rule.Slot) && !part.Children.ContainsKey(rule.Slot))
                    return string.IsNullOrWhiteSpace(rule.Message) ? $"{part.Definition.DisplayName ?? part.Definition.Name} is missing a part." : rule.Message;
            }
        }

        return null;
    }

    /// <summary>
    /// A figure as the report may carry it: missing, NaN, infinities and anything larger either way than
    /// <paramref name="max"/> are 0 (no figure)
    /// </summary>
    private static double Finite(double? value, double max) => value is { } number && double.IsFinite(number) && Math.Abs(number) <= max ? number : 0;
}
