using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// A free spot a loose part can go on: a slot of a mounted part, or of the car itself when
/// <see cref="Parent"/> is null. <see cref="OwnSlot"/> is the slot of the loose part that meets it.
/// </summary>
public sealed record MountPlace(PartInstance? Parent, int ParentSlot, int OwnSlot);

/// <summary>
/// Taking parts off a car and putting them on. A part comes off with everything that is mounted on it and
/// goes back on the same way; what fits where is the catalog's say (<see cref="PartsCatalog.CanMate"/>).
/// </summary>
public static class Workbench
{
    /// <param name="carParts">The parts mounted on the car itself (Car.Parts)</param>
    public static List<MountPlace> FindPlaces(PartsCatalog catalog, List<PartInstance> carParts, PartInstance loose)
    {
        var places = new List<MountPlace>();
        if (catalog.Get(loose.DefinitionId) is not { } looseDefinition) return places;

        // An engine block goes in the engine bay and nowhere else
        if (PartKinds.IsBlock(looseDefinition))
        {
            if (carParts.All(p => p.ParentSlot != PartInstance.CarEngineSlot)) places.Add(new MountPlace(null, PartInstance.CarEngineSlot, 0));
            return places;
        }

        var looseGroup = PartKinds.GroupOf(looseDefinition);
        var looseSlots = looseDefinition.Slots.Where(s => loose.Children.All(c => c.ParentSlot != s.Id)).ToList();
        var mountsBy = looseSlots.FirstOrDefault(s => s.Id == 1)?.Id ?? looseSlots.FirstOrDefault()?.Id ?? 0;

        // The running gear goes on the car's own wheel slots: a rim, a brake, a spring or a shock on every free
        // corner. A tyre goes on a mounted rim it fits (same diameter, a rim width the tyre takes).
        if (looseGroup == PartKinds.Tyres)
        {
            foreach (var rim in RunningGear.Rims(carParts))
            {
                if (rim.Children.Any(c => c.ParentSlot == RunningGear.TyreSlotOnRim)) continue;
                if (catalog.Get(rim.DefinitionId) is { } rimDefinition && RunningGear.TyreFitsRim(looseDefinition, rimDefinition))
                    places.Add(new MountPlace(rim, RunningGear.TyreSlotOnRim, mountsBy));
            }

            return places;
        }

        if (RunningGear.IsRunningGear(looseGroup))
        {
            foreach (var carSlot in RunningGear.CarSlotsFor(looseGroup))
            {
                if (carParts.All(p => p.ParentSlot != carSlot)) places.Add(new MountPlace(null, carSlot, mountsBy));
            }

            return places;
        }

        // A rear axle housing goes under the car on its axle slot; what goes on a housing finds it as on any part
        if (looseGroup == PartKinds.RearAxles)
        {
            if (carParts.All(p => p.ParentSlot != RearAxle.CarSlot)) places.Add(new MountPlace(null, RearAxle.CarSlot, mountsBy));
            return places;
        }

        places.AddRange(PlacesOn(catalog, carParts, loose, looseDefinition, looseGroup, looseSlots));
        return places;
    }

    /// <summary>Where a loose part could go on the engine on the stand. A block goes in a car, never on another engine.</summary>
    public static List<MountPlace> FindPlacesOnStand(PartsCatalog catalog, PartInstance engine, PartInstance loose)
    {
        if (catalog.Get(loose.DefinitionId) is not { } looseDefinition || PartKinds.IsBlock(looseDefinition)) return new();

        var looseGroup = PartKinds.GroupOf(looseDefinition);
        if (looseGroup == PartKinds.Tyres || RunningGear.IsRunningGear(looseGroup) || RearAxle.IsAxleGroup(looseGroup)) return new();

        var looseSlots = looseDefinition.Slots.Where(s => loose.Children.All(c => c.ParentSlot != s.Id)).ToList();
        return PlacesOn(catalog, new[] { engine }, loose, looseDefinition, looseGroup, looseSlots).ToList();
    }

    /// <summary>
    /// The place is on the engine, so the work order (<see cref="EngineWork.WhyNotOn"/>) has a say in it; a place on
    /// the car's own slots or its running gear has no rules
    /// </summary>
    /// <param name="engineParts">The engine's parts, the block itself among them</param>
    public static bool IsOnEngine(IReadOnlySet<PartInstance> engineParts, MountPlace place) =>
        place.Parent != null && engineParts.Contains(place.Parent);

    private static IEnumerable<MountPlace> PlacesOn(PartsCatalog catalog, IEnumerable<PartInstance> roots, PartInstance loose,
        PartDefinition looseDefinition, string looseGroup, List<PartSlot> looseSlots)
    {
        foreach (var (parent, parentDefinition, slot) in FreeSlots(catalog, roots))
        {
            if (PartKinds.NeverStacks(looseGroup) && PartKinds.GroupOf(parentDefinition) == looseGroup) continue;

            var own = looseSlots.FirstOrDefault(s => catalog.CanMate(looseDefinition, s, parentDefinition, slot));
            if (own != null) yield return new MountPlace(parent, slot.Id, own.Id);
        }
    }

    /// <summary>
    /// The slots of the parts that nothing is on and that a loose part could go on: not the slot a part hangs by, nor
    /// the running gear's (a wheel takes its tyre by <see cref="FindPlaces"/>' own rules)
    /// </summary>
    internal static IEnumerable<(PartInstance Parent, PartDefinition Definition, PartSlot Slot)> FreeSlots(PartsCatalog catalog, IEnumerable<PartInstance> roots)
    {
        foreach (var parent in roots.SelectMany(p => p.SelfAndDescendants()))
        {
            if (catalog.Get(parent.DefinitionId) is not { } definition) continue;
            // A brake's own slots are free for what goes on it (a drum's shoes); the rest of the running gear takes nothing
            var group = PartKinds.GroupOf(definition);
            if (RunningGear.IsRunningGear(group) && group != PartKinds.Brakes) continue;

            foreach (var slot in definition.Slots)
            {
                if (slot.Id != parent.OwnSlot && parent.Children.All(c => c.ParentSlot != slot.Id)) yield return (parent, definition, slot);
            }
        }
    }

    public static void Mount(List<PartInstance> carParts, MountPlace place, PartInstance loose)
    {
        loose.ParentSlot = place.ParentSlot;
        loose.OwnSlot = place.OwnSlot;
        (place.Parent?.Children ?? carParts).Add(loose);
    }

    /// <summary>Takes a part off, with whatever is on it. False when the part is not on the car.</summary>
    public static bool Remove(List<PartInstance> carParts, PartInstance part)
    {
        var siblings = carParts.Contains(part)
            ? carParts
            : carParts.Select(root => PartTrees.FindParent(root, part)).FirstOrDefault(parent => parent != null)?.Children;
        if (siblings == null) return false;

        siblings.Remove(part);
        part.ParentSlot = 0;
        part.OwnSlot = 0;
        return true;
    }
}
