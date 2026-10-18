using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts;

/// <summary>
/// What a car shows at a corner whose wheel is off (<see cref="ShownParts.MissingWheels"/>): the car's own wheel goes out of
/// sight there, and its brake and suspension parts show in its place, the drum on its plate, the hub, the shock, a coil,
/// what the rear axle carries on that side. Placed as the garage's parts view places them (<see cref="CarPartsLayout"/>),
/// by the car's wheel hubs as its model has them, and shown whatever the car's meshes (<see cref="ShownModelAt.Always"/>).
/// </summary>
public static class ShownCornerParts
{
    /// <param name="missing">The corners whose wheel is off, bit i for corner i</param>
    public static List<ShownModelAt> Of(PartsCatalog catalog, List<PartInstance> carParts, CarAnchors? hubs, int missing)
    {
        var shown = new List<ShownModelAt>();
        if (missing == 0 || hubs == null) return shown;

        bool Bare(int carSlot) => RunningGear.CornerOf(carSlot) is >= 0 and var corner && (missing & (1 << corner)) != 0;

        var gear = new List<CarPart>();
        var wanted = new HashSet<InstalledPart>(ReferenceEqualityComparer.Instance);
        foreach (var root in carParts)
        {
            var axle = root.ParentSlot == RearAxle.CarSlot;
            if (!axle && (!Bare(root.ParentSlot) || RunningGear.IsWheelSlot(root.ParentSlot))) continue;
            if (PartTrees.ToInstalled(catalog, root) is not { } tree) continue;

            gear.Add(new CarPart(root.ParentSlot, tree.Root));
            if (!axle)
            {
                wanted.UnionWith(tree.Root.SelfAndDescendants());
                continue;
            }

            // On the rear axle only what it carries on a bare side, not the axle itself
            foreach (var (slot, child) in tree.Root.Children)
                if (RearAxle.CarSlotFor(slot) is { } carSlot && Bare(carSlot)) wanted.UnionWith(child.SelfAndDescendants());
        }

        if (wanted.Count == 0) return shown;

        foreach (var placed in CarPartsLayout.Build(catalog, hubs, null, gear))
        {
            if (placed.Source is not { } source || !wanted.Contains(source)) continue;
            if (PartModelBounds.Of(catalog, placed.Part) is not { } box || catalog.GetModelPath(placed.Part) is not { } path) continue;
            shown.Add(new ShownModelAt(new ShownModel(ShownParts.FileName(placed.Part.Id), path), placed.World, box.Min, box.Max, Always: true));
        }

        return shown;
    }
}
