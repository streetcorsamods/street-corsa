using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// What a car costs new: the body, and every part on it. The body is
/// priced by its weight, the lighter the dearer: a car is a racer by its power to weight, and the power is in the price of
/// its engine, priced by what it makes (<see cref="PartPricing.EngineNewPrice"/>). A heavy car with a tired engine is a cheap car,
/// whatever it cost when it was new.
///
/// Plain BCL and linked into EngineBench: the figures come from <see cref="Source"/> (the game's rules), the core's
/// when none is given.
/// </summary>
public static class CarPricing
{
    /// <summary>
    /// The body's figures: <see cref="BodyPrice"/> at <see cref="BodyKg"/>, more or less as the car's weight stands against
    /// that to the power of <see cref="WeightPower"/>, within <see cref="MinBody"/> and <see cref="MaxBody"/>; a car whose
    /// weight is not known (or not believable: outside <see cref="MinPlausibleKg"/> to <see cref="MaxPlausibleKg"/>) at
    /// <see cref="BodyPrice"/>
    /// </summary>
    public readonly record struct Figures(double BodyPrice, double BodyKg, double WeightPower, double MinBody, double MaxBody,
        double MinPlausibleKg, double MaxPlausibleKg);

    /// <summary>The core's figures, the rules' defaults</summary>
    public static Figures Defaults { get; } = new(BodyPrice: 2500, BodyKg: 1400, WeightPower: 1.5, MinBody: 500, MaxBody: 50_000,
        MinPlausibleKg: 300, MaxPlausibleKg: 5000);

    /// <summary>Where the figures come from, asked each time; null: <see cref="Defaults"/></summary>
    public static Func<Figures>? Source { get; set; }

    private static Figures Now => Source?.Invoke() ?? Defaults;

    /// <summary>What the bare body of a car of this weight costs, in the game's dollars</summary>
    public static double BodyPrice(double? weightKg)
    {
        var figures = Now;
        var price = Sane(figures.BodyPrice, Defaults.BodyPrice);
        if (weightKg is not { } weight || !double.IsFinite(weight) || weight < figures.MinPlausibleKg || weight > figures.MaxPlausibleKg)
            return price;

        var scaled = price * Math.Pow(Sane(figures.BodyKg, Defaults.BodyKg) / weight, double.IsFinite(figures.WeightPower) ? figures.WeightPower : 0);
        var low = Math.Max(0, Sane(figures.MinBody, 0));
        return double.IsFinite(scaled) ? Math.Clamp(scaled, low, Math.Max(low, Sane(figures.MaxBody, price))) : price;
    }

    /// <summary>A car new: its body, its engine (<see cref="PartPricing.EngineNewPrice"/>) and its running gear's new prices</summary>
    public readonly record struct NewCar(double Body, double Engine, double RunningGear)
    {
        public double Total => Body + Engine + RunningGear;
    }

    /// <summary>
    /// What a car of <paramref name="weightKg"/> costs new with the factory engine <paramref name="build"/> and the
    /// factory running gear <paramref name="runningGear"/>. A car without a build (one that races on its AC engine) has
    /// its engine priced by the power its maker gives, <paramref name="acPowerHp"/>, as a build making it would be; with
    /// neither, no engine.
    /// </summary>
    public static NewCar New(PartsCatalog catalog, double? weightKg, RatedBuild? build, IEnumerable<PartDefinition> runningGear,
        double? acPowerHp = null) => new(
        BodyPrice(weightKg),
        build != null ? PartPricing.EngineNewPrice(PartsOf(catalog, build), build.PowerHp)
            : acPowerHp is > 0 ? PartPricing.EngineNewPrice([], acPowerHp) : 0,
        runningGear.Sum(PartPricing.NewPrice));

    /// <summary>The parts of a factory engine the catalog has</summary>
    public static List<PartDefinition> PartsOf(PartsCatalog catalog, RatedBuild build) =>
        build.Build.Parts.Select(catalog.Get).OfType<PartDefinition>().ToList();

    private static double Sane(double value, double otherwise) => double.IsFinite(value) && value > 0 ? value : otherwise;
}
