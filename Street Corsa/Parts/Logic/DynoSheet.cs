namespace StreetCorsa.Parts.Logic;

/// <summary>
/// What an engine's parts hand the dyno, filled in as the block goes over them (<see cref="EngineAssembly"/>):
/// the working copy behind <see cref="DynoInputs"/>, with what the parts check against besides (the compression the
/// fuel takes). Each bank of a vee is filled on a copy and the engine is the average of its banks. A figure added here
/// goes into <see cref="BeAverageOf"/> and <see cref="ToInputs"/> too (OwnPartLogicReviewTests checks both).
/// </summary>
internal sealed class DynoSheet
{
    /// <summary>No part on the engine is that fragile: the curve runs as far as the weakest part allows</summary>
    public const double NoMaxRpm = -1;

    public int Cylinders;
    public double Bore = 0.01;
    public double Stroke = 0.01;
    public double ClearanceVolume = 100_000;
    public double IntakeMin = 1, IntakeMax = 1, ExhaustMin = 1, ExhaustMax = 1;
    public double IntakeOpen = 0.001, IntakeClose = 0.002, ExhaustOpen = 0.003, ExhaustClose = 0.004;
    public double SparkMin = 0.5, SparkInc, SparkRpm0, SparkRpm1 = 10_000;
    public double BurnTime = 100_000;
    public double RpmLimit = 10_000;
    public double HeatLoss = 100;
    public double ChargerRpmMultiplier, ChargerRpmOptimum, ChargerRpmRange, BoostMax, BoostWastegate;
    public double MixtureRatio = 14;
    public double MixtureHeat = 1_000_000;
    public double MaxFuelFlow, MaxAirFlow;
    public double MaxRpm = NoMaxRpm;

    /// <summary>The static compression the fuel system's mixture takes, x:1; 0 when nothing feeds the engine</summary>
    public double MinCompression, MaxCompression;

    public DynoSheet Clone() => (DynoSheet)MemberwiseClone();

    /// <summary>
    /// The engine of two banks: each figure halfway between them, the cylinders and the fuel's limits as they were. The
    /// top speed and the most fuel and air are limits, not figures: the engine's is the tighter of the banks', and a
    /// bank without one (<see cref="NoMaxRpm"/>, 0 flow) leaves the other's standing, as the weakest part sets the top
    /// speed of the whole (a V8 with one head short of <c>max_rpm</c> revs as far as the other head allows, not half
    /// way to nowhere)
    /// </summary>
    public void BeAverageOf(DynoSheet one, DynoSheet other)
    {
        static double Mid(double a, double b) => (a + b) * 0.5;

        // The tighter of two limits; one bank's alone where the other has none
        static double Tighter(double a, double b, double none) => a <= none ? b : b <= none ? a : Math.Min(a, b);

        Bore = Mid(one.Bore, other.Bore);
        Stroke = Mid(one.Stroke, other.Stroke);
        ClearanceVolume = Mid(one.ClearanceVolume, other.ClearanceVolume);
        IntakeMin = Mid(one.IntakeMin, other.IntakeMin);
        ExhaustMin = Mid(one.ExhaustMin, other.ExhaustMin);
        IntakeMax = Mid(one.IntakeMax, other.IntakeMax);
        ExhaustMax = Mid(one.ExhaustMax, other.ExhaustMax);
        IntakeOpen = Mid(one.IntakeOpen, other.IntakeOpen);
        IntakeClose = Mid(one.IntakeClose, other.IntakeClose);
        ExhaustOpen = Mid(one.ExhaustOpen, other.ExhaustOpen);
        ExhaustClose = Mid(one.ExhaustClose, other.ExhaustClose);
        SparkMin = Mid(one.SparkMin, other.SparkMin);
        SparkInc = Mid(one.SparkInc, other.SparkInc);
        SparkRpm0 = Mid(one.SparkRpm0, other.SparkRpm0);
        SparkRpm1 = Mid(one.SparkRpm1, other.SparkRpm1);
        BurnTime = Mid(one.BurnTime, other.BurnTime);
        RpmLimit = Mid(one.RpmLimit, other.RpmLimit);
        HeatLoss = Mid(one.HeatLoss, other.HeatLoss);
        ChargerRpmMultiplier = Mid(one.ChargerRpmMultiplier, other.ChargerRpmMultiplier);
        ChargerRpmOptimum = Mid(one.ChargerRpmOptimum, other.ChargerRpmOptimum);
        ChargerRpmRange = Mid(one.ChargerRpmRange, other.ChargerRpmRange);
        BoostMax = Mid(one.BoostMax, other.BoostMax);
        BoostWastegate = Mid(one.BoostWastegate, other.BoostWastegate);
        MixtureRatio = Mid(one.MixtureRatio, other.MixtureRatio);
        MixtureHeat = Mid(one.MixtureHeat, other.MixtureHeat);
        MaxFuelFlow = Tighter(one.MaxFuelFlow, other.MaxFuelFlow, 0);
        MaxAirFlow = Tighter(one.MaxAirFlow, other.MaxAirFlow, 0);
        MaxRpm = Tighter(one.MaxRpm, other.MaxRpm, NoMaxRpm);
    }

    public DynoInputs ToInputs() => new()
    {
        Cylinders = Cylinders,
        Bore = Bore,
        Stroke = Stroke,
        ClearanceVolume = ClearanceVolume,
        IntakeMin = IntakeMin,
        IntakeMax = IntakeMax,
        ExhaustMin = ExhaustMin,
        ExhaustMax = ExhaustMax,
        IntakeOpen = IntakeOpen,
        IntakeClose = IntakeClose,
        ExhaustOpen = ExhaustOpen,
        ExhaustClose = ExhaustClose,
        SparkMin = SparkMin,
        SparkInc = SparkInc,
        SparkRpm0 = SparkRpm0,
        SparkRpm1 = SparkRpm1,
        BurnTime = BurnTime,
        RpmLimit = RpmLimit,
        MaxRpm = MaxRpm,
        HeatLoss = HeatLoss,
        MixtureRatio = MixtureRatio,
        MixtureHeat = MixtureHeat,
        MaxFuelFlow = MaxFuelFlow,
        MaxAirFlow = MaxAirFlow,
        BoostMax = BoostMax,
        BoostWastegate = BoostWastegate,
        TurboRpmMultiplier = ChargerRpmMultiplier,
        TurboRpmOptimum = ChargerRpmOptimum,
        TurboRpmRange = ChargerRpmRange
    };
}
