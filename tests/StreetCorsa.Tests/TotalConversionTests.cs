using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using StreetCorsa.Configuration;
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
[Collection(GlobalsCollection.Name)]
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

    [Fact]
    public void A_null_is_the_base_games_figure_and_no_entry_in_a_list()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "time": { "actions": null, "perPart": null },
              "partShops": { "kinds": null },
              "rivals": { "king": null, "money": { "byAge": [null, { "under": 30, "low": 100, "high": 200 }] } }
            }
            """, problems);

        Assert.Equal(120, book.Time.Of(GameAction.BuyCar));
        Assert.Equal(new TimeRules().PerPart, book.Time.PerPart);
        Assert.Equal(1.1, book.PartShops.For("speed").PriceFactor);
        Assert.Equal(new KingRules().Money, book.Rivals.King.Money);
        Assert.Single(book.Rivals.Money.ByAge);
        Assert.Contains(problems, p => p.Contains("time.actions is null"));
        Assert.Contains(problems, p => p.Contains("rivals.money.byAge[0] is null"));
    }

    [Fact]
    public void A_least_and_a_most_the_wrong_way_round_are_turned_round()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "market": { "maxMileage": 3000 },
              "street": { "longestWait": 3 },
              "rivals": { "money": { "byAge": [{ "under": 99, "low": 500, "high": 100 }] } },
              "partShops": { "kinds": { "wheels": { "stockLow": 90 } } }
            }
            """, problems);

        Assert.Equal((3000, 5000), (book.Market.MinMileage, book.Market.MaxMileage));
        Assert.Equal((3, 5), (book.Street.ShortestWait, book.Street.LongestWait));
        Assert.Equal((100m, 500m), (book.Rivals.Money.ByAge[0].Low, book.Rivals.Money.ByAge[0].High));
        Assert.Equal((60, 90), (book.PartShops.For("wheels").StockLow, book.PartShops.For("wheels").StockHigh));
        Assert.Contains(problems, p => p.Contains("market.minMileage (5000) is over market.maxMileage (3000)"));
        Assert.Contains(problems, p => p.Contains("partShops.kinds.wheels.stockLow"));
    }

    [Fact]
    public void Names_in_the_entries_of_lists_and_tables_are_checked_too()
    {
        var problems = new List<string>();
        Read("""
            {
              "start": { "cars": [{ "carr": "ks_toyota_ae86" }] },
              "rivals": { "money": { "byAge": [{ "undr": 30 }] } },
              "partShops": { "kinds": { "speed": { "priceFactr": 1.2 } } },
              "time": { "actions": { "AnythingAtAll": 5 } }
            }
            """, problems);

        Assert.Contains(problems, p => p.Contains("start.cars[0].carr is no figure"));
        Assert.Contains(problems, p => p.Contains("rivals.money.byAge[0].undr is no figure"));
        Assert.Contains(problems, p => p.Contains("partShops.kinds.speed.priceFactr is no figure"));
        Assert.DoesNotContain(problems, p => p.Contains("AnythingAtAll"));
    }

    [Fact]
    public void A_new_kind_of_part_shop_given_in_part_takes_the_rest_from_the_other_kind()
    {
        var problems = new List<string>();
        var book = Read("""{ "partShops": { "otherKind": { "restockDays": 9 }, "kinds": { "tuner": { "priceFactor": 1.3 } } } }""", problems);

        var tuner = book.PartShops.For("tuner");
        Assert.Equal(1.3, tuner.PriceFactor);
        Assert.Equal(9, tuner.RestockDays);
        Assert.Equal(book.PartShops.OtherKind.StockHigh, tuner.StockHigh);
        Assert.Empty(problems);
    }

    [Fact]
    public void The_hours_the_day_works_out_are_not_written_as_figures()
    {
        var json = new RuleBook().ToJson();

        Assert.DoesNotContain("firstHour", json);
        Assert.DoesNotContain("lastHour", json);
    }

    [Fact]
    public void A_book_set_aside_for_a_game_being_opened_comes_back_when_it_is_not()
    {
        var before = RuleBook.InPlay();
        var opened = new GameState { RuleBook = new RuleBook { Police = new PoliceRules { DayChance = 0.9 } }.ToJson(), SaveName = "b" };

        RuleBook.PlayBy(opened);
        try
        {
            Assert.Equal(0.9, RuleBook.Current.Police.DayChance);
            before.Restore();
            Assert.Equal(RuleBook.Installed.Police.DayChance, RuleBook.Current.Police.DayChance);
        }
        finally
        {
            RuleBook.PlayBy(null);
        }
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
        Assert.True(mods.Get("total")!.IsSetting);
        Assert.False(mods.Get("addon")!.IsSetting);
        Assert.Same(mods.Get("total"), mods.Setting);
        Assert.Empty(mods.Problems);
    }

    [Fact]
    public void One_setting_plays_and_the_others_rest_with_the_add_ons_that_need_them()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("rods", """{ "id": "rods", "type": "setting" }""");
        Mod("drift", """{ "id": "drift", "type": "setting" }""");
        Mod("rod_parts", """{ "id": "rod_parts", "requires": { "rods": "" } }""");
        Mod("rod_parts_more", """{ "id": "rod_parts_more", "requires": { "rod_parts": "" } }""");
        Mod("anywhere", """{ "id": "anywhere" }""");

        var mods = ModSet.Load(_temp.Path, setting: "drift");

        Assert.Equal([ModIds.BaseId, "anywhere", "drift"], mods.Mods.Select(m => m.Id));
        Assert.Equal("drift", mods.Setting?.Id);
        Assert.Equal(["drift", "rods"], mods.Settings.Select(m => m.Id));
        Assert.Equal(["rods", "rod_parts", "rod_parts_more"], mods.Resting.Select(m => m.Id));
        Assert.Empty(mods.Problems);

        var rods = ModSet.Load(_temp.Path, setting: "rods");
        Assert.Equal(["anywhere", "rod_parts", "rod_parts_more", "rods", ModIds.BaseId], rods.Mods.Select(m => m.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["drift"], rods.Resting.Select(m => m.Id));
    }

    [Fact]
    public void A_setting_not_installed_plays_the_first_one_and_none_is_the_bare_core()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("later", """{ "id": "later", "type": "setting", "order": 5 }""");
        Mod("sooner", """{ "id": "sooner", "type": "game", "order": 1 }""");

        Assert.Equal("sooner", ModSet.Load(_temp.Path, setting: "gone").Setting?.Id);
        Assert.Equal("sooner", ModSet.Load(_temp.Path).Setting?.Id);

        using var bare = new TempDir();
        bare.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", """{ "id": "streetcorsa" }""");
        var core = ModSet.Load(bare.Path, setting: "sooner");
        Assert.Null(core.Setting);
        Assert.Empty(core.Settings);
        Assert.Empty(core.Resting);
    }

    [Fact]
    public void A_restart_into_another_setting_says_what_it_opens_on()
    {
        static IReadOnlyList<string> Args(StartAfter then) => ["Street Corsa.exe", .. SettingStart.Arguments(42, then)];

        Assert.Equal(["--after", "42"], SettingStart.Arguments(42, StartAfter.Menu));
        Assert.Equal(new StartAfter(StartKind.NewGame, "Bo Duke"), SettingStart.Then(Args(new StartAfter(StartKind.NewGame, "Bo Duke"))));
        Assert.Equal(new StartAfter(StartKind.NewGame, ""), SettingStart.Then(Args(new StartAfter(StartKind.NewGame, ""))));
        Assert.Equal(new StartAfter(StartKind.LoadGame, "bo"), SettingStart.Then(Args(new StartAfter(StartKind.LoadGame, "bo"))));
        Assert.Equal(StartAfter.Menu, SettingStart.Then(["Street Corsa.exe"]));
        Assert.Equal(StartAfter.Menu, SettingStart.Then(["Street Corsa.exe", "--then", "load"]));
        Assert.Equal(StartAfter.Menu, SettingStart.Then(["Street Corsa.exe", "--then", "dance", "x"]));
    }

    [Fact]
    public void A_restarted_run_waits_for_the_one_before_to_close()
    {
        using var earlier = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -Command Start-Sleep -Seconds 2")
            { UseShellExecute = false, CreateNoWindow = true })!;
        var waited = System.Diagnostics.Stopwatch.StartNew();

        SettingStart.AwaitEarlier(["Street Corsa.exe", "--after", earlier.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]);

        Assert.True(earlier.HasExited);
        Assert.True(waited.Elapsed > TimeSpan.FromSeconds(1), $"waited {waited.Elapsed}");

        // The wait runs before the log is opened: a static logger would fail the class's initializer there, and the wait
        // with it (the tests open the log, so only this says so)
        Assert.DoesNotContain(typeof(SettingStart).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public),
            f => typeof(StreetCorsa.Logging.IAppLogger).IsAssignableFrom(f.FieldType));
    }

    [Fact]
    public void A_save_of_a_resting_setting_is_for_another_run()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("rods", """{ "id": "rods", "type": "setting", "name": "Street Rod" }""");
        Mod("drift", """{ "id": "drift", "type": "setting" }""");
        var mods = ModSet.Load(_temp.Path, setting: "drift");

        GameState Played(params string[] ids) => new() { Mods = ids.Select(id => new ModRef { Id = id }).ToList() };

        Assert.Equal("Street Rod", Services.Storage.ModsInSave.OtherSetting(Played(ModIds.BaseId, "rods"), mods)?.Name);
        Assert.Null(Services.Storage.ModsInSave.OtherSetting(Played(ModIds.BaseId, "drift"), mods));
        Assert.Null(Services.Storage.ModsInSave.OtherSetting(Played(ModIds.BaseId, "gone"), mods));
        Assert.Null(Services.Storage.ModsInSave.OtherSetting(Played(ModIds.BaseId), mods));

        // The save's own setting first, whatever its mods say
        Assert.Equal("rods", Services.Storage.ModsInSave.OtherSetting(new GameState { Setting = "rods" }, mods)?.Id);
        Assert.Null(Services.Storage.ModsInSave.OtherSetting(new GameState { Setting = "drift", Mods = [new ModRef { Id = "rods" }] }, mods));
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
    public void A_mod_removes_what_the_mods_before_it_have_and_not_what_it_or_a_later_mod_adds()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/text/words.json", """{ "lines": { "greeting": "Howdy" } }""");
        _temp.File($"{ModIds.BaseId}/sfx/cues.json", """{ "cues": { "click": { "bus": "interface", "files": ["a.ogg"] } } }""");
        _temp.File($"{ModIds.BaseId}/sfx/a.ogg", "x");
        Mod("tokyo", """{ "id": "tokyo", "order": 1, "removes": { "text": ["greeting"], "sfx": ["click"] } }""");
        _temp.File("tokyo/text/words.json", """{ "lines": { "greeting": "Konnichiwa" } }""");
        _temp.File("tokyo/sfx/cues.json", """{ "cues": { "click": { "bus": "interface", "files": ["b.ogg"] } } }""");
        _temp.File("tokyo/sfx/b.ogg", "x");
        Mod("later", """{ "id": "later", "order": 2 }""");
        _temp.File("later/text/words.json", """{ "lines": { "greeting": "Yo" } }""");

        var mods = Load();
        var text = GameText.Load(mods);
        var cues = StreetCorsa.Audio.SoundCues.Load(mods);

        Assert.Equal(["Konnichiwa", "Yo"], text.Lines("greeting"));
        Assert.EndsWith(Path.Combine("tokyo", "sfx", "b.ogg"), cues.Get("click")!.Files.Single());
        Assert.DoesNotContain(text.Problems, p => p.Contains("greeting"));
    }

    [Fact]
    public void Removes_null_is_no_removal()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("empty", """{ "id": "empty", "removes": null, "requires": null }""");

        var mods = Load();

        Assert.NotNull(mods.Get("empty"));
        Assert.Empty(mods.Problems);
    }

    [Fact]
    public void A_file_with_a_name_twice_in_an_object_is_left_out_whole()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/{ModSet.GameFileName}", """{ "title": "Street Corsa" }""");
        Mod("twice", """{ "id": "twice" }""");
        _temp.File($"twice/{ModSet.GameFileName}", """{ "garage": "mine", "title": "A", "Title": "B" }""");
        _temp.File("twice/rules/police.json", """{ "police": { "dayChance": 1, "DAYCHANCE": 2 }, "news": { "daysKept": 3 } }""");

        var mods = Load();
        var problems = new List<string>();
        var game = ModMerge.ModFiles(mods, ModSet.GameFileName, problems);
        var rules = ModMerge.ContentFiles(mods, ContentTypes.Rules, problems);

        Assert.Equal("Street Corsa", (string)game["title"]!);
        Assert.False(game.ContainsKey("garage"));
        Assert.Empty(rules);
        Assert.Equal(2, problems.Count(p => p.Contains("twice in one object")));
        Assert.Null(ModMerge.Parse("""{ "a": 1, "A": 2 }"""));
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
        Assert.False(AppSettings.IsInstall(_temp.Combine("ac")));
        Directory.CreateDirectory(_temp.Combine("ac", "content", "tracks"));
        Assert.True(AppSettings.IsInstall(_temp.Combine("ac")));
        Assert.False(AppSettings.IsInstall(null));
    }
}
