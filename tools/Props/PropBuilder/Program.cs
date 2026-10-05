using System.Globalization;
using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Tools;
using static StreetCorsa.Tools.Kn5SceneWriter;

namespace PropBuilder;

/// <summary>
/// A prop for a scene (the garage's radio) as a KN5, out of what a prepare script left (tools/Props/prepare_*.py): an
/// OBJ (Y up, facing -Z, triangles) and its pictures, drawn with AC's ksPerPixelMultiMap. With a materials.json
/// (prepare_prop.py) each of the OBJ's materials is one of it, its pictures named there; without, the model is one
/// material of &lt;name&gt;_diffuse.jpg, &lt;name&gt;_normal.png and &lt;name&gt;_maps.png (prepare_radio.py). The model
/// comes out in metres, stood on its base: its origin the middle of its footprint, on the floor. Where it stands in a
/// scene is the game's to say (prop.json).
///
///   PropBuilder &lt;prepared folder&gt; &lt;name&gt; &lt;out folder&gt; --scale 0.112 [--turn degrees] [--mirror]
///
/// The Bush radio: PropBuilder &lt;prepared&gt; radio "Street Corsa/Content/bushradio/props/radio" --scale 0.112
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

        var groups = ObjModel.ReadObj(Path.Combine(prepared, name + ".obj"), scale, turn, mirror);
        Console.WriteLine($"{name}: {groups.Values.Sum(g => g.Vertices.Count)} vertices, {groups.Values.Sum(g => g.Indices.Count) / 3} triangles, {groups.Count} material group(s)");

        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = name;

        var manifest = Path.Combine(prepared, "materials.json");
        var materialOf = File.Exists(manifest)
            ? ObjModel.AddMaterials(kn5, prepared, manifest)
            : new Dictionary<string, uint>
            {
                [""] = ObjModel.AddMultiMapMaterial(kn5, name, AddTexture(kn5, Path.Combine(prepared, name + "_diffuse.jpg")),
                    AddTexture(kn5, Path.Combine(prepared, name + "_normal.png")), AddTexture(kn5, Path.Combine(prepared, name + "_maps.png")), 0f)
            };

        // Without a manifest, all is the one material
        ObjModel.AddMeshes(kn5, groups, materialOf, File.Exists(manifest) ? null : materialOf.Values.First(), castShadows: true);

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
}
