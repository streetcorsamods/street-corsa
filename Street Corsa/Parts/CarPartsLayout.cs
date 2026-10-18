using System.Numerics;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts;

/// <summary>Wheel hub centres of a car, in the space the car is drawn in</summary>
public sealed record CarAnchors(Vector3 WheelLF, Vector3 WheelRF, Vector3 WheelLR, Vector3 WheelRR)
{
    /// <summary>
    /// How far the engine sits from where the hubs and the layout put it, in the car's space (metres): moved by hand in
    /// the garage's placement mode and kept in the car's entry in cars.json (<see cref="Cars.CarEntry.EngineOffset"/>)
    /// </summary>
    public Vector3 EngineOffset { get; init; }

    public Vector3 FrontAxle => (WheelLF + WheelRF) / 2f;
    public Vector3 RearAxle => (WheelLR + WheelRR) / 2f;
    public float Wheelbase => Vector3.Distance(FrontAxle, RearAxle);

    /// <summary>Hub of a corner as the running gear numbers them: 0 front left, 1 front right, 2 rear left, 3 rear right</summary>
    public Vector3 Hub(int corner) => corner switch { 0 => WheelLF, 1 => WheelRF, 2 => WheelLR, _ => WheelRR };

    public Vector3 Axle(int corner) => RunningGear.IsFront(corner) ? FrontAxle : RearAxle;

    /// <summary>Unit vector from the rear axle to the front axle, level with the ground</summary>
    public Vector3 Forward
    {
        get
        {
            var direction = FrontAxle - RearAxle;
            direction.Y = 0f;
            return direction.LengthSquared() < 1e-6f ? Vector3.UnitZ : Vector3.Normalize(direction);
        }
    }
}

/// <summary>
/// Estimates where a car's parts sit from its wheel hubs and its <see cref="EngineLayout"/> alone, since AC cars know
/// nothing about engine bays or suspension mounts: a longitudinal engine with the gearbox bolted to its back (in the
/// front or ahead of the rear axle facing forward, behind the rear axle turned around) or one lying across the car,
/// the wheel and the brake on the
/// hub, a coil spring and a shock next to every wheel, the rear axle on the line between the rear hubs. The wheels are
/// the AC model's own wheels' places: the view hides those while the parts are on show, so both never show at once.
/// Part models have their front towards -Z while AC cars face +Z, hence the frames below.
/// </summary>
public static class CarPartsLayout
{
    // Engine: crankshaft a little above the hubs, block centre behind the front axle line
    private const float CrankAboveHubs = 0.06f;
    private const float EngineBehindFrontAxle = 0.18f;
    private static readonly Vector3 DefaultCrankPosition = new(0f, -0.18f, 0f);

    // Engine behind the driver: placed by the block's rear face, where the gearbox bolts on. A mid engine's gearbox
    // reaches back to the rear axle; a rear engine's rear face is at the axle, its gearbox reaching forward past it.
    private const float MidRearFaceAheadOfRearAxle = 0.4f;
    private const float RearRearFaceBehindRearAxle = 0.05f;

    // An engine across the car: its crankshaft a little ahead of the front axle (the drive shafts come out of the
    // gearbox beside it on the axle line), or ahead of the rear axle behind the driver; the block's middle off the
    // centre line towards its timing end, so the block and the gearbox on its other end sit about the middle
    private const float TransverseCrankAheadOfFrontAxle = 0.08f;
    private const float TransverseCrankAheadOfRearAxle = 0.25f;
    private const float TransverseBlockOffCentre = 0.15f;

    /// <summary>Half a block's length, for a block whose model can't be measured</summary>
    private const float DefaultBlockHalfLength = 0.3f;

    // Running gear, relative to a hub: towards the car's centre line and up
    private const float BrakeInboard = 0.06f;
    private const float SpringInboard = 0.24f;
    private const float SpringSeatBelowHub = 0.06f;
    private const float SwayBarAheadOfAxle = 0.15f;

    // The rear axle housing's models end 0.655 m out each side (tools/RunningGearParts); on the car each end goes into
    // the drum, this far in from the hub, its tubes drawn longer to the car's track and its middle (this far either side
    // of the centre) its own size. Into the drum, not just to its backing plate: the drum hides an end that goes too far,
    // a gap shows (the owner's look in game)
    private const float HousingHalfWidth = 0.655f;
    private const float HousingEndInsideHub = 0.02f;
    private const float HousingMiddle = 0.25f;

    /// <param name="engine">The car's engine block with everything on it; null for an empty engine bay</param>
    /// <param name="gear">What sits on the car's wheel slots, each with what is on it (the tyre on the rim)</param>
    public static List<PlacedPart> Build(PartsCatalog catalog, CarAnchors anchors, InstalledPart? engine, IReadOnlyList<CarPart>? gear = null,
        EngineLayout layout = EngineLayout.Front)
    {
        var result = new List<PlacedPart>();

        if (engine != null) result.AddRange(PartAssembler.Assemble(engine, EnginePlacement(catalog, anchors, engine.Definition, layout)));

        foreach (var part in gear ?? Array.Empty<CarPart>())
        {
            if (part.CarSlot == Cars.RearAxle.CarSlot)
            {
                result.AddRange(Axle(anchors, part.Root));
                continue;
            }

            var world = RunningGear.IsSwayBarSlot(part.CarSlot) ? SwayBarPlacement(anchors, part.Root.Definition, part.CarSlot)
                : CornerPlacement(anchors, part.Root.Definition, part.CarSlot);
            if (world is { } placed) result.AddRange(PartAssembler.Assemble(part.Root, placed));
        }

        return result;
    }

    /// <summary>
    /// Where a part goes on the car itself, by the car slot it takes: running gear at its wheel's hub, anything else
    /// (an engine) in the engine's place. Null for a slot of a wheel's corner whose hubs don't tell which way is in.
    /// </summary>
    public static Matrix4x4? CarLevelPlacement(PartsCatalog catalog, CarAnchors anchors, PartDefinition part, int carSlot, EngineLayout layout) =>
        carSlot == Cars.RearAxle.CarSlot ? AxlePlacement(anchors, part)
        : RunningGear.IsSwayBarSlot(carSlot) ? SwayBarPlacement(anchors, part, carSlot)
        : RunningGear.CornerOf(carSlot) < 0 ? EnginePlacement(catalog, anchors, part, layout) : CornerPlacement(anchors, part, carSlot);

    /// <summary>
    /// The rear axle housing: its mount (slot 1) in the middle of the rear hubs, facing the way the car does, so its +X
    /// (the left shaft's side) is the car's left
    /// </summary>
    public static Matrix4x4 AxlePlacement(CarAnchors anchors, PartDefinition housing) => BySlot(housing, CarFrame(anchors.Forward), anchors.RearAxle);

    /// <summary>How much longer a housing's tubes are drawn so its ends go into the drums at the car's own track</summary>
    public static PartStretch AxleStretch(CarAnchors anchors)
    {
        var halfTrack = Vector3.Distance(anchors.WheelLR, anchors.WheelRR) / 2f;
        var factor = halfTrack > 0.3f
            ? Math.Clamp((halfTrack - HousingEndInsideHub - HousingMiddle) / (HousingHalfWidth - HousingMiddle), 0.5f, 2f)
            : 1f;
        return new PartStretch(HousingMiddle, factor);
    }

    /// <summary>
    /// The rear axle and what is on it: the housing with its tubes drawn out to the car's track (<see cref="AxleStretch"/>),
    /// the carrier and gears in its middle as they are, the shafts (inside the tubes) moved out as far as the tubes' ends,
    /// and the brakes, springs, shocks and sway bar it carries (with what is on them) where they would be on the car's own
    /// slots they stand for: at the wheel, whatever the car's track
    /// </summary>
    private static List<PlacedPart> Axle(CarAnchors anchors, InstalledPart housing)
    {
        var world = AxlePlacement(anchors, housing.Definition);
        var stretch = AxleStretch(anchors);
        var across = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, world));
        var outward = stretch.At(HousingHalfWidth) - HousingHalfWidth;

        var placed = PartAssembler.Assemble(housing, world);
        var at = new Dictionary<InstalledPart, Matrix4x4>(ReferenceEqualityComparer.Instance);
        foreach (var p in placed.Where(p => p.Source != null)) at[p.Source!] = p.World;

        // What each part on a corner slot of the housing moves by, with everything on it
        var moved = new Dictionary<InstalledPart, Matrix4x4>(ReferenceEqualityComparer.Instance);
        foreach (var (slot, child) in housing.Children)
        {
            if (RearAxle.CarSlotFor(slot) is not { } carSlot || !at.TryGetValue(child, out var old) || !Matrix4x4.Invert(old, out var undo)) continue;
            var wanted = RunningGear.IsSwayBarSlot(carSlot) ? SwayBarPlacement(anchors, child.Definition, carSlot) : CornerPlacement(anchors, child.Definition, carSlot);
            if (wanted is not { } target) continue;
            foreach (var part in child.SelfAndDescendants()) moved[part] = undo * target;
        }

        return placed.Select(p =>
        {
            if (ReferenceEquals(p.Source, housing)) return p with { Stretch = stretch };
            if (p.Source != null && moved.TryGetValue(p.Source, out var by)) return p with { World = p.World * by };
            if (PartKinds.GroupOf(p.Part) != PartKinds.AxleShafts) return p;
            var side = MathF.Sign(Vector3.Dot(p.World.Translation - world.Translation, across));
            return p with { World = p.World * Matrix4x4.CreateTranslation(across * (side * outward)) };
        }).ToList();
    }

    /// <summary>
    /// Where a loose part goes on a mounted part's slot: on the rear axle's slot that stands for one of the car's own (a
    /// rear brake, spring, shock or sway bar), where it would on that car slot; elsewhere where the slot puts it
    /// </summary>
    public static Matrix4x4? PlacementOn(PartsCatalog catalog, CarAnchors? anchors, PartDefinition parent, int parentSlot, Matrix4x4 parentWorld,
        PartDefinition part, int ownSlot, EngineLayout layout) =>
        anchors != null && PartKinds.GroupOf(parent) == PartKinds.RearAxles && RearAxle.CarSlotFor(parentSlot) is { } carSlot
            ? CarLevelPlacement(catalog, anchors, part, carSlot, layout)
            : PartAssembler.ChildWorld(parent, parentSlot, part, ownSlot, parentWorld);

    /// <summary>
    /// A sway bar across its axle: its mount (slot 1) a little ahead of the middle of the axle's hubs, facing the way the
    /// car does
    /// </summary>
    public static Matrix4x4 SwayBarPlacement(CarAnchors anchors, PartDefinition bar, int carSlot) =>
        BySlot(bar, CarFrame(anchors.Forward),
            (carSlot == RunningGear.FrontSwayBarSlot ? anchors.FrontAxle : anchors.RearAxle) + anchors.Forward * SwayBarAheadOfAxle);

    /// <summary>A wheel-side part on its hub: the brake on it, a spring or a shock next to it; null for a slot of no corner</summary>
    private static Matrix4x4? CornerPlacement(CarAnchors anchors, PartDefinition part, int carSlot)
    {
        var corner = RunningGear.CornerOf(carSlot);
        if (corner < 0) return null;

        var hub = anchors.Hub(corner);
        var inboard = anchors.Axle(corner) - hub;
        inboard.Y = 0f;
        if (inboard.LengthSquared() < 1e-6f) return null;
        inboard = Vector3.Normalize(inboard);

        // Every wheel-side part is modelled for one side; turning it around the vertical axis gives the other
        var side = SideFrame(anchors.Forward, inboard);
        if (carSlot == RunningGear.BrakeSlot(corner)) return BySlot(part, side, hub + inboard * BrakeInboard);

        // A wheel by the middle of its tyre on the hub, where the car's own wheel has its middle (its WHEEL_* dummy): the
        // rim's tyre seat, else the face it bolts on by. The source game's rims are modelled the other way round from the
        // running gear: their face is -X (the owner's look in game), so a wheel is turned round
        if (carSlot == RunningGear.WheelSlot(corner)) return BySlot(part, SideFrame(anchors.Forward, -inboard), hub, RunningGear.TyreSlotOnRim);

        // A spring faces the way the car does on both sides: a leaf spring with its traction bar, a torsion bar, run along
        // the car and are not the same end to end, which turning them round would swap on the right (a coil is round)
        var springSeat = hub + inboard * SpringInboard - Vector3.UnitY * SpringSeatBelowHub;
        return BySlot(part, carSlot == RunningGear.SpringSlot(corner) ? CarFrame(anchors.Forward) : side, springSeat);
    }

    /// <summary>Where an engine block sits in the car by the car's layout, its model measured through the catalog</summary>
    public static Matrix4x4 EnginePlacement(PartsCatalog catalog, CarAnchors anchors, PartDefinition block, EngineLayout layout) =>
        EnginePlacement(anchors, block, layout, layout == EngineLayout.Front ? null : PartModelBounds.Of(catalog, block));

    /// <summary>
    /// Where an engine block sits in the car, crankshaft a little above the hubs: a front engine behind the front axle
    /// line; a mid one facing forward with its rear face ahead of the rear axle; a rear one turned around, its rear
    /// face at the rear axle. One across the car has its timing end towards the side <see cref="CarFrame"/> calls
    /// right and its gearbox towards the other (not yet checked against a front-wheel-drive car in the garage).
    /// </summary>
    /// <param name="bounds">The block model's box, to find its rear face by; null for a block of the usual length</param>
    public static Matrix4x4 EnginePlacement(CarAnchors anchors, PartDefinition block, EngineLayout layout = EngineLayout.Front, (Vector3 Min, Vector3 Max)? bounds = null)
    {
        var forward = anchors.Forward;
        var right = Vector3.Cross(Vector3.UnitY, forward);
        var frame = CarFrame(layout switch
        {
            EngineLayout.Rear => -forward,
            EngineLayout.FrontTransverse or EngineLayout.MidTransverse => right,
            _ => forward
        });

        var crank = block.Slots.FirstOrDefault(s => s.Name.Contains("crank", StringComparison.OrdinalIgnoreCase));
        var crankLocal = crank == null ? DefaultCrankPosition : new Vector3(crank.Position[0], crank.Position[1], 0f);
        var lift = Vector3.UnitY * CrankAboveHubs;

        // What lands on the target: the crankshaft's line, at the block's origin in front, at its rear face behind the driver
        var (local, target) = layout switch
        {
            EngineLayout.Mid => (crankLocal with { Z = RearFaceZ(bounds) }, anchors.RearAxle + forward * MidRearFaceAheadOfRearAxle + lift),
            EngineLayout.Rear => (crankLocal with { Z = RearFaceZ(bounds) }, anchors.RearAxle - forward * RearRearFaceBehindRearAxle + lift),
            EngineLayout.FrontTransverse => (crankLocal with { Z = MiddleZ(bounds) }, anchors.FrontAxle + forward * TransverseCrankAheadOfFrontAxle + right * TransverseBlockOffCentre + lift),
            EngineLayout.MidTransverse => (crankLocal with { Z = MiddleZ(bounds) }, anchors.RearAxle + forward * TransverseCrankAheadOfRearAxle + right * TransverseBlockOffCentre + lift),
            _ => (crankLocal, anchors.FrontAxle - forward * EngineBehindFrontAxle + lift)
        };

        // Then where the car's entry moves it to, by hand
        var origin = target - Vector3.TransformNormal(local, frame) + anchors.EngineOffset;
        return frame * Matrix4x4.CreateTranslation(origin);
    }

    /// <summary>Z of a block's rear face in its own space: part models have their front towards -Z</summary>
    private static float RearFaceZ((Vector3 Min, Vector3 Max)? bounds) => bounds?.Max.Z ?? DefaultBlockHalfLength;

    /// <summary>Z of the middle of a block's length in its own space</summary>
    private static float MiddleZ((Vector3 Min, Vector3 Max)? bounds) => bounds is { } box ? (box.Min.Z + box.Max.Z) / 2 : 0f;

    /// <summary>
    /// Places a part so that its first slot (where it meets the car) lands on a point, or the slot
    /// <paramref name="slotId"/> when it has one
    /// </summary>
    private static Matrix4x4 BySlot(PartDefinition part, Matrix4x4 frame, Vector3 target, int slotId = 1)
    {
        var slot = part.Slots.FirstOrDefault(s => s.Id == slotId) ?? part.Slots.FirstOrDefault(s => s.Id == 1) ?? part.Slots.FirstOrDefault();
        var slotLocal = slot == null ? Vector3.Zero : new Vector3(slot.Position[0], slot.Position[1], slot.Position[2]);
        var origin = target - Vector3.TransformNormal(slotLocal, frame);
        return frame * Matrix4x4.CreateTranslation(origin);
    }

    /// <summary>Rotation that turns a part model (front towards -Z) to face the way the car does</summary>
    private static Matrix4x4 CarFrame(Vector3 forward)
    {
        var right = Vector3.Cross(Vector3.UnitY, forward);
        return Basis(-right, Vector3.UnitY, -forward);
    }

    /// <summary>Rotation for a wheel-side part: its +X ends up pointing away from the car</summary>
    private static Matrix4x4 SideFrame(Vector3 forward, Vector3 inboard)
    {
        var outboard = -inboard;
        return Basis(outboard, Vector3.UnitY, Vector3.Cross(outboard, Vector3.UnitY));
    }

    private static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z) => new(
        x.X, x.Y, x.Z, 0f,
        y.X, y.Y, y.Z, 0f,
        z.X, z.Y, z.Z, 0f,
        0f, 0f, 0f, 1f);
}
