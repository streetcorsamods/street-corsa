using System.Globalization;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Parts.Logic;

/// <summary>Torque over engine speed, with the figures people quote</summary>
public sealed class DynoResult
{
    public const double WattsPerHp = 745.7;

    /// <param name="limiter">Engine speed the rev limiter allows; the peak figures are those the engine can reach</param>
    public DynoResult(double displacement, double compression, IReadOnlyList<(double Rpm, double Torque)> curve, double limiter = double.MaxValue)
    {
        Displacement = displacement;
        Compression = compression;
        Curve = curve;

        foreach (var (rpm, torque) in curve)
        {
            if (rpm > limiter) break;

            if (torque > MaxTorque) (MaxTorque, MaxTorqueRpm) = (torque, rpm);

            var power = Power(rpm, torque);
            if (power > MaxPowerHp) (MaxPowerHp, MaxPowerRpm) = (power, rpm);
        }
    }

    /// <summary>Cubic metres</summary>
    public double Displacement { get; }

    /// <summary>Static compression ratio, x:1</summary>
    public double Compression { get; }

    /// <summary>Net torque at the flywheel in Nm, at wide open throttle</summary>
    public IReadOnlyList<(double Rpm, double Torque)> Curve { get; }

    public double MaxTorque { get; }
    public double MaxTorqueRpm { get; }
    public double MaxPowerHp { get; }
    public double MaxPowerRpm { get; }

    public double TorqueAt(double rpm)
    {
        if (Curve.Count == 0) return 0;
        if (rpm <= Curve[0].Rpm) return Curve[0].Torque * Math.Max(0, rpm / Curve[0].Rpm);

        for (var i = 1; i < Curve.Count; i++)
        {
            if (rpm > Curve[i].Rpm) continue;

            var (left, right) = (Curve[i - 1], Curve[i]);
            return left.Torque + (right.Torque - left.Torque) * (rpm - left.Rpm) / (right.Rpm - left.Rpm);
        }

        return 0;
    }

    public double PowerHpAt(double rpm) => Power(rpm, TorqueAt(rpm));

    private static double Power(double rpm, double torque) => torque * rpm * Math.PI / 30.0 / WattsPerHp;
}

/// <summary>
/// The engine simulation: from what the parts add up to (<see cref="DynoInputs"/>)
/// to a torque curve. A mean value model, one operating point per speed: how much air the cylinders trap through
/// their valves in the time they have, what the fuel system can match, and the share of the heat the ideal cycle
/// turns into work at this compression.
///
/// The energy side follows the parts' own figures: the fuels' heating values only add up to the power the original
/// game showed when the full ideal air cycle efficiency is applied and nothing is taken off for friction (builds
/// held back by their fuel system land within 5% that way). Breathing is fitted to the builds with a known
/// power in engine_builds.json; tools/EngineBench "rated" shows how the model does against them.
/// </summary>
public static class EngineDyno
{
    /// <summary>Fastest an engine the dyno runs may turn: past any engine the game could hold</summary>
    public const double MaxRpm = 30_000;

    /// <summary>Horsepower per kilowatt, the one figure for both the dyno and a car's quoted "150kW" (<see cref="Cars.AcSpecs"/>)</summary>
    public const double HpPerKw = 1000 / DynoResult.WattsPerHp;

    /// <summary>A compression ratio at or below this is none: the dyno runs no engine with it</summary>
    public const double MinCompression = 1.5;

    private const double RpmStep = 250;
    private const double AirDensity = 1.184;        // kg/m3 at 25 C
    private const double Atmosphere = 1.0;          // bar
    private const double HeatCapacityRatio = 1.4;

    // First order filling: how fast a cylinder fills and empties (1/s at full breathing), and how breathing
    // figures below 1 weigh in. Fitted.
    private const double IntakeRate = 680;
    private const double ExhaustRate = 565;
    private const double FlowExponent = 1.42;

    // Share of the charge displaced by burnt gas that did not get out. Fitted.
    private const double ResidualPenalty = 0.6;

    // Long valve timing costs cylinder filling at low speed: charge pushed back out before the valve shuts
    private const double ReversionRpm = 1800;
    private const double ReversionGain = 1.2;
    private const double ReversionBase = 0.15;

    // A burn that is not centred wastes work; degrees of spark error for the work to drop to zero
    private const double PhasingWidth = 140;

    // Boost in the specs' units to bar of manifold pressure
    private const double BoostScale = 0.25;

    /// <summary>
    /// Past the float speed the valves bounce and the torque falls away over this share of it (a third of it left a tenth
    /// past, all but none a fifth past, where the curve ends)
    /// </summary>
    public const double FloatWidth = 0.1;

    /// <summary>How far past the float speed, in widths, the curve runs: the engine's top</summary>
    private const double FloatWidthsToTop = 2;

    public static DynoResult Run(DynoInputs inputs)
    {
        var displacement = CylinderVolume(inputs) * inputs.Cylinders;
        var compression = Compression(inputs);

        var curve = new List<(double, double)>();
        var maxRpm = TopRpm(inputs);
        if (Unrunnable(inputs) != null)
            return new DynoResult(double.IsFinite(displacement) ? Math.Max(0, displacement) : 0, double.IsFinite(compression) ? compression : 1, curve);

        for (var rpm = RpmStep; rpm <= maxRpm + 1; rpm += RpmStep)
        {
            // A point the model cannot work out (a figure of the parts that is not a number) makes no torque
            var torque = Torque(inputs, rpm, displacement, compression);
            curve.Add((rpm, double.IsFinite(torque) ? Math.Max(0, torque) : 0));
        }

        // The figures people quote are those the engine reaches: under its ignition cut, and short of hurting itself
        var reaches = Math.Min(inputs.RpmLimit > 0 && double.IsFinite(inputs.RpmLimit) ? inputs.RpmLimit : double.MaxValue, RedlineRpm(inputs));
        return new DynoResult(displacement, compression, curve, reaches);
    }

    private static double Torque(DynoInputs inputs, double rpm, double displacement, double compression)
    {
        var cycleTime = 120.0 / rpm;

        // Breathing: the intake fills the cylinder for as long as it is open, the exhaust clears it
        var intakeDuration = Span(inputs.IntakeOpen, inputs.IntakeClose);
        var exhaustDuration = Span(inputs.ExhaustOpen, inputs.ExhaustClose);
        var filling = 1 - Math.Exp(-IntakeRate * Flow(inputs.IntakeMax) * intakeDuration * cycleTime);
        var residual = Math.Exp(-ExhaustRate * Flow(inputs.ExhaustMax) * exhaustDuration * cycleTime);

        // A valve open much longer than the stroke lets charge back out while the piston is slow
        var excess = Math.Max(0, intakeDuration - 0.25) / 0.25;
        var reversion = 1 - Math.Min(0.6, ReversionGain * (ReversionBase + excess) * Math.Exp(-rpm / ReversionRpm));

        var volumetric = filling * (1 - ResidualPenalty * residual) * reversion;

        var pressure = Atmosphere + BoostScale * Boost(inputs, rpm) + ChargersBoost(inputs, rpm);
        var airPerSecond = AirDensity * pressure / Atmosphere * displacement * volumetric / cycleTime;
        if (inputs.MaxAirFlow > 0) airPerSecond = Math.Min(airPerSecond, inputs.MaxAirFlow);

        // Fuel follows air at the set ratio as far as the fuel system goes; the air beyond that burns nothing
        var ratio = Math.Max(1, inputs.MixtureRatio);
        var fuelPerSecond = airPerSecond / ratio;
        if (inputs.MaxFuelFlow > 0) fuelPerSecond = Math.Min(fuelPerSecond, inputs.MaxFuelFlow);
        var mixturePerSecond = fuelPerSecond * (1 + ratio);

        var ideal = 1 - Math.Pow(compression, 1 - HeatCapacityRatio);
        var wallLoss = Math.Clamp(1 - inputs.HeatLoss / 100.0 * cycleTime * 0.25, 0.5, 1);
        var power = mixturePerSecond * inputs.MixtureHeat * ideal * wallLoss * Phasing(inputs, rpm) * Floating(inputs, rpm);

        return power / (rpm * Math.PI / 30.0);
    }

    /// <summary>Swept volume of one cylinder, m3</summary>
    public static double CylinderVolume(DynoInputs inputs) => Math.PI / 4 * inputs.Bore * inputs.Bore * inputs.Stroke;

    /// <summary>Static compression ratio, x:1; 1 (none) without a clearance volume</summary>
    public static double Compression(DynoInputs inputs) =>
        inputs.ClearanceVolume > 0 ? (CylinderVolume(inputs) + inputs.ClearanceVolume) / inputs.ClearanceVolume : 1;

    /// <summary>
    /// The speed the dyno runs up to, as far as the engine can turn: a fifth past where its valves float, else what its
    /// weakest part survives, else a quarter past its rev limit
    /// </summary>
    public static double TopRpm(DynoInputs inputs) =>
        inputs.FloatRpm > 0 ? inputs.FloatRpm * (1 + FloatWidth * FloatWidthsToTop)
        : inputs.MaxRpm > 0 ? inputs.MaxRpm
        : inputs.RpmLimit * 1.25;

    /// <summary>
    /// Where the engine starts to hurt itself: what its weakest rotating part survives, or a tenth past where its valves
    /// float (they meet the pistons), whichever comes first; <see cref="double.MaxValue"/> when nothing says
    /// </summary>
    public static double RedlineRpm(DynoInputs inputs) =>
        Math.Min(inputs.MaxRpm > 0 ? inputs.MaxRpm : double.MaxValue, inputs.FloatRpm > 0 ? inputs.FloatRpm * (1 + FloatWidth) : double.MaxValue);

    /// <summary>What of the torque is left with the valves floating: all of it up to the float speed, then falling away</summary>
    private static double Floating(DynoInputs inputs, double rpm)
    {
        if (inputs.FloatRpm <= 0 || rpm <= inputs.FloatRpm) return 1;
        var past = (rpm - inputs.FloatRpm) / (inputs.FloatRpm * FloatWidth);
        return Math.Exp(-past * past);
    }

    /// <summary>The player's words for an engine nothing feeds: said by the evaluator and by the figures alike</summary>
    public const string NotFedKey = "engine.problem.not_fed";

    /// <summary>Why the dyno runs no curve for these inputs, in the player's words; null when it runs one</summary>
    public static string? Unrunnable(DynoInputs inputs)
    {
        var displacement = CylinderVolume(inputs) * inputs.Cylinders;
        var compression = Compression(inputs);
        var maxRpm = TopRpm(inputs);

        // The inputs are a pack's figures: NaN fails every comparison, so it is ruled out by name; a bore or a stroke of
        // nothing or less is no engine either, whatever its square comes to
        if (!double.IsFinite(displacement) || displacement <= 0 || inputs.Cylinders <= 0 || inputs.Bore <= 0 || inputs.Stroke <= 0)
            return PartWords.Say("engine.problem.no_displacement");
        if (!double.IsFinite(compression) || compression <= MinCompression)
            return PartWords.Say("engine.problem.no_compression", ("compression", Figure(double.IsFinite(compression) ? compression : 0, "0.0")));
        if (!double.IsFinite(maxRpm) || maxRpm <= 0)
            return PartWords.Say("engine.problem.no_rev_range");
        if (maxRpm > MaxRpm)
            return PartWords.Say("engine.problem.revs_past_any", ("rpm", Figure(maxRpm, "0")), ("most", Figure(MaxRpm, "0")));
        return null;
    }

    /// <summary>A figure as the player's lines take it: the same digits in every locale (a "3,5:1" is no compression)</summary>
    public static string Figure(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Length of a valve event as a fraction of the cycle; events may wrap around the end of the cycle</summary>
    public static double Span(double open, double close)
    {
        var span = close - open;
        return span < 0 ? span + 1 : span;
    }

    private static double Flow(double breathing) => Math.Pow(Math.Clamp(breathing, 0, 1), FlowExponent);

    /// <summary>
    /// Best work comes with the burn centred a little after top dead centre, which takes a spark lead of about
    /// half the burn. Too early fights the piston, too late wastes expansion.
    /// </summary>
    private static double Phasing(DynoInputs inputs, double rpm)
    {
        var progress = inputs.SparkRpm1 > inputs.SparkRpm0
            ? Math.Clamp((rpm - inputs.SparkRpm0) / (inputs.SparkRpm1 - inputs.SparkRpm0), 0, 1)
            : 0;
        var spark = inputs.SparkMin + inputs.SparkInc * progress;

        var leadDegrees = (0.5 - spark) * 720;
        var burnDegrees = inputs.BurnTime * rpm * 6;
        var bestLead = burnDegrees * 0.5 + 8;

        var error = (leadDegrees - bestLead) / PhasingWidth;
        return Math.Clamp(1 - error * error, 0.5, 1);
    }

    /// <summary>Boost of the chargers the parts' figures bring at this speed, bar: theirs adds up, as in Assetto Corsa</summary>
    public static double ChargersBoost(DynoInputs inputs, double rpm)
    {
        var boost = 0.0;
        foreach (var charger in inputs.Chargers) boost += charger.BoostAt(rpm);
        return boost;
    }

    /// <summary>Boost of the specs' charger at this speed, bar of manifold pressure</summary>
    public static double SpecsBoostBar(DynoInputs inputs, double rpm) => BoostScale * Boost(inputs, rpm);

    /// <summary>
    /// Chargers spin with the engine; boost peaks where they work best and the wastegate caps it. A blower's
    /// specs give its working band (engine speeds) multiplied by the square of its drive ratio
    /// (<see cref="EngineAssembly"/>), so the speed it is compared with is the engine's times that square: a roots
    /// blower geared 4:1 with its band at 900-4400 rpm makes its boost there, not at 10,000.
    /// </summary>
    public static double Boost(DynoInputs inputs, double rpm)
    {
        if (inputs.BoostMax <= 0 || inputs.TurboRpmRange <= 0) return 0;

        var multiplier = Math.Max(1, inputs.TurboRpmMultiplier);
        var chargerRpm = rpm * multiplier * multiplier;
        var offset = (chargerRpm - inputs.TurboRpmOptimum) / inputs.TurboRpmRange;
        var boost = inputs.BoostMax * Math.Exp(-0.5 * offset * offset);
        return inputs.BoostWastegate > 0 ? Math.Min(boost, inputs.BoostWastegate) : boost;
    }
}
