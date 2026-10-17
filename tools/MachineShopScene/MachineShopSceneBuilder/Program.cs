using AcTools.Kn5File;
using AcTools.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreetCorsa.Tools;
using static StreetCorsa.Tools.Kn5SceneWriter;

namespace MachineShopSceneBuilder;

/// <summary>
/// Builds the inside of a machine shop (the machine shop screen): shop.kn5 and the shop.json that goes with it, out of
/// the pictures prepare_machine_shop.py gathered.
///
/// A shop 12 m across and 12 m deep, seen from just inside the door: the counter across the front, and behind it the
/// machines: a boring bar on its cabinet (left), the crank grinder along the back wall under the sign, the head bench
/// with its surfacing mill (right), the hot tank in the back corner, two engine stands down the left wall under a tool
/// board and a rack of customers' work down the right. No parts are in the model: the game stands the player's work and
/// the day's other jobs in its places (shop.json, "places") as they walk in. The scene is the machine shops' alone
/// (shop.json, "kinds").
///
///   MachineShopSceneBuilder &lt;prepared folder&gt; &lt;out folder&gt; [--flip]
/// </summary>
internal static class Program
{
    // The room, in metres: x across (the engine stands at -x), z from the back wall (-z) to the door (+z), y up
    private const float HalfWidth = 6f;
    private const float HalfDepth = 6f;
    private const float Height = 4f;

    private const float FloorTile = 2f;
    private const float WallTile = 2.4f;
    private const float WoodTile = 1.2f;
    private const float SteelTile = 1f;

    // The counter, across the front of the shop
    private const float CounterFront = 2.9f;
    private const float CounterBack = 2.3f;
    private const float CounterHalf = 2.8f;
    private const float CounterTop = 0.95f;

    // The boring bar's cabinet and its table
    private static readonly Vec3 BoringAt = new(-3.0f, 0f, -0.9f);
    private const float BoringTable = 0.8f;

    // The crank grinder's bed along the back wall
    private const float GrinderZ = -4.85f;
    private const float GrinderBed = 0.85f;

    // The head bench and its mill
    private static readonly Vec3 HeadBenchAt = new(3.2f, 0f, -0.9f);
    private const float HeadBench = 0.85f;

    // The engine stands down the left wall
    private static readonly Vec3[] StandsAt = [new(-4.6f, 0f, -0.2f), new(-4.6f, 0f, -2.9f)];

    // The rack of customers' work down the right wall: three levels, five places a level
    private const float RackFront = 5.3f;
    private const float RackRear = 5.95f;
    private const float RackFrom = -4.2f;
    private const float RackPitch = 1.0f;
    private static readonly float[] RackLevels = [0.12f, 0.75f, 1.38f];

    private static readonly (float X, float Z)[] Tubes = [(-3f, -3.2f), (0f, -3.2f), (3f, -3.2f), (-3f, -0.4f), (0f, -0.4f), (3f, -0.4f), (-3f, 2.4f), (0f, 2.4f), (3f, 2.4f)];

    private static bool _flip;

    private static int Main(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith("--")).ToList();
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("MachineShopSceneBuilder <prepared folder> <out folder> [--flip]");
            return 1;
        }

        var prepared = positional[0];
        var output = positional[1];
        _flip = args.Contains("--flip");

        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = "machine_shop";

        AddTextures(kn5, prepared);

        var floor = AddMaterial(kn5, "sh_floor", Look.Floor, ambient: 0.34f, diffuse: 0.5f, specular: 0.2f, specularExp: 30f);
        var wall = AddMaterial(kn5, "sh_wall", Look.Wall, ambient: 0.34f, diffuse: 0.48f);
        var ceiling = AddMaterial(kn5, "sh_ceiling", Look.Ceiling, ambient: 0.36f, diffuse: 0.4f);
        var wood = AddMaterial(kn5, "sh_wood", Look.Wood, ambient: 0.34f, diffuse: 0.5f, specular: 0.15f, specularExp: 30f);
        var steel = AddMaterial(kn5, "sh_steel", Look.Steel, ambient: 0.32f, diffuse: 0.48f, specular: 0.25f, specularExp: 30f);
        var sign = AddMaterial(kn5, "sh_sign", Look.Sign, ambient: 0.4f, diffuse: 0.5f, specular: 0.15f, specularExp: 30f);
        var machine = AddMaterial(kn5, "sh_machine", "machine.jpg", ambient: 0.32f, diffuse: 0.5f, specular: 0.35f, specularExp: 40f);
        var pegboard = AddMaterial(kn5, "sh_pegboard", "pegboard.jpg", ambient: 0.34f, diffuse: 0.48f);
        var tube = AddMaterial(kn5, "sh_tube", "tube.jpg", ambient: 0f, diffuse: 0f, emissive: 1.4f);
        var window = AddMaterial(kn5, "sh_window", "window.jpg", ambient: 0f, diffuse: 0f, emissive: 1f);
        var tank = AddMaterial(kn5, "sh_tank", "tank.jpg", ambient: 0.3f, diffuse: 0.45f, specular: 0.3f, specularExp: 30f);
        var chrome = AddMaterial(kn5, "sh_chrome", "chrome.jpg", ambient: 0.3f, diffuse: 0.4f, specular: 0.8f, specularExp: 80f);
        var grit = AddMaterial(kn5, "sh_grit", "grit.jpg", ambient: 0.32f, diffuse: 0.45f);
        var tickets = AddMaterial(kn5, "sh_tickets", "tickets.jpg", ambient: 0.36f, diffuse: 0.5f);
        var rubber = AddMaterial(kn5, "sh_rubber", "rubber.jpg", ambient: 0.3f, diffuse: 0.4f);

        AddMesh(kn5, "SH_FLOOR", Floor(), floor);
        AddMesh(kn5, "SH_WALL", Walls(), wall);
        AddMesh(kn5, "SH_CEILING", Ceiling(), ceiling);
        AddMesh(kn5, "SH_WOOD", Woodwork(), wood);
        AddMesh(kn5, "SH_STEEL", Steelwork(), steel);
        AddMesh(kn5, "SH_SIGN", Sign(), sign);
        AddMesh(kn5, "SH_MACHINE", Machines(), machine);
        AddMesh(kn5, "SH_PEGBOARD", Pegboard(), pegboard);
        AddMesh(kn5, "SH_TUBE", TubeLights(), tube);
        AddMesh(kn5, "SH_WINDOW", Windows(), window);
        AddMesh(kn5, "SH_TANK", HotTank(), tank);
        AddMesh(kn5, "SH_CHROME", Chrome(), chrome);
        AddMesh(kn5, "SH_GRIT", Wheels(), grit);
        AddMesh(kn5, "SH_TICKETS", Tickets(), tickets);
        AddMesh(kn5, "SH_RUBBER", Skirting(), rubber);

        SaveScene(kn5, output, "shop", Description().ToString(Formatting.Indented));
        return 0;
    }

    /// <summary>What the machine shop wears: a picture for each surface, its sign, and the colour of its tubes</summary>
    private static class Look
    {
        public const string Id = "machine_shop";
        public const string Floor = "floor_concrete_floor_worn_001.jpg";
        public const string Wall = "wall_concrete_block_wall.jpg";
        public const string Ceiling = "ceiling_concrete_wall_004.jpg";
        public const string Wood = "wood_plywood.jpg";
        public const string Steel = "steel_metal_plate.jpg";
        public const string Sign = "sign_machine.jpg";
        public static readonly int[] Lamps = [240, 245, 255];
    }

    #region Geometry

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

        const float z = HalfDepth;
        var normal = -Vec3.UnitZ;
        void Panel(float x0, float x1, float y0, float y1) =>
            mesh.Quad(V(x0, y0, z), V(x1, y0, z), V(x1, y1, z), V(x0, y1, z), normal, Xy);
        Panel(-HalfWidth, HalfWidth, 2.5f, Height);
        Panel(-HalfWidth, -5f, 0f, 2.5f);
        Panel(5f, HalfWidth, 0f, 2.5f);
        Panel(-5f, -1.6f, 0f, 1.0f);
        Panel(1.6f, 5f, 0f, 1.0f);
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

    /// <summary>The counter, the head bench's top, the rack's boards and the door</summary>
    private static Mesh Woodwork()
    {
        var mesh = new Mesh();

        mesh.Box(V(-CounterHalf, 0f, CounterBack), V(CounterHalf, CounterTop - 0.04f, CounterFront), WoodTile);
        mesh.Box(V(-CounterHalf - 0.06f, CounterTop - 0.04f, CounterBack - 0.06f), V(CounterHalf + 0.06f, CounterTop, CounterFront + 0.06f), WoodTile, bottom: true);

        var h = HeadBenchAt;
        mesh.Box(V(h.X - 1.0f, HeadBench - 0.05f, h.Z - 0.45f), V(h.X + 1.0f, HeadBench, h.Z + 0.45f), WoodTile, bottom: true);

        var rackTo = RackFrom + 5 * RackPitch;
        foreach (var y in RackLevels.Skip(1))
            mesh.Box(V(RackFront, y - 0.04f, RackFrom), V(RackRear - 0.04f, y, rackTo), WoodTile, bottom: true);

        mesh.Quad(V(-0.6f, 0f, HalfDepth - 0.02f), V(0.6f, 0f, HalfDepth - 0.02f), V(0.6f, 2.2f, HalfDepth - 0.02f), V(-0.6f, 2.2f, HalfDepth - 0.02f),
            -Vec3.UnitZ, p => new Vec2(p.X / WoodTile, -p.Y / WoodTile));
        return mesh;
    }

    /// <summary>The rack's uprights and back, the engine stands, the head bench's legs and the tubes' fittings</summary>
    private static Mesh Steelwork()
    {
        var mesh = new Mesh();

        var rackTo = RackFrom + 5 * RackPitch;
        mesh.Box(V(RackRear - 0.04f, 0f, RackFrom), V(RackRear, 2.0f, rackTo), SteelTile);
        for (var i = 0; i <= 5; i++)
        {
            var z = RackFrom + i * RackPitch;
            mesh.Bar(V(RackFront + 0.02f, 0f, z), V(RackFront + 0.02f, 2.0f, z), 0.05f, SteelTile);
        }

        // The engine stands: a cross of legs on the floor, a post, the head the engine bolts to
        foreach (var s in StandsAt)
        {
            mesh.Bar(V(s.X - 0.5f, 0.04f, s.Z), V(s.X + 0.5f, 0.04f, s.Z), 0.07f, SteelTile);
            mesh.Bar(V(s.X, 0.04f, s.Z - 0.45f), V(s.X, 0.04f, s.Z + 0.45f), 0.07f, SteelTile);
            mesh.Bar(V(s.X, 0f, s.Z), V(s.X, 0.42f, s.Z), 0.08f, SteelTile);
            mesh.Box(V(s.X - 0.2f, 0.42f, s.Z - 0.2f), V(s.X + 0.2f, 0.45f, s.Z + 0.2f), SteelTile, bottom: true);
        }

        var h = HeadBenchAt;
        foreach (var dx in new[] { -0.95f, 0.95f })
        foreach (var dz in new[] { -0.4f, 0.4f })
            mesh.Bar(V(h.X + dx, 0f, h.Z + dz), V(h.X + dx, HeadBench - 0.05f, h.Z + dz), 0.06f, SteelTile);

        foreach (var (x, z) in Tubes)
            mesh.Box(V(x - 0.66f, Height - 0.06f, z - 0.1f), V(x + 0.66f, Height, z + 0.1f), SteelTile, bottom: false);

        return mesh;
    }

    /// <summary>The board over the crank grinder, the shop's word on it</summary>
    private static Mesh Sign()
    {
        const float x0 = -2.4f;
        const float x1 = 2.4f;
        const float y0 = 2.9f;
        const float y1 = 3.5f;
        const float z = -HalfDepth + 0.04f;
        var mesh = new Mesh();
        mesh.Quad(V(x0, y0, z), V(x1, y0, z), V(x1, y1, z), V(x0, y1, z), Vec3.UnitZ, p => new Vec2((p.X - x0) / (x1 - x0), (y1 - p.Y) / (y1 - y0)));
        return mesh;
    }

    /// <summary>The machines in their enamel: the boring bar, the crank grinder, the surfacing mill</summary>
    private static Mesh Machines()
    {
        var mesh = new Mesh();
        const float tile = 0.8f;

        // The boring bar: a cabinet with the table on it, a column at the back, the head reaching out over the table
        var b = BoringAt;
        mesh.Box(V(b.X - 0.6f, 0f, b.Z - 0.5f), V(b.X + 0.6f, BoringTable - 0.05f, b.Z + 0.5f), tile);
        mesh.Box(V(b.X - 0.2f, BoringTable, b.Z - 0.62f), V(b.X + 0.2f, 2.35f, b.Z - 0.4f), tile);
        mesh.Box(V(b.X - 0.18f, 1.95f, b.Z - 0.42f), V(b.X + 0.18f, 2.25f, b.Z + 0.05f), tile, bottom: true);
        mesh.Box(V(b.X - 0.12f, 2.25f, b.Z - 0.55f), V(b.X + 0.12f, 2.55f, b.Z - 0.25f), tile);

        // The crank grinder: its long bed, the headstock and tailstock, the guard over the wheel at the back
        mesh.Box(V(-1.9f, 0f, GrinderZ - 0.45f), V(1.9f, GrinderBed, GrinderZ + 0.35f), tile);
        mesh.Box(V(-1.9f, GrinderBed, GrinderZ - 0.3f), V(-1.3f, GrinderBed + 0.5f, GrinderZ + 0.3f), tile, bottom: true);
        mesh.Box(V(1.3f, GrinderBed, GrinderZ - 0.25f), V(1.7f, GrinderBed + 0.4f, GrinderZ + 0.25f), tile, bottom: true);
        mesh.Box(V(-0.5f, GrinderBed, GrinderZ - 0.95f), V(0.5f, GrinderBed + 0.85f, GrinderZ - 0.45f), tile, bottom: true);
        mesh.Box(V(-0.7f, 0f, GrinderZ - 1.1f), V(0.7f, GrinderBed, GrinderZ - 0.45f), tile);

        // The surfacing mill behind the head bench: its column, and the head over the bench
        var h = HeadBenchAt;
        mesh.Box(V(h.X + 0.5f, 0f, h.Z - 0.75f), V(h.X + 0.9f, 2.2f, h.Z - 0.45f), tile);
        mesh.Box(V(h.X + 0.45f, 1.55f, h.Z - 0.5f), V(h.X + 0.95f, 1.9f, h.Z + 0.1f), tile, bottom: true);
        return mesh;
    }

    /// <summary>The hot tank in the back corner: a black steel tank with its lid propped open</summary>
    private static Mesh HotTank()
    {
        var mesh = new Mesh();
        mesh.Box(V(-5.5f, 0f, -5.6f), V(-3.7f, 0.9f, -4.4f), SteelTile);
        mesh.Box(V(-5.5f, 0.9f, -5.65f), V(-3.7f, 1.95f, -5.58f), SteelTile, bottom: true);
        return mesh;
    }

    /// <summary>Ground steel: the boring bar's bar, the grinder's centres, the mill's cutter, the counter's edge, the till</summary>
    private static Mesh Chrome()
    {
        var mesh = new Mesh();
        var b = BoringAt;
        mesh.Bar(V(b.X, 1.75f, b.Z - 0.1f), V(b.X, 1.95f, b.Z - 0.1f), 0.06f, 0.5f);
        mesh.Box(V(b.X - 0.55f, BoringTable - 0.05f, b.Z - 0.45f), V(b.X + 0.55f, BoringTable, b.Z + 0.45f), 0.5f, bottom: true);
        mesh.Bar(V(-1.3f, GrinderBed + 0.2f, GrinderZ), V(-1.05f, GrinderBed + 0.2f, GrinderZ), 0.05f, 0.5f);
        mesh.Bar(V(1.05f, GrinderBed + 0.2f, GrinderZ), V(1.3f, GrinderBed + 0.2f, GrinderZ), 0.05f, 0.5f);
        var h = HeadBenchAt;
        mesh.Box(V(h.X + 0.55f, 1.45f, h.Z - 0.3f), V(h.X + 0.85f, 1.55f, h.Z), 0.5f, bottom: true);
        mesh.Bar(V(-CounterHalf - 0.06f, CounterTop - 0.02f, CounterFront + 0.07f), V(CounterHalf + 0.06f, CounterTop - 0.02f, CounterFront + 0.07f), 0.025f, 0.5f);
        mesh.Box(V(1.7f, CounterTop, 2.42f), V(2.15f, CounterTop + 0.12f, 2.78f), 0.5f);
        mesh.Box(V(1.75f, CounterTop + 0.12f, 2.42f), V(2.1f, CounterTop + 0.34f, 2.6f), 0.5f);
        return mesh;
    }

    /// <summary>The grinding wheel in its guard, its edge showing toward the bed</summary>
    private static Mesh Wheels()
    {
        var mesh = new Mesh();
        mesh.Box(V(-0.38f, GrinderBed + 0.05f, GrinderZ - 0.46f), V(0.38f, GrinderBed + 0.75f, GrinderZ - 0.3f), 0.5f);
        return mesh;
    }

    /// <summary>The pad of work tickets by the till, and the spike of the ones done</summary>
    private static Mesh Tickets()
    {
        var mesh = new Mesh();
        mesh.Box(V(0.6f, CounterTop, 2.45f), V(0.95f, CounterTop + 0.03f, 2.75f), 0.3f);
        return mesh;
    }

    /// <summary>Hardboard over the engine stands, the shop's tools on it in the picture's holes</summary>
    private static Mesh Pegboard()
    {
        const float x = -HalfWidth + 0.02f;
        var mesh = new Mesh();
        mesh.Quad(V(x, 1.3f, -3.4f), V(x, 1.3f, 2.4f), V(x, 2.5f, 2.4f), V(x, 2.5f, -3.4f), Vec3.UnitX, p => new Vec2(p.Z, -p.Y));
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

    private static Mesh Windows()
    {
        var mesh = new Mesh();
        const float z = HalfDepth - 0.01f;
        foreach (var (x0, x1) in new[] { (-5f, -1.6f), (1.6f, 5f) })
            mesh.Quad(V(x0, 1.0f, z), V(x1, 1.0f, z), V(x1, 2.5f, z), V(x0, 2.5f, z), -Vec3.UnitZ, p => new Vec2(0.5f, 0.5f));
        return mesh;
    }

    private static Mesh Skirting()
    {
        var mesh = new Mesh();
        mesh.Box(V(-HalfWidth, 0f, -HalfDepth), V(HalfWidth, 0.1f, -HalfDepth + 0.015f), 0.5f);
        mesh.Box(V(-HalfWidth, 0f, -HalfDepth), V(-HalfWidth + 0.015f, 0.1f, HalfDepth), 0.5f);
        mesh.Box(V(HalfWidth - 0.015f, 0f, -HalfDepth), V(HalfWidth, 0.1f, HalfDepth), 0.5f);
        mesh.Box(V(-CounterHalf + 0.04f, 0f, CounterFront), V(CounterHalf - 0.04f, 0.1f, CounterFront + 0.015f), 0.5f);
        return mesh;
    }

    #endregion

    #region Places

    /// <summary>A place a part stands in: the middle of its floor, its room, the way it faces, and what goes there</summary>
    private sealed record Place(float X, float Y, float Z, float Width, float Depth, float Height, float Heading, string Holds);

    private static IEnumerable<Place> Places()
    {
        // The machines first: a block on the boring bar, a crank in the grinder, heads on the bench
        yield return new Place(BoringAt.X, BoringTable, BoringAt.Z, 1.05f, 0.85f, 0.75f, 15f, "engine_blocks");
        yield return new Place(0f, GrinderBed + 0.05f, GrinderZ, 2.3f, 0.42f, 0.4f, 0f, "crankshafts");
        yield return new Place(HeadBenchAt.X - 0.5f, HeadBench, HeadBenchAt.Z + 0.05f, 0.9f, 0.75f, 0.55f, -10f, "cylinder_heads");

        // The engine stands (a whole engine lies along the wall), then the counter beside the tickets
        foreach (var s in StandsAt) yield return new Place(s.X, 0.45f, s.Z, 1.7f, 1.0f, 1.3f, 90f, "engine");
        yield return new Place(-1.6f, CounterTop, (CounterBack + CounterFront) / 2f, 0.9f, 0.55f, 0.5f, 0f, "shelf");
        yield return new Place(-0.3f, CounterTop, (CounterBack + CounterFront) / 2f, 0.8f, 0.55f, 0.5f, 0f, "shelf");

        // The rack of customers' work: the middle level first, then up and down
        foreach (var level in new[] { 1, 2, 0 })
        {
            for (var i = 0; i < 5; i++)
                yield return new Place((RackFront + RackRear - 0.04f) / 2f, RackLevels[level], RackFrom + (i + 0.5f) * RackPitch, 0.95f, 0.58f, 0.58f, -90f,
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
            ["x"] = t.X, ["y"] = Round(Height - 0.3f), ["z"] = t.Z, ["brightness"] = 0.75f, ["range"] = 8f
        }));

        JObject Light(float brightness, JArray color, float ambient, JArray up, JArray down, float lampScale) => new()
        {
            ["brightness"] = brightness, ["color"] = color, ["ambient"] = ambient, ["ambientUp"] = up, ["ambientDown"] = down,
            ["cubemapAmbient"] = 0f, ["lampScale"] = lampScale
        };

        return new JObject
        {
            ["_comment"] = new JArray(
                "The inside of a machine shop (the machine shop screen). shop.kn5 and this file are written by tools/MachineShopScene",
                "(prepare_machine_shop.py, then MachineShopSceneBuilder): change the builder, not these files.",
                "kinds: the kinds of part shop seen in this scene alone (partshops' \"kind\"); a scene with none is every other shop's.",
                "x runs across the shop (the engine stands at -x), z from the back wall (-z) to the door (+z), y up; the middle of the floor is the origin.",
                "camera, room, places, sun, lights and looks as the part shop's (shopscenes/shop/shop.json). The places hold the",
                "player's work at this shop first, then the day's other jobs; a place may hold a shelf's group by its id, that kind",
                "of part first and alone: a block on the boring bar, a crank in the grinder, a head on the bench; whole engines on",
                "the stands, the rest on the counter and in the rack.",
                "Textures: Poly Haven (CC0), the rest drawn by prepare_machine_shop.py."),
            ["id"] = "machine_shop",
            ["title"] = "The machine shop",
            ["kinds"] = new JArray("machine"),
            ["credit"] = PolyHavenCredit([Look.Floor, Look.Wall, Look.Ceiling, Look.Wood, Look.Steel]),
            ["camera"] = new JObject
            {
                ["alpha"] = Round(MathF.PI / 2f), ["beta"] = 0.17f, ["radius"] = 7.0f, ["targetX"] = 0f, ["targetHeight"] = 1.1f, ["targetZ"] = -1.6f, ["fov"] = 50f
            },
            ["room"] = new JObject { ["halfWidth"] = HalfWidth, ["halfDepth"] = HalfDepth, ["height"] = Height },
            ["sun"] = new JArray(0.2f, 0.8f, 0.6f),
            ["lights"] = new JObject
            {
                ["day"] = Light(0.45f, Rgb(250, 240, 220), 0.8f, Rgb(170, 178, 185), Rgb(115, 110, 100), 0.85f),
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
            ["looks"] = new JArray(new JObject
            {
                ["id"] = Look.Id,
                ["kinds"] = new JArray("machine"),
                ["surfaces"] = new JObject
                {
                    ["sh_floor"] = Look.Floor, ["sh_wall"] = Look.Wall, ["sh_ceiling"] = Look.Ceiling, ["sh_wood"] = Look.Wood, ["sh_steel"] = Look.Steel,
                    ["sh_sign"] = Look.Sign
                },
                ["lampColor"] = new JArray(Look.Lamps)
            })
        };
    }

    #endregion

    // Nothing of the room stands in the way of the light (Kn5SceneWriter.AddMesh makes every mesh shadowless); the parts cast their own
    private static void AddMesh(IKn5 kn5, string name, SceneMesh mesh, uint material) =>
        Kn5SceneWriter.AddMesh(kn5, name, mesh.Vertices, mesh.Indices, material);
}
