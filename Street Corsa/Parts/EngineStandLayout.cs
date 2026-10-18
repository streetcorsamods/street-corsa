using System.Numerics;
using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts;

/// <summary>
/// The garage's engine stand and the engine on it: the engine hangs off the stand's head by its bellhousing face, the
/// crankshaft level at a working height, its front towards +Z (the way a car in the garage faces). The stand is
/// drawn from boxes of square tube, as real ones are made, on the floor beside the car (<see cref="Spot"/>), always
/// there with whatever engine is on it.
/// </summary>
public static class EngineStandLayout
{
    /// <summary>Height of the crankshaft above the floor</summary>
    public const float CrankHeight = 0.78f;

    /// <summary>The prop's id in the mods' props folders: its place in a garage is where the stand stands there</summary>
    public const string PropId = "engine_stand";

    /// <summary>
    /// Where the stand stands in a garage that gives it no place: on the floor 3 m to the car's left (AC cars face +Z with
    /// their left towards +X), the engine on it level with the middle of the car
    /// </summary>
    public static readonly Vector3 DefaultSpot = new(3f, 0f, 0.3f);

    /// <summary>
    /// Where the stand stands in the garage played from (<see cref="PlaceAt"/>): the middle of its spindle over the floor
    /// there. It always faces the way the car does, its engine's front towards +Z.
    /// </summary>
    public static Vector3 Spot { get; private set; } = DefaultSpot;

    /// <summary>The stand put where the garage's prop says (<see cref="PropId"/>); <see cref="DefaultSpot"/> when it says nowhere</summary>
    public static void PlaceAt(Vector3? spot) => Spot = spot is { } at && float.IsFinite(at.X) && float.IsFinite(at.Y) && float.IsFinite(at.Z) ? at : DefaultSpot;

    /// <summary>A point on the spindle the engine turns over on, the crankshaft's line: it runs along Z</summary>
    public static Vector3 Spindle => Spot + new Vector3(0f, CrankHeight, 0f);

    /// <summary>Height of the top of the stand's base above the floor: an engine turned over has to stay above it</summary>
    public const float BaseTop = BaseHeight + BaseHalfHeight;

    /// <summary>
    /// The node of the stand's head, which the engine is bolted to: it turns on the spindle with the engine. The rest of
    /// the stand is <see cref="StandNode"/>.
    /// </summary>
    public const string HeadNode = "engine_stand_head";

    private const string StandNode = "engine_stand";
    private const float BaseHeight = 0.1f;
    private const float BaseHalfHeight = 0.032f;

    /// <summary>Where the head sits, from <see cref="Spot"/>, when there is no engine to hang off it</summary>
    private const float EmptyHeadZ = -0.35f;

    /// <summary>The engine's parts in their places on the stand; empty when there is none</summary>
    public static List<PlacedPart> Build(PartsCatalog catalog, InstalledPart? engine) =>
        engine == null ? new List<PlacedPart>() : PartAssembler.Assemble(engine, EnginePlacement(catalog, engine.Definition));

    /// <summary>Crankshaft on the stand's axis at <see cref="CrankHeight"/>, front towards +Z</summary>
    public static Matrix4x4 EnginePlacement(PartsCatalog catalog, PartDefinition block)
    {
        var crank = block.Slots.FirstOrDefault(s => s.Name.Contains("crank", StringComparison.OrdinalIgnoreCase));
        var crankLocal = crank == null ? Vector3.Zero : new Vector3(crank.Position[0], crank.Position[1], 0f);

        // Turned round about Y, as in a car: part models have their front towards -Z
        var frame = Matrix4x4.CreateRotationY(MathF.PI);
        var origin = Spindle - Vector3.TransformNormal(crankLocal, frame);
        return frame * Matrix4x4.CreateTranslation(origin);
    }

    /// <summary>Z of the block's rear face on the stand, where the stand's head bolts on, in the garage's space</summary>
    public static float HeadZ(PartsCatalog catalog, InstalledPart? engine)
    {
        if (engine == null || PartModelBounds.Of(catalog, engine.Definition) is not { } bounds) return Spot.Z + EmptyHeadZ;

        // The rear is the model's +Z, turned to -Z on the stand
        var world = EnginePlacement(catalog, engine.Definition);
        return Vector3.Transform(new Vector3(0f, 0f, bounds.Max.Z), world).Z;
    }

    /// <summary>
    /// The stand as a model of its own, its head at <paramref name="headZ"/>: head plate and arms, spindle, post and a
    /// base whose legs reach forward under the engine, on casters. Nothing on it can be picked: it has no part nodes.
    /// </summary>
    public static IKn5 CreateModel(float headZ)
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = StandNode;
        AddTo(kn5, headZ);
        return kn5;
    }

    /// <summary>The stand added to a model of parts, as nodes of its own that are no part's: the head, and the rest</summary>
    public static void AddTo(IKn5 kn5, float headZ)
    {
        const float h = CrankHeight;
        const float b = BaseHeight;
        const float bh = BaseHalfHeight;
        var post = headZ - 0.27f;

        // Head: plate, a cross of arms to the bolt holes, the spindle back to the post
        var head = new List<(Vector3 Centre, Vector3 Half)>
        {
            (new(0f, h, headZ - 0.015f), new(0.12f, 0.12f, 0.012f)),
            (new(0f, h, headZ - 0.006f), new(0.25f, 0.022f, 0.01f)),
            (new(0f, h, headZ - 0.006f), new(0.022f, 0.21f, 0.01f)),
            (new(0f, h, (headZ - 0.03f + post) / 2f), new(0.036f, 0.036f, (headZ - 0.03f - post) / 2f))
        };

        var steel = new List<(Vector3 Centre, Vector3 Half)>
        {
            // Post, and the collar the spindle turns in
            (new(0f, (h + 0.05f) / 2f, post), new(0.042f, (h + 0.05f) / 2f, 0.042f)),
            (new(0f, h, post), new(0.06f, 0.06f, 0.06f)),

            // Base: a crossbar at the post, two legs forward under the engine, one back to keep it from tipping
            (new(0f, b, post), new(0.36f, bh, 0.036f)),
            (new(-0.33f, b, post + 0.44f), new(0.036f, bh, 0.44f)),
            (new(0.33f, b, post + 0.44f), new(0.036f, bh, 0.44f)),
            (new(0f, b, post - 0.2f), new(0.036f, bh, 0.2f))
        };

        var casters = new[] { new Vector3(-0.33f, 0f, post + 0.84f), new Vector3(0.33f, 0f, post + 0.84f), new Vector3(-0.33f, 0f, post), new Vector3(0.33f, 0f, post), new Vector3(0f, 0f, post - 0.36f) }
            .Select(c => (new Vector3(c.X, 0.034f, c.Z), new Vector3(0.028f, 0.034f, 0.03f)))
            .ToList();

        var paint = PlaceholderMesh.SolidMaterial(kn5, "stand_paint", 0xA8, 0x1E, 0x14, 0.4f, 0.55f, 0.35f, 60f);
        var node = Kn5Node.CreateBaseNode(StandNode);
        node.Children.Add(PlaceholderMesh.Boxes("stand_steel", steel, paint));
        node.Children.Add(PlaceholderMesh.Boxes("stand_casters", casters, PlaceholderMesh.SolidMaterial(kn5, "stand_rubber", 0x1C, 0x1C, 0x1C, 0.4f, 0.55f, 0.05f, 8f)));
        node.Transform = Translation(Spot.X, Spot.Y);
        kn5.RootNode.Children.Add(node);

        var headNode = Kn5Node.CreateBaseNode(HeadNode);
        headNode.Children.Add(PlaceholderMesh.Boxes("stand_head", head, paint));
        headNode.Transform = Translation(Spot.X, Spot.Y);
        kn5.RootNode.Children.Add(headNode);
    }

    /// <summary>
    /// The stand's boxes are laid out on the car's centre line, on the floor, at the head's Z (which follows the engine):
    /// the node moves them over to <see cref="Spot"/>, and up to its floor
    /// </summary>
    private static Mat4x4 Translation(float x, float y) => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, 0, 1);
}
