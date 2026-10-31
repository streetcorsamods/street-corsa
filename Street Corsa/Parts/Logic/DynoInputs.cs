namespace StreetCorsa.Parts.Logic;

/// <summary>
/// Everything the engine's parts hand to the engine simulation, as the block collects them (<see cref="EngineAssembly"/>):
/// SI units, valve and spark events as fractions of the 720 degree cycle.
/// </summary>
public sealed record DynoInputs
{
    public int Cylinders { get; init; }

    /// <summary>Metres</summary>
    public double Bore { get; init; }

    /// <summary>Metres</summary>
    public double Stroke { get; init; }

    /// <summary>Volume left above the piston at top dead centre, cubic metres per cylinder</summary>
    public double ClearanceVolume { get; init; }

    /// <summary>How well a cylinder breathes through its valves at low and at full lift, 0..1</summary>
    public double IntakeMin { get; init; }
    public double IntakeMax { get; init; }
    public double ExhaustMin { get; init; }
    public double ExhaustMax { get; init; }

    /// <summary>Valve events as a fraction of the 720 degree cycle</summary>
    public double IntakeOpen { get; init; }
    public double IntakeClose { get; init; }
    public double ExhaustOpen { get; init; }
    public double ExhaustClose { get; init; }

    /// <summary>Spark as a fraction of the cycle (0.5 = top dead centre): from Min, moving by Inc between Rpm0 and Rpm1</summary>
    public double SparkMin { get; init; }
    public double SparkInc { get; init; }
    public double SparkRpm0 { get; init; }
    public double SparkRpm1 { get; init; }

    /// <summary>Seconds the mixture takes to burn</summary>
    public double BurnTime { get; init; }

    /// <summary>Where the ignition cuts; 0 for an engine without a cut, which revs as far as its valves let it</summary>
    public double RpmLimit { get; init; }

    /// <summary>Highest speed the weakest rotating part survives: past it the engine hurts itself; 0 or less for none</summary>
    public double MaxRpm { get; init; }

    /// <summary>
    /// Where the valves stop following the cam (the lowest <c>max_rpm</c> of the cam and heads): past it the torque falls
    /// away, and the curve ends where it is all but gone; 0 or less for a valvetrain that never floats
    /// </summary>
    public double FloatRpm { get; init; }

    /// <summary>Heat lost to the cylinder walls, percent per second</summary>
    public double HeatLoss { get; init; }

    /// <summary>Air to fuel, by mass</summary>
    public double MixtureRatio { get; init; }

    /// <summary>Heat released per kg of mixture, J</summary>
    public double MixtureHeat { get; init; }

    /// <summary>What the fuel system and the air filter can deliver, kg/s; 0 = no limit</summary>
    public double MaxFuelFlow { get; init; }
    public double MaxAirFlow { get; init; }

    /// <summary>Forced induction: peak boost in bar over atmosphere and where the charger works best</summary>
    public double BoostMax { get; init; }
    public double BoostWastegate { get; init; }
    public double TurboRpmMultiplier { get; init; }
    public double TurboRpmOptimum { get; init; }
    public double TurboRpmRange { get; init; }

    /// <summary>The specs' charger is a turbo (lag and a gauge in AC), not a blower</summary>
    public bool ChargerTurbo { get; init; }

    /// <summary>Chargers the parts' figures bring (<see cref="PartFigures"/>), on top of the specs' one; their boost adds up</summary>
    public IReadOnlyList<DynoCharger> Chargers { get; init; } = Array.Empty<DynoCharger>();
}
