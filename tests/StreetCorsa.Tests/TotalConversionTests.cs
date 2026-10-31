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
/// The rules of a career as data (rules/*.json, <see cref="RuleBook"/>): the core's files hold exactly the code's
/// figures, a setting's only what it changes, a mod changes one figure without repeating the rest, a save keeps
/// the rules it was started with
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
    /// The core's rules files are the code's figures written out, so the game plays exactly as it did before the rules
    /// were data. Set SC_WRITE_BASE_RULES=1 to write them again from the code after a figure is added or changed.
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
                        $"The core's \"{name}\" rules: every figure the game plays by, as the code has them.",
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

    [Fact]
    public void A_career_with_no_start_day_starts_on_the_day_it_is_started()
    {
        var book = new RuleBook();

        Assert.Equal(DateTime.Today.AddHours(book.Day.FirstHour), book.Start.FirstMorning(book.Day));
    }

    [Fact]
    public void A_start_car_with_no_skin_reads_back_from_the_save_without_a_problem()
    {
        var book = new RuleBook { Start = { Cars = [new StartCar { Car = "some_car" }] } };
        var problems = new List<string>();

        var kept = RuleBook.Read((JsonObject)JsonNode.Parse(book.ToJson())!, problems);

        Assert.Empty(problems);
        Assert.Equal("some_car", kept.Start.Cars.Single().Car);
        Assert.Null(kept.Start.Cars.Single().Skin);
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
    public void A_mods_own_kind_of_shop_is_made_of_the_behaviours_there_are_and_a_behaviour_there_is_not_is_told()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "partShops": { "kinds": {
                "tuning_house": { "parts": "speed", "kits": "aftermarket", "priceFactor": 1.3, "dearRarer": true },
                "body_shop": { "stock": "van", "parts": "running_gear", "tuners": false }
              } }
            }
            """, problems);

        var tuning = book.PartShops.For("tuning_house");
        Assert.Equal((PartShopKindRules.ShelvesStock, PartShopKindRules.SpeedParts, PartShopKindRules.AftermarketKits), (tuning.Stock, tuning.Parts, tuning.Kits));
        Assert.Equal(1.3, tuning.PriceFactor);
        // The rest from the other kind's: a specialist's shelves
        Assert.Equal(book.PartShops.OtherKind.StockLow, tuning.StockLow);
        Assert.True(tuning.Tuners);

        var body = book.PartShops.For("body_shop");
        Assert.Equal(PartShopKindRules.ShelvesStock, body.Stock);
        Assert.Equal(PartShopKindRules.RunningGearParts, body.Parts);
        Assert.Contains(problems, p => p.Contains("partShops.kinds.body_shop.stock", StringComparison.Ordinal) && p.Contains("\"van\"", StringComparison.Ordinal));

        var before = RuleBook.InPlay();
        RuleBook.Current = book;
        try
        {
            var shop = new PartShopDefinition { Kind = "Tuning_House" };
            Assert.Equal("tuning_house", shop.KindId);
            Assert.True(Services.PartShops.PartShopRules.SellsKits(shop));
            Assert.True(Services.PartShops.PartShopRules.SellsToTuners(shop));
            Assert.Equal(1.3, Services.PartShops.PartShopRules.PriceFactor(shop));
            Assert.False(Services.PartShops.PartShopRules.SellsToTuners(new PartShopDefinition { Kind = "body_shop" }));
        }
        finally
        {
            before.Restore();
        }
    }

    [Fact]
    public void A_kinds_behaviour_there_is_not_falls_back_on_where_it_starts_and_what_the_counters_cannot_serve_is_told()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "partShops": {
                "otherKind": { "kits": "none", "parts": "bogus" },
                "kinds": {
                  "TuningHouse": { "kits": "Factroy" },
                  "speed": { "kits": "nope" },
                  "lathe": { "machining": true, "stock": "shelves" },
                  "scrap": { "stock": "yard", "kits": "factory" }
                } }
            }
            """, problems);

        // The other kind told under its own path, and a mod's kind falling back on it, a core kind on its own
        Assert.Contains(problems, p => p.StartsWith("partShops.otherKind.parts:", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("kinds.otherKind", StringComparison.Ordinal));
        Assert.Equal(PartShopKindRules.NoKits, book.PartShops.For("TuningHouse").Kits);
        Assert.Equal(PartShopKindRules.AftermarketKits, book.PartShops.For("speed").Kits);
        Assert.Equal(PartShopKindRules.NoStock, book.PartShops.For("lathe").Stock);
        Assert.Equal(PartShopKindRules.NoKits, book.PartShops.For("scrap").Kits);
        Assert.Contains(problems, p => p.StartsWith("partShops.kinds.lathe:", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("partShops.kinds.scrap:", StringComparison.Ordinal));

        var before = RuleBook.InPlay();
        RuleBook.Current = book;
        try
        {
            // The kind in the rules' own spelling, for its text keys
            Assert.Equal("TuningHouse", new PartShopDefinition { Kind = "tuninghouse" }.KindId);
        }
        finally
        {
            before.Restore();
        }
    }

    [Fact]
    public void A_null_is_the_base_games_figure_and_no_entry_in_a_list()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "time": { "actions": null, "perPart": null },
              "partShops": { "kinds": null },
              "rivals": { "boss": null, "money": { "byAge": [null, { "under": 30, "low": 100, "high": 200 }] } }
            }
            """, problems);

        Assert.Equal(120, book.Time.Of(GameAction.BuyCar));
        Assert.Equal(new TimeRules().PerPart, book.Time.PerPart);
        Assert.Equal(1.1, book.PartShops.For("speed").PriceFactor);
        Assert.Equal(new BossRules().Money, book.Rivals.Boss.Money);
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
    public void A_games_own_setting_or_none_plays_never_another()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("sooner", """{ "id": "sooner", "type": "setting", "order": 1 }""");
        Mod("anywhere", """{ "id": "anywhere" }""");

        var none = ModSet.Load(_temp.Path, exactSetting: true);
        Assert.Null(none.Setting);
        Assert.Equal([ModIds.BaseId, "anywhere"], none.Mods.Select(m => m.Id));
        Assert.Equal(["sooner"], none.Settings.Select(m => m.Id));

        Assert.Null(ModSet.Load(_temp.Path, setting: "gone", exactSetting: true).Setting);
        Assert.Equal("sooner", ModSet.Load(_temp.Path, setting: "sooner", exactSetting: true).Setting?.Id);
        Assert.True(none.IsPlayable(null));
        Assert.True(none.IsPlayable("sooner"));
        Assert.False(none.IsPlayable("gone"));
    }

    [Fact]
    public void A_setting_without_what_it_requires_is_not_offered()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("touge", """{ "id": "touge", "type": "setting", "order": 1, "requires": { "jdm_cars": "" } }""");
        Mod("rods", """{ "id": "rods", "type": "setting", "order": 2 }""");

        var mods = ModSet.Load(_temp.Path, setting: "touge");

        Assert.Equal(["rods"], mods.Settings.Select(m => m.Id));
        Assert.Equal("rods", mods.Setting?.Id);
        Assert.Contains(mods.Problems, p => p.StartsWith("touge: the setting cannot be played") && p.Contains("\"jdm_cars\""));
        Assert.False(mods.IsPlayable("touge"));
        Assert.Null(ModSet.Load(_temp.Path, setting: "touge", exactSetting: true).Setting);
    }

    [Fact]
    public void Everything_holds_every_setting_and_plays_none()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("rods", """{ "id": "rods", "type": "setting" }""");
        Mod("drift", """{ "id": "drift", "type": "setting" }""");
        Mod("rod_parts", """{ "id": "rod_parts", "requires": { "rods": "" } }""");

        var all = ModSet.Load(_temp.Path, everything: true);

        Assert.Null(all.Setting);
        Assert.Empty(all.Resting);
        Assert.Equal(["drift", "rod_parts", "rods", ModIds.BaseId], all.Mods.Select(m => m.Id).Order(StringComparer.Ordinal));
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

/// <summary>The content in play switched at run time: the main screen's core, a game's setting, and back</summary>
[Collection(GlobalsCollection.Name)]
public class ContentInPlayTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ModSet _before = ModSet.Installed;

    public void Dispose()
    {
        ModSet.Play(_before);
        _temp.Dispose();
    }

    private void Mod(string id, string manifest) => _temp.File($"{id}/{ModSet.ManifestFileName}", manifest);

    [Fact]
    public void The_core_alone_leaves_every_other_mod_out_but_lists_the_settings()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("rods", """{ "id": "rods", "type": "setting" }""");
        Mod("engines", """{ "id": "engines" }""");

        var core = ModSet.CoreOf(_temp.Path);

        Assert.Equal([ModIds.BaseId], core.Mods.Select(m => m.Id));
        Assert.Null(core.Setting);
        Assert.Equal(["rods"], core.Settings.Select(m => m.Id));
        Assert.Empty(core.Problems);
    }

    [Fact]
    public void Nothing_keeps_the_mods_content_for_good_outside_a_content_cache()
    {
        // A Lazy over the content is read once: it would keep the main screen's core into a game, or a setting after it
        var held = new System.Text.RegularExpressions.Regex(@"(static\s+readonly|readonly)\s+Lazy<[^;]*?(\.Installed\b|ModSet\.)");
        var found = Directory.EnumerateFiles(RepoPaths.GameProject, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => held.IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(RepoPaths.GameProject, l.File)}:{l.Line}")
            .ToList();

        Assert.Empty(found);
    }

    [Fact]
    public void A_content_cache_is_read_again_once_another_content_plays()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("rods", """{ "id": "rods", "type": "setting" }""");
        var reads = 0;
        var cache = new ContentCache<string>(() =>
        {
            reads++;
            return string.Join(",", ModSet.Installed.Mods.Select(m => m.Id));
        });

        ModSet.Play(ModSet.CoreOf(_temp.Path));
        Assert.Equal(ModIds.BaseId, cache.Value);
        Assert.Equal(ModIds.BaseId, cache.Value);
        Assert.Equal(1, reads);

        ModSet.Play(ModSet.Load(_temp.Path, setting: "rods"));
        Assert.Equal($"{ModIds.BaseId},rods", cache.Value);
        Assert.Equal(2, reads);

    }

    [Fact]
    public void A_content_cache_read_while_the_content_switches_keeps_the_new_contents_value()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("rods", """{ "id": "rods", "type": "setting" }""");
        var core = ModSet.CoreOf(_temp.Path);
        var rods = ModSet.Load(_temp.Path, setting: "rods");
        var reads = 0;
        var cache = new ContentCache<string>(() =>
        {
            // The first read is overtaken by a switch: what it read is the core's, kept for neither
            var read = string.Join(",", ModSet.Installed.Mods.Select(m => m.Id));
            if (++reads == 1) ModSet.Play(rods);
            return read;
        });

        ModSet.Play(core);
        Assert.Equal($"{ModIds.BaseId},rods", cache.Value);
        Assert.Equal(2, reads);

        ModSet.Play(core);
        Assert.Equal(ModIds.BaseId, cache.Value);
        Assert.Equal(3, reads);
    }
}
