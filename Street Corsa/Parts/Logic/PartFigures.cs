using System.Globalization;
using Newtonsoft.Json.Linq;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Parts.Logic;

/// <summary>
/// The engine figures a part gives in its pack, over the engine its specs make: a pack's <c>"figures"</c>.
/// A number puts the part's own figure, <c>{ "add": n }</c> adds to the engine's and <c>{ "times": n }</c> multiplies
/// it, so a block says its bore, a header adds to how well the engine breathes out and a carburettor gives the fuel
/// it can deliver. On an engine of specs the figures go over what the specs worked out, before
/// the dyno runs, so a data part works on any engine; an engine whose block has no specs is made of figures alone
/// (<see cref="DataEngineBase"/> under them).
///
/// Of the same figure, the parts' own numbers are taken first (the part furthest out from the block wins, sibling
/// parts by the slot they are on), then what is added, then what multiplies. Units are plain metric: mm, cc, g/s,
/// degrees of the crankshaft, bar, rpm, kg m2, Nm.
/// </summary>
public static class PartFigures
{
    /// <summary>Most forward gears a gearbox of figures may have: what Assetto Corsa takes</summary>
    public const int MaxGears = 10;

    /// <summary>The engine report's inertia figures (the parts' old units) to kg m2 of a spinning engine</summary>
    public const double KgM2PerInertiaUnit = 0.02;

    /// <summary>The friction figure of a stock oil pan on a stock bottom end (the median of the builds)</summary>
    public const double ReferenceFriction = 0.0002;

    /// <summary>Nm a clutch holds per unit of the report's clamp figure</summary>
    public const double ClutchNmPerUnit = 2.6;

    /// <summary>Assetto Corsa's turbo lag the game gives a turbo whose part says none</summary>
    public const double DefaultLagUp = 0.992;
    public const double DefaultLagDown = 0.985;

    /// <summary>A figure engine's rotating inertia, kg m2 per litre, when no part gives one (a small-block V8's)</summary>
    public const double InertiaPerLitre = 0.028;

    public const double DefaultIdleRpm = 800;

    /// <summary>The ignition cut of an engine of figures none of whose parts says how fast it turns</summary>
    public const double DataEngineRevLimit = 6000;

    /// <summary>
    /// The slowest tick-over a part may give an engine, and the most of its limiter: an idle past them would put the
    /// car's least revs under nothing or over its limiter, and the damage and downshift points that follow it
    /// </summary>
    public const double MinIdleRpm = 300;
    public const double MostIdleOfLimiter = 0.5;

    /// <summary>Most turbos an engine of figures may have: what the car's data is given sections for</summary>
    public const int MaxTurbos = 8;

    private const double CycleDegrees = 720;
    private const double MaxBoost = 5;
    private const int MaxCylinders = 32;

    /// <summary>
    /// What an engine of figures starts from before its parts' figures go on: a small-block V8's breathing, valve
    /// timing, spark and mixture as the stock parts give them, and no bore, stroke, cylinders, compression
    /// or fuel at all, which its parts have to give; no ignition cut either (<see cref="DataEngineRevLimit"/> when no part
    /// says how fast it turns).
    /// </summary>
    public static readonly DynoInputs DataEngineBase = new()
    {
        IntakeMax = 0.8,
        ExhaustMax = 0.3,
        IntakeOpen = 0.05,
        IntakeClose = 0.05 + 230 / CycleDegrees,
        ExhaustOpen = 0.6,
        ExhaustClose = 0.6 + 230 / CycleDegrees,
        SparkMin = 0.5 - 35 / CycleDegrees,
        SparkInc = 15 / CycleDegrees,
        SparkRpm0 = 2000,
        SparkRpm1 = 6000,
        BurnTime = 0.0007,
        HeatLoss = 75,
        MixtureRatio = 12.5,
        MixtureHeat = 1.70e6
    };

    private sealed record DynoFigure(string Name, Func<DynoInputs, double> Get, Func<DynoInputs, double, DynoInputs> With, bool Whole = false);

    /// <summary>The figures on the dyno's inputs, in the order they go on: compression last, from the bore and stroke it ends with</summary>
    private static readonly DynoFigure[] DynoFigures =
    {
        new("cylinders", i => i.Cylinders, (i, v) => i with { Cylinders = (int)Math.Round(v) }, Whole: true),
        new("bore", i => i.Bore * 1000, (i, v) => i with { Bore = v / 1000 }),
        new("stroke", i => i.Stroke * 1000, (i, v) => i with { Stroke = v / 1000 }),
        new("chamber", i => i.ClearanceVolume * 1e6, (i, v) => i with { ClearanceVolume = v / 1e6 }),
        new("intake_flow", i => i.IntakeMax, (i, v) => i with { IntakeMax = v }),
        new("exhaust_flow", i => i.ExhaustMax, (i, v) => i with { ExhaustMax = v }),
        new("intake_duration", i => EngineDyno.Span(i.IntakeOpen, i.IntakeClose) * CycleDegrees, (i, v) => i with { IntakeClose = Wrap(i.IntakeOpen + v / CycleDegrees) }),
        new("exhaust_duration", i => EngineDyno.Span(i.ExhaustOpen, i.ExhaustClose) * CycleDegrees, (i, v) => i with { ExhaustClose = Wrap(i.ExhaustOpen + v / CycleDegrees) }),
        new("spark_advance", i => (0.5 - i.SparkMin) * CycleDegrees, (i, v) =>
        {
            var high = i.SparkMin + i.SparkInc;
            var low = 0.5 - v / CycleDegrees;
            return i with { SparkMin = low, SparkInc = high - low };
        }),
        new("spark_advance_high", i => (0.5 - i.SparkMin - i.SparkInc) * CycleDegrees, (i, v) => i with { SparkInc = 0.5 - v / CycleDegrees - i.SparkMin }),
        new("spark_rpm_low", i => i.SparkRpm0, (i, v) => i with { SparkRpm0 = v }),
        new("spark_rpm_high", i => i.SparkRpm1, (i, v) => i with { SparkRpm1 = v }),
        new("burn_time", i => i.BurnTime * 1000, (i, v) => i with { BurnTime = v / 1000 }),
        new("heat_loss", i => i.HeatLoss, (i, v) => i with { HeatLoss = v }),
        new("mixture_ratio", i => i.MixtureRatio, (i, v) => i with { MixtureRatio = v }),
        new("mixture_heat", i => i.MixtureHeat / 1e6, (i, v) => i with { MixtureHeat = v * 1e6 }),
        new("fuel_flow", i => i.MaxFuelFlow * 1000, (i, v) => i with { MaxFuelFlow = v / 1000 }),
        new("air_flow", i => i.MaxAirFlow * 1000, (i, v) => i with { MaxAirFlow = v / 1000 }),
        // A speed added to or multiplied goes on the one the engine has: without one of its own, how fast the engine turns
        // as it is; the cut last, from the speeds it ends with (an engine without a cut stops a little past its redline)
        new("max_rpm", i => i.MaxRpm >= EngineDyno.LeastSpeed ? i.MaxRpm : EngineDyno.ReachesRpm(i), (i, v) => i with { MaxRpm = v }),
        new("float_rpm", i => i.FloatRpm >= EngineDyno.LeastSpeed ? i.FloatRpm : EngineDyno.ReachesRpm(i), (i, v) => i with { FloatRpm = v }),
        new("rev_limit", EngineDyno.LimiterRpm, (i, v) => i with { RpmLimit = v }),
        new("compression", EngineDyno.Compression, (i, v) => i with { ClearanceVolume = v > 1 ? EngineDyno.CylinderVolume(i) / (v - 1) : 0 })
    };

    /// <summary>Figures of the engine as a whole, which the evaluator puts on its report (<see cref="ApplyEngine"/>)</summary>
    public static readonly IReadOnlyList<string> EngineFigures = new[]
    {
        "idle_rpm", "inertia", "friction", "clutch_torque", "final_drive", "reverse", "diff_lock", "lag_up", "lag_down"
    };

    private const string GearsKey = "gears";
    private const string TurboKey = "turbo";
    private const string BlowerKey = "blower";

    private static readonly HashSet<string> Known = new(DynoFigures.Select(f => f.Name).Concat(EngineFigures).Append(GearsKey).Append(TurboKey).Append(BlowerKey), StringComparer.Ordinal);

    /// <summary>Every part's figures read, as the catalog loads; what does not read is told and left out</summary>
    public static void Read(IEnumerable<PartDefinition> parts, List<string> problems)
    {
        foreach (var part in parts) part.ReadFigures = Read(part, problems);
    }

    public static PartFigureSet? Read(PartDefinition part, List<string> problems) => Read(part.Id, part.Figures, problems);

    /// <summary>Figures given by <paramref name="owner"/> (a part, a machine shop's cut), checked; null when none read</summary>
    public static PartFigureSet? Read(string owner, IEnumerable<KeyValuePair<string, JToken>>? given, List<string> problems)
    {
        if (given is not { } figures || !figures.Any()) return null;

        var numbers = new List<PartFigure>();
        var chargers = new List<DynoCharger>();
        List<double>? gears = null;
        foreach (var (key, token) in figures)
        {
            var label = $"{owner} figure \"{key}\"";
            if (!Known.Contains(key))
            {
                problems.Add($"{label}: no such figure (see the modding guide's list); left out");
                continue;
            }

            switch (key)
            {
                case GearsKey:
                    gears = Gears(token, label, problems) ?? gears;
                    break;
                case TurboKey or BlowerKey:
                    foreach (var charger in token is JArray list ? list.ToList() : new List<JToken> { token })
                    {
                        if (Charger(charger, key == TurboKey, label, problems) is not { } read) continue;
                        if (read.Turbo && chargers.Count(c => c.Turbo) >= MaxTurbos)
                        {
                            problems.Add($"{label}: more than {MaxTurbos} turbos; the rest left out");
                            break;
                        }

                        chargers.Add(read);
                    }
                    break;
                default:
                    if (Number(key, token, label, problems) is { } figure) numbers.Add(figure);
                    break;
            }
        }

        return numbers.Count == 0 && gears == null && chargers.Count == 0 ? null : new PartFigureSet { Numbers = numbers, Gears = gears, Chargers = chargers };
    }

    /// <summary>
    /// The figure sets of an assembly in the order their numbers go on: level by level out from the block, so a part
    /// further out always comes after (and wins over) one nearer the block; on one level, by the order of the parts
    /// they are on, then by their slot
    /// </summary>
    public static IReadOnlyList<PartFigureSet> Of(InstalledPart root)
    {
        var sets = new List<PartFigureSet>();
        var level = new List<InstalledPart> { root };
        while (level.Count > 0)
        {
            // A part's own figures, then what a machine shop's cuts on it change
            sets.AddRange(level.SelectMany(p => new[] { p.Definition.ReadFigures, p.CutFigures }).OfType<PartFigureSet>());
            level = level.SelectMany(p => p.Children.OrderBy(c => c.Key).Select(c => c.Value)).ToList();
        }

        return sets;
    }

    /// <summary>
    /// The dyno's inputs with the parts' figures on them; the very same inputs when no part has any. A speed added to or
    /// multiplied on an engine that says nothing of its speed goes on <paramref name="defaultCut"/>
    /// </summary>
    public static DynoInputs Apply(DynoInputs inputs, IReadOnlyList<PartFigureSet> sets, double defaultCut = 0)
    {
        if (sets.Count == 0) return inputs;

        foreach (var figure in DynoFigures)
        {
            if (Combine(sets, figure.Name, figure.Get(EngineDyno.WithCut(inputs, defaultCut))) is { } value) inputs = figure.With(inputs, value);
        }

        var chargers = sets.SelectMany(s => s.Chargers).ToList();
        return chargers.Count == 0 ? inputs : inputs with { Chargers = inputs.Chargers.Concat(chargers).ToList() };
    }

    /// <summary>
    /// An engine figure (<see cref="EngineFigures"/>) as the parts leave it, from the engine's own; null when no part
    /// gives it
    /// </summary>
    public static double? ApplyEngine(IReadOnlyList<PartFigureSet> sets, string name, double current) => Combine(sets, name, current);

    /// <summary>The gears the parts give, the part's furthest out from the block (the last of <see cref="Of"/>); null when none gives any</summary>
    public static IReadOnlyList<double>? GearsOf(IReadOnlyList<PartFigureSet> sets) => sets.LastOrDefault(s => s.Gears != null)?.Gears;

    /// <summary>What an engine of figures lacks to run, in the player's words; null when it has what it needs</summary>
    public static string? MissingOf(DynoInputs inputs)
    {
        if (inputs.Cylinders <= 0) return PartWords.Say("engine.problem.figures.no_cylinders");
        if (inputs.Bore <= 0) return PartWords.Say("engine.problem.figures.no_bore");
        if (inputs.Stroke <= 0) return PartWords.Say("engine.problem.figures.no_stroke");
        if (inputs.ClearanceVolume <= 0) return PartWords.Say("engine.problem.figures.no_compression");
        if (inputs.MaxFuelFlow <= 0) return PartWords.Say(EngineDyno.NotFedKey);
        return null;
    }

    private static double? Combine(IReadOnlyList<PartFigureSet> sets, string name, double current)
    {
        double? own = null;
        double added = 0, times = 1;
        var touched = false;
        foreach (var set in sets)
        {
            foreach (var figure in set.Numbers)
            {
                if (figure.Name != name) continue;

                touched = true;
                switch (figure.Op)
                {
                    case FigureOp.Set: own = figure.Value; break;
                    case FigureOp.Add: added += figure.Value; break;
                    case FigureOp.Times: times *= figure.Value; break;
                }
            }
        }

        return touched ? ((own ?? current) + added) * times : null;
    }

    private static PartFigure? Number(string name, JToken token, string label, List<string> problems)
    {
        var op = FigureOp.Set;
        if (token is JObject change)
        {
            var keys = change.Properties().Select(p => p.Name).ToList();
            if (keys.Count != 1 || keys[0] is not ("add" or "times"))
            {
                problems.Add($"{label}: give a number, {{ \"add\": n }} or {{ \"times\": n }}; left out");
                return null;
            }

            op = keys[0] == "add" ? FigureOp.Add : FigureOp.Times;
            token = change[keys[0]]!;
        }

        if (Finite(token, label, problems) is not { } value) return null;

        if (op == FigureOp.Times && value <= 0)
        {
            problems.Add($"{label}: multiplies by {Text(value)}; only by more than 0; left out");
            return null;
        }

        if (op == FigureOp.Set)
        {
            if (value < 0)
            {
                problems.Add($"{label}: {Text(value)} is below 0; left out");
                return null;
            }

            if (name == "cylinders" && (value != Math.Truncate(value) || value < 1 || value > MaxCylinders))
            {
                problems.Add($"{label}: {Text(value)} is no number of cylinders (1 to {MaxCylinders}); left out");
                return null;
            }

            if (name is "rev_limit" or "max_rpm" or "float_rpm" && value is <= 0 or > EngineDyno.MaxRpm)
            {
                problems.Add($"{label}: {Text(value)} rpm is no engine speed (above 0, up to {Text(EngineDyno.MaxRpm)}); left out");
                return null;
            }

            if (name == "compression" && value <= EngineDyno.MinCompression)
            {
                problems.Add($"{label}: a compression of {Text(value)}:1 is none; left out");
                return null;
            }
        }

        return new PartFigure(name, op, value);
    }

    private static List<double>? Gears(JToken token, string label, List<string> problems)
    {
        if (token is not JArray list || list.Count == 0 || list.Count > MaxGears)
        {
            problems.Add($"{label}: give the forward ratios as a list, first gear first, 1 to {MaxGears} of them; left out");
            return null;
        }

        var ratios = new List<double>();
        foreach (var item in list)
        {
            if (Finite(item, label, problems) is not { } ratio) return null;
            if (ratio <= 0)
            {
                problems.Add($"{label}: a ratio of {Text(ratio)} is none; the gears are left out");
                return null;
            }

            ratios.Add(ratio);
        }

        return ratios;
    }

    private static DynoCharger? Charger(JToken token, bool turbo, string label, List<string> problems)
    {
        if (token is not JObject charger)
        {
            problems.Add($"{label}: give the charger as {{ \"boost\": bar, \"full_boost_rpm\": rpm, ... }}; left out");
            return null;
        }

        var allowed = turbo ? new[] { "boost", "spool_rpm", "full_boost_rpm", "lag_up", "lag_down" } : new[] { "boost", "spool_rpm", "full_boost_rpm" };
        foreach (var unknown in charger.Properties().Select(p => p.Name).Where(n => !allowed.Contains(n)))
            problems.Add($"{label}: \"{unknown}\" is not a figure of a {(turbo ? "turbo" : "blower")}; it is left out");

        double? Read(string key) => charger[key] is { } value ? Finite(value, $"{label} \"{key}\"", problems) : null;

        var boost = Read("boost");
        var full = Read("full_boost_rpm");
        if (boost is not (> 0 and <= MaxBoost) || full is not (> 0 and <= EngineDyno.MaxRpm))
        {
            problems.Add($"{label}: a charger needs its \"boost\" (bar, up to {Text(MaxBoost)}) and its \"full_boost_rpm\"; left out");
            return null;
        }

        var spool = Read("spool_rpm") ?? 0;
        if (spool < 0 || spool >= full)
        {
            problems.Add($"{label}: \"spool_rpm\" has to be below \"full_boost_rpm\"; left out");
            return null;
        }

        var lagUp = turbo ? Read("lag_up") ?? DefaultLagUp : DefaultLagUp;
        var lagDown = turbo ? Read("lag_down") ?? DefaultLagDown : DefaultLagDown;
        if (lagUp is <= 0 or >= 1 || lagDown is <= 0 or >= 1)
        {
            problems.Add($"{label}: a turbo's lags are between 0 and 1 (closer to 1, slower); left out");
            return null;
        }

        return new DynoCharger(turbo, boost.Value, spool, full.Value, lagUp, lagDown);
    }

    private static double? Finite(JToken token, string label, List<string> problems)
    {
        if (token.Type is not (JTokenType.Integer or JTokenType.Float))
        {
            problems.Add($"{label}: \"{token}\" is not a number; left out");
            return null;
        }

        var value = (double)token;
        if (double.IsFinite(value)) return value;

        problems.Add($"{label}: {token} is not a finite number; left out");
        return null;
    }

    private static double Wrap(double fraction) => fraction - Math.Floor(fraction);

    private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
