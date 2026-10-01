using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Rules;
using StreetCorsa.Services.Settings;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Tests;

/// <summary>
/// The rules of a career as data (rules/*.json, <see cref="RuleBook"/>): the base game's files hold exactly the code's
/// figures, a mod changes one figure without repeating the rest, a save keeps the rules it was started with
/// </summary>
public class RuleBookTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string BaseRulesFolder => Path.Combine(RepoPaths.BaseContent, ContentTypes.Rules);

    /// <summary>The parts of the book, as their files and top-level names have them</summary>
    private static IEnumerable<PropertyInfo> Parts() =>
        typeof(RuleBook).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite);

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>
    /// The base game's rules files are the code's figures written out, so the game plays exactly as it did before the
    /// rules were data. Set SC_WRITE_BASE_RULES=1 to write them again from the code after a figure is added or changed.
    /// </summary>
    [Fact]
    public void The_base_games_rules_files_hold_the_codes_figures()
    {
        var defaults = new RuleBook();
        if (Environment.GetEnvironmentVariable("SC_WRITE_BASE_RULES") == "1")
        {
            Directory.CreateDirectory(BaseRulesFolder);
            foreach (var part in Parts())
            {
                var name = Camel(part.Name);
                var file = new JsonObject
                {
                    ["_comment"] = new JsonArray(
                        $"The base game's \"{name}\" rules: every figure the game plays by, as the code has them.",
                        "A mod changes any of them with a .json file of its own in its rules folder, naming only what it changes",
                        "(see docs/modding.md, \"rules\"). A career keeps the rules it was started with."),
                    [name] = JsonSerializer.SerializeToNode(part.GetValue(defaults), part.PropertyType, RuleBook.Options)
                };
                File.WriteAllText(Path.Combine(BaseRulesFolder, name.ToLowerInvariant() + ".json"),
                    file.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
            }
        }

        var problems = new List<string>();
        var merged = ModMerge.ContentFiles(ModSet.BaseOnly(RepoPaths.BaseContent), ContentTypes.Rules, problems);
        var book = RuleBook.Read(merged, problems);

        Assert.Empty(problems);
        foreach (var part in Parts()) Assert.True(merged.ContainsKey(Camel(part.Name)), $"no base rules file has \"{Camel(part.Name)}\"");
        Assert.Equal(defaults.ToJson(), book.ToJson());
    }

    private RuleBook Read(string json, List<string>? problems = null)
    {
        _temp.File("mod/rules/mine.json", json);
        problems ??= [];
        var merged = ModMerge.NewObject();
        ModMerge.Into(merged, ModMerge.ContentFiles(ModSet.BaseOnly(RepoPaths.BaseContent), ContentTypes.Rules, problems));
        var mods = ModSet.BaseOnly(_temp.Combine("mod"));
        ModMerge.Into(merged, ModMerge.ContentFiles(mods, ContentTypes.Rules, problems));
        return RuleBook.Read(merged, problems);
    }

    [Fact]
    public void A_mod_changes_one_figure_and_keeps_the_rest()
    {
        var book = Read("""{ "Police": { "dayChance": 0.2 }, "time": { "actions": { "BuyCar": 90 } } }""");

        Assert.Equal(0.2, book.Police.DayChance);
        Assert.Equal(new PoliceRules().NightChance, book.Police.NightChance);
        // A table given in part keeps the rest of its entries
        Assert.Equal(90, book.Time.Of(GameAction.BuyCar));
        Assert.Equal(30, book.Time.Of(GameAction.VisitNewspaper));
    }

    [Fact]
    public void Lists_are_taken_whole()
    {
        var book = Read("""{ "world": { "summerMonths": [12, 1, 2], "winterMonths": [6, 7, 8], "midsummer": 355 } }""");

        Assert.Equal([12, 1, 2], book.World.SummerMonths);
        Assert.Equal([6, 7, 8], book.World.WinterMonths);
    }

    [Fact]
    public void Names_the_rules_do_not_know_and_parts_that_do_not_read_are_told_and_left_at_the_base_games()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "_comment": "notes are passed over",
              "polise": { "dayChance": 1 },
              "police": { "dayChanse": 1, "nightChance": 0.4 },
              "market": { "adFee": "a lot" }
            }
            """, problems);

        Assert.Contains(problems, p => p.Contains("\"polise\" is no part of the rules"));
        Assert.Contains(problems, p => p.Contains("police.dayChanse is no figure"));
        Assert.Contains(problems, p => p.Contains("\"market\" could not be read"));
        Assert.DoesNotContain(problems, p => p.Contains("_comment"));
        Assert.Equal(0.4, book.Police.NightChance);
        Assert.Equal(new MarketRules().AdFee, book.Market.AdFee);
    }

    [Fact]
    public void A_save_keeps_the_rules_it_was_started_with_and_takes_new_figures_from_the_mods()
    {
        var started = JsonNode.Parse(new RuleBook { Police = new PoliceRules { DayChance = 0.25 } }.ToJson())!.AsObject();
        // A figure the game has gained since the save was started: not in it
        started["police"]!.AsObject().Remove("nightChance");

        var book = RuleBook.ForSave(started.ToJsonString());

        Assert.Equal(0.25, book.Police.DayChance);
        Assert.Equal(RuleBook.Installed.Police.NightChance, book.Police.NightChance);
    }

    [Fact]
    public void A_save_from_before_the_rules_plays_by_the_mods_and_keeps_them_from_then_on()
    {
        var game = new GameState { RuleBook = null, SaveName = "old" };

        RuleBook.PlayBy(game);
        try
        {
            Assert.Equal(RuleBook.Installed.ToJson(), game.RuleBook);
            Assert.Equal(RuleBook.Installed.ToJson(), RuleBook.Current.ToJson());
        }
        finally
        {
            RuleBook.PlayBy(null);
        }
    }

    [Fact]
    public void A_new_career_starts_on_the_rules_it_is_given()
    {
        var book = new RuleBook
        {
            Start = new StartRules { Money = 4000m, Date = new DateOnly(1986, 4, 1) },
            Day = new DayRules { StartHour = 10, EndHour = 26 }
        };

        var state = GameState.CreateNew("Takumi", book: book);

        Assert.Equal(4000m, state.Player.Money);
        Assert.Equal(new DateTime(1986, 4, 1, 10, 0, 0), state.Date);
        Assert.Equal(24, book.Day.LastHour);
        Assert.Equal(book.ToJson(), state.RuleBook);
    }

    [Fact]
    public void A_difficulty_preset_comes_from_the_rules()
    {
        var easy = GameRules.For(Difficulty.Easy);
        var custom = GameRules.For(Difficulty.Custom);

        Assert.Equal(RuleBook.Installed.Difficulty.Easy.CarPriceMultiplier, easy.CarPriceMultiplier);
        Assert.Equal(PinkSlipFrequency.Low, easy.PinkSlipFrequency);
        Assert.Equal(Difficulty.Normal, custom.Difficulty);
        Assert.Equal(0.5, new RuleBook().Difficulty.PinkSlipFactor(PinkSlipFrequency.Low));
    }

    [Fact]
    public void A_kind_of_part_shop_the_rules_leave_out_takes_the_other_kinds_figures()
    {
        var rules = new PartShopRules();

        Assert.Equal(1.1, rules.For("Speed").PriceFactor);
        Assert.Equal(0.42, rules.For("junkyard").BuyFactor);
        Assert.Same(rules.OtherKind, rules.For("Specialist"));
    }
}

/// <summary>mod.json's type, requires, gameVersion and removes; game.json; the problems the player is told of</summary>
public class ModFoundationsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private void Mod(string id, string manifest) => _temp.File($"{id}/{ModSet.ManifestFileName}", manifest);

    private ModSet Load(string version = "1.0") => ModSet.Load(_temp.Path, ModVersion.TryParse(version, out var v) ? v : null);

    [Fact]
    public void A_mod_without_what_it_requires_is_left_out_and_so_is_what_needs_it()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("needs_ghost", """{ "id": "needs_ghost", "requires": { "ghost": "" } }""");
        Mod("needs_needs", """{ "id": "needs_needs", "requires": { "needs_ghost": "1.0" } }""");
        Mod("needs_new", """{ "id": "needs_new", "requires": { "old": 2 } }""");
        Mod("old", """{ "id": "old", "version": "1.5" }""");

        var mods = Load();

        Assert.Equal([ModIds.BaseId, "old"], mods.Mods.Select(m => m.Id));
        Assert.Contains(mods.Problems, p => p.Contains("needs_ghost") && p.Contains("\"ghost\""));
        Assert.Contains(mods.Problems, p => p.StartsWith("needs_needs"));
        Assert.Contains(mods.Problems, p => p.Contains("needs \"old\" at 2.0.0 or later, which is at 1.5"));
    }

    [Fact]
    public void A_mod_loads_after_the_mods_it_requires_whatever_its_order()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("addon", """{ "id": "addon", "order": 1, "requires": { "total": "" } }""");
        Mod("total", """{ "id": "total", "order": 50, "type": "game" }""");
        Mod("other", """{ "id": "other", "order": 10 }""");

        var mods = Load();

        Assert.Equal([ModIds.BaseId, "other", "total", "addon"], mods.Mods.Select(m => m.Id));
        Assert.True(mods.Get("total")!.IsGame);
        Assert.False(mods.Get("addon")!.IsGame);
        Assert.Empty(mods.Problems);
    }

    [Fact]
    public void A_mod_for_a_newer_game_is_left_out()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("future", """{ "id": "future", "gameVersion": "0.3" }""");
        Mod("today", """{ "id": "today", "gameVersion": "0.1.0" }""");
        Mod("odd", """{ "id": "odd", "type": "mission" }""");

        var mods = Load("0.1.0");

        Assert.Equal([ModIds.BaseId, "odd", "today"], mods.Mods.Select(m => m.Id));
        Assert.Contains(mods.Problems, p => p.Contains("needs the game at 0.3.0 or later, this is 0.1.0"));
        Assert.Contains(mods.Problems, p => p.Contains("\"mission\" is neither"));
    }

    [Fact]
    public void Versions_read_as_mods_write_them()
    {
        Assert.True(ModVersion.TryParse("1", out var one));
        Assert.True(ModVersion.TryParse("v1.2-beta", out var beta));
        Assert.False(ModVersion.TryParse("new", out _));
        Assert.Equal(new ModVersion(1, 0, 0), one);
        Assert.Equal(new ModVersion(1, 2, 0), beta);
        Assert.True(beta > one);
    }

    [Fact]
    public void What_a_mod_removes_is_out_of_the_game()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/text/words.json", """{ "lines": { "greeting": "Howdy", "farewell": "So long" } }""");
        _temp.File($"{ModIds.BaseId}/events/events.json", """{ "events": [ { "id": "derby" }, { "id": "cup" } ] }""");
        Mod("tokyo", """{ "id": "tokyo", "removes": { "text": ["greeting"], "events": ["derby", "nothing_here"] } }""");

        var mods = Load();
        var text = GameText.Load(mods);
        var problems = new List<string>();
        var events = ModJson.Load(mods, ContentTypes.Events, "events", "event", problems, e => e.Id);

        Assert.False(text.Has("greeting"));
        Assert.True(text.Has("farewell"));
        Assert.Equal(["cup"], events.Entries.Select(e => e.Id));
        Assert.Contains(events.Replaced, r => r.Contains("derby") && r.Contains("removed"));
        Assert.Contains(problems, p => p.Contains("nothing_here"));
        Assert.True(mods.IsRemoved(ContentTypes.Events, "streetcorsa:derby"));
    }

    [Fact]
    public void Game_json_goes_over_the_mods_before_it_key_by_key()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/{ModSet.GameFileName}", """{ "title": "Street Corsa", "garage": "garage" }""");
        Mod("tokyo", """{ "id": "tokyo" }""");
        _temp.File($"tokyo/{ModSet.GameFileName}", """{ "_comment": "a note", "Title": "Wangan Nights" }""");

        var game = GameFile.Load(Load());

        Assert.Equal("Wangan Nights", game.Title);
        Assert.Equal("garage", game.Garage);
    }

    [Fact]
    public void Merging_goes_into_objects_and_replaces_the_rest()
    {
        var into = ModMerge.Parse("""{ "a": { "x": 1, "y": [1, 2] }, "b": 2 }""")!;
        ModMerge.Into(into, ModMerge.Parse("""{ "A": { "Y": [3], "z": true }, "_note": 1 }""")!);

        Assert.Equal(1, (int)into["a"]!["x"]!);
        Assert.Equal([3], into["a"]!["y"]!.AsArray().Select(n => (int)n!));
        Assert.True((bool)into["a"]!["z"]!);
        Assert.False(into.ContainsKey("_note"));
    }

    [Fact]
    public void Each_problem_is_kept_once()
    {
        var area = "Test " + Guid.NewGuid();
        ModProblems.Add(area, ["one", "two"]);
        ModProblems.Add(area, "one");

        Assert.Equal(2, ModProblems.All.Count(p => p.StartsWith(area)));
    }

    [Fact]
    public void Steams_library_folders_are_read_from_its_vdf()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0" { "path"    "C:\\Program Files (x86)\\Steam"   "apps" { "228980" "1" } }
                "1" { "path"		"D:\\SteamLibrary" }
            }
            """;

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], AcInstallFinder.LibrariesIn(vdf));
    }

    [Fact]
    public void A_folder_is_an_install_when_it_has_cars_and_tracks()
    {
        Directory.CreateDirectory(_temp.Combine("ac", "content", "cars"));
        Assert.False(AcInstallFinder.IsInstall(_temp.Combine("ac")));
        Directory.CreateDirectory(_temp.Combine("ac", "content", "tracks"));
        Assert.True(AcInstallFinder.IsInstall(_temp.Combine("ac")));
        Assert.False(AcInstallFinder.IsInstall(null));
    }
}
