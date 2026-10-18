using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// What a racer does to a car's rear axle once the engine makes the power for it (step 31): a carrier that locks (a posi)
/// in place of an open one, then shorter gears, the next ratio its carrier takes, up to <see cref="MaxRatio"/>. Gears and a
/// posi make no power on the dyno, so they are not <see cref="EngineTuner"/>'s to weigh: they go by the engine's power
/// (<see cref="PosiPower"/>, <see cref="GearsPower"/>). One part at a time, only one that goes on the axle as it is: a posi
/// that takes the gears it has, gears its carrier takes.
/// </summary>
public static class AxleTuner
{
    /// <summary>An engine of this many horsepower and more is worth a carrier that locks</summary>
    public static double PosiPower => EngineRules.Now.Tuner.PosiPower;

    /// <summary>An engine of this many horsepower and more, behind a carrier that locks, is worth shorter gears</summary>
    public static double GearsPower => EngineRules.Now.Tuner.GearsPower;

    /// <summary>The shortest gears a racer puts in a car that goes on the street</summary>
    public static double MaxRatio => EngineRules.Now.Tuner.MaxRatio;

    /// <summary>
    /// The part a racer would put on the car's rear axle next, behind an engine of <paramref name="powerHp"/>: the cheapest
    /// posi that takes the gears, else the next shorter gears; null when the axle wants nothing, or nothing fits the budget
    /// </summary>
    public static PartDefinition? NextPart(PartsCatalog catalog, List<PartInstance> carParts, double powerHp, decimal budget, Func<PartDefinition, decimal> price)
    {
        var axle = RearAxle.Mounted(carParts);
        if (axle.Housing == null || axle.Carrier == null || axle.Gears == null
            || catalog.Get(axle.Housing.DefinitionId) is not { } housing || catalog.Get(axle.Carrier.DefinitionId) is not { } carrier
            || catalog.Get(axle.Gears.DefinitionId) is not { } gears)
            return null;

        if (!Locks(carrier))
        {
            if (powerHp < PosiPower) return null;
            return Mountable(catalog, housing, RearAxle.CarrierSlot, PartKinds.Differentials)
                .Where(IsPosi)
                .Where(c => Takes(catalog, c, gears) && price(c) <= budget)
                .OrderBy(price).ThenBy(c => c.Id, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        if (powerHp < GearsPower) return null;
        var ratio = RearAxle.Ratio(gears);
        return Mountable(catalog, carrier, RearAxle.GearsSlot, PartKinds.RingPinions)
            .Where(g => RearAxle.Ratio(g) > ratio + 0.01 && RearAxle.Ratio(g) <= MaxRatio + 0.005 && price(g) <= budget)
            .OrderBy(RearAxle.Ratio).ThenBy(price).ThenBy(g => g.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Puts the part on the car's rear axle in place of the one it replaces: a carrier with the old one's gears moved over,
    /// or the gears on the carrier. Returns what came off, loose (a carrier without its gears); null when it did not go on.
    /// </summary>
    /// <param name="wear">The new part's condition: 1 bought new</param>
    public static PartInstance? Fit(PartsCatalog catalog, List<PartInstance> carParts, PartDefinition part, double wear = 1)
    {
        var axle = RearAxle.Mounted(carParts);
        if (axle.Housing == null || axle.Carrier == null) return null;

        var fresh = new PartInstance(part.Id) { OwnSlot = 1, Wear = Math.Clamp(wear, 0, 1) };
        if (PartKinds.GroupOf(part) == PartKinds.Differentials)
        {
            var old = axle.Carrier;
            axle.Housing.Children.Remove(old);
            fresh.ParentSlot = RearAxle.CarrierSlot;
            if (axle.Gears != null)
            {
                old.Children.Remove(axle.Gears);
                fresh.Children.Add(axle.Gears);
            }

            axle.Housing.Children.Add(fresh);
            return Loose(old);
        }

        if (axle.Gears is not { } oldGears) return null;
        axle.Carrier.Children.Remove(oldGears);
        fresh.ParentSlot = RearAxle.GearsSlot;
        axle.Carrier.Children.Add(fresh);
        return Loose(oldGears);
    }

    /// <summary>
    /// A car that has been around, worked on to <paramref name="level"/> (0 to 1): its axle given what a racer would have
    /// put in it behind an engine of <paramref name="powerHp"/>, now and then, part by part (a posi, then a step or two of
    /// gears), worn like the car. Returns the parts that went on.
    /// </summary>
    public static List<PartDefinition> TuneUsed(PartsCatalog catalog, List<PartInstance> carParts, double powerHp, double level, double condition, Random random)
    {
        var fitted = new List<PartDefinition>();
        for (var step = 0; step < 3 && random.NextDouble() < Math.Clamp(level, 0, 1); step++)
        {
            if (NextPart(catalog, carParts, powerHp, decimal.MaxValue, _ => 0m) is not { } part || Fit(catalog, carParts, part, condition) == null) break;
            fitted.Add(part);
        }

        return fitted;
    }

    /// <summary>A carrier that locks under power at all: a posi, a locker, a spool, a welded one</summary>
    private static bool Locks(PartDefinition carrier) => RearAxle.Lock(carrier).Power > 0;

    /// <summary>A carrier that locks some and slips some: not a spool, nor a welded open one</summary>
    private static bool IsPosi(PartDefinition carrier)
    {
        var (power, coast, _) = RearAxle.Lock(carrier);
        return power > 0 && !(power >= 1 && coast >= 1);
    }

    private static bool Takes(PartsCatalog catalog, PartDefinition carrier, PartDefinition gears) =>
        Mountable(catalog, carrier, RearAxle.GearsSlot, PartKinds.RingPinions).Any(g => g.Id.Equals(gears.Id, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<PartDefinition> Mountable(PartsCatalog catalog, PartDefinition parent, int slotId, string group) =>
        catalog.MountableOf(parent, slotId, group).Where(p => p.IsScripted);

    private static PartInstance Loose(PartInstance part)
    {
        part.ParentSlot = 0;
        part.OwnSlot = 0;
        return part;
    }
}
