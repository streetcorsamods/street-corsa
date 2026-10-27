using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Parts.Logic;

/// <summary>What an engine build comes to: whether it runs, its curve, and what it hands to the drivetrain</summary>
public sealed class EngineReport
{
    /// <summary>Why the engine does not run, in the player's words; null when it does</summary>
    public string? Problem { get; init; }

    public bool Runs => Problem == null && Dyno is { MaxPowerHp: > 0 };

    public DynoInputs? Inputs { get; init; }
    public DynoResult? Dyno { get; init; }

    public double IdleRpm { get; init; }

    /// <summary>Where the engine stops: its ignition cut, else a little past its redline (it goes no further)</summary>
    public double LimiterRpm { get; init; }

    /// <summary>
    /// Where the engine starts to hurt itself: what its weakest rotating part survives, or a tenth past where its valves
    /// float (the valves meet the pistons), whichever comes first; the curve's end when nothing says
    /// </summary>
    public double RedlineRpm { get; init; }

    /// <summary>How far the engine is driven: its redline, or its cut when that comes first; 0 when the report says neither</summary>
    public double DrivenToRpm => RedlineRpm > 0 && (LimiterRpm <= 0 || RedlineRpm < LimiterRpm) ? RedlineRpm : LimiterRpm;

    /// <summary>Rotating inertia of the engine, kg m2</summary>
    public double Inertia { get; init; }

    /// <summary>Forward gear ratios, first gear first</summary>
    public IReadOnlyList<double> GearRatios { get; init; } = Array.Empty<double>();

    public double ReverseRatio { get; init; }
    public double FinalRatio { get; init; }

    /// <summary>1 = front, 2 = rear, 3 = all wheels; 0 when there is no transmission</summary>
    public int DriveType { get; init; }

    /// <summary>
    /// Limited slip lock, 0..1; null when nothing of the build says (a gearbox of figures with no <c>"diff_lock"</c>):
    /// the car keeps its own differential
    /// </summary>
    public double? DiffLock { get; init; }

    /// <summary>
    /// The engine's own drag when it is pushed, the oil pan's figure over the losses of the moving parts,
    /// as the block leaves it on the car; 0 when there is no figure
    /// </summary>
    public double Friction { get; init; }

    /// <summary>The clutch's clamp figure: its Nm over <see cref="PartFigures.ClutchNmPerUnit"/>; 0 without a clutch</summary>
    public double ClutchCapacity { get; init; }

    /// <summary>
    /// Whether a turbocharger with specs of its own is on the engine: boost comes with a lag, and a gauge. The
    /// turbos of the parts' figures are among the <see cref="Inputs"/>' chargers.
    /// </summary>
    public bool Turbocharged { get; init; }

    /// <summary>Assetto Corsa's lag of that turbo, as the parts' figures leave it (<c>"lag_up"</c>, <c>"lag_down"</c>)</summary>
    public double TurboLagUp { get; init; } = PartFigures.DefaultLagUp;
    public double TurboLagDown { get; init; } = PartFigures.DefaultLagDown;

    /// <summary>Whether the block has no specs: the engine is made of its parts' figures alone</summary>
    public bool FromFigures { get; init; }

    /// <summary>Kilograms, all parts of the build</summary>
    public double Mass { get; init; }

    public IReadOnlyList<PartDefinition> Unplaced { get; init; } = Array.Empty<PartDefinition>();

    /// <summary>
    /// What went wrong working the engine out (figures that broke the sum), one line each, for the log; empty
    /// when nothing did. Any of it makes the engine's <see cref="Problem"/>.
    /// </summary>
    public IReadOnlyList<string> Faults { get; init; } = Array.Empty<string>();
}

/// <summary>Works an engine build out from its parts' specs and figures and runs it on the dyno</summary>
public static class EngineEvaluator
{
    private const int MaxForwardGears = PartFigures.MaxGears;

    // Figures past any engine the game could hold: a part file can make them, finite and absurd, and AC would be
    // handed them. Each is well above anything real, so a real build never meets one.
    private const double MaxRpm = EngineDyno.MaxRpm;
    private const double MaxPowerHp = 20_000;
    private const double MaxTorqueNm = 50_000;
    private const double MaxRatio = 100;
    private const double MaxFigure = 1e6;

    /// <summary>The car's final drive when no gearbox of the engine gives one</summary>
    private const double DefaultFinalDrive = 3.3;

    /// <remarks>
    /// Never throws for what the content does: figures that break the sum are the engine's problem, and it does not run
    /// </remarks>
    public static EngineReport Evaluate(PartsCatalog catalog, PartTree tree)
    {
        try
        {
            return Run(tree);
        }
        catch (Exception ex)
        {
            var fault = $"evaluating {tree.Root.Definition.Id} failed ({ex.GetType().Name}: {ex.Message})";
            return new EngineReport { Problem = PartWords.Say("engine.problem.not_evaluated", ("fault", fault)), Faults = new[] { fault }, Unplaced = tree.Unplaced };
        }
    }

    private static EngineReport Run(PartTree tree)
    {
        var root = tree.Root;
        var figures = PartFigures.Of(root);

        // A block without specs is an engine of figures alone, whichever of its parts give them: they go on the base of
        // such an engine. Otherwise the parts' specs make the engine and the figures go over it.
        var fromFigures = root.Definition.Specs.Count == 0;
        var assembly = fromFigures ? null : EngineAssembly.Of(root);
        var defaultCut = fromFigures ? PartFigures.DataEngineRevLimit : EngineAssembly.DefaultRevLimit;
        var inputs = EngineDyno.WithCut(PartFigures.Apply(fromFigures ? PartFigures.DataEngineBase : assembly!.Inputs, figures, defaultCut), defaultCut);
        var dyno = EngineDyno.Run(inputs);

        // What the block needs on its slots, in its own order (an engine of figures, then what its figures lack), then
        // the compression its fuel takes, then what the other parts need
        var problem = NeedOf(root) ?? (fromFigures ? PartFigures.MissingOf(inputs) : null);
        if (problem == null && assembly != null && inputs is not { MaxFuelFlow: <= 0, MaxAirFlow: <= 0 })
        {
            if (dyno.Compression < assembly.MinCompression)
                problem = PartWords.Say("engine.problem.compression_low", ("compression", EngineDyno.Figure(assembly.MinCompression, "0.0")));
            else if (dyno.Compression > assembly.MaxCompression)
                problem = PartWords.Say("engine.problem.compression_high", ("compression", EngineDyno.Figure(assembly.MaxCompression, "0.0")));
        }

        problem ??= root.SelfAndDescendants().Skip(1).Select(NeedOf).FirstOrDefault(n => n != null);
        if (problem == null && inputs is { MaxFuelFlow: <= 0, MaxAirFlow: <= 0 })
            problem = PartWords.Say(EngineDyno.NotFedKey);
        if (problem == null && dyno is { Curve.Count: 0 })
            problem = EngineDyno.Unrunnable(inputs) ?? PartWords.Say("engine.problem.no_power");
        if (problem == null && !(dyno.MaxPowerHp <= MaxPowerHp && dyno.MaxTorque <= MaxTorqueNm))
            problem = PartWords.Say("engine.problem.absurd_power", ("hp", EngineDyno.Figure(dyno.MaxPowerHp, "0")), ("nm", EngineDyno.Figure(dyno.MaxTorque, "0")));

        // Figures come out of a sum of a pack's numbers: what is not a number is no figure, so nothing that is not one
        // ever reaches the car's data
        var gearbox = assembly?.Transmission;
        var gearRatios = (gearbox?.Gears ?? Array.Empty<double>()).Take(MaxForwardGears).Select(r => Finite(r, MaxRatio)).Where(r => r > 0).ToList();
        var gearsGiven = PartFigures.GearsOf(figures);
        if (gearsGiven != null) gearRatios = gearsGiven.Select(r => Finite(r, MaxRatio)).Where(r => r > 0).ToList();

        // A gearbox of figures (or an engine of them) has only what a part says, and with nothing said the car keeps
        // its own differential
        var diffLock = Engine("diff_lock", Finite(gearbox?.DiffLock, MaxFigure), MaxFigure);
        double? keptDiffLock = fromFigures || gearsGiven != null
            ? PartFigures.ApplyEngine(figures, "diff_lock", 0) != null ? diffLock : null
            : diffLock;

        // The engine's own figures, in the units the parts give them in (the report's figure times unit), then back to
        // the report's; a figure no part gives stays exactly what it was
        double Engine(string name, double current, double max, double unit = 1) =>
            PartFigures.ApplyEngine(figures, name, current * unit) is { } given ? Finite(given, max * unit) / unit : current;

        var parts = root.SelfAndDescendants().ToList();
        var clutchCapacity = assembly == null ? 0 : Finite(assembly.ClutchTorque / PartFigures.ClutchNmPerUnit, MaxFigure * 100);
        var friction = assembly == null ? 0 : Finite(assembly.Friction, MaxFigure);
        var inertia = fromFigures
            ? dyno.Displacement * 1000 * PartFigures.InertiaPerLitre / PartFigures.KgM2PerInertiaUnit
            : Finite(assembly!.Inertia / PartFigures.KgM2PerInertiaUnit, MaxFigure);
        var limiter = EngineDyno.LimiterRpm(inputs);
        var redline = EngineDyno.RedlineRpm(inputs);
        if (redline == double.MaxValue) redline = dyno.Curve.Count > 0 ? dyno.Curve[^1].Rpm : limiter;

        // A part's idle is held to what an engine ticks over at, below the limiter the car will have (the curve's end
        // without one): a mod's 9000 or -900 would put the car's least revs over its limiter or under nothing. Whether
        // the block's spec or a part's figure gives it: AC gets the idle the same way from both
        var idle = Engine("idle_rpm", fromFigures ? PartFigures.DefaultIdleRpm : Finite(assembly!.IdleRpm, MaxRpm), MaxRpm);
        var revsTo = Finite(limiter, MaxRpm) is > 0 and var given ? given : dyno.Curve.Count > 0 ? dyno.Curve[^1].Rpm : MaxRpm;
        idle = Math.Clamp(idle, PartFigures.MinIdleRpm, Math.Max(PartFigures.MinIdleRpm, revsTo * PartFigures.MostIdleOfLimiter));

        return new EngineReport
        {
            Problem = problem,
            Inputs = inputs,
            Dyno = dyno,
            FromFigures = fromFigures,
            IdleRpm = idle,
            LimiterRpm = Finite(limiter, MaxRpm),
            RedlineRpm = Finite(redline, MaxRpm),
            Inertia = Engine("inertia", inertia, MaxFigure, PartFigures.KgM2PerInertiaUnit),
            GearRatios = gearRatios,
            ReverseRatio = Engine("reverse", Finite(Math.Abs(gearbox?.Reverse ?? 0), MaxRatio), MaxRatio),
            FinalRatio = Engine("final_drive", gearbox != null ? Finite(gearbox.FinalDrive, MaxRatio) : DefaultFinalDrive, MaxRatio),
            DriveType = gearbox is { Gears.Count: > 0 } ? gearbox.DriveType : 0,
            DiffLock = keptDiffLock,
            Friction = PartFigures.ApplyEngine(figures, "friction", friction > 0 ? friction / PartFigures.ReferenceFriction : 1) is { } drag
                ? Finite(drag * PartFigures.ReferenceFriction, MaxFigure)
                : friction,
            ClutchCapacity = Engine("clutch_torque", clutchCapacity, MaxFigure * 100, PartFigures.ClutchNmPerUnit),
            Turbocharged = parts.Any(p => p.Is(PartClasses.Turbocharger) && p.Definition.Specs.ContainsKey(EngineAssembly.Specs.Boost)),
            TurboLagUp = LagOf("lag_up", PartFigures.DefaultLagUp),
            TurboLagDown = LagOf("lag_down", PartFigures.DefaultLagDown),
            Mass = Finite(parts.Sum(p => (double)p.Definition.Mass), MaxFigure),
            Unplaced = tree.Unplaced
        };

        double LagOf(string name, double fallback) => PartFigures.ApplyEngine(figures, name, fallback) is { } lag && lag is > 0 and < 1 ? lag : fallback;
    }

    /// <summary>The first slot the part needs filled that is empty, in its rule's words; null when it has all</summary>
    private static string? NeedOf(InstalledPart part)
    {
        foreach (var rule in part.Definition.Needs)
        {
            if (part.Definition.Slots.Any(s => s.Id == rule.Slot) && !part.Children.ContainsKey(rule.Slot))
                return string.IsNullOrWhiteSpace(rule.Message) ? PartWords.Say("engine.problem.missing_part", ("part", part.Definition.DisplayName ?? part.Definition.Name)) : rule.Message;
        }

        return null;
    }

    /// <summary>
    /// A figure as the report may carry it: missing, NaN, infinities and anything larger either way than
    /// <paramref name="max"/> are 0 (no figure)
    /// </summary>
    private static double Finite(double? value, double max) => value is { } number && double.IsFinite(number) && Math.Abs(number) <= max ? number : 0;
}
