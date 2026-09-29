using System.Globalization;
using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Tools;
using static StreetCorsa.Tools.Kn5SceneWriter;

namespace PropBuilder;

/// <summary>
/// A prop for a scene (the garage's radio) as a KN5, out of what a prepare script left (tools/Props/prepare_*.py): an
/// OBJ (Y up, facing -Z, triangles) and its three pictures, &lt;name&gt;_diffuse.jpg, &lt;name&gt;_normal.png and
/// &lt;name&gt;_maps.png, drawn with AC's ksPerPixelMultiMap. The model comes out in metres, stood on its base: its
/// origin the middle of its footprint, on the floor. Where it stands in a scene is the game's to say (prop.json).
///
///   PropBuilder &lt;prepared folder&gt; &lt;name&gt; &lt;out folder&gt; --scale 0.112 [--turn degrees] [--mirror]
///
/// The garage's radio: PropBuilder &lt;prepared&gt; radio "Street Corsa/Content/streetcorsa/props/radio" --scale 0.112
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith("--")).ToList();
        if (positional.Count < 3 || !TryOption(args, "--scale", out var scale))
        {
            Console.Error.WriteLine("PropBuilder <prepared folder> <name> <out folder> --scale <metres a unit> [--turn <degrees>] [--mirror]");
            return 1;
        }

        var turn = TryOption(args, "--turn", out var degrees) ? degrees : 0f;
        var mirror = args.Contains("--mirror");
        var (prepared, name, output) = (positional[0], positional[1], positional[2]);

        var (vertices, indices) = ReadObj(Path.Combine(prepared, name + ".obj"), scale, turn, mirror);
        Console.WriteLine($"{name}: {vertices.Count} vertices, {indices.Count / 3} triangles");

        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = name;
        var diffuse = AddTexture(kn5, Path.Combine(prepared, name + "_diffuse.jpg"));
        var normal = AddTexture(kn5, Path.Combine(prepared, name + "_normal.png"));
        var maps = AddTexture(kn5, Path.Combine(prepared, name + "_maps.png"));
        var material = AddMultiMapMaterial(kn5, name, diffuse, normal, maps);

        // One mesh as long as 16-bit indices reach, in pieces beyond
        foreach (var (piece, part) in Split(vertices, indices).Select((p, i) => (p, i)))
            AddMesh(kn5, part == 0 ? name.ToUpperInvariant() : $"{name.ToUpperInvariant()}_{part}", piece.Vertices, piece.Indices, material, castShadows: true);

        var path = Path.Combine(output, name + ".kn5");
        Save(kn5, path);
        Console.WriteLine($"{path}: {new FileInfo(path).Length / 1024} KB");
        return 0;
    }

    private static bool TryOption(string[] args, string name, out float value)
    {
        value = 0f;
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length && float.TryParse(args[at + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// ksPerPixelMultiMap: the picture, its normal map, and the maps (red specular, green gloss, blue reflection), lit
    /// as a car's plastics and paint are in AC
    /// </summary>
    private static uint AddMultiMapMaterial(IKn5 kn5, string name, string diffuse, string normal, string maps)
    {
        kn5.Materials[name] = new Kn5Material
        {
            Name = name,
            ShaderName = "ksPerPixelMultiMap",
            ShaderProperties =
            [
                Property("ksAmbient", 0.35f),
                Property("ksDiffuse", 0.45f),
                Property("ksSpecular", 0.4f),
                Property("ksSpecularEXP", 60f),
                new Kn5Material.ShaderProperty { Name = "ksEmissive", ValueC = new Vec3(0f, 0f, 0f) },
                Property("ksAlphaRef", 0f),
                Property("fresnelC", 0.04f),
                Property("fresnelEXP", 3f),
                Property("fresnelMaxLevel", 0.25f),
                Property("isAdditive", 0f),
                Property("useDetail", 0f),
                Property("detailUVMultiplier", 1f),
                Property("sunSpecular", 1.5f),
                Property("sunSpecularEXP", 400f),
                Property("nmObjectSpace", 0f)
            ],
            TextureMappings =
            [
                new Kn5Material.TextureMapping { Name = "txDiffuse", Texture = diffuse, Slot = 0 },
                new Kn5Material.TextureMapping { Name = "txNormal", Texture = normal, Slot = 1 },
                new Kn5Material.TextureMapping { Name = "txMaps", Texture = maps, Slot = 2 }
            ]
        };
        return MaterialIndex(kn5, name);
    }

    private static Kn5Material.ShaderProperty Property(string name, float value) => new() { Name = name, ValueA = value };

    /// <summary>
    /// The OBJ's triangles as KN5 vertices: scaled to metres, turned about the vertical by <paramref name="turn"/>
    /// degrees, stood on its base with its footprint's middle at the origin, tangents worked out from the UVs for the
    /// normal map. AC's space is the OBJ's (Y up, right-handed: a car faces +Z with its left towards +X), so the model goes
    /// in as it is; <paramref name="mirror"/> is for one that comes out back to front (its lettering reads backwards),
    /// and turns the winding round with it.
    /// </summary>
    private static (List<Kn5Node.Vertex> Vertices, List<int> Indices) ReadObj(string path, float scale, float turn, bool mirror)
    {
        var positions = new List<Vec3>();
        var uvs = new List<Vec2>();
        var normals = new List<Vec3>();
        var corners = new List<(int P, int T, int N)>();

        foreach (var raw in File.ReadLines(path))
        {
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            switch (parts[0])
            {
                case "v":
                    positions.Add(new Vec3(F(parts[1]), F(parts[2]), F(parts[3])));
                    break;
                case "vt":
                    uvs.Add(new Vec2(F(parts[1]), F(parts[2])));
                    break;
                case "vn":
                    normals.Add(new Vec3(F(parts[1]), F(parts[2]), F(parts[3])));
                    break;
                case "f" when parts.Length == 4:
                    foreach (var corner in parts.Skip(1))
                    {
                        var ids = corner.Split('/');
                        corners.Add((int.Parse(ids[0]) - 1, ids.Length > 1 && ids[1].Length > 0 ? int.Parse(ids[1]) - 1 : -1,
                            ids.Length > 2 ? int.Parse(ids[2]) - 1 : -1));
                    }
                    break;
                case "f":
                    throw new InvalidDataException("the OBJ is not all triangles: export it triangulated");
            }
        }

        var (sin, cos) = MathF.SinCos(turn * MathF.PI / 180f);
        var side = mirror ? -1f : 1f;
        Vec3 Place(Vec3 p) => new(side * (p.X * cos + p.Z * sin) * scale, p.Y * scale, (-p.X * sin + p.Z * cos) * scale);
        Vec3 Turn(Vec3 n) => new(side * (n.X * cos + n.Z * sin), n.Y, -n.X * sin + n.Z * cos);

        var placed = positions.Select(Place).ToList();
        var minY = placed.Min(p => p.Y);
        var midX = (placed.Min(p => p.X) + placed.Max(p => p.X)) / 2f;
        var midZ = (placed.Min(p => p.Z) + placed.Max(p => p.Z)) / 2f;
        placed = placed.Select(p => new Vec3(p.X - midX, p.Y - minY, p.Z - midZ)).ToList();

        var vertices = new List<Kn5Node.Vertex>();
        var indices = new List<int>();
        var known = new Dictionary<(int, int, int), int>();
        foreach (var corner in corners)
        {
            if (!known.TryGetValue(corner, out var index))
            {
                index = vertices.Count;
                known[corner] = index;
                var normal = corner.N >= 0 ? Turn(normals[corner.N]) : new Vec3(0f, 1f, 0f);

                // OBJ's V runs up, a DirectX texture's down
                var uv = corner.T >= 0 ? new Vec2(uvs[corner.T].X, 1f - uvs[corner.T].Y) : new Vec2(0f, 0f);
                vertices.Add(new Kn5Node.Vertex(placed[corner.P], normal, uv, new Vec3(1f, 0f, 0f)));
            }

            indices.Add(index);
        }

        // Mirrored: every triangle the other way round, so it still faces out
        if (mirror)
        {
            for (var i = 0; i + 2 < indices.Count; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        }

        return (WithTangents(vertices, indices), indices);
    }

    private static float F(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Each vertex's tangent: the way its texture's U runs across its triangles, square to its normal</summary>
    private static List<Kn5Node.Vertex> WithTangents(List<Kn5Node.Vertex> vertices, List<int> indices)
    {
        var sums = new Vec3[vertices.Count];
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var (a, b, c) = (vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
            var e1 = Sub(b.Position, a.Position);
            var e2 = Sub(c.Position, a.Position);
            var (du1, dv1) = (b.Tex.X - a.Tex.X, b.Tex.Y - a.Tex.Y);
            var (du2, dv2) = (c.Tex.X - a.Tex.X, c.Tex.Y - a.Tex.Y);
            var d = du1 * dv2 - du2 * dv1;
            if (MathF.Abs(d) < 1e-12f) continue;

            var r = 1f / d;
            var t = new Vec3((e1.X * dv2 - e2.X * dv1) * r, (e1.Y * dv2 - e2.Y * dv1) * r, (e1.Z * dv2 - e2.Z * dv1) * r);
            foreach (var index in new[] { indices[i], indices[i + 1], indices[i + 2] }) sums[index] = Add(sums[index], t);
        }

        return vertices.Select((v, i) =>
        {
            var n = v.Normal;
            var t = sums[i];
            var dot = t.X * n.X + t.Y * n.Y + t.Z * n.Z;
            t = new Vec3(t.X - n.X * dot, t.Y - n.Y * dot, t.Z - n.Z * dot);
            var length = MathF.Sqrt(t.X * t.X + t.Y * t.Y + t.Z * t.Z);
            t = length > 1e-6f ? new Vec3(t.X / length, t.Y / length, t.Z / length) : new Vec3(1f, 0f, 0f);
            return new Kn5Node.Vertex(v.Position, v.Normal, v.Tex, t);
        }).ToList();

        static Vec3 Sub(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        static Vec3 Add(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    /// <summary>The triangles in pieces of at most <see cref="Kn5SceneWriter.MaxVerticesPerMesh"/> vertices each</summary>
    private static IEnumerable<(List<Kn5Node.Vertex> Vertices, List<int> Indices)> Split(List<Kn5Node.Vertex> vertices, List<int> indices)
    {
        if (vertices.Count <= MaxVerticesPerMesh)
        {
            yield return (vertices, indices);
            yield break;
        }

        var pieceVertices = new List<Kn5Node.Vertex>();
        var pieceIndices = new List<int>();
        var map = new Dictionary<int, int>();
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            if (pieceVertices.Count + 3 > MaxVerticesPerMesh)
            {
                yield return (pieceVertices, pieceIndices);
                (pieceVertices, pieceIndices, map) = (new(), new(), new());
            }

            for (var k = 0; k < 3; k++)
            {
                var source = indices[i + k];
                if (!map.TryGetValue(source, out var local))
                {
                    local = pieceVertices.Count;
                    map[source] = local;
                    pieceVertices.Add(vertices[source]);
                }
                pieceIndices.Add(local);
            }
        }

        yield return (pieceVertices, pieceIndices);
    }
}
