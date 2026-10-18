using AcTools.Numerics;
using static StreetCorsa.Tools.SceneMesh;

namespace StreetCorsa.Tools;

/// <summary>
/// The box a shop is: the room the part shop and the machine shop (tools/ShopScene, tools/MachineShopScene) are built
/// in, and what they have in common inside it: the floor, the four walls with the front one round the door and the two
/// windows, the ceiling, the shut door, an engine stand, the fluorescent tubes with their fittings, the sign on the back
/// wall, the window panes and the rubber skirting along the walls. x runs across the room, z from the back wall (-z) to
/// the door (+z), y up; the middle of the floor is the origin. Each builder puts its own counters and fittings round
/// these, in the same order as before they were shared, so the shipped scenes come out byte for byte.
/// </summary>
/// <param name="sill">How high the windows' sills are: the front wall's panels under them stop there</param>
/// <param name="tubes">Where the fluorescent tubes hang, across and along the room</param>
/// <param name="flip">Every face wound the other way round (the builders' <c>--flip</c>)</param>
internal sealed class RoomBox(float halfWidth, float halfDepth, float height, float sill, IReadOnlyList<(float X, float Z)> tubes, bool flip)
{
    // How big a picture is on the surface, in metres
    public const float FloorTile = 2f;
    public const float WallTile = 2.4f;
    public const float WoodTile = 1.2f;
    public const float SteelTile = 1f;

    // The door in the middle of the front wall, the windows either side of it, and the top they share
    private const float DoorHalf = 0.6f;
    private const float DoorHeight = 2.2f;
    private const float WindowIn = 1.6f;
    private const float WindowOut = 5f;
    private const float WindowTop = 2.5f;

    public float HalfWidth => halfWidth;
    public float HalfDepth => halfDepth;
    public float Height => height;

    /// <summary>An empty mesh wound as this room's are</summary>
    public SceneMesh Mesh() => new(flip);

    public SceneMesh Floor()
    {
        var mesh = Mesh();
        mesh.Quad(V(-halfWidth, 0f, -halfDepth), V(halfWidth, 0f, -halfDepth), V(halfWidth, 0f, halfDepth), V(-halfWidth, 0f, halfDepth),
            Vec3.UnitY, p => new Vec2(p.X / FloorTile, p.Z / FloorTile));
        return mesh;
    }

    /// <summary>The four walls, facing in; the front one round the door and the two windows</summary>
    public SceneMesh Walls()
    {
        var mesh = Mesh();
        Vec2 Xy(Vec3 p) => new(p.X / WallTile, -p.Y / WallTile);
        Vec2 Zy(Vec3 p) => new(p.Z / WallTile, -p.Y / WallTile);

        mesh.Quad(V(-halfWidth, 0f, -halfDepth), V(halfWidth, 0f, -halfDepth), V(halfWidth, height, -halfDepth), V(-halfWidth, height, -halfDepth), Vec3.UnitZ, Xy);
        foreach (var x in new[] { -halfWidth, halfWidth })
            mesh.Quad(V(x, 0f, -halfDepth), V(x, 0f, halfDepth), V(x, height, halfDepth), V(x, height, -halfDepth), V(-MathF.Sign(x), 0f, 0f), Zy);

        // The front: under and over the windows, between them and the door, over the door
        var z = halfDepth;
        var normal = -Vec3.UnitZ;
        void Panel(float x0, float x1, float y0, float y1) =>
            mesh.Quad(V(x0, y0, z), V(x1, y0, z), V(x1, y1, z), V(x0, y1, z), normal, Xy);
        Panel(-halfWidth, halfWidth, WindowTop, height);
        Panel(-halfWidth, -WindowOut, 0f, WindowTop);
        Panel(WindowOut, halfWidth, 0f, WindowTop);
        Panel(-WindowOut, -WindowIn, 0f, sill);
        Panel(WindowIn, WindowOut, 0f, sill);
        Panel(-WindowIn, -DoorHalf, 0f, WindowTop);
        Panel(DoorHalf, WindowIn, 0f, WindowTop);
        Panel(-DoorHalf, DoorHalf, DoorHeight, WindowTop);
        return mesh;
    }

    public SceneMesh Ceiling()
    {
        var mesh = Mesh();
        mesh.Quad(V(-halfWidth, height, -halfDepth), V(halfWidth, height, -halfDepth), V(halfWidth, height, halfDepth), V(-halfWidth, height, halfDepth),
            -Vec3.UnitY, p => new Vec2(p.X / WallTile, p.Z / WallTile));
        return mesh;
    }

    /// <summary>The door, shut, a little inside the front wall: into the woodwork</summary>
    public void Door(SceneMesh mesh)
    {
        var z = halfDepth - 0.02f;
        mesh.Quad(V(-DoorHalf, 0f, z), V(DoorHalf, 0f, z), V(DoorHalf, DoorHeight, z), V(-DoorHalf, DoorHeight, z),
            -Vec3.UnitZ, p => new Vec2(p.X / WoodTile, -p.Y / WoodTile));
    }

    /// <summary>An engine stand at <paramref name="s"/>: a cross of legs on the floor, a post, the head the engine bolts to; into the steelwork</summary>
    public void Stand(SceneMesh mesh, Vec3 s)
    {
        mesh.Bar(V(s.X - 0.5f, 0.04f, s.Z), V(s.X + 0.5f, 0.04f, s.Z), 0.07f, SteelTile);
        mesh.Bar(V(s.X, 0.04f, s.Z - 0.45f), V(s.X, 0.04f, s.Z + 0.45f), 0.07f, SteelTile);
        mesh.Bar(V(s.X, 0f, s.Z), V(s.X, 0.42f, s.Z), 0.08f, SteelTile);
        mesh.Box(V(s.X - 0.2f, 0.42f, s.Z - 0.2f), V(s.X + 0.2f, 0.45f, s.Z + 0.2f), SteelTile, bottom: true);
    }

    /// <summary>The fittings the tubes hang in, against the ceiling; into the steelwork</summary>
    public void TubeFittings(SceneMesh mesh)
    {
        foreach (var (x, z) in tubes)
            mesh.Box(V(x - 0.66f, height - 0.06f, z - 0.1f), V(x + 0.66f, height, z + 0.1f), SteelTile, bottom: false);
    }

    /// <summary>The board on the back wall between these corners, its picture stretched over it</summary>
    public SceneMesh Sign(float x0, float x1, float y0, float y1)
    {
        var z = -halfDepth + 0.04f;
        var mesh = Mesh();
        mesh.Quad(V(x0, y0, z), V(x1, y0, z), V(x1, y1, z), V(x0, y1, z), Vec3.UnitZ, p => new Vec2((p.X - x0) / (x1 - x0), (y1 - p.Y) / (y1 - y0)));
        return mesh;
    }

    /// <summary>The fluorescent tubes, two to a fitting</summary>
    public SceneMesh TubeLights()
    {
        var mesh = Mesh();
        foreach (var (x, z) in tubes)
        {
            foreach (var dz in new[] { -0.045f, 0.045f })
                mesh.Box(V(x - 0.6f, height - 0.1f, z + dz - 0.018f), V(x + 0.6f, height - 0.064f, z + dz + 0.018f), 1f, bottom: true);
        }

        return mesh;
    }

    /// <summary>The front windows, seen from inside: the bright day beyond them</summary>
    public SceneMesh Windows()
    {
        var mesh = Mesh();
        var z = halfDepth - 0.01f;
        foreach (var (x0, x1) in new[] { (-WindowOut, -WindowIn), (WindowIn, WindowOut) })
            mesh.Quad(V(x0, sill, z), V(x1, sill, z), V(x1, WindowTop, z), V(x0, WindowTop, z), -Vec3.UnitZ, p => new Vec2(0.5f, 0.5f));
        return mesh;
    }

    /// <summary>Black rubber along the foot of the back and side walls; the builder adds its counter's</summary>
    public SceneMesh Skirting()
    {
        var mesh = Mesh();
        mesh.Box(V(-halfWidth, 0f, -halfDepth), V(halfWidth, 0.1f, -halfDepth + 0.015f), 0.5f);
        mesh.Box(V(-halfWidth, 0f, -halfDepth), V(-halfWidth + 0.015f, 0.1f, halfDepth), 0.5f);
        mesh.Box(V(halfWidth - 0.015f, 0f, -halfDepth), V(halfWidth, 0.1f, halfDepth), 0.5f);
        return mesh;
    }
}
