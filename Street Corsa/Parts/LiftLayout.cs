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
    /// Where under the lift's middle the pads of its arms reach, in the lift's own space (the car's front +Z): the body
    /// over them is what the lift takes; between the frame rails and the sills, either side of the middle between the axles
    /// </summary>
    public const float PadsFromMiddle = 0.5f;
    public const float PadsToSide = 1.05f;
    public const float PadsAlong = 1.35f;

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

    /// <summary>The lift drawn from boxes, as a model of its own</summary>
    public static IKn5 CreateModel()
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = LiftNode;

        // The plate in the floor, with yellow and black edging round it
        var plate = new List<(Vector3 Centre, Vector3 Half)> { (new(0f, 0.004f, 0f), new(0.72f, 0.004f, 0.34f)) };
        var edging = new List<(Vector3 Centre, Vector3 Half)>
        {
            (new(0f, 0.005f, 0.36f), new(0.76f, 0.005f, 0.025f)),
            (new(0f, 0.005f, -0.36f), new(0.76f, 0.005f, 0.025f)),
            (new(0.74f, 0.005f, 0f), new(0.025f, 0.005f, 0.34f)),
            (new(-0.74f, 0.005f, 0f), new(0.025f, 0.005f, 0.34f))
        };

        // The post: the piston, long enough to reach full height out of its cylinder under the floor, and the saddle on
        // it: a beam along the car, arms across it and a rubber pad at each arm's end, the pads' tops at the node's origin
        const float pad = 0.025f;
        const float beam = 0.035f;
        const float pistonLength = FullHeight + UnderBody + 0.2f;
        var piston = new List<(Vector3 Centre, Vector3 Half)>
        {
            (new(0f, -pad * 2f - beam * 2f - pistonLength / 2f, 0f), new(0.075f, pistonLength / 2f, 0.075f))
        };
        var saddle = new List<(Vector3 Centre, Vector3 Half)>
        {
            (new(0f, -pad * 2f - beam, 0f), new(0.09f, beam, 0.68f)),
            (new(0f, -pad * 2f - beam, 0.6f), new(0.46f, beam * 0.8f, 0.05f)),
            (new(0f, -pad * 2f - beam, -0.6f), new(0.46f, beam * 0.8f, 0.05f))
        };
        var pads = new List<(Vector3 Centre, Vector3 Half)>();
        foreach (var x in new[] { -0.4f, 0.4f })
        foreach (var z in new[] { -0.6f, 0.6f })
            pads.Add((new(x, -pad, z), new(0.07f, pad, 0.07f)));

        var steel = PlaceholderMesh.SolidMaterial(kn5, "lift_steel", 0x3A, 0x3E, 0x44, 0.4f, 0.55f, 0.35f, 40f);
        var yellow = PlaceholderMesh.SolidMaterial(kn5, "lift_yellow", 0xD8, 0xA8, 0x1E, 0.45f, 0.6f, 0.2f, 20f);
        var chrome = PlaceholderMesh.SolidMaterial(kn5, "lift_chrome", 0xB8, 0xBC, 0xC2, 0.35f, 0.5f, 0.9f, 120f);
        var rubber = PlaceholderMesh.SolidMaterial(kn5, "lift_rubber", 0x1A, 0x1A, 0x1A, 0.4f, 0.6f, 0.05f, 6f);

        var floor = Kn5Node.CreateBaseNode(LiftNode + "_floor");
        floor.Children.Add(PlaceholderMesh.Boxes("lift_plate", plate, steel));
        floor.Children.Add(PlaceholderMesh.Boxes("lift_edging", edging, yellow));
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
