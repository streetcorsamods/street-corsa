using System.Reflection;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.PartShops;

namespace StreetCorsa.Tests;

/// <summary>The review fixes of the own part logic: the engine model's edges, the save's old ids, the parts files</summary>
public sealed class OwnPartLogicReviewTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // A four of specs: an overhead-valve block (its cam on the block) and an overhead-cam one (its cam on the head), and
    // a block of figures alone that needs its camshaft
    private const string Pack = """
        { "id": "engines/t", "parts": [
          { "id": "engines/t/ohv_block", "class": "engine_block", "mass": 100,
            "specs": { "cylinders": 4, "bore": 100, "deck_height": 239.64, "idle_rpm": 800, "rev_limit": 6000,
                       "spark_advance": 35, "spark_advance_high": 20, "spark_rpm_low": 3000, "spark_rpm_high": 6000 },
            "slot_roles": { "crankshaft": 8, "head": 45, "intake_manifold": 14, "camshaft": 10 },
            "slots": [ { "id": 8, "takes": [ "t:crank" ] }, { "id": 45, "takes": [ "t:ohv_head" ] }, { "id": 14, "takes": [ "t:intake" ] },
                       { "id": 10, "takes": [ "t:cam" ] } ] },
          { "id": "engines/t/ohc_block", "class": "engine_block", "mass": 100,
            "specs": { "cylinders": 4, "bore": 100, "deck_height": 239.64, "idle_rpm": 800, "rev_limit": 6000,
                       "spark_advance": 35, "spark_advance_high": 20, "spark_rpm_low": 3000, "spark_rpm_high": 6000 },
            "slot_roles": { "crankshaft": 8, "head": 45, "intake_manifold": 14 },
            "slots": [ { "id": 8, "takes": [ "t:crank" ] }, { "id": 45, "takes": [ "t:ohc_head" ] }, { "id": 14, "takes": [ "t:intake" ] } ] },
          { "id": "engines/t/crank", "class": "crankshaft", "specs": { "max_rpm": 5500, "inertia": 0.007, "stroke": 86.2 },
            "slot_roles": { "connecting_rods": 40 }, "slots": [ { "id": 1, "fits": [ "t:crank" ] }, { "id": 40, "takes": [ "t:rods" ] } ] },
          { "id": "engines/t/free_crank", "class": "crankshaft", "specs": { "inertia": 0.007, "stroke": 86.2 },
            "slot_roles": { "connecting_rods": 40 }, "slots": [ { "id": 1, "fits": [ "t:crank" ] }, { "id": 40, "takes": [ "t:rods" ] } ] },
          { "id": "engines/t/rods", "class": "connecting_rods", "specs": { "inertia": 0.005, "rod_length": 165.54 },
            "slot_roles": { "pistons": 42 }, "slots": [ { "id": 1, "fits": [ "t:rods" ] }, { "id": 42, "takes": [ "t:pistons" ] } ] },
          { "id": "engines/t/pistons", "class": "pistons", "specs": { "inertia": 0.004, "dome": 0, "pin_height": 30 },
            "slots": [ { "id": 1, "fits": [ "t:pistons" ] } ] },
          { "id": "engines/t/ohv_head", "class": "cylinder_head",
            "specs": { "inertia": 0.003, "chamber": 60, "heat_loss": 75, "intake_valve": 50, "intake_valves": 1, "exhaust_valve": 40, "exhaust_valves": 1 },
            "slots": [ { "id": 1, "fits": [ "t:ohv_head" ] } ] },
          { "id": "engines/t/ohc_head", "class": "cylinder_head",
            "specs": { "inertia": 0.003, "chamber": 60, "heat_loss": 75, "intake_valve": 50, "intake_valves": 1, "exhaust_valve": 40, "exhaust_valves": 1 },
            "slot_roles": { "camshaft": 3 }, "slots": [ { "id": 1, "fits": [ "t:ohc_head" ] }, { "id": 3, "takes": [ "t:cam" ] } ] },
          { "id": "engines/t/cam", "class": "camshaft",
            "specs": { "inertia": 0.01, "friction_loss": 0.1, "lobe_separation": 210, "intake_lift": 12, "exhaust_lift": 12,
                       "intake_duration": 286, "exhaust_duration": 300, "advance_min": -15, "advance_max": 15, "advance_step": 1 },
            "slots": [ { "id": 1, "fits": [ "t:cam" ] } ] },
          { "id": "engines/t/light_cam", "class": "camshaft",
            "specs": { "lobe_separation": 210, "intake_lift": 12, "exhaust_lift": 12,
                       "intake_duration": 286, "exhaust_duration": 300, "advance_min": -15, "advance_max": 15, "advance_step": 1 },
            "slots": [ { "id": 1, "fits": [ "t:cam" ] } ] },
          { "id": "engines/t/limited_cam", "class": "camshaft",
            "specs": { "max_rpm": 7000, "lobe_separation": 210, "intake_lift": 12, "exhaust_lift": 12,
                       "intake_duration": 286, "exhaust_duration": 300, "advance_min": -15, "advance_max": 15, "advance_step": 1 },
            "slots": [ { "id": 1, "fits": [ "t:cam" ] } ] },
          { "id": "engines/t/intake", "class": "intake_manifold", "specs": { "flow": 0.85 },
            "slots": [ { "id": 1, "fits": [ "t:intake" ] }, { "id": 7, "takes": [ "t:rail" ] } ] },
          { "id": "engines/t/rail", "class": "fuel_system", "specs": { "mixture_ratio": 12.5, "fuel_flow": 40, "air_flow": 500 },
            "slots": [ { "id": 1, "fits": [ "t:rail" ] } ] },

          { "id": "engines/t/figures_block", "class": "engine_block", "mass": 120,
            "slots": [ { "id": 10, "takes": [ "t:fhead" ] }, { "id": 11, "takes": [ "t:fcam" ] }, { "id": 12, "takes": [ "t:fcarb" ] } ],
            "needs": [ { "slot": 11, "message": "the engine needs its camshaft." } ],
            "figures": { "cylinders": 4, "bore": 86, "stroke": 86, "compression": 9.5, "rev_limit": 6500 } },
          { "id": "engines/t/figures_head", "class": "cylinder_head", "slots": [ { "id": 1, "fits": [ "t:fhead" ] } ],
            "figures": { "intake_flow": 0.8, "exhaust_flow": 0.35 } },
          { "id": "engines/t/figures_cam", "class": "camshaft", "slots": [ { "id": 1, "fits": [ "t:fcam" ] } ],
            "figures": { "intake_duration": 240, "exhaust_duration": 240 } },
          { "id": "engines/t/figures_carb", "class": "fuel_system", "slots": [ { "id": 1, "fits": [ "t:fcarb" ] } ],
            "figures": { "fuel_flow": 30, "air_flow": 400 } }
        ] }
        """;

    private static readonly string[] Bottom = { "engines/t/rods", "engines/t/pistons", "engines/t/intake", "engines/t/rail" };

    private PartsCatalog Catalog()
    {
        _temp.File("parts/engines/t/pack.json", Pack);
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        Assert.Empty(catalog.Problems);
        return catalog;
    }

    private static EngineReport Evaluate(PartsCatalog catalog, params string[] parts)
    {
        var tree = PartTreeBuilder.Build(catalog, parts, PartClasses.EngineBlock)!;
        Assert.Empty(tree.Unplaced);
        return EngineEvaluator.Evaluate(catalog, tree);
    }

    private static string[] Ohv(string crank, string cam) => [ "engines/t/ohv_block", crank, "engines/t/ohv_head", cam, .. Bottom ];

    private static string[] Ohc(string cam) => [ "engines/t/ohc_block", "engines/t/crank", "engines/t/ohc_head", cam, .. Bottom ];

    [Fact]
    public void A_block_camshaft_without_a_top_speed_leaves_the_cranks_and_its_own_is_kept()
    {
        var catalog = Catalog();

        var crankLimited = Evaluate(catalog, Ohv("engines/t/crank", "engines/t/light_cam"));
        Assert.True(crankLimited.Runs, crankLimited.Problem);
        Assert.Equal(5500, crankLimited.Inputs!.MaxRpm);

        var camLimited = Evaluate(catalog, Ohv("engines/t/free_crank", "engines/t/limited_cam"));
        Assert.True(camLimited.Runs, camLimited.Problem);
        Assert.Equal(7000, camLimited.Inputs!.MaxRpm);
    }

    [Fact]
    public void An_overhead_cam_turns_with_the_engine_as_a_block_cam_does()
    {
        var catalog = Catalog();

        var light = Evaluate(catalog, Ohc("engines/t/light_cam"));
        var heavy = Evaluate(catalog, Ohc("engines/t/cam"));
        var onTheBlock = Evaluate(catalog, Ohv("engines/t/crank", "engines/t/cam"));
        var lightOnTheBlock = Evaluate(catalog, Ohv("engines/t/crank", "engines/t/light_cam"));

        Assert.True(heavy.Runs, heavy.Problem);
        Assert.True(heavy.Inertia > light.Inertia);
        Assert.Equal(onTheBlock.Inertia - lightOnTheBlock.Inertia, heavy.Inertia - light.Inertia, 9);
    }

    [Fact]
    public async Task A_camshaft_of_absurd_figures_is_worked_out_without_hanging()
    {
        var catalog = Catalog();
        var cam = catalog.Get("engines/t/cam")!;
        cam.Specs["lobe_separation"] = 1e20;
        cam.Specs["intake_duration"] = 1e20;
        cam.Specs["advance"] = 1.0;
        cam.Specs["advance_step"] = 1e-9;

        var report = await Task.Run(() => Evaluate(catalog, Ohv("engines/t/crank", "engines/t/cam")), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        foreach (var events in new[] { report.Inputs!.IntakeOpen, report.Inputs.IntakeClose })
            Assert.True(double.IsNaN(events) || events is >= 0 and <= 1, $"{events}");
    }

    [Fact]
    public void A_block_of_figures_alone_still_asks_for_what_it_needs()
    {
        var catalog = Catalog();

        var without = Evaluate(catalog, "engines/t/figures_block", "engines/t/figures_head", "engines/t/figures_carb");
        var with = Evaluate(catalog, "engines/t/figures_block", "engines/t/figures_head", "engines/t/figures_cam", "engines/t/figures_carb");

        Assert.Equal("the engine needs its camshaft.", without.Problem);
        Assert.True(with.Runs, with.Problem);
    }

    [Fact]
    public void Clogging_is_held_from_none_to_all_of_the_flow()
    {
        PartDefinition Cleaner(object clogging) => new() { Id = "x", Specs = { ["clogging"] = clogging } };

        Assert.Equal(1, EngineAssembly.Clogging(Cleaner(1.9)));
        Assert.Equal(0, EngineAssembly.Clogging(Cleaner(-0.5)));
        Assert.Equal(0.25, EngineAssembly.Clogging(Cleaner(0.25)));
        Assert.Equal(0, EngineAssembly.Clogging(new PartDefinition { Id = "y" }));
    }

    [Fact]
    public void The_dyno_sheet_averages_and_hands_on_every_figure()
    {
        var doubles = typeof(DynoSheet).GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => f.FieldType == typeof(double)).ToList();
        var one = new DynoSheet();
        var other = new DynoSheet();
        for (var i = 0; i < doubles.Count; i++)
        {
            doubles[i].SetValue(one, 1000.0 + 2 * i);
            doubles[i].SetValue(other, 1002.0 + 2 * i);
        }

        var both = new DynoSheet();
        both.BeAverageOf(one, other);

        // The fuel's limits are checked against, never averaged: they are the same on every bank
        foreach (var field in doubles.Where(f => f.Name is not (nameof(DynoSheet.MinCompression) or nameof(DynoSheet.MaxCompression))))
            Assert.True((double)field.GetValue(both)! == 1001.0 + 2 * doubles.IndexOf(field), $"{field.Name} is not averaged");

        one.Cylinders = 6;
        var inputs = one.ToInputs();
        var given = doubles.Select(f => (double)f.GetValue(one)!).ToHashSet();
        foreach (var property in typeof(DynoInputs).GetProperties().Where(p => p.PropertyType == typeof(double)))
            Assert.True(given.Contains((double)property.GetValue(inputs)!), $"{property.Name} is not handed on");
        Assert.Equal(6, inputs.Cylinders);
    }

    [Fact]
    public void A_later_fuels_file_changes_only_the_figures_it_gives()
    {
        _temp.File("base/fuels.json", """{ "fuels": { "methanol": { "heat": 1.4, "best_ratio": 16.0, "burn_time": 2.25, "burn_time_off": 0.4 } } }""");
        _temp.File("mod/fuels.json", """{ "fuels": { "methanol": { "heat": 1.5 }, "half": { "heat": 1.0 } } }""");

        var fuels = Fuels.Load([_temp.Combine("base"), _temp.Combine("mod")]);

        var methanol = fuels.All["methanol"];
        Assert.Equal(1.5, methanol.Heat);
        Assert.Equal(16.0, methanol.BestRatio);
        Assert.Equal(2.25, methanol.BurnTime);
        Assert.False(fuels.All.ContainsKey("half"));
        Assert.Contains(fuels.Problems, p => p.Contains("fuel half"));
    }

    [Fact]
    public void A_part_shops_shelf_of_a_renamed_part_goes_by_its_new_id()
    {
        _temp.File("parts/engines/t/pack.json", Pack);
        _temp.File("parts/" + PartPack.AliasesFileName, """{ "engines/old/Rail": "engines/t/rail" }""");
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        var shop = new PartShopLocation { Shelves = [new ShopShelf { DefinitionId = "engines/old/Rail", Count = 2 }, new ShopShelf { DefinitionId = "engines/t/cam", Count = 1 }] };

        Assert.Equal(1, PartShopService.RenameShelves(catalog, [shop]));

        Assert.Equal(["engines/t/rail", "engines/t/cam"], shop.Shelves.Select(s => s.DefinitionId));
        Assert.Equal(0, PartShopService.RenameShelves(catalog, [shop]));
    }

    [Fact]
    public void A_pack_of_the_older_format_and_a_class_of_no_file_are_told()
    {
        _temp.File("parts/engines/old/pack.json", """
            { "id": "engines/old", "parts": [
              { "id": "engines/old/head", "class_chain": [ "cylinder_head" ], "properties": { "chamber": 60 }, "slots": [ { "id": 1 } ] },
              { "id": "engines/old/typo", "class": "cylinderhead", "slots": [ { "id": 1 } ] }
            ] }
            """);
        _temp.File("parts/" + EngineBuild.FileName, """[ { "id": "old/build", "name": "old", "origin": "car", "parts": [ "engines/old/head" ] } ]""");

        var catalog = PartsCatalog.Load(_temp.Combine("parts"));

        Assert.Contains(catalog.Problems, p => p.Contains("engines/old/head") && p.Contains("class_chain, properties"));
        Assert.Contains(catalog.Problems, p => p.Contains("cylinderhead"));
        Assert.Equal(EngineBuild.OriginCar, Assert.Single(catalog.EngineBuilds).Origin);
    }

    [Fact]
    public void Slot_shifts_of_a_renamed_part_move_its_new_self()
    {
        _temp.File("parts/engines/t/pack.json", Pack.Replace("""{ "id": 7, "takes": [ "t:rail" ] }""", """{ "id": 7, "position": [0, 0, 0], "takes": [ "t:rail" ] }"""));
        _temp.File("parts/" + PartPack.AliasesFileName, """{ "engines/old/Intake": "engines/t/intake" }""");
        _temp.File("parts/" + SlotShifts.FileName, """{ "engines/old/Intake": { "7": [0.0, 0.01, 0.0] }, "engines/gone/part": { "1": [0.1, 0, 0] } }""");

        var catalog = PartsCatalog.Load(_temp.Combine("parts"));

        Assert.Equal(0.01f, catalog.Get("engines/t/intake")!.Slots.Single(s => s.Id == 7).Position[1], 5);
        Assert.Equal(0.01f, catalog.Shifts.Of("engines/t/intake", 7)[1], 5);
        Assert.Contains(catalog.Problems, p => p.Contains("engines/gone/part"));
    }
}
