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
    /// period brakes and springs; 2 the leaf springs, the torsion bars and the sway bars
    /// </summary>
    public const int GearVersion = 2;

    /// <summary>The factory parts of one axle; a drum brake's shoes and wheel cylinder with it, the sway bar across it (none for an axle without)</summary>
    public sealed record AxleParts(PartDefinition Tyre, PartDefinition Rim, PartDefinition Brake, PartDefinition Spring, PartDefinition Shock,
        PartDefinition? Shoes = null, PartDefinition? Cylinder = null, PartDefinition? SwayBar = null)
    {
        /// <summary>The axle's brake torque, its shoes and wheel cylinder in it</summary>
        public double BrakeTorque => RunningGear.BrakeTorque(Brake, Shoes, Cylinder);
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
            return new AxleParts(tyre, rim, brake, spring, shock,
                OnBrake(catalog, brake, RunningGear.ShoesSlot, PartKinds.BrakeShoes, car), OnBrake(catalog, brake, RunningGear.WheelCylinderSlot, PartKinds.WheelCylinders, car),
                SwayBar(catalog, front, axle.SwayBarRate));
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

    /// <summary>What goes on a brake's slot (a drum's shoes, its wheel cylinder): the factory one of those that fit, else the first</summary>
    private static PartDefinition? OnBrake(PartsCatalog catalog, PartDefinition brake, int slotId, string group, CarFacts? car)
    {
        if (brake.Slots.FirstOrDefault(s => s.Id == slotId) is not { } slot) return null;
        var fits = catalog.FindMountable(brake, slot).Select(m => m.Part).Where(p => PartKinds.GroupOf(p) == group)
            .DistinctBy(p => p.Id, StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        return RunningGearGuess.Best(fits, car ?? CarFacts.None, 0);
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

        return parts;
    }

    /// <summary>The axle's brake, with its shoes and wheel cylinder when it takes them</summary>
    private static PartInstance Brake(AxleParts axle, int carSlot, double wear)
    {
        var brake = Instance(axle.Brake, carSlot, 1, wear);
        if (axle.Shoes != null) brake.Children.Add(Instance(axle.Shoes, RunningGear.ShoesSlot, 1, wear));
        if (axle.Cylinder != null) brake.Children.Add(Instance(axle.Cylinder, RunningGear.WheelCylinderSlot, 1, wear));
        return brake;
    }

    /// <summary>
    /// A car's running gear from before <see cref="GearVersion"/>, brought up to it: a brake or a spring that is what the
    /// factory gave it then (the source game's nearest) becomes what the factory gives it now (a period drum, a leaf
    /// spring, a torsion bar), in the same wear and damage, with what goes on it. What the player put on stays. An axle the
    /// factory gives a sway bar gets it, worn like the axle's springs.
    /// </summary>
    /// <returns>How many parts changed</returns>
    public static int BringUpToDate(PartsCatalog catalog, AcCarSpecs specs, CarFacts car, RunningGearPicks? picks, List<PartInstance> carParts)
    {
        if (Choose(catalog, specs) is not var (oldFront, oldRear) || Choose(catalog, specs, car, picks) is not var (front, rear)) return 0;

        var changed = 0;
        foreach (var part in carParts.ToList())
        {
            var corner = RunningGear.CornerOf(part.ParentSlot);
            if (corner < 0) continue;

            var (was, now) = RunningGear.IsFront(corner) ? (oldFront, front) : (oldRear, rear);
            if (part.ParentSlot == RunningGear.BrakeSlot(corner) && part.DefinitionId == was.Brake.Id && now.Brake.Id != was.Brake.Id)
            {
                part.DefinitionId = now.Brake.Id;
                part.Children.Clear();
                if (now.Shoes != null) part.Children.Add(Instance(now.Shoes, RunningGear.ShoesSlot, 1, part.Wear));
                if (now.Cylinder != null) part.Children.Add(Instance(now.Cylinder, RunningGear.WheelCylinderSlot, 1, part.Wear));
                changed++;
            }
            else if (part.ParentSlot == RunningGear.SpringSlot(corner) && part.DefinitionId == was.Spring.Id && now.Spring.Id != was.Spring.Id)
            {
                part.DefinitionId = now.Spring.Id;
                part.Children.Clear();
                changed++;
            }
        }

        foreach (var (now, isFront) in new[] { (front, true), (rear, false) })
        {
            var slot = RunningGear.SwayBarSlot(isFront);
            if (now.SwayBar == null || carParts.Any(p => p.ParentSlot == slot)) continue;

            var springs = carParts.Where(p => RunningGear.CornerOf(p.ParentSlot) is var c and >= 0 && RunningGear.IsFront(c) == isFront && p.ParentSlot == RunningGear.SpringSlot(c)).ToList();
            carParts.Add(Instance(now.SwayBar, slot, 1, springs.Count > 0 ? springs.Average(p => p.Wear) : 1));
            changed++;
        }

        return changed;
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

    private static List<PartDefinition> ByGroup(PartsCatalog catalog, string group) =>
        catalog.Parts.Values.Where(p => p.IsScripted && PartKinds.GroupOf(p) == group).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
}
