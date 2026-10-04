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

    /// <summary>
    /// A used car's rust and worn paint as it comes into the game, at the worst condition (they shrink with a better one,
    /// rolled between <see cref="RustRollLow"/> and <see cref="RustRollHigh"/> of it for rust, <see cref="PaintRollLow"/>
    /// and <see cref="PaintRollHigh"/> for paint): a dealer's lot cleans its cars up, a private car comes as its owner
    /// left it (<see cref="Parts.Cars.CarLooks.Roll"/>)
    /// </summary>
    public double DealerRust { get; set; } = 0.35;

    public double PrivateRust { get; set; } = 0.8;

    public double DealerPaintWear { get; set; } = 0.6;

    public double PrivatePaintWear { get; set; } = 1.1;

    /// <summary>How far either way from its figure a used car's rust is rolled, and its worn paint</summary>
    public double RustRollLow { get; set; } = 0.3;

    public double RustRollHigh { get; set; } = 1.2;

    public double PaintRollLow { get; set; } = 0.4;

    public double PaintRollHigh { get; set; } = 1.2;

    /// <summary>The chance a used car comes with a cracked windscreen, at the worst condition</summary>
    public double DealerCrackChance { get; set; } = 0.05;

    public double PrivateCrackChance { get; set; } = 0.35;

    /// <summary>How cracked a used car's windscreen comes (0..1), rolled between these</summary>
    public double CrackRollLow { get; set; } = 0.2;

    public double CrackRollHigh { get; set; } = 0.8;

    /// <summary>The dirt a used car comes with: up to this on a dealer's washed car; between these on a private one</summary>
    public double DealerDirt { get; set; } = 0.08;

    public double PrivateDirtMin { get; set; } = 0.15;

    public double PrivateDirtMax { get; set; } = 0.6;

    /// <summary>
    /// Rust a day: this times the rust there is plus <see cref="RustStart"/> times the worn paint (a sound body with sound
    /// paint does not start to rust). A car with a third of its body rusted gains about a tenth more in two months.
    /// </summary>
    public double RustPerDay { get; set; } = 0.0015;

    public double RustStart { get; set; } = 0.1;

    /// <summary>A front zone hit this hard (km/h) cracks the windscreen; one this hard cracks it right across</summary>
    public double CrackFromKmh { get; set; } = 25;

    public double ShatteredKmh { get; set; } = 70;

    /// <summary>A spring bent past this (its condition, 0..1) lets its corner sag, by up to <see cref="MostSag"/> metres broken</summary>
    public double SagFrom { get; set; } = 0.6;

    public double MostSag { get; set; } = 0.05;

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
