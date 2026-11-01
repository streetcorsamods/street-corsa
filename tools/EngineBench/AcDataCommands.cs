using System.Diagnostics;
using System.Text;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Parts.Sounds;

namespace StreetCorsa;

/// <summary>What the parts make of a car's Assetto Corsa data and sound: export, car, sound, sounds</summary>
public static partial class Program
{
    /// <summary>Writes the Assetto Corsa data files a build changes, made from an unpacked car data folder</summary>
    private static int Export(PartsCatalog catalog, string id, string carData, string output)
    {
        if (BenchOutput.Refusal(output, carData) is { } refused)
        {
            Console.WriteLine(refused);
            return 1;
        }

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

        BenchOutput.Write(output, files.Select(f => (f.Key, f.Value)), Encoding.Latin1);

        Console.WriteLine($"{build.Name}: {report.Dyno!.MaxPowerHp:0} hp, {report.GearRatios.Count} gears -> {string.Join(", ", files.Keys)} in {output}");
        return 0;
    }

    /// <summary>
    /// A donor car's sound as the overlay would write it under another car's id: the GUID lines the bank answers,
    /// and which engine events the donor lacks. The master GUIDs next to the cars folder serve a Kunos donor.
    /// </summary>
    private static int Sound(string donorFolder, string carId, string? output)
    {
        if (output != null && BenchOutput.Refusal(output, donorFolder) is { } refused)
        {
            Console.WriteLine(refused);
            return 1;
        }

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
            BenchOutput.Write(output, [(AcCarSound.GuidsFileName, guids)], Helpers.SafeFile.Utf8NoBom);
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
        Console.WriteLine($"{library.Curated.Count} curated sound(s) under {library.Root}, {library.Harvested.Count} harvested off {cars} car(s)\n");
        // The time on stderr, so two runs of the same content diff clean
        Console.Error.WriteLine($"harvested in {stopwatch.Elapsed.TotalMilliseconds:0} ms");
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
            var request = SoundMatcher.RequestFor(catalog, tree.Root.Definition.Id, report, tree.Root.SelfAndDescendants().Select(p => p.Definition.Id));
            var choice = SoundMatcher.Choose(library, request);
            Console.WriteLine($"{Short(build.Build.Id),-48} {request.Cylinders,3} {request.Family,-6} {request.LimiterRpm,5:0}  " +
                              (choice == null ? "none" : $"{choice.Sound.Name} ({Short(choice.Sound.Id)}) [{choice.Score:0.0}]: {choice.Reason}"));
        }

        return 0;
    }

    /// <summary>Every data file a car's parts change: the build as its engine, the factory running gear as its wheels</summary>
    private static int Car(PartsCatalog catalog, string carFolder, string id, string? output)
    {
        if (output != null && BenchOutput.Refusal(output, carFolder) is { } refused)
        {
            Console.WriteLine(refused);
            return 1;
        }

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
        var sound = SoundMatcher.ForCar(library, SoundMatcher.RequestFor(catalog, tree.Root.Definition.Id, report, tree.Root.SelfAndDescendants().Select(p => p.Definition.Id)), carId, out var choice);
        if (choice != null) Console.WriteLine($"  sound: {choice.Sound.Name} ({choice.Sound.Id}): {choice.Reason}{(sound == null ? " (the car's own bank)" : "")}");

        // Only what changed, line by line
        foreach (var (name, content) in result.Files)
        {
            var before = (readFile(name) ?? "").Replace("\r\n", "\n").Split('\n');
            var after = content.Replace("\r\n", "\n").Split('\n');
            var changed = after.Where((line, i) => i >= before.Length || before[i] != line).Count(l => l.Length > 0);
            Console.WriteLine($"  {name}: {changed} line(s) differ");
        }

        if (output != null) BenchOutput.Write(output, result.Files.Select(f => (f.Key, f.Value)), Encoding.Latin1);

        return result.CanDrive ? 0 : 1;
    }
}
