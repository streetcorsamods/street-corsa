using System.Numerics;
using AcTools.Kn5File;
using AcTools.Numerics;

namespace StreetCorsa.Parts;

/// <summary>
/// A small box for the parts that come without a model (fuel rails and injectors that their packs never
/// shows). Parts are picked and mounted by clicking them, so every part needs something to click.
/// Also builds meshes of boxes and solid-colour materials for anything else drawn from boxes (the engine stand).
/// </summary>
internal static class PlaceholderMesh
{
    public const float HalfSize = 0.035f;

    private const string MaterialName = "placeholder";

    /// <summary>The box as a mesh node of the model, around the part's own origin; its material is added to the model once</summary>
    public static Kn5Node Create(IKn5 kn5, string name) =>
        Boxes(name, new[] { (Vector3.Zero, new Vector3(HalfSize)) }, SolidMaterial(kn5, MaterialName, 0x9A, 0x9A, 0x9A, 0.45f, 0.5f, 0.25f, 30f));

    /// <summary>One mesh node of axis-aligned boxes, each given by its centre and half-size</summary>
    public static Kn5Node Boxes(string name, IReadOnlyList<(Vector3 Centre, Vector3 Half)> boxes, uint materialId)
    {
        // Four corners per side, so that every side gets its own normal
        var sides = new (Vector3 Normal, Vector3 U, Vector3 V)[]
        {
            (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX), (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY), (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX)
        };

        var vertices = new List<Kn5Node.Vertex>();
        var indices = new List<ushort>();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var (centre, half) in boxes)
        {
            min = Vector3.Min(min, centre - half);
            max = Vector3.Max(max, centre + half);
            foreach (var (normal, u, v) in sides)
            {
                var first = (ushort)vertices.Count;
                foreach (var (a, b) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    var p = centre + (normal + u * a + v * b) * half;
                    vertices.Add(new Kn5Node.Vertex(new Vec3(p.X, p.Y, p.Z), new Vec3(normal.X, normal.Y, normal.Z),
                        new Vec2((a + 1) / 2, (b + 1) / 2), new Vec3(u.X, u.Y, u.Z)));
                }

                // Both ways round: the box shows whichever way the renderer culls
                var (second, third, fourth) = ((ushort)(first + 1), (ushort)(first + 2), (ushort)(first + 3));
                indices.AddRange(new[] { first, second, third, first, third, fourth });
                indices.AddRange(new[] { first, third, second, first, fourth, third });
            }
        }

        var middle = (min + max) / 2f;
        return new Kn5Node
        {
            NodeClass = Kn5NodeClass.Mesh,
            Name = name,
            Active = true,
            Children = new List<Kn5Node>(),
            CastShadows = true,
            IsVisible = true,
            IsRenderable = true,
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            MaterialId = materialId,
            BoundingSphereCenter = new Vec3(middle.X, middle.Y, middle.Z),
            BoundingSphereRadius = Vector3.Distance(min, max) / 2f
        };
    }

    /// <summary>A plain ksPerPixel material of one colour, added to the model once by name; returns its id</summary>
    public static uint SolidMaterial(IKn5 kn5, string name, byte red, byte green, byte blue, float ambient, float diffuse, float specular, float exponent)
    {
        if (IdOf(kn5, name) is { } existing) return existing;

        var texture = name + ".bmp";
        var data = Kn5Bitmap.Solid(red, green, blue);
        kn5.Textures[texture] = new Kn5Texture { Name = texture, Active = true, Length = data.Length };
        kn5.TexturesData[texture] = data;
        kn5.Materials[name] = new Kn5Material
        {
            Name = name,
            ShaderName = "ksPerPixel",
            ShaderProperties = new[]
            {
                new Kn5Material.ShaderProperty { Name = "ksAmbient", ValueA = ambient },
                new Kn5Material.ShaderProperty { Name = "ksDiffuse", ValueA = diffuse },
                new Kn5Material.ShaderProperty { Name = "ksSpecular", ValueA = specular },
                new Kn5Material.ShaderProperty { Name = "ksSpecularEXP", ValueA = exponent }
            },
            TextureMappings = new[] { new Kn5Material.TextureMapping { Name = "txDiffuse", Texture = texture, Slot = 0 } }
        };

        return IdOf(kn5, name)!.Value;
    }

    private static uint? IdOf(IKn5 kn5, string name)
    {
        var index = 0u;
        foreach (var key in kn5.Materials.Keys)
        {
            if (key == name) return index;
            index++;
        }

        return null;
    }
}
