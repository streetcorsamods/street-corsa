namespace StreetCorsa.Parts.Cars;

/// <summary>
/// The figures the engines are worked by (<c>"engines"</c> in the rules): how they are cooled and oiled
/// (<see cref="EngineCooling"/>), how the cars on the lots were worked on (<see cref="EngineFactory"/>), how the rivals'
/// tuners work (<see cref="EngineTuner"/>), and how an AC car's engine is matched to a build
/// (<see cref="StockEngineMatcher"/>).
///
/// Linked into EngineBench, which has no rules: <see cref="Source"/> is pointed at the career's rules by the game, and
/// <see cref="ProgramSource"/> at the installed mods' rules; null there, which leaves the base game's figures.
/// </summary>
public sealed class EngineRules
{
    /// <summary>The rules in play; null: the base game's figures</summary>
    public static Func<EngineRules>? Source { get; set; }

    /// <summary>
    /// The installed mods' rules, not a career's: for what is worked out into the program's own catalog (the factory
    /// engine an AC car is matched to); null: the base game's figures
    /// </summary>
    public static Func<EngineRules>? ProgramSource { get; set; }

    private static readonly EngineRules BaseGame = new();

    /// <summary>The figures in play</summary>
    public static EngineRules Now => Source?.Invoke() ?? BaseGame;

    /// <summary>The installed mods' figures, whatever career is in play</summary>
    public static EngineRules Program => ProgramSource?.Invoke() ?? BaseGame;

    public EngineCoolingRules Cooling { get; set; } = new();

    public EngineFactoryRules Factory { get; set; } = new();

    public EngineTunerRules Tuner { get; set; } = new();

    public EngineMatchRules Matcher { get; set; } = new();
}

/// <summary>How well an engine is cooled and oiled, by its parts (<see cref="EngineCooling"/>)</summary>
public sealed class EngineCoolingRules
{
    /// <summary>A performance radiator carries off this much more than a factory one</summary>
    public double PerformanceRadiator { get; set; } = 1.35;

    /// <summary>A performance water pump moves this much more coolant</summary>
    public double PerformancePump { get; set; } = 1.1;

    /// <summary>What an engine without its radiator is left to cool it with, of what it makes</summary>
    public double NoRadiator { get; set; } = 0.15;

    /// <summary>What a radiator still carries off with no water pump to push the coolant through it</summary>
    public double NoPump { get; set; } = 0.25;

    /// <summary>How much of the cooling is done standing still: no fan where the engine had one, a stock fan, a performance one</summary>
    public double NoFan { get; set; } = 0.15;

    public double StockFan { get; set; } = 0.35;

    public double PerformanceFan { get; set; } = 0.5;

    /// <summary>A radiator with an electric fan of its own keeps up best standing still</summary>
    public double ElectricFan { get; set; } = 0.55;

    /// <summary>The g a stock oil pan holds its oil to; a deep or race pan, longer</summary>
    public double StockSumpG { get; set; } = 1.05;

    public double RaceSumpG { get; set; } = 1.25;

    /// <summary>A race pan this big (litres) holds its oil a little longer: to <see cref="BigSumpG"/></summary>
    public double BigSumpLitres { get; set; } = 6.5;

    public double BigSumpG { get; set; } = 1.3;

    /// <summary>No oil pan at all: the pickup is in the air under any g</summary>
    public double NoSumpG { get; set; } = 0.5;
}

/// <summary>How the cars on the lots were worked on before they got there (<see cref="EngineFactory"/>)</summary>
public sealed class EngineFactoryRules
{
    /// <summary>How far a part's wear strays either way from the car's condition</summary>
    public double WearNoise { get; set; } = 0.08;

    /// <summary>A part put on later is this much less worn than the rest</summary>
    public double NewerPartBonus { get; set; } = 0.15;

    /// <summary>The chance, at the most worked-on, that the engine was swapped for a bigger one of the family</summary>
    public double EngineSwapChance { get; set; } = 0.25;

    /// <summary>A swapped-in engine makes this many times the stock one's power, least to most</summary>
    public double SwapMinPower { get; set; } = 1.05;

    public double SwapMaxPower { get; set; } = 1.5;

    /// <summary>Bolt-ons never take an engine past this many times its stock power</summary>
    public double MaxPowerOverStock { get; set; } = 1.6;

    /// <summary>A bolt-on gains at least this share of power (1.005: half a percent), or is a dearer part</summary>
    public double MinPowerGain { get; set; } = 1.005;

    /// <summary>Most bolt-on parts changed, at the most worked-on</summary>
    public int MaxBoltOns { get; set; } = 4;

    /// <summary>Tries at finding each bolt-on that works out</summary>
    public int AttemptsPerBoltOn { get; set; } = 6;
}

/// <summary>How the rivals' tuners work on an engine (<see cref="EngineTuner"/>)</summary>
public sealed class EngineTunerRules
{
    /// <summary>An upgrade gains at least this share of power, or it is not worth the money</summary>
    public double MinGain { get; set; } = 0.03;

    /// <summary>How deep a part that no longer fits is replaced by the cheapest that does</summary>
    public int MaxReplacementDepth { get; set; } = 5;

    /// <summary>A swapped-in engine makes no more than this times the power of the one it replaces: a bigger engine of the family, not a race engine</summary>
    public double MaxSwapPower { get; set; } = 1.6;

    /// <summary>How worn an engine out of the paper is</summary>
    public double UsedEngineCondition { get; set; } = 0.75;

    /// <summary>Cooled this well, an engine keeps its temperature at full throttle</summary>
    public double CoolEnough { get; set; } = 1.0;

    /// <summary>Cooled less than this, an engine goes flat at full throttle: a tuner does not make that upgrade</summary>
    public double RunsHotBelow { get; set; } = 0.85;

    /// <summary>An engine of this many horsepower and more is worth a carrier that locks (<see cref="AxleTuner"/>)</summary>
    public double PosiPower { get; set; } = 300;

    /// <summary>An engine of this many horsepower and more, behind a carrier that locks, is worth shorter gears</summary>
    public double GearsPower { get; set; } = 380;

    /// <summary>The shortest gears a racer puts in a car that goes on the street</summary>
    public double MaxRatio { get; set; } = 4.11;

    /// <summary>A used car with a worked-on engine had its rear axle seen to as well, part by part, this often (<see cref="AxleTuner.TuneUsed"/>)</summary>
    public double UsedAxleChance { get; set; } = 0.6;
}

/// <summary>How an AC car's engine is matched to an engine build (<see cref="StockEngineMatcher"/>): points for each likeness</summary>
public sealed class EngineMatchRules
{
    /// <summary>The build's family is the car's, or another</summary>
    public double SameFamily { get; set; } = 3.0;

    public double OtherFamily { get; set; } = -3.0;

    /// <summary>A number both names hold (a 427 is a 427), a word both hold</summary>
    public double SharedNumber { get; set; } = 2.0;

    public double SharedWord { get; set; } = 1.0;

    /// <summary>Taken off for each step of the power ratio's log away from what the car's data claims</summary>
    public double PowerWeight { get; set; } = 4.0;

    /// <summary>A build the source game made for a car over a kit or a note</summary>
    public double FactoryBuildBonus { get; set; } = 0.2;
}
