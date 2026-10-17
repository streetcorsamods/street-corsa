using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// The order a live rear axle and its leaf springs go on and come off: the axle sits on its leaves (it hangs on them; they
/// are bolted to the car), so on a car sprung on leaves the axle goes on once both rear leaves are on, and a leaf comes off
/// once the axle is off. A coil-sprung axle carries its springs (<see cref="RearAxle.Carries"/>) and has no such order.
/// </summary>
public static class AxleWork
{
    /// <summary>
    /// Whether a car's rear is sprung on leaves: a live axle with a leaf spring on one of the car's rear spring slots, or
    /// whose factory rear spring is a leaf
    /// </summary>
    public static bool LeafSprung(PartsCatalog catalog, List<PartInstance> carParts, bool liveAxle, PartDefinition? factoryRearSpring) =>
        liveAxle && ((factoryRearSpring != null && RunningGear.IsLeafSpring(factoryRearSpring)) || RearLeaves(catalog, carParts).Any());

    /// <summary>Why a part on the car cannot come off now (a leaf spring under the rear axle), in the player's words; null when it can</summary>
    public static string? WhyNotOff(PartsCatalog catalog, List<PartInstance> carParts, PartInstance part) =>
        RearLeaves(catalog, carParts).Contains(part) && carParts.Any(p => p.ParentSlot == RearAxle.CarSlot)
            ? PartWords.Say("workbench.axle.on_leaves")
            : null;

    /// <summary>Why a loose part cannot go on the car now (a rear axle with its leaves not both on), in the player's words; null when it can</summary>
    public static string? WhyNotOn(PartsCatalog catalog, List<PartInstance> carParts, PartInstance loose, bool leafSprung) =>
        leafSprung && catalog.Get(loose.DefinitionId) is { } definition && PartKinds.GroupOf(definition) == PartKinds.RearAxles
        && RearLeaves(catalog, carParts).Count() < 2
            ? PartWords.Say("workbench.axle.needs_leaves")
            : null;

    /// <summary>The leaf springs on the car's own rear spring slots</summary>
    private static IEnumerable<PartInstance> RearLeaves(PartsCatalog catalog, List<PartInstance> carParts) =>
        carParts.Where(p => (p.ParentSlot == RunningGear.SpringSlot(2) || p.ParentSlot == RunningGear.SpringSlot(3))
                            && catalog.Get(p.DefinitionId) is { } definition && RunningGear.IsLeafSpring(definition));
}
