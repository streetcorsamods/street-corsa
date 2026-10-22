using System.Runtime.CompilerServices;
using System.Text;
using StreetCorsa.Dialogs;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// Runs before any test. The game's classes take their logger from <see cref="AppLoggerFactory"/>, which throws
/// until <c>Initialize()</c> has run, and <c>Initialize()</c> writes log files under %AppData%\StreetCorsa\Logs.
/// The tests must never touch the user's folders, so the factory is set up on Serilog's default (silent) logger
/// instead, through the seam made for it.
///
/// LiteDB's shared mapper builds a type's mapping the first time it meets it, and two tests meeting the same type
/// at once can see it half built. The types the saves are made of are mapped here, once, before any test runs.
///
/// Every part is priced as the game prices it: by the shipped parts measured on their builds (<see cref="TestCars.Prices"/>),
/// made the first time a test asks for a price. No test makes the game's parts service, which would point the prices
/// at its own catalog.
/// </summary>
internal static class TestLogging
{
    [ModuleInitializer]
    internal static void SilenceLogging()
    {
        AppLoggerFactory.InitializeSilent();
        PartPricing.Book = () => TestCars.Prices.Value;
        PartPricing.Jobs = () => TestCars.Catalog.Value;

        LiteDB.BsonMapper.Global.ToDocument(new StreetCorsa.Models.Race.ProcessedRaceSession());
        LiteDB.BsonMapper.Global.ToDocument(new StreetCorsa.Models.GameState.GameState());
        // A part tree with a child and a cut: the mapper builds a type's mapping on first use and is not safe to share
        // between threads while it does, and the tests that map one (MachiningTests, PartShopTests) run side by side
        LiteDB.BsonMapper.Global.ToDocument(new StreetCorsa.Models.GameState.PartInstance("x") { Cuts = { ["bore"] = 1 }, Children = { new StreetCorsa.Models.GameState.PartInstance("y") } });
    }
}

/// <summary>
/// The one collection for every test that sets a process-wide static (<c>AppSettings.Instance</c>, <c>CarBook.Current</c>,
/// <c>PartGroups.Current</c>, <c>RaceFormats.Current</c>, <c>RuleBook.Current</c>/<c>PlayBy</c>, <c>ThemeImages.Use</c>,
/// <c>Locale</c>, <c>AdBlurbs.Writer</c>, a <c>GameSettingsService</c> made (it sets the AC folder in use)): xunit runs
/// these after every parallel collection and one at a time, so no other test reads a value one of them set. A test
/// that writes such a static and is not in here races the rest.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalsCollection
{
    public const string Name = "Globals";
}

/// <summary>
/// A fresh folder under the system temp folder for one test, deleted with everything in it (read-only files
/// included) when the test ends. Every filesystem test works in one of these and nowhere else.
/// </summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StreetCorsa.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Writes a file (making its folder), returns its full path</summary>
    public string File(string relative, string content) => Bytes(relative, System.Text.Encoding.UTF8.GetBytes(content));

    /// <summary>
    /// Writes parts as a parts folder holds them, a file each in the folder: a JSON list of parts, each file named after
    /// the last word of its id (one without an id, or no part at all, after its place in the list). A name no file may
    /// have (a ':' of a mod's id) is written with '_' for it, and a name already written in the call gets its place in
    /// the list after it, so no part is written over another.
    /// </summary>
    public void Parts(string folder, string parts)
    {
        var index = 0;
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bad = System.IO.Path.GetInvalidFileNameChars();
        foreach (var part in Newtonsoft.Json.Linq.JArray.Parse(parts))
        {
            var name = part is Newtonsoft.Json.Linq.JObject { } o && o["id"]?.Type == Newtonsoft.Json.Linq.JTokenType.String
                ? new string(((string)o["id"]!).Split('/')[^1].Select(c => bad.Contains(c) ? '_' : c).ToArray())
                : $"part{index}";
            if (name.Length == 0 || !written.Add(name)) written.Add(name = $"{name}_{index}");
            File(System.IO.Path.Combine(folder, name + ".json"), part.ToString());
            index++;
        }
    }

    public string Bytes(string relative, byte[] content)
    {
        var full = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(Path)) return;
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
            // A handle still open (LiteDB closing late): the temp folder is the OS's to clean
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Where the repository is, found from this source file (the test output lives outside the tree)</summary>
public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string GameProject => Path.Combine(Root, "Street Corsa");

    /// <summary>The Content folder in the source tree: the base game and nothing else</summary>
    public static string Content => Path.Combine(GameProject, Mods.ModSet.FolderName);

    public static string BaseContent => Path.Combine(Content, Mods.ModIds.BaseId);

    public static string PartsFolder => Path.Combine(BaseContent, Mods.ContentTypes.Parts);

    /// <summary>
    /// The tests' own engines (roadmap step 38): one small pack per kind of engine the game supports, round figures, no
    /// models, written by Fixtures\make_test_engines.py. Read as the base game's own, ids bare (<c>engines/test_v8/block</c>).
    /// The base game has no engine of its own since the engines became mods (2026-10-20), and the tests depend on no mod's
    /// content: the tests of machining, rebuilds, the stand, cooling and tuning work on these.
    /// </summary>
    public static string TestEngines => Path.Combine(Root, "tests", "StreetCorsa.Tests", "Fixtures", "test_engines");

    /// <summary>
    /// The real rated builds the engine model is held to (EngineGoldenTests), their parts as data, written by
    /// Fixtures\make_goldens.py: read by the goldens' own catalog only, so no other test leans on a real engine
    /// </summary>
    public static string Goldens => Path.Combine(Root, "tests", "StreetCorsa.Tests", "Fixtures", "goldens");

    /// <summary>The tests' parts and the goldens', for the goldens' own catalog</summary>
    public static IReadOnlyList<(Mods.ModInfo Mod, string Dir)> GoldenRoots => [.. PartsRoots, (PartsRoots[0].Mod, Goldens)];

    /// <summary>The base game's parts and the tests' engines, as <see cref="PartsCatalog.Load(IReadOnlyList{ValueTuple{Mods.ModInfo, string}})"/> takes them</summary>
    public static IReadOnlyList<(Mods.ModInfo Mod, string Dir)> PartsRoots
    {
        get
        {
            var game = new Mods.ModInfo { Id = Mods.ModIds.BaseId, Name = "Street Corsa" };
            return [(game, PartsFolder), (game, TestEngines)];
        }
    }

    private static string FindRoot([CallerFilePath] string here = "")
    {
        for (var dir = Path.GetDirectoryName(here); dir != null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Street Corsa.slnx"))) return dir;
        }

        throw new DirectoryNotFoundException($"No Street Corsa.slnx above {here}");
    }
}

/// <summary>A car's data.acd as AC packs it: each entry's name, then its bytes shifted by the key made from the car id</summary>
public static class AcdFixture
{
    /// <summary>Writes the car folder's data.acd holding the entries (and the folder); returns the file</summary>
    public static string Write(string carFolder, string carId, params (string Name, byte[] Content)[] entries)
    {
        var key = Encoding.ASCII.GetBytes(AcdFile.KeyFor(carId));
        using var data = new MemoryStream();
        var w = new BinaryWriter(data);
        foreach (var (name, content) in entries)
        {
            w.Write(name.Length);
            w.Write(Encoding.ASCII.GetBytes(name));
            w.Write(content.Length);
            for (var i = 0; i < content.Length; i++) w.Write((int)(byte)(content[i] + key[i % key.Length]));
        }

        Directory.CreateDirectory(carFolder);
        var file = Path.Combine(carFolder, AcdFile.FileName);
        File.WriteAllBytes(file, data.ToArray());
        return file;
    }
}

/// <summary>The cars the condition and gas station tests take apart: part trees and AC data folders</summary>
public static class TestCars
{
    /// <summary>The parts the game ships, read once</summary>
    public static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsRoots));

    /// <summary>The parts the game ships, their rated builds and the cooling each engine needs, made once</summary>
    public static readonly Lazy<(PartsCatalog Catalog, EngineBuildIndex Builds, EngineCooling Cooling)> Cooling = new(() =>
    {
        var builds = EngineBuildIndex.Create(Catalog.Value);
        return (Catalog.Value, builds, EngineCooling.Create(Catalog.Value, builds));
    });

    /// <summary>
    /// The base game's parts and the fixture's V8s measured on their builds, as the game does: what every test's prices are.
    /// Measured on a catalog of its own, never on <see cref="Catalog"/>: the measuring writes figures onto the parts while
    /// other tests read them, and the tests run side by side.
    /// </summary>
    public static readonly Lazy<PartPriceBook> Prices = new(() =>
    {
        var own = PartsCatalog.Load(RepoPaths.PartsRoots);
        return PartPriceBook.Create(own, EngineBuildIndex.Create(own));
    });

    /// <summary>The tests' own V8 in a car (its rated build, Fixtures	est_engines), its tree as the factory puts one in</summary>
    public static Car WithRealEngine(PartsCatalog catalog)
    {
        var build = catalog.EngineBuilds.Single(b => b.Id == "rated/test-v8");
        var tree = PartTreeBuilder.BuildEngine(catalog, build)!;
        var car = new Car("car_a") { HasPartsAssigned = true };
        car.Parts.Add(PartTrees.FromInstalled(tree.Root, PartInstance.CarEngineSlot));
        return car;
    }

    /// <summary>A rim with its tyre, a brake and a shock on every corner, and a spring when asked (made-up part ids)</summary>
    public static void AddRunningGear(Car car, bool springs)
    {
        for (var i = 0; i < RunningGear.Corners; i++)
        {
            var rim = new PartInstance("rim") { ParentSlot = RunningGear.WheelSlot(i) };
            rim.Children.Add(new PartInstance("tyre") { ParentSlot = RunningGear.TyreSlotOnRim });
            car.Parts.Add(rim);
            car.Parts.Add(new PartInstance("brake") { ParentSlot = RunningGear.BrakeSlot(i) });
            if (springs) car.Parts.Add(new PartInstance("spring") { ParentSlot = RunningGear.SpringSlot(i) });
            car.Parts.Add(new PartInstance("shock") { ParentSlot = RunningGear.ShockSlot(i) });
        }
    }

    /// <summary>
    /// An AC car's data folder: the car.ini given, and suspensions and tyres that put its centre of gravity
    /// (0.485 + 0.495) / 2 m over the road
    /// </summary>
    public static void WriteData(string carFolder, string carIni)
    {
        var data = Path.Combine(carFolder, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "car.ini"), carIni);
        File.WriteAllText(Path.Combine(data, "suspensions.ini"), "[FRONT]\nBASEY=-0.145\n[REAR]\nBASEY=-0.155\n");
        File.WriteAllText(Path.Combine(data, "tyres.ini"), "[FRONT]\nRADIUS=0.34\n[REAR]\nRADIUS=0.34\n");
    }
}

/// <summary>A navigation service for the tests that move between screens they make themselves</summary>
public static class TestNavigation
{
    /// <summary>
    /// The services only go to the screens the typed factories build: none of them is given here, and SafeNavigate is
    /// handed its screen. Without a flow, the service reads the base game's
    /// </summary>
    public static NavigationService WithoutServices(DialogService dialogs, FlowBook? flow = null) => new(
        dialogs, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, null!, null!, null!, null!, _ => { }, flow);
}
