using System.Diagnostics;
using System.Globalization;
using System.Text;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Parts.Sounds;

namespace StreetCorsa;

/// <summary>
/// Puts engine builds of the converted parts on the dyno from the command line:
/// lists them, shows one in detail, or checks all builds with a known power against the engine model.
/// </summary>
public static class Program
{
    /// <summary>
    /// How far a rated build's modelled power may be off its rated figure before <c>rated</c>/<c>all</c> fail. The model
    /// is fitted to about 15% mean absolute error (the ratings mix SLRR dyno readings and factory figures); a build
    /// several times further off has lost a part in a conversion or the model broke
    /// </summary>
    private const double MaxRatedError = 0.5;

    private const int DefaultTuneCount = 10;

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: EngineBench <parts folder> list | rated | all | inputs | show <build id>");
            Console.WriteLine("       A Content folder as the <parts folder> loads the parts of every mod in it, as the game does");
            Console.WriteLine("       EngineBench <parts folder> export <build id> <car data folder> <output folder>");
            Console.WriteLine("       EngineBench <parts folder> cars <AC cars folder>       factory engine suggested for every car");
            Console.WriteLine("       EngineBench <parts folder> tune <build id> [count]     engines a used car of that build may turn up with");
            Console.WriteLine("       EngineBench <parts folder> bench <build id>            take every part off and find where it goes back");
            Console.WriteLine("       EngineBench <parts folder> renew <older parts folder>  engines saved with an older conversion, brought up to date");
            Console.WriteLine("       EngineBench <parts folder> gear <AC cars folder>       factory running gear chosen for every car");
            Console.WriteLine("       EngineBench <parts folder> axles <AC cars folder>      factory rear axle chosen for every car, against its own data");
            Console.WriteLine("       EngineBench <parts folder> prices [AC cars folder]     what the parts cost new by kind, and every car new");
            Console.WriteLine("       EngineBench <parts folder> prices <part id fragment>   the power and price of every part whose id holds it");
            Console.WriteLine("       EngineBench <parts folder> layout <AC cars folder>     where every car carries its engine, guessed from its data");
            Console.WriteLine("       EngineBench <parts folder> car <AC car folder> <build id> [output folder]   every data file the car's parts change");
            Console.WriteLine("       EngineBench <parts folder> sound <AC car folder> <car id> [output folder]   the car's sound written for another car id");
            Console.WriteLine("       EngineBench <parts folder> sounds <AC cars folder> [sounds folder]        the sound library, and the sound every build gets");
            Console.WriteLine("A <build id> is the exact id or a part of one; a part that more than one build's id holds is refused with a list of them");
            return 1;
        }

        // The one command that needs no parts: the catalog is not loaded for it
        if (args[1] == "layout" && args.Length > 2) return Layout(args[2]);

        var catalog = LoadCatalog(args[0]);
        TemporaryModConstants(catalog);
        Console.WriteLine($"{catalog.Parts.Count} parts, {catalog.EngineBuilds.Count} engine builds");

        switch (args[1])
        {
            case "prices":
                return Prices(catalog, args.Length > 2 ? args[2] : null);

            case "list":
                foreach (var build in catalog.EngineBuilds) Console.WriteLine($"  {build.Id,-62} {build.Name}");
                return 0;

            // Both fail when a rated build no longer runs or is far off its rating: the check after a converter run
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
                Console.WriteLine("Unknown command");
                return 1;
        }
    }

    private static int Table(PartsCatalog catalog, IEnumerable<EngineBuild> builds)
    {
        Console.WriteLine($"{"build",-48} {"rated",6} {"model",6} {"error",6}  {"@rpm",5} {"Nm",5} {"@rpm",5} {"litres",6} {"CR",5}  problem");
        var errors = new List<double>();
        // Rated builds that do not run, or run far off their rating (unrated ones that do not run are known and listed only)
        var failures = new List<string>();
        foreach (var build in builds)
        {
            var tree = PartTreeBuilder.BuildEngine(catalog, build);
            if (tree == null)
            {
                Console.WriteLine($"{Short(build.Id),-48} no engine block among its parts");
                if (build.RatedPower != null) failures.Add($"{build.Id}: no engine block among its parts");
                continue;
            }

            var report = EngineEvaluator.Evaluate(catalog, tree);
            var dyno = report.Dyno;
            var error = build.RatedPower is { } rated && dyno != null ? (dyno.MaxPowerHp - rated) / rated : (double?)null;
            if (error != null && report.Problem == null) errors.Add(error.Value);
            if (build.RatedPower != null)
            {
                if (report.Problem != null || dyno == null) failures.Add($"{build.Id}: {report.Problem ?? "no dyno run"}");
                else if (Math.Abs(error!.Value) > MaxRatedError) failures.Add($"{build.Id}: {error:+0%;-0%} off its rated {build.RatedPower:0} hp");
            }

            Console.WriteLine($"{Short(build.Id),-48} {build.RatedPower,6:0} {dyno?.MaxPowerHp,6:0} {error,6:+0%;-0%}  {dyno?.MaxPowerRpm,5:0} " +
                              $"{dyno?.MaxTorque,5:0} {dyno?.MaxTorqueRpm,5:0} {dyno?.Displacement * 1000,6:0.00} {dyno?.Compression,5:0.0}  " +
                              $"{report.Problem}{(tree.Unplaced.Count > 0 ? $" [{tree.Unplaced.Count} unplaced]" : "")}");
        }

        if (errors.Count > 0)
            Console.WriteLine($"\n{errors.Count} rated builds that run: mean error {errors.Average():+0.0%;-0.0%}, " +
                              $"mean absolute {errors.Average(Math.Abs):0.0%}, worst {errors.MaxBy(Math.Abs):+0.0%;-0.0%}");

        if (failures.Count == 0) return 0;

        Console.WriteLine($"\n{failures.Count} rated build(s) do not run or are more than {MaxRatedError:0%} off:");
        foreach (var failure in failures) Console.WriteLine("  " + failure);
        return 1;
    }

    // TEMPORARY while porting: SC_MOD_CONSTANTS=<json> of script path -> what the mod classes set at run time
    private static void TemporaryModConstants(PartsCatalog catalog)
    {
        var file = Environment.GetEnvironmentVariable("SC_MOD_CONSTANTS");
        if (string.IsNullOrEmpty(file)) return;
        var constants = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
        foreach (var part in catalog.Parts.Values)
        {
            if (part.SourceScript == null) continue;
            if (constants[part.SourceScript.Replace('\\', '/').Replace(".class", "")] is not Newtonsoft.Json.Linq.JObject c) continue;
            if (c["open_air_flow"] is { } air) part.Properties["open_air_flow"] = (double)air;
            if (c["drive_at_default"] != null && part.Properties.ContainsKey("default_rpm_turbo_mul")) part.Properties["rpm_turbo_mul"] = part.Number("default_rpm_turbo_mul");
        }
    }

    /// <summary>
    /// Every build's whole report, one JSON object a line, to compare the part logic of two versions of the game by:
    /// what feeds the dyno, the curve, and everything the car's data is given
    /// </summary>
    private static int Snapshot(PartsCatalog catalog, string file)
    {
        using var writer = new StreamWriter(file);
        foreach (var build in catalog.EngineBuilds.OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            var tree = PartTreeBuilder.BuildEngine(catalog, build);
            var report = tree == null ? null : EngineEvaluator.Evaluate(catalog, tree);
            var dyno = report?.Dyno;
            writer.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                build.Id,
                report?.Problem,
                report?.Inputs,
                report?.IdleRpm,
                report?.LimiterRpm,
                report?.Inertia,
                report?.GearRatios,
                report?.ReverseRatio,
                report?.FinalRatio,
                report?.DriveType,
                report?.DiffLock,
                report?.Friction,
                report?.ClutchCapacity,
                report?.Turbocharged,
                report?.Mass,
                Hp = dyno?.MaxPowerHp,
                HpRpm = dyno?.MaxPowerRpm,
                Nm = dyno?.MaxTorque,
                NmRpm = dyno?.MaxTorqueRpm,
                dyno?.Displacement,
                dyno?.Compression,
                Curve = dyno?.Curve.Select(p => new[] { p.Rpm, Math.Round(p.Torque, 3) }),
                Unplaced = tree?.Unplaced.Select(p => p.Id)
            }));
        }

        return 0;
    }

    /// <summary>What the scripts feed the engine model, side by side</summary>
    private static int Inputs(PartsCatalog catalog, IEnumerable<EngineBuild> builds)
    {
        // BENCH_JSON=<file>: one JSON object per build, for fitting the engine model outside. A run's file holds that
        // run's builds only: an earlier run's lines would be fitted twice
        var jsonFile = Environment.GetEnvironmentVariable("BENCH_JSON");
        if (!string.IsNullOrEmpty(jsonFile)) File.WriteAllText(jsonFile, string.Empty);

        Console.WriteLine($"{"build",-30} {"hp",4} {"inMax",6} {"inMin",6} {"exMax",6} {"inDur",5} {"exDur",5} {"inOpn",5} {"exOpn",5} {"lead0",5} {"lead1",5} {"burn ms",7} {"Tloss",5} {"A/F",5} {"H MJ",5} {"air",6} {"fuel",6} {"maxRpm",6} {"boost",5}");
        foreach (var build in builds)
        {
            var tree = PartTreeBuilder.BuildEngine(catalog, build);
            var i = tree == null ? null : EngineEvaluator.Evaluate(catalog, tree).Inputs;
            if (i == null) continue;

            if (!string.IsNullOrEmpty(jsonFile))
                File.AppendAllText(jsonFile, Newtonsoft.Json.JsonConvert.SerializeObject(new { build.Id, build.RatedPower, Inputs = i }) + Environment.NewLine);

            double Span(double open, double close) => (close - open < 0 ? close - open + 1 : close - open) * 720;
            Console.WriteLine($"{Short(build.Id)[^Math.Min(30, Short(build.Id).Length)..],-30} {build.RatedPower,4:0} {i.IntakeMax,6:0.000} {i.IntakeMin,6:0.000} {i.ExhaustMax,6:0.000} " +
                              $"{Span(i.IntakeOpen, i.IntakeClose),5:0} {Span(i.ExhaustOpen, i.ExhaustClose),5:0} {i.IntakeOpen * 720,5:0} {i.ExhaustOpen * 720,5:0} " +
                              $"{(0.5 - i.SparkMin) * 720,5:0} {(0.5 - i.SparkMin - i.SparkInc) * 720,5:0} {i.BurnTime * 1000,7:0.000} {i.HeatLoss,5:0} {i.MixtureRatio,5:0.0} " +
                              $"{i.MixtureHeat / 1e6,5:0.00} {i.MaxAirFlow,6:0.000} {i.MaxFuelFlow,6:0.000} {i.MaxRpm,6:0} {i.BoostMax,5:0.00}");
        }

        return 0;
    }

    private static int Show(PartsCatalog catalog, string id)
    {
        var build = FindBuild(catalog.EngineBuilds, b => b.Id, id);
        if (build == null) return 1;

        var tree = PartTreeBuilder.BuildEngine(catalog, build);
        if (tree == null)
        {
            Console.WriteLine("No engine block among its parts");
            return 1;
        }

        Console.WriteLine($"\n{build.Name}  ({build.Id})");
        Print(tree.Root, 0);
        foreach (var part in tree.Unplaced) Console.WriteLine($"  UNPLACED {part.Id}");
        foreach (var missing in tree.Missing) Console.WriteLine($"  MISSING  {missing}");

        var report = EngineEvaluator.Evaluate(catalog, tree);
        Console.WriteLine($"\nRuns: {report.Runs}{(report.Problem != null ? " - " + report.Problem : "")}");
        Console.WriteLine($"Inputs: {report.Inputs}");
        Console.WriteLine($"Idle {report.IdleRpm:0} rpm, limiter {report.LimiterRpm:0} rpm, inertia {report.Inertia:0.000} kg m2, " +
                          $"mass {report.Mass:0} kg, value ${tree.Root.SelfAndDescendants().Sum(p => PartPricing.NewPrice(p.Definition)):0}, friction {report.Friction:0.000000}, clutch {report.ClutchCapacity:0}{(report.Turbocharged ? ", turbo" : "")}");
        Console.WriteLine($"Gears {string.Join(" / ", report.GearRatios.Select(r => r.ToString("0.00")))}  reverse {report.ReverseRatio:0.00}  " +
                          $"final {report.FinalRatio:0.00}  drive {report.DriveType}  lock {report.DiffLock:0.00}");

        if (report.Dyno is { } dyno)
        {
            Console.WriteLine($"{dyno.Displacement * 1000:0.00} l, {dyno.Compression:0.0}:1, {dyno.MaxPowerHp:0} hp @ {dyno.MaxPowerRpm:0}, " +
                              $"{dyno.MaxTorque:0} Nm @ {dyno.MaxTorqueRpm:0}{(build.RatedPower != null ? $"  (rated {build.RatedPower:0} hp)" : "")}");
            foreach (var (rpm, torque) in dyno.Curve.Where(p => p.Rpm % 500 == 0))
                Console.WriteLine($"  {rpm,6:0} rpm {torque,6:0} Nm {dyno.PowerHpAt(rpm),6:0} hp  {new string('#', (int)(torque / 12))}");
        }

        return 0;
    }

    /// <summary>Writes the Assetto Corsa data files a build changes, made from an unpacked car data folder</summary>
    private static int Export(PartsCatalog catalog, string id, string carData, string output)
    {
        var build = FindBuild(catalog.EngineBuilds, b => b.Id, id);
        if (build == null) return 1;

        var tree = PartTreeBuilder.BuildEngine(catalog, build);
        if (tree == null)
        {
            Console.WriteLine($"No usable build '{id}'");
            return 1;
        }

        var report = EngineEvaluator.Evaluate(catalog, tree);
        if (!report.Runs)
        {
            Console.WriteLine($"{build.Name} does not run: {report.Problem}");
            return 1;
        }

        Dictionary<string, string> files;
        try
        {
            // Read and written in Latin-1, as the game does: AC's files are ANSI, and UTF-8 would mangle a mod's accents
            files = AcEngineData.Generate(report, AcCarDataReader.ForFolder(carData));
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine($"{ex.Message}: {carData} is not the data folder of a car");
            return 1;
        }

        Directory.CreateDirectory(output);
        foreach (var (name, content) in files) File.WriteAllText(Path.Combine(output, name), content, Encoding.Latin1);

        Console.WriteLine($"{build.Name}: {report.Dyno!.MaxPowerHp:0} hp, {report.GearRatios.Count} gears -> {string.Join(", ", files.Keys)} in {output}");
        return 0;
    }

    /// <summary>The engine each Assetto Corsa car would get, with the runners-up</summary>
    private static int Cars(PartsCatalog catalog, string carsFolder)
    {
        var index = EngineBuildIndex.Create(catalog);
        Console.WriteLine($"{index.Runnable.Count} builds run\n");

        foreach (var folder in AcCarFolder.InstalledCars(carsFolder))
        {
            if (!File.Exists(AcCarUi.PathIn(folder))) continue;

            // Mod cars often ship a ui_car.json that is not quite JSON: the car is reported and the run goes on
            try
            {
                var ui = AcCarUi.TryRead(folder, out var error);
                if (ui == null)
                {
                    Console.WriteLine($"{Path.GetFileName(folder)}: {error}");
                    continue;
                }

                var brand = AcCarUi.GetString(ui, "brand") ?? "";
                var name = AcCarUi.GetString(ui, "name") ?? "";
                var bhp = AcSpecs.ParsePower(AcCarUi.GetString(AcCarUi.GetObject(ui, "specs"), "bhp"));

                Console.WriteLine($"{Path.GetFileName(folder)}: {brand} | {name} | {bhp:0} bhp  (family '{CarBook.Current.FamilyOfCar(Path.GetFileName(folder), brand, name)}')");
                foreach (var match in StockEngineMatcher.Rank(index, Path.GetFileName(folder), brand, name, bhp).Take(4))
                    Console.WriteLine($"    {match.Score,6:0.00}  {match.Build.PowerHp,4:0} hp {match.Build.Litres,5:0.00} l  [{match.Build.Family,-6}] {match.Build.Build.Name}  ({match.Build.Build.Id})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{Path.GetFileName(folder)}: {ex.Message}");
            }
        }

        return 0;
    }

    /// <summary>
    /// The parts of one folder, or of every mod when the folder is a Content folder (no engine builds of its own, a
    /// base game among its folders): mod builds and parts are benched as the game loads them
    /// </summary>
    /// <param name="useContent">
    /// Whether the folder's makes and part groups become the ones in use (process-wide): false for a second catalog
    /// (renew's old one), which must not change the rules the first is worked by
    /// </param>
    /// <summary>
    /// What the parts cost new, kind by kind (the dyno's price book), and what every installed car costs new: its body by
    /// its weight, its factory engine and running gear
    /// </summary>
    private static int Prices(PartsCatalog catalog, string? carsFolder)
    {
        var watch = Stopwatch.StartNew();
        var index = EngineBuildIndex.Create(catalog);
        var book = PartPriceBook.Create(catalog, index);
        PartPricing.Book = () => book;
        Console.WriteLine($"{index.Runnable.Count} builds run, {book.Measured} parts measured on the dyno in {watch.ElapsedMilliseconds} ms\n");

        Console.WriteLine($"  {"kind",-18} {"parts",5} {"least",8} {"middle",8} {"most",8}   dearest");
        foreach (var kind in catalog.Parts.Values.GroupBy(PartKinds.GroupOf).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var priced = kind.Select(p => (Part: p, Price: PartPricing.NewPrice(p))).OrderBy(p => p.Price).ToList();
            var dearest = priced[^1];
            Console.WriteLine($"  {kind.Key,-18} {priced.Count,5} {priced[0].Price,8:0} {priced[priced.Count / 2].Price,8:0} {dearest.Price,8:0}   " +
                              $"{dearest.Part.Id} ({book.PowerOf(dearest.Part.Id) ?? 0:0} hp)");
        }

        if (carsFolder == null) return 0;

        // Not a folder: the parts whose id holds it, each with its power and price
        if (!Directory.Exists(carsFolder))
        {
            Console.WriteLine();
            foreach (var part in catalog.Parts.Values.Where(p => p.Id.Contains(carsFolder, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Id))
                Console.WriteLine($"  {part.Id,-60} {PartKinds.GroupOf(part),-18} {book.PowerOf(part.Id) ?? 0,6:0.0} hp  ${PartPricing.NewPrice(part):0}");
            return 0;
        }

        Console.WriteLine($"\n  {"car",-36} {"kg",5} {"hp",4} {"hp/t",5} {"body",7} {"engine",7} {"gear",6} {"new",7}   factory engine");
        var rows = new List<(string Line, double Total)>();
        foreach (var (folder, specs) in InstalledCarSpecs(carsFolder))
        {
            var carId = Path.GetFileName(folder);
            var build = index.Get(CarBook.Current.Car(carId)?.Engine) ?? FirstMatch(index, folder, carId);
            var (facts, picks) = (FactsOf(folder), CarBook.Current.Car(carId)?.RunningGear);
            var axle = RunningGearGuess.ChooseAxle(catalog, specs, facts, picks);
            var gear = RunningGearFactory.Create(catalog, specs, 1, axle, facts, picks).SelectMany(p => p.SelfAndDescendants())
                .Select(p => catalog.Get(p.DefinitionId)).OfType<PartDefinition>();
            var car = CarPricing.New(catalog, specs.TotalMass, build, gear);
            var power = build?.PowerHp ?? 0;
            rows.Add(($"  {carId,-36} {specs.TotalMass,5:0} {power,4:0} {power * 1000 / specs.TotalMass,5:0} {car.Body,7:0} {car.Engine,7:0} {car.RunningGear,6:0} {car.Total,7:0}   {build?.Build.Name ?? "none"}", car.Total));
        }

        foreach (var (line, _) in rows.OrderBy(r => r.Total)) Console.WriteLine(line);
        return 0;
    }

    /// <summary>The build the engine matcher ranks first for a car without one in cars.json</summary>
    private static RatedBuild? FirstMatch(EngineBuildIndex index, string folder, string carId)
    {
        var ui = AcCarUi.TryRead(folder, out _);
        if (ui == null) return null;

        var brand = AcCarUi.GetString(ui, "brand") ?? "";
        var name = AcCarUi.GetString(ui, "name") ?? "";
        var bhp = AcSpecs.ParsePower(AcCarUi.GetString(AcCarUi.GetObject(ui, "specs"), "bhp"));
        return StockEngineMatcher.Rank(index, carId, brand, name, bhp).FirstOrDefault()?.Build;
    }

    private static PartsCatalog LoadCatalog(string folder, bool useContent = true)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!File.Exists(Path.Combine(folder, EngineBuild.FileName)) && Directory.Exists(Path.Combine(folder, Mods.ModIds.BaseId)))
        {
            var mods = Mods.ModSet.Load(folder);
            if (useContent) UseContent(mods);
            return PartsCatalog.Load(mods.Folders(Mods.ContentTypes.Parts).ToList());
        }

        // A parts folder of a Content folder (a mod's, the base game's): the makes and the part groups of that Content
        // folder's mods. One outside any (a git archive, a scratch run): the source tree's, with the folder's own
        // groups over them
        if (useContent)
        {
            var content = Directory.GetParent(folder)?.Parent;
            if (content != null && Directory.Exists(Path.Combine(content.FullName, Mods.ModIds.BaseId))) UseContent(Mods.ModSet.Load(content.FullName));
            else UseContent(SourceContent(), folder);
        }

        return PartsCatalog.Load(folder);
    }

    /// <summary>The source tree's Content folder, above the program; null when it does not run from a build of the source</summary>
    private static Mods.ModSet? SourceContent()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var content = Path.Combine(dir.FullName, "Street Corsa", Mods.ModSet.FolderName);
            if (Directory.Exists(Path.Combine(content, Mods.ModIds.BaseId))) return Mods.ModSet.Load(content);
        }

        return null;
    }

    /// <summary>The makes (which family a car or an engine is) and the part groups of the mods, as the game reads them</summary>
    /// <param name="partsFolder">A parts folder whose own groups go over the mods'</param>
    private static void UseContent(Mods.ModSet? mods, string? partsFolder = null)
    {
        if (mods == null) Console.WriteLine("content: no Content folder found for the makes and part groups; the folder's own groups only");
        CarBook.Current = mods == null ? CarBook.Empty : CarBook.Load(mods);
        var folders = mods?.Folders(Mods.ContentTypes.Parts).Select(f => f.Dir).ToList() ?? new List<string>();
        if (partsFolder != null) folders.Add(partsFolder);
        PartGroups.Current = PartGroups.Load(folders);
        foreach (var problem in CarBook.Current.Problems.Concat(PartGroups.Current.Problems)) Console.WriteLine($"content: {problem}");
    }

    private static int Tune(PartsCatalog catalog, string id, int count)
    {
        var index = EngineBuildIndex.Create(catalog);
        var stock = FindBuild(index.Runnable, b => b.Build.Id, id, " that runs");
        if (stock == null) return 1;

        Console.WriteLine($"{stock.Build.Name}: {stock.PowerHp:0} hp, family '{stock.Family}'\n");
        var random = new Random(1);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var level = random.NextDouble();
            var engine = EngineFactory.CreateTuned(catalog, index, stock, 0.8, level, random);
            if (engine == null) continue;

            Console.WriteLine($"  level {level:0.00}: {engine.Report.Dyno?.MaxPowerHp,4:0} hp  ${PartPricing.WorthOfAssembly(catalog, engine.Root),6:0}  " +
                              $"{(engine.IsModified ? string.Join(", ", engine.Changes) : "stock")}{(engine.Report.Runs ? "" : "  DOES NOT RUN: " + engine.Report.Problem)}");
        }

        Console.WriteLine($"\n{stopwatch.Elapsed.TotalMilliseconds / count:0} ms per engine");
        return 0;
    }

    /// <summary>Every part of a build comes off and has to find its way back to where it was</summary>
    private static int Bench(PartsCatalog catalog, string id)
    {
        var index = EngineBuildIndex.Create(catalog);
        var stock = FindBuild(index.Runnable, b => b.Build.Id, id, " that runs");
        if (stock == null) return 1;

        var engine = EngineFactory.CreateStock(catalog, stock, 1.0, new Random(1));
        if (engine == null)
        {
            Console.WriteLine($"No build '{id}' that runs");
            return 1;
        }

        var car = new List<PartInstance> { engine.Root };
        var failures = 0;
        foreach (var part in engine.Root.SelfAndDescendants().Skip(1).ToList())
        {
            var parent = PartTrees.FindParent(engine.Root, part)!;
            var (slot, own) = (part.ParentSlot, part.OwnSlot);

            Workbench.Remove(car, part);
            var without = EngineFactory.Evaluate(catalog, engine.Root);
            var places = Workbench.FindPlaces(catalog, car, part);
            var back = places.FirstOrDefault(p => ReferenceEquals(p.Parent, parent) && p.ParentSlot == slot);
            var definition = catalog.Get(part.DefinitionId)!;
            var parentDefinition = catalog.Get(parent.DefinitionId)!;
            var alternatives = catalog.FindMountable(parentDefinition, parentDefinition.Slots.First(s => s.Id == slot));

            Console.WriteLine($"  {(back == null ? "LOST" : "ok  ")} {definition.DisplayName ?? definition.Id,-46} {places.Count} place(s), " +
                              $"{alternatives.Count} part(s) fit its slot{(alternatives.Any(a => a.Part == definition) ? "" : "  (not itself!)")}");
            Console.WriteLine($"         without it: {(without is { Runs: true } ? $"{without.Dyno!.MaxPowerHp:0} hp" : without?.Problem)}  (CR {without?.Dyno?.Compression:0.0})");
            if (back == null) failures++;

            Workbench.Mount(car, back ?? new MountPlace(parent, slot, own), part);
        }

        var after = EngineFactory.Evaluate(catalog, engine.Root);
        Console.WriteLine($"\n{failures} part(s) could not go back; engine makes {after?.Dyno?.MaxPowerHp:0} hp (was {engine.Report.Dyno?.MaxPowerHp:0})");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// What a save made with an older conversion goes through: every engine of the old catalog that holds parts
    /// the current one knows by another id is brought up to date and has to run again with all its parts on.
    /// </summary>
    private static int Renew(PartsCatalog catalog, string oldPartsFolder)
    {
        // The old catalog is worked by the new one's makes and groups: it is the new game that loads the old save
        var old = LoadCatalog(oldPartsFolder, useContent: false);
        var failures = 0;
        var renewed = 0;

        foreach (var stock in EngineBuildIndex.Create(old).Runnable)
        {
            var engine = EngineFactory.CreateStock(old, stock, 1.0, new Random(1));
            if (engine == null) continue;

            var parts = engine.Root.SelfAndDescendants().Count();
            var loose = new List<PartInstance>();
            if (!SavedParts.BringUpToDate(catalog, engine.Root, loose)) continue;

            renewed++;
            var gone = engine.Root.SelfAndDescendants().Concat(loose.SelectMany(l => l.SelfAndDescendants())).Count(p => catalog.Get(p.DefinitionId) == null);
            var after = EngineFactory.Evaluate(catalog, engine.Root);
            var runs = after is { Runs: true };
            if (!runs || gone > 0) failures++;

            Console.WriteLine($"  {(runs && gone == 0 ? "ok  " : "FAIL")} {Short(stock.Build.Id),-48} {stock.PowerHp,4:0} hp -> " +
                              $"{(runs ? $"{after!.Dyno!.MaxPowerHp,4:0} hp" : after?.Problem ?? "no engine")}  {parts} parts, {loose.Count} came off, {gone} unknown");
            foreach (var part in loose) Console.WriteLine($"         off: {catalog.Get(part.DefinitionId)?.DisplayName ?? part.DefinitionId}");
        }

        Console.WriteLine($"\n{renewed} engine(s) brought up to date, {failures} with problems");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>What the game knows of a car for the parts' came_in: its make, family, country (the make's, else its ui's) and year</summary>
    private static CarFacts FactsOf(string folder)
    {
        var id = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var ui = AcCarUi.TryRead(folder, out _);
        var (brand, name) = (AcCarUi.GetString(ui, "brand"), AcCarUi.GetString(ui, "name"));
        var book = CarBook.Current;
        var make = book.MakeOf(id, brand, name);
        var country = book.CountryOfMake(make) ?? (AcCarUi.GetString(ui, "country") is { Length: > 0 } named ? book.Country(named) : null);
        return new CarFacts(make, book.FamilyOfCar(id, brand, name), AcCarUi.GetInt(ui, "year"), country);
    }

    /// <summary>Every installed car with a ui_car.json and the data it reads; one line for each car whose data does not read</summary>
    private static IEnumerable<(string Folder, AcCarSpecs Specs)> InstalledCarSpecs(string carsFolder)
    {
        foreach (var folder in AcCarFolder.InstalledCars(carsFolder))
        {
            if (!File.Exists(Path.Combine(folder, "ui", "ui_car.json"))) continue;

            AcCarSpecs specs;
            try { specs = AcCarSpecs.Read(AcCarDataReader.ForCar(folder)); }
            catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(folder)}: {ex.Message}"); continue; }

            yield return (folder, specs);
        }
    }

    /// <summary>Where every car carries its engine, as guessed from its weight and the wheels it drives</summary>
    private static int Layout(string carsFolder)
    {
        foreach (var (folder, specs) in InstalledCarSpecs(carsFolder))
        {
            Console.WriteLine($"{EngineLayouts.Guess(specs),-5}  {specs.FrontWeightShare,5:0.000} front  {specs.Traction ?? "?",-4}  {Path.GetFileName(folder)}");
        }

        return 0;
    }

    /// <summary>The running gear every car leaves the factory with, matched to its own data</summary>
    private static int Gear(PartsCatalog catalog, string carsFolder)
    {
        foreach (var (folder, specs) in InstalledCarSpecs(carsFolder))
        {
            var facts = FactsOf(folder);
            Console.WriteLine($"{Path.GetFileName(folder)}: {facts.Make ?? "?"} {facts.Year} ({facts.Country ?? "?"}), {specs.TotalMass:0} kg, {specs.FrontWeightShare:0%} front");
            if (RunningGearFactory.Choose(catalog, specs, facts, CarBook.Current.Car(Path.GetFileName(folder))?.RunningGear) is not var (front, rear))
            {
                Console.WriteLine("    no running gear in the catalog");
                continue;
            }

            foreach (var (axle, parts, name) in new[] { (specs.Front, front, "front"), (specs.Rear, rear, "rear") })
            {
                Console.WriteLine($"  {name}: {axle.SuspensionType ?? "?"}, tyre {axle.TyreWidth * 1000:0}/{axle.TyreRadius:0.000}/{axle.RimRadius:0.000} m, brake {axle.BrakeTorque:0} Nm, spring {axle.SpringRate:0} N/m, damp {axle.DampBump:0}/{axle.DampRebound:0}, sway bar {axle.SwayBarRate:0} N/m, {axle.CornerLoad:0} kg/wheel");
                Console.WriteLine($"    tyre   {RunningGear.TyreWidth(parts.Tyre) * 1000:0}/{RunningGear.TyreRadius(parts.Tyre):0.000}/{RunningGear.TyreRimRadius(parts.Tyre):0.000} m  grip {RunningGear.TyreGrip(parts.Tyre):0.00}  {parts.Tyre.Id}");
                Console.WriteLine($"    rim    {RunningGear.RimWidth(parts.Rim):0.0}\" offset {RunningGear.RimOffset(parts.Rim) * 1000:0} mm, {parts.Rim.Mass:0.0} kg  {parts.Rim.Id}");
                Console.WriteLine($"    brake  {parts.BrakeTorque:0} Nm  {parts.Brake.DisplayName ?? parts.Brake.Id}" +
                                  (parts.Shoes == null ? "" : $", {parts.Shoes.DisplayName}, {parts.Cylinder?.DisplayName}") +
                                  (RunningGear.Heat(parts.Brake, parts.Shoes) is { } heat ? $"  fade {heat.TorqueK:0.00} C/kJ {heat.Curve}" : "  no fade of its own"));
                Console.WriteLine($"    spring {RunningGear.SpringRate(parts.Spring):0} N/m for {RunningGear.SpringDesignLoad(parts.Spring):0} kg  {parts.Spring.DisplayName ?? parts.Spring.Id}");
                Console.WriteLine($"    shock  {RunningGear.Damping(parts.Shock).Bump:0}/{RunningGear.Damping(parts.Shock).Rebound:0}  {parts.Shock.DisplayName ?? parts.Shock.Id}");
                Console.WriteLine(parts.SwayBar is { } bar ? $"    bar    {RunningGear.SwayBarRate(bar):0} N/m  {bar.DisplayName ?? bar.Id}" : "    bar    none");
            }
        }

        return 0;
    }

    /// <summary>
    /// The rear axle every car leaves the factory with (cars.json's picks, else the guess from the parts' "came_in"),
    /// beside what its own data says: the rear suspension, the final drive, the differential and the engine's peak torque
    /// </summary>
    private static int Axles(PartsCatalog catalog, string carsFolder)
    {
        var housings = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (folder, specs) in InstalledCarSpecs(carsFolder))
        {
            // The game's cars, when the mods name any
            var id = Path.GetFileName(folder);
            if (CarBook.Current.Cars.Count > 0 && CarBook.Current.Car(id) == null) continue;
            var ui = AcCarUi.TryRead(folder, out _);
            var (brand, name, year) = (AcCarUi.GetString(ui, "brand"), AcCarUi.GetString(ui, "name"), AcCarUi.GetInt(ui, "year"));
            var facts = FactsOf(folder);
            var (power, coast, preload) = specs.Differential;
            Console.WriteLine($"{id}: {brand} {name} {year}, make {facts.Make ?? "?"} ({facts.Family}), {specs.Traction ?? "?"}, rear {specs.Rear.SuspensionType ?? "?"}, " +
                              $"{specs.PeakTorque:0} Nm, final {specs.FinalRatio:0.00}, diff {power:0.00}/{coast:0.00}/{preload:0}, brake temps {(specs.Front.HasBrakeTemps ? "front" : "-")}/{(specs.Rear.HasBrakeTemps ? "rear" : "-")}");

            var picks = CarBook.Current.Car(id)?.RunningGear;
            if (RunningGearGuess.ChooseAxle(catalog, specs, facts, picks) is not { } axle)
            {
                Console.WriteLine(specs.DrivesRear ? "    NO AXLE in the catalog for it" : "    no rear axle: the rear wheels are not driven");
                continue;
            }

            var (lockPower, lockCoast, lockPreload) = RearAxle.Lock(axle.Carrier);
            Console.WriteLine($"    {axle.Housing.DisplayName}{(picks?.Axle != null ? " (picked)" : "")}, {RearAxle.Strength(axle.Housing):0} Nm");
            Console.WriteLine($"    {axle.Carrier.DisplayName}, {lockPower:0.00}/{lockCoast:0.00}/{lockPreload:0}; {axle.Gears.DisplayName}; {axle.Shaft.DisplayName}");
            housings[axle.Housing.DisplayName ?? axle.Housing.Id] = housings.GetValueOrDefault(axle.Housing.DisplayName ?? axle.Housing.Id) + 1;
        }

        Console.WriteLine();
        foreach (var (housing, count) in housings.OrderByDescending(h => h.Value)) Console.WriteLine($"{count,4}  {housing}");
        return 0;
    }

    /// <summary>
    /// A donor car's sound as the overlay would write it under another car's id: the GUID lines the bank answers,
    /// and which engine events the donor lacks. The master GUIDs next to the cars folder serve a Kunos donor.
    /// </summary>
    private static int Sound(string donorFolder, string carId, string? output)
    {
        var master = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(donorFolder)) ?? "", "..", "sfx", AcCarSound.GuidsFileName);
        var sound = AcCarSound.FromCar(donorFolder, Path.GetFullPath(master));
        if (sound == null)
        {
            Console.WriteLine($"{donorFolder} has no bank of its own, or no GUIDs to go with it");
            return 1;
        }

        var guids = sound.GuidsFor(carId);
        var lines = guids.Split('\n').Count(l => l.Contains('}'));
        Console.WriteLine($"{sound.DonorId}: {new FileInfo(sound.BankPath).Length / 1e6:0.0} MB bank, {lines} GUID line(s) the bank answers, for {carId}");
        foreach (var line in guids.Split('\n').Where(l => l.Contains("event:/cars/") || l.Contains("bank:/"))) Console.WriteLine("  " + line.TrimEnd());
        var missing = AcCarSound.MissingEngineEvents(guids, carId);
        if (missing.Count > 0) Console.WriteLine($"  MISSING {string.Join(", ", missing)}");

        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, AcCarSound.GuidsFileName), guids);
            Console.WriteLine($"  written to {Path.Combine(output, AcCarSound.GuidsFileName)}");
        }

        return missing.Count == 0 ? 0 : 2;
    }

    /// <summary>
    /// The sound library as the game sees it: the curated sounds, the banks harvested off the installed cars
    /// (one line per distinct bank, with its carriers), then the sound every runnable build would race with.
    /// </summary>
    private static int Sounds(PartsCatalog catalog, string carsFolder, string soundsFolder)
    {
        var index = EngineBuildIndex.Create(catalog);
        var library = SoundLibrary.Load(Path.GetFullPath(soundsFolder));
        var master = Path.GetFullPath(Path.Combine(carsFolder, "..", "sfx", AcCarSound.GuidsFileName));
        var stopwatch = Stopwatch.StartNew();
        var cars = library.Harvest(carsFolder, master, SoundLibrary.StockFacts(index, catalog));
        Console.WriteLine($"{library.Curated.Count} curated sound(s) under {library.Root}, {library.Harvested.Count} harvested off {cars} car(s) in {stopwatch.Elapsed.TotalMilliseconds:0} ms\n");
        foreach (var problem in library.Problems) Console.WriteLine("  left out: " + problem);
        if (library.Problems.Count > 0) Console.WriteLine();

        Console.WriteLine($"{"sound",-44} {"MB",5} {"cyl",3} {"family",-6} {"rpm",5} {"cars",4}  name / carriers");
        foreach (var sound in library.All.OrderByDescending(e => e.Curated).ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{Short(sound.Id),-44} {sound.Size / 1e6,5:0} {sound.Cylinders,3} {sound.Family,-6} {sound.RpmMax,5:0} {sound.Carriers.Count,4}  {sound.Name}" +
                              (sound.Carriers.Count > 1 ? $"  [{string.Join(", ", sound.Carriers.Take(4))}{(sound.Carriers.Count > 4 ? ", ..." : "")}]" : ""));
        }

        Console.WriteLine($"\n{"build",-48} {"cyl",3} {"family",-6} {"limit",5}  sound: reason");
        foreach (var build in index.Runnable)
        {
            var tree = PartTreeBuilder.BuildEngine(catalog, build.Build);
            if (tree == null) continue;
            var report = EngineEvaluator.Evaluate(catalog, tree);
            var request = SoundMatcher.RequestFor(catalog, tree.Root.Definition.Id, report);
            var choice = SoundMatcher.Choose(library, request);
            Console.WriteLine($"{Short(build.Build.Id),-48} {request.Cylinders,3} {request.Family,-6} {request.LimiterRpm,5:0}  " +
                              (choice == null ? "none" : $"{choice.Sound.Name} ({Short(choice.Sound.Id)}) [{choice.Score:0.0}]: {choice.Reason}"));
        }

        return 0;
    }

    /// <summary>Every data file a car's parts change: the build as its engine, the factory running gear as its wheels</summary>
    private static int Car(PartsCatalog catalog, string carFolder, string id, string? output)
    {
        var build = FindBuild(catalog.EngineBuilds, b => b.Id, id);
        if (build == null) return 1;

        var tree = PartTreeBuilder.BuildEngine(catalog, build);
        if (tree == null)
        {
            Console.WriteLine($"No usable build '{id}'");
            return 1;
        }

        var readFile = AcCarDataReader.ForCar(carFolder);
        var specs = AcCarSpecs.Read(readFile);
        var (facts, picks) = (FactsOf(carFolder), CarBook.Current.Car(Path.GetFileName(Path.TrimEndingDirectorySeparator(carFolder)))?.RunningGear);
        var factory = RunningGearFactory.Choose(catalog, specs, facts, picks);
        var gear = RunningGearFactory.Create(catalog, specs, 1, null, facts, picks);
        var mounted = RunningGear.Mounted(gear, catalog);

        var report = EngineEvaluator.Evaluate(catalog, tree);
        var result = AcCarBuild.Generate(catalog, new CarBuild
        {
            Engine = report,
            FactoryEngineMass = report.Mass,
            RunningGear = mounted,
            SwayBars = RunningGear.SwayBars(gear),
            FactoryRunningGear = factory
        }, readFile);

        Console.WriteLine($"{build.Name}: {report.Dyno?.MaxPowerHp:0} hp -> {string.Join(", ", result.Files.Keys)}");
        foreach (var problem in result.Problems) Console.WriteLine($"  PROBLEM {problem}");

        // The sound the car would race with, from the library next to the parts and the cars next to this one
        var carsFolder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(carFolder)) ?? carFolder;
        var library = SoundLibrary.Load(Path.GetFullPath(Path.Combine(catalog.Root, "..", "sounds")));
        library.Harvest(carsFolder, Path.GetFullPath(Path.Combine(carsFolder, "..", "sfx", AcCarSound.GuidsFileName)), SoundLibrary.StockFacts(EngineBuildIndex.Create(catalog), catalog));
        var carId = Path.GetFileName(Path.TrimEndingDirectorySeparator(carFolder));
        var sound = SoundMatcher.ForCar(library, SoundMatcher.RequestFor(catalog, tree.Root.Definition.Id, report), carId, out var choice);
        if (choice != null) Console.WriteLine($"  sound: {choice.Sound.Name} ({choice.Sound.Id}): {choice.Reason}{(sound == null ? " (the car's own bank)" : "")}");

        // Only what changed, line by line
        foreach (var (name, content) in result.Files)
        {
            var before = (readFile(name) ?? "").Replace("\r\n", "\n").Split('\n');
            var after = content.Replace("\r\n", "\n").Split('\n');
            var changed = after.Where((line, i) => i >= before.Length || before[i] != line).Count(l => l.Length > 0);
            Console.WriteLine($"  {name}: {changed} line(s) differ");
        }

        if (output != null)
        {
            Directory.CreateDirectory(output);
            foreach (var (name, content) in result.Files) File.WriteAllText(Path.Combine(output, name), content, Encoding.Latin1);
        }

        return result.CanDrive ? 0 : 1;
    }

    private static void Print(InstalledPart part, int depth)
    {
        Console.WriteLine($"  {new string(' ', depth * 2)}{(depth == 0 ? "" : $"[{part.ParentSlot}] ")}{part}");
        foreach (var child in part.Children.OrderBy(c => c.Key)) Print(child.Value, depth + 1);
    }

    private static string Short(string id) => id.Length > 48 ? id[^48..] : id;

    /// <summary>
    /// The build a command names, the same way for every command: the build of exactly that id, else the one build
    /// whose id contains it. Several that contain it is an error listing them: a short id must not bench one build
    /// and show another. Null (with the reason printed) when there is none or no single one.
    /// </summary>
    /// <param name="what">Said of the candidates when none matches, e.g. " that runs"</param>
    private static T? FindBuild<T>(IReadOnlyCollection<T> candidates, Func<T, string> idOf, string id, string what = "") where T : class
    {
        var exact = candidates.FirstOrDefault(c => idOf(c).Equals(id, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var matches = candidates.Where(c => idOf(c).Contains(id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1) return matches[0];

        if (matches.Count == 0) Console.WriteLine($"No build '{id}'{what}");
        else
        {
            Console.WriteLine($"'{id}' names {matches.Count} builds{what}; give more of the id:");
            foreach (var match in matches.Take(12)) Console.WriteLine($"  {idOf(match)}");
            if (matches.Count > 12) Console.WriteLine("  ...");
        }

        return null;
    }
}
