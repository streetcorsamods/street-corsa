using System.Globalization;
using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Tools;

namespace StationSceneBuilder;

/// <summary>
/// Cuts the gas station scene out of an installed Assetto Corsa track: every mesh of the given models near a point,
/// the big ones (the road, the terrain) cut down to the triangles near it, moved so that point, on the ground, is the
/// middle of the scene, into one KN5 with the materials and textures they use. Meshes whose names hold one of the
/// <c>--exclude</c> words (race-day barriers, adverts) are left out. A sky dome for each light of the day goes round it
/// (<c>STATION_SKY_DAY</c>, <c>_DUSK</c>, <c>_NIGHT</c>; the game shows the one for the hour), and the ground's height
/// goes next to the model (ground.bin, <see cref="Ground"/>), so a car driving the scene sits on the road.
///
/// The track is somebody else's work: what this writes is for the player's own game, never shipped.
///
///   StationSceneBuilder &lt;track folder&gt; &lt;out.kn5&gt; --at &lt;node name | x,y,z&gt; [--radius 300]
///       [--models a.kn5,b.kn5] [--exclude word,word] [--ground-half 150]
///       [--sky day=a.jpg,dusk=b.jpg,night=c.jpg] [--sky-radius 900]
/// </summary>
internal static class Program
{

    private const string Usage = "StationSceneBuilder <track folder> <out.kn5> --at <node name | x,y,z> [--radius 300] [--models a.kn5,b.kn5] [--exclude word,word] [--ground-half 150] [--sky day=a.jpg,dusk=b.jpg,night=c.jpg] [--sky-radius 900]";

    private static int Main(string[] args)
    {
        var positional = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || !args[i - 1].StartsWith("--"))).ToList();
        var at = Option(args, "--at");
        if (positional.Count < 2 || at == null)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        foreach (var option in NumberOptions)
        {
            if (Option(args, option) is { } text && Number(text) == null)
            {
                Console.Error.WriteLine($"{option} takes a number of metres, not {text}");
                Console.Error.WriteLine(Usage);
                return 1;
            }
        }

        var track = positional[0];
        var output = positional[1];
        var radius = Number(Option(args, "--radius")) ?? 300f;
        var models = (Option(args, "--models") ?? Path.GetFileName(track.TrimEnd('/', '\\')) + ".kn5").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var sky = Option(args, "--sky");
        var skyRadius = Number(Option(args, "--sky-radius")) ?? radius * 3f;
        var exclude = (Option(args, "--exclude") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var groundHalf = Number(Option(args, "--ground-half")) ?? 150f;

        var scene = Kn5.CreateEmpty();
        scene.RootNode.Name = Path.GetFileNameWithoutExtension(output);
        Vec3? centre = ParsePoint(at);

        // The middle is found in the first model, and its meshes are the ground (the others are trees and such)
        var groundMeshes = new List<Kn5Node>();
        foreach (var model in models)
        {
            var path = Path.Combine(track, model);
            Console.WriteLine($"Reading {path}");
            var kn5 = Kn5.FromFile(path);
            var meshes = new List<(Kn5Node Node, Mat4x4 World)>();
            Collect(kn5.RootNode, Mat4x4.Identity, meshes, ref centre, at);
            if (centre is not { } middle)
            {
                Console.Error.WriteLine($"{model} has no node {at}: give its model first, or a point");
                return 1;
            }

            var kept = 0;
            var triangles = 0;
            foreach (var (node, world) in meshes)
            {
                if (exclude.Any(word => node.Name.Contains(word, StringComparison.OrdinalIgnoreCase))) continue;
                if (Cut(node, world, middle, radius) is not { } cut) continue;
                var material = kn5.GetMaterial(node.MaterialId);
                if (material == null) continue;

                cut.MaterialId = CopyMaterial(kn5, scene, material);
                cut.Name = $"{Path.GetFileNameWithoutExtension(model)}_{node.Name}";
                scene.RootNode.Children.Add(cut);
                if (model == models[0]) groundMeshes.Add(cut);
                kept++;
                triangles += cut.Indices.Length / 3;
            }

            Console.WriteLine($"  {kept} meshes, {triangles} triangles within {radius} m of {middle}");
        }

        // The ground under the middle is the scene's zero
        var ground = Ground.Build(groundMeshes, groundHalf);
        if (ground.Nearest(0f, 0f, 15f) is not { } height)
        {
            Console.Error.WriteLine("There is no ground under the middle");
            return 1;
        }

        ground.Lower(height);
        foreach (var mesh in scene.RootNode.Children)
        {
            for (var i = 0; i < mesh.Vertices.Length; i++) mesh.Vertices[i].Position -= new Vec3(0f, height, 0f);
            mesh.BoundingSphereCenter -= new Vec3(0f, height, 0f);
        }

        var groundFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "ground.bin");
        ground.Write(groundFile);
        Console.WriteLine($"The ground {height:0.00} m under the middle; {groundFile}");

        if (sky != null)
        {
            foreach (var entry in sky.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split('=', 2);
                Kn5SceneWriter.AddSky(scene, "station_sky_", parts.Length == 2 ? parts[0] : "day", parts[^1], skyRadius);
            }
        }

        Kn5SceneWriter.Save(scene, output);
        Console.WriteLine($"{output}: {new FileInfo(output).Length / 1024 / 1024} MB, {scene.Textures.Count} textures, {scene.Materials.Count} materials");
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static readonly string[] NumberOptions = ["--radius", "--sky-radius", "--ground-half"];

    /// <summary>A number as the command line gives it; null when there is none, or it is no number (checked up front)</summary>
    private static float? Number(string? text) =>
        text != null && float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static Vec3? ParsePoint(string text)
    {
        var parts = text.Split(',');
        return parts.Length == 3 && parts.All(p => float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            ? new Vec3(Number(parts[0])!.Value, Number(parts[1])!.Value, Number(parts[2])!.Value)
            : null;
    }

    /// <summary>Every mesh that is drawn, with where it is in the world; the named node gives the middle</summary>
    private static void Collect(Kn5Node node, Mat4x4 parent, List<(Kn5Node, Mat4x4)> meshes, ref Vec3? centre, string at)
    {
        var world = node.NodeClass == Kn5NodeClass.Base ? node.Transform * parent : parent;
        if (centre == null && node.Name == at) centre = Vec3.Transform(Vec3.Zero, world);

        if (node.NodeClass != Kn5NodeClass.Base)
        {
            // The track's own markers and its invisible walls are no scenery
            if (node.IsVisible && node.IsRenderable && node.Active && !node.Name.StartsWith("AC_", StringComparison.Ordinal))
                meshes.Add((node, world));
            return;
        }

        if (!node.Active) return;
        foreach (var child in node.Children) Collect(child, world, meshes, ref centre, at);
    }

    /// <summary>
    /// The triangles of a mesh that come within the radius of the middle (across the ground), in the scene's space;
    /// null when none does
    /// </summary>
    private static Kn5Node? Cut(Kn5Node node, Mat4x4 world, Vec3 middle, float radius)
    {
        var centre = Vec3.Transform(node.BoundingSphereCenter, world);
        if (Flat(centre - middle) - node.BoundingSphereRadius > radius) return null;

        var positions = node.Vertices.Select(v => Vec3.Transform(v.Position, world) - middle).ToArray();
        var near = positions.Select(p => Flat(p) <= radius).ToArray();
        var map = new Dictionary<int, int>();
        var vertices = new List<Kn5Node.Vertex>();
        var uv2 = node.Uv2 == null ? null : new List<Vec2>();
        var indices = new List<ushort>();
        for (var i = 0; i + 2 < node.Indices.Length; i += 3)
        {
            int a = node.Indices[i], b = node.Indices[i + 1], c = node.Indices[i + 2];
            if (!near[a] && !near[b] && !near[c]) continue;
            foreach (var index in new[] { a, b, c })
            {
                if (!map.TryGetValue(index, out var mapped))
                {
                    var v = node.Vertices[index];
                    mapped = vertices.Count;
                    map[index] = mapped;
                    vertices.Add(new Kn5Node.Vertex(positions[index], Normalized(Vec3.TransformNormal(v.Normal, world)), v.Tex,
                        Normalized(Vec3.TransformNormal(v.Tangent, world))));
                    uv2?.Add(node.Uv2![index]);
                }

                indices.Add((ushort)mapped);
            }
        }

        if (indices.Count == 0) return null;

        var (sphereCentre, sphereRadius) = Bounds(vertices.Select(v => v.Position));
        return new Kn5Node
        {
            NodeClass = Kn5NodeClass.Mesh,
            Active = true,
            Children = [],
            CastShadows = node.CastShadows,
            IsVisible = true,
            IsTransparent = node.IsTransparent,
            IsRenderable = true,
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            Uv2 = uv2?.ToArray(),
            Layer = node.Layer,
            LodIn = 0f,
            LodOut = 0f,
            BoundingSphereCenter = sphereCentre,
            BoundingSphereRadius = sphereRadius
        };
    }

    private static float Flat(Vec3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    private static Vec3 Normalized(Vec3 v)
    {
        var length = v.Length();
        return length > 1e-6f ? v / length : v;
    }

    private static (Vec3 Centre, float Radius) Bounds(IEnumerable<Vec3> points)
    {
        var min = new Vec3(float.MaxValue);
        var max = new Vec3(float.MinValue);
        foreach (var p in points)
        {
            min = Vec3.Min(min, p);
            max = Vec3.Max(max, p);
        }

        return ((min + max) / 2f, (max - min).Length() / 2f);
    }

    /// <summary>The material in the scene by its name, copied over with its textures the first time; its index</summary>
    private static uint CopyMaterial(IKn5 from, IKn5 to, Kn5Material material)
    {
        var names = to.Materials.Keys.ToList();
        var index = names.IndexOf(material.Name);
        if (index >= 0) return (uint)index;

        foreach (var mapping in material.TextureMappings)
        {
            if (to.Textures.ContainsKey(mapping.Texture) || !from.Textures.TryGetValue(mapping.Texture, out var texture)) continue;
            to.Textures[mapping.Texture] = texture;
            if (from.TexturesData.TryGetValue(mapping.Texture, out var data)) to.TexturesData[mapping.Texture] = data;
        }

        to.Materials[material.Name] = material;
        return (uint)(to.Materials.Count - 1);
    }
}
