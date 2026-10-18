using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Tools;

namespace RunningGearParts;

/// <summary>
/// The shapes running gear is made of, on top of the scene builders' flat faces: a lathe (a profile turned about an
/// axis, its cross-section round or of a shape of its own: a diff cover), a tube, a rod swept along a path (a sway bar's
/// bends), a box turned any way (a gear's tooth) and a hex head. Every face looks out; the picture is laid on by metres.
/// </summary>
internal sealed class GearMesh : SceneMesh
{
    /// <summary>Metres a picture's width covers</summary>
    private const float Tile = 0.25f;

    /// <summary>
    /// A profile turned about an axis: each point is (how far along the axis, how far out), walked so the outside is on the
    /// left (along the axis on the outside, back on a bore's). Each step of the profile is a band of its own, sharp at the
    /// profile's corners and smooth round the axis. <paramref name="shape"/> scales the radius by the angle (none: round);
    /// <paramref name="phase"/> turns the start of the circle.
    /// </summary>
    public GearMesh Lathe(Vec3 origin, Vec3 axis, IReadOnlyList<(float At, float R)> profile, int segments, Func<float, float>? shape = null, float phase = 0f)
    {
        axis = Vec3.Normalize(axis);
        var (u, v) = Across(axis);
        Vec3 Radial(float angle) => u * MathF.Cos(angle) + v * MathF.Sin(angle);

        var along = 0f;
        for (var i = 0; i + 1 < profile.Count; i++)
        {
            var (a0, r0) = profile[i];
            var (a1, r1) = profile[i + 1];
            var (dA, dR) = (a1 - a0, r1 - r0);
            var length = MathF.Sqrt(dA * dA + dR * dR);
            if (length < 1e-7f) continue;

            var first = Vertices.Count;
            for (var k = 0; k <= segments; k++)
            {
                var angle = phase + k * MathF.Tau / segments;
                var scale = shape?.Invoke(angle) ?? 1f;
                var radial = Radial(angle);
                var normal = Vec3.Normalize(radial * (dA / length) - axis * (dR / length));
                var tangent = Vec3.Cross(axis, radial);
                var around = k * MathF.Tau / segments * MathF.Max(r0, r1) / Tile;
                Vertices.Add(new Kn5Node.Vertex(origin + axis * a0 + radial * (r0 * scale), normal, new Vec2(around, along / Tile), tangent));
                Vertices.Add(new Kn5Node.Vertex(origin + axis * a1 + radial * (r1 * scale), normal, new Vec2(around, (along + length) / Tile), tangent));
            }

            for (var k = 0; k < segments; k++)
            {
                var a = first + 2 * k;
                var normal = Vertices[a].Normal + Vertices[a + 2].Normal;
                // A band that closes to a point (a cap) has two corners on the axis: one triangle of it is enough
                if (r0 > 1e-6f) Facing(a, a + 2, a + 1, normal);
                if (r1 > 1e-6f) Facing(a + 1, a + 2, a + 3, normal);
            }

            along += length;
        }

        return this;
    }

    /// <summary>A round bar from one point to another, its ends closed</summary>
    public GearMesh Tube(Vec3 from, Vec3 to, float radius, int segments = 16, bool caps = true)
    {
        var length = (to - from).Length();
        var profile = caps
            ? new[] { (0f, 0f), (0f, radius), (length, radius), (length, 0f) }
            : new[] { (0f, radius), (length, radius) };
        return Lathe(from, to - from, profile, segments);
    }

    /// <summary>A hex head (a bolt's, a nut's, a torsion bar's end): six flat sides about an axis</summary>
    public GearMesh Hex(Vec3 from, Vec3 to, float acrossCorners) =>
        Lathe(from, to - from, [(0f, 0f), (0f, acrossCorners), ((to - from).Length(), acrossCorners), ((to - from).Length(), 0f)], 6);

    /// <summary>
    /// A rod of round section along a path, its rings square to the path and turning with it as little as they can; the
    /// ends closed
    /// </summary>
    public GearMesh Sweep(IReadOnlyList<Vec3> path, float radius, int segments = 12)
    {
        if (path.Count < 2) return this;

        var tangents = new Vec3[path.Count];
        for (var i = 0; i < path.Count; i++)
        {
            var before = i > 0 ? Vec3.Normalize(path[i] - path[i - 1]) : Vec3.Zero;
            var after = i + 1 < path.Count ? Vec3.Normalize(path[i + 1] - path[i]) : Vec3.Zero;
            tangents[i] = Vec3.Normalize(before + after);
        }

        var (u, _) = Across(tangents[0]);
        var first = Vertices.Count;
        var along = 0f;
        for (var i = 0; i < path.Count; i++)
        {
            if (i > 0)
            {
                along += (path[i] - path[i - 1]).Length();
                // The ring's first direction carried on, square to the new tangent
                u = Vec3.Normalize(u - tangents[i] * Vec3.Dot(u, tangents[i]));
            }

            var v = Vec3.Cross(tangents[i], u);
            for (var k = 0; k <= segments; k++)
            {
                var angle = k * MathF.Tau / segments;
                var radial = u * MathF.Cos(angle) + v * MathF.Sin(angle);
                Vertices.Add(new Kn5Node.Vertex(path[i] + radial * radius, radial, new Vec2(k * MathF.Tau * radius / segments / Tile, along / Tile), tangents[i]));
            }
        }

        var ring = segments + 1;
        for (var i = 0; i + 1 < path.Count; i++)
        {
            for (var k = 0; k < segments; k++)
            {
                var a = first + i * ring + k;
                var normal = Vertices[a].Normal + Vertices[a + 1].Normal;
                Facing(a, a + 1, a + ring, normal);
                Facing(a + 1, a + ring + 1, a + ring, normal);
            }
        }

        Lathe(path[0], -tangents[0], [(0f, radius), (0f, 0f)], segments);
        Lathe(path[^1], tangents[^1], [(0f, radius), (0f, 0f)], segments);
        return this;
    }

    /// <summary>A box about its middle, its sides along three square directions, half as long as <paramref name="half"/> each way</summary>
    public GearMesh Block(Vec3 centre, Vec3 x, Vec3 y, Vec3 z, Vec3 half)
    {
        x = Vec3.Normalize(x) * half.X;
        y = Vec3.Normalize(y) * half.Y;
        z = Vec3.Normalize(z) * half.Z;
        foreach (var (n, a, b) in new[] { (x, y, z), (-x, y, z), (y, x, z), (-y, x, z), (z, x, y), (-z, x, y) })
        {
            var c = centre + n;
            Quad(c - a - b, c + a - b, c + a + b, c - a + b, Vec3.Normalize(n), p => new Vec2(Vec3.Dot(p, Vec3.Normalize(a)) / Tile, Vec3.Dot(p, Vec3.Normalize(b)) / Tile));
        }

        return this;
    }

    /// <summary>An axis-aligned box between two corners, all six sides</summary>
    public GearMesh Block(Vec3 min, Vec3 max) =>
        Block((min + max) * 0.5f, Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, (max - min) * 0.5f);

    /// <summary>A path through corners, each corner rounded off by an arc of <paramref name="radius"/></summary>
    public static List<Vec3> Rounded(IReadOnlyList<Vec3> corners, float radius, int steps = 6)
    {
        var path = new List<Vec3> { corners[0] };
        for (var i = 1; i + 1 < corners.Count; i++)
        {
            var into = Vec3.Normalize(corners[i] - corners[i - 1]);
            var outOf = Vec3.Normalize(corners[i + 1] - corners[i]);
            var start = corners[i] - into * radius;
            var end = corners[i] + outOf * radius;
            for (var s = 0; s <= steps; s++)
            {
                // A quadratic curve through the corner: near enough an arc for a bend
                var t = s / (float)steps;
                path.Add(start * ((1 - t) * (1 - t)) + corners[i] * (2 * t * (1 - t)) + end * (t * t));
            }
        }

        path.Add(corners[^1]);
        return path;
    }

    /// <summary>Two directions square to an axis and to each other</summary>
    private static (Vec3 U, Vec3 V) Across(Vec3 axis)
    {
        var helper = MathF.Abs(axis.Y) > 0.9f ? Vec3.UnitX : Vec3.UnitY;
        var u = Vec3.Normalize(Vec3.Cross(helper, axis));
        return (u, Vec3.Cross(axis, u));
    }
}
