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
/// carrier and an axle shaft each side. Parts of data: what each does is a figure in its pack's <c>specs</c>
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

    /// <summary>
    /// The housing's slots for what a live axle carries at the rear corners (the owner's "diferencial": the whole rear axle
    /// with its brakes, springs and shocks): each side's brake, spring and shock, and the rear sway bar. A car on a live
    /// axle carries its rear running gear on it, but for its leaf springs: the axle hangs on those, they stay on the car's
    /// own slots (<see cref="Carries"/>); a car with an independent rear keeps all of it on the car. The export, the
    /// condition and the garage find it in either place (<see cref="RunningGear.Mounted"/>).
    /// </summary>
    public const int LeftBrakeSlot = 5, RightBrakeSlot = 6, LeftSpringSlot = 7, RightSpringSlot = 8, LeftShockSlot = 9, RightShockSlot = 10, SwayBarSlot = 11;

    /// <summary>The housing's slot that stands for one of the car's own rear corner slots (a rear brake, spring or shock, the rear sway bar); null for another</summary>
    public static int? HousingSlotFor(int carSlot) => carSlot switch
    {
        _ when carSlot == RunningGear.BrakeSlot(2) => LeftBrakeSlot,
        _ when carSlot == RunningGear.BrakeSlot(3) => RightBrakeSlot,
        _ when carSlot == RunningGear.SpringSlot(2) => LeftSpringSlot,
        _ when carSlot == RunningGear.SpringSlot(3) => RightSpringSlot,
        _ when carSlot == RunningGear.ShockSlot(2) => LeftShockSlot,
        _ when carSlot == RunningGear.ShockSlot(3) => RightShockSlot,
        RunningGear.RearSwayBarSlot => SwayBarSlot,
        _ => null
    };

    /// <summary>The car's own slot a housing's slot stands for; null for a slot of the axle's own (the carrier, the shafts)</summary>
    public static int? CarSlotFor(int housingSlot) => housingSlot switch
    {
        LeftBrakeSlot => RunningGear.BrakeSlot(2),
        RightBrakeSlot => RunningGear.BrakeSlot(3),
        LeftSpringSlot => RunningGear.SpringSlot(2),
        RightSpringSlot => RunningGear.SpringSlot(3),
        LeftShockSlot => RunningGear.ShockSlot(2),
        RightShockSlot => RunningGear.ShockSlot(3),
        SwayBarSlot => RunningGear.RearSwayBarSlot,
        _ => null
    };

    /// <summary>
    /// Whether the rear axle carries a part that goes on one of the car's own slots: only on a live axle (the car's data's
    /// rear suspension is <c>AXLE</c>), and never a leaf spring, which the axle hangs on
    /// </summary>
    public static bool Carries(PartDefinition part, int carSlot, bool liveAxle) =>
        liveAxle && HousingSlotFor(carSlot) != null && !(carSlot == RunningGear.SpringSlot(RunningGear.CornerOf(carSlot)) && RunningGear.IsLeafSpring(part));

    /// <summary>
    /// The housing on the car's axle slot that has the slot a part on a car slot goes on instead (<see cref="Carries"/>), and
    /// that slot; null when the part stays on the car's own
    /// </summary>
    public static (PartInstance Housing, int Slot)? HousingFor(PartsCatalog catalog, List<PartInstance> carParts, int carSlot, PartDefinition part, bool liveAxle)
    {
        if (!Carries(part, carSlot, liveAxle) || HousingSlotFor(carSlot) is not { } slot || carParts.FirstOrDefault(p => p.ParentSlot == CarSlot) is not { } housing)
            return null;
        return catalog.Get(housing.DefinitionId)?.Slots.Any(s => s.Id == slot) == true ? (housing, slot) : null;
    }

    /// <summary>The parts on the housing that stand for the car's rear corner slots, each with the car slot it stands for</summary>
    public static IEnumerable<(PartInstance Part, int CarSlot)> CornerParts(List<PartInstance> carParts) =>
        carParts.FirstOrDefault(p => p.ParentSlot == CarSlot)?.Children
            .Select(c => (Part: c, CarSlot: CarSlotFor(c.ParentSlot)))
            .Where(c => c.CarSlot != null)
            .Select(c => (c.Part, c.CarSlot!.Value)) ?? [];

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

    /// <summary>
    /// How hard a carrier locks, as one figure to price it by: an open one 0.3, a posi about 1, a spool 2.3; a carrier may say
    /// its own (<c>price_lock</c>: a welded open one is priced near the open one it was, though it locks like a spool)
    /// </summary>
    public static double LockFigure(PartDefinition carrier)
    {
        if (carrier.Number("price_lock") is > 0 and var own && double.IsFinite(own)) return own;

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
