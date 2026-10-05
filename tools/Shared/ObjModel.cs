using System.Globalization;
using System.Text.Json;
using AcTools.Kn5File;
using AcTools.Numerics;
using static StreetCorsa.Tools.Kn5SceneWriter;

namespace StreetCorsa.Tools;

/// <summary>
/// A model out of what a prepare script left (an OBJ, Y up, triangles, and its pictures, tools/Props/prepare_prop.py)
/// into a KN5: its triangles by material, AC's ksPerPixelMultiMap materials, and meshes as long as 16-bit indices reach.
/// Linked into the builders that read one (the props, the diner's street).
/// </summary>
internal static class ObjModel
{
    /// <summary>
    /// A mesh a material, the OBJ's groups of one material together; as long as 16-bit indices reach, in pieces beyond.
    /// A group of a material <paramref name="materialOf"/> leaves out (glass) is dropped unless <paramref name="fallback"/> is given
    /// </summary>
    public static void AddMeshes(IKn5 kn5, Dictionary<string, (List<Kn5Node.Vertex> Vertices, List<int> Indices)> groups,
        Dictionary<string, uint> materialOf, uint? fallback, bool castShadows)
    {
        var kept = fallback == null ? groups.Where(g => materialOf.ContainsKey(g.Key)) : groups;
        foreach (var byMaterial in kept.GroupBy(g => materialOf.TryGetValue(g.Key, out var m) ? m : fallback!.Value))
        {
            var (vertices, indices) = Merge(byMaterial.Select(g => g.Value));
            var mesh = kn5.Materials.Keys.ElementAt((int)byMaterial.Key).ToUpperInvariant();
            foreach (var (piece, part) in Split(vertices, indices).Select((p, i) => (p, i)))
                AddMesh(kn5, part == 0 ? mesh : $"{mesh}_{part}", piece.Vertices, piece.Indices, byMaterial.Key,
                    castShadows: castShadows);
        }
    }

    /// <summary>
    /// ksPerPixelMultiMap: the picture, its normal map (when it has one), and the maps (red specular, green gloss, blue
    /// reflection), lit as a car's plastics and paint are in AC (a scene more by the sun: <paramref name="ksDiffuse"/> up,
    /// <paramref name="ksAmbient"/> down); <paramref name="emissive"/> for a part that glows
    /// </summary>
    public static uint AddMultiMapMaterial(IKn5 kn5, string name, string diffuse, string? normal, string maps, float emissive,
        bool alphaTest = false, float ksAmbient = 0.35f, float ksDiffuse = 0.45f)
    {
        var textures = new List<Kn5Material.TextureMapping> { new() { Name = "txDiffuse", Texture = diffuse, Slot = 0 } };
        if (normal != null) textures.Add(new Kn5Material.TextureMapping { Name = "txNormal", Texture = normal, Slot = 1 });
        textures.Add(new Kn5Material.TextureMapping { Name = "txMaps", Texture = maps, Slot = 2 });

        kn5.Materials[name] = new Kn5Material
        {
            Name = name,
            ShaderName = "ksPerPixelMultiMap",
            ShaderProperties =
            [
                Property("ksAmbient", ksAmbient),
                Property("ksDiffuse", ksDiffuse),
                Property("ksSpecular", 0.4f),
                Property("ksSpecularEXP", 60f),
                new Kn5Material.ShaderProperty { Name = "ksEmissive", ValueC = new Vec3(emissive, emissive, emissive) },
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
            TextureMappings = textures.ToArray(),

            // Cut out by the picture's alpha (a road marking), drawn whole or not at all
            AlphaTested = alphaTest
        };
        return MaterialIndex(kn5, name);
    }

    private static Kn5Material.ShaderProperty Property(string name, float value) => new() { Name = name, ValueA = value };

    private sealed record MaterialJson(string Name, string Diffuse, string? Normal, string Maps, float Emissive, bool AlphaTest);

    private sealed record ManifestJson(List<MaterialJson> Materials, Dictionary<string, string?> Aliases);

    /// <summary>
    /// materials.json's materials, and each of the OBJ's material names to the one it is; a name to none (glass the
    /// prepare script left out) is not in the list, and its triangles are dropped
    /// </summary>
    public static Dictionary<string, uint> AddMaterials(IKn5 kn5, string prepared, string manifest, float ksAmbient = 0.35f, float ksDiffuse = 0.45f)
    {
        var json = JsonSerializer.Deserialize<ManifestJson>(File.ReadAllText(manifest), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidDataException("materials.json is empty");
        var index = new Dictionary<string, uint>();
        foreach (var m in json.Materials)
        {
            index[m.Name] = AddMultiMapMaterial(kn5, m.Name, AddTexture(kn5, Path.Combine(prepared, m.Diffuse)),
                m.Normal == null ? null : AddTexture(kn5, Path.Combine(prepared, m.Normal)), AddTexture(kn5, Path.Combine(prepared, m.Maps)), m.Emissive, m.AlphaTest, ksAmbient, ksDiffuse);
        }

        return json.Aliases.Where(a => a.Value != null).ToDictionary(a => a.Key, a => index[a.Value!]);
    }

    /// <summary>Several groups' triangles as one list</summary>
    public static (List<Kn5Node.Vertex> Vertices, List<int> Indices) Merge(IEnumerable<(List<Kn5Node.Vertex> Vertices, List<int> Indices)> groups)
    {
        var vertices = new List<Kn5Node.Vertex>();
        var indices = new List<int>();
        foreach (var (v, i) in groups)
        {
            var offset = vertices.Count;
            vertices.AddRange(v);
            indices.AddRange(i.Select(x => x + offset));
        }

        return (vertices, indices);
    }

    /// <summary>
    /// The OBJ's triangles as KN5 vertices, by the material they are of: scaled to metres, turned about the vertical by
    /// <paramref name="turn"/> degrees, stood on its base with its footprint's middle at the origin (unless not <paramref name="recentre"/>: a scene keeps its own), tangents worked out
    /// from the UVs for the normal map. AC's space is the OBJ's (Y up, right-handed: a car faces +Z with its left
    /// towards +X), so the model goes in as it is; <paramref name="mirror"/> is for one that comes out back to front (its
    /// lettering reads backwards), and turns the winding round with it.
    /// </summary>
    public static Dictionary<string, (List<Kn5Node.Vertex> Vertices, List<int> Indices)> ReadObj(string path, float scale, float turn, bool mirror, bool recentre = true)
    {
        var positions = new List<Vec3>();
        var uvs = new List<Vec2>();
        var normals = new List<Vec3>();
        var corners = new List<(int P, int T, int N, string Material)>();
        var material = "";

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
                case "usemtl":
                    material = raw.Trim()["usemtl".Length..].Trim();
                    break;
                case "f" when parts.Length == 4:
                    foreach (var corner in parts.Skip(1))
                    {
                        var ids = corner.Split('/');
                        corners.Add((int.Parse(ids[0]) - 1, ids.Length > 1 && ids[1].Length > 0 ? int.Parse(ids[1]) - 1 : -1,
                            ids.Length > 2 ? int.Parse(ids[2]) - 1 : -1, material));
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
        if (recentre)
        {
            var minY = placed.Min(p => p.Y);
            var midX = (placed.Min(p => p.X) + placed.Max(p => p.X)) / 2f;
            var midZ = (placed.Min(p => p.Z) + placed.Max(p => p.Z)) / 2f;
            placed = placed.Select(p => new Vec3(p.X - midX, p.Y - minY, p.Z - midZ)).ToList();
        }

        var groups = new Dictionary<string, (List<Kn5Node.Vertex> Vertices, List<int> Indices)>();
        foreach (var byMaterial in corners.GroupBy(c => c.Material))
        {
            var vertices = new List<Kn5Node.Vertex>();
            var indices = new List<int>();
            var known = new Dictionary<(int, int, int), int>();
            foreach (var (p, t, n, _) in byMaterial)
            {
                if (!known.TryGetValue((p, t, n), out var index))
                {
                    index = vertices.Count;
                    known[(p, t, n)] = index;
                    var normal = n >= 0 ? Turn(normals[n]) : new Vec3(0f, 1f, 0f);

                    // OBJ's V runs up, a DirectX texture's down
                    var uv = t >= 0 ? new Vec2(uvs[t].X, 1f - uvs[t].Y) : new Vec2(0f, 0f);
                    vertices.Add(new Kn5Node.Vertex(placed[p], normal, uv, new Vec3(1f, 0f, 0f)));
                }

                indices.Add(index);
            }

            // Mirrored: every triangle the other way round, so it still faces out
            if (mirror)
            {
                for (var i = 0; i + 2 < indices.Count; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
            }

            groups[byMaterial.Key] = (WithTangents(vertices, indices), indices);
        }

        return groups;
    }

    private static float F(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Each vertex's tangent: the way its texture's U runs across its triangles, square to its normal</summary>
    public static List<Kn5Node.Vertex> WithTangents(List<Kn5Node.Vertex> vertices, List<int> indices)
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
    public static IEnumerable<(List<Kn5Node.Vertex> Vertices, List<int> Indices)> Split(List<Kn5Node.Vertex> vertices, List<int> indices)
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
