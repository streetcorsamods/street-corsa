using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Helpers;

namespace StreetCorsa.Tools;

/// <summary>
/// What the scene builders (station, street, warehouse) have in common when they put a KN5 together: pictures,
/// the plain ksPerPixel material every surface of theirs is, meshes with their bounds, a sky dome, and the save.
/// Linked into each builder, which keeps its own geometry.
/// </summary>
internal static class Kn5SceneWriter
{
    /// <summary>As many vertices as a mesh's 16-bit indices reach</summary>
    public const int MaxVerticesPerMesh = ushort.MaxValue;

    /// <summary>A picture into the model, by its file name in lower case unless given a key; the key</summary>
    public static string AddTexture(IKn5 kn5, string file, string? key = null)
    {
        var data = File.ReadAllBytes(file);
        key ??= Path.GetFileName(file).ToLowerInvariant();
        kn5.Textures[key] = new Kn5Texture { Name = key, Active = true, Length = data.Length };
        kn5.TexturesData[key] = data;
        return key;
    }

    /// <summary>
    /// A ksPerPixel material drawn with one picture, which must be in the model already; its index, found by its name
    /// (a material given again keeps its place)
    /// </summary>
    public static uint AddMaterial(IKn5 kn5, string name, string texture, float ambient, float diffuse, float specular = 0f,
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
        return MaterialIndex(kn5, name);
    }

    /// <summary>Where a material is in the model's list, which is what a mesh names it by</summary>
    public static uint MaterialIndex(IKn5 kn5, string name)
    {
        var index = kn5.Materials.Keys.ToList().IndexOf(name);
        if (index < 0) throw new InvalidOperationException($"no material {name}");
        return (uint)index;
    }

    private static Kn5Material.ShaderProperty Property(string name, float value) => new() { Name = name, ValueA = value };

    /// <summary>
    /// A mesh of the scene's own, drawn and shadowless unless asked (a prop set into a scene casts one), its bounding
    /// sphere round its box; flip turns every triangle's winding round
    /// </summary>
    public static void AddMesh(IKn5 kn5, string name, IReadOnlyList<Kn5Node.Vertex> vertices, IEnumerable<int> indices, uint material,
        bool flip = false, bool castShadows = false, Kn5Node? parent = null)
    {
        if (vertices.Count > MaxVerticesPerMesh) throw new InvalidOperationException($"{name}: too many vertices");

        var shorts = indices.Select(i => (ushort)i).ToArray();
        if (flip)
        {
            for (var i = 0; i + 2 < shorts.Length; i += 3) (shorts[i + 1], shorts[i + 2]) = (shorts[i + 2], shorts[i + 1]);
        }

        var min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vec3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var v in vertices)
        {
            min = new Vec3(Math.Min(min.X, v.Position.X), Math.Min(min.Y, v.Position.Y), Math.Min(min.Z, v.Position.Z));
            max = new Vec3(Math.Max(max.X, v.Position.X), Math.Max(max.Y, v.Position.Y), Math.Max(max.Z, v.Position.Z));
        }

        var size = new Vec3(max.X - min.X, max.Y - min.Y, max.Z - min.Z);
        (parent ?? kn5.RootNode).Children.Add(new Kn5Node
        {
            NodeClass = Kn5NodeClass.Mesh,
            Name = name,
            Active = true,
            Children = [],
            // The scenes are lit the way their pictures were taken (a photo's shadows, a key light through the roof):
            // nothing of them stands in the light's way
            CastShadows = castShadows,
            IsVisible = true,
            IsRenderable = true,
            Vertices = vertices.ToArray(),
            Indices = shorts,
            MaterialId = material,
            BoundingSphereCenter = new Vec3((min.X + max.X) / 2f, (min.Y + max.Y) / 2f, (min.Z + max.Z) / 2f),
            BoundingSphereRadius = MathF.Sqrt(size.X * size.X + size.Y * size.Y + size.Z * size.Z) / 2f
        });
    }

    /// <summary>
    /// A node with nothing of its own at <paramref name="at"/>, for meshes to go under (<see cref="AddMesh"/>'s parent):
    /// the game moves the node, and what is under it goes with it. AC's node transform is a row-major matrix with the
    /// translation in its last row
    /// </summary>
    public static Kn5Node AddDummy(IKn5 kn5, string name, Vec3 at)
    {
        var node = new Kn5Node
        {
            NodeClass = Kn5NodeClass.Base,
            Name = name,
            Active = true,
            Children = [],
            Transform = new Mat4x4(1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, at.X, at.Y, at.Z, 1f)
        };
        kn5.RootNode.Children.Add(node);
        return node;
    }

    /// <summary>
    /// A dome round the middle, facing in, drawn with an equirectangular panorama: from <paramref name="below"/>
    /// (radians under the horizon) to the top, u = 0.5 - atan2(x, z) / 2pi as the street's
    /// </summary>
    public static void SkyDome(IKn5 kn5, string name, uint material, float radius, int segments, int rings, float below)
    {
        var vertices = new List<Kn5Node.Vertex>();
        var indices = new List<ushort>();
        for (var ring = 0; ring <= rings; ring++)
        {
            var elevation = below + (MathF.PI / 2f - below) * ring / rings;
            for (var column = 0; column <= segments; column++)
            {
                var azimuth = MathF.PI - 2f * MathF.PI * column / segments;
                var direction = new Vec3(MathF.Cos(elevation) * MathF.Sin(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Cos(azimuth));
                var uv = new Vec2((float)column / segments, 0.5f - elevation / MathF.PI);
                vertices.Add(new Kn5Node.Vertex(direction * radius, -direction, uv, new Vec3(1f, 0f, 0f)));
            }
        }

        var stride = segments + 1;
        for (var ring = 0; ring < rings; ring++)
        {
            for (var column = 0; column < segments; column++)
            {
                var a = ring * stride + column;
                indices.AddRange([(ushort)a, (ushort)(a + 1), (ushort)(a + stride), (ushort)(a + 1), (ushort)(a + stride + 1), (ushort)(a + stride)]);
            }
        }

        kn5.RootNode.Children.Add(new Kn5Node
        {
            NodeClass = Kn5NodeClass.Mesh,
            Name = name,
            Active = true,
            Children = [],
            CastShadows = false,
            IsVisible = true,
            IsRenderable = true,
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            MaterialId = material,
            BoundingSphereCenter = new Vec3(0f, 0f, 0f),
            BoundingSphereRadius = radius
        });
    }

    /// <summary>
    /// A dome round the scene for a light of the day, drawn with an equirectangular panorama and lit by itself, from a
    /// little below the horizon to the top: its material &lt;prefix&gt;&lt;light&gt;, its mesh the same in capitals
    /// (STATION_SKY_DUSK), which the game shows at that light and hides at the others
    /// </summary>
    public static void AddSky(IKn5 kn5, string prefix, string light, string panorama, float radius)
    {
        var key = AddTexture(kn5, panorama, $"{prefix}{light}{Path.GetExtension(panorama).ToLowerInvariant()}");
        var material = AddMaterial(kn5, prefix + light, key, ambient: 0f, diffuse: 0f, emissive: 1f);
        SkyDome(kn5, (prefix + light).ToUpperInvariant(), material, radius, segments: 128, rings: 48, below: -0.08f);
    }

    /// <summary>
    /// Every picture of a prepared folder (its .jpg files, in name order) into the model, whether the first look wears
    /// it or not: a scene with looks switches its surfaces to the others at run time
    /// </summary>
    public static void AddTextures(IKn5 kn5, string folder)
    {
        foreach (var file in Directory.GetFiles(folder, "*.jpg").OrderBy(f => f, StringComparer.Ordinal))
            AddTexture(kn5, file);
    }

    /// <summary>An empty model named <paramref name="rootName"/> at its root, with every picture of the prepared folder in it</summary>
    public static IKn5 Begin(string rootName, string prepared)
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = rootName;
        AddTextures(kn5, prepared);
        return kn5;
    }

    /// <summary>
    /// The credit line of a scene whose surfaces are Poly Haven's: each texture once, by its Poly Haven name (the
    /// prepared file's name less its surface prefix, "floor_garage_floor.jpg" giving "garage_floor"), in name order
    /// </summary>
    public static string PolyHavenCredit(IEnumerable<string> textures) =>
        "Surface textures by Poly Haven (CC0): " + string.Join(", ", textures
            .Select(t => t[(t.IndexOf('_') + 1)..^4]).Distinct().Order(StringComparer.Ordinal));

    /// <summary>
    /// A scene and its description into the out folder: &lt;name&gt;.kn5 (<see cref="Save"/>) and &lt;name&gt;.json, both
    /// written whole or not at all, each told on the console
    /// </summary>
    public static void SaveScene(IKn5 kn5, string output, string name, string description)
    {
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, name + ".kn5");
        Save(kn5, path);
        Console.WriteLine($"{path}: {new FileInfo(path).Length / 1024} KB");

        var json = Path.Combine(output, name + ".json");
        SafeFile.WriteAllText(json, description + Environment.NewLine);
        Console.WriteLine(json);
    }

    /// <summary>
    /// Saves the model whole or not at all: to a temp file next to it (named as SafeFile names its own, so its sweep
    /// knows it), flushed to the disk, then renamed over the target. A builder that stops half way leaves the shipped
    /// scene as it was
    /// </summary>
    public static void Save(IKn5 kn5, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                kn5.Save(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
