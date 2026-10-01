namespace StreetCorsa.Rules;

/// <summary>
/// How cars wear and fail (<c>"condition"</c> in the rules): damage, mileage, dirt, fuel and oil, and what a race takes
/// out of them
/// </summary>
public sealed class ConditionRules
{
    /// <summary>A body that has taken this much, all zones together, is a write-off (a 200 km/h wreck reports 205 on two zones)</summary>
    public double TotaledKmh { get; set; } = 200;

    /// <summary>AC's life of a new engine</summary>
    public double NewEngineLife { get; set; } = 1000;

    /// <summary>A gearbox or a corner in worse shape than this does not go racing</summary>
    public double BrokenBelow { get; set; } = 0.1;

    /// <summary>
    /// Wear from plain driving, per km, on the engine and the gearbox. Small: what a race does to a car is AC's damage;
    /// a drag race costs a few thousandths of a percent.
    /// </summary>
    public double MileageWearPerKm { get; set; } = 1.0 / 40000;

    /// <summary>Dirt a body picks up per km of racing, on top of what AC gives it (on dirt, in the wet)</summary>
    public double DirtPerKm { get; set; } = 0.01;

    /// <summary>Less fuel than this, in litres, and the tank is dry</summary>
    public double EmptyTankLitres { get; set; } = 0.25;

    /// <summary>Less fuel than this, in litres, and the car does not go racing: it would run dry within the first minute</summary>
    public double TooLittleToRaceLitres { get; set; } = 2;

    /// <summary>The report says the tank is low below this share of it</summary>
    public double LowTankShare { get; set; } = 0.15;

    /// <summary>
    /// Oil an engine in good shape burns, share of a full sump: at every start (the drive there, the warm-up) and per km
    /// of racing. A worn engine burns up to <see cref="WornOilBurn"/> times as much: rings and valve guides let it by.
    /// </summary>
    public double OilBurnPerRace { get; set; } = 0.004;

    public double OilBurnPerKm { get; set; } = 0.004;

    public double WornOilBurn { get; set; } = 5;

    /// <summary>The report says the oil is low below this, and the attendant would top it up</summary>
    public double LowOil { get; set; } = 0.75;

    /// <summary>What a sump holds its oil to at 0 oil, share of what it holds full: a low sump surges sooner</summary>
    public double DrySumpShare { get; set; } = 0.4;

    /// <summary>The share of the failed part's loss the rest of the rotating assembly takes with it</summary>
    public double SharedEngineDamage { get; set; } = 0.35;

    /// <summary>
    /// What one race takes off the engine, the transmission, the tyres and the body (share of their health), for race
    /// results that do not say how the car came back (from before they did)
    /// </summary>
    public double EngineWearPerRace { get; set; } = 0.01;

    public double TransmissionWearPerRace { get; set; } = 0.005;

    public double TyreWearPerRace { get; set; } = 0.02;

    public double BodyWearPerRace { get; set; } = 0.001;

    /// <summary>What a crash takes off on top, for the same race results</summary>
    public double CrashEngineWear { get; set; } = 0.05;

    public double CrashTransmissionWear { get; set; } = 0.03;

    public double CrashBodyWear { get; set; } = 0.10;

    public double CrashTyreWear { get; set; } = 0.05;
}
