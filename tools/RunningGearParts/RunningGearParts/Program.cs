using System.Globalization;
using System.Text;
using System.Text.Json;
using AcTools.Kn5File;

namespace RunningGearParts;

/// <summary>
/// The running gear's own models (roadmap step 31 phases 4 and 5), made here out of lathe profiles, swept rods and boxes
/// and written as KN5 next to the packs under parts/running_gear that name them: the rear axle housings of every family,
/// their rings and pinions, the axle shaft, the drum's wheel cylinder, its backing plates and the front hub, the period
/// shocks, the torsion bars and sway bars (one each, at the size its pack gives in diameter_in), and the traction bars. The stand-ins made of downloads (the
/// drums, discs, shoes, leaves and the carrier's case) are prepare_standins.py's.
///
///   RunningGearParts build &lt;parts folder&gt;
///   RunningGearParts obj &lt;model.kn5&gt; &lt;out.obj&gt;      a model's triangles as an OBJ, to look at
/// </summary>
internal static class Program
{
    private const string Usage = "RunningGearParts build <parts folder> | obj <model.kn5> <out.obj>";

    private static readonly string[] ShaftPacks = ["axles_gm", "axles_ford", "axles_mopar", "axles_dana", "axle_speed"];

    /// <summary>The drums' sizes, each with its model's open side (least X) and outside radius, as prepare_standins.py made them</summary>
    private static readonly (string Size, float InnerX, float Radius)[] DrumSizes =
        [("9", -0.092f, 0.127f), ("9_5", -0.097f, 0.134f), ("10", -0.102f, 0.141f), ("11", -0.112f, 0.155f)];

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "build") return Build(Path.Combine(args[1], "running_gear"));
        if (args.Length == 3 && args[0] == "obj") return Obj(args[1], args[2]);
        Console.Error.WriteLine(Usage);
        return 1;
    }

    private static int Build(string root)
    {
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"No running_gear packs at {root}");
            return 1;
        }

        // Only into a pack that is there: the names are this tool's, and a pack renamed since would otherwise get a new
        // folder of models no pack.json names (Kn5SceneWriter.Save makes the folder), with nothing said
        void Write(Model model, string pack)
        {
            var packJson = Path.Combine(root, pack, PackFileName);
            if (!File.Exists(packJson)) throw new FileNotFoundException($"{pack}: no {PackFileName} names its models at {packJson}; the pack was renamed or moved", packJson);

            var path = Path.Combine(root, pack, model.Name + ".kn5");
            model.Save(path);
            Console.WriteLine($"{pack}/{model.Name}.kn5: {model.Triangles} triangles, {new FileInfo(path).Length / 1024} KB");
        }

        try
        {
            foreach (var family in Parts.Families)
            {
                Write(Parts.Housing(family), family.Pack);
                Write(Parts.RingPinion(family), family.Pack);
            }

            foreach (var pack in ShaftPacks) Write(Parts.AxleShaft(), pack);
            Write(Parts.WheelCylinder(), "brakes_drum");

            // A backing plate for each drum size (the stand-in drums' open side and radius, as their models are), the front hub
            foreach (var (size, innerX, radius) in DrumSizes) Write(Parts.BackingPlate("plate_" + size, innerX, radius), "brakes_drum");
            Write(Parts.Hub(), "brakes_drum");

            Write(Parts.Shock("gabriel_hd", Finish.CastIron), "shocks_period");
            Write(Parts.Shock("koni", Finish.Red, collar: true), "shocks_period");
            Write(Parts.Shock("drag_90_10", Finish.Chrome, collar: true), "shocks_period");
            Write(Parts.Shock("drag_50_50", Finish.Blue, collar: true), "shocks_period");

            foreach (var (id, inches) in Sized(root, "springs_torsion")) Write(Parts.TorsionBar(id, inches), "springs_torsion");
            foreach (var (id, inches) in Sized(root, "sway_bars")) Write(Parts.SwayBar(id, inches), "sway_bars");
            foreach (var id in new[] { "traction_master", "lakewood", "slapper", "ladder" }) Write(Parts.TractionBar(id), "springs_leaf");
        }
        catch (FileNotFoundException ex)
        {
            // The models written so far stand in packs that are there; the missing pack is told and the run stops
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        return 0;
    }

    private const string PackFileName = "pack.json";

    /// <summary>A pack's parts that give their size in diameter_in: each one's own id and its diameter</summary>
    private static IEnumerable<(string Id, float Inches)> Sized(string root, string pack)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, pack, PackFileName)));
        foreach (var part in json.RootElement.GetProperty("parts").EnumerateArray())
        {
            if (!part.TryGetProperty("specs", out var specs) || !specs.TryGetProperty("diameter_in", out var diameter)) continue;
            var id = part.GetProperty("id").GetString()!;
            yield return (id[(id.LastIndexOf('/') + 1)..], diameter.GetSingle());
        }
    }

    private static int Obj(string kn5Path, string objPath)
    {
        var kn5 = Kn5.FromFile(kn5Path);
        var text = new StringBuilder();
        var offset = 1;
        foreach (var node in kn5.RootNode.Children.Where(n => n.NodeClass == Kn5NodeClass.Mesh))
        {
            text.AppendLine($"o {node.Name}");
            foreach (var v in node.Vertices)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"v {v.Position.X:0.#####} {v.Position.Y:0.#####} {v.Position.Z:0.#####}"));
            for (var i = 0; i + 2 < node.Indices.Length; i += 3)
                text.AppendLine($"f {node.Indices[i] + offset} {node.Indices[i + 1] + offset} {node.Indices[i + 2] + offset}");
            offset += node.Vertices.Length;
        }

        File.WriteAllText(objPath, text.ToString());
        return 0;
    }
}
