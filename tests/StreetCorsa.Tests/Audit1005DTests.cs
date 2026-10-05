using Newtonsoft.Json;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Parts.Scripting;
using StreetCorsa.Rules;
using StreetCorsa.Services.Garage;
using StreetCorsa.Services.PartShops;

namespace StreetCorsa.Tests;

/// <summary>The 2026-10-05 audit's fixes in the parts, the mods, the rules and the scene files</summary>
public sealed class Audit1005DTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ----- A part's idle held to an engine's -----

    // An engine of figures, and a part on it that gives its idle
    private const string IdlePack = """
        { "id": "engines/data", "parts": [
          { "id": "engines/data/block", "name": "block", "class_chain": [ "Block" ], "mass": 120,
            "slots": [ { "id": 10, "takes": [ "t:head" ] }, { "id": 11, "takes": [ "t:cam" ] }, { "id": 12, "takes": [ "t:carb" ] },
                       { "id": 13, "takes": [ "t:idle" ] } ],
            "figures": { "cylinders": 4, "bore": 86, "stroke": 86, "compression": 9.5, "rev_limit": 6500, "max_rpm": 7000, "idle_rpm": 850 } },
          { "id": "engines/data/head", "name": "head", "class_chain": [ "CylinderHead" ], "slots": [ { "id": 1, "fits": [ "t:head" ] } ],
            "figures": { "intake_flow": 0.8, "exhaust_flow": 0.35 } },
          { "id": "engines/data/cam", "name": "cam", "class_chain": [ "Camshaft" ], "slots": [ { "id": 1, "fits": [ "t:cam" ] } ],
            "figures": { "intake_duration": 240, "exhaust_duration": 240, "spark_advance": 32 } },
          { "id": "engines/data/carb", "name": "carb", "slots": [ { "id": 1, "fits": [ "t:carb" ] } ],
            "figures": { "fuel_flow": 30, "air_flow": 400 } },
          { "id": "engines/data/fast_idle", "name": "fast idle", "slots": [ { "id": 1, "fits": [ "t:idle" ] } ], "figures": { "idle_rpm": 9000 } },
          { "id": "engines/data/no_idle", "name": "no idle", "slots": [ { "id": 1, "fits": [ "t:idle" ] } ], "figures": { "idle_rpm": { "add": -900 } } }
        ] }
        """;

    private EngineReport EngineWith(string idlePart)
    {
        _temp.File("parts/engines/data/pack.json", IdlePack);
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        Assert.Empty(catalog.Problems);
        var tree = PartTreeBuilder.Build(catalog, ["engines/data/block", "engines/data/head", "engines/data/cam", "engines/data/carb", idlePart], "Block")!;
        Assert.Empty(tree.Unplaced);
        return EngineEvaluator.Evaluate(catalog, tree);
    }

    [Fact]
    public void An_idle_past_half_the_limiter_is_held_there()
    {
        var report = EngineWith("engines/data/fast_idle");

        Assert.Equal(6500 * PartFigures.MostIdleOfLimiter, report.IdleRpm);
    }

    [Fact]
    public void An_idle_taken_below_nothing_is_the_slowest_tick_over()
    {
        var report = EngineWith("engines/data/no_idle");

        Assert.Equal(PartFigures.MinIdleRpm, report.IdleRpm);
    }

    // ----- Turbos: no more than the car's data has sections for -----

    [Fact]
    public void A_part_with_more_turbos_than_the_car_takes_keeps_the_first()
    {
        var turbos = string.Join(", ", Enumerable.Repeat("""{ "boost": 0.3, "full_boost_rpm": 4000 }""", PartFigures.MaxTurbos + 3));
        var part = JsonConvert.DeserializeObject<PartDefinition>($$"""{ "id": "engines/data/many", "figures": { "turbo": [ {{turbos}} ] } }""")!;
        var problems = new List<string>();

        var set = PartFigures.Read(part, problems)!;

        Assert.Equal(PartFigures.MaxTurbos, set.Chargers.Count);
        Assert.Contains(problems, p => p.Contains($"more than {PartFigures.MaxTurbos} turbos", StringComparison.Ordinal));
    }

    // ----- A mod's removals whatever the case of the kind -----

    [Fact]
    public void A_kind_of_content_a_mod_removes_is_found_whatever_its_case()
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("Content/remover/mod.json", """{ "id": "remover", "removes": { "Racers": [ "king" ], "racers": [ "joe" ], "Streets": null } }""");

        var mods = ModSet.Load(_temp.Combine("Content"));
        var remover = mods.Mods.Single(m => m.Id == "remover");

        Assert.Equal(["king", "joe"], ModSet.RemovesOf(remover, "racers"));
        Assert.Empty(ModSet.RemovesOf(remover, "streets"));
        Assert.True(mods.IsRemoved("racers", "king"));
    }

    // ----- A stand-in with "drop": null -----

    [Fact]
    public void A_stand_in_that_drops_nothing_is_made()
    {
        var loader = new ScriptClassLoader(Path.Combine(RepoPaths.PartsFolder, PartScripts.Folder));
        loader.UseStandIns(_temp.File("stand_ins.json", """
            { "stand_ins": { "java.game.parts.enginepart.block.block_inline.Block_Inline_OHV": {
                "Copy": "java.game.parts.enginepart.block.block_vee.Block_Vee_OHV",
                "extends": "java.game.parts.enginepart.block.Block_Inline",
                "drop": null, } } }
            """));

        Assert.Empty(loader.Problems);
        Assert.NotNull(loader.Find("java.game.parts.enginepart.block.block_inline.Block_Inline_OHV"));
    }

    // ----- The rules' figures held to what the game can play by -----

    [Fact]
    public void A_divisor_of_nothing_a_loop_in_the_millions_and_a_cost_that_pays_are_the_base_games()
    {
        var merged = ModMerge.Parse("""
            {
              "condition": { "totaledKmh": 0, "newEngineLife": -5 },
              "engines": { "factory": { "maxBoltOns": 5000000 } },
              "repairs": { "rustCost": -900, "paintCost": 120 },
              "dealers": { "saleDiscount": 1.5 },
              "rivals": { "money": { "roundTo": 0 }, "king": { "carChoices": 0 } },
              "difficulty": { "hard": { "carPriceMultiplier": 0 } }
            }
            """)!;
        var problems = new List<string>();

        var book = RuleBook.Read(merged, problems);

        var fresh = new RuleBook();
        Assert.Equal(fresh.Condition.TotaledKmh, book.Condition.TotaledKmh);
        Assert.Equal(fresh.Condition.NewEngineLife, book.Condition.NewEngineLife);
        Assert.Equal(fresh.Engines.Factory.MaxBoltOns, book.Engines.Factory.MaxBoltOns);
        Assert.Equal(fresh.Repairs.RustCost, book.Repairs.RustCost);
        Assert.Equal(120m, book.Repairs.PaintCost);
        Assert.Equal(fresh.Dealers.SaleDiscount, book.Dealers.SaleDiscount);
        Assert.Equal(fresh.Rivals.Money.RoundTo, book.Rivals.Money.RoundTo);
        Assert.Equal(fresh.Rivals.King.CarChoices, book.Rivals.King.CarChoices);
        Assert.Equal(1.0, book.Difficulty.Hard.CarPriceMultiplier);
        Assert.Contains(problems, p => p.StartsWith("condition.totaledKmh (0)", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("engines.factory.maxBoltOns (5000000)", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("repairs.rustCost (-900)", StringComparison.Ordinal));
        Assert.Equal(8, problems.Count);
    }

    [Fact]
    public void The_base_games_rules_are_inside_their_own_bounds()
    {
        var problems = new List<string>();
        RuleBook.Read(ModMerge.ContentFiles(ModSet.BaseOnly(RepoPaths.BaseContent), ContentTypes.Rules, problems), problems);

        Assert.Empty(problems);
    }

    // ----- A prop's model and place -----

    [Fact]
    public void A_prop_whose_model_is_outside_its_folder_or_whose_place_is_no_number_is_left_out()
    {
        _temp.File("streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("streetcorsa/props/radio/prop.json", """{ "model": "..\\..\\..\\elsewhere.kn5", "places": { "garage": { "position": [ 1, 1, 1 ] } } }""");
        _temp.File("elsewhere.kn5", "x");
        _temp.File("streetcorsa/props/lamp/lamp.kn5", "x");
        _temp.File("streetcorsa/props/lamp/prop.json", """{ "places": { "garage": { "position": [ 1, 1e39, 1 ] } } }""");
        var showroom = Path.Combine(_temp.Path, "showrooms", "garage", "garage.kn5");
        var problems = new List<string>();

        Assert.Null(GarageProps.Find(ModSet.Load(_temp.Path), "radio", showroom, problems));
        Assert.Null(GarageProps.Find(ModSet.Load(_temp.Path), "lamp", showroom, problems));
        Assert.Equal(2, problems.Count);
        Assert.Contains("outside its folder", problems[0]);
    }

    // ----- The shop's file read as leniently as the other scenes' -----

    [Fact]
    public void A_shop_file_with_a_note_and_a_trailing_comma_loads()
    {
        _temp.File("shop/shop.kn5", "x");
        _temp.File("shop/shop.json", """
            {
              // the shop the tests stand in
              "id": "testshop",
              "title": "Test shop",
            }
            """);

        var scene = ShopScenes.Load(_temp.Combine("shop"));

        Assert.NotNull(scene);
        Assert.Equal("testshop", scene!.Id);
    }
}
