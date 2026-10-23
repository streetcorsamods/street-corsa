using System.Globalization;
using System.Text;
using System.Text.Json;
using AcTools.Kn5File;

namespace RunningGearParts;

/// <summary>
/// The running gear's own models (roadmap step 31 phases 4 and 5), made here out of lathe profiles, swept rods and boxes
/// and written as KN5 next to the part files under parts/running_gear that name them: the rear axle housings of every family,
/// their rings and pinions, the axle shaft, the drum's wheel cylinder, its backing plates and the front hub, the 4-piston disc,
/// the period shocks and the stock shocks' factory one, the torsion bars and sway bars (one each, at the size its part gives in diameter_in), and the traction bars; and outside running_gear the
/// speed shop's wheels (rims/speed_shop) and mufflers (exhaust/glasspacks). The stand-ins made of downloads (the
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
        if (args.Length == 2 && args[0] == "build") return Build(args[1]);
        if (args.Length == 3 && args[0] == "obj") return Obj(args[1], args[2]);
        Console.Error.WriteLine(Usage);
        return 1;
    }

    private static int Build(string parts)
    {
        var root = Path.Combine(parts, "running_gear");
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"No running_gear parts at {root}");
            return 1;
        }

        // Only into a folder of parts that is there: the names are this tool's, and a folder renamed since would otherwise
        // get a new folder of models no part names (Kn5SceneWriter.Save makes the folder), with nothing said. A pack is under
        // running_gear unless another part of the parts folder is named (the speed shop's wheels and mufflers)
        void Write(Model model, string pack, string area = "running_gear")
        {
            var folder = Path.Combine(parts, area, pack);
            if (PartFiles(folder).Count == 0) throw new FileNotFoundException($"{area}/{pack}: no part files name its models at {folder}; the folder was renamed or moved", folder);

            var path = Path.Combine(folder, model.Name + ".kn5");
            model.Save(path);
            Console.WriteLine($"{area}/{pack}/{model.Name}.kn5: {model.Triangles} triangles, {new FileInfo(path).Length / 1024} KB");
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
            Write(Parts.Disc4Piston(), "brakes_disc");

            Write(Parts.Shock("gabriel_hd", Finish.CastIron), "shocks_period");
            Write(Parts.Shock("koni", Finish.Red, collar: true), "shocks_period");
            Write(Parts.Shock("drag_90_10", Finish.Chrome, collar: true), "shocks_period");
            Write(Parts.Shock("drag_50_50", Finish.Blue, collar: true), "shocks_period");
            // The stock shocks' model (parts/models.json): a plain factory tube shock in grey paint
            Write(Parts.Shock("factory_shock", Finish.Grey), "shocks_period");

            foreach (var (id, inches) in Sized(root, "springs_torsion")) Write(Parts.TorsionBar(id, inches), "springs_torsion");
            foreach (var (id, inches) in Sized(root, "sway_bars")) Write(Parts.SwayBar(id, inches), "sway_bars");
            foreach (var id in new[] { "traction_master", "lakewood", "slapper", "ladder" }) Write(Parts.TractionBar(id), "springs_leaf");

            Write(BoltOns.CragarSs(), "speed_shop", "rims");
            Write(BoltOns.TorqThrust(), "speed_shop", "rims");
            Write(BoltOns.Glasspack(), "glasspacks", "exhaust");
            Write(BoltOns.CherryBomb(), "glasspacks", "exhaust");
        }
        catch (FileNotFoundException ex)
        {
            // The models written so far stand in folders that are there; the missing one is told and the run stops
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        return 0;
    }

    /// <summary>The part files in a folder (one .json each), by name; none for a folder that is not there</summary>
    private static List<string> PartFiles(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase).ToList() : [];

    /// <summary>
    /// A folder's parts that give their size in diameter_in: each one's own id and its diameter. A folder that is not
    /// there, or has no such part, throws as a missing pack.json did: the run would write none of its models and say nothing
    /// </summary>
    private static List<(string Id, float Inches)> Sized(string root, string pack)
    {
        var folder = Path.Combine(root, pack);
        var sized = new List<(string, float)>();
        foreach (var file in PartFiles(folder))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var part = json.RootElement;
            if (!part.TryGetProperty("specs", out var specs) || !specs.TryGetProperty("diameter_in", out var diameter)) continue;
            var id = part.GetProperty("id").GetString()!;
            sized.Add((id[(id.LastIndexOf('/') + 1)..], diameter.GetSingle()));
        }

        if (sized.Count == 0) throw new FileNotFoundException($"{pack}: no part files with a diameter_in at {folder}; the folder was renamed or moved", folder);
        return sized;
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
