using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Tools;
using static StreetCorsa.Tools.Kn5SceneWriter;

namespace PaintBoothSceneBuilder;

/// <summary>
/// Builds the paint booth (the paint booth screen): booth.kn5, the room, and painter.kn5, the man who paints the car,
/// out of the pictures prepare_booth.py drew.
///
/// A 1960s body shop's spray booth, 5.6 m across and 9.2 m deep, 3.2 m high: white steel walls, a concrete floor with
/// a steel grate down its middle over the exhaust trench, the filters across the back wall, light boxes along the ceiling
/// and down the walls, a cart of paint cans in a corner. The car comes in through the open front (+z) from the shop
/// outside, dim, and stands in the middle facing the filters. The scene is lit as a whole, nothing of it casting a
/// shadow (booth.json's lights); the car and the painter cast theirs.
///
/// The painter stands at the origin facing +z, his feet on the floor: his right arm a node of its own (ARM) at the
/// shoulder, the gun in his hand, so the game swings it up and down the car's side; the spray out of the gun's nozzle
/// another (MIST), shown while he sprays.
///
///   PaintBoothSceneBuilder &lt;prepared folder&gt; &lt;out folder&gt;
///
/// The base game's booth: PaintBoothSceneBuilder &lt;pictures&gt; "Street Corsa/Content/streetcorsa/booths/booth"
/// (build_booth.py runs both steps).
/// </summary>
internal static class Program
{
    // The booth, in metres: x across, z from the filters (-z) to the door (+z), y up
    private const float HalfWidth = 2.8f;
    private const float Back = -4.6f;
    private const float Front = 4.6f;
    private const float Height = 3.2f;
    private const float DoorHalf = 1.7f;
    private const float DoorTop = 2.6f;

    // The shop outside the door
    private const float ShopHalf = 8f;
    private const float ShopEnd = 24f;
    private const float ShopHeight = 4.5f;

    // The grate down the middle of the floor
    private const float GrateHalf = 0.35f;

    // How big a picture is on the surface, in metres
    private const float WallTile = 2.4f;
    private const float FloorTile = 4f;
    private const float SteelTile = 1f;
    private const float FilterTile = 2.4f;

    // Where the painter's right shoulder is (ARM), and his gun's nozzle in the arm's space (MIST)
    public static readonly Vec3 Shoulder = new(-0.24f, 1.40f, 0.02f);
    public static readonly Vec3 Nozzle = new(0f, -0.09f, 0.70f);

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("PaintBoothSceneBuilder <prepared folder> <out folder>");
            return 1;
        }

        var (prepared, output) = (args[0], args[1]);
        Directory.CreateDirectory(output);

        var booth = Booth(prepared);
        var path = Path.Combine(output, "booth.kn5");
        Kn5SceneWriter.Save(booth, path);
        Console.WriteLine($"{path}: {new FileInfo(path).Length / 1024} KB");

        var painter = Painter(prepared);
        path = Path.Combine(output, "painter.kn5");
        Kn5SceneWriter.Save(painter, path);
        Console.WriteLine($"{path}: {new FileInfo(path).Length / 1024} KB");
        return 0;
    }

    #region The booth

    private static IKn5 Booth(string prepared)
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = "booth";
        foreach (var name in new[] { "wall.jpg", "ceiling.jpg", "floor.jpg", "grate.jpg", "filter.jpg", "light.jpg", "steel.jpg", "shop.jpg", "metal.jpg", "dark.jpg" })
            AddTexture(kn5, Path.Combine(prepared, name));

        var wall = AddMaterial(kn5, "pb_wall", "wall.jpg", ambient: 0.62f, diffuse: 0.4f, specular: 0.12f, specularExp: 20f);
        var ceiling = AddMaterial(kn5, "pb_ceiling", "ceiling.jpg", ambient: 0.62f, diffuse: 0.3f);
        var floor = AddMaterial(kn5, "pb_floor", "floor.jpg", ambient: 0.26f, diffuse: 0.62f, specular: 0.15f, specularExp: 30f);
        var grate = AddMaterial(kn5, "pb_grate", "grate.jpg", ambient: 0.3f, diffuse: 0.6f, specular: 0.3f, specularExp: 40f);
        var filter = AddMaterial(kn5, "pb_filter", "filter.jpg", ambient: 0.62f, diffuse: 0.35f);
        var light = AddMaterial(kn5, "pb_light", "light.jpg", ambient: 0f, diffuse: 0f, emissive: 1.5f);
        var steel = AddMaterial(kn5, "pb_steel", "steel.jpg", ambient: 0.5f, diffuse: 0.45f, specular: 0.35f, specularExp: 40f);
        var shop = AddMaterial(kn5, "pb_shop", "shop.jpg", ambient: 0.16f, diffuse: 0.3f);
        var shopFloor = AddMaterial(kn5, "pb_shop_floor", "floor.jpg", ambient: 0.14f, diffuse: 0.32f);
        var cans = AddMaterial(kn5, "pb_cans", "metal.jpg", ambient: 0.32f, diffuse: 0.5f, specular: 0.5f, specularExp: 60f);
        var dark = AddMaterial(kn5, "pb_dark", "dark.jpg", ambient: 0.3f, diffuse: 0.45f);

        AddMesh(kn5, "PB_FLOOR", BoothFloor(), floor);
        AddMesh(kn5, "PB_GRATE", Grate(), grate);
        AddMesh(kn5, "PB_WALL", Walls(), wall);
        AddMesh(kn5, "PB_CEILING", Ceiling(), ceiling);
        AddMesh(kn5, "PB_FILTER", Filters(), filter);
        AddMesh(kn5, "PB_LIGHT", Lights(), light);
        AddMesh(kn5, "PB_STEEL", Steelwork(), steel);
        AddMesh(kn5, "PB_SHOP", Shop(), shop);
        AddMesh(kn5, "PB_SHOP_FLOOR", ShopFloor(), shopFloor);
        AddMesh(kn5, "PB_CANS", Cans(), cans);
        AddMesh(kn5, "PB_DARK", Lids(), dark);
        return kn5;
    }

    private static Mesh BoothFloor()
    {
        var mesh = new Mesh();
        Vec2 Uv(Vec3 p) => new(p.X / FloorTile, p.Z / FloorTile);
        // Either side of the grate
        mesh.Quad(V(-HalfWidth, 0f, Back), V(-GrateHalf, 0f, Back), V(-GrateHalf, 0f, Front), V(-HalfWidth, 0f, Front), Vec3.UnitY, Uv);
        mesh.Quad(V(GrateHalf, 0f, Back), V(HalfWidth, 0f, Back), V(HalfWidth, 0f, Front), V(GrateHalf, 0f, Front), Vec3.UnitY, Uv);
        return mesh;
    }

    private static Mesh Grate()
    {
        var mesh = new Mesh();
        mesh.Quad(V(-GrateHalf, 0.002f, Back), V(GrateHalf, 0.002f, Back), V(GrateHalf, 0.002f, Front), V(-GrateHalf, 0.002f, Front), Vec3.UnitY,
            p => new Vec2((p.X + GrateHalf) / (2 * GrateHalf), p.Z / 1.2f));
        return mesh;
    }

    /// <summary>The side walls, the back wall over and under the filters, the front round the door; all facing in</summary>
    private static Mesh Walls()
    {
        var mesh = new Mesh();
        Vec2 Xy(Vec3 p) => new(p.X / WallTile, -p.Y / WallTile);
        Vec2 Zy(Vec3 p) => new(p.Z / WallTile, -p.Y / WallTile);

        foreach (var x in new[] { -HalfWidth, HalfWidth })
            mesh.Quad(V(x, 0f, Back), V(x, 0f, Front), V(x, Height, Front), V(x, Height, Back), V(-MathF.Sign(x), 0f, 0f), Zy);

        // The back: a band under the filters and over them
        void BackPanel(float x0, float x1, float y0, float y1) =>
            mesh.Quad(V(x0, y0, Back), V(x1, y0, Back), V(x1, y1, Back), V(x0, y1, Back), Vec3.UnitZ, Xy);
        BackPanel(-HalfWidth, HalfWidth, 0f, FilterBottom);
        BackPanel(-HalfWidth, HalfWidth, FilterTop, Height);

        // The front, round the door; and its outside, seen from the shop
        void FrontPanel(float x0, float x1, float y0, float y1)
        {
            mesh.Quad(V(x0, y0, Front), V(x1, y0, Front), V(x1, y1, Front), V(x0, y1, Front), -Vec3.UnitZ, Xy);
            mesh.Quad(V(x0, y0, Front + 0.12f), V(x1, y0, Front + 0.12f), V(x1, y1, Front + 0.12f), V(x0, y1, Front + 0.12f), Vec3.UnitZ, Xy);
        }
        FrontPanel(-HalfWidth, -DoorHalf, 0f, Height);
        FrontPanel(DoorHalf, HalfWidth, 0f, Height);
        FrontPanel(-DoorHalf, DoorHalf, DoorTop, Height);

        // The booth's outside walls and roof, seen from the shop as the car comes in
        mesh.Quad(V(-HalfWidth - 0.12f, 0f, Front + 0.12f), V(-HalfWidth - 0.12f, 0f, Back), V(-HalfWidth - 0.12f, Height + 0.1f, Back), V(-HalfWidth - 0.12f, Height + 0.1f, Front + 0.12f), -Vec3.UnitX, Zy);
        mesh.Quad(V(HalfWidth + 0.12f, 0f, Back), V(HalfWidth + 0.12f, 0f, Front + 0.12f), V(HalfWidth + 0.12f, Height + 0.1f, Front + 0.12f), V(HalfWidth + 0.12f, Height + 0.1f, Back), Vec3.UnitX, Zy);
        mesh.Quad(V(-HalfWidth - 0.12f, Height + 0.1f, Back), V(HalfWidth + 0.12f, Height + 0.1f, Back), V(HalfWidth + 0.12f, Height + 0.1f, Front + 0.12f), V(-HalfWidth - 0.12f, Height + 0.1f, Front + 0.12f), Vec3.UnitY,
            p => new Vec2(p.X / WallTile, p.Z / WallTile));
        return mesh;
    }

    private const float FilterBottom = 0.35f;
    private const float FilterTop = 2.95f;

    private static Mesh Filters()
    {
        var mesh = new Mesh();
        const float z = Back + 0.01f;
        mesh.Quad(V(-HalfWidth, FilterBottom, z), V(HalfWidth, FilterBottom, z), V(HalfWidth, FilterTop, z), V(-HalfWidth, FilterTop, z), Vec3.UnitZ,
            p => new Vec2((p.X + HalfWidth) / FilterTile, -(p.Y - FilterBottom) / FilterTile));
        return mesh;
    }

    private static Mesh Ceiling()
    {
        var mesh = new Mesh();
        mesh.Quad(V(-HalfWidth, Height, Back), V(HalfWidth, Height, Back), V(HalfWidth, Height, Front), V(-HalfWidth, Height, Front), -Vec3.UnitY,
            p => new Vec2(p.X / WallTile, p.Z / WallTile));
        return mesh;
    }

    // The ceiling's light boxes: two rows down its length; and three boxes down each wall, upright
    private static readonly float[] CeilingRows = [-1.55f, 1.55f];
    private static readonly float[] WallBoxes = [-3f, 0f, 3f];

    private static Mesh Lights()
    {
        var mesh = new Mesh();
        foreach (var x in CeilingRows)
        {
            // Its face, a little under the ceiling, the picture stretched over each 2 m length
            for (var z = Back + 0.4f; z < Front - 0.5f; z += 2.1f)
            {
                var z1 = Math.Min(z + 2f, Front - 0.4f);
                mesh.Quad(V(x - 0.45f, Height - 0.08f, z), V(x + 0.45f, Height - 0.08f, z), V(x + 0.45f, Height - 0.08f, z1), V(x - 0.45f, Height - 0.08f, z1), -Vec3.UnitY,
                    p => new Vec2((p.X - x + 0.45f) / 0.9f, (p.Z - z) / (z1 - z)));
            }
        }
        foreach (var x in new[] { -HalfWidth + 0.06f, HalfWidth - 0.06f })
        foreach (var z in WallBoxes)
        {
            var normal = V(-MathF.Sign(x), 0f, 0f);
            mesh.Quad(V(x, 0.7f, z - 0.3f), V(x, 0.7f, z + 0.3f), V(x, 2.7f, z + 0.3f), V(x, 2.7f, z - 0.3f), normal,
                p => new Vec2((p.Z - z + 0.3f) / 0.6f, -(p.Y - 0.7f) / 2f));
        }
        return mesh;
    }

    /// <summary>The kick plates, the door's frame, the light boxes' housings, the filters' frame, the cart</summary>
    private static Mesh Steelwork()
    {
        var mesh = new Mesh();
        foreach (var x in new[] { -HalfWidth, HalfWidth })
        {
            var inner = x - MathF.Sign(x) * 0.02f;
            mesh.Box(V(Math.Min(x, inner), 0f, Back), V(Math.Max(x, inner), 0.3f, Front), SteelTile);
            foreach (var z in WallBoxes)
                mesh.Box(V(Math.Min(x, x - MathF.Sign(x) * 0.06f), 0.64f, z - 0.36f), V(Math.Max(x, x - MathF.Sign(x) * 0.06f), 2.76f, z + 0.36f), SteelTile);
        }
        mesh.Box(V(-HalfWidth, 0f, Back), V(HalfWidth, 0.3f, Back + 0.02f), SteelTile);

        // The light rows' housings
        foreach (var x in CeilingRows)
            mesh.Box(V(x - 0.5f, Height - 0.07f, Back + 0.3f), V(x + 0.5f, Height, Front - 0.3f), SteelTile);

        // The door's frame
        mesh.Box(V(-DoorHalf - 0.1f, 0f, Front - 0.02f), V(-DoorHalf, DoorTop, Front + 0.14f), SteelTile);
        mesh.Box(V(DoorHalf, 0f, Front - 0.02f), V(DoorHalf + 0.1f, DoorTop, Front + 0.14f), SteelTile);
        mesh.Box(V(-DoorHalf - 0.1f, DoorTop, Front - 0.02f), V(DoorHalf + 0.1f, DoorTop + 0.1f, Front + 0.14f), SteelTile);

        // The filters' frame and their middle posts
        mesh.Box(V(-HalfWidth, FilterBottom - 0.05f, Back), V(HalfWidth, FilterBottom, Back + 0.05f), SteelTile);
        mesh.Box(V(-HalfWidth, FilterTop, Back), V(HalfWidth, FilterTop + 0.05f, Back + 0.05f), SteelTile);

        // The cart in the front left corner: its shelves, its legs, its handle
        var (cx, cz) = (CartX, CartZ);
        foreach (var y in new[] { 0.18f, 0.85f })
            mesh.Box(V(cx - 0.3f, y, cz - 0.22f), V(cx + 0.3f, y + 0.03f, cz + 0.22f), SteelTile);
        foreach (var (dx, dz) in new[] { (-0.28f, -0.2f), (0.28f, -0.2f), (-0.28f, 0.2f), (0.28f, 0.2f) })
            mesh.Box(V(cx + dx - 0.015f, 0.05f, cz + dz - 0.015f), V(cx + dx + 0.015f, 0.88f, cz + dz + 0.015f), SteelTile);
        mesh.Bar(V(cx + 0.3f, 0.88f, cz - 0.2f), V(cx + 0.3f, 1.0f, cz - 0.2f), 0.02f, SteelTile);
        mesh.Bar(V(cx + 0.3f, 0.88f, cz + 0.2f), V(cx + 0.3f, 1.0f, cz + 0.2f), 0.02f, SteelTile);
        mesh.Bar(V(cx + 0.3f, 1.0f, cz - 0.2f), V(cx + 0.3f, 1.0f, cz + 0.2f), 0.02f, SteelTile);
        return mesh;
    }

    private const float CartX = 2.2f;
    private const float CartZ = 3.6f;

    // The paint cans on the cart: where each stands, on which shelf
    private static readonly (float Dx, float Dz, float Y)[] CanSpots =
        [(-0.18f, -0.1f, 0.88f), (0.02f, -0.12f, 0.88f), (0.2f, 0.08f, 0.88f), (-0.1f, 0.1f, 0.88f), (-0.15f, 0f, 0.21f), (0.12f, 0.05f, 0.21f)];

    private static Mesh Cans()
    {
        var mesh = new Mesh();
        foreach (var (dx, dz, y) in CanSpots) mesh.Cylinder(V(CartX + dx, y, CartZ + dz), 0.085f, 0.19f, 14, SteelTile);
        return mesh;
    }

    private static Mesh Lids()
    {
        var mesh = new Mesh();
        foreach (var (dx, dz, y) in CanSpots) mesh.Cylinder(V(CartX + dx, y + 0.19f, CartZ + dz), 0.088f, 0.012f, 14, SteelTile);
        return mesh;
    }

    /// <summary>The shop outside the door: its walls, dim, round the way the car comes in</summary>
    private static Mesh Shop()
    {
        var mesh = new Mesh();
        Vec2 Xy(Vec3 p) => new(p.X / 4f, -p.Y / 4f);
        Vec2 Zy(Vec3 p) => new(p.Z / 4f, -p.Y / 4f);
        mesh.Quad(V(-ShopHalf, 0f, ShopEnd), V(ShopHalf, 0f, ShopEnd), V(ShopHalf, ShopHeight, ShopEnd), V(-ShopHalf, ShopHeight, ShopEnd), -Vec3.UnitZ, Xy);
        foreach (var x in new[] { -ShopHalf, ShopHalf })
            mesh.Quad(V(x, 0f, Back - 1f), V(x, 0f, ShopEnd), V(x, ShopHeight, ShopEnd), V(x, ShopHeight, Back - 1f), V(-MathF.Sign(x), 0f, 0f), Zy);
        mesh.Quad(V(-ShopHalf, ShopHeight, Back - 1f), V(ShopHalf, ShopHeight, Back - 1f), V(ShopHalf, ShopHeight, ShopEnd), V(-ShopHalf, ShopHeight, ShopEnd), -Vec3.UnitY,
            p => new Vec2(p.X / 4f, p.Z / 4f));
        mesh.Quad(V(-ShopHalf, 0f, Back - 1f), V(ShopHalf, 0f, Back - 1f), V(ShopHalf, ShopHeight, Back - 1f), V(-ShopHalf, ShopHeight, Back - 1f), Vec3.UnitZ, Xy);
        return mesh;
    }

    private static Mesh ShopFloor()
    {
        var mesh = new Mesh();
        Vec2 Uv(Vec3 p) => new(p.X / FloorTile, p.Z / FloorTile);
        mesh.Quad(V(-ShopHalf, -0.001f, Front), V(ShopHalf, -0.001f, Front), V(ShopHalf, -0.001f, ShopEnd), V(-ShopHalf, -0.001f, ShopEnd), Vec3.UnitY, Uv);
        foreach (var (x0, x1) in new[] { (-ShopHalf, -HalfWidth - 0.12f), (HalfWidth + 0.12f, ShopHalf) })
            mesh.Quad(V(x0, -0.001f, Back - 1f), V(x1, -0.001f, Back - 1f), V(x1, -0.001f, Front), V(x0, -0.001f, Front), Vec3.UnitY, Uv);
        return mesh;
    }

    #endregion

    #region The painter

    private static IKn5 Painter(string prepared)
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = "painter";
        foreach (var name in new[] { "cloth.jpg", "dark.jpg", "rubber.jpg", "metal.jpg", "lens.jpg", "mist.png" })
            AddTexture(kn5, Path.Combine(prepared, name));

        var cloth = AddMaterial(kn5, "pt_cloth", "cloth.jpg", ambient: 0.34f, diffuse: 0.55f);
        var dark = AddMaterial(kn5, "pt_dark", "dark.jpg", ambient: 0.3f, diffuse: 0.45f, specular: 0.2f, specularExp: 20f);
        var rubber = AddMaterial(kn5, "pt_rubber", "rubber.jpg", ambient: 0.3f, diffuse: 0.4f, specular: 0.15f, specularExp: 15f);
        var metal = AddMaterial(kn5, "pt_metal", "metal.jpg", ambient: 0.3f, diffuse: 0.45f, specular: 0.7f, specularExp: 70f);
        var lens = AddMaterial(kn5, "pt_lens", "lens.jpg", ambient: 0.25f, diffuse: 0.3f, specular: 0.9f, specularExp: 120f);
        var mist = AddMaterial(kn5, "pt_mist", "mist.png", ambient: 0.6f, diffuse: 0.3f, emissive: 0.25f);
        kn5.Materials["pt_mist"].BlendMode = Kn5MaterialBlendMode.AlphaBlend;
        kn5.Materials["pt_mist"].DepthMode = Kn5MaterialDepthMode.DepthNoWrite;

        var root = Kn5Node.CreateBaseNode("PAINTER");
        kn5.RootNode.Children.Add(root);

        // The body: boots, legs, the coveralls' body, the hood over his head, his mask and goggles, his left arm down
        var body = new Mesh();
        foreach (var x in new[] { -0.1f, 0.1f })
        {
            // Thigh and shin, a little bent at the knee, as a man stands at work
            body.Capsule(V(x, 0.92f, 0f), V(x * 1.1f, 0.5f, 0.03f), 0.085f, 0.07f);
            body.Capsule(V(x * 1.1f, 0.5f, 0.03f), V(x * 1.1f, 0.14f, 0f), 0.068f, 0.058f);
        }
        // Hips, the coveralls' body, broader at the chest, the shoulders
        body.Tube(V(0f, 0.86f, 0f), V(0f, 1.08f, 0f), 0.17f, 0.165f, 14, 0.7f);
        body.Tube(V(0f, 1.08f, 0f), V(0f, 1.40f, 0.01f), 0.165f, 0.2f, 14, 0.66f);
        body.Capsule(V(-0.2f, 1.41f, 0.01f), V(0.2f, 1.41f, 0.01f), 0.075f, 0.075f);
        // The neck, and the hood over his head
        body.Tube(V(0f, 1.42f, 0f), V(0f, 1.53f, 0.01f), 0.05f, 0.05f, 10, 1f);
        body.Sphere(V(0f, 1.62f, -0.005f), 0.122f, 14, 10, 0.5f);
        // His left arm, the gun's cup in his other hand: down at his side, the elbow a little out
        body.Capsule(V(0.25f, 1.41f, 0.01f), V(0.29f, 1.14f, 0.0f), 0.055f, 0.05f);
        body.Capsule(V(0.29f, 1.14f, 0.0f), V(0.26f, 0.92f, 0.07f), 0.048f, 0.044f);
        Child(root, "PT_BODY", body, cloth);

        var boots = new Mesh();
        foreach (var x in new[] { -0.11f, 0.11f }) boots.Box(V(x - 0.065f, 0f, -0.09f), V(x + 0.065f, 0.12f, 0.15f), 0.5f, bottom: true);
        boots.Sphere(V(0.255f, 0.88f, 0.08f), 0.05f, 10, 8, 0.5f);
        Child(root, "PT_BOOTS", boots, rubber);

        // The respirator over his mouth and nose, its two filters, and the goggles' strap and rims
        var mask = new Mesh();
        mask.Sphere(V(0f, 1.57f, 0.085f), 0.055f, 12, 8, 0.5f);
        mask.Cylinder(V(-0.045f, 1.55f, 0.105f), 0.026f, 0.035f, 10, 0.5f, alongZ: true);
        mask.Cylinder(V(0.045f, 1.55f, 0.105f), 0.026f, 0.035f, 10, 0.5f, alongZ: true);
        mask.Tube(V(0f, 1.655f, 0f), V(0f, 1.675f, 0f), 0.126f, 0.126f, 18, 1f);
        mask.Cylinder(V(-0.042f, 1.665f, 0.1f), 0.032f, 0.022f, 12, 0.5f, alongZ: true);
        mask.Cylinder(V(0.042f, 1.665f, 0.1f), 0.032f, 0.022f, 12, 0.5f, alongZ: true);
        Child(root, "PT_MASK", mask, dark);

        var goggles = new Mesh();
        goggles.Cylinder(V(-0.042f, 1.665f, 0.105f), 0.025f, 0.02f, 12, 0.5f, alongZ: true);
        goggles.Cylinder(V(0.042f, 1.665f, 0.105f), 0.025f, 0.02f, 12, 0.5f, alongZ: true);
        Child(root, "PT_GOGGLES", goggles, lens);

        // His right arm at the shoulder: what the game swings
        var arm = Kn5Node.CreateBaseNode("ARM");
        arm.Transform = Mat4x4.CreateTranslation(Shoulder);
        root.Children.Add(arm);

        var sleeve = new Mesh();
        sleeve.Capsule(V(0f, 0f, 0f), V(-0.03f, -0.2f, 0.18f), 0.055f, 0.05f);
        sleeve.Capsule(V(-0.03f, -0.2f, 0.18f), V(0f, -0.15f, 0.44f), 0.05f, 0.045f);
        Child(arm, "PT_SLEEVE", sleeve, cloth);

        var glove = new Mesh();
        glove.Sphere(V(0f, -0.14f, 0.49f), 0.048f, 10, 8, 0.5f);
        Child(arm, "PT_GLOVE", glove, rubber);

        // The gun: its body over the hand, the nozzle ahead, the cup under it, the air hose off its handle
        var gun = new Mesh();
        gun.Box(V(-0.025f, -0.12f, 0.5f), V(0.025f, -0.06f, 0.66f), 0.5f, bottom: true);
        gun.Cylinder(V(0f, -0.09f, 0.66f), 0.012f, 0.04f, 8, 0.5f, alongZ: true);
        gun.Cylinder(V(0f, -0.29f, 0.56f), 0.05f, 0.16f, 12, 0.5f);
        Child(arm, "PT_GUN", gun, metal);

        var hose = new Mesh();
        hose.Bar(V(0f, -0.2f, 0.5f), V(0.05f, -0.75f, 0.2f), 0.022f, 0.5f);
        hose.Bar(V(0.05f, -0.75f, 0.2f), V(0.1f, -1.38f, -0.35f), 0.022f, 0.5f);
        Child(arm, "PT_HOSE", hose, rubber);

        // The spray: a cone out of the nozzle, along +z, soft at its end
        var spray = Kn5Node.CreateBaseNode("MIST");
        spray.Transform = Mat4x4.CreateTranslation(Nozzle);
        arm.Children.Add(spray);
        var cone = new Mesh();
        cone.Cone(V(0f, 0f, 0f), 0.02f, 0.34f, 0.95f, 16);
        Child(spray, "PT_MIST", cone, mist, castShadows: false);
        return kn5;
    }

    /// <summary>A mesh under <paramref name="parent"/>, casting a shadow unless it is the spray</summary>
    private static void Child(Kn5Node parent, string name, Mesh mesh, uint material, bool castShadows = true)
    {
        var holder = Kn5.CreateEmpty();
        Kn5SceneWriter.AddMesh(holder, name, mesh.Vertices, mesh.Indices, material, castShadows: castShadows);
        parent.Children.Add(holder.RootNode.Children[0]);
    }

    #endregion

    #region Geometry

    private sealed class Mesh
    {
        public readonly List<Kn5Node.Vertex> Vertices = [];
        public readonly List<int> Indices = [];

        /// <summary>A flat four-cornered face seen from the side <paramref name="normal"/> points to, the corners in order round its edge</summary>
        public void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 normal, Func<Vec3, Vec2> uv)
        {
            var i = Vertices.Count;
            var tangent = Vec3.Normalize(b - a);
            foreach (var p in new[] { a, b, c, d }) Vertices.Add(new Kn5Node.Vertex(p, normal, uv(p), tangent));
            Facing(i, i + 1, i + 2, normal);
            Facing(i, i + 2, i + 3, normal);
        }

        private void Facing(int a, int b, int c, Vec3 normal)
        {
            var pa = Vertices[a].Position;
            var cross = Vec3.Cross(Vertices[b].Position - pa, Vertices[c].Position - pa);
            if (Vec3.Dot(cross, normal) > 0f) Indices.AddRange([a, b, c]);
            else Indices.AddRange([a, c, b]);
        }

        /// <summary>A box between two corners, its faces looking out; the bottom left out unless asked for</summary>
        public void Box(Vec3 min, Vec3 max, float tile, bool bottom = false)
        {
            Vec2 Xy(Vec3 p) => new(p.X / tile, -p.Y / tile);
            Vec2 Zy(Vec3 p) => new(p.Z / tile, -p.Y / tile);
            Vec2 Xz(Vec3 p) => new(p.X / tile, p.Z / tile);

            Quad(V(min.X, min.Y, max.Z), V(max.X, min.Y, max.Z), V(max.X, max.Y, max.Z), V(min.X, max.Y, max.Z), Vec3.UnitZ, Xy);
            Quad(V(min.X, min.Y, min.Z), V(max.X, min.Y, min.Z), V(max.X, max.Y, min.Z), V(min.X, max.Y, min.Z), -Vec3.UnitZ, Xy);
            Quad(V(max.X, min.Y, min.Z), V(max.X, min.Y, max.Z), V(max.X, max.Y, max.Z), V(max.X, max.Y, min.Z), Vec3.UnitX, Zy);
            Quad(V(min.X, min.Y, min.Z), V(min.X, min.Y, max.Z), V(min.X, max.Y, max.Z), V(min.X, max.Y, min.Z), -Vec3.UnitX, Zy);
            Quad(V(min.X, max.Y, min.Z), V(max.X, max.Y, min.Z), V(max.X, max.Y, max.Z), V(min.X, max.Y, max.Z), Vec3.UnitY, Xz);
            if (bottom) Quad(V(min.X, min.Y, min.Z), V(max.X, min.Y, min.Z), V(max.X, min.Y, max.Z), V(min.X, min.Y, max.Z), -Vec3.UnitY, Xz);
        }

        /// <summary>A square-section bar from <paramref name="from"/> to <paramref name="to"/>, <paramref name="size"/> across</summary>
        public void Bar(Vec3 from, Vec3 to, float size, float tile)
        {
            var axis = Vec3.Normalize(to - from);
            var helper = MathF.Abs(axis.Y) > 0.9f ? Vec3.UnitX : Vec3.UnitY;
            var side = Vec3.Normalize(Vec3.Cross(axis, helper)) * (size / 2f);
            var up = Vec3.Normalize(Vec3.Cross(side, axis)) * (size / 2f);

            var corners = new[] { side + up, -side + up, -side - up, side - up };
            for (var k = 0; k < 4; k++)
            {
                var p = corners[k];
                var q = corners[(k + 1) % 4];
                var normal = Vec3.Normalize(p + q);
                Quad(from + p, from + q, to + q, to + p, normal, v => new Vec2(Vec3.Dot(v - from, axis) / tile, Vec3.Dot(v - from, Vec3.Normalize(q - p)) / tile));
            }
        }

        /// <summary>
        /// A closed cylinder standing on <paramref name="bottom"/> (upright), or lying along +z from it when
        /// <paramref name="alongZ"/>
        /// </summary>
        public void Cylinder(Vec3 bottom, float radius, float height, int sides, float tile, bool alongZ = false)
        {
            Vec3 At(float angle, float h) => alongZ
                ? bottom + V(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, h)
                : bottom + V(MathF.Cos(angle) * radius, h, MathF.Sin(angle) * radius);
            Vec3 Out(float angle) => alongZ ? V(MathF.Cos(angle), MathF.Sin(angle), 0f) : V(MathF.Cos(angle), 0f, MathF.Sin(angle));
            var axis = alongZ ? Vec3.UnitZ : Vec3.UnitY;

            for (var k = 0; k < sides; k++)
            {
                var a0 = k * MathF.Tau / sides;
                var a1 = (k + 1) * MathF.Tau / sides;
                var normal = Vec3.Normalize(Out(a0) + Out(a1));
                var u0 = k * radius * MathF.Tau / sides / tile;
                var u1 = (k + 1) * radius * MathF.Tau / sides / tile;
                var (p0, p1, p2, p3) = (At(a0, 0f), At(a1, 0f), At(a1, height), At(a0, height));
                Quad(p0, p1, p2, p3, normal, p => new Vec2(Vec3.Distance(p, p0) < 1e-5f || Vec3.Distance(p, p3) < 1e-5f ? u0 : u1,
                    Vec3.Dot(p - bottom, axis) / tile));

                // The ends, a fan of triangles each
                foreach (var (h, n) in new[] { (0f, -axis), (height, axis) })
                {
                    var i = Vertices.Count;
                    var centre = bottom + axis * h;
                    Vertices.Add(new Kn5Node.Vertex(centre, n, new Vec2(0.5f, 0.5f), Vec3.UnitX));
                    Vertices.Add(new Kn5Node.Vertex(At(a0, h), n, new Vec2(0.5f + MathF.Cos(a0) / 2, 0.5f + MathF.Sin(a0) / 2), Vec3.UnitX));
                    Vertices.Add(new Kn5Node.Vertex(At(a1, h), n, new Vec2(0.5f + MathF.Cos(a1) / 2, 0.5f + MathF.Sin(a1) / 2), Vec3.UnitX));
                    Facing(i, i + 1, i + 2, n);
                }
            }
        }

        /// <summary>
        /// An open tube from <paramref name="from"/> to <paramref name="to"/>, its radius going from <paramref name="r0"/> to
        /// <paramref name="r1"/>; its section squashed to <paramref name="squash"/> of its width front to back (a body)
        /// </summary>
        public void Tube(Vec3 from, Vec3 to, float r0, float r1, int sides, float squash)
        {
            var axis = Vec3.Normalize(to - from);
            var helper = MathF.Abs(axis.Y) > 0.9f ? Vec3.UnitX : Vec3.UnitY;
            var side = Vec3.Normalize(Vec3.Cross(helper, axis));
            var front = Vec3.Normalize(Vec3.Cross(axis, side));
            if (MathF.Abs(axis.Y) > 0.9f) (side, front) = (Vec3.UnitX, Vec3.UnitZ);
            Vec3 Round(float angle, float r, float s) => side * (MathF.Cos(angle) * r) + front * (MathF.Sin(angle) * r * s);

            var start = Vertices.Count;
            var length = Vec3.Distance(from, to);
            for (var end = 0; end < 2; end++)
            {
                var centre = end == 0 ? from : to;
                var r = end == 0 ? r0 : r1;
                for (var k = 0; k <= sides; k++)
                {
                    var angle = k * MathF.Tau / sides;
                    var normal = Vec3.Normalize(side * MathF.Cos(angle) * squash + front * MathF.Sin(angle));
                    Vertices.Add(new Kn5Node.Vertex(centre + Round(angle, r, squash), normal, new Vec2((float)k / sides, end * length / 0.5f), axis));
                }
            }
            for (var k = 0; k < sides; k++)
            {
                var (a, b, c, d) = (start + k, start + k + 1, start + sides + 1 + k + 1, start + sides + 1 + k);
                var n = Vec3.Normalize(Vertices[a].Normal + Vertices[b].Normal);
                Facing(a, b, c, n);
                Facing(a, c, d, n);
            }
        }

        /// <summary>A rounded limb from <paramref name="a"/> to <paramref name="b"/>: a tube with a ball at each end</summary>
        public void Capsule(Vec3 a, Vec3 b, float ra, float rb)
        {
            Tube(a, b, ra, rb, 12, 1f);
            Sphere(a, ra, 12, 8, 0.5f);
            Sphere(b, rb, 12, 8, 0.5f);
        }

        /// <summary>A sphere round <paramref name="centre"/></summary>
        public void Sphere(Vec3 centre, float radius, int sides, int rings, float tile)
        {
            var start = Vertices.Count;
            for (var r = 0; r <= rings; r++)
            {
                var phi = MathF.PI * r / rings;
                for (var s = 0; s <= sides; s++)
                {
                    var theta = MathF.Tau * s / sides;
                    var n = V(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                    Vertices.Add(new Kn5Node.Vertex(centre + n * radius, n, new Vec2((float)s / sides * radius * MathF.Tau / tile, (float)r / rings), V(-MathF.Sin(theta), 0f, MathF.Cos(theta))));
                }
            }
            for (var r = 0; r < rings; r++)
            for (var s = 0; s < sides; s++)
            {
                var a = start + r * (sides + 1) + s;
                var b = a + sides + 1;
                var normal = Vertices[a].Normal;
                Facing(a, b, a + 1, Vec3.Normalize(Vertices[a].Normal + Vertices[b].Normal + Vertices[a + 1].Normal));
                Facing(a + 1, b, b + 1, Vec3.Normalize(Vertices[a + 1].Normal + Vertices[b].Normal + Vertices[b + 1].Normal));
                _ = normal;
            }
        }

        /// <summary>
        /// An open cone from <paramref name="tip"/> along +z, <paramref name="length"/> long, from <paramref name="tipRadius"/>
        /// to <paramref name="endRadius"/>; its picture's u along it, v round it. Seen from both sides.
        /// </summary>
        public void Cone(Vec3 tip, float tipRadius, float endRadius, float length, int sides)
        {
            for (var k = 0; k < sides; k++)
            {
                var a0 = k * MathF.Tau / sides;
                var a1 = (k + 1) * MathF.Tau / sides;
                var p0 = tip + V(MathF.Cos(a0) * tipRadius, MathF.Sin(a0) * tipRadius, 0f);
                var p1 = tip + V(MathF.Cos(a1) * tipRadius, MathF.Sin(a1) * tipRadius, 0f);
                var p2 = tip + V(MathF.Cos(a1) * endRadius, MathF.Sin(a1) * endRadius, length);
                var p3 = tip + V(MathF.Cos(a0) * endRadius, MathF.Sin(a0) * endRadius, length);
                var outward = Vec3.Normalize(V(MathF.Cos((a0 + a1) / 2), MathF.Sin((a0 + a1) / 2), -(endRadius - tipRadius) / length));
                float v0 = (float)k / sides, v1 = (float)(k + 1) / sides;
                foreach (var normal in new[] { outward, -outward })
                {
                    var i = Vertices.Count;
                    Vertices.Add(new Kn5Node.Vertex(p0, normal, new Vec2(0f, v0), Vec3.UnitZ));
                    Vertices.Add(new Kn5Node.Vertex(p1, normal, new Vec2(0f, v1), Vec3.UnitZ));
                    Vertices.Add(new Kn5Node.Vertex(p2, normal, new Vec2(1f, v1), Vec3.UnitZ));
                    Vertices.Add(new Kn5Node.Vertex(p3, normal, new Vec2(1f, v0), Vec3.UnitZ));
                    Facing(i, i + 1, i + 2, normal);
                    Facing(i, i + 2, i + 3, normal);
                }
            }
        }
    }

    private static Vec3 V(float x, float y, float z) => new(x, y, z);

    #endregion

    // The booth is lit as a whole (booth.json's lights): nothing of it stands in the light's way
    private static void AddMesh(IKn5 kn5, string name, Mesh mesh, uint material) =>
        Kn5SceneWriter.AddMesh(kn5, name, mesh.Vertices, mesh.Indices, material);
}
