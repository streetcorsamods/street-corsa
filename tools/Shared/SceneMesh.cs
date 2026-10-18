using AcTools.Kn5File;
using AcTools.Numerics;

namespace StreetCorsa.Tools;

/// <summary>
/// A mesh a scene builder puts together out of flat faces (the paint booth, the part shop, the warehouse): quads,
/// triangles, boxes and square bars, each face wound so its front is the side its normal points to (turned round when
/// the builder is asked to <c>--flip</c>). Linked into the builders that make their own geometry; a builder with more
/// shapes (the booth's painter) adds them in a class of its own on top of this one.
/// </summary>
internal class SceneMesh(bool flip = false)
{
    public readonly List<Kn5Node.Vertex> Vertices = [];
    public readonly List<int> Indices = [];

    /// <summary>A point, short enough to write a face's corners on one line</summary>
    public static Vec3 V(float x, float y, float z) => new(x, y, z);

    /// <summary>
    /// The mesh into the model as a scene's own (<see cref="Kn5SceneWriter.AddMesh"/>: drawn and shadowless, since the
    /// scenes are lit the way their pictures were taken and nothing of them stands in the light's way)
    /// </summary>
    public static void AddMesh(IKn5 kn5, string name, SceneMesh mesh, uint material) =>
        Kn5SceneWriter.AddMesh(kn5, name, mesh.Vertices, mesh.Indices, material);

    /// <summary>Another mesh's faces added to this one's; this one</summary>
    public SceneMesh Append(SceneMesh other)
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

    /// <summary>A flat triangle seen from the side <paramref name="normal"/> points to</summary>
    public void Triangle(Vec3 a, Vec3 b, Vec3 c, Vec3 normal, Func<Vec3, Vec2> uv)
    {
        var i = Vertices.Count;
        var tangent = Vec3.Normalize(b - a);
        foreach (var p in new[] { a, b, c }) Vertices.Add(new Kn5Node.Vertex(p, normal, uv(p), tangent));
        Facing(i, i + 1, i + 2, normal);
    }

    /// <summary>A triangle of vertices already added, wound so its front is the side <paramref name="normal"/> points to</summary>
    protected void Facing(int a, int b, int c, Vec3 normal)
    {
        var pa = Vertices[a].Position;
        var cross = Vec3.Cross(Vertices[b].Position - pa, Vertices[c].Position - pa);
        var along = Vec3.Dot(cross, normal) > 0f;
        if (along != flip) Indices.AddRange([a, b, c]);
        else Indices.AddRange([a, c, b]);
    }

    /// <summary>
    /// A box between two corners, its faces looking out, the picture laid on each by the world's metres over
    /// <paramref name="tile"/>; the bottom is left out (it stands on something) unless asked for
    /// </summary>
    public void Box(Vec3 min, Vec3 max, float tile, bool bottom = false)
    {
        Vec2 Xy(Vec3 p) => new(p.X / tile, -p.Y / tile);
        Vec2 Zy(Vec3 p) => new(p.Z / tile, -p.Y / tile);
        Vec2 Xz(Vec3 p) => new(p.X / tile, p.Z / tile);

        Quad(new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z), Vec3.UnitZ, Xy);
        Quad(new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z), -Vec3.UnitZ, Xy);
        Quad(new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(max.X, max.Y, min.Z), Vec3.UnitX, Zy);
        Quad(new(min.X, min.Y, min.Z), new(min.X, min.Y, max.Z), new(min.X, max.Y, max.Z), new(min.X, max.Y, min.Z), -Vec3.UnitX, Zy);
        Quad(new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z), Vec3.UnitY, Xz);
        if (bottom)
            Quad(new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z), -Vec3.UnitY, Xz);
    }

    /// <summary>
    /// A square-section bar from <paramref name="from"/> to <paramref name="to"/>, <paramref name="size"/> across: its
    /// four sides (the ends are never seen)
    /// </summary>
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
            Quad(from + p, from + q, to + q, to + p, normal, v =>
            {
                var along = Vec3.Dot(v - from, axis);
                var across = Vec3.Dot(v - from, Vec3.Normalize(q - p)) + size;
                return new Vec2(along / tile, across / tile);
            });
        }
    }
}
