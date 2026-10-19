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
    /// Between a group's material and its object in a key <see cref="ReadObj"/> keeps an object apart by: never in a
    /// material's or an object's name
    /// </summary>
    public const char Separator = '\u001f';

    /// <summary>The material a group's key names</summary>
    public static string MaterialOf(string key) => key.Split(Separator)[0];

    /// <summary>The object a group's key names, when it is one kept apart; null for the rest</summary>
    public static string? ObjectOf(string key) => key.IndexOf(Separator) is var at && at >= 0 ? key[(at + 1)..] : null;

    /// <summary>
    /// A mesh a material, the OBJ's groups of one material together; as long as 16-bit indices reach, in pieces beyond.
    /// A group of a material <paramref name="materialOf"/> leaves out (glass) is dropped unless <paramref name="fallback"/> is given.
    /// An object <see cref="ReadObj"/> kept apart goes under a dummy node of its own name, at the bottom middle of
    /// its box, with its mesh relative to it: the game moves the node (grass leaning in the wind)
    /// </summary>
    public static void AddMeshes(IKn5 kn5, Dictionary<string, (List<Kn5Node.Vertex> Vertices, List<int> Indices)> groups,
        Dictionary<string, uint> materialOf, uint? fallback, bool castShadows)
    {
        var kept = fallback == null ? groups.Where(g => materialOf.ContainsKey(MaterialOf(g.Key))) : groups;
        foreach (var byKey in kept.GroupBy(g => (Material: materialOf.TryGetValue(MaterialOf(g.Key), out var m) ? m : fallback!.Value, Object: ObjectOf(g.Key))))
        {
            var (vertices, indices) = Merge(byKey.Select(g => g.Value));
            var material = byKey.Key.Material;
            var mesh = byKey.Key.Object ?? kn5.Materials.Keys.ElementAt((int)material).ToUpperInvariant();
            var parent = kn5.RootNode;
            if (byKey.Key.Object != null)
            {
                var pivot = new Vec3((vertices.Min(v => v.Position.X) + vertices.Max(v => v.Position.X)) / 2f, vertices.Min(v => v.Position.Y),
                    (vertices.Min(v => v.Position.Z) + vertices.Max(v => v.Position.Z)) / 2f);
                vertices = vertices.Select(v => new Kn5Node.Vertex(new Vec3(v.Position.X - pivot.X, v.Position.Y - pivot.Y, v.Position.Z - pivot.Z), v.Normal, v.Tex, v.Tangent)).ToList();
                parent = AddDummy(kn5, byKey.Key.Object, pivot);
            }

            foreach (var (piece, part) in Split(vertices, indices).Select((p, i) => (p, i)))
                AddMesh(kn5, part == 0 ? mesh : $"{mesh}_{part}", piece.Vertices, piece.Indices, material,
                    castShadows: castShadows, parent: parent);
        }
    }

    /// <summary>
    /// ksPerPixelMultiMap: the picture, its normal map (when it has one), and the maps (red specular, green gloss, blue
    /// reflection), lit as a car's plastics and paint are in AC (a scene more by the sun: <paramref name="ksDiffuse"/> up,
    /// <paramref name="ksAmbient"/> down); <paramref name="emissive"/> for a part that glows. A skin on bones (an actor's)
    /// is drawn by <paramref name="shader"/> ksSkinnedMesh with the same pictures.
    /// </summary>
    /// <param name="blend">Seen through by its picture's alpha (a shade on the ground): drawn after the solid things, blended over them</param>
    public static uint AddMultiMapMaterial(IKn5 kn5, string name, string diffuse, string? normal, string maps, float emissive,
        bool alphaTest = false, float ksAmbient = 0.35f, float ksDiffuse = 0.45f, string shader = "ksPerPixelMultiMap", bool blend = false)
    {
        var textures = new List<Kn5Material.TextureMapping> { new() { Name = "txDiffuse", Texture = diffuse, Slot = 0 } };
        var specular = 0.4f;
        var specularExp = 60f;
        if (alphaTest || blend)
        {
            // Cut out by the picture's alpha (a leaf, a clump of grass, a road marking), or blended by it: AC's
            // multi-map shader takes a cut-out's alpha from its normal map, not its picture, so these are drawn by the
            // plain per-pixel shader, which takes the picture's and lights them matt, with no maps or normal map.
            // Leaves and grass shine nowhere, and a picture of them was lit once already when it was taken
            shader = alphaTest ? "ksPerPixelAT" : "ksPerPixel";
            specular = 0.02f;
            specularExp = 10f;
            ksDiffuse *= 0.6f;
            ksAmbient *= 0.8f;
        }
        else
        {
            if (normal != null) textures.Add(new Kn5Material.TextureMapping { Name = "txNormal", Texture = normal, Slot = 1 });
            textures.Add(new Kn5Material.TextureMapping { Name = "txMaps", Texture = maps, Slot = 2 });
        }

        kn5.Materials[name] = new Kn5Material
        {
            Name = name,
            ShaderName = shader,
            BlendMode = blend ? Kn5MaterialBlendMode.AlphaBlend : Kn5MaterialBlendMode.Opaque,
            ShaderProperties =
            [
                Property("ksAmbient", ksAmbient),
                Property("ksDiffuse", ksDiffuse),
                Property("ksSpecular", specular),
                Property("ksSpecularEXP", specularExp),
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

    private sealed record MaterialJson(string Name, string Diffuse, string? Normal, string Maps, float Emissive, bool AlphaTest, bool Blend = false);

    private sealed record ManifestJson(List<MaterialJson> Materials, Dictionary<string, string?> Aliases);

    /// <summary>
    /// materials.json's materials, and each of the OBJ's material names to the one it is; a name to none (glass the
    /// prepare script left out) is not in the list, and its triangles are dropped
    /// </summary>
    public static Dictionary<string, uint> AddMaterials(IKn5 kn5, string prepared, string manifest, float ksAmbient = 0.35f, float ksDiffuse = 0.45f,
        string shader = "ksPerPixelMultiMap")
    {
        var json = JsonSerializer.Deserialize<ManifestJson>(File.ReadAllText(manifest), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidDataException("materials.json is empty");
        var index = new Dictionary<string, uint>();
        foreach (var m in json.Materials)
        {
            // A cut-out's pictures beyond its own are never drawn, so they stay out of the file
            var cutOrBlend = m.AlphaTest || m.Blend;
            index[m.Name] = AddMultiMapMaterial(kn5, m.Name, AddTexture(kn5, Path.Combine(prepared, m.Diffuse)),
                m.Normal == null || cutOrBlend ? null : AddTexture(kn5, Path.Combine(prepared, m.Normal)),
                cutOrBlend ? "" : AddTexture(kn5, Path.Combine(prepared, m.Maps)), m.Emissive, m.AlphaTest, ksAmbient, ksDiffuse, shader, m.Blend);
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
    /// <param name="apart">An object (an "o" line's name) whose triangles are kept apart from the rest of their material, as a group keyed by both (<see cref="ObjectOf"/>); null keeps none apart</param>
    public static Dictionary<string, (List<Kn5Node.Vertex> Vertices, List<int> Indices)> ReadObj(string path, float scale, float turn, bool mirror, bool recentre = true,
        Func<string, bool>? apart = null)
    {
        var positions = new List<Vec3>();
        var uvs = new List<Vec2>();
        var normals = new List<Vec3>();
        var corners = new List<(int P, int T, int N, string Material)>();
        var material = "";
        var objectKey = "";

        var lineNumber = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNumber++;
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
                case "o":
                    var name = raw.Trim()["o".Length..].Trim();
                    objectKey = apart != null && apart(name) ? Separator + name : "";
                    break;
                case "f" when parts.Length == 4:
                    foreach (var corner in parts.Skip(1))
                    {
                        // Each corner's indices held against what the file has given so far (a face names only vertices
                        // written before it), so another exporter's OBJ fails on its own line rather than deep in the
                        // meshes with no word of where; a negative index counts back from the last one given, as OBJ allows
                        var ids = corner.Split('/');
                        corners.Add((Index(ids[0], positions.Count, "vertex", lineNumber),
                            ids.Length > 1 && ids[1].Length > 0 ? Index(ids[1], uvs.Count, "texture coordinate", lineNumber) : -1,
                            ids.Length > 2 ? Index(ids[2], normals.Count, "normal", lineNumber) : -1, material + objectKey));
                    }
                    break;
                case "f":
                    throw new InvalidDataException($"line {lineNumber}: the OBJ is not all triangles: export it triangulated");
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

    /// <summary>
    /// A face corner's index as the list's (OBJ counts from 1, and back from the end when negative), which must name one
    /// of the <paramref name="count"/> given so far; told with the OBJ's line otherwise
    /// </summary>
    private static int Index(string text, int count, string what, int line)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var given) || given == 0)
            throw new InvalidDataException($"line {line}: '{text}' is no {what} index");

        var index = given > 0 ? given - 1 : count + given;
        if (index < 0 || index >= count)
            throw new InvalidDataException($"line {line}: {what} index {given} is out of range: the OBJ has {count} so far");
        return index;
    }

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
