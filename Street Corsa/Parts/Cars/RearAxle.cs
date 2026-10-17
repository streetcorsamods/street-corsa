using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>What a car's rear axle carries: the housing on the car's axle slot, its carrier with the gears on it, its shafts</summary>
public sealed record RearAxleParts(PartInstance? Housing, PartInstance? Carrier, PartInstance? Gears, PartInstance? LeftShaft, PartInstance? RightShaft)
{
    public static RearAxleParts None { get; } = new(null, null, null, null, null);

    /// <summary>The parts there are, housing first</summary>
    public IEnumerable<PartInstance> All => new[] { Housing, Carrier, Gears, LeftShaft, RightShaft }.OfType<PartInstance>();
}

/// <summary>
/// The rear axle (step 31): a housing on the car's own axle slot, a differential carrier on it, the ring and pinion on the
/// carrier and an axle shaft each side. Parts of data: what each does is a figure in its pack's <c>properties</c>
/// (<c>strength</c> in Nm at the axle, <c>ratio</c>, <c>diff_power</c>, <c>diff_coast</c>, <c>diff_preload</c> in Nm,
/// <c>drag</c> the share of the engine's torque the housing's gears take). They mate by standard fittings
/// (<c>axle:&lt;family&gt;</c>, <c>gears:&lt;family&gt;</c>, <c>shaft:&lt;family&gt;</c>), so any mod's axle and carrier meet.
/// The axle takes the final drive and the differential over from the gearbox (<see cref="Export.AcAxleData"/>).
/// </summary>
public static class RearAxle
{
    /// <summary>The car's own slot the housing goes on, beside the wheels' 101-314</summary>
    public const int CarSlot = 401;

    public const int CarrierSlot = 2;
    public const int LeftShaftSlot = 3;
    public const int RightShaftSlot = 4;

    /// <summary>The carrier's slot for the ring and pinion</summary>
    public const int GearsSlot = 2;

    /// <summary>Whether a group is one of the axle's (housing, carrier, gears, shafts)</summary>
    public static bool IsAxleGroup(string group) =>
        group is PartKinds.RearAxles or PartKinds.Differentials or PartKinds.RingPinions or PartKinds.AxleShafts;

    /// <summary>The axle's parts on a car, by their slots</summary>
    public static RearAxleParts Mounted(List<PartInstance> carParts)
    {
        var housing = carParts.FirstOrDefault(p => p.ParentSlot == CarSlot);
        if (housing == null) return RearAxleParts.None;

        var carrier = housing.Children.FirstOrDefault(c => c.ParentSlot == CarrierSlot);
        return new RearAxleParts(housing, carrier, carrier?.Children.FirstOrDefault(c => c.ParentSlot == GearsSlot),
            housing.Children.FirstOrDefault(c => c.ParentSlot == LeftShaftSlot), housing.Children.FirstOrDefault(c => c.ParentSlot == RightShaftSlot));
    }

    /// <summary>Nm at the axle the part holds when new</summary>
    public static double Strength(PartDefinition part) => part.Number("strength");

    /// <summary>The ring and pinion's ratio, the car's final drive</summary>
    public static double Ratio(PartDefinition gears) => gears.Number("ratio");

    /// <summary>Assetto Corsa's differential figures: lock under power and on the overrun (both 1: a spool), and preload, Nm</summary>
    public static (double Power, double Coast, double Preload) Lock(PartDefinition carrier) =>
        (carrier.Number("diff_power"), carrier.Number("diff_coast"), carrier.Number("diff_preload"));

    /// <summary>How hard a carrier locks, as one figure to price it by: an open one 0.3, a posi about 1, a spool 2.3</summary>
    public static double LockFigure(PartDefinition carrier)
    {
        var (power, coast, preload) = Lock(carrier);
        return 0.3 + Math.Clamp(power, 0, 1) + Math.Clamp(coast, 0, 1) + Math.Clamp(preload, 0, 500) / 200;
    }

    /// <summary>Share of the engine's torque the housing's gears take (a 9-inch's pinion support bearing: a few percent)</summary>
    public static double Drag(PartDefinition housing) => Math.Clamp(housing.Number("drag"), 0, 0.2);

    /// <summary>What a part holds as it is: a worn one a little less, a damaged one much less</summary>
    public static double StrengthNow(PartDefinition part, PartInstance instance) =>
        Strength(part) * (0.75 + 0.25 * Math.Clamp(instance.Wear, 0, 1)) * (0.3 + 0.7 * Math.Clamp(instance.Tear, 0, 1));

    /// <summary>The part of the axle that gives first, and what it holds; null with nothing on the axle that says</summary>
    public static (PartInstance Part, double Strength)? Weakest(PartsCatalog catalog, RearAxleParts axle)
    {
        (PartInstance Part, double Strength)? weakest = null;
        foreach (var part in axle.All)
        {
            if (catalog.Get(part.DefinitionId) is not { } definition || Strength(definition) <= 0) continue;
            var strength = StrengthNow(definition, part);
            if (weakest == null || strength < weakest.Value.Strength) weakest = (part, strength);
        }

        return weakest;
    }
}
