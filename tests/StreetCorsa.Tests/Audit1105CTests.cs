using System.IO.Compression;
using System.Text;
using StreetCorsa.Helpers;
using StreetCorsa.Mods;
using StreetCorsa.Rules;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Talk;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-11-05 tech audit's fixes in the mods, the rules, the AI and the talk: mod ids, zips that lie about their
/// size or their paths, what deletes leave behind, the rules' figures, a machinist's job as a pick
/// </summary>
public sealed class Audit1105CTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Content => _temp.Combine("Content");

    // ---- mod ids ----

    [Theory]
    [InlineData("mymod", true)]
    [InlineData("mymod\n", false)]
    [InlineData("mymod\r\n", false)]
    [InlineData("mymod ", false)]
    [InlineData(" mymod", false)]
    [InlineData("car\n", false)]
    public void A_mod_id_with_a_line_break_or_a_space_at_its_end_is_no_mod_id(string id, bool valid) =>
        Assert.Equal(valid, ModIds.IsValidModId(id));

    // ---- paths in a zip ----

    [Theory]
    [InlineData("textures\\car.png", true)]
    [InlineData("evil\\..\\..\\x", false)]
    [InlineData("textures/COM1.png", false)]
    [InlineData("CON", false)]
    [InlineData("data/nul.json", false)]
    [InlineData("aux .txt", false)]
    [InlineData("a./b", false)]
    [InlineData("a /b", false)]
    [InlineData("a/ /b", false)]
    [InlineData("console.json", true)]
    public void A_zip_path_is_safe_only_when_each_step_is_a_plain_name(string path, bool safe) =>
        Assert.Equal(safe, ModImport.IsSafe(path));

    [Fact]
    public void A_backslash_entry_that_climbs_out_is_refused_and_nothing_is_written()
    {
        _temp.File("Content/streetcorsa/mod.json", "{}");
        var zip = _temp.Zip("climb.zip", ("climb/mod.json", """{ "id": "climb" }"""), ("climb\\..\\..\\..\\outside.txt", "out"));

        var plan = ModImport.Plan([zip], Content);

        Assert.Equal(ImportRefusal.UnsafePath, Assert.Single(plan.Refused).Why);
        ModImport.Install(plan, Content);
        Assert.False(File.Exists(_temp.Combine("outside.txt")));
        Assert.False(Directory.Exists(Path.Combine(Content, "addons", "climb")));
    }

    [Fact]
    public void A_zip_changed_after_its_plan_is_checked_again_as_it_is_unpacked()
    {
        _temp.File("Content/streetcorsa/mod.json", "{}");
        var zip = _temp.Zip("radio.zip", ("radio/mod.json", """{ "id": "radio" }"""), ("radio/a.txt", "small"));
        var plan = ModImport.Plan([zip], Content);
        Assert.Single(plan.Mods);

        // Bigger than when it was picked: the room was counted for the small one
        _temp.Zip("radio.zip", ("radio/mod.json", """{ "id": "radio" }"""), ("radio/a.txt", new string('x', 100_000)));
        var bigger = ModImport.Install(plan, Content);
        Assert.Contains("size when it was picked", Assert.Single(bigger.Failed).Error);
        Assert.False(Directory.Exists(Path.Combine(Content, "addons", "radio")));

        // A path out of the mod's folder put in since
        _temp.Zip("radio.zip", ("radio/mod.json", """{ "id": "radio" }"""), ("radio/../../../outside.txt", "x"));
        var climbing = ModImport.Install(plan, Content);
        Assert.Single(climbing.Failed);
        Assert.False(File.Exists(_temp.Combine("outside.txt")));
        Assert.False(Directory.Exists(Path.Combine(Content, "addons", "radio")));
    }

    [Fact]
    public void An_entry_that_unpacks_to_more_than_it_claims_never_lands_whole()
    {
        _temp.File("Content/streetcorsa/mod.json", "{}");
        var data = new string('x', 200_000);
        var zip = _temp.Zip("bomb.zip", ("bomb/mod.json", """{ "id": "bomb" }"""), ("bomb/data.bin", data));
        const int claimed = 1000;
        ClaimSize(zip, "bomb/data.bin", claimed);

        var plan = ModImport.Plan([zip], Content);
        var mod = Assert.Single(plan.Mods);
        Assert.True(mod.Size < 2000);
        var outcome = ModImport.Install(plan, Content);

        // Refused as it comes, or cut where it claims to end: never the 200 KB
        var file = Path.Combine(Content, "addons", "bomb", "data.bin");
        Assert.True(outcome.Failed.Count == 1 || new FileInfo(file).Length <= claimed);
    }

    [Fact]
    public void A_mod_json_too_big_or_bigger_than_it_claims_is_unreadable()
    {
        var huge = _temp.Zip("huge.zip", ("huge/mod.json", "{ \"id\": \"huge\", \"description\": \"" + new string('x', ModImport.MostManifestBytes) + "\" }"));
        var lying = _temp.Zip("lying.zip", ("lying/mod.json", "{ \"id\": \"lying\", \"description\": \"" + new string('x', 50_000) + "\" }"));
        ClaimSize(lying, "lying/mod.json", 10);

        var plan = ModImport.Plan([huge, lying], Content);

        Assert.Empty(plan.Mods);
        Assert.All(plan.Refused, r => Assert.Equal(ImportRefusal.Unreadable, r.Why));
        Assert.Equal(2, plan.Refused.Count);
    }

    [Fact]
    public void A_zip_of_too_many_files_is_refused_whole()
    {
        var path = _temp.Combine("zips", "many.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("many/mod.json").Open().Dispose();
            for (var i = 0; i < ModImport.MostEntries; i++) zip.CreateEntry($"many/{i}", CompressionLevel.NoCompression);
        }

        var refused = Assert.Single(ModImport.Plan([path], Content).Refused);

        Assert.Equal(ImportRefusal.NotAZip, refused.Why);
    }

    /// <summary>Writes <paramref name="size"/> as the entry's unpacked size in its local header and the zip's directory</summary>
    private static void ClaimSize(string zip, string entry, uint size)
    {
        var bytes = File.ReadAllBytes(zip);
        var name = Encoding.UTF8.GetBytes(entry);
        for (var i = 0; i + 46 < bytes.Length; i++)
        {
            var signature = BitConverter.ToUInt32(bytes, i);
            if (signature == 0x04034b50 && NameAt(bytes, i + 30, BitConverter.ToUInt16(bytes, i + 26), name))
                BitConverter.TryWriteBytes(bytes.AsSpan(i + 22), size);
            else if (signature == 0x02014b50 && NameAt(bytes, i + 46, BitConverter.ToUInt16(bytes, i + 28), name))
                BitConverter.TryWriteBytes(bytes.AsSpan(i + 24), size);
        }

        File.WriteAllBytes(zip, bytes);
    }

    private static bool NameAt(byte[] bytes, int at, int length, byte[] name) =>
        length == name.Length && at + length <= bytes.Length && bytes.AsSpan(at, length).SequenceEqual(name);

    // ---- what deletes leave ----

    [Fact]
    public void What_a_delete_left_goes_with_the_next_sweep_and_a_mod_kept_aside_stays()
    {
        var aside = Path.Combine(Content, ModImport.ReplacedFolder);
        var gone = Path.Combine(aside, "gone" + ModImport.GoneSuffix);
        _temp.File("Content/_replaced/gone" + ModImport.GoneSuffix + "/held.txt", "x");
        _temp.File("Content/_replaced/kept/mod.json", """{ "id": "kept" }""");

        // A file held open: the delete leaves the folder, named as only waiting to go
        using (File.Open(Path.Combine(gone, "held.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            ModImport.DeleteAside(gone);
        Assert.True(Directory.Exists(gone));

        // The next install sweeps it; the mod that could not be moved back is never taken
        _temp.File("Content/streetcorsa/mod.json", "{}");
        var zip = _temp.Zip("radio.zip", ("radio/mod.json", """{ "id": "radio" }"""));
        ModImport.Install(ModImport.Plan([zip], Content), Content);

        Assert.False(Directory.Exists(gone));
        Assert.True(File.Exists(Path.Combine(aside, "kept", "mod.json")));
    }

    [Fact]
    public void A_mod_manager_delete_sweeps_what_earlier_ones_left()
    {
        _temp.File("Content/streetcorsa/mod.json", "{}");
        _temp.File("Content/addons/radio/mod.json", """{ "id": "radio" }""");
        _temp.File("Content/_replaced/old" + ModImport.GoneSuffix + "/file.txt", "left");

        ModManager.Delete(ModManager.List(Content).Single(m => m.Id == "radio"));

        Assert.False(Directory.Exists(Path.Combine(Content, ModImport.ReplacedFolder)));
    }

    // ---- the rules ----

    [Fact]
    public void The_world_day_reputation_and_simulation_figures_are_held_to_what_the_game_plays_by()
    {
        var problems = new List<string>();
        var book = TestRules.BaseWith(_temp, """
            {
              "simulation": { "roadCrashChance": 5, "dragCrashChance": -0.1 },
              "reputation": { "winRateBonus": -30, "starWinRate": 2 },
              "world": { "windMinKmh": -5, "latitude": 400, "midsummer": 0 },
              "day": { "startHour": 30 }
            }
            """, problems);

        var core = new RuleBook();
        Assert.Equal(core.Simulation.RoadCrashChance, book.Simulation.RoadCrashChance);
        Assert.Equal(core.Simulation.DragCrashChance, book.Simulation.DragCrashChance);
        Assert.Equal(core.Reputation.WinRateBonus, book.Reputation.WinRateBonus);
        Assert.Equal(core.Reputation.StarWinRate, book.Reputation.StarWinRate);
        Assert.Equal(core.World.WindMinKmh, book.World.WindMinKmh);
        Assert.Equal(core.World.Latitude, book.World.Latitude);
        Assert.Equal(core.World.Midsummer, book.World.Midsummer);
        Assert.Equal(core.Day.StartHour, book.Day.StartHour);
        foreach (var figure in new[] { "simulation.roadCrashChance", "simulation.dragCrashChance", "reputation.winRateBonus", "reputation.starWinRate",
                     "world.windMinKmh", "world.latitude", "world.midsummer", "day.startHour" })
            Assert.Contains(problems, p => p.StartsWith(figure, StringComparison.Ordinal));
    }

    [Fact]
    public void The_least_oil_sold_is_never_more_than_a_sump_holds()
    {
        var problems = new List<string>();
        var book = TestRules.BaseWith(_temp, """{ "repairs": { "leastOilLitres": 10, "sumpLitres": 4 } }""", problems);

        Assert.Equal(4, book.Repairs.LeastOilLitres);
        Assert.Equal(4, book.Repairs.SumpLitres);
        Assert.Contains(problems, p => p.StartsWith("repairs.leastOilLitres", StringComparison.Ordinal));
    }

    // ---- the AI's clock ----

    [Theory]
    [InlineData(0.5, 500)]
    [InlineData(-3, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 365L * 24 * 3600 * 1000)]
    public void Seconds_from_the_rules_are_whole_milliseconds_never_below_none(double seconds, long ms) =>
        Assert.Equal(ms, AiBackoff.Milliseconds(seconds));

    [Fact]
    public void A_rest_runs_on_the_clock_that_never_steps()
    {
        var rest = new AiBackoff();
        Assert.False(rest.IsResting);
        Assert.True(rest.Rest(60));
        Assert.True(rest.IsResting);
        Assert.False(rest.Rest(60));
        rest.Clear();
        Assert.False(rest.IsResting);
        Assert.True(rest.Rest(0));
        Assert.False(rest.IsResting);
    }

    // ---- the counters ----

    [Fact]
    public void A_machinists_job_has_no_kind_fit_or_stock_of_its_own()
    {
        var job = ClerkPick.Job("bore", "Bore .030 over", "Ford 289 block", 85m, speed: true, days: 3);

        Assert.Equal(("Ford 289 block", true, 3), (job.Part, job.Speed, job.Days));
        Assert.Equal(("", false, 0), (job.Kind, job.Fits, job.InStock));
        var stock = new ClerkPick("cam", "Mopar cam", "Camshafts", 100m, true, 1);
        Assert.Equal(("", false, 0), (stock.Part, stock.Speed, stock.Days));
    }
}

/// <summary>The fixes that read or write process-wide state: the mods' setting marks, the mod.json sections' cache</summary>
[Collection(GlobalsCollection.Name)]
public sealed class Audit1105CGlobalTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_mod_managers_list_never_marks_a_setting()
    {
        _temp.File("Content/streetcorsa/mod.json", "{}");
        _temp.File("Content/settings/audit_c_rods/mod.json", """{ "id": "audit_c_rods", "type": "setting" }""");
        ModIds.MarkSetting("audit_c_rods", false);
        try
        {
            var listed = ModManager.List(_temp.Combine("Content"));

            Assert.Contains(listed, m => m.Id == "audit_c_rods" && m.IsSetting);
            Assert.False(ModIds.GoesBare("audit_c_rods"));
        }
        finally
        {
            ModIds.MarkSetting("audit_c_rods", false);
        }
    }

    [Fact]
    public void Reading_every_mod_or_the_available_ones_never_flips_an_ids_namespace()
    {
        var content = _temp.Combine("Content");
        _temp.File("Content/streetcorsa/mod.json", "{}");
        _temp.File("Content/settings/audit_c_flip/mod.json", """{ "id": "audit_c_flip", "type": "setting" }""");
        ModIds.MarkSetting("audit_c_flip", false);
        try
        {
            Assert.Contains(ModSet.EverythingIn(content).Settings, s => s.Id == "audit_c_flip");
            Assert.Contains(ModSet.AvailableIn(content).Settings, s => s.Id == "audit_c_flip");
            Assert.False(ModIds.GoesBare("audit_c_flip"));
            Assert.Equal("audit_c_flip:engines/x", ModIds.Qualify("audit_c_flip", "engines/x"));

            // The load that plays content still marks it
            ModSet.Load(content, setting: "audit_c_flip", exactSetting: true);
            Assert.True(ModIds.GoesBare("audit_c_flip"));
        }
        finally
        {
            ModIds.MarkSetting("audit_c_flip", false);
        }
    }

    [Fact]
    public void A_mod_json_changed_on_disk_is_read_anew_and_one_not_read_lately_let_go()
    {
        var path = _temp.File("mod/mod.json", """{ "builds": [1] }""");
        Assert.Equal("[1]", ManifestSections.Get(path, "builds"));
        Assert.True(ManifestSections.Sweeping);

        File.WriteAllText(path, """{ "builds": [1, 2, 3] }""");
        Assert.Equal("[1, 2, 3]", ManifestSections.Get(path, "builds"));
        Assert.Null(ManifestSections.Get(path, "sounds"));

        // Read from many threads at once (content loads in parallel): each gets the one text
        var texts = new string?[64];
        Parallel.For(0, texts.Length, i => texts[i] = ManifestSections.Get(path, "builds"));
        Assert.All(texts, t => Assert.Equal("[1, 2, 3]", t));

        // Not read for longer than it is kept: let go, and the sweep stops
        ManifestSections.Sweep(Environment.TickCount64 + ManifestSections.KeepMs + 1);
        Assert.Equal(0, ManifestSections.Kept);
        Assert.False(ManifestSections.Sweeping);
        Assert.Equal("[1, 2, 3]", ManifestSections.Get(path, "builds"));
    }
}
