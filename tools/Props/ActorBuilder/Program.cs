using System.Text.Json;
using AcTools.Kn5File;
using AcTools.KsAnimFile;
using AcTools.Numerics;
using StreetCorsa.Tools;

namespace ActorBuilder;

/// <summary>
/// A figure that moves (the gas station's attendant) as a KN5 with a skin on bones, and each of its animations as a
/// .ksanim next to it, out of what prepare_actor.py (the skin, the bones, the animations) and prepare_prop.py (the
/// materials and their pictures, --keep-names) made of a rigged model. The model comes in metres, facing +z, its
/// origin where prepare_actor.py had it (under the figure's feet); where it stands in a scene is the game's to say.
///
/// The skin is drawn as AC draws a driver: a mesh whose vertices are each bound to up to four bones, every bone a
/// dummy of the model moved by the animation (its node's name in the .ksanim), the vertices following. The materials
/// are AC's ksSkinnedMesh with the pictures a prop's ksPerPixelMultiMap has.
///
///   ActorBuilder &lt;prepared folder&gt; &lt;actor.json&gt; &lt;name&gt; &lt;out folder&gt; [--rename &lt;animation&gt;=&lt;file name&gt; ...]
///
/// Each animation's file is named after the animation in the model, in lower case ("Animation" gives animation.ksanim),
/// or as --rename says (Animation=talk gives talk.ksanim).
///
/// The attendant: ActorBuilder &lt;prepared&gt; &lt;actor.json&gt; attendant "Street Corsa/Content/streetrod/props/attendant" --rename Animation=talk
/// </summary>
internal static class Program
{
    private const string Usage = "ActorBuilder <prepared folder> <actor.json> <name> <out folder> [--rename <animation>=<file name> ...]";

    /// <summary>The bones AC's skinned shader (ksSkinnedMesh) takes at most; prepare_actor.py folds a rig down to them</summary>
    public const int MaxBones = 64;

    private sealed record BoneJson(string Name, int Parent, float[] Rest, float[] Bind);

    private sealed record MeshJson(string Name, string Material, float[] Vertices, int[] Indices);

    private sealed record AnimationJson(float Fps, int Frames, Dictionary<string, float[][]> Bones);

    private sealed record ActorJson(List<BoneJson> Bones, List<MeshJson> Meshes, Dictionary<string, AnimationJson> Animations);

    private static int Main(string[] args)
    {
        var options = new BuilderArgs(args);
        var positional = options.Positional;
        if (positional.Count != 4)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var (prepared, actorFile, name, output) = (positional[0], positional[1], positional[2], positional[3]);
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rename in options.Options("--rename"))
        {
            var split = rename.IndexOf('=');
            if (split <= 0 || split == rename.Length - 1)
            {
                Console.Error.WriteLine($"--rename {rename}: an animation's name, =, the file's name");
                return 1;
            }

            renames[rename[..split]] = rename[(split + 1)..];
        }
        var actor = JsonSerializer.Deserialize<ActorJson>(File.ReadAllText(actorFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException($"{actorFile} is empty");
        if (actor.Bones.Count == 0 || actor.Meshes.Count == 0)
        {
            Console.Error.WriteLine($"{actorFile}: no bones or no meshes");
            return 1;
        }

        if (actor.Bones.Count > MaxBones)
        {
            Console.Error.WriteLine($"{actorFile}: {actor.Bones.Count} bones, more than the {MaxBones} AC's skinned shader takes (AcTools stops the game with a message box over such a model); prepare_actor.py --max-bones {MaxBones}");
            return 1;
        }

        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = name;
        var materialOf = ObjModel.AddMaterials(kn5, prepared, Path.Combine(prepared, "materials.json"), shader: "ksSkinnedMesh");

        // The bones, each a dummy under its parent (the model's root for one without), where the rig has it at rest
        var dummies = new List<Kn5Node>();
        foreach (var bone in actor.Bones)
        {
            var dummy = Kn5Node.CreateBaseNode(bone.Name);
            dummy.Transform = Matrix(bone.Rest, bone.Name);
            dummies.Add(dummy);
            (bone.Parent >= 0 ? dummies[bone.Parent] : kn5.RootNode).Children.Add(dummy);
        }

        var bones = actor.Bones.Select(b => new Kn5Node.Bone { Name = b.Name, Transform = Matrix(b.Bind, b.Name) }).ToArray();
        var triangles = 0;
        foreach (var (mesh, index) in actor.Meshes.Select((m, i) => (m, i)))
        {
            if (!materialOf.TryGetValue(mesh.Material, out var material))
            {
                Console.Error.WriteLine($"{mesh.Name}: no material {mesh.Material} in {prepared} (glass is left out)");
                continue;
            }

            if (mesh.Vertices.Length % 16 != 0 || mesh.Indices.Length % 3 != 0)
            {
                Console.Error.WriteLine($"{mesh.Name}: vertices are 16 floats each, indices triangles");
                return 1;
            }

            var count = mesh.Vertices.Length / 16;
            if (count > Kn5SceneWriter.MaxVerticesPerMesh)
            {
                Console.Error.WriteLine($"{mesh.Name}: {count} vertices is more than a mesh holds ({Kn5SceneWriter.MaxVerticesPerMesh})");
                return 1;
            }

            var vertices = new List<Kn5Node.Vertex>(count);
            var weights = new Kn5Node.VerticeWeight[count];
            for (var i = 0; i < count; i++)
            {
                var v = mesh.Vertices.AsSpan(i * 16, 16);
                vertices.Add(new Kn5Node.Vertex(new Vec3(v[0], v[1], v[2]), new Vec3(v[3], v[4], v[5]), new Vec2(v[6], v[7]), new Vec3(1f, 0f, 0f)));
                weights[i] = new Kn5Node.VerticeWeight { Weights = new Vec4(v[8], v[9], v[10], v[11]), Indices = new Vec4(v[12], v[13], v[14], v[15]) };
                if (v[12] >= bones.Length || v[13] >= bones.Length || v[14] >= bones.Length || v[15] >= bones.Length)
                {
                    Console.Error.WriteLine($"{mesh.Name}: a vertex is bound to a bone that is not there");
                    return 1;
                }
            }

            var indices = mesh.Indices.ToList();
            if (indices.Any(i => i < 0 || i >= count))
            {
                Console.Error.WriteLine($"{mesh.Name}: an index is out of its vertices");
                return 1;
            }

            kn5.RootNode.Children.Add(SkinnedMesh($"{name.ToUpperInvariant()}_{index}", ObjModel.WithTangents(vertices, indices), weights, indices, bones, material));
            triangles += indices.Count / 3;
        }

        Directory.CreateDirectory(output);
        var model = Path.Combine(output, name + ".kn5");
        Kn5SceneWriter.Save(kn5, model);
        Console.WriteLine($"{model}: {new FileInfo(model).Length / 1024} KB, {actor.Bones.Count} bones, {triangles} triangles, {kn5.Materials.Count} material(s)");

        foreach (var (animation, clip) in actor.Animations)
        {
            var entries = clip.Bones.Select(b => new KsAnimEntryV2
            {
                NodeName = b.Key,
                KeyFrames = b.Value.Select(Keyframe).ToArray()
            }).ToList();
            var file = Path.Combine(output, SafeName(renames.GetValueOrDefault(animation, animation)) + ".ksanim");
            KsAnim.FromEntries(entries).Save(file);
            Console.WriteLine($"{file}: {clip.Frames} frames at {clip.Fps:0.#} fps ({clip.Frames / clip.Fps:0.0} s), {entries.Count} bone(s) moving");
        }

        return 0;
    }

    /// <summary>A mesh on bones: drawn, casting a shadow, its vertices each bound to up to four of the model's dummies</summary>
    private static Kn5Node SkinnedMesh(string name, List<Kn5Node.Vertex> vertices, Kn5Node.VerticeWeight[] weights, List<int> indices,
        Kn5Node.Bone[] bones, uint material)
    {
        var min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vec3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var v in vertices)
        {
            min = new Vec3(Math.Min(min.X, v.Position.X), Math.Min(min.Y, v.Position.Y), Math.Min(min.Z, v.Position.Z));
            max = new Vec3(Math.Max(max.X, v.Position.X), Math.Max(max.Y, v.Position.Y), Math.Max(max.Z, v.Position.Z));
        }

        var size = new Vec3(max.X - min.X, max.Y - min.Y, max.Z - min.Z);
        return new Kn5Node
        {
            NodeClass = Kn5NodeClass.SkinnedMesh,
            Name = name,
            Active = true,
            Children = [],
            CastShadows = true,
            IsVisible = true,
            IsRenderable = true,
            IsTransparent = false,
            Bones = bones,
            Vertices = vertices.ToArray(),
            VerticeWeights = weights,
            Indices = indices.Select(i => (ushort)i).ToArray(),
            MaterialId = material,
            Layer = 0,
            MisteryBytes = new byte[8],
            BoundingSphereCenter = new Vec3((min.X + max.X) / 2f, (min.Y + max.Y) / 2f, (min.Z + max.Z) / 2f),
            BoundingSphereRadius = MathF.Sqrt(size.X * size.X + size.Y * size.Y + size.Z * size.Z) / 2f
        };
    }

    private static Mat4x4 Matrix(float[] m, string bone)
    {
        if (m.Length != 16 || m.Any(v => !float.IsFinite(v))) throw new InvalidDataException($"{bone}: a transform is 16 finite numbers");
        return new Mat4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
    }

    /// <summary>A keyframe as prepare_actor.py wrote it: rotation x, y, z, w, then the position, then the scale</summary>
    private static KsAnimKeyframe Keyframe(float[] k)
    {
        if (k.Length != 10 || k.Any(v => !float.IsFinite(v))) throw new InvalidDataException("a keyframe is 10 finite numbers");
        return new KsAnimKeyframe(new Quat(k[0], k[1], k[2], k[3]), new Vec3(k[4], k[5], k[6]), new Vec3(k[7], k[8], k[9]));
    }

    /// <summary>An animation's file name out of its name in the model: letters, digits and _ only, in lower case</summary>
    private static string SafeName(string animation)
    {
        var clean = new string(animation.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray()).Trim('_');
        return clean.Length > 0 ? clean : "animation";
    }
}
