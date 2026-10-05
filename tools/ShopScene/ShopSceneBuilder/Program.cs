using AcTools.Kn5File;
using AcTools.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreetCorsa.Tools;
using static StreetCorsa.Tools.Kn5SceneWriter;

namespace ShopSceneBuilder;

/// <summary>
/// Builds the inside of a part shop (the part shop screen): shop.kn5 and the shop.json that goes with it, out of the
/// pictures prepare_shop.py gathered.
///
/// A shop 12 m across and 12 m deep, seen from just inside the door: the counter across the middle with the till on it,
/// shelving along the back wall under the shop's sign, a tyre rack down the left wall under a pegboard, gondola
/// shelves down the right, an engine stand and a display table on the floor, fluorescent tubes overhead. No stock is
/// in the model: the game stands the shop's own parts in its places (shop.json, "places") when the player walks in.
///
/// Every surface is its own material ("sh_floor", "sh_wall", ...) and every picture of every look goes into the model,
/// so the game dresses one kind of shop differently from the next by pointing those materials at other pictures.
///
///   ShopSceneBuilder &lt;prepared folder&gt; &lt;out folder&gt; [--flip]
/// </summary>
internal static class Program
{
    // The room, in metres: x across (the tyre rack at -x), z from the back wall (-z) to the door (+z), y up
    private const float HalfWidth = 6f;
    private const float HalfDepth = 6f;
    private const float Height = 3.6f;

    // How big a picture is on the surface, in metres
    private const float FloorTile = 2f;
    private const float WallTile = 2.4f;
    private const float WoodTile = 1.2f;
    private const float SteelTile = 1f;

    // The back wall's shelving: four units of three places a level, five levels
    private const float BackFront = -3.98f;
    private const float BackRear = -4.46f;
    private const float UnitWidth = 2.7f;
    private static readonly float[] BackLevels = [0.12f, 0.62f, 1.12f, 1.62f, 2.12f];

    // The right wall's gondola: six places a level, four levels
    private const float RightFront = 5.35f;
    private const float RightRear = 5.96f;
    private const float RightFrom = -3.6f;
    private const float RightPitch = 0.9f;
    private static readonly float[] RightLevels = [0.12f, 0.72f, 1.32f, 1.92f];

    // The tyre rack: on the floor and on a steel shelf a metre up, seven places each
    private const float RackFront = -5.45f;
    private const float RackFrom = -3.6f;
    private const float RackPitch = 0.74f;
    private static readonly float[] RackLevels = [0.02f, 1.0f];

    private static readonly (float X, float Z)[] Tubes = [(-3f, -2.8f), (0f, -2.8f), (3f, -2.8f), (-3f, 0f), (0f, 0f), (3f, 0f), (-3f, 2.8f), (0f, 2.8f), (3f, 2.8f)];

    private static readonly Vec3 StandAt = new(-2.3f, 0f, 1.0f);
    private static readonly Vec3 TableAt = new(2.4f, 0f, 1.0f);

    private static bool _flip;

    private static int Main(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith("--")).ToList();
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("ShopSceneBuilder <prepared folder> <out folder> [--flip]");
            return 1;
        }

        var prepared = positional[0];
        var output = positional[1];
        _flip = args.Contains("--flip");

        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = "shop";

        AddTextures(kn5, prepared);

        var first = Looks[0];
        var floor = AddMaterial(kn5, "sh_floor", first.Floor, ambient: 0.34f, diffuse: 0.5f, specular: 0.18f, specularExp: 40f);
        var wall = AddMaterial(kn5, "sh_wall", first.Wall, ambient: 0.34f, diffuse: 0.48f);
        var ceiling = AddMaterial(kn5, "sh_ceiling", first.Ceiling, ambient: 0.36f, diffuse: 0.4f);
        var wood = AddMaterial(kn5, "sh_wood", first.Wood, ambient: 0.34f, diffuse: 0.5f, specular: 0.15f, specularExp: 30f);
        var steel = AddMaterial(kn5, "sh_steel", first.Steel, ambient: 0.32f, diffuse: 0.48f, specular: 0.25f, specularExp: 30f);
        var sign = AddMaterial(kn5, "sh_sign", first.Sign, ambient: 0.4f, diffuse: 0.5f, specular: 0.15f, specularExp: 30f);
        var pegboard = AddMaterial(kn5, "sh_pegboard", "pegboard.jpg", ambient: 0.34f, diffuse: 0.48f);
        var tube = AddMaterial(kn5, "sh_tube", "tube.jpg", ambient: 0f, diffuse: 0f, emissive: 1.4f);
        var window = AddMaterial(kn5, "sh_window", "window.jpg", ambient: 0f, diffuse: 0f, emissive: 1f);
        var till = AddMaterial(kn5, "sh_till", "till.jpg", ambient: 0.3f, diffuse: 0.45f, specular: 0.4f, specularExp: 50f);
        var chrome = AddMaterial(kn5, "sh_chrome", "chrome.jpg", ambient: 0.3f, diffuse: 0.4f, specular: 0.8f, specularExp: 80f);
        var rubber = AddMaterial(kn5, "sh_rubber", "rubber.jpg", ambient: 0.3f, diffuse: 0.4f);

        AddMesh(kn5, "SH_FLOOR", Floor(), floor);
        AddMesh(kn5, "SH_WALL", Walls(), wall);
        AddMesh(kn5, "SH_CEILING", Ceiling(), ceiling);
        AddMesh(kn5, "SH_WOOD", Woodwork(), wood);
        AddMesh(kn5, "SH_STEEL", Steelwork(), steel);
        AddMesh(kn5, "SH_SIGN", Sign(), sign);
        AddMesh(kn5, "SH_PEGBOARD", Pegboard(), pegboard);
        AddMesh(kn5, "SH_TUBE", TubeLights(), tube);
        AddMesh(kn5, "SH_WINDOW", Windows(), window);
        AddMesh(kn5, "SH_TILL", Till(), till);
        AddMesh(kn5, "SH_CHROME", Chrome(), chrome);
        AddMesh(kn5, "SH_RUBBER", Skirting(), rubber);

        SaveScene(kn5, output, "shop", Description().ToString(Formatting.Indented));
        return 0;
    }

    #region Looks

    /// <summary>What a kind of shop wears: a picture for each surface, its sign, and the colour of its tubes</summary>
    private sealed record Look(string Id, string[] Kinds, string Floor, string Wall, string Ceiling, string Wood, string Steel, string Sign, int[] Lamps);

    private static readonly Look[] Looks =
    [
        new("speed_shop", ["speed"], "floor_checker.jpg", "wall_painted_brick.jpg", "ceiling_grey_plaster.jpg",
            "wood_dark_wood.jpg", "steel_blue_metal_plate.jpg", "sign_speed.jpg", [245, 245, 255]),
        new("parts_counter", ["specialist"], "floor_linoleum_brown.jpg", "wall_plastered_wall_04.jpg", "ceiling_beige_wall_001.jpg",
            "wood_kitchen_wood.jpg", "steel_metal_plate.jpg", "sign_specialist.jpg", [255, 240, 215]),
        new("tire_shop", ["wheels"], "floor_garage_floor.jpg", "wall_concrete_block_wall.jpg", "ceiling_concrete_wall_004.jpg",
            "wood_plywood.jpg", "steel_painted_metal_shutter.jpg", "sign_wheels.jpg", [235, 245, 255]),
        new("yard_shed", ["junkyard"], "floor_concrete_floor_worn_001.jpg", "wall_raw_plank_wall.jpg", "ceiling_roof_planks.jpg",
            "wood_old_planks_02.jpg", "steel_green_metal_rust.jpg", "sign_junkyard.jpg", [255, 215, 160])
    ];

    #endregion

    #region Geometry

    // The shapes the scene builders share (tools/Shared/SceneMesh.cs), wound the other way round with --flip
    private sealed class Mesh() : SceneMesh(_flip);

    private static Vec3 V(float x, float y, float z) => new(x, y, z);

    private static Mesh Floor()
    {
        var mesh = new Mesh();
        mesh.Quad(V(-HalfWidth, 0f, -HalfDepth), V(HalfWidth, 0f, -HalfDepth), V(HalfWidth, 0f, HalfDepth), V(-HalfWidth, 0f, HalfDepth),
            Vec3.UnitY, p => new Vec2(p.X / FloorTile, p.Z / FloorTile));
        return mesh;
    }

    /// <summary>The four walls, facing in; the front one round the door and the two windows</summary>
    private static Mesh Walls()
    {
        var mesh = new Mesh();
        Vec2 Xy(Vec3 p) => new(p.X / WallTile, -p.Y / WallTile);
        Vec2 Zy(Vec3 p) => new(p.Z / WallTile, -p.Y / WallTile);

        mesh.Quad(V(-HalfWidth, 0f, -HalfDepth), V(HalfWidth, 0f, -HalfDepth), V(HalfWidth, Height, -HalfDepth), V(-HalfWidth, Height, -HalfDepth), Vec3.UnitZ, Xy);
        foreach (var x in new[] { -HalfWidth, HalfWidth })
            mesh.Quad(V(x, 0f, -HalfDepth), V(x, 0f, HalfDepth), V(x, Height, HalfDepth), V(x, Height, -HalfDepth), V(-MathF.Sign(x), 0f, 0f), Zy);

        // The front: under and over the windows, between them and the door, over the door
        const float z = HalfDepth;
        var normal = -Vec3.UnitZ;
        void Panel(float x0, float x1, float y0, float y1) =>
            mesh.Quad(V(x0, y0, z), V(x1, y0, z), V(x1, y1, z), V(x0, y1, z), normal, Xy);
        Panel(-HalfWidth, HalfWidth, 2.5f, Height);
        Panel(-HalfWidth, -5f, 0f, 2.5f);
        Panel(5f, HalfWidth, 0f, 2.5f);
        Panel(-5f, -1.6f, 0f, 0.9f);
        Panel(1.6f, 5f, 0f, 0.9f);
        Panel(-1.6f, -0.6f, 0f, 2.5f);
        Panel(0.6f, 1.6f, 0f, 2.5f);
        Panel(-0.6f, 0.6f, 2.2f, 2.5f);
        return mesh;
    }

    private static Mesh Ceiling()
    {
        var mesh = new Mesh();
        mesh.Quad(V(-HalfWidth, Height, -HalfDepth), V(HalfWidth, Height, -HalfDepth), V(HalfWidth, Height, HalfDepth), V(-HalfWidth, Height, HalfDepth),
            -Vec3.UnitY, p => new Vec2(p.X / WallTile, p.Z / WallTile));
        return mesh;
    }

    /// <summary>The counter, the shelves' boards, the display table's top and the door</summary>
    private static Mesh Woodwork()
    {
        var mesh = new Mesh();

        // The counter: its body, and a top a little proud of it
        mesh.Box(V(-2.6f, 0f, -2.4f), V(2.6f, 0.92f, -1.8f), WoodTile);
        mesh.Box(V(-2.66f, 0.92f, -2.46f), V(2.66f, 0.96f, -1.74f), WoodTile, bottom: true);

        // The back wall's boards
        for (var unit = 0; unit < 4; unit++)
        {
            var x0 = -2f * UnitWidth + unit * UnitWidth;
            foreach (var y in BackLevels)
                mesh.Box(V(x0 + 0.03f, y - 0.03f, BackRear), V(x0 + UnitWidth - 0.03f, y, BackFront), WoodTile, bottom: true);
        }

        // The display table's top
        mesh.Box(V(TableAt.X - 0.95f, 0.72f, TableAt.Z - 0.5f), V(TableAt.X + 0.95f, 0.76f, TableAt.Z + 0.5f), WoodTile, bottom: true);

        // The door, shut
        mesh.Quad(V(-0.6f, 0f, HalfDepth - 0.02f), V(0.6f, 0f, HalfDepth - 0.02f), V(0.6f, 2.2f, HalfDepth - 0.02f), V(-0.6f, 2.2f, HalfDepth - 0.02f),
            -Vec3.UnitZ, p => new Vec2(p.X / WoodTile, -p.Y / WoodTile));
        return mesh;
    }

    /// <summary>The shelving's uprights, the gondola, the tyre rack, the engine stand and the table's legs</summary>
    private static Mesh Steelwork()
    {
        var mesh = new Mesh();

        // Back wall: an upright at each corner of each unit
        for (var unit = 0; unit <= 4; unit++)
        {
            var x = -2f * UnitWidth + unit * UnitWidth;
            foreach (var z in new[] { BackRear + 0.02f, BackFront - 0.02f })
                mesh.Bar(V(x, 0f, z), V(x, 2.4f, z), 0.05f, SteelTile);
        }

        // Right wall: the gondola's back panel and uprights, its shelves of steel
        var rightTo = RightFrom + 6 * RightPitch;
        mesh.Box(V(RightRear - 0.04f, 0f, RightFrom), V(RightRear, 2.3f, rightTo), SteelTile);
        foreach (var y in RightLevels)
            mesh.Box(V(RightFront, y - 0.03f, RightFrom), V(RightRear - 0.04f, y, rightTo), SteelTile, bottom: true);
        for (var i = 0; i <= 6; i += 2)
        {
            var z = RightFrom + i * RightPitch;
            mesh.Bar(V(RightFront + 0.02f, 0f, z), V(RightFront + 0.02f, 2.3f, z), 0.05f, SteelTile);
        }

        // Left wall: the tyre rack's upper shelf on its posts, and a rail along the floor
        var rackTo = RackFrom + 7 * RackPitch;
        mesh.Box(V(-HalfWidth + 0.02f, RackLevels[1] - 0.03f, RackFrom), V(RackFront, RackLevels[1], rackTo), SteelTile, bottom: true);
        for (var i = 0; i <= 7; i++)
        {
            var z = RackFrom + i * RackPitch;
            mesh.Bar(V(RackFront, 0f, z), V(RackFront, 2.05f, z), 0.04f, SteelTile);
        }

        mesh.Bar(V(RackFront, 0.06f, RackFrom), V(RackFront, 0.06f, rackTo), 0.04f, SteelTile);
        mesh.Bar(V(RackFront, RackLevels[1] + 0.06f, RackFrom), V(RackFront, RackLevels[1] + 0.06f, rackTo), 0.04f, SteelTile);

        // The engine stand: a cross of legs on the floor, a post, the arm and the head the engine bolts to
        var s = StandAt;
        mesh.Bar(V(s.X - 0.5f, 0.04f, s.Z), V(s.X + 0.5f, 0.04f, s.Z), 0.07f, SteelTile);
        mesh.Bar(V(s.X, 0.04f, s.Z - 0.45f), V(s.X, 0.04f, s.Z + 0.45f), 0.07f, SteelTile);
        mesh.Bar(V(s.X, 0f, s.Z), V(s.X, 0.42f, s.Z), 0.08f, SteelTile);
        mesh.Box(V(s.X - 0.2f, 0.42f, s.Z - 0.2f), V(s.X + 0.2f, 0.45f, s.Z + 0.2f), SteelTile, bottom: true);

        // The display table's legs
        var t = TableAt;
        foreach (var dx in new[] { -0.9f, 0.9f })
        foreach (var dz in new[] { -0.45f, 0.45f })
            mesh.Bar(V(t.X + dx, 0f, t.Z + dz), V(t.X + dx, 0.72f, t.Z + dz), 0.05f, SteelTile);

        // The tubes' fittings
        foreach (var (x, z) in Tubes)
            mesh.Box(V(x - 0.66f, Height - 0.06f, z - 0.1f), V(x + 0.66f, Height, z + 0.1f), SteelTile, bottom: false);

        return mesh;
    }

    /// <summary>The board over the back shelves, the shop's word on it</summary>
    private static Mesh Sign()
    {
        const float x0 = -2.6f;
        const float x1 = 2.6f;
        const float y0 = 2.66f;
        const float y1 = 3.31f;
        const float z = -HalfDepth + 0.04f;
        var mesh = new Mesh();
        mesh.Quad(V(x0, y0, z), V(x1, y0, z), V(x1, y1, z), V(x0, y1, z), Vec3.UnitZ, p => new Vec2((p.X - x0) / (x1 - x0), (y1 - p.Y) / (y1 - y0)));
        return mesh;
    }

    /// <summary>Hardboard over the tyre rack, a metre to the picture</summary>
    private static Mesh Pegboard()
    {
        const float x = -HalfWidth + 0.02f;
        var mesh = new Mesh();
        mesh.Quad(V(x, 2.1f, RackFrom), V(x, 2.1f, RackFrom + 7 * RackPitch), V(x, 3f, RackFrom + 7 * RackPitch), V(x, 3f, RackFrom), Vec3.UnitX,
            p => new Vec2(p.Z, -p.Y));
        return mesh;
    }

    private static Mesh TubeLights()
    {
        var mesh = new Mesh();
        foreach (var (x, z) in Tubes)
        {
            foreach (var dz in new[] { -0.045f, 0.045f })
                mesh.Box(V(x - 0.6f, Height - 0.1f, z + dz - 0.018f), V(x + 0.6f, Height - 0.064f, z + dz + 0.018f), 1f, bottom: true);
        }

        return mesh;
    }

    /// <summary>The front windows, seen from inside: the bright day beyond them</summary>
    private static Mesh Windows()
    {
        var mesh = new Mesh();
        const float z = HalfDepth - 0.01f;
        foreach (var (x0, x1) in new[] { (-5f, -1.6f), (1.6f, 5f) })
            mesh.Quad(V(x0, 0.9f, z), V(x1, 0.9f, z), V(x1, 2.5f, z), V(x0, 2.5f, z), -Vec3.UnitZ, p => new Vec2(0.5f, 0.5f));
        return mesh;
    }

    /// <summary>The till on the counter: a body with its keys sloping to the front, and the drawer under it</summary>
    private static Mesh Till()
    {
        var mesh = new Mesh();
        mesh.Box(V(1.6f, 0.96f, -2.32f), V(2.05f, 1.06f, -1.94f), 0.5f);
        mesh.Box(V(1.65f, 1.06f, -2.32f), V(2f, 1.3f, -2.12f), 0.5f);
        return mesh;
    }

    /// <summary>The till's sign holder and the counter's edge trim</summary>
    private static Mesh Chrome()
    {
        var mesh = new Mesh();
        mesh.Box(V(1.7f, 1.3f, -2.27f), V(1.95f, 1.42f, -2.24f), 0.5f);
        mesh.Bar(V(-2.66f, 0.94f, -1.73f), V(2.66f, 0.94f, -1.73f), 0.025f, 0.5f);
        return mesh;
    }

    /// <summary>Black rubber along the foot of the walls and the counter</summary>
    private static Mesh Skirting()
    {
        var mesh = new Mesh();
        mesh.Box(V(-HalfWidth, 0f, -HalfDepth), V(HalfWidth, 0.1f, -HalfDepth + 0.015f), 0.5f);
        mesh.Box(V(-HalfWidth, 0f, -HalfDepth), V(-HalfWidth + 0.015f, 0.1f, HalfDepth), 0.5f);
        mesh.Box(V(HalfWidth - 0.015f, 0f, -HalfDepth), V(HalfWidth, 0.1f, HalfDepth), 0.5f);
        mesh.Box(V(-2.62f, 0f, -1.79f), V(2.62f, 0.1f, -1.775f), 0.5f);
        return mesh;
    }

    #endregion

    #region Places

    /// <summary>
    /// A place a part stands in: the middle of its floor, how much room it has (width along its own x, depth along its
    /// own z, height), the way it faces (degrees about the vertical; 0 faces +z, towards the door), and what goes there
    /// </summary>
    private sealed record Place(float X, float Y, float Z, float Width, float Depth, float Height, float Heading, string Holds);

    private static IEnumerable<Place> Places()
    {
        // The engine stand and the display table first: what the eye goes to
        yield return new Place(StandAt.X, 0.45f, StandAt.Z, 1.0f, 0.9f, 1.1f, 25f, "engine");
        yield return new Place(TableAt.X - 0.45f, 0.76f, TableAt.Z, 0.85f, 0.9f, 0.8f, -15f, "large");
        yield return new Place(TableAt.X + 0.45f, 0.76f, TableAt.Z, 0.85f, 0.9f, 0.8f, -15f, "large");

        // On the counter, beside the till
        yield return new Place(-1.4f, 0.96f, -2.1f, 0.8f, 0.6f, 0.5f, 0f, "shelf");
        yield return new Place(0.2f, 0.96f, -2.1f, 0.8f, 0.6f, 0.5f, 0f, "shelf");

        // The back wall, eye height first, then up and down from it
        foreach (var level in new[] { 2, 1, 3, 0, 4 })
        {
            for (var unit = 0; unit < 4; unit++)
            {
                for (var k = 0; k < 3; k++)
                {
                    var x = -2f * UnitWidth + unit * UnitWidth + 0.45f + k * 0.9f;
                    yield return new Place(x, BackLevels[level], (BackFront + BackRear) / 2f, 0.85f, 0.46f, 0.45f, 0f, level == 0 ? "large" : "shelf");
                }
            }
        }

        // The tyre rack: facing the room
        foreach (var y in RackLevels)
        {
            for (var i = 0; i < 7; i++)
                yield return new Place((-HalfWidth + RackFront) / 2f, y, RackFrom + (i + 0.5f) * RackPitch, 0.72f, 0.5f, 0.95f, 90f, "wheel");
        }

        // The right wall's gondola: facing the room
        foreach (var (y, level) in RightLevels.Select((y, i) => (y, i)))
        {
            for (var i = 0; i < 6; i++)
                yield return new Place((RightFront + RightRear - 0.04f) / 2f, y, RightFrom + (i + 0.5f) * RightPitch, 0.85f, 0.55f, 0.55f, -90f,
                    level == 0 ? "large" : "shelf");
        }
    }

    #endregion

    #region Description

    private static JObject Description()
    {
        static JArray Rgb(int r, int g, int b) => new(r, g, b);
        static float Round(float v) => MathF.Round(v, 3);

        var lamps = new JArray(Tubes.Select(t => new JObject
        {
            ["x"] = t.X, ["y"] = Round(Height - 0.3f), ["z"] = t.Z, ["brightness"] = 0.7f, ["range"] = 8f
        }));

        JObject Light(float brightness, JArray color, float ambient, JArray up, JArray down, float lampScale) => new()
        {
            ["brightness"] = brightness, ["color"] = color, ["ambient"] = ambient, ["ambientUp"] = up, ["ambientDown"] = down,
            ["cubemapAmbient"] = 0f, ["lampScale"] = lampScale
        };

        return new JObject
        {
            ["_comment"] = new JArray(
                "The inside of a part shop (the part shop screen). shop.kn5 and this file are written by tools/ShopScene",
                "(prepare_shop.py, then ShopSceneBuilder): change the builder, not these files.",
                "x runs across the shop (the tyre rack at -x), z from the back wall (-z) to the door (+z), y up; the middle of the floor is the origin.",
                "camera: the view from inside the door, orbiting a point (targetX, targetHeight, targetZ) (alpha pi/2 = the camera towards",
                "the door, radians; beta up from level; radius metres; fov the lens, degrees high); it sways a little by itself and the",
                "player can look round it.",
                "room: the inside the camera stays in: the walls halfWidth either side of the middle along x and halfDepth along z, the ceiling height up.",
                "places: where the game stands the shop's parts as the player walks in: the middle of the place's floor, its room (width",
                "along its own x, depth along its own z, height), heading in degrees about the vertical (0 faces +z), and holds: engine (a",
                "whole engine, else the biggest part), wheel (tyres and rims), large (anything that fits), shelf (anything that fits).",
                "sun: the key light's direction, in through the windows. lights: the renderer's light for each light of the day; the",
                "tubes' lamps are lit at every hour, each look's colour; lampScale is how bright they are then.",
                "looks: what a shop wears, the picture on each surface and its sign; a shop wears the look its partshops entry names",
                "(\"look\"), else the first whose kinds hold its kind, else the first.",
                "Textures: Poly Haven (CC0), the rest drawn by prepare_shop.py."),
            ["id"] = "shop",
            ["title"] = "The part shop",
            ["credit"] = PolyHavenCredit(Looks.SelectMany(l => new[] { l.Floor, l.Wall, l.Ceiling, l.Wood, l.Steel })),
            ["camera"] = new JObject
            {
                ["alpha"] = Round(MathF.PI / 2f), ["beta"] = 0.17f, ["radius"] = 6.8f, ["targetX"] = 0f, ["targetHeight"] = 1.05f, ["targetZ"] = -1.0f, ["fov"] = 50f
            },
            ["room"] = new JObject { ["halfWidth"] = HalfWidth, ["halfDepth"] = HalfDepth, ["height"] = Height },
            ["sun"] = new JArray(0.2f, 0.8f, 0.6f),
            ["lights"] = new JObject
            {
                ["day"] = Light(0.45f, Rgb(250, 240, 220), 0.8f, Rgb(175, 180, 185), Rgb(120, 112, 100), 0.85f),
                ["dusk"] = Light(0.25f, Rgb(255, 190, 150), 0.75f, Rgb(140, 130, 135), Rgb(100, 90, 80), 1f),
                ["night"] = Light(0.05f, Rgb(140, 160, 220), 0.65f, Rgb(110, 112, 120), Rgb(80, 72, 64), 1.1f)
            },
            ["window"] = new JObject { ["day"] = 1.1f, ["dusk"] = 0.6f, ["night"] = 0.06f },
            ["lamps"] = lamps,
            ["places"] = new JArray(Places().Select(p => new JObject
            {
                ["x"] = Round(p.X), ["y"] = Round(p.Y), ["z"] = Round(p.Z), ["width"] = p.Width, ["depth"] = p.Depth, ["height"] = p.Height,
                ["heading"] = p.Heading, ["holds"] = p.Holds
            })),
            ["looks"] = new JArray(Looks.Select(l => new JObject
            {
                ["id"] = l.Id,
                ["kinds"] = new JArray(l.Kinds),
                ["surfaces"] = new JObject
                {
                    ["sh_floor"] = l.Floor, ["sh_wall"] = l.Wall, ["sh_ceiling"] = l.Ceiling, ["sh_wood"] = l.Wood, ["sh_steel"] = l.Steel,
                    ["sh_sign"] = l.Sign
                },
                ["lampColor"] = new JArray(l.Lamps)
            }))
        };
    }

    #endregion

    // The key light comes in through the windows and the tubes light from overhead: nothing of the room stands in the
    // way of either (Kn5SceneWriter.AddMesh makes every mesh shadowless); the parts on the shelves cast their own
    private static void AddMesh(IKn5 kn5, string name, SceneMesh mesh, uint material) =>
        Kn5SceneWriter.AddMesh(kn5, name, mesh.Vertices, mesh.Indices, material);
}
