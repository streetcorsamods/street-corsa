using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// The running gear a car leaves the factory with: for each axle the catalog's tyre, rim, brake, spring and
/// shock nearest to what the car's own Assetto Corsa data describes. The choice is deterministic, so the same
/// car always gets the same factory parts and what the player mounts can be measured against them.
/// A brake or a spring a part's <c>"came_in"</c> says the car left the factory with (step 31: the period drums, leaf
/// springs, torsion bars) wins over the nearest of the source game's, which stay the choice for a car no such part names.
/// The sway bars are the stock bars nearest to the car's own <c>[ARB]</c>, none on an axle whose data has none.
/// </summary>
public static class RunningGearFactory
{
    /// <summary>
    /// What a car's running gear is brought up to once (<see cref="Car.GearVersion"/>): 1 is step 31, the rear axle and the
    /// period brakes and springs; 2 the leaf springs, the torsion bars and the sway bars; 3 the rear brakes, springs, shocks
    /// and sway bar carried on the rear axle's housing; 4 the leaf springs and an independent rear's running gear back on the
    /// car's own slots; 5 the front hubs (the drums go onto their backing plates as the save is read:
    /// <see cref="PutDrumsOnPlates"/>)
    /// </summary>
    public const int GearVersion = 5;

    /// <summary>
    /// The factory parts of one axle; a drum brake's backing plate, shoes, wheel cylinder and hub with it, the sway bar
    /// across it (none for an axle without)
    /// </summary>
    public sealed record AxleParts(PartDefinition Tyre, PartDefinition Rim, PartDefinition Brake, PartDefinition Spring, PartDefinition Shock,
        PartDefinition? Shoes = null, PartDefinition? Cylinder = null, PartDefinition? SwayBar = null)
    {
        public PartDefinition? Plate { get; init; }
        public PartDefinition? Hub { get; init; }

        /// <summary>The axle's brake torque, its shoes and wheel cylinder in it</summary>
        public double BrakeTorque => RunningGear.BrakeTorque(Brake, Shoes, Cylinder);

        /// <summary>Every part of it there is</summary>
        public IEnumerable<PartDefinition> All =>
            new[] { Tyre, Rim, Brake, Spring, Shock, Shoes, Cylinder, SwayBar, Plate, Hub }.OfType<PartDefinition>();
    }

    /// <param name="car">The car's make, family, country and year, for the parts' <c>came_in</c>; null chooses among the
    /// source game's parts alone, as before step 31</param>
    /// <param name="picks">cars.json's picks for the car's brakes and springs</param>
    /// <returns>The factory parts of both axles, null when the catalog lacks a kind</returns>
    public static (AxleParts Front, AxleParts Rear)? Choose(PartsCatalog catalog, AcCarSpecs specs, CarFacts? car = null, RunningGearPicks? picks = null)
    {
        var tyres = ByGroup(catalog, PartKinds.Tyres);
        var rims = ByGroup(catalog, PartKinds.Rims);
        var brakes = ByGroup(catalog, PartKinds.Brakes);
        var springs = ByGroup(catalog, PartKinds.Springs);
        var shocks = SourceGame(ByGroup(catalog, PartKinds.Shocks));
        if (tyres.Count == 0 || rims.Count == 0 || brakes.Count == 0 || springs.Count == 0 || shocks.Count == 0) return null;

        // A tyre is only as good as a rim it goes on
        var fitted = tyres.Where(t => rims.Any(r => RunningGear.TyreFitsRim(t, r))).ToList();
        if (fitted.Count == 0) return null;

        // The tyre nearest in width, the plainest compound of that width, then the nearest rolling radius.
        // One tyre for the whole car unless the car is clearly staggered: what a swap does should read the
        // same at both ends.
        PartDefinition Tyre(double width, double radius) => fitted
            .OrderBy(t => Math.Abs(RunningGear.TyreWidth(t) - width))
            .ThenBy(t => RunningGear.TyreGrip(t))
            .ThenBy(t => Math.Abs(RunningGear.TyreRadius(t) - radius))
            .ThenBy(t => t.Id, StringComparer.Ordinal).First();
        var frontTyre = Tyre(specs.Front.TyreWidth, specs.Front.TyreRadius);
        var rearTyre = Math.Abs(specs.Rear.TyreWidth - specs.Front.TyreWidth) < 0.03 ? frontTyre : Tyre(specs.Rear.TyreWidth, specs.Rear.TyreRadius);

        // The source game's own, nearest to the car's figures: what a car no part's came_in names leaves the factory on
        var plainBrakes = SourceGame(brakes);
        var plainSprings = SourceGame(springs);

        AxleParts Axle(AcAxleSpecs axle, PartDefinition tyre, bool front)
        {
            // The rim the plainest that takes the tyre; the rest by the figure nearest to the car's own, which
            // is the smallest the catalog has more often than not: what the shop sells is an upgrade
            var rim = rims.Where(r => RunningGear.TyreFitsRim(tyre, r))
                .OrderBy(r => r.Number("rim_type")).ThenBy(r => Math.Abs(RunningGear.RimOffset(r)))
                .ThenBy(PartPricing.NewPrice).ThenBy(r => r.Id, StringComparer.Ordinal).First();
            var brake = Factory(brakes, front, specs, car, picks?.Brakes(front), b => RunningGear.BrakeTorque(b), axle.BrakeTorque)
                ?? plainBrakes.OrderBy(b => Math.Abs(RunningGear.BrakeTorque(b) - axle.BrakeTorque))
                    .ThenBy(PartPricing.NewPrice).ThenBy(b => b.Id, StringComparer.Ordinal).First();
            var spring = Factory(springs, front, specs, car, picks?.Springs(front), RunningGear.SpringRate, axle.SpringRate)
                ?? plainSprings.OrderBy(s => Math.Abs(RunningGear.SpringRate(s) - axle.SpringRate))
                    .ThenBy(s => Math.Abs(RunningGear.SpringDesignLoad(s) - axle.CornerLoad)).ThenBy(s => s.Id, StringComparer.Ordinal).First();
            var shock = shocks.OrderBy(s => Math.Abs(RunningGear.Damping(s).Bump - axle.DampBump))
                .ThenBy(s => Math.Abs(RunningGear.Damping(s).Rebound - axle.DampRebound)).ThenBy(s => s.Id, StringComparer.Ordinal).First();
            // A drum on its backing plate, the shoes and wheel cylinder there
            var plate = PlateFor(catalog, brake, front);
            return new AxleParts(tyre, rim, brake, spring, shock,
                OnSlot(catalog, plate ?? brake, RunningGear.ShoesSlot, PartKinds.BrakeShoes, car), OnSlot(catalog, plate ?? brake, RunningGear.WheelCylinderSlot, PartKinds.WheelCylinders, car),
                SwayBar(catalog, front, axle.SwayBarRate))
            {
                Plate = plate,
                Hub = OnSlot(catalog, brake, RunningGear.HubSlot, PartKinds.Hubs, car)
            };
        }

        return (Axle(specs.Front, frontTyre, true), Axle(specs.Rear, rearTyre, false));
    }

    /// <summary>
    /// The part of a kind the car left the factory on by the parts' <c>came_in</c>: the one cars.json picks, else one for
    /// this axle and suspension, of the car's country, naming its make, its family or any car, made in its years; a
    /// standard one before a dealer's option; then the car's weight and the figure nearest to its own. Null when no part
    /// says it came in the car: the caller's own way decides.
    /// </summary>
    private static PartDefinition? Factory(List<PartDefinition> parts, bool front, AcCarSpecs specs, CarFacts? car, string? picked,
        Func<PartDefinition, double> figure, double target)
    {
        if (picked != null && parts.FirstOrDefault(p => p.Id.Equals(picked, StringComparison.OrdinalIgnoreCase)) is { } pick) return pick;
        if (car == null) return null;

        var suspension = specs.Axle(front).SuspensionType;
        var fits = parts.Where(p => p.CameIn is { } came && came.FitsAxle(front, suspension) && came.FitsCountry(car.Country)
                                    && RunningGearGuess.Rank(came, car) <= 2 && came.YearsOutside(car.Year) == 0).ToList();
        if (fits.Count == 0) return null;
        if (fits.Any(p => !p.CameIn!.Option)) fits = fits.Where(p => !p.CameIn!.Option).ToList();

        return fits.OrderBy(p => RunningGearGuess.Rank(p.CameIn, car))
            .ThenBy(p => p.CameIn!.MassOutside(specs.TotalMass))
            .ThenBy(p => Math.Abs(figure(p) - target))
            .ThenBy(p => p.Id, StringComparer.Ordinal).First();
    }

    /// <summary>
    /// The factory sway bar of an axle: of the bars for that axle that came in cars (all of them, for a catalog with none
    /// of those) the one nearest to the car's own rate; none for an axle whose data has no bar
    /// </summary>
    private static PartDefinition? SwayBar(PartsCatalog catalog, bool front, double own)
    {
        if (own <= 0 || !double.IsFinite(own)) return null;
        var bars = catalog.Parts.Values.Where(p => RunningGear.IsWorkingSwayBar(p) && RunningGear.FitsAxle(p, front)).ToList();
        if (bars.Any(b => b.CameIn != null)) bars = bars.Where(b => b.CameIn != null).ToList();
        return bars.OrderBy(b => Math.Abs(RunningGear.SwayBarRate(b) - own)).ThenBy(b => b.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>
    /// What goes on a part's slot (a backing plate's shoes and wheel cylinder, a drum's hub): the factory one
    /// of those that fit, else the first; null for a part without the slot
    /// </summary>
    private static PartDefinition? OnSlot(PartsCatalog catalog, PartDefinition part, int slotId, string group, CarFacts? car)
    {
        if (part.Slots.FirstOrDefault(s => s.Id == slotId) is not { } slot) return null;
        return RunningGearGuess.Best(catalog.MountableOf(part, slot, group), car ?? CarFacts.None, 0);
    }

    /// <summary>The backing plate a drum goes on, for this axle; null for a brake that goes on the corner itself (a disc)</summary>
    public static PartDefinition? PlateFor(PartsCatalog catalog, PartDefinition brake, bool front)
    {
        if (brake.Slots.FirstOrDefault(s => s.Id == 1) is not { Fits.Count: > 0 } mount) return null;
        return catalog.Parts.Values
            .Where(p => PartKinds.GroupOf(p) == PartKinds.BackingPlates && RunningGear.FitsAxle(p, front)
                        && p.Slots.Any(s => s.Id == RunningGear.DrumSlot && catalog.CanMate(brake, mount, p, s)))
            .OrderBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>The factory running gear as parts on the car's wheel slots, worn like the car, and its rear axle when given</summary>
    public static List<PartInstance> Create(PartsCatalog catalog, AcCarSpecs specs, double condition, RearAxleChoice? rearAxle = null,
        CarFacts? car = null, RunningGearPicks? picks = null)
    {
        var parts = new List<PartInstance>();
        if (Choose(catalog, specs, car, picks) is not var (front, rear)) return parts;
        if (rearAxle != null) parts.Add(CreateAxle(rearAxle, condition));

        var wear = Math.Clamp(condition, 0, 1);
        for (var corner = 0; corner < RunningGear.Corners; corner++)
        {
            var axle = RunningGear.IsFront(corner) ? front : rear;

            var rim = Instance(axle.Rim, RunningGear.WheelSlot(corner), 1, wear);
            rim.Children.Add(Instance(axle.Tyre, RunningGear.TyreSlotOnRim, 1, wear));
            parts.Add(rim);
            parts.Add(Brake(axle, RunningGear.BrakeSlot(corner), wear));
            parts.Add(Instance(axle.Spring, RunningGear.SpringSlot(corner), 1, wear));
            parts.Add(Instance(axle.Shock, RunningGear.ShockSlot(corner), 1, wear));
        }

        foreach (var (axle, isFront) in new[] { (front, true), (rear, false) })
        {
            if (axle.SwayBar != null) parts.Add(Instance(axle.SwayBar, RunningGear.SwayBarSlot(isFront), 1, wear));
        }

        HangOnAxle(catalog, parts, specs.HasLiveAxle);
        return parts;
    }

    /// <summary>
    /// The axle's brake as it goes on the corner: a drum on its backing plate with the shoes and wheel cylinder, a disc by
    /// itself; the hub on either when it takes one
    /// </summary>
    private static PartInstance Brake(AxleParts axle, int carSlot, double wear)
    {
        var brake = Instance(axle.Brake, axle.Plate != null ? RunningGear.DrumSlot : carSlot, 1, wear);
        if (axle.Hub != null) brake.Children.Add(Instance(axle.Hub, RunningGear.HubSlot, 1, wear));

        var holder = axle.Plate != null ? Instance(axle.Plate, carSlot, 1, wear) : brake;
        if (axle.Shoes != null) holder.Children.Add(Instance(axle.Shoes, RunningGear.ShoesSlot, 1, wear));
        if (axle.Cylinder != null) holder.Children.Add(Instance(axle.Cylinder, RunningGear.WheelCylinderSlot, 1, wear));
        if (axle.Plate != null) holder.Children.Add(brake);
        return holder;
    }

    /// <summary>
    /// The drums a save of before step 31's backing plates has on the corners (on the car's brake slots or the rear axle's),
    /// each with its shoes and wheel cylinder on it, put on the backing plate for it with those on the plate: the shape a
    /// drum brake has had since. A drum no plate takes stays as it is. For a save as it is read, before its trees are
    /// checked against the catalog, which would take the shoes off a drum that has no slot for them.
    /// </summary>
    /// <returns>How many drums went onto a plate</returns>
    public static int PutDrumsOnPlates(PartsCatalog catalog, List<PartInstance> carParts)
    {
        var moved = 0;
        foreach (var (drum, carSlot) in carParts.Select(p => (p, p.ParentSlot)).Concat(RearAxle.CornerParts(carParts)).ToList())
        {
            var corner = RunningGear.CornerOf(carSlot);
            if (corner < 0 || carSlot != RunningGear.BrakeSlot(corner) || catalog.Get(drum.DefinitionId) is not { } definition
                || !PartKinds.Is(definition, "DrumBrake") || PlateFor(catalog, definition, RunningGear.IsFront(corner)) is not { } plateDefinition)
                continue;

            var plate = new PartInstance(plateDefinition.Id) { ParentSlot = drum.ParentSlot, OwnSlot = 1, Wear = drum.Wear };
            Replace(carParts, drum, plate);

            // The shoes and the wheel cylinder go where the plate takes them; anything else stays on the drum
            foreach (var child in drum.Children.ToList())
            {
                if (catalog.Get(child.DefinitionId) is not { } childDefinition || childDefinition.Slots.FirstOrDefault(s => s.Id == child.OwnSlot) is not { } own) continue;
                if (plateDefinition.Slots.FirstOrDefault(s => plate.Children.All(c => c.ParentSlot != s.Id) && catalog.CanMate(childDefinition, own, plateDefinition, s)) is not { } slot)
                    continue;
                drum.Children.Remove(child);
                child.ParentSlot = slot.Id;
                plate.Children.Add(child);
            }

            drum.ParentSlot = RunningGear.DrumSlot;
            drum.OwnSlot = 1;
            plate.Children.Add(drum);
            moved++;
        }

        return moved;
    }

    /// <summary>
    /// What step 31's phase 5 added to a car that had its running gear before (<see cref="GearVersion"/> 5): a hub on each
    /// front drum or disc that takes one, worn like the brake
    /// </summary>
    /// <returns>How many parts were added</returns>
    private static int AddHardware(PartsCatalog catalog, CarFacts car, List<PartInstance> carParts)
    {
        var added = 0;
        var corners = RunningGear.Mounted(carParts, catalog);
        for (var corner = 0; corner < RunningGear.Corners; corner++)
        {
            var at = corners[corner];
            if (at.Brake is { } brake && at.Hub == null && catalog.Get(brake.DefinitionId) is { } brakeDefinition
                && OnSlot(catalog, brakeDefinition, RunningGear.HubSlot, PartKinds.Hubs, car) is { } hub)
            {
                brake.Children.Add(Instance(hub, RunningGear.HubSlot, 1, brake.Wear));
                added++;
            }

        }

        return added;
    }

    /// <summary>A part on the car, or on one of its parts, swapped for another in the same place</summary>
    private static void Replace(List<PartInstance> carParts, PartInstance old, PartInstance fresh)
    {
        var siblings = carParts.Contains(old) ? carParts : carParts.Select(root => PartTrees.FindParent(root, old)).First(p => p != null)!.Children;
        siblings[siblings.IndexOf(old)] = fresh;
    }

    /// <summary>
    /// A car's running gear from before <see cref="GearVersion"/>, brought up to it: a brake or a spring that is what the
    /// factory gave it then (the source game's nearest) becomes what the factory gives it now (a period drum, a leaf
    /// spring, a torsion bar), in the same wear and damage, with what goes on it. What the player put on stays. An axle the
    /// factory gives a sway bar gets it, worn like the axle's springs, and a brake its hub, only when the car's running gear
    /// is from before them: one the player took off since stays off.
    /// </summary>
    /// <param name="fromVersion">The car's <see cref="Car.GearVersion"/> before this</param>
    /// <returns>How many parts changed</returns>
    public static int BringUpToDate(PartsCatalog catalog, AcCarSpecs specs, CarFacts car, RunningGearPicks? picks, List<PartInstance> carParts, int fromVersion = 0)
    {
        if (Choose(catalog, specs) is not var (oldFront, oldRear) || Choose(catalog, specs, car, picks) is not var (front, rear)) return 0;

        var changed = PutDrumsOnPlates(catalog, carParts);
        // On the car's own corner slots, and on the rear axle's that stand for them
        foreach (var (part, carSlot) in carParts.Select(p => (p, p.ParentSlot)).Concat(RearAxle.CornerParts(carParts)).ToList())
        {
            var corner = RunningGear.CornerOf(carSlot);
            if (corner < 0) continue;

            var (was, now) = RunningGear.IsFront(corner) ? (oldFront, front) : (oldRear, rear);
            if (carSlot == RunningGear.BrakeSlot(corner) && part.DefinitionId == was.Brake.Id && now.Brake.Id != was.Brake.Id)
            {
                // The new brake as the factory puts it on, the old one's wear and damage on the brake itself
                var fresh = Brake(now, part.ParentSlot, part.Wear);
                var brake = now.Plate != null ? fresh.Children.First(c => c.ParentSlot == RunningGear.DrumSlot) : fresh;
                brake.Tear = part.Tear;
                Replace(carParts, part, fresh);
                changed++;
            }
            else if (carSlot == RunningGear.SpringSlot(corner) && part.DefinitionId == was.Spring.Id && now.Spring.Id != was.Spring.Id)
            {
                part.DefinitionId = now.Spring.Id;
                part.Children.Clear();
                changed++;
            }
        }

        foreach (var (now, isFront) in fromVersion < 2 ? new[] { (front, true), (rear, false) } : [])
        {
            var slot = RunningGear.SwayBarSlot(isFront);
            var (frontBar, rearBar) = RunningGear.SwayBars(carParts);
            if (now.SwayBar == null || (isFront ? frontBar : rearBar) != null) continue;

            var springs = RunningGear.Mounted(carParts).Where((_, c) => RunningGear.IsFront(c) == isFront).Select(c => c.Spring).OfType<PartInstance>().ToList();
            carParts.Add(Instance(now.SwayBar, slot, 1, springs.Count > 0 ? springs.Average(p => p.Wear) : 1));
            changed++;
        }

        return changed + (fromVersion < 5 ? AddHardware(catalog, car, carParts) : 0) + HangOnAxle(catalog, carParts, specs.HasLiveAxle);
    }

    /// <summary>
    /// The rear running gear where it belongs (<see cref="RearAxle.Carries"/>), with what is on each part: a brake, coil spring,
    /// shock or sway bar on the car's own slots onto a live rear axle's housing where it has a slot for it; a leaf spring, or
    /// any of them on an independent rear's carrier, off the housing back onto the car's slot. A slot taken already keeps
    /// what is on it.
    /// </summary>
    /// <param name="liveAxle">Whether the car's rear is a live axle (its data's rear suspension <c>AXLE</c>)</param>
    /// <returns>How many parts moved</returns>
    public static int HangOnAxle(PartsCatalog catalog, List<PartInstance> carParts, bool liveAxle)
    {
        var moved = 0;
        foreach (var (part, carSlot) in RearAxle.CornerParts(carParts).ToList())
        {
            if (catalog.Get(part.DefinitionId) is not { } definition || RearAxle.Carries(definition, carSlot, liveAxle) || carParts.Any(p => p.ParentSlot == carSlot)) continue;
            carParts.Single(p => p.ParentSlot == RearAxle.CarSlot).Children.Remove(part);
            part.ParentSlot = carSlot;
            carParts.Add(part);
            moved++;
        }

        foreach (var part in carParts.ToList())
        {
            if (catalog.Get(part.DefinitionId) is not { } definition ||
                RearAxle.HousingFor(catalog, carParts, part.ParentSlot, definition, liveAxle) is not var (housing, slot) ||
                housing.Children.Any(c => c.ParentSlot == slot)) continue;
            carParts.Remove(part);
            part.ParentSlot = slot;
            housing.Children.Add(part);
            moved++;
        }

        return moved;
    }

    /// <summary>
    /// The factory rear axle as a part tree on the car's axle slot. It wears slower than the tyres the car's figures
    /// give the condition of: a well worn car's axle is tired, not gone.
    /// </summary>
    public static PartInstance CreateAxle(RearAxleChoice axle, double condition)
    {
        var wear = 0.4 + 0.6 * Math.Clamp(condition, 0, 1);
        var housing = Instance(axle.Housing, RearAxle.CarSlot, 1, wear);
        var carrier = Instance(axle.Carrier, RearAxle.CarrierSlot, 1, wear);
        carrier.Children.Add(Instance(axle.Gears, RearAxle.GearsSlot, 1, wear));
        housing.Children.Add(carrier);
        housing.Children.Add(Instance(axle.Shaft, RearAxle.LeftShaftSlot, 1, wear));
        housing.Children.Add(Instance(axle.Shaft, RearAxle.RightShaftSlot, 1, wear));
        return housing;
    }

    private static PartInstance Instance(PartDefinition definition, int parentSlot, int ownSlot, double wear) => new(definition.Id)
    {
        ParentSlot = parentSlot,
        OwnSlot = ownSlot,
        Wear = wear
    };

    /// <summary>
    /// The source game's parts of a kind: of a script, no <c>came_in</c>; a part of data no car came with is an aftermarket
    /// one (a Super Stock spring, a 90/10 shock). Failing those the parts without <c>came_in</c>, failing those all of them.
    /// </summary>
    private static List<PartDefinition> SourceGame(List<PartDefinition> parts) =>
        parts.Where(p => p.CameIn == null && p.SourceScript != null).ToList() is { Count: > 0 } scripted ? scripted
        : parts.Where(p => p.CameIn == null).ToList() is { Count: > 0 } plain ? plain : parts;

    private static List<PartDefinition> ByGroup(PartsCatalog catalog, string group) => catalog.InGroup(group).Where(p => p.IsScripted).ToList();
}
