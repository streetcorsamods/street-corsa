using StreetCorsa.Parts.Export;

namespace StreetCorsa.Parts.Cars;

/// <summary>What the guess knows of a car beyond its data: its make (makes.json), the make's family and country, its model year</summary>
public sealed record CarFacts(string? Make, string? Family, int? Year, string? Country = null)
{
    public static CarFacts None { get; } = new(null, null, null);
}

/// <summary>The rear axle a car leaves the factory with: housing, carrier, ring and pinion, and the shaft for each side</summary>
public sealed record RearAxleChoice(PartDefinition Housing, PartDefinition Carrier, PartDefinition Gears, PartDefinition Shaft);

/// <summary>
/// A car's factory running gear where cars.json picks none (step 31): from the parts' own <c>"came_in"</c> (the makes,
/// families, engine torque and years they left the factory in) against the car's make, year and its own data (the
/// engine's peak torque, <c>FINAL</c>, <c>[DIFFERENTIAL]</c>). Deterministic: the same car always gets the same parts,
/// since they are also what the export measures the car's parts against.
/// </summary>
public static class RunningGearGuess
{
    /// <summary>The final drive taken for a car whose data gives none</summary>
    public const double TypicalRatio = 3.55;

    /// <summary>Nm of engine torque a model year off a part's years weighs as much as</summary>
    private const double NmPerYear = 15;

    /// <returns>The axle, null for a car whose rear wheels are not driven (the differential is in its gearbox) or a catalog without a whole axle for it</returns>
    public static RearAxleChoice? ChooseAxle(PartsCatalog catalog, AcCarSpecs specs, CarFacts car, RunningGearPicks? picks)
    {
        if (!specs.DrivesRear) return null;

        var housings = ByGroup(catalog, PartKinds.RearAxles);
        var housing = Picked(catalog, picks?.Axle, PartKinds.RearAxles) ?? Best(housings, car, specs.PeakTorque);
        if (housing == null) return null;

        var carrierSlot = housing.Slots.FirstOrDefault(s => s.Id == RearAxle.CarrierSlot);
        var shaftSlot = housing.Slots.FirstOrDefault(s => s.Id == RearAxle.LeftShaftSlot);
        if (carrierSlot == null || shaftSlot == null) return null;

        // Every carrier that goes in, with the gear sets that go on each
        var carriers = Mountable(catalog, housing, carrierSlot, PartKinds.Differentials)
            .Select(c => (Carrier: c, Gears: c.Slots.FirstOrDefault(s => s.Id == RearAxle.GearsSlot) is { } slot ? Mountable(catalog, c, slot, PartKinds.RingPinions) : []))
            .Where(c => c.Gears.Count > 0).ToList();
        if (carriers.Count == 0) return null;

        var target = picks?.Ratio ?? (specs.FinalRatio > 0 ? specs.FinalRatio : TypicalRatio);
        var pickedCarrier = Picked(catalog, picks?.Diff, PartKinds.Differentials);
        var choices = carriers.Where(c => c.Carrier.Id == pickedCarrier?.Id).ToList();
        if (choices.Count == 0) choices = carriers.Where(c => Fits(c.Carrier, car)).ToList();
        if (choices.Count == 0) choices = carriers;

        // The ratio first (it is the car's own figure), then the carrier that locks most like the car's own
        var (power, coast, _) = specs.Differential;
        var best = choices
            .SelectMany(c => c.Gears.Where(g => g.CameIn != null || c.Gears.All(o => o.CameIn == null)).Select(g => (c.Carrier, Gears: g)))
            .OrderBy(c => Math.Round(Math.Abs(RearAxle.Ratio(c.Gears) - target), 3))
            .ThenBy(c => Math.Abs(RearAxle.Lock(c.Carrier).Power - power) + Math.Abs(RearAxle.Lock(c.Carrier).Coast - coast))
            .ThenBy(c => RearAxle.Strength(c.Carrier))
            .ThenBy(c => c.Carrier.Id, StringComparer.Ordinal).ThenBy(c => c.Gears.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (best.Carrier == null) return null;

        var shafts = Mountable(catalog, housing, shaftSlot, PartKinds.AxleShafts);
        var shaft = Picked(catalog, picks?.Shafts, PartKinds.AxleShafts) is { } pickedShaft && shafts.Any(s => s.Id == pickedShaft.Id)
            ? pickedShaft
            : Best(shafts, car, specs.PeakTorque);
        return shaft == null ? null : new RearAxleChoice(housing, best.Carrier, best.Gears, shaft);
    }

    /// <summary>
    /// The part that came in this car most nearly: one naming its make before one naming its family, before one for any
    /// car, before one naming other makes; one of the car's years before one that was not made then; then the engine
    /// torque and how far off its years are; then the plainest (the least it holds). Parts no car came with are only taken
    /// when there is nothing else.
    /// </summary>
    public static PartDefinition? Best(IReadOnlyList<PartDefinition> parts, CarFacts car, double torque)
    {
        var factory = parts.Where(p => p.CameIn != null).ToList();
        return (factory.Count > 0 ? factory : parts)
            .OrderBy(p => Rank(p.CameIn, car))
            .ThenBy(p => p.CameIn?.YearsOutside(car.Year) > 0)
            .ThenBy(p => p.CameIn is { } came ? came.TorqueOutside(torque > 0 ? torque : null) + came.YearsOutside(car.Year) * NmPerYear : 0)
            .ThenBy(RearAxle.Strength)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>0 a part naming the car's make, 1 its family, 2 one for any car, 3 one naming other makes, 4 one no car came with</summary>
    internal static int Rank(CameIn? came, CarFacts car) => came switch
    {
        null => 4,
        _ when came.NamesMake(car.Make) => 0,
        _ when came.NamesFamily(car.Family) => 1,
        { IsForMakes: false } => 2,
        _ => 3
    };

    /// <summary>Whether a part could have come in the car: it came in some car, and none of other makes, in its years</summary>
    private static bool Fits(PartDefinition part, CarFacts car) =>
        part.CameIn is { } came && Rank(came, car) <= 2 && came.YearsOutside(car.Year) == 0;

    private static PartDefinition? Picked(PartsCatalog catalog, string? id, string group) =>
        string.IsNullOrWhiteSpace(id) || catalog.Get(id) is not { } part || PartKinds.GroupOf(part) != group ? null : part;

    private static List<PartDefinition> Mountable(PartsCatalog catalog, PartDefinition parent, PartSlot slot, string group) =>
        catalog.FindMountable(parent, slot).Select(m => m.Part).Where(p => PartKinds.GroupOf(p) == group)
            .DistinctBy(p => p.Id, StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

    private static List<PartDefinition> ByGroup(PartsCatalog catalog, string group) =>
        catalog.Parts.Values.Where(p => PartKinds.GroupOf(p) == group).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
}
