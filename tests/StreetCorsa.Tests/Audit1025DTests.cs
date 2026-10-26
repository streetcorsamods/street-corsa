using System.Numerics;
using System.Text.Json.Nodes;
using StreetCorsa.Helpers;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Rules;
using StreetCorsa.Services;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-25 tech audit's fixes in the parts, the mods, the rules and the helpers: mod data that is no number or no
/// path, the curve a car names, the mod books' ids, deep build lists, the ui files' cap, the rules' figures
/// </summary>
public sealed class Audit1025DTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ---- paths ----

    [Fact]
    public void A_path_the_file_system_cannot_hold_is_no_path_under_the_root()
    {
        Assert.False(PathNames.TryCombineUnder(_temp.Path, "model\0.kn5", out var full));
        Assert.Equal(string.Empty, full);
        Assert.True(PathNames.TryCombineUnder(_temp.Path, "model.kn5", out _));
    }

    [Fact]
    public void Ids_that_flatten_the_same_never_share_a_model_file()
    {
        Assert.Equal("mymod_a_b", PathNames.Flatten("mymod:a/b"));
        var names = new[] { "mymod:a/b", "mymod_a_b", "mymod:a_b", "mymod:a\\b" }.Select(ShownParts.FileName).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n => Assert.StartsWith("parts" + Path.DirectorySeparatorChar + "mymod_a_b_", n));
        // The same id in other capitals is the same part, and the same file
        Assert.Equal(ShownParts.FileName("Rims/Test/X"), ShownParts.FileName("rims/test/x"), ignoreCase: true);
    }

    // ---- part files ----

    [Fact]
    public void A_model_path_with_a_nul_costs_only_its_own_model()
    {
        _temp.File("parts/test/good.json", """{ "id": "test/good", "name": "good", "model": "good.kn5" }""");
        _temp.File("parts/test/bad.json", """{ "id": "test/bad", "name": "bad", "model": "bad\u0000.kn5" }""");
        _temp.File("parts/test/good.kn5", "kn5");
        _temp.File("parts/models.json", """{ "models": { "test/good": "test/x\u0000.kn5" } }""");

        var catalog = PartsCatalog.Load(_temp.Combine("parts"));

        Assert.NotNull(catalog.Get("test/good"));
        Assert.NotNull(catalog.Get("test/bad"));
        Assert.Null(catalog.GetModelPath(catalog.Get("test/bad")!));
        Assert.EndsWith("good.kn5", catalog.GetModelPath(catalog.Get("test/good")!));
        Assert.Contains(catalog.Problems, p => p.Contains("models.json") && p.Contains("test/good"));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    public void A_part_figure_that_is_no_number_is_0_and_told(string figure)
    {
        _temp.File("parts/test/odd.json", $$"""
            { "id": "test/odd", "name": "odd", "mass": {{figure}},
              "slots": [ { "id": 3, "position": [ 0.1, {{figure}}, 0.2 ], "rotation": [ {{figure}}, 0, 0 ] }, { "id": 4, "position": [ 1, 2, 3 ] } ] }
            """);

        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        var part = catalog.Get("test/odd")!;

        Assert.Equal(0f, part.Mass);
        Assert.Equal([0.1f, 0f, 0.2f], part.Slots[0].Position);
        Assert.Equal([0f, 0f, 0f], part.Slots[0].Rotation);
        Assert.Equal([1f, 2f, 3f], part.Slots[1].Position);
        var told = Assert.Single(catalog.Problems, p => p.Contains("test/odd"));
        Assert.Contains("mass, slot 3 position, slot 3 rotation", told);
    }

    // ---- a car's own data ----

    private const string EngineIni = "[HEADER]\r\nVERSION=1\r\nPOWER_CURVE={0}\r\n\r\n[ENGINE_DATA]\r\nLIMITER=5500\r\nMINIMUM=750\r\n";

    [Theory]
    [InlineData(@"..\..\secret.lut")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData(@"\\host\share\x.lut")]
    [InlineData("power.ini")]
    [InlineData("sub/power.lut")]
    public void A_curve_named_out_of_the_data_folder_is_never_read_power_lut_is(string curve)
    {
        var asked = new List<string>();
        string? Read(string name)
        {
            asked.Add(name);
            return name switch
            {
                "engine.ini" => string.Format(EngineIni, curve),
                "power.lut" => "0|100\r\n3000|300 ; peak\r\n",
                _ => null
            };
        }

        var specs = AcCarSpecs.Read(Read);

        Assert.Equal(300, specs.PeakTorque);
        Assert.DoesNotContain(curve, asked);
    }

    [Fact]
    public void An_unpacked_cars_data_never_reads_a_name_that_leaves_its_folder()
    {
        _temp.File("cars/fury/data/engine.ini", "[HEADER]\r\nVERSION=1\r\n");
        _temp.File("cars/fury/secret.lut", "0|1");
        var data = AcCarData.Open(_temp.Combine("cars", "fury"));

        Assert.NotNull(data.ReadText("engine.ini"));
        Assert.Null(data.ReadText(@"..\secret.lut"));
        Assert.Null(data.ReadText(_temp.Combine("cars", "fury", "secret.lut")));
        Assert.Null(data.ReadText(@"\\host\share\x.lut"));
        Assert.Null(AcCarDataReader.ReadFile(_temp.Combine("cars", "fury"), @"..\secret.lut"));
        Assert.Null(AcCarDataReader.ForFolder(_temp.Combine("cars", "fury", "data"))(@"..\secret.lut"));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    public void A_car_figure_that_is_no_number_is_none(string figure)
    {
        var ini = new IniText($"[BASIC]\r\nTOTALMASS={figure}\r\nOTHER=12.5\r\n");

        Assert.Null(ini.GetNumber("BASIC", "TOTALMASS"));
        Assert.Equal(12.5, ini.GetNumber("BASIC", "OTHER"));
    }

    [Fact]
    public void A_curve_lines_comments_and_figures_that_are_no_numbers_are_read_as_AC_reads_them()
    {
        var points = AcPowerCurve.Points("0|100\r\n1000|150 ; a note\r\n2000|Infinity\r\nNaN|200\r\n; only a note\r\n3000|250\n");

        Assert.Equal([(0.0, 100.0), (1000.0, 150.0), (3000.0, 250.0)], points);
        Assert.Empty(AcPowerCurve.Points(null));
    }

    [Fact]
    public void The_engine_a_car_runs_on_reads_its_curve_the_same_way()
    {
        var spec = EngineSpecs.FromAcData("Fury", name => name switch
        {
            "engine.ini" => string.Format(EngineIni, " curve.lut "),
            "curve.lut" => "1000|150 ; a note\r\n2000|Infinity\r\n3000|400\r\n",
            _ => null
        }, null, EngineLayout.Front)!;

        Assert.Equal(400, spec.PeakTorque);
        Assert.Equal(275, spec.TorqueAt(2000), 6);
    }

    // ---- the ui files ----

    [Fact]
    public void A_ui_car_json_larger_than_any_real_one_is_not_read()
    {
        _temp.File("cars/big/ui/ui_car.json", "{ \"name\": \"Big\", \"x\": \"" + new string('x', (int)AcCarUi.MaxUiJsonBytes) + "\" }");
        _temp.File("cars/small/ui/ui_car.json", "{ \"name\": \"Small\" }");

        Assert.Null(AcCarUi.TryRead(_temp.Combine("cars", "big"), out var error));
        Assert.Contains("bytes", error);
        Assert.Equal("Small", AcCarUi.GetString(AcCarUi.TryRead(_temp.Combine("cars", "small"), out _), "name"));
    }

    [Fact]
    public void A_track_written_loosely_is_still_a_track()
    {
        _temp.File("tracks/loose/ui/ui_track.json", """
            { "Name": "Loose Strip", // a note
              "tags": "drag", "pitboxes": 20, "length": 402.3, "run": "dragstrip", }
            """);
        _temp.File("tracks/loose/ui/quarter/ui_track.json", """{ "name": "Quarter", "tags": "drag", "pitboxes": 4 }""");
        _temp.File("tracks/huge/ui/ui_track.json", "{ \"name\": \"Huge\", \"x\": \"" + new string('x', (int)AcUiJson.MaxBytes) + "\" }");
        _temp.File("tracks/broken/ui/ui_track.json", "{ \"name\": ");

        var tracks = new AssettoCorsaContentService().LoadTracks(_temp.Combine("tracks"));

        var track = Assert.Single(tracks);
        Assert.Equal("Loose Strip", track.Name);
        Assert.Equal(["drag"], track.Tags);
        Assert.Equal("20", track.Pitboxes);
        Assert.Equal("402.3", track.Length);
        var layout = Assert.Single(track.Configurations);
        Assert.Equal("quarter", layout.FolderName);
        Assert.Equal("4", layout.Pitboxes);
        Assert.Equal(["drag"], layout.Tags);
    }

    // ---- the mod books ----

    [Fact]
    public void A_mod_job_or_kit_size_with_spaces_round_its_id_replaces_or_takes_out_the_one_of_that_id()
    {
        _temp.File("base/rebuilds.json", """{ "jobs": [ { "id": "a", "kinds": [ "carburettor" ] }, { "id": "b", "kinds": [ "pistons" ] } ] }""");
        _temp.File("mod/rebuilds.json", """{ "jobs": [ { "id": " a ", "kinds": [ "camshaft" ] }, { "id": "b ", "disabled": true } ] }""");
        _temp.File("base/engine_kits.json", """{ "sizes": [ { "id": "short_block", "kinds": [ "block" ] }, { "id": "complete", "all": true } ] }""");
        _temp.File("mod/engine_kits.json", """{ "sizes": [ { "id": " short_block", "kinds": [ "oil_pan" ] }, { "id": "complete ", "disabled": true } ] }""");
        var roots = new[] { (ModIds.BaseId, true, _temp.Combine("base")), ("mymod", false, _temp.Combine("mod")) };

        var problems = new List<string>();
        var jobs = PartRebuilds.Load(roots, problems);
        var kits = EngineKits.Load(roots, problems);

        var job = Assert.Single(jobs.All);
        Assert.Equal("a", job.Id);
        Assert.Equal(["camshaft"], job.Kinds);
        Assert.Equal("mymod/rebuilds.json", job.Source);
        var size = Assert.Single(kits.Sizes);
        Assert.Equal("short_block", size.Id);
        Assert.Equal(["oil_pan"], size.Kinds);
        Assert.Empty(problems);
    }

    [Fact]
    public void A_book_file_that_does_not_read_is_told_and_the_next_one_is_read()
    {
        _temp.File("base/work_order.json", "{ \"kinds\": [ ");
        _temp.File("mod/rebuilds.json", "null");
        var problems = new List<string>();

        ModBookFile.ReadEach<Newtonsoft.Json.Linq.JObject>([(ModIds.BaseId, true, _temp.Combine("base")), ("mymod", false, _temp.Combine("mod"))],
            "work_order.json", problems, "its rules are left out", (_, _) => Assert.Fail("nothing reads"));
        var empty = PartRebuilds.Load([("mymod", false, _temp.Combine("mod"))], problems);

        var told = Assert.Single(problems);
        Assert.StartsWith("work_order.json: ", told);
        Assert.EndsWith("its rules are left out", told);
        Assert.Empty(empty.All);
    }

    // ---- deep trees ----

    [Fact]
    public void A_build_list_chained_deeper_than_a_save_may_hang_stops_at_the_cap()
    {
        var parts = new JsonArray { JsonNode.Parse("""{ "id": "engines/deep/block", "name": "block", "class": "engine_block", "slots": [ { "id": 1, "takes": [ "t:link" ] } ] }""") };
        var ids = new List<string> { "engines/deep/block" };
        for (var i = 0; i < Models.GameState.PartInstance.MaxDepth + 6; i++)
        {
            parts.Add(JsonNode.Parse($$"""{ "id": "engines/deep/link{{i}}", "name": "link", "slots": [ { "id": 1, "fits": [ "t:link" ] }, { "id": 2, "takes": [ "t:link" ] } ] }"""));
            ids.Add($"engines/deep/link{i}");
        }

        _temp.Parts("parts/engines/deep", parts.ToJsonString());
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));

        var tree = PartTreeBuilder.Build(catalog, ids, PartClasses.EngineBlock)!;

        var all = tree.Root.SelfAndDescendants().ToList();
        Assert.Equal(Models.GameState.PartInstance.MaxDepth + 1, all.Count);
        Assert.Equal(6, tree.Unplaced.Count);
        Assert.Equal(Models.GameState.PartInstance.MaxDepth, all[^1].Depth);
    }

    [Fact]
    public void A_tree_is_walked_each_part_before_what_is_on_it_in_the_order_they_are_mounted()
    {
        InstalledPart Part(string id) => new(new PartDefinition { Id = id });
        var root = Part("root");
        var a = Part("a");
        var b = Part("b");
        root.Mount(5, a, 1);
        root.Mount(2, b, 1);
        a.Mount(1, Part("a1"), 1);
        a.Mount(2, Part("a2"), 1);
        b.Mount(1, Part("b1"), 1);

        Assert.Equal(["root", "a", "a1", "a2", "b", "b1"], root.SelfAndDescendants().Select(p => p.Definition.Id));
        Assert.Equal(0, root.Depth);
        Assert.Equal(2, b.Children[1].Depth);
    }

    // ---- the rules ----

    private RuleBook Rules(string json, List<string> problems)
    {
        _temp.File("mod/rules/mine.json", json);
        var merged = ModMerge.NewObject();
        ModMerge.Into(merged, ModMerge.ContentFiles(ModSet.BaseOnly(RepoPaths.BaseContent), ContentTypes.Rules, problems));
        ModMerge.Into(merged, ModMerge.ContentFiles(ModSet.BaseOnly(_temp.Combine("mod")), ContentTypes.Rules, problems));
        return RuleBook.Read(merged, problems);
    }

    [Fact]
    public void The_cores_rules_hold_every_figure_they_are_checked_by()
    {
        var problems = new List<string>();
        RuleBook.Read(ModMerge.ContentFiles(ModSet.BaseOnly(RepoPaths.BaseContent), ContentTypes.Rules, problems), problems);

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("kmPerEarlierOwner", 0)]
    [InlineData("kmPerEarlierOwner", -40000)]
    [InlineData("mileageStepKm", 0)]
    [InlineData("mileageStepKm", -10000)]
    public void A_market_figure_the_game_divides_by_is_never_0_or_below(string figure, double value)
    {
        var problems = new List<string>();
        var book = Rules($$"""{ "market": { "{{figure}}": {{value}} } }""", problems);

        Assert.Equal(40_000, book.Market.KmPerEarlierOwner);
        Assert.Equal(10_000, book.Market.MileageStepKm);
        Assert.Contains(problems, p => p.Contains("market." + figure));
    }

    [Fact]
    public void The_streets_the_polices_the_rivals_and_the_conditions_figures_are_held_too()
    {
        var problems = new List<string>();
        var book = Rules("""
            { "street": { "dayWait": 0, "dragShareByDay": 2 },
              "police": { "nightChance": 1.5, "nightFromHour": 30 },
              "rivals": { "leaveChance": -1, "money": { "spread": 3 }, "boss": { "carCondition": 9 } },
              "condition": { "lowOil": 4, "dealerRust": 50 } }
            """, problems);

        Assert.Equal(new StreetRules().DayWait, book.Street.DayWait);
        Assert.Equal(new StreetRules().DragShareByDay, book.Street.DragShareByDay);
        Assert.Equal(new PoliceRules().NightChance, book.Police.NightChance);
        Assert.Equal(new PoliceRules().NightFromHour, book.Police.NightFromHour);
        Assert.Equal(new RivalRules().LeaveChance, book.Rivals.LeaveChance);
        Assert.Equal(new RivalMoneyRules().Spread, book.Rivals.Money.Spread);
        Assert.Equal(new BossRules().CarCondition, book.Rivals.Boss.CarCondition);
        Assert.Equal(new ConditionRules().LowOil, book.Condition.LowOil);
        Assert.Equal(new ConditionRules().DealerRust, book.Condition.DealerRust);
        Assert.Equal(9, problems.Count);
    }

    // ---- the mod.json sections ----

    [Fact]
    public void A_mod_json_section_is_read_anew_once_the_file_changes()
    {
        var manifest = _temp.File("mymod/mod.json", """{ "id": "mymod", "sounds": { "a": 1 } }""");
        var mod = new ModInfo { Id = "mymod", Folder = _temp.Combine("mymod") };

        Assert.Equal("""{ "a": 1 }""", mod.Section(ModSections.Sounds));
        Assert.Null(mod.Section(ModSections.Builds));

        File.WriteAllText(manifest, """{ "id": "mymod", "SOUNDS": { "b": 22 }, "builds": null }""");
        File.SetLastWriteTimeUtc(manifest, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal("""{ "b": 22 }""", mod.Section(ModSections.Sounds));
        Assert.Null(mod.Section(ModSections.Builds));
    }

    // ---- small helpers ----

    [Fact]
    public void A_boxs_corners_and_the_box_round_it_turned()
    {
        var min = new Vector3(-1, 0, -2);
        var max = new Vector3(1, 3, 2);

        Assert.Equal(8, Box3.Corners(min, max).Distinct().Count());
        Assert.Equal(min, Box3.Corner(min, max, 0));
        Assert.Equal(max, Box3.Corner(min, max, 7));
        var (low, high) = Box3.Bounds(min, max, Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(10, 0, 0));
        Assert.Equal(8, low.X, 4);
        Assert.Equal(12, high.X, 4);
        Assert.Equal(-1, low.Z, 4);
        Assert.Equal(1, high.Z, 4);
    }

    [Fact]
    public void Wear_that_is_no_number_is_taken_as_new()
    {
        var brake = new PartDefinition { Id = "brakes/x", Specs = { ["force"] = 1000.0, ["radius"] = 0.1 } };

        Assert.Equal(RunningGear.BrakeTorque(brake, 1), RunningGear.BrakeTorque(brake, double.NaN));
        Assert.Equal(RunningGear.BrakeTorque(brake, 0), RunningGear.BrakeTorque(brake, -3));
    }
}

/// <summary>The setting ids of the 2026-10-25 audit: a resting setting's ids stay its own, a mod read again as an add-on is one</summary>
[Collection(GlobalsCollection.Name)]
public sealed class Audit1025DSettingIdsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ModSet _before = ModSet.Installed;

    public void Dispose()
    {
        ModSet.Play(_before);
        _temp.Dispose();
    }

    private void Mod(string folder, string manifest) => _temp.File($"{folder}/{ModSet.ManifestFileName}", manifest);

    [Fact]
    public void A_resting_settings_id_written_in_full_stays_its_own_while_another_plays()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("audit_d_rods", """{ "id": "audit_d_rods", "type": "setting" }""");
        Mod("audit_d_touge", """{ "id": "audit_d_touge", "type": "setting" }""");
        var rods = ModSet.Load(_temp.Path, setting: "audit_d_rods");

        ModSet.Play(rods);

        Assert.Equal("x", ModIds.Normalize("audit_d_rods:x"));
        Assert.Equal("audit_d_touge:x", ModIds.Normalize("audit_d_touge:x"));
        Assert.False(ModIds.GoesBare("audit_d_touge"));

        // The core alone again: no setting plays, so none rests beside one
        ModSet.Play(ModSet.CoreOf(_temp.Path));
        Assert.Equal("x", ModIds.Normalize("audit_d_touge:x"));
    }

    [Fact]
    public void A_mod_read_again_as_an_add_on_is_one()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("audit_d_flip", """{ "id": "audit_d_flip", "type": "setting" }""");
        ModSet.Load(_temp.Path, setting: "audit_d_flip");
        Assert.True(ModIds.GoesBare("audit_d_flip"));

        Mod("audit_d_flip", """{ "id": "audit_d_flip" }""");
        ModSet.Load(_temp.Path);

        Assert.False(ModIds.GoesBare("audit_d_flip"));
        Assert.Equal("audit_d_flip:x", ModIds.Qualify("audit_d_flip", "x"));
    }
}
