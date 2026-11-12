using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using AcTools.Kn5File;
using StreetCorsa.Dialogs;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Rules;

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
/// The content in play is the core alone, copied into the output as the game has it next to itself: the tests lean on
/// no mod's content. The makes and countries the core has none of are the tests' own (<see cref="TestCars.Makes"/>).
///
/// Every part is priced as the game prices it: by the tests' parts measured on their builds (<see cref="TestCars.Prices"/>),
/// made the first time a test asks for a price. No test makes the game's parts service, which would point the prices
/// at its own catalog.
/// </summary>
internal static class TestLogging
{
    [ModuleInitializer]
    internal static void SilenceLogging()
    {
        AppLoggerFactory.InitializeSilent();
        // The tests play the bare core, as a game in no setting does; the makes are the tests' own
        Mods.ModSet.Play(Mods.ModSet.PlayedIn(null));
        CarBook.Current = TestCars.Makes.Value;
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
/// <c>PartGroups.Current</c>, <c>RaceFormats.Current</c>, <c>RuleBook.Current</c>/<c>PlayBy</c>, <c>CrewBook.Current</c>, <c>ThemeImages.Use</c>,
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

    /// <summary>A zip of these entries (path -> text) at zips/<paramref name="name"/>, written over one made before</summary>
    public string Zip(string name, params (string Path, string Text)[] entries)
    {
        var path = Combine("zips", name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var (entry, text) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(text);
        }

        return path;
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

    /// <summary>The Content folder in the source tree: the core, and whatever mods the developer has put there (never read by a test but the core)</summary>
    public static string Content => Path.Combine(GameProject, Mods.ModSet.FolderName);

    /// <summary>The core's folder</summary>
    public static string BaseContent => Path.Combine(Content, Mods.ModIds.BaseId);

    /// <summary>The core alone from the source tree, as the game plays it with no setting: no mod the developer put there</summary>
    public static Mods.ModSet Core => Mods.ModSet.CoreOf(Content);

    /// <summary>The core's parts folder: the parts' rule tables (part_groups, part_classes, work_order...)</summary>
    public static string CorePartsFolder => Path.Combine(BaseContent, Mods.ContentTypes.Parts);

    /// <summary>
    /// The tests' own engines: one small pack per kind of engine the game supports, round figures, no
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

    /// <summary>
    /// The tests' own running gear (step 31): rear axles, period drums and discs, leaf springs, torsion and sway bars,
    /// shocks and the plain parts of a car no part's came_in names, written by Fixtures\make_test_running_gear.py. Read
    /// as the base game's own, ids bare (<c>running_gear/test_axles/light</c>).
    /// </summary>
    public static string TestRunningGear => Path.Combine(Root, "tests", "StreetCorsa.Tests", "Fixtures", "test_running_gear");

    /// <summary>The tests' own makes and countries (<see cref="TestCars.Makes"/>): a cars/*.json as a mod writes one</summary>
    public static string TestMakes => Path.Combine(Root, "tests", "StreetCorsa.Tests", "Fixtures", "test_makes");

    /// <summary>
    /// The core's parts and the goldens', for the goldens' own catalog: not the tests' engines, so what it holds does
    /// not move when make_test_engines.py does
    /// </summary>
    public static IReadOnlyList<(Mods.ModInfo Mod, string Dir)> GoldenRoots => [PartsRoots[0], (PartsRoots[0].Mod, Goldens)];

    /// <summary>
    /// The core's parts, the tests' engines and the tests' running gear, as
    /// <see cref="PartsCatalog.Load(IReadOnlyList{ValueTuple{Mods.ModInfo, string}})"/> takes them
    /// </summary>
    public static IReadOnlyList<(Mods.ModInfo Mod, string Dir)> PartsRoots
    {
        get
        {
            var game = new Mods.ModInfo { Id = Mods.ModIds.BaseId, Name = "Street Corsa" };
            return [(game, CorePartsFolder), (game, TestEngines), (game, TestRunningGear)];
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
    /// <summary>The core's parts and the tests' own engines and running gear, read once</summary>
    public static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsRoots));

    /// <summary>A small V8's report: peaks at 400 Nm at 3500 rpm, no gearbox of its own</summary>
    public static EngineReport SmallV8()
    {
        var curve = new List<(double Rpm, double Torque)>();
        for (var rpm = 1000.0; rpm <= 6000; rpm += 500) curve.Add((rpm, 400 - Math.Abs(rpm - 3500) * 0.05));
        return new EngineReport { Dyno = new DynoResult(0.0057, 9.5, curve), IdleRpm = 800, LimiterRpm = 5800, Inertia = 8, Mass = 250 };
    }

    /// <summary>The tests' own makes and countries (Fixtures\test_makes), the book every test reads but one that sets its own</summary>
    public static readonly Lazy<CarBook> Makes = new(() =>
        CarBook.Read(Mods.ModMerge.Parse(File.ReadAllText(Path.Combine(RepoPaths.TestMakes, "makes.json")))!, []));

    /// <summary>The tests' parts, their rated builds and the cooling each engine needs, made once</summary>
    public static readonly Lazy<(PartsCatalog Catalog, EngineBuildIndex Builds, EngineCooling Cooling)> Cooling = new(() =>
    {
        var builds = EngineBuildIndex.Create(Catalog.Value);
        return (Catalog.Value, builds, EngineCooling.Create(Catalog.Value, builds));
    });

    /// <summary>
    /// The core's parts and the tests' own measured on their builds, as the game does: what every test's prices are.
    /// Measured on a catalog of its own, never on <see cref="Catalog"/>: the measuring writes figures onto the parts while
    /// other tests read them, and the tests run side by side.
    /// </summary>
    public static readonly Lazy<PartPriceBook> Prices = new(() =>
    {
        var own = PartsCatalog.Load(RepoPaths.PartsRoots);
        return PartPriceBook.Create(own, EngineBuildIndex.Create(own));
    });

    /// <summary>The tests' own V8 in a car (its rated build, Fixtures\test_engines), its tree as the factory puts one in</summary>
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

    /// <summary>A '66 Chevrolet: the make, maker, year and country the running gear tests fit their parts to</summary>
    public static readonly CarFacts Chevy66 = new("chevrolet", "gm", 1966, "USA");

    /// <summary>
    /// A 1600 kg rear-wheel-drive car's AC figures, on double wishbones at the front and AC's <paramref name="rear"/>
    /// suspension at the back: a live axle (AXLE) unless told otherwise
    /// </summary>
    public static AcCarSpecs LiveAxleSpecs(string rear = "AXLE") => new()
    {
        Traction = "RWD", TotalMass = 1600, FrontWeightShare = 0.55, PeakTorque = 390, FinalRatio = 3.55,
        Front = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.33, BrakeTorque = 1600, SpringRate = 30000, DampBump = 3000, DampRebound = 4000, CornerLoad = 450, SuspensionType = "DWB", SwayBarRate = 8000 },
        Rear = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.33, BrakeTorque = 900, SpringRate = 25000, DampBump = 3000, DampRebound = 4000, CornerLoad = 400, SuspensionType = rear, SwayBarRate = 1300 }
    };

    /// <summary>A box of a car on four wheels, its paint all round, a windscreen at the front above the hood</summary>
    public static IKn5 BoxCar()
    {
        var kn5 = Kn5.CreateEmpty();
        var paint = PlaceholderMesh.SolidMaterial(kn5, "car_paint", 200, 30, 30, 0.5f, 0.5f, 0.3f, 30);
        var glass = PlaceholderMesh.SolidMaterial(kn5, "window_glass", 20, 20, 20, 0.5f, 0.5f, 0.3f, 30);
        var tyre = PlaceholderMesh.SolidMaterial(kn5, "tyre", 10, 10, 10, 0.5f, 0.5f, 0.1f, 10);
        kn5.RootNode.Children.Add(PlaceholderMesh.Boxes("body", [(new Vector3(0, 0.75f, 0), new Vector3(0.9f, 0.4f, 2.2f))], paint));
        kn5.RootNode.Children.Add(PlaceholderMesh.Boxes("screen", [(new Vector3(0, 1.3f, 0.6f), new Vector3(0.7f, 0.15f, 0.01f))], glass));
        foreach (var (name, x, z) in new[] { ("WHEEL_LF", 0.8f, 1.4f), ("WHEEL_RF", -0.8f, 1.4f), ("WHEEL_LR", 0.8f, -1.4f), ("WHEEL_RR", -0.8f, -1.4f) })
        {
            var wheel = Kn5Node.CreateBaseNode(name);
            wheel.Transform = Helpers.Matrices.ToMat4x4(Matrix4x4.CreateTranslation(x, 0.33f, z));
            wheel.Children.Add(PlaceholderMesh.Boxes(name + "_tyre", [(Vector3.Zero, new Vector3(0.1f, 0.33f, 0.33f))], tyre));
            kn5.RootNode.Children.Add(wheel);
        }
        return kn5;
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

/// <summary>The rules a game plays by with one mod on top of the base game's</summary>
public static class TestRules
{
    /// <summary>The base game's rules merged with <paramref name="json"/> as a mod's rules/mine.json in <paramref name="temp"/></summary>
    public static RuleBook BaseWith(TempDir temp, string json, List<string>? problems = null)
    {
        temp.File("mod/rules/mine.json", json);
        problems ??= [];
        var merged = ModMerge.NewObject();
        ModMerge.Into(merged, ModMerge.ContentFiles(ModSet.BaseOnly(RepoPaths.BaseContent), ContentTypes.Rules, problems));
        ModMerge.Into(merged, ModMerge.ContentFiles(ModSet.BaseOnly(temp.Combine("mod")), ContentTypes.Rules, problems));
        return RuleBook.Read(merged, problems);
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

/// <summary>
/// A saves repository that keeps nothing: a save counts, hands the session repository a throwaway in-memory database
/// to write in and records the race sessions written there, or throws <see cref="Throw"/>. Everything else is not
/// for these tests.
/// </summary>
public class RecordingRepository : Services.Storage.IGameStateRepository
{
    public int Saves { get; private set; }
    public Exception? Throw { get; set; }
    public List<Models.Race.ProcessedRaceSession> Recorded { get; } = new();

    public void Save(GameState state, string saveName, Action<LiteDB.LiteDatabase>? sameTransaction)
    {
        if (Throw != null) throw Throw;
        Saves++;
        using var db = new LiteDB.LiteDatabase(new MemoryStream());
        sameTransaction?.Invoke(db);
        Recorded.AddRange(db.GetCollection<Models.Race.ProcessedRaceSession>("ProcessedRaceSessions").FindAll());
    }

    public void Save(GameState state, string saveName) => Save(state, saveName, null);
    public GameState? Load(string saveName) => throw new NotSupportedException();
    public bool Exists(string saveName) => throw new NotSupportedException();
    public void Delete(string saveName) => throw new NotSupportedException();
    public List<string> ListSaves() => throw new NotSupportedException();
    public GameState CreateNew(string saveName, string playerName, GameRules? rules = null) => throw new NotSupportedException();
    public void Dispose() { }
}

/// <summary>A running gear catalog of the tests' own: a rim, a tyre, a brake, a spring and a shock, and what a test adds</summary>
public static class TestRunningGear
{
    /// <summary>
    /// The five parts as <paramref name="pack"/>/rim, /tyre, /brake, /spring and /shock (a 15" rim, a 205 tyre of
    /// 0.32 m, a 1000 N brake, a 30000 N/m spring), and <paramref name="more"/> (each a part's JSON), in
    /// <paramref name="temp"/>'s parts folder
    /// </summary>
    public static PartsCatalog Catalog(TempDir temp, string pack, params string[] more)
    {
        var parts = new[]
        {
            $$"""{ "id": "{{pack}}/rim", "mass": 8, "specs": { "offset": 0.0, "wheel_radius": 0.1905 } }""",
            $$"""{ "id": "{{pack}}/tyre", "mass": 10, "specs": { "friction": 1.0, "radius": 0.32, "tyre_width": 205, "wheel_radius": 190.5 } }""",
            $$"""{ "id": "{{pack}}/brake", "mass": 5, "specs": { "force": 1000, "radius": 0.1 } }""",
            $$"""{ "id": "{{pack}}/spring", "mass": 2, "specs": { "force": 30000 } }""",
            $$"""{ "id": "{{pack}}/shock", "mass": 2 }"""
        }.Concat(more);
        temp.Parts(Path.Combine("parts", pack.Replace('/', Path.DirectorySeparatorChar)), "[" + string.Join(",", parts) + "]");
        return PartsCatalog.Load(temp.Combine("parts"));
    }

    /// <summary>The factory's parts on both axles: the five of <see cref="Catalog"/></summary>
    public static (RunningGearFactory.AxleParts, RunningGearFactory.AxleParts) Factory(PartsCatalog catalog, string pack)
    {
        var axle = new RunningGearFactory.AxleParts(catalog.Get($"{pack}/tyre")!, catalog.Get($"{pack}/rim")!,
            catalog.Get($"{pack}/brake")!, catalog.Get($"{pack}/spring")!, catalog.Get($"{pack}/shock")!);
        return (axle, axle);
    }
}

/// <summary>Directory junctions for the tests that check what a junction leads to</summary>
public static class Junction
{
    /// <summary>
    /// A junction at <paramref name="link"/> to the folder <paramref name="target"/> (mklink /J, which needs no
    /// rights); fails the test with mklink's own words when it could not be made (a policy, a file system without them)
    /// </summary>
    public static void Make(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var mklink = System.Diagnostics.Process.Start(start)!;
        var output = mklink.StandardOutput.ReadToEndAsync();
        var error = mklink.StandardError.ReadToEndAsync();
        if (!mklink.WaitForExit(10_000))
        {
            mklink.Kill();
            Assert.Fail($"mklink /J did not finish in 10 s: {link} -> {target}");
        }

        Assert.True(mklink.ExitCode == 0 && Directory.Exists(link),
            $"mklink /J failed ({mklink.ExitCode}): {output.Result.Trim()} {error.Result.Trim()}");
    }
}
