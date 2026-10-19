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
/// scene is the game's to say (prop.json). With --keep-origin the model keeps the origin it was made with: a part's
/// model, whose origin is where it mounts (tools/RunningGearParts/prepare_standins.py). Each --node name=folder adds a
/// moving part as a node of that name (the lift's lift_post, the engine stand's engine_stand_head), its OBJ name.obj in
/// a folder prepared as the model's (tools/GarageScene/compose_props.py splits them), put at x,y,z after an @ (where
/// it is as the model rests: the lift's arms on the floor); it needs --keep-origin.
///
///   PropBuilder &lt;prepared folder&gt; &lt;name&gt; &lt;out folder&gt; --scale 0.112 [--turn degrees] [--mirror] [--keep-origin]
///       [--node &lt;name&gt;=&lt;prepared folder&gt; ...]
///
/// The Bush radio: PropBuilder &lt;prepared&gt; radio "Street Corsa/Content/bushradio/props/radio" --scale 0.112
/// </summary>
internal static class Program
{
    private const string Usage = "PropBuilder <prepared folder> <name> <out folder> --scale <metres a unit> [--turn <degrees>] [--mirror] [--keep-origin] [--node <name>=<prepared folder>[@x,y,z] ...]";

    private static int Main(string[] args)
    {
        // A word after --scale or --turn is its number, never the folder
        var options = new BuilderArgs(args, "--mirror", "--keep-origin");
        var positional = options.Positional;
        if (positional.Count != 3 || options.Option("--scale") == null)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        if (!options.NumbersOk(Usage, "--scale", "--turn")) return 1;

        // The moving parts keep their origins, so the rest must keep its own with them
        if (options.Options("--node").Count > 0 && !options.Flag("--keep-origin"))
        {
            Console.Error.WriteLine("--node needs --keep-origin: the model and its moving parts share one origin");
            return 1;
        }

        var scale = options.Number("--scale")!.Value;
        var turn = options.Number("--turn") ?? 0f;
        var mirror = options.Flag("--mirror");
        var (prepared, name, output) = (positional[0], positional[1], positional[2]);

        var groups = ObjModel.ReadObj(Path.Combine(prepared, name + ".obj"), scale, turn, mirror, recentre: !options.Flag("--keep-origin"));
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

        // The parts that move, each a node of its own (the lift's lift_post): its OBJ and pictures prepared as the model's
        // are, the node's origin the OBJ's
        foreach (var node in options.Options("--node"))
        {
            // name=folder, or name=folder@x,y,z: where the node is put as the model rests (the lift's arms on the floor)
            var split = node.IndexOf('=');
            var (nodeName, folder) = split > 0 ? (node[..split], node[(split + 1)..]) : ("", "");
            var at = folder.LastIndexOf('@');
            var rest = Vec3.Zero;
            if (at >= 0)
            {
                var numbers = folder[(at + 1)..].Split(',').Select(n => float.TryParse(n, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : float.NaN).ToArray();
                if (numbers.Length != 3 || numbers.Any(n => !float.IsFinite(n)))
                {
                    Console.Error.WriteLine($"--node {node}: the place after @ is x,y,z in metres");
                    return 1;
                }

                rest = new Vec3(numbers[0], numbers[1], numbers[2]);
                folder = folder[..at];
            }

            var nodeManifest = Path.Combine(folder, "materials.json");
            if (nodeName.Length == 0 || !File.Exists(nodeManifest))
            {
                Console.Error.WriteLine($"--node {node}: a name=prepared folder with a materials.json");
                return 1;
            }

            var nodeGroups = ObjModel.ReadObj(Path.Combine(folder, nodeName + ".obj"), scale, turn, mirror, recentre: false);
            var before = kn5.RootNode.Children.Count;
            ObjModel.AddMeshes(kn5, nodeGroups, ObjModel.AddMaterials(kn5, folder, nodeManifest), null, castShadows: true);

            var parent = Kn5Node.CreateBaseNode(nodeName);
            parent.Transform = new Mat4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, rest.X, rest.Y, rest.Z, 1);
            parent.Children.AddRange(kn5.RootNode.Children.Skip(before));
            kn5.RootNode.Children.RemoveRange(before, kn5.RootNode.Children.Count - before);
            kn5.RootNode.Children.Add(parent);
            Console.WriteLine($"{nodeName}: {nodeGroups.Values.Sum(g => g.Indices.Count) / 3} triangles");
        }

        var path = Path.Combine(output, name + ".kn5");
        Save(kn5, path);
        Console.WriteLine($"{path}: {new FileInfo(path).Length / 1024} KB");
        return 0;
    }
}
