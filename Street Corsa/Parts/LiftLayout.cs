using System.Numerics;
using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts;

/// <summary>
/// The garage's lift: an in-ground hydraulic post with a saddle under the car's middle, between its axles. Always in the
/// garage (nothing to buy); a garage prop of its own (<c>props/lift</c>), so a garage says where it stands, and its
/// model, unless a mod gives one, is drawn here from boxes as the workbench is: a steel plate in the floor, and the post
/// with its saddle (<see cref="PostNode"/>) that rises out of it. Everything here is in the lift's own space: the post's
/// middle on the floor at the origin, the car's front +Z.
/// </summary>
/// <remarks>
/// A mod's model names its moving part <see cref="PostNode"/>, with the top of the saddle (where the car rests) at that
/// node's origin; anything else in it stays on the floor.
/// </remarks>
public static class LiftLayout
{
    /// <summary>The prop's id in the mods' props folders</summary>
    public const string PropId = "lift";

    /// <summary>The node that rises with the car</summary>
    public const string PostNode = "lift_post";

    /// <summary>How high the car goes at the first stop: its wheels hang clear of the floor</summary>
    public const float WheelsClearHeight = 0.3f;

    /// <summary>How high the car goes at full height: room to work underneath</summary>
    public const float FullHeight = 1.4f;

    /// <summary>How fast the lift goes, metres a second: the first stop in about two seconds, full height in seven</summary>
    public const float Speed = 0.2f;

    /// <summary>
    /// How far the wheels drop from the body as it comes up off them, metres: the springs let out to the end of their
    /// travel. Until the body is this high the wheels stay on the floor; past it they hang this far down.
    /// </summary>
    public const float WheelDrop = 0.1f;

    /// <summary>The saddle's top down in the floor, on its plate: the game's own lift (a model's rests where its node is put)</summary>
    public const float SaddleDown = 0.07f;

    /// <summary>
    /// How far under the car's body the saddle catches it when the car's model does not say (<see cref="UnderBodyOf"/>):
    /// a 1960s car's frame rails off the floor
    /// </summary>
    public const float UnderBody = 0.2f;

    private const string LiftNode = "lift";

    /// <summary>How high the car stands at a stop</summary>
    public static float HeightOf(LiftStop stop) => stop switch
    {
        LiftStop.WheelsClear => WheelsClearHeight,
        LiftStop.Full => FullHeight,
        _ => 0f
    };

    /// <summary>How far the wheels hang below where they sit on the floor with the car <paramref name="height"/> up (<see cref="WheelDrop"/>)</summary>
    public static float Droop(float height) => Math.Clamp(height, 0f, WheelDrop);

    /// <summary>The stop under this one, as the lift's Lower button takes it: full height to the wheels clear, then down</summary>
    public static LiftStop Below(LiftStop stop) => stop == LiftStop.Full ? LiftStop.WheelsClear : LiftStop.Down;

    /// <summary>The stop after this one as the lift's button steps it: down, the wheels clear, full height, down again</summary>
    public static LiftStop Next(LiftStop stop) => stop switch
    {
        LiftStop.Down => LiftStop.WheelsClear,
        LiftStop.WheelsClear => LiftStop.Full,
        _ => LiftStop.Down
    };

    /// <summary>
    /// Where the saddle's top is at a stop: down, where the lift rests (<paramref name="down"/>, its model's); up, the
    /// car's height over the floor and the body's underside on it (<paramref name="underBody"/>)
    /// </summary>
    public static float SaddleGoal(LiftStop stop, float underBody, float down) =>
        stop == LiftStop.Down ? down : HeightOf(stop) + Math.Max(underBody, down);

    /// <summary>
    /// How high the car is with the saddle's top at <paramref name="saddle"/>: on its wheels until the saddle comes up to
    /// the body's underside (<paramref name="underBody"/>), then carried; the saddle rises to the body first, the car after
    /// </summary>
    public static float CarHeight(float saddle, float underBody) => Math.Max(0f, saddle - underBody);

    /// <summary>
    /// The body's underside where the lift takes it, from the lowest of the car's body over the saddle's pads
    /// (<paramref name="measured"/>, metres over the floor; null when nothing of it is there): held between
    /// <see cref="LowestUnderBody"/> and <see cref="HighestUnderBody"/>, and <see cref="UnderBody"/> without it
    /// </summary>
    public static float UnderBodyOf(float? measured) =>
        measured is { } m && float.IsFinite(m) ? Math.Clamp(m, LowestUnderBody, HighestUnderBody) : UnderBody;

    /// <summary>The lowest and highest a body's underside is taken to be over the floor, for a car model that says otherwise</summary>
    public const float LowestUnderBody = 0.08f;
    public const float HighestUnderBody = 0.6f;

    /// <summary>
    /// Where the saddle's four rubber pads are, in the lift's own space (the car's front +Z): this far either side of the
    /// middle (under a 1960s car's frame rails) and this far ahead and behind it, each this far across from its middle
    /// </summary>
    public const float PadSide = 0.5f;
    public const float PadAlong = 0.6f;
    public const float PadHalf = 0.07f;

    /// <summary>
    /// Where under the lift's middle its pads reach, in the lift's own space: the body over them is what the lift takes
    /// (the game's own saddle's pads, <see cref="PadSide"/>; a mod's model takes the car where they would)
    /// </summary>
    public const float PadsFromMiddle = PadSide - PadHalf;
    public const float PadsToSide = PadSide + PadHalf;
    public const float PadsAlong = PadAlong + PadHalf;

    /// <summary>
    /// Where the lift stands under a car whose hubs are <paramref name="anchors"/>: under the middle between its axles,
    /// turned the way it faces, at the floor height the prop's place gives (<paramref name="prop"/>)
    /// </summary>
    public static Matrix4x4 UnderCar(CarAnchors anchors, Matrix4x4 prop)
    {
        var middle = (anchors.FrontAxle + anchors.RearAxle) / 2f;
        var forward = anchors.Forward;
        var right = Vector3.Cross(Vector3.UnitY, forward);
        var turn = new Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            0f, 1f, 0f, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f);
        return turn * Matrix4x4.CreateTranslation(middle.X, prop.Translation.Y, middle.Z);
    }

    /// <summary>
    /// The lift drawn from boxes, as a model of its own: a steel tread plate in the floor the size of the saddle lying on
    /// it, edged in yellow and black hazard stripes, with a collar the piston comes up through and bolts at its corners;
    /// the saddle (a beam along the car, an arm across at each end with a pad at either end of it) on its piston
    /// </summary>
    public static IKn5 CreateModel()
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = LiftNode;

        // The saddle: its pads' tops at the node's origin, the arms under them, the beam under the arms; and the piston,
        // long enough to reach full height out of its cylinder under the floor
        const float pad = 0.025f;
        const float beam = 0.035f;
        const float beamAlong = PadAlong + 0.08f;
        const float pistonLength = FullHeight + HighestUnderBody + 0.2f;
        var piston = new List<(Vector3 Centre, Vector3 Half)>
        {
            (new(0f, -pad * 2f - beam * 2f - pistonLength / 2f, 0f), new(0.075f, pistonLength / 2f, 0.075f))
        };
        var saddle = new List<(Vector3 Centre, Vector3 Half)>
        {
            (new(0f, -pad * 2f - beam, 0f), new(0.09f, beam, beamAlong)),
            (new(0f, -pad * 2f - beam, PadAlong), new(PadSide + 0.06f, beam * 0.8f, 0.05f)),
            (new(0f, -pad * 2f - beam, -PadAlong), new(PadSide + 0.06f, beam * 0.8f, 0.05f))
        };
        var pads = new List<(Vector3 Centre, Vector3 Half)>();
        foreach (var x in new[] { -PadSide, PadSide })
        foreach (var z in new[] { -PadAlong, PadAlong })
            pads.Add((new(x, -pad, z), new(PadHalf, pad, PadHalf)));

        // The plate: as big as the saddle lying on it and a hand round it, its tread raised in rows that turn each way,
        // the hazard stripes round its edge, the collar in its middle and a bolt at each corner and halfway along
        const float plateX = PadSide + PadHalf + 0.06f;
        const float plateZ = beamAlong + 0.07f;
        const float plateTop = 0.006f;
        const float edge = 0.05f;
        const float stripe = 0.1f;
        var plate = new List<(Vector3 Centre, Vector3 Half)> { (new(0f, plateTop / 2f, 0f), new(plateX, plateTop / 2f, plateZ)) };

        var tread = new List<(Vector3 Centre, Vector3 Half)>();
        const float pitch = 0.07f;
        var row = 0;
        for (var z = -plateZ + edge + pitch / 2f; z < plateZ - edge; z += pitch, row++)
        {
            var column = 0;
            for (var x = -plateX + edge + pitch / 2f; x < plateX - edge; x += pitch, column++)
            {
                var half = (row + column) % 2 == 0 ? new Vector3(0.016f, 0.0015f, 0.005f) : new Vector3(0.005f, 0.0015f, 0.016f);
                tread.Add((new(x, plateTop + 0.0015f, z), half));
            }
        }

        var yellowStripes = new List<(Vector3 Centre, Vector3 Half)>();
        var blackStripes = new List<(Vector3 Centre, Vector3 Half)>();
        var outerX = plateX + edge;
        void Stripes(float fromA, float toA, Func<float, Vector3> centre, Func<float, Vector3> half)
        {
            var n = Math.Max(1, (int)MathF.Round((toA - fromA) / stripe));
            var length = (toA - fromA) / n;
            for (var i = 0; i < n; i++)
                (i % 2 == 0 ? yellowStripes : blackStripes).Add((centre(fromA + length * (i + 0.5f)), half(length / 2f)));
        }
        foreach (var sz in new[] { -1f, 1f })
            Stripes(-outerX, outerX, a => new(a, plateTop / 2f + 0.001f, sz * (plateZ + edge / 2f)), h => new(h, plateTop / 2f + 0.001f, edge / 2f));
        foreach (var sx in new[] { -1f, 1f })
            Stripes(-plateZ, plateZ, a => new(sx * (plateX + edge / 2f), plateTop / 2f + 0.001f, a), h => new(edge / 2f, plateTop / 2f + 0.001f, h));

        var collar = new List<(Vector3 Centre, Vector3 Half)> { (new(0f, plateTop + 0.006f, 0f), new(0.12f, 0.006f, 0.12f)) };
        var bolts = new List<(Vector3 Centre, Vector3 Half)>();
        foreach (var x in new[] { -plateX + 0.04f, plateX - 0.04f })
        foreach (var z in new[] { -plateZ + 0.04f, 0f, plateZ - 0.04f })
            bolts.Add((new(x, plateTop + 0.004f, z), new(0.012f, 0.004f, 0.012f)));
        foreach (var x in new[] { -0.09f, 0.09f })
        foreach (var z in new[] { -0.09f, 0.09f })
            bolts.Add((new(x, plateTop + 0.016f, z), new(0.01f, 0.004f, 0.01f)));

        var plateSteel = PlaceholderMesh.SolidMaterial(kn5, "lift_plate", 0x5C, 0x60, 0x64, 0.45f, 0.55f, 0.5f, 60f);
        var treadSteel = PlaceholderMesh.SolidMaterial(kn5, "lift_tread", 0x7A, 0x7E, 0x82, 0.45f, 0.6f, 0.7f, 80f);
        var steel = PlaceholderMesh.SolidMaterial(kn5, "lift_steel", 0x3A, 0x3E, 0x44, 0.4f, 0.55f, 0.35f, 40f);
        var yellow = PlaceholderMesh.SolidMaterial(kn5, "lift_yellow", 0xD8, 0xA8, 0x1E, 0.45f, 0.6f, 0.2f, 20f);
        var black = PlaceholderMesh.SolidMaterial(kn5, "lift_black", 0x16, 0x16, 0x16, 0.4f, 0.55f, 0.15f, 12f);
        var chrome = PlaceholderMesh.SolidMaterial(kn5, "lift_chrome", 0xB8, 0xBC, 0xC2, 0.35f, 0.5f, 0.9f, 120f);
        var rubber = PlaceholderMesh.SolidMaterial(kn5, "lift_rubber", 0x1A, 0x1A, 0x1A, 0.4f, 0.6f, 0.05f, 6f);

        var floor = Kn5Node.CreateBaseNode(LiftNode + "_floor");
        floor.Children.Add(PlaceholderMesh.Boxes("lift_plate", plate, plateSteel));
        floor.Children.Add(PlaceholderMesh.Boxes("lift_tread", tread, treadSteel));
        floor.Children.Add(PlaceholderMesh.Boxes("lift_stripes_yellow", yellowStripes, yellow));
        floor.Children.Add(PlaceholderMesh.Boxes("lift_stripes_black", blackStripes, black));
        floor.Children.Add(PlaceholderMesh.Boxes("lift_collar", collar, steel));
        floor.Children.Add(PlaceholderMesh.Boxes("lift_bolts", bolts, chrome));
        floor.Transform = Identity;
        kn5.RootNode.Children.Add(floor);

        var post = Kn5Node.CreateBaseNode(PostNode);
        post.Children.Add(PlaceholderMesh.Boxes("lift_piston", piston, chrome));
        post.Children.Add(PlaceholderMesh.Boxes("lift_saddle", saddle, yellow));
        post.Children.Add(PlaceholderMesh.Boxes("lift_pads", pads, rubber));
        post.Transform = new Mat4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, SaddleDown, 0, 1);
        kn5.RootNode.Children.Add(post);
        return kn5;
    }

    private static Mat4x4 Identity => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
}
