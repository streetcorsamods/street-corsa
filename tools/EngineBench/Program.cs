using System.Globalization;

namespace StreetCorsa;

/// <summary>
/// Puts engine builds on the dyno from the command line:
/// lists them, shows one in detail, or checks all builds with a known power against the engine model.
/// Main and its switch are here; the modes are in the other parts of the class (EngineReports, AcDataCommands,
/// CarCommands), the catalog's loading in CatalogLoading.
/// </summary>
public static partial class Program
{
    private const int DefaultTuneCount = 10;

    /// <summary>
    /// The makes, groups, classes and fuels come from the game as it ships alone (the core), whatever
    /// mods the Content folder holds: the before-merge check (tools/check.ps1) runs the same on a clean clone that way
    /// </summary>
    private const string BaseOnlySwitch = "--base-only";

    /// <summary>Each command's usage line, by the command words it is for</summary>
    private static readonly (string[] Verbs, string Line)[] Usage =
    [
        (["list", "rated", "all", "inputs", "show"], "EngineBench <parts folder> list | rated | all | inputs | show <build id>"),
        (["snapshot"], "EngineBench <parts folder> snapshot <file>             every build's whole report, one JSON line each, to diff two versions by"),
        (["export"], "EngineBench <parts folder> export <build id> <car data folder> <output folder>"),
        (["cars"], "EngineBench <parts folder> cars <AC cars folder>       factory engine suggested for every car"),
        (["tune"], "EngineBench <parts folder> tune <build id> [count]     engines a used car of that build may turn up with"),
        (["bench"], "EngineBench <parts folder> bench <build id>            take every part off and find where it goes back"),
        (["renew"], "EngineBench <parts folder> renew <older parts folder>  engines saved with older parts, brought up to date"),
        (["gear"], "EngineBench <parts folder> gear <AC cars folder>       factory running gear chosen for every car"),
        (["axles"], "EngineBench <parts folder> axles <AC cars folder>      factory rear axle chosen for every car, against its own data"),
        (["prices"], "EngineBench <parts folder> prices [AC cars folder]     what the parts cost new by kind, and every car new"),
        (["prices"], "EngineBench <parts folder> prices <part id fragment>   the power and price of every part whose id holds it"),
        (["layout"], "EngineBench <parts folder> layout <AC cars folder>     where every car carries its engine, guessed from its data"),
        (["car"], "EngineBench <parts folder> car <AC car folder> <build id> [output folder]   every data file the car's parts change"),
        (["sound"], "EngineBench <parts folder> sound <AC car folder> <car id> [output folder]   the car's sound written for another car id"),
        (["sounds"], "EngineBench <parts folder> sounds <AC cars folder> [sounds folder]        the sound library, and the sound every build gets")
    ];

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: " + Usage[0].Line);
        Console.WriteLine("       A Content folder as the <parts folder> loads the parts of every mod in it, as the game does");
        Console.WriteLine("       --base-only anywhere: the makes, groups, classes and fuels of the core alone, not of the mods");
        foreach (var (_, line) in Usage.Skip(1)) Console.WriteLine("       " + line);
        Console.WriteLine("A <build id> is the exact id or a part of one; a part that more than one build's id holds is refused with a list of them");
        Console.WriteLine(@"export, car and sound never write into the car they read nor into an Assetto Corsa content\cars folder");
    }

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var baseOnly = args.Contains(BaseOnlySwitch);
        args = args.Where(a => a != BaseOnlySwitch).ToArray();

        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        // The one command that needs no parts: the catalog is not loaded for it
        if (args[1] == "layout" && args.Length > 2) return Layout(args[2]);

        var catalog = LoadCatalog(args[0], baseOnly: baseOnly);
        Console.WriteLine($"{catalog.Parts.Count} parts, {catalog.EngineBuilds.Count} engine builds");

        switch (args[1])
        {
            case "prices":
                return Prices(catalog, args.Length > 2 ? args[2] : null);

            case "list":
                foreach (var build in catalog.EngineBuilds) Console.WriteLine($"  {build.Id,-62} {build.Name}");
                return 0;

            // Both fail when a rated build no longer runs or is far off its rating (EngineBuild.RunsToItsRating; the tests'
            // EngineGoldenTests holds the content to the same before a merge)
            case "rated":
                return Table(catalog, catalog.EngineBuilds.Where(b => b.RatedPower != null));

            case "all":
                return Table(catalog, catalog.EngineBuilds);

            case "snapshot" when args.Length > 2:
                return Snapshot(catalog, args[2]);

            case "inputs":
                return Inputs(catalog, catalog.EngineBuilds.Where(b => b.RatedPower != null));

            case "show" when args.Length > 2:
                return Show(catalog, args[2]);

            case "export" when args.Length > 4:
                return Export(catalog, args[2], args[3], args[4]);

            case "cars" when args.Length > 2:
                return Cars(catalog, args[2]);

            case "tune" when args.Length > 2:
                var count = DefaultTuneCount;
                if (args.Length > 3 && (!int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1))
                {
                    Console.WriteLine($"tune takes a count of 1 or more, not '{args[3]}': EngineBench <parts folder> tune <build id> [count]");
                    return 1;
                }

                return Tune(catalog, args[2], count);

            case "bench" when args.Length > 2:
                return Bench(catalog, args[2]);

            case "renew" when args.Length > 2:
                return Renew(catalog, args[2]);

            case "gear" when args.Length > 2:
                return Gear(catalog, args[2]);

            case "axles" when args.Length > 2:
                return Axles(catalog, args[2]);

            case "car" when args.Length > 3:
                return Car(catalog, args[2], args[3], args.Length > 4 ? args[4] : null);

            case "sound" when args.Length > 3:
                return Sound(args[2], args[3], args.Length > 4 ? args[4] : null);

            case "sounds" when args.Length > 2:
                return Sounds(catalog, args[2], args.Length > 3 ? args[3] : Path.Combine(args[0], "..", "sounds"));

            default:
                // A known command short of its arguments is told its own usage; anything else the whole list
                var usage = Usage.Where(u => u.Verbs.Contains(args[1])).Select(u => u.Line).ToList();
                if (usage.Count == 0)
                {
                    Console.WriteLine($"Unknown command '{args[1]}'");
                    PrintUsage();
                    return 1;
                }

                foreach (var line in usage) Console.WriteLine("Usage: " + line);
                return 1;
        }
    }
}
