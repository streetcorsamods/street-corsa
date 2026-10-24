using AcTools.Kn5File;
using AcTools.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreetCorsa.Tools;
using static StreetCorsa.Tools.Kn5SceneWriter;

namespace GarageSceneBuilder;

/// <summary>
/// Builds the player's garage (the garage screen): workshop.kn5 and the scene.json that goes with it, out of the
/// pictures prepare_garage.py gathered.
///
/// A two-bay block garage of the early sixties, 16 m across and 19 m deep, under a pitched roof on open timber
/// trusses: the car stands in the right-hand bay, its sectional door rolled up overhead so a car comes in off the
/// driveway; the left bay is the workshop, its door shut, a pegboard on the front wall over where the workbench stands.
/// The walls are concrete block, the lower part painted green, with wired-glass windows down the right side and a
/// side door on the left; fluorescent fittings hang off the tie beams. Nothing loose is in the model: the benches,
/// chests, cans and tyres are the props' (Content/streetrod/props, placed by each prop's place in "workshop").
///
/// Every surface is its own material ("gr_floor", "gr_wall", ...), for a mod to dress the garage differently.
///
///   GarageSceneBuilder &lt;prepared folder&gt; &lt;out folder&gt; [--flip]
/// </summary>
internal static class Program
{
    private const string Id = "workshop";

    // The room, in metres: x across (the car's left, the workshop bay, at +x), z along the car (its nose at +z, the
    // doors behind it at -z), y up; the car stands on the origin
    private const float RightWall = -7f;
    private const float LeftWall = 9f;
    private const float BackWall = -9f;
    private const float FrontWall = 10f;
    private const float Eaves = 3.4f;
    private const float Ridge = 5f;
    private const float RidgeX = (RightWall + LeftWall) / 2f;

    // The painted dado and the stripe along its top
    private const float DadoTop = 1.15f;
    private const float StripeTop = 1.2f;

    // The car's door: open, the door up under the roof; the workshop's beside it, shut
    private const float DoorHalf = 1.4f;
    private const float DoorHeight = 2.3f;
    private const float WorkshopDoorX = 6f;

    // The trusses, along z; the fittings hang off the two either side of the car's middle
    private static readonly float[] Trusses = [-7.6f, -5.1f, -2.6f, -0.1f, 2.4f, 4.9f, 7.4f];
    private static readonly (float X, float Z)[] Fittings =
        [(0f, -5.1f), (0f, -0.1f), (0f, 4.9f), (WorkshopDoorX, -5.1f), (WorkshopDoorX, -0.1f), (WorkshopDoorX, 4.9f), (0f, 7.4f), (WorkshopDoorX, 7.4f)];
    private const float FittingHeight = 3.1f;

    // How big a picture is on the surface, in metres
    private const float FloorTile = 3f;
    private const float WallTile = 2f;
    private const float RoofTile = 2f;
    private const float TimberTile = 1.5f;
    private const float DriveTile = 3f;

    private static bool _flip;

    private static int Main(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith("--")).ToList();
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("GarageSceneBuilder <prepared folder> <out folder> [--flip]");
            return 1;
        }

        var prepared = positional[0];
        var output = positional[1];
        _flip = args.Contains("--flip");

        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = Id;

        foreach (var file in Directory.GetFiles(prepared, "*.jpg").Order(StringComparer.Ordinal))
            AddTexture(kn5, file);

        uint Plain(string name, string picture, float specular = 0f, float specularExp = 1f) =>
            AddMaterial(kn5, name, picture, ambient: 0.32f, diffuse: 0.45f, specular: specular, specularExp: specularExp);

        var floor = Plain("gr_floor", "floor_garage_floor.jpg", 0.12f, 30f);
        var wall = Plain("gr_wall", "wall_concrete_block_wall_03.jpg");
        var dado = Plain("gr_dado", "dado_painted_concrete.jpg", 0.08f, 20f);
        var stripe = Plain("gr_stripe", "stripe.jpg");
        var roof = Plain("gr_roof", "roof_weathered_brown_planks.jpg");
        var timber = Plain("gr_timber", "timber_weathered_planks.jpg");
        var door = Plain("gr_door", "door_sections.jpg", 0.1f, 20f);
        var sideDoor = Plain("gr_sidedoor", "sidedoor_rough_pine_door.jpg");
        // Outside, in the open: lit more by the sky than the room's walls are
        var drive = AddMaterial(kn5, "gr_drive", "drive_concrete_pavement.jpg", ambient: 0.6f, diffuse: 0.5f);
        var track = Plain("gr_track", "track.jpg", 0.2f, 30f);
        var frame = Plain("gr_frame", "frame.jpg", 0.1f, 20f);
        var fitting = Plain("gr_fitting", "fitting.jpg", 0.3f, 40f);
        var pegboard = Plain("gr_pegboard", "pegboard.jpg");
        var posters = new Dictionary<string, uint>
        {
            ["poster_oil"] = Plain("gr_poster_oil", "poster_oil.jpg", 0.1f, 20f),
            ["poster_plugs"] = Plain("gr_poster_plugs", "poster_plugs.jpg", 0.1f, 20f),
            ["poster_tyres"] = Plain("gr_poster_tyres", "poster_tyres.jpg", 0.1f, 20f),
            ["calendar"] = Plain("gr_calendar", "calendar.jpg")
        };
        var window = AddMaterial(kn5, "gr_window", "window.jpg", ambient: 0f, diffuse: 0f, emissive: 1f);
        var tube = AddMaterial(kn5, "gr_tube", "tube.jpg", ambient: 0f, diffuse: 0f, emissive: 1.2f);

        AddMesh(kn5, "GR_FLOOR", Floor(), floor);
        AddMesh(kn5, "GR_DADO", Walls(0f, DadoTop, Openings()), dado);
        AddMesh(kn5, "GR_STRIPE", Walls(DadoTop, StripeTop, Openings()), stripe);
        AddMesh(kn5, "GR_WALL", Walls(StripeTop, Eaves, Openings()).Append(Gables()).Append(Reveals()), wall);
        AddMesh(kn5, "GR_ROOF", Roof(), roof);
        AddMesh(kn5, "GR_TIMBER", Trussing().Append(DoorCasing()), timber);
        AddMesh(kn5, "GR_DOOR", Doors(), door);
        AddMesh(kn5, "GR_SIDEDOOR", SideDoor(), sideDoor);
        AddMesh(kn5, "GR_TRACK", DoorTracks(), track);
        AddMesh(kn5, "GR_DRIVE", Driveway(), drive);
        AddMesh(kn5, "GR_PEGBOARD", Pegboard(), pegboard);

        var (glass, frames) = Windows();
        AddMesh(kn5, "GR_WINDOW", glass, window);
        AddMesh(kn5, "GR_FRAME", frames, frame);

        foreach (var (name, material) in posters)
            AddMesh(kn5, "GR_" + name.ToUpperInvariant(), Poster(name), material);

        var (fittings, tubes) = LightFittings();
        AddMesh(kn5, "GR_FITTING", fittings, fitting);
        AddMesh(kn5, "GR_TUBE", tubes, tube);

        // Past the apron, seen through the open door: black for now, the owner's call until the garage has a street of its
        // own (a mod may add sky domes for the hours, GARAGE_SKY_DAY...). A dome, not the renderer's background, which
        // is white
        var outside = AddMaterial(kn5, "gr_outside", "black.jpg", ambient: 0f, diffuse: 0f);
        SkyDome(kn5, "GR_OUTSIDE", outside, 60f, segments: 32, rings: 16, below: -1.5f);

        SaveScene(kn5, output, Id, Description().ToString(Formatting.Indented));

        // SaveScene names the description after the scene; the garage's is scene.json, as a mod's garage's is
        var written = Path.Combine(output, Id + ".json");
        var scene = Path.Combine(output, "scene.json");
        File.Move(written, scene, overwrite: true);
        return 0;
    }

    #region Geometry

    // The shapes the scene builders share (tools/Shared/SceneMesh.cs), wound the other way round with --flip
    private sealed class Mesh() : SceneMesh(_flip);

    private static Vec3 V(float x, float y, float z) => new(x, y, z);

    private static float RoofHeight(float x) => Ridge - (Ridge - Eaves) * MathF.Abs(x - RidgeX) / (LeftWall - RidgeX);

    private static Mesh Floor()
    {
        var mesh = new Mesh();
        mesh.Quad(V(RightWall, 0f, BackWall), V(LeftWall, 0f, BackWall), V(LeftWall, 0f, FrontWall), V(RightWall, 0f, FrontWall),
            Vec3.UnitY, p => new Vec2(p.X / FloorTile, p.Z / FloorTile));
        return mesh;
    }

    /// <summary>A hole in a wall: along the wall from u0 to u1, from v0 up to v1</summary>
    private readonly record struct Hole(float U0, float U1, float V0, float V1);

    /// <summary>A wall: where a point of it (along it, up it) is, which way it faces, and its holes</summary>
    private sealed record WallSide(Func<float, float, Vec3> At, Vec3 Normal, float From, float To, Hole[] Holes);

    /// <summary>The four walls, inside faces; the back wall has the car's door in it</summary>
    private static WallSide[] Openings() =>
    [
        new((u, v) => V(u, v, BackWall), Vec3.UnitZ, RightWall, LeftWall, [new(-DoorHalf, DoorHalf, 0f, DoorHeight)]),
        new((u, v) => V(u, v, FrontWall), -Vec3.UnitZ, RightWall, LeftWall, []),
        new((u, v) => V(RightWall, v, u), Vec3.UnitX, BackWall, FrontWall, []),
        new((u, v) => V(LeftWall, v, u), -Vec3.UnitX, BackWall, FrontWall, [])
    ];

    /// <summary>
    /// The walls between two heights, facing in, round their holes: the stretches between the holes full height, and
    /// over and under each hole what is left of the band
    /// </summary>
    private static Mesh Walls(float bottom, float top, WallSide[] sides)
    {
        var mesh = new Mesh();
        foreach (var side in sides)
        {
            void Rect(float u0, float u1, float v0, float v1)
            {
                if (u1 - u0 < 1e-3f || v1 - v0 < 1e-3f) return;
                var a = side.At(u0, v0);
                var b = side.At(u1, v0);
                var c = side.At(u1, v1);
                var d = side.At(u0, v1);
                var along = Vec3.Normalize(b - a);
                mesh.Quad(a, b, c, d, side.Normal, p => new Vec2(Vec3.Dot(p, along) / WallTile, -p.Y / WallTile));
            }

            var holes = side.Holes.Where(h => h.V1 > bottom && h.V0 < top).OrderBy(h => h.U0).ToList();
            var u = side.From;
            foreach (var hole in holes)
            {
                Rect(u, hole.U0, bottom, top);
                Rect(hole.U0, hole.U1, bottom, Math.Max(bottom, hole.V0));
                Rect(hole.U0, hole.U1, Math.Min(top, hole.V1), top);
                u = hole.U1;
            }

            Rect(u, side.To, bottom, top);
        }

        return mesh;
    }

    /// <summary>The front and back walls' triangles up to the ridge</summary>
    private static Mesh Gables()
    {
        var mesh = new Mesh();
        foreach (var (z, normal) in new[] { (BackWall, Vec3.UnitZ), (FrontWall, -Vec3.UnitZ) })
        {
            Vec2 Uv(Vec3 p) => new(p.X / WallTile, -p.Y / WallTile);
            mesh.Triangle(V(RightWall, Eaves, z), V(LeftWall, Eaves, z), V(RidgeX, Ridge, z), normal, Uv);
        }

        return mesh;
    }

    /// <summary>The block's thickness round the car's door: its sides and its head, seen from inside</summary>
    private static Mesh Reveals()
    {
        const float depth = 0.2f;
        var mesh = new Mesh();
        Vec2 Uv(Vec3 p) => new((p.Z + p.X) / WallTile, -p.Y / WallTile);
        foreach (var x in new[] { -DoorHalf, DoorHalf })
        {
            var normal = V(-MathF.Sign(x), 0f, 0f);
            mesh.Quad(V(x, 0f, BackWall), V(x, 0f, BackWall - depth), V(x, DoorHeight, BackWall - depth), V(x, DoorHeight, BackWall), normal, Uv);
        }

        mesh.Quad(V(-DoorHalf, DoorHeight, BackWall), V(DoorHalf, DoorHeight, BackWall), V(DoorHalf, DoorHeight, BackWall - depth),
            V(-DoorHalf, DoorHeight, BackWall - depth), -Vec3.UnitY, p => new Vec2(p.X / WallTile, p.Z / WallTile));
        return mesh;
    }

    /// <summary>The underside of the two slopes, the boards running up them</summary>
    private static Mesh Roof()
    {
        var mesh = new Mesh();
        var run = LeftWall - RidgeX;
        var rise = Ridge - Eaves;
        foreach (var (eaveX, side) in new[] { (RightWall, -1f), (LeftWall, 1f) })
        {
            // Facing down and in, square to the slope
            var normal = Vec3.Normalize(V(-side * rise, -run, 0f));
            Vec2 Uv(Vec3 p) => new(p.Z / RoofTile, MathF.Abs(p.X - RidgeX) * MathF.Sqrt(1f + rise * rise / (run * run)) / RoofTile);
            mesh.Quad(V(RidgeX, Ridge, BackWall), V(RidgeX, Ridge, FrontWall), V(eaveX, Eaves, FrontWall), V(eaveX, Eaves, BackWall), normal, Uv);
        }

        return mesh;
    }

    /// <summary>
    /// A king post truss at each of <see cref="Trusses"/>: the tie beam across under the eaves, the rafters up under the
    /// roof, the post from the beam to the ridge; the ridge board down the length
    /// </summary>
    private static Mesh Trussing()
    {
        var mesh = new Mesh();
        foreach (var z in Trusses)
        {
            mesh.Bar(V(RightWall, Eaves - 0.1f, z), V(LeftWall, Eaves - 0.1f, z), 0.18f, TimberTile);
            mesh.Bar(V(RightWall, Eaves, z), V(RidgeX, Ridge - 0.12f, z), 0.16f, TimberTile);
            mesh.Bar(V(LeftWall, Eaves, z), V(RidgeX, Ridge - 0.12f, z), 0.16f, TimberTile);
            mesh.Bar(V(RidgeX, Eaves, z), V(RidgeX, Ridge - 0.1f, z), 0.12f, TimberTile);

            // Struts from the foot of the post out to the middle of each rafter
            foreach (var side in new[] { -1f, 1f })
            {
                var x = RidgeX + side * (LeftWall - RidgeX) / 2f;
                mesh.Bar(V(RidgeX, Eaves, z), V(x, RoofHeight(x) - 0.12f, z), 0.1f, TimberTile);
            }
        }

        mesh.Bar(V(RidgeX, Ridge - 0.1f, BackWall), V(RidgeX, Ridge - 0.1f, FrontWall), 0.14f, TimberTile);
        return mesh;
    }

    /// <summary>The timber casing round the car's door and round the side door</summary>
    private static Mesh DoorCasing()
    {
        var mesh = new Mesh();
        const float z = BackWall + 0.04f;
        mesh.Bar(V(-DoorHalf - 0.06f, 0f, z), V(-DoorHalf - 0.06f, DoorHeight + 0.06f, z), 0.12f, TimberTile);
        mesh.Bar(V(DoorHalf + 0.06f, 0f, z), V(DoorHalf + 0.06f, DoorHeight + 0.06f, z), 0.12f, TimberTile);
        mesh.Bar(V(-DoorHalf - 0.12f, DoorHeight + 0.06f, z), V(DoorHalf + 0.12f, DoorHeight + 0.06f, z), 0.12f, TimberTile);

        var (z0, z1) = SideDoorSpan;
        const float x = LeftWall - 0.04f;
        mesh.Bar(V(x, 0f, z0 - 0.05f), V(x, SideDoorHeight + 0.05f, z0 - 0.05f), 0.1f, TimberTile);
        mesh.Bar(V(x, 0f, z1 + 0.05f), V(x, SideDoorHeight + 0.05f, z1 + 0.05f), 0.1f, TimberTile);
        mesh.Bar(V(x, SideDoorHeight + 0.05f, z0 - 0.1f), V(x, SideDoorHeight + 0.05f, z1 + 0.1f), 0.1f, TimberTile);
        return mesh;
    }

    /// <summary>
    /// The car's door rolled up, its sections lying flat under the roof on their tracks, both faces; and the workshop's
    /// door down, a little proud of the block
    /// </summary>
    private static Mesh Doors()
    {
        var mesh = new Mesh();
        const float y = DoorHeight + 0.14f;
        const float z0 = BackWall + 0.15f;
        const float z1 = z0 + DoorHeight;
        Vec2 Up(Vec3 p) => new((p.X + DoorHalf) / (2f * DoorHalf), (p.Z - z0) / DoorHeight);
        mesh.Quad(V(-DoorHalf, y, z0), V(DoorHalf, y, z0), V(DoorHalf, y, z1), V(-DoorHalf, y, z1), -Vec3.UnitY, Up);
        mesh.Quad(V(-DoorHalf, y + 0.01f, z0), V(DoorHalf, y + 0.01f, z0), V(DoorHalf, y + 0.01f, z1), V(-DoorHalf, y + 0.01f, z1), Vec3.UnitY, Up);

        const float z = BackWall + 0.03f;
        const float left = WorkshopDoorX - DoorHalf;
        mesh.Quad(V(left, 0f, z), V(left + 2f * DoorHalf, 0f, z), V(left + 2f * DoorHalf, DoorHeight, z), V(left, DoorHeight, z), Vec3.UnitZ,
            p => new Vec2((p.X - left) / (2f * DoorHalf), (DoorHeight - p.Y) / DoorHeight));
        return mesh;
    }

    /// <summary>The steel tracks the car's door runs in: up the sides of the opening, then back under the roof, hung off it</summary>
    private static Mesh DoorTracks()
    {
        var mesh = new Mesh();
        const float y = DoorHeight + 0.1f;
        const float z = BackWall + 0.08f;
        const float end = BackWall + 0.15f + DoorHeight + 0.3f;
        foreach (var x in new[] { -DoorHalf - 0.04f, DoorHalf + 0.04f })
        {
            mesh.Bar(V(x, 0f, z), V(x, y, z), 0.06f, 1f);
            mesh.Bar(V(x, y, z), V(x, y, end), 0.06f, 1f);
            mesh.Bar(V(x, y, end), V(x, Eaves - 0.18f, end), 0.04f, 1f);
        }

        mesh.Bar(V(-DoorHalf - 0.04f, y, end), V(DoorHalf + 0.04f, y, end), 0.05f, 1f);
        return mesh;
    }

    private static readonly (float Z0, float Z1) SideDoorSpan = (-7.9f, -7.0f);
    private const float SideDoorHeight = 2.05f;

    /// <summary>A plank door in the left wall, out to the yard</summary>
    private static Mesh SideDoor()
    {
        var mesh = new Mesh();
        var (z0, z1) = SideDoorSpan;
        const float x = LeftWall - 0.02f;
        mesh.Quad(V(x, 0f, z0), V(x, 0f, z1), V(x, SideDoorHeight, z1), V(x, SideDoorHeight, z0), -Vec3.UnitX,
            p => new Vec2((p.Z - z0) / (z1 - z0), (SideDoorHeight - p.Y) / SideDoorHeight));
        return mesh;
    }

    /// <summary>The concrete outside the doors, the garage's width and two car lengths out; past it, the dark</summary>
    private static Mesh Driveway()
    {
        var mesh = new Mesh();
        const float y = -0.02f;
        const float far = BackWall - 10f;
        mesh.Quad(V(RightWall, y, far), V(LeftWall, y, far), V(LeftWall, y, BackWall - 0.2f), V(RightWall, y, BackWall - 0.2f), Vec3.UnitY,
            p => new Vec2(p.X / DriveTile, p.Z / DriveTile));
        return mesh;
    }

    /// <summary>The pegboard on the front wall, over the workbench</summary>
    private static Mesh Pegboard()
    {
        var mesh = new Mesh();
        const float z = FrontWall - 0.03f;
        const float x0 = WorkshopDoorX + 1.3f;
        const float x1 = WorkshopDoorX - 1.3f;
        mesh.Box(V(x1, 0.95f, z - 0.02f), V(x0, 2.15f, z + 0.02f), 1f);
        return mesh;
    }

    /// <summary>A window: the wall it is in, where it starts and ends along the wall, its sill and head</summary>
    private sealed record WindowAt(WallSide Wall, float U0, float U1, float Sill, float Head);

    private static IEnumerable<WindowAt> WindowPlaces()
    {
        var sides = Openings();
        yield return new(sides[2], -6.2f, -4.8f, 1.4f, 2.5f);
        yield return new(sides[2], -0.7f, 0.7f, 1.4f, 2.5f);
        yield return new(sides[2], 2.6f, 4.0f, 1.4f, 2.5f);
        yield return new(sides[2], 6.6f, 8.0f, 1.4f, 2.5f);
        yield return new(sides[1], -4.4f, -3.0f, 1.4f, 2.5f);
    }

    /// <summary>The windows: the glass a hair off the wall, lit from outside, and the steel frame round it with a sill</summary>
    private static (Mesh Glass, Mesh Frames) Windows()
    {
        var glass = new Mesh();
        var frames = new Mesh();
        foreach (var w in WindowPlaces())
        {
            var off = w.Wall.Normal * 0.02f;
            var a = w.Wall.At(w.U0, w.Sill) + off;
            var b = w.Wall.At(w.U1, w.Sill) + off;
            var c = w.Wall.At(w.U1, w.Head) + off;
            var d = w.Wall.At(w.U0, w.Head) + off;

            // The picture reads left to right as seen from inside: the viewer's right is the wall's direction or its reverse
            var right = Vec3.Cross(-w.Wall.Normal, Vec3.UnitY);
            var along = Vec3.Normalize(b - a);
            var flipped = Vec3.Dot(right, along) < 0f;
            glass.Quad(a, b, c, d, w.Wall.Normal, p =>
            {
                var t = Vec3.Dot(p - a, along) / (w.U1 - w.U0);
                return new Vec2(flipped ? 1f - t : t, (w.Head - p.Y) / (w.Head - w.Sill));
            });

            var frameOff = w.Wall.Normal * 0.04f;
            foreach (var (from, to) in new[] { (a, b), (b, c), (c, d), (d, a) })
                frames.Bar(from + frameOff, to + frameOff, 0.07f, 1f);

            var sillOut = w.Wall.Normal * 0.14f;
            frames.Bar(a + sillOut * 0.5f - Vec3.UnitY * 0.03f, b + sillOut * 0.5f - Vec3.UnitY * 0.03f, 0.12f, 1f);
        }

        return (glass, frames);
    }

    /// <summary>A sign nailed to a wall: which wall, its middle along it, its bottom, its width and height</summary>
    private static readonly Dictionary<string, (int Wall, float U, float Bottom, float Width, float Height)> Posters = new()
    {
        ["poster_oil"] = (2, -2.6f, 1.55f, 0.6f, 0.8f),
        ["poster_plugs"] = (1, 0.6f, 1.75f, 0.8f, 0.6f),
        ["poster_tyres"] = (3, 4.8f, 1.55f, 0.6f, 0.8f),
        ["calendar"] = (3, -5.7f, 1.5f, 0.4f, 0.6f)
    };

    private static Mesh Poster(string name)
    {
        var (wallIndex, u, bottom, width, height) = Posters[name];
        var wall = Openings()[wallIndex];
        var mesh = new Mesh();
        var off = wall.Normal * 0.015f;

        // Seen from inside, the viewer's right along the wall
        var right = Vec3.Cross(-wall.Normal, Vec3.UnitY);
        var middle = wall.At(u, bottom) + off;
        var a = middle - right * (width / 2f);
        var b = middle + right * (width / 2f);
        mesh.Quad(a, b, b + Vec3.UnitY * height, a + Vec3.UnitY * height, wall.Normal,
            p => new Vec2(Vec3.Dot(p - a, right) / width, (bottom + height - p.Y) / height));
        return mesh;
    }

    /// <summary>A twin-tube fluorescent fitting on two chains off the tie beam, its tubes underneath</summary>
    private static (Mesh Fittings, Mesh Tubes) LightFittings()
    {
        const float half = 0.62f;
        var fittings = new Mesh();
        var tubes = new Mesh();
        foreach (var (x, z) in Fittings)
        {
            fittings.Box(V(x - half, FittingHeight, z - 0.1f), V(x + half, FittingHeight + 0.06f, z + 0.1f), 1f, bottom: true);
            foreach (var end in new[] { -half + 0.1f, half - 0.1f })
                fittings.Bar(V(x + end, FittingHeight + 0.06f, z), V(x + end, Eaves - 0.19f, z), 0.015f, 1f);
            foreach (var dz in new[] { -0.05f, 0.05f })
                tubes.Box(V(x - half + 0.04f, FittingHeight - 0.035f, z + dz - 0.014f), V(x + half - 0.04f, FittingHeight - 0.007f, z + dz + 0.014f), 1f, bottom: true);
        }

        return (fittings, tubes);
    }

    #endregion

    #region Description

    /// <summary>scene.json: the room the camera stays in, the light for each light of the day, a lamp under each fitting, the windows' glow, the marks</summary>
    private static JObject Description()
    {
        static JArray Rgb(int r, int g, int b) => new(r, g, b);
        static float Round(float v) => MathF.Round(v, 3);

        var lamps = new JArray(Fittings.Select(f => new JObject
        {
            ["x"] = Round(f.X), ["y"] = Round(FittingHeight - 0.15f), ["z"] = Round(f.Z), ["brightness"] = 0.7f, ["range"] = 9f
        }));

        JObject Light(float brightness, JArray color, float ambient, JArray up, JArray down, float lampScale) => new()
        {
            ["brightness"] = brightness, ["color"] = color, ["ambient"] = ambient, ["ambientUp"] = up, ["ambientDown"] = down,
            ["cubemapAmbient"] = 0f, ["lampScale"] = lampScale
        };

        return new JObject
        {
            ["_comment"] = new JArray(
                "The player's garage (the garage screen). workshop.kn5 and this file are written by tools/GarageScene",
                "(prepare_garage.py, then GarageSceneBuilder): change the builder, not these files. The props in it (benches, chests,",
                "cans, tyres) are not in the model: each prop's prop.json says where it stands in \"workshop\" (docs/modding.md, props).",
                "x runs across the garage (the car's left at +x), z along the car (its nose at +z, the doors behind it), y up; the car",
                "stands on the origin.",
                "room: the inside the camera stays in, however it is swung or wheeled out: the walls at minX, maxX, minZ and maxZ, the",
                "tie beams height up.",
                "sun: the key light's direction, in through the windows and the door. lights: the renderer's light for each light of",
                "the day; the lamps under the fittings are lit at every hour, lampScale how bright they are then.",
                "glow: how bright each self-lit material is at each light of the day (the windows, lit from outside).",
                "marks: for the car scripts (docs/modding.md, scripts): door is where a car taken out of the lot comes in from, out",
                "on the driveway; paths are named lists of points for a mod's scripts.",
                "Textures and skies: Poly Haven (CC0), the rest drawn by prepare_garage.py."),
            ["id"] = Id,
            ["title"] = "The garage",
            ["credit"] = PolyHavenCredit(["floor_garage_floor.jpg", "wall_concrete_block_wall_03.jpg", "dado_painted_concrete.jpg",
                "roof_weathered_brown_planks.jpg", "timber_weathered_planks.jpg", "sidedoor_rough_pine_door.jpg", "drive_concrete_pavement.jpg"]),
            ["room"] = new JObject
            {
                ["minX"] = RightWall, ["maxX"] = LeftWall, ["minZ"] = BackWall, ["maxZ"] = FrontWall, ["height"] = Eaves - 0.2f
            },
            ["sun"] = new JArray(-0.55f, 0.75f, -0.4f),
            ["lights"] = new JObject
            {
                ["day"] = Light(0.7f, Rgb(240, 236, 225), 1.0f, Rgb(165, 172, 180), Rgb(115, 110, 100), 0.8f),
                ["dusk"] = Light(0.35f, Rgb(255, 190, 150), 0.9f, Rgb(125, 112, 120), Rgb(90, 82, 74), 1f),
                ["night"] = Light(0.08f, Rgb(140, 160, 220), 0.6f, Rgb(70, 78, 100), Rgb(62, 56, 50), 1.15f)
            },
            ["lamps"] = lamps,
            ["glow"] = new JObject
            {
                ["day"] = new JObject { ["gr_window"] = 1.1f },
                ["dusk"] = new JObject { ["gr_window"] = 0.5f },
                ["night"] = new JObject { ["gr_window"] = 0.06f }
            },
            ["marks"] = new JObject { ["door"] = new JObject { ["behind"] = 13f } }
        };
    }

    #endregion

    #region Writing

    // The key light comes in from outside, through walls the renderer does not stop it at: nothing of the building may
    // stand in its way, and Kn5SceneWriter.AddMesh makes every mesh shadowless
    private static void AddMesh(IKn5 kn5, string name, SceneMesh mesh, uint material) =>
        Kn5SceneWriter.AddMesh(kn5, name, mesh.Vertices, mesh.Indices, material);

    #endregion
}
