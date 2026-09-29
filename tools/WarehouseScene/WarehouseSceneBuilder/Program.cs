using System.Globalization;
using AcTools.Kn5File;
using AcTools.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WarehouseSceneBuilder;

/// <summary>
/// Builds the warehouse the player's cars are kept in (the car lot screen): warehouse.kn5 and the warehouse.json that
/// goes with it, out of the pictures prepare_warehouse.py gathered.
///
/// A brick shed with a pitched roof on steel trusses, 28 m down the aisle and 24 m across: ten bays, five a side, the
/// cars facing each other over the aisle; a roller door at the near end, the warehouse's number on the far wall, lamps
/// hanging over the bays and skylights in the roof.
///
/// Every surface is its own material ("wh_floor", "wh_wall", ...), and every picture of every look goes into the model,
/// so the game dresses one warehouse differently from the next by pointing those materials at other pictures, and puts
/// up its number by pointing the two digit plates at the right digits, without loading the scene again.
///
///   WarehouseSceneBuilder &lt;prepared folder&gt; &lt;out folder&gt; [--flip]
/// </summary>
internal static class Program
{
    // The hall, in metres: x down the aisle (the door at +x), z across it, y up; the middle of the floor is the origin
    private const float HalfLength = 14f;
    private const float HalfWidth = 12f;
    private const float Eaves = 5.5f;
    private const float Ridge = 8f;
    private const float BrickTop = 3.2f;

    // Bays: five a side, one car's pitch apart, their middles this far either side of the aisle's
    private const float CarPitch = 4.6f;
    private const int BaysPerSide = 5;
    private const float RowOffset = 6.2f;
    private const float BayDepth = 6.2f;

    // The trusses stand between the bays, so a lamp over a car never has a truss in front of it
    private static readonly float[] Trusses = [-11.5f, -6.9f, -2.3f, 2.3f, 6.9f, 11.5f];

    // How big a picture is on the surface, in metres
    private const float FloorTile = 3f;
    private const float WallTile = 2.4f;
    private const float UpperTile = 3f;
    private const float SteelTile = 1.5f;

    private const float LampHeight = 4.3f;
    private const int MaxVerticesPerMesh = ushort.MaxValue;

    private static bool _flip;

    private static int Main(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith("--")).ToList();
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("WarehouseSceneBuilder <prepared folder> <out folder> [--flip]");
            return 1;
        }

        var prepared = positional[0];
        var output = positional[1];
        _flip = args.Contains("--flip");

        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = "warehouse";

        // Every picture goes in, whether the first look wears it or not: the others are switched to at run time
        foreach (var file in Directory.GetFiles(prepared, "*.jpg").OrderBy(f => f, StringComparer.Ordinal))
            AddTexture(kn5, file);

        var first = Looks[0];
        var floor = AddMaterial(kn5, "wh_floor", first.Floor, ambient: 0.32f, diffuse: 0.45f, specular: 0.12f, specularExp: 30f);
        var wall = AddMaterial(kn5, "wh_wall", first.Wall, ambient: 0.32f, diffuse: 0.45f);
        var upper = AddMaterial(kn5, "wh_upper", first.Upper, ambient: 0.32f, diffuse: 0.45f, specular: 0.08f, specularExp: 20f);
        var roof = AddMaterial(kn5, "wh_roof", first.Roof, ambient: 0.28f, diffuse: 0.35f);
        var door = AddMaterial(kn5, "wh_door", first.Door, ambient: 0.32f, diffuse: 0.45f, specular: 0.1f, specularExp: 20f);
        var steel = AddMaterial(kn5, "wh_steel", first.Steel, ambient: 0.32f, diffuse: 0.45f, specular: 0.15f, specularExp: 25f);
        var paint = AddMaterial(kn5, "wh_paint", "paint_yellow.jpg", ambient: 0.32f, diffuse: 0.45f);
        var sign = AddMaterial(kn5, "wh_sign", "sign_warehouse.jpg", ambient: 0.38f, diffuse: 0.45f, specular: 0.2f, specularExp: 40f);
        var tens = AddMaterial(kn5, "wh_digit_tens", "sign_digit_0.jpg", ambient: 0.38f, diffuse: 0.45f, specular: 0.2f, specularExp: 40f);
        var units = AddMaterial(kn5, "wh_digit_units", "sign_digit_1.jpg", ambient: 0.38f, diffuse: 0.45f, specular: 0.2f, specularExp: 40f);
        var skylight = AddMaterial(kn5, "wh_skylight", "skylight.jpg", ambient: 0f, diffuse: 0f, emissive: 1f);
        var shade = AddMaterial(kn5, "wh_shade", "shade.jpg", ambient: 0.3f, diffuse: 0.45f, specular: 0.3f, specularExp: 40f);
        var bulb = AddMaterial(kn5, "wh_bulb", "bulb.jpg", ambient: 0f, diffuse: 0f, emissive: 1f);
        var cable = AddMaterial(kn5, "wh_cable", "cable.jpg", ambient: 0.3f, diffuse: 0.3f);

        AddMesh(kn5, "WH_FLOOR", Floor(), floor);
        AddMesh(kn5, "WH_WALL", Walls(0f, BrickTop, WallTile), wall);
        AddMesh(kn5, "WH_UPPER", Walls(BrickTop, Eaves, UpperTile).Append(Gables()), upper);
        AddMesh(kn5, "WH_ROOF", Roof(), roof);
        AddMesh(kn5, "WH_DOOR", Door(), door);
        AddMesh(kn5, "WH_STEEL", Steelwork(), steel);
        AddMesh(kn5, "WH_PAINT", BayLines(), paint);

        var (plate, tensPlate, unitsPlate) = Sign();
        AddMesh(kn5, "WH_SIGN", plate, sign);
        AddMesh(kn5, "WH_DIGIT_TENS", tensPlate, tens);
        AddMesh(kn5, "WH_DIGIT_UNITS", unitsPlate, units);

        AddMesh(kn5, "WH_SKYLIGHT", Skylights(), skylight);

        var (shades, bulbs, cables) = LampFittings();
        AddMesh(kn5, "WH_SHADE", shades, shade);
        AddMesh(kn5, "WH_BULB", bulbs, bulb);
        AddMesh(kn5, "WH_CABLE", cables, cable);

        Directory.CreateDirectory(output);
        var path = Path.Combine(output, "warehouse.kn5");
        kn5.Save(path);
        Console.WriteLine($"{path}: {new FileInfo(path).Length / 1024} KB");

        var json = Path.Combine(output, "warehouse.json");
        File.WriteAllText(json, Description().ToString(Formatting.Indented) + Environment.NewLine);
        Console.WriteLine(json);
        return 0;
    }

    #region Looks

    /// <summary>What a warehouse wears: a picture for each surface, and the colour its lamps give</summary>
    private sealed record Look(string Id, string Floor, string Wall, string Upper, string Roof, string Door, string Steel, int[] Lamps);

    private static readonly Look[] Looks =
    [
        new("red_brick", "floor_concrete_floor_worn_001.jpg", "wall_red_brick_03.jpg", "upper_corrugated_iron_02.jpg",
            "upper_corrugated_iron_02.jpg", "door_painted_metal_shutter.jpg", "steel_blue_metal_plate.jpg", [255, 214, 170]),
        new("old_factory", "floor_hangar_concrete_floor.jpg", "wall_factory_brick.jpg", "upper_rusty_corrugated_iron.jpg",
            "upper_rusty_corrugated_iron.jpg", "door_rusty_metal_shutter.jpg", "steel_rusty_painted_metal.jpg", [255, 190, 130]),
        new("whitewashed", "floor_garage_floor.jpg", "wall_whitewashed_brick.jpg", "upper_box_profile_metal_sheet.jpg",
            "upper_corrugated_iron_02.jpg", "door_wooden_garage_door.jpg", "steel_green_metal_rust.jpg", [235, 240, 255]),
        new("block_shed", "floor_smooth_concrete_floor.jpg", "wall_concrete_block_wall.jpg", "upper_weathered_plank_siding.jpg",
            "upper_corrugated_iron_02.jpg", "door_rusty_metal_shutter.jpg", "steel_metal_plate.jpg", [230, 245, 255]),
        new("yellow_brick", "floor_concrete_floor_worn_001.jpg", "wall_yellow_brick.jpg", "upper_corrugated_iron_02.jpg",
            "upper_rusty_corrugated_iron.jpg", "door_painted_metal_shutter.jpg", "steel_rusty_painted_metal.jpg", [255, 205, 150])
    ];

    #endregion

    #region Geometry

    private sealed class Mesh
    {
        public readonly List<Kn5Node.Vertex> Vertices = [];
        public readonly List<int> Indices = [];

        public Mesh Append(Mesh other)
        {
            var offset = Vertices.Count;
            Vertices.AddRange(other.Vertices);
            Indices.AddRange(other.Indices.Select(i => i + offset));
            return this;
        }

        /// <summary>
        /// A flat four-cornered face seen from the side <paramref name="normal"/> points to, the corners in order round
        /// its edge; the picture laid on it by <paramref name="uv"/>, from where a corner is
        /// </summary>
        public void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 normal, Func<Vec3, Vec2> uv)
        {
            var i = Vertices.Count;
            var tangent = Vec3.Normalize(b - a);
            foreach (var p in new[] { a, b, c, d }) Vertices.Add(new Kn5Node.Vertex(p, normal, uv(p), tangent));
            Facing(i, i + 1, i + 2, normal);
            Facing(i, i + 2, i + 3, normal);
        }

        public void Triangle(Vec3 a, Vec3 b, Vec3 c, Vec3 normal, Func<Vec3, Vec2> uv)
        {
            var i = Vertices.Count;
            var tangent = Vec3.Normalize(b - a);
            foreach (var p in new[] { a, b, c }) Vertices.Add(new Kn5Node.Vertex(p, normal, uv(p), tangent));
            Facing(i, i + 1, i + 2, normal);
        }

        /// <summary>A triangle wound so its front is the side <paramref name="normal"/> points to</summary>
        private void Facing(int a, int b, int c, Vec3 normal)
        {
            var pa = Vertices[a].Position;
            var cross = Vec3.Cross(Vertices[b].Position - pa, Vertices[c].Position - pa);
            var along = Vec3.Dot(cross, normal) > 0f;
            if (along != _flip) Indices.AddRange([a, b, c]);
            else Indices.AddRange([a, c, b]);
        }

        /// <summary>
        /// A square-section bar from <paramref name="from"/> to <paramref name="to"/>, <paramref name="size"/> across:
        /// its four sides (the ends are never seen)
        /// </summary>
        public void Bar(Vec3 from, Vec3 to, float size, float tile)
        {
            var axis = Vec3.Normalize(to - from);
            var helper = MathF.Abs(axis.Y) > 0.9f ? Vec3.UnitX : Vec3.UnitY;
            var side = Vec3.Normalize(Vec3.Cross(axis, helper)) * (size / 2f);
            var up = Vec3.Normalize(Vec3.Cross(side, axis)) * (size / 2f);
            var length = Vec3.Distance(from, to);

            var corners = new[] { side + up, -side + up, -side - up, side - up };
            for (var k = 0; k < 4; k++)
            {
                var p = corners[k];
                var q = corners[(k + 1) % 4];
                var normal = Vec3.Normalize(p + q);
                var a = from + p;
                var b = from + q;
                var c = to + q;
                var d = to + p;
                Quad(a, b, c, d, normal, v =>
                {
                    var along = Vec3.Dot(v - from, axis);
                    var across = Vec3.Dot(v - from, Vec3.Normalize(q - p)) + size;
                    return new Vec2(along / tile, across / tile);
                });
            }
        }
    }

    private static float RoofHeight(float z) => Ridge - (Ridge - Eaves) * MathF.Abs(z) / HalfWidth;

    private static Vec3 V(float x, float y, float z) => new(x, y, z);

    private static Mesh Floor()
    {
        var mesh = new Mesh();
        mesh.Quad(V(-HalfLength, 0f, -HalfWidth), V(HalfLength, 0f, -HalfWidth), V(HalfLength, 0f, HalfWidth), V(-HalfLength, 0f, HalfWidth),
            Vec3.UnitY, p => new Vec2(p.X / FloorTile, p.Z / FloorTile));
        return mesh;
    }

    /// <summary>The four walls between two heights, facing in</summary>
    private static Mesh Walls(float bottom, float top, float tile)
    {
        var mesh = new Mesh();

        // The long sides
        foreach (var z in new[] { -HalfWidth, HalfWidth })
        {
            mesh.Quad(V(-HalfLength, bottom, z), V(HalfLength, bottom, z), V(HalfLength, top, z), V(-HalfLength, top, z),
                V(0f, 0f, -MathF.Sign(z)), p => new Vec2(p.X / tile, -p.Y / tile));
        }

        // The ends
        foreach (var x in new[] { -HalfLength, HalfLength })
        {
            mesh.Quad(V(x, bottom, -HalfWidth), V(x, bottom, HalfWidth), V(x, top, HalfWidth), V(x, top, -HalfWidth),
                V(-MathF.Sign(x), 0f, 0f), p => new Vec2(p.Z / tile, -p.Y / tile));
        }

        return mesh;
    }

    /// <summary>The ends' triangles up to the ridge</summary>
    private static Mesh Gables()
    {
        var mesh = new Mesh();
        foreach (var x in new[] { -HalfLength, HalfLength })
        {
            var normal = V(-MathF.Sign(x), 0f, 0f);
            Vec2 Uv(Vec3 p) => new(p.Z / UpperTile, -p.Y / UpperTile);
            mesh.Triangle(V(x, Eaves, -HalfWidth), V(x, Eaves, 0f), V(x, Ridge, 0f), normal, Uv);
            mesh.Triangle(V(x, Eaves, 0f), V(x, Eaves, HalfWidth), V(x, Ridge, 0f), normal, Uv);
        }

        return mesh;
    }

    /// <summary>The underside of the two slopes, the corrugations running down them</summary>
    private static Mesh Roof()
    {
        var mesh = new Mesh();
        var slope = (Ridge - Eaves) / HalfWidth;
        foreach (var side in new[] { -1f, 1f })
        {
            // Facing down and in, square to the slope
            var normal = Vec3.Normalize(V(0f, -1f, -side * slope));
            Vec2 Uv(Vec3 p) => new(p.X / UpperTile, MathF.Abs(p.Z) * MathF.Sqrt(1f + slope * slope) / UpperTile);
            mesh.Quad(V(-HalfLength, Ridge, 0f), V(HalfLength, Ridge, 0f), V(HalfLength, Eaves, side * HalfWidth), V(-HalfLength, Eaves, side * HalfWidth),
                normal, Uv);
        }

        return mesh;
    }

    /// <summary>The roller door at the near end of the aisle, a little proud of the brick, with a steel frame round it</summary>
    private static Mesh Door()
    {
        const float half = 3f;
        const float height = 4.4f;
        const float x = HalfLength - 0.03f;
        var mesh = new Mesh();
        mesh.Quad(V(x, 0f, -half), V(x, 0f, half), V(x, height, half), V(x, height, -half), V(-1f, 0f, 0f),
            p => new Vec2((p.Z + half) / (2f * half), (height - p.Y) / height));
        return mesh;
    }

    private static Mesh Steelwork()
    {
        var mesh = new Mesh();

        foreach (var x in Trusses)
        {
            // A column at each wall, the bottom chord across, the rafters up to the ridge
            foreach (var z in new[] { -HalfWidth + 0.2f, HalfWidth - 0.2f })
                mesh.Bar(V(x, 0f, z), V(x, Eaves, z), 0.3f, SteelTile);

            mesh.Bar(V(x, Eaves, -HalfWidth), V(x, Eaves, HalfWidth), 0.2f, SteelTile);
            foreach (var side in new[] { -1f, 1f })
                mesh.Bar(V(x, Eaves, side * HalfWidth), V(x, Ridge - 0.12f, 0f), 0.2f, SteelTile);

            // The web: posts every 4 m, and a brace from the foot of each post out to the next one's head
            mesh.Bar(V(x, Eaves, 0f), V(x, Ridge - 0.12f, 0f), 0.12f, SteelTile);
            foreach (var side in new[] { -1f, 1f })
            {
                foreach (var z in new[] { 4f, 8f })
                    mesh.Bar(V(x, Eaves, side * z), V(x, RoofHeight(z) - 0.12f, side * z), 0.1f, SteelTile);
                foreach (var z in new[] { 0f, 4f })
                    mesh.Bar(V(x, Eaves, side * z), V(x, RoofHeight(z + 4f) - 0.12f, side * (z + 4f)), 0.08f, SteelTile);
            }
        }

        // Purlins down the length, under the roof
        foreach (var z in new[] { -10f, -6f, -2f, 2f, 6f, 10f })
            mesh.Bar(V(-HalfLength, RoofHeight(z) - 0.1f, z), V(HalfLength, RoofHeight(z) - 0.1f, z), 0.12f, SteelTile);

        // The door's frame
        const float doorHalf = 3f;
        const float doorHeight = 4.4f;
        const float frameX = HalfLength - 0.1f;
        mesh.Bar(V(frameX, 0f, -doorHalf - 0.1f), V(frameX, doorHeight + 0.1f, -doorHalf - 0.1f), 0.2f, SteelTile);
        mesh.Bar(V(frameX, 0f, doorHalf + 0.1f), V(frameX, doorHeight + 0.1f, doorHalf + 0.1f), 0.2f, SteelTile);
        mesh.Bar(V(frameX, doorHeight + 0.1f, -doorHalf - 0.2f), V(frameX, doorHeight + 0.1f, doorHalf + 0.2f), 0.2f, SteelTile);

        return mesh;
    }

    /// <summary>Where the bays are, their middles: five a side, a row either side of the aisle</summary>
    private static IEnumerable<(float X, float Z, float Heading)> Bays()
    {
        // Across the aisle from each other, noses to the aisle: heading 0 faces +z
        foreach (var (z, heading) in new[] { (-RowOffset, 0f), (RowOffset, 180f) })
        {
            for (var i = 0; i < BaysPerSide; i++)
                yield return ((i - (BaysPerSide - 1) / 2f) * CarPitch, z, heading);
        }
    }

    /// <summary>The yellow lines between the bays, and along the aisle's edge</summary>
    private static Mesh BayLines()
    {
        const float width = 0.12f;
        const float y = 0.004f;
        var mesh = new Mesh();
        Vec2 Uv(Vec3 p) => new(p.X / 1.5f, p.Z / 1.5f);

        var near = RowOffset - BayDepth / 2f;
        var far = RowOffset + BayDepth / 2f;
        var ends = (BaysPerSide / 2f) * CarPitch;

        foreach (var side in new[] { -1f, 1f })
        {
            for (var i = 0; i <= BaysPerSide; i++)
            {
                var x = -ends + i * CarPitch;
                mesh.Quad(V(x - width / 2f, y, side * near), V(x + width / 2f, y, side * near), V(x + width / 2f, y, side * far), V(x - width / 2f, y, side * far),
                    Vec3.UnitY, Uv);
            }

            var edge = side * near;
            mesh.Quad(V(-ends, y, edge - width / 2f), V(ends, y, edge - width / 2f), V(ends, y, edge + width / 2f), V(-ends, y, edge + width / 2f),
                Vec3.UnitY, Uv);
        }

        return mesh;
    }

    /// <summary>
    /// The enamel plate on the far wall: WAREHOUSE, then the two digits of the warehouse's number, reading left to
    /// right as seen from the door
    /// </summary>
    private static (Mesh Plate, Mesh Tens, Mesh Units) Sign()
    {
        const float x = -HalfLength + 0.05f;
        const float bottom = 3.9f;
        const float height = 0.8f;
        const float plateWidth = 3.2f;
        const float gap = 0.12f;
        const float digitWidth = 0.8f;
        var normal = V(1f, 0f, 0f);

        // Seen from the door, looking down -x, the viewer's right is -z: the sign reads from +z towards -z
        var start = (plateWidth + gap + 2f * digitWidth + gap / 2f) / 2f;

        Mesh Panel(float left, float width)
        {
            var mesh = new Mesh();
            var right = left - width;
            mesh.Quad(V(x, bottom, left), V(x, bottom, right), V(x, bottom + height, right), V(x, bottom + height, left), normal,
                p => new Vec2((left - p.Z) / width, (bottom + height - p.Y) / height));
            return mesh;
        }

        var plate = Panel(start, plateWidth);
        var tens = Panel(start - plateWidth - gap, digitWidth);
        var units = Panel(start - plateWidth - gap - digitWidth - gap / 2f, digitWidth);
        return (plate, tens, units);
    }

    /// <summary>Panes of wired glass in the roof, in every other span between the trusses, on both slopes</summary>
    private static Mesh Skylights()
    {
        var mesh = new Mesh();
        var slope = (Ridge - Eaves) / HalfWidth;
        for (var i = 0; i + 1 < Trusses.Length; i += 2)
        {
            var x0 = Trusses[i] + 0.4f;
            var x1 = Trusses[i + 1] - 0.4f;
            foreach (var side in new[] { -1f, 1f })
            {
                var normal = Vec3.Normalize(V(0f, -1f, -side * slope));
                var z0 = side * 3f;
                var z1 = side * 8f;
                float Y(float z) => RoofHeight(z) - 0.03f;
                mesh.Quad(V(x0, Y(z0), z0), V(x1, Y(z0), z0), V(x1, Y(z1), z1), V(x0, Y(z1), z1), normal,
                    p => new Vec2((p.X - x0) / (x1 - x0), (MathF.Abs(p.Z) - 3f) / 5f));
            }
        }

        return mesh;
    }

    /// <summary>Where the lamps hang: over the middle bay and the two outer ones, above each row</summary>
    private static IEnumerable<Vec3> Lamps()
    {
        foreach (var z in new[] { -RowOffset + 1f, RowOffset - 1f })
        {
            foreach (var x in new[] { -2f * CarPitch, 0f, 2f * CarPitch })
                yield return V(x, LampHeight, z);
        }
    }

    /// <summary>An enamel shade on a cable for each lamp, and the bulb under it</summary>
    private static (Mesh Shades, Mesh Bulbs, Mesh Cables) LampFittings()
    {
        const int segments = 20;
        const float topRadius = 0.09f;
        const float bottomRadius = 0.42f;
        const float shadeHeight = 0.34f;
        var shades = new Mesh();
        var bulbs = new Mesh();
        var cables = new Mesh();

        foreach (var lamp in Lamps())
        {
            for (var k = 0; k < segments; k++)
            {
                var a0 = 2f * MathF.PI * k / segments;
                var a1 = 2f * MathF.PI * (k + 1) / segments;
                Vec3 Ring(float a, float r, float y) => V(lamp.X + r * MathF.Cos(a), y, lamp.Z + r * MathF.Sin(a));
                var mid = (a0 + a1) / 2f;
                var outward = Vec3.Normalize(V(MathF.Cos(mid) * shadeHeight, bottomRadius - topRadius, MathF.Sin(mid) * shadeHeight));
                var top = lamp.Y + shadeHeight;
                Vec2 Uv(Vec3 p) => new(0.5f, 0.5f);

                // Outside and in: the shade is seen from below as well as from across the hall
                shades.Quad(Ring(a0, bottomRadius, lamp.Y), Ring(a1, bottomRadius, lamp.Y), Ring(a1, topRadius, top), Ring(a0, topRadius, top), outward, Uv);
                shades.Quad(Ring(a0, bottomRadius, lamp.Y), Ring(a1, bottomRadius, lamp.Y), Ring(a1, topRadius, top), Ring(a0, topRadius, top), -outward, Uv);

                // The bulb: a small disc just inside the rim, facing down
                bulbs.Triangle(V(lamp.X, lamp.Y + 0.08f, lamp.Z), Ring(a0, 0.14f, lamp.Y + 0.1f), Ring(a1, 0.14f, lamp.Y + 0.1f), -Vec3.UnitY, Uv);
            }

            var cableTop = RoofHeight(lamp.Z) - 0.1f;
            cables.Bar(V(lamp.X, lamp.Y + shadeHeight, lamp.Z), V(lamp.X, cableTop, lamp.Z), 0.025f, 1f);
        }

        return (shades, bulbs, cables);
    }

    #endregion

    #region Description

    /// <summary>
    /// warehouse.json: where the cars stand and the camera, the renderer's light for each light of the day with a lamp
    /// under each shade, and the looks
    /// </summary>
    private static JObject Description()
    {
        static JArray Rgb(int r, int g, int b) => new(r, g, b);
        static float Round(float v) => MathF.Round(v, 3);

        var lamps = new JArray(Lamps().Select(l => new JObject
        {
            ["x"] = Round(l.X), ["y"] = Round(l.Y - 0.1f), ["z"] = Round(l.Z), ["brightness"] = 0.8f, ["range"] = 14f
        }));

        JObject Light(float brightness, JArray color, float ambient, JArray up, JArray down, float lampScale) => new()
        {
            ["brightness"] = brightness, ["color"] = color, ["ambient"] = ambient, ["ambientUp"] = up, ["ambientDown"] = down,
            ["cubemapAmbient"] = 0f, ["lampScale"] = lampScale
        };

        return new JObject
        {
            ["_comment"] = new JArray(
                "The warehouse the player's cars are kept in (the car lot screen). warehouse.kn5 and this file are written by",
                "tools/WarehouseScene (prepare_warehouse.py, then WarehouseSceneBuilder): change the builder, not these files.",
                "x runs down the aisle (the roller door at +x), z across it, y up; the middle of the floor is the origin.",
                "bays: where a car stands, heading in degrees about the vertical (0 faces +z). The warehouse holds as many cars",
                "as it has bays; more cars fill a second warehouse, and so on.",
                "camera: the view down the aisle, orbiting a point targetHeight over the middle (alpha 0 = the camera on +x,",
                "radians; beta up from level; radius metres).",
                "room: the inside the camera stays in, however it is swung or wheeled out: the walls halfLength either side of the",
                "middle along x and halfWidth along z, the roof height up at its lowest (the eaves).",
                "sun: the key light's direction, from overhead through the skylights; its shadows are the cars' on the floor.",
                "lights: the renderer's light for each light of the day. The lamps are lit at every hour, each colour its look's;",
                "lampScale is how bright they are against their own brightness then. skylight is how bright the glass is.",
                "looks: what a warehouse wears, the picture on each surface; warehouse n (from 1) wears look (n - 1) mod count.",
                "Textures: Poly Haven (CC0), the rest drawn by prepare_warehouse.py."),
            ["id"] = "warehouse",
            ["title"] = "The warehouse",
            ["credit"] = "Surface textures by Poly Haven (CC0): " + string.Join(", ", Looks
                .SelectMany(l => new[] { l.Floor, l.Wall, l.Upper, l.Roof, l.Door, l.Steel })
                .Select(t => t[(t.IndexOf('_') + 1)..^4]).Distinct().Order(StringComparer.Ordinal)),
            ["bays"] = new JArray(Bays().Select(b => new JObject { ["x"] = Round(b.X), ["z"] = Round(b.Z), ["heading"] = b.Heading })),
            ["camera"] = new JObject { ["alpha"] = 0f, ["beta"] = 0.16f, ["radius"] = 12.5f, ["targetHeight"] = 1.6f },
            ["room"] = new JObject { ["halfLength"] = HalfLength, ["halfWidth"] = HalfWidth, ["height"] = Eaves },
            ["sun"] = new JArray(0.25f, 1f, 0.35f),
            ["lights"] = new JObject
            {
                ["day"] = Light(0.8f, Rgb(235, 235, 225), 1.0f, Rgb(170, 180, 190), Rgb(120, 115, 105), 0.7f),
                ["dusk"] = Light(0.45f, Rgb(255, 190, 150), 1.0f, Rgb(130, 115, 125), Rgb(95, 85, 75), 1f),
                ["night"] = Light(0.15f, Rgb(140, 160, 220), 0.7f, Rgb(70, 80, 105), Rgb(65, 58, 50), 1.15f)
            },
            ["skylight"] = new JObject { ["day"] = 1.1f, ["dusk"] = 0.55f, ["night"] = 0.08f },
            ["lamps"] = lamps,
            ["looks"] = new JArray(Looks.Select(l => new JObject
            {
                ["id"] = l.Id,
                ["surfaces"] = new JObject
                {
                    ["wh_floor"] = l.Floor, ["wh_wall"] = l.Wall, ["wh_upper"] = l.Upper, ["wh_roof"] = l.Roof,
                    ["wh_door"] = l.Door, ["wh_steel"] = l.Steel
                },
                ["lampColor"] = new JArray(l.Lamps)
            })),
            ["digits"] = new JObject
            {
                ["tens"] = "wh_digit_tens", ["units"] = "wh_digit_units", ["picture"] = "sign_digit_{0}.jpg"
            }
        };
    }

    #endregion

    #region Writing

    private static void AddTexture(IKn5 kn5, string file)
    {
        var data = File.ReadAllBytes(file);
        var key = Path.GetFileName(file).ToLowerInvariant();
        kn5.Textures[key] = new Kn5Texture { Name = key, Active = true, Length = data.Length };
        kn5.TexturesData[key] = data;
    }

    private static uint AddMaterial(IKn5 kn5, string name, string texture, float ambient, float diffuse, float specular = 0f,
        float specularExp = 1f, float emissive = 0f)
    {
        if (!kn5.Textures.ContainsKey(texture)) throw new InvalidOperationException($"{name}: no picture {texture}");

        kn5.Materials[name] = new Kn5Material
        {
            Name = name,
            ShaderName = "ksPerPixel",
            ShaderProperties =
            [
                Property("ksAmbient", ambient),
                Property("ksDiffuse", diffuse),
                Property("ksSpecular", specular),
                Property("ksSpecularEXP", specularExp),
                new Kn5Material.ShaderProperty { Name = "ksEmissive", ValueC = new Vec3(emissive, emissive, emissive) },
                Property("ksAlphaRef", 0f)
            ],
            TextureMappings = [new Kn5Material.TextureMapping { Name = "txDiffuse", Texture = texture, Slot = 0 }]
        };
        return (uint)(kn5.Materials.Count - 1);
    }

    private static Kn5Material.ShaderProperty Property(string name, float value) => new() { Name = name, ValueA = value };

    private static void AddMesh(IKn5 kn5, string name, Mesh mesh, uint material)
    {
        if (mesh.Vertices.Count > MaxVerticesPerMesh) throw new InvalidOperationException($"{name}: too many vertices");

        var min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vec3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var v in mesh.Vertices)
        {
            min = new Vec3(Math.Min(min.X, v.Position.X), Math.Min(min.Y, v.Position.Y), Math.Min(min.Z, v.Position.Z));
            max = new Vec3(Math.Max(max.X, v.Position.X), Math.Max(max.Y, v.Position.Y), Math.Max(max.Z, v.Position.Z));
        }

        var size = max - min;
        kn5.RootNode.Children.Add(new Kn5Node
        {
            NodeClass = Kn5NodeClass.Mesh,
            Name = name,
            Active = true,
            Children = [],
            // The key light comes in from overhead, through the roof: nothing of the building may stand in its way
            CastShadows = false,
            IsVisible = true,
            IsRenderable = true,
            Vertices = mesh.Vertices.ToArray(),
            Indices = mesh.Indices.Select(i => (ushort)i).ToArray(),
            MaterialId = material,
            BoundingSphereCenter = (min + max) / 2f,
            BoundingSphereRadius = size.Length() / 2f
        });
    }

    #endregion
}
