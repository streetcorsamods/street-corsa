using System.Globalization;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>The 2026-10-18 audit's fixes to the parts: the engine model's edges, the catalog's files, the saves' trees, the AC data's bounds</summary>
public sealed class Audit1018ATests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // A four of specs that runs: block, crank, rods, pistons, head, cam, intake with its injection, gearbox
    private const string Engine = """
        [
          { "id": "engines/a/block", "class": "engine_block", "mass": 100,
            "specs": { "cylinders": 4, "bore": 100, "deck_height": 239.64, "idle_rpm": 800, "rev_limit": 6500,
                       "spark_advance": 35, "spark_advance_high": 20, "spark_rpm_low": 3000, "spark_rpm_high": 6000 },
            "slot_roles": { "crankshaft": 8, "head": 45, "intake_manifold": 14, "camshaft": 10, "transmission": 20 },
            "slots": [ { "id": 8, "takes": [ "a:crank" ] }, { "id": 45, "takes": [ "a:head" ] }, { "id": 14, "takes": [ "a:intake" ] },
                       { "id": 10, "takes": [ "a:cam" ] }, { "id": 20, "takes": [ "a:box" ] } ] },
          { "id": "engines/a/crank", "class": "crankshaft", "specs": { "max_rpm": 7000, "inertia": 0.007, "stroke": 86.2 },
            "slot_roles": { "connecting_rods": 40 }, "slots": [ { "id": 1, "fits": [ "a:crank" ] }, { "id": 40, "takes": [ "a:rods" ] } ] },
          { "id": "engines/a/rods", "class": "connecting_rods", "specs": { "inertia": 0.005, "rod_length": 165.54 },
            "slot_roles": { "pistons": 42 }, "slots": [ { "id": 1, "fits": [ "a:rods" ] }, { "id": 42, "takes": [ "a:pistons" ] } ] },
          { "id": "engines/a/pistons", "class": "pistons", "specs": { "inertia": 0.004, "dome": 0, "pin_height": 30 },
            "slots": [ { "id": 1, "fits": [ "a:pistons" ] } ] },
          { "id": "engines/a/head", "class": "cylinder_head",
            "specs": { "inertia": 0.003, "chamber": 60, "heat_loss": 75, "intake_valve": 50, "intake_valves": 1, "exhaust_valve": 40, "exhaust_valves": 1 },
            "slots": [ { "id": 1, "fits": [ "a:head" ] } ] },
          { "id": "engines/a/cam", "class": "camshaft",
            "specs": { "inertia": 0.01, "friction_loss": 0.1, "lobe_separation": 210, "intake_lift": 12, "exhaust_lift": 12,
                       "intake_duration": 286, "exhaust_duration": 300, "advance_min": -15, "advance_max": 15, "advance_step": 1 },
            "slots": [ { "id": 1, "fits": [ "a:cam" ] } ] },
          { "id": "engines/a/intake", "class": "intake_manifold", "specs": { "flow": 0.85 },
            "slots": [ { "id": 1, "fits": [ "a:intake" ] }, { "id": 7, "takes": [ "a:rail" ] } ] },
          { "id": "engines/a/rail", "class": "fuel_system", "specs": { "mixture_ratio": 12.5, "fuel_flow": 40, "air_flow": 500 },
            "slots": [ { "id": 1, "fits": [ "a:rail" ] } ] },
          { "id": "engines/a/box", "class": "transmission", "specs": { "gears": [ 2.5, 1.5, 1.0 ], "reverse": 2.5, "final_drive": 3.5 },
            "slots": [ { "id": 1, "fits": [ "a:box" ] } ] }
        ]
        """;

    private static readonly string[] Parts =
    [
        "engines/a/block", "engines/a/crank", "engines/a/rods", "engines/a/pistons", "engines/a/head", "engines/a/cam",
        "engines/a/intake", "engines/a/rail", "engines/a/box"
    ];

    private PartsCatalog EngineCatalog()
    {
        _temp.Parts("parts/engines/a", Engine);
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        Assert.Empty(catalog.Problems);
        return catalog;
    }

    private static EngineReport Evaluate(PartsCatalog catalog)
    {
        var tree = PartTreeBuilder.Build(catalog, Parts, PartClasses.EngineBlock)!;
        Assert.Empty(tree.Unplaced);
        return EngineEvaluator.Evaluate(catalog, tree);
    }

    private static PartDefinition Spec(PartsCatalog catalog, string id, string name, object value)
    {
        var part = catalog.Get(id)!;
        part.Specs[name] = value;
        return part;
    }

    // ---- the engine model's edges ----

    [Fact]
    public void The_engine_runs_as_written()
    {
        var report = Evaluate(EngineCatalog());
        Assert.True(report.Runs, report.Problem);
        Assert.Equal(800, report.IdleRpm);
        Assert.Equal([2.5, 1.5, 1.0], report.GearRatios);
    }

    [Theory]
    [InlineData(9000.0, 6500 * PartFigures.MostIdleOfLimiter)]
    [InlineData(-900.0, PartFigures.MinIdleRpm)]
    public void A_blocks_idle_given_as_a_spec_is_held_under_its_limiter_as_a_figure_is(double idle, double expected)
    {
        var catalog = EngineCatalog();
        Spec(catalog, "engines/a/block", EngineAssembly.Specs.IdleRpm, idle);

        var report = Evaluate(catalog);

        Assert.True(report.Runs, report.Problem);
        Assert.Equal(expected, report.IdleRpm);
    }

    [Theory]
    [InlineData("engines/a/block", "bore", 0.0)]
    [InlineData("engines/a/block", "bore", -100.0)]
    [InlineData("engines/a/crank", "stroke", 0.0)]
    [InlineData("engines/a/crank", "stroke", -86.2)]
    [InlineData("engines/a/block", "cylinders", 0.0)]
    [InlineData("engines/a/block", "cylinders", -8.0)]
    [InlineData("engines/a/pistons", "dome", 100000.0)]
    public void A_spec_no_engine_could_have_is_contained_and_told(string part, string spec, double value)
    {
        var catalog = EngineCatalog();
        Spec(catalog, part, spec, value);

        var report = Evaluate(catalog);

        Assert.False(report.Runs);
        Assert.NotNull(report.Problem);
        Assert.Empty(report.Faults);

        // The game's own reason, said in full (no placeholder left), never a part's needs: every part is there
        Assert.DoesNotContain("{", report.Problem);
        Assert.DoesNotContain("missing", report.Problem);
        Assert.Equal(0, report.Dyno!.MaxPowerHp);
    }

    [Fact]
    public void A_gearbox_with_no_gears_hands_the_car_none_and_no_drive()
    {
        var catalog = EngineCatalog();
        Spec(catalog, "engines/a/box", EngineAssembly.Specs.Gears, new List<object>());

        var report = Evaluate(catalog);

        Assert.True(report.Runs, report.Problem);
        Assert.Empty(report.GearRatios);
        Assert.Equal(0, report.DriveType);
    }

    [Fact]
    public async Task A_cam_whose_step_the_advance_cannot_take_in_is_worked_out_without_hanging()
    {
        var catalog = EngineCatalog();
        var cam = catalog.Get("engines/a/cam")!;
        cam.Specs["advance_min"] = 1e6;
        cam.Specs["advance_max"] = 1e6 + 1e-9;
        cam.Specs["advance_step"] = 2e-13;
        cam.Specs["advance"] = 1e6 + 5e-10;

        var report = await Task.Run(() => Evaluate(catalog), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.NotNull(report.Inputs);
        Assert.Empty(report.Faults);
    }

    [Fact]
    public void A_bank_without_a_limit_leaves_the_other_banks_standing()
    {
        var one = new DynoSheet { MaxRpm = DynoSheet.NoMaxRpm, MaxFuelFlow = 0, MaxAirFlow = 0.4 };
        var other = new DynoSheet { MaxRpm = 6500, MaxFuelFlow = 0.04, MaxAirFlow = 0.3 };

        var both = new DynoSheet();
        both.BeAverageOf(one, other);

        Assert.Equal(6500, both.MaxRpm);
        Assert.Equal(0.04, both.MaxFuelFlow);
        Assert.Equal(0.3, both.MaxAirFlow);

        // Two limits: the tighter; none at all: none
        var tighter = new DynoSheet();
        tighter.BeAverageOf(other, new DynoSheet { MaxRpm = 5500, MaxFuelFlow = 0.05, MaxAirFlow = 0.35 });
        Assert.Equal(5500, tighter.MaxRpm);
        Assert.Equal(0.04, tighter.MaxFuelFlow);
        Assert.Equal(0.3, tighter.MaxAirFlow);

        var none = new DynoSheet();
        none.BeAverageOf(new DynoSheet(), new DynoSheet());
        Assert.Equal(DynoSheet.NoMaxRpm, none.MaxRpm);
        Assert.Equal(0, none.MaxFuelFlow);
    }

    [Fact]
    public void The_dynos_reasons_are_keys_with_figures_the_same_in_every_locale()
    {
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("7.5", EngineDyno.Figure(7.5, "0.0"));
            Assert.Equal("30000", EngineDyno.Figure(EngineDyno.MaxRpm, "0"));

            // Said through PartWords (the game's words in the tests, the keys in the tools), the figure the same either way
            Assert.Equal(PartWords.Say("engine.problem.no_displacement"), EngineDyno.Unrunnable(new DynoInputs()));
            Assert.Equal(PartWords.Say("engine.problem.no_displacement"), EngineDyno.Unrunnable(new DynoInputs { Cylinders = 4, Bore = -0.1, Stroke = 0.08, ClearanceVolume = 0.00006 }));
            var compression = EngineDyno.Unrunnable(new DynoInputs { Cylinders = 4, Bore = 0.1, Stroke = 0.08, ClearanceVolume = 1 });
            Assert.Equal(PartWords.Say("engine.problem.no_compression", ("compression", "1.0")), compression);
            Assert.DoesNotContain("1,0", compression);
            Assert.Equal(PartWords.Say(EngineDyno.NotFedKey), PartFigures.MissingOf(new DynoInputs { Cylinders = 4, Bore = 0.1, Stroke = 0.08, ClearanceVolume = 0.00006 }));
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public void The_dynos_horsepower_per_kilowatt_is_the_one_the_car_specs_read_by()
    {
        Assert.Equal(1.34102, EngineDyno.HpPerKw, 5);
        Assert.Equal(150 * EngineDyno.HpPerKw, AcSpecs.ParsePower("150 kW")!.Value, 6);
    }

    // ---- the catalog's files ----

    [Fact]
    public void A_job_that_would_make_a_part_nothing_is_told_and_the_catalog_loads()
    {
        _temp.Parts("parts/axles/a", """
            [
              { "id": "axles/a/open", "class": "part", "slots": [ { "id": 1 } ], "becomes": { "weld_spiders": null, "": "axles/a/welded" } },
              { "id": "axles/a/welded", "class": "part", "slots": [ { "id": 1 } ] }
            ]
            """);
        _temp.File("parts/" + PartRebuilds.FileName, """
            { "jobs": [ { "id": "weld_spiders", "name": "Weld", "kinds": [ "differential" ], "becomes": true, "restores": 0, "cost": 0.04, "minutes": 120 } ] }
            """);

        var catalog = PartsCatalog.Load(_temp.Combine("parts"));

        Assert.Contains(catalog.Problems, p => p.Contains("axles/a/open", StringComparison.Ordinal) && p.Contains("weld_spiders", StringComparison.Ordinal) && p.Contains("names no part", StringComparison.Ordinal));
        Assert.Empty(catalog.Get("axles/a/open")!.Becomes ?? new Dictionary<string, string>());
        Assert.Null(catalog.Get(null));
    }

    [Fact]
    public void A_cars_build_renamed_twice_is_found_by_the_id_a_save_holds()
    {
        _temp.Parts("parts/engines/a", Engine);
        _temp.File("parts/" + EngineBuild.FileName, $$"""[ { "id": "factory/new-four", "name": "New four", "parts": {{System.Text.Json.JsonSerializer.Serialize(Parts)}} } ]""");
        _temp.File("parts/" + EngineBuild.AliasesFileName, """{ "factory/old-four": "factory/older-four", "factory/older-four": "factory/new-four", "factory/lost": "" }""");

        var catalog = PartsCatalog.Load(_temp.Combine("parts"));

        Assert.Equal("factory/new-four", catalog.CurrentBuildId("factory/old-four"));
        Assert.Equal("factory/new-four", catalog.CurrentBuildId("factory/older-four"));
        Assert.Equal("factory/never", catalog.CurrentBuildId("factory/never"));
        Assert.Contains(catalog.Problems, p => p.Contains("factory/lost", StringComparison.Ordinal) && p.Contains("leads nowhere", StringComparison.Ordinal));

        var index = EngineBuildIndex.Create(catalog);
        Assert.Equal("factory/new-four", index.Get("factory/old-four")!.Build.Id);
        Assert.Null(index.Get("factory/never"));
    }

    // ---- the saves' trees ----

    private static PartInstance Chain(int depth)
    {
        var root = new PartInstance("engines/a/head") { ParentSlot = RunningGear.WheelSlot(0), OwnSlot = 1 };
        var at = root;
        for (var i = 0; i < depth; i++)
        {
            var next = new PartInstance("engines/a/head") { ParentSlot = 1, OwnSlot = 1 };
            at.Children.Add(next);
            at = next;
        }

        return root;
    }

    [Fact]
    public void A_save_of_parts_nested_without_end_is_brought_up_to_date_and_asked_the_work_order_without_overflowing()
    {
        var catalog = PartsCatalog.Empty(_temp.Path);
        var loose = new List<PartInstance>();
        var root = Chain(200_000);

        Assert.False(SavedParts.BringUpToDate(catalog, root, loose));
        Assert.Empty(loose);

        var carParts = new List<PartInstance> { root };
        Assert.Null(CarWork.WhyNotOff(catalog, carParts, root));
        Assert.Null(CarWork.WhyNotOff(catalog, carParts, root.Children[0]));
    }

    [Fact]
    public void A_save_with_two_parts_on_the_axle_slot_is_brought_up_to_date_without_throwing()
    {
        var catalog = TestCars.Catalog.Value;
        var chevy = TestCars.Chevy66;
        var axle = RunningGearGuess.ChooseAxle(catalog, TestCars.LiveAxleSpecs(), chevy, null)!;
        var parts = RunningGearFactory.Create(catalog, TestCars.LiveAxleSpecs(), 1, axle, chevy);
        var housing = parts.Single(p => p.ParentSlot == RearAxle.CarSlot);
        Assert.NotEmpty(RearAxle.CornerParts(parts));

        // An edited save: a second housing on the car's axle slot, carrying nothing
        parts.Add(new PartInstance(housing.DefinitionId) { ParentSlot = RearAxle.CarSlot, OwnSlot = housing.OwnSlot });

        // The car's rear is no live axle after all: the corners come off the housing that carries them, back onto the car
        var moved = RunningGearFactory.HangOnAxle(catalog, parts, liveAxle: false);

        Assert.True(moved > 0);
        Assert.Empty(RearAxle.CornerParts(parts));
    }

    // ---- the AC data's bounds ----

    private const string Suspensions = "[FRONT]\nTYPE=DWB\nTRACK=1.500\nHUB_MASS=40\n[REAR]\nTYPE=AXLE\nTRACK=1.500\nHUB_MASS=85\n";
    private const string CarIni = "[BASIC]\nTOTALMASS=1500\n";

    private PartsCatalog GearCatalog()
    {
        _temp.Parts("parts/gear/a", """
            [
                { "id": "gear/a/rim", "mass": 8, "specs": { "offset": 0.0, "wheel_radius": 0.1905 } },
                { "id": "gear/a/out_rim", "mass": 8, "specs": { "offset": -0.02, "wheel_radius": 0.1905 } },
                { "id": "gear/a/far_rim", "mass": 8, "specs": { "offset": 1e300, "wheel_radius": 0.1905 } },
                { "id": "gear/a/heavy_rim", "mass": 1e38, "specs": { "offset": 0.0, "wheel_radius": 0.1905 } },
                { "id": "gear/a/tyre", "mass": 10, "specs": { "friction": 1.0, "radius": 0.32, "tyre_width": 205, "wheel_radius": 190.5 } },
                { "id": "gear/a/brake", "mass": 5, "specs": { "force": 1000, "radius": 0.1 } },
                { "id": "gear/a/spring", "mass": 2, "specs": { "force": 30000 } },
                { "id": "gear/a/shock", "mass": 2 }
              ]
            """);
        return PartsCatalog.Load(_temp.Combine("parts"));
    }

    private static (RunningGearFactory.AxleParts, RunningGearFactory.AxleParts) Factory(PartsCatalog catalog)
    {
        var axle = new RunningGearFactory.AxleParts(catalog.Get("gear/a/tyre")!, catalog.Get("gear/a/rim")!,
            catalog.Get("gear/a/brake")!, catalog.Get("gear/a/spring")!, catalog.Get("gear/a/shock")!);
        return (axle, axle);
    }

    private static CornerParts[] Corners(string rim) =>
        Enumerable.Range(0, RunningGear.Corners).Select(_ => new CornerParts
        {
            Rim = new PartInstance(rim),
            Tyre = new PartInstance("gear/a/tyre"),
            Brake = new PartInstance("gear/a/brake"),
            Spring = new PartInstance("gear/a/spring"),
            Shock = new PartInstance("gear/a/shock")
        }).ToArray();

    private static string? Data(string name) => name switch
    {
        "suspensions.ini" => Suspensions,
        "car.ini" => CarIni,
        _ => null
    };

    [Fact]
    public void A_rims_offset_or_weight_past_any_rim_changes_nothing_while_a_real_one_moves_the_track()
    {
        var catalog = GearCatalog();
        var problems = new List<string>();

        Assert.Empty(AcRunningGearData.Generate(catalog, Corners("gear/a/far_rim"), Factory(catalog), Data, problems));
        Assert.Empty(AcRunningGearData.Generate(catalog, Corners("gear/a/heavy_rim"), Factory(catalog), Data, problems));
        Assert.Empty(problems);

        var files = AcRunningGearData.Generate(catalog, Corners("gear/a/out_rim"), Factory(catalog), Data, problems);
        var suspensions = new IniText(files["suspensions.ini"]);
        Assert.Equal(1.54, suspensions.GetNumber("FRONT", "TRACK")!.Value, 3);
        Assert.Equal(1.54, suspensions.GetNumber("REAR", "TRACK")!.Value, 3);
    }

    [Fact]
    public void An_axle_ratio_or_a_mass_past_any_part_is_no_figure()
    {
        Assert.Equal(0, AcCarIni.Bounded(1e300, AcCarIni.MaxRatio));
        Assert.Equal(0, AcCarIni.Bounded(double.NaN, AcCarIni.MaxRatio));
        Assert.Equal(4.11, AcCarIni.Bounded(4.11, AcCarIni.MaxRatio));
        Assert.Equal(60, AcCarIni.MassOf(new PartDefinition { Id = "x", Mass = 1e38f }, 60));

        var car = new IniText(CarIni);
        AcCarIni.AddMass(car, 1e38);
        AcCarIni.AddMass(car, double.PositiveInfinity);
        Assert.False(car.Changed);
        AcCarIni.AddMass(car, 50);
        Assert.Equal(1550, car.GetNumber("BASIC", "TOTALMASS"));
    }
}
