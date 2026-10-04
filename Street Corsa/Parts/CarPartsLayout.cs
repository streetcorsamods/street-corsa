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
/// hub, a coil spring and a shock next to every wheel.
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

    /// <param name="engine">The car's engine block with everything on it; null for an empty engine bay</param>
    /// <param name="gear">What sits on the car's wheel slots, each with what is on it (the tyre on the rim)</param>
    public static List<PlacedPart> Build(PartsCatalog catalog, CarAnchors anchors, InstalledPart? engine, IReadOnlyList<CarPart>? gear = null,
        EngineLayout layout = EngineLayout.Front)
    {
        var result = new List<PlacedPart>();

        if (engine != null) result.AddRange(PartAssembler.Assemble(engine, EnginePlacement(catalog, anchors, engine.Definition, layout)));

        foreach (var part in gear ?? Array.Empty<CarPart>())
        {
            // Rims and tyres are not drawn: the Assetto Corsa model brings its own wheels, and the part ones
            // land on the very same WHEEL_* dummies, so both show at once. They are still parts - mounted,
            // bought, sold and exported to the car's physics as before - they just have no model here.
            if (RunningGear.IsWheelSlot(part.CarSlot)) continue;

            if (CornerPlacement(anchors, part.Root.Definition, part.CarSlot) is { } world) result.AddRange(PartAssembler.Assemble(part.Root, world));
        }

        return result;
    }

    /// <summary>
    /// Where a part goes on the car itself, by the car slot it takes: running gear at its wheel's hub, anything else
    /// (an engine) in the engine's place. Null for a slot of a wheel's corner whose hubs don't tell which way is in.
    /// </summary>
    public static Matrix4x4? CarLevelPlacement(PartsCatalog catalog, CarAnchors anchors, PartDefinition part, int carSlot, EngineLayout layout) =>
        RunningGear.CornerOf(carSlot) < 0 ? EnginePlacement(catalog, anchors, part, layout) : CornerPlacement(anchors, part, carSlot);

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

        var springSeat = hub + inboard * SpringInboard - Vector3.UnitY * SpringSeatBelowHub;
        return BySlot(part, side, springSeat);
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

    /// <summary>Places a part so that its first slot (where it meets the car) lands on a point</summary>
    private static Matrix4x4 BySlot(PartDefinition part, Matrix4x4 frame, Vector3 target)
    {
        var slot = part.Slots.FirstOrDefault(s => s.Id == 1) ?? part.Slots.FirstOrDefault();
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
