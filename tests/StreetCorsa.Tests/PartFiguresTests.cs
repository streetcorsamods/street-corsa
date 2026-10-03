using System.Globalization;
using Newtonsoft.Json;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// A part's "figures": engine figures in plain metric units, for a part without a script of the source game (an
/// engine made of them alone) or over what its script works out
/// </summary>
public sealed class PartFiguresTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // Everything goes straight on the block by a fitting of its own
    private const string Pack = """
        { "id": "engines/data", "parts": [
          { "id": "engines/data/block", "name": "two litre four block", "class_chain": [ "Block" ], "mass": 120,
            "slots": [ { "id": 10, "takes": [ "t:head" ] }, { "id": 11, "takes": [ "t:cam" ] }, { "id": 12, "takes": [ "t:carb" ] },
                       { "id": 13, "takes": [ "t:header" ] }, { "id": 14, "takes": [ "t:gear" ] }, { "id": 15, "takes": [ "t:turbo" ] },
                       { "id": 16, "takes": [ "t:turbo" ] }, { "id": 17, "takes": [ "t:blower" ] } ],
            "figures": { "cylinders": 4, "bore": 86, "stroke": 86, "compression": 9.5, "rev_limit": 6500, "max_rpm": 7000,
                         "idle_rpm": 850, "inertia": 0.12 } },
          { "id": "engines/data/head", "name": "head", "class_chain": [ "CylinderHead" ], "slots": [ { "id": 1, "fits": [ "t:head" ] } ],
            "figures": { "intake_flow": 0.8, "exhaust_flow": 0.35 } },
          { "id": "engines/data/cam", "name": "cam", "class_chain": [ "Camshaft" ], "slots": [ { "id": 1, "fits": [ "t:cam" ] } ],
            "figures": { "intake_duration": 240, "exhaust_duration": 240, "spark_advance": 32, "spark_advance_high": 22 } },
          { "id": "engines/data/carb", "name": "carb", "slots": [ { "id": 1, "fits": [ "t:carb" ] } ],
            "figures": { "fuel_flow": 30, "air_flow": 400 } },
          { "id": "engines/data/header", "name": "header", "slots": [ { "id": 1, "fits": [ "t:header" ] } ],
            "figures": { "exhaust_flow": { "times": 1.15 } } },
          { "id": "engines/data/gearbox", "name": "five speed", "class_chain": [ "Transmission" ], "slots": [ { "id": 1, "fits": [ "t:gear" ] } ],
            "figures": { "gears": [ 3.4, 2.1, 1.4, 1.0, 0.8 ], "reverse": 3.2, "final_drive": 3.9, "clutch_torque": 450 } },
          { "id": "engines/data/small_turbo", "name": "small turbo", "slots": [ { "id": 1, "fits": [ "t:turbo" ] } ],
            "figures": { "turbo": { "boost": 0.4, "spool_rpm": 1500, "full_boost_rpm": 3000, "lag_up": 0.98 } } },
          { "id": "engines/data/big_turbo", "name": "big turbo", "slots": [ { "id": 1, "fits": [ "t:turbo" ] } ],
            "figures": { "turbo": { "boost": 0.5, "spool_rpm": 3500, "full_boost_rpm": 5500, "lag_up": 0.995, "lag_down": 0.97 } } },
          { "id": "engines/data/blower", "name": "blower", "slots": [ { "id": 1, "fits": [ "t:blower" ] } ],
            "figures": { "blower": { "boost": 0.5, "full_boost_rpm": 2500 } } }
        ] }
        """;

    private static readonly string[] Bare = { "engines/data/block", "engines/data/head", "engines/data/cam", "engines/data/carb", "engines/data/gearbox" };

    private PartsCatalog Catalog()
    {
        _temp.File("parts/engines/data/pack.json", Pack);
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        Assert.Empty(catalog.Problems);
        return catalog;
    }

    private static EngineReport Evaluate(PartsCatalog catalog, params string[] parts)
    {
        var tree = PartTreeBuilder.Build(catalog, parts, "Block")!;
        Assert.Empty(tree.Unplaced);
        return EngineEvaluator.Evaluate(catalog, tree);
    }

    [Fact]
    public void An_engine_of_figures_alone_runs_on_the_dyno()
    {
        var report = Evaluate(Catalog(), Bare);

        Assert.True(report.Runs, report.Problem);
        Assert.True(report.FromFigures);
        Assert.Equal(1.998, report.Dyno!.Displacement * 1000, 3);
        Assert.Equal(9.5, report.Dyno.Compression, 6);
        Assert.InRange(report.Dyno.MaxPowerHp, 60, 250);
        Assert.Equal(6500, report.LimiterRpm);
        Assert.Equal(7000, report.Dyno.Curve[^1].Rpm);
        Assert.Equal(850, report.IdleRpm);
        Assert.Equal(0.12, report.Inertia * PartFigures.KgM2PerInertiaUnit, 6);
        Assert.Equal(240, (report.Inputs!.IntakeClose - report.Inputs.IntakeOpen) * 720, 6);
        Assert.Equal(32, (0.5 - report.Inputs.SparkMin) * 720, 6);
        Assert.Equal(22, (0.5 - report.Inputs.SparkMin - report.Inputs.SparkInc) * 720, 6);
        Assert.Equal(120, report.Mass);
    }

    [Fact]
    public void The_gearbox_of_figures_has_as_many_gears_as_it_says_and_its_clutch_holds_its_torque()
    {
        var report = Evaluate(Catalog(), Bare);

        Assert.Equal(new[] { 3.4, 2.1, 1.4, 1.0, 0.8 }, report.GearRatios);
        Assert.Equal(3.2, report.ReverseRatio);
        Assert.Equal(3.9, report.FinalRatio);
        Assert.Equal(450, report.ClutchCapacity * PartFigures.ClutchNmPerUnit, 6);
    }

    [Fact]
    public void A_part_that_multiplies_a_figure_works_on_what_the_others_give()
    {
        var catalog = Catalog();
        var bare = Evaluate(catalog, Bare);
        var headed = Evaluate(catalog, [.. Bare, "engines/data/header"]);

        Assert.Equal(0.35 * 1.15, headed.Inputs!.ExhaustMax, 9);
        Assert.True(headed.Dyno!.MaxPowerHp > bare.Dyno!.MaxPowerHp);
    }

    [Fact]
    public void An_engine_of_figures_with_nothing_to_feed_it_does_not_run()
    {
        var report = Evaluate(Catalog(), "engines/data/block", "engines/data/head", "engines/data/cam");

        Assert.False(report.Runs);
        Assert.Contains("nothing feeds the engine", report.Problem);
    }

    [Fact]
    public void Twin_turbos_add_their_boost_and_each_is_a_turbo_of_its_own_in_the_cars_data()
    {
        var catalog = Catalog();
        var natural = Evaluate(catalog, Bare);
        var report = Evaluate(catalog, [.. Bare, "engines/data/small_turbo", "engines/data/big_turbo"]);

        Assert.Equal(2, report.Inputs!.Chargers.Count);
        Assert.Equal(0.9, EngineDyno.ChargersBoost(report.Inputs, 6000), 9);
        Assert.True(report.Dyno!.MaxPowerHp > natural.Dyno!.MaxPowerHp * 1.3);

        var files = AcEngineData.Generate(report, CarData());
        var engine = new IniText(files["engine.ini"]);
        Assert.Equal("0.980", engine.Get("TURBO_0", "LAG_UP"));
        Assert.Equal("0.985", engine.Get("TURBO_0", "LAG_DN"));
        Assert.Equal("0.995", engine.Get("TURBO_1", "LAG_UP"));
        Assert.Equal("0.970", engine.Get("TURBO_1", "LAG_DN"));
        Assert.Null(engine.Get("TURBO_2", "MAX_BOOST"));

        // The small one spools first
        Assert.True(engine.GetNumber("TURBO_0", "REFERENCE_RPM") < engine.GetNumber("TURBO_1", "REFERENCE_RPM"));

        // With the throttle open AC gives back the dyno's torque: the curve times what both turbos add
        var lut = files["power.lut"].Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('|')).ToDictionary(p => double.Parse(p[0], CultureInfo.InvariantCulture), p => double.Parse(p[1], CultureInfo.InvariantCulture));
        double AcBoost(int i, double rpm) =>
            Math.Min(engine.GetNumber($"TURBO_{i}", "MAX_BOOST")!.Value, engine.GetNumber($"TURBO_{i}", "MAX_BOOST")!.Value * Math.Pow(rpm / engine.GetNumber($"TURBO_{i}", "REFERENCE_RPM")!.Value, engine.GetNumber($"TURBO_{i}", "GAMMA")!.Value));
        foreach (var rpm in new[] { 5500.0, 6500.0 })
            Assert.Equal(report.Dyno.TorqueAt(rpm), lut[rpm] * (1 + AcBoost(0, rpm) + AcBoost(1, rpm)), report.Dyno.TorqueAt(rpm) * 0.03);

        // Five gears of its own
        var drivetrain = new IniText(files["drivetrain.ini"]);
        Assert.Equal("5", drivetrain.Get("GEARS", "COUNT"));
        Assert.Equal("0.800", drivetrain.Get("GEARS", "GEAR_5"));
        Assert.Equal("3.900", drivetrain.Get("GEARS", "FINAL"));
    }

    [Fact]
    public void A_blowers_boost_stays_in_the_curve()
    {
        var catalog = Catalog();
        var natural = Evaluate(catalog, Bare);
        var report = Evaluate(catalog, [.. Bare, "engines/data/blower"]);

        Assert.True(report.Dyno!.MaxTorque > natural.Dyno!.MaxTorque * 1.2);
        var engine = new IniText(AcEngineData.Generate(report, CarData())["engine.ini"]);
        Assert.Null(engine.Get("TURBO_0", "MAX_BOOST"));
    }

    [Fact]
    public void Figures_that_do_not_read_are_told_and_left_out()
    {
        var part = JsonConvert.DeserializeObject<PartDefinition>("""
            { "id": "engines/data/bad", "figures": {
                "bore": 86, "bore_size": 86, "stroke": { "plus": 2 }, "cylinders": 2.5, "compression": 1.2, "intake_flow": { "times": 0 },
                "chamber": "big", "gears": [ 3, 0 ], "turbo": { "boost": 0.5 }, "blower": [ { "boost": 0.4, "full_boost_rpm": 3000, "lag_up": 0.9 } ] } }
            """)!;
        var problems = new List<string>();

        var set = PartFigures.Read(part, problems)!;

        Assert.Equal([new PartFigure("bore", FigureOp.Set, 86)], set.Numbers);
        Assert.Null(set.Gears);
        // The blower is read, without the lag a blower has not got
        Assert.Equal(new DynoCharger(false, 0.4, 0, 3000, PartFigures.DefaultLagUp, PartFigures.DefaultLagDown), Assert.Single(set.Chargers));
        Assert.Collection(problems,
            p => Assert.Contains("\"bore_size\": no such figure", p),
            p => Assert.Contains("\"stroke\": give a number", p),
            p => Assert.Contains("no number of cylinders", p),
            p => Assert.Contains("compression of 1.2:1", p),
            p => Assert.Contains("multiplies by 0", p),
            p => Assert.Contains("\"big\" is not a number", p),
            p => Assert.Contains("a ratio of 0 is none", p),
            p => Assert.Contains("needs its \"boost\"", p),
            p => Assert.Contains("\"lag_up\" is not a figure of a blower", p));
    }

    [Fact]
    public void Figures_go_over_what_the_scripts_work_out_on_an_engine_of_the_source_game()
    {
        var catalog = PartsCatalog.Load(RepoPaths.PartsFolder);
        var build = catalog.EngineBuilds.Single(b => b.Id.EndsWith("chrysler-v8-pack/chrysler-la-340-275-hp", StringComparison.Ordinal));
        var tree = PartTreeBuilder.BuildEngine(catalog, build)!;
        var before = EngineEvaluator.Evaluate(catalog, tree);

        var crank = tree.Root.SelfAndDescendants().First(p => p.Is("Crankshaft")).Definition;
        var block = tree.Root.Definition;
        var problems = new List<string>();
        crank.Figures = JsonConvert.DeserializeObject<PartDefinition>("""{ "figures": { "stroke": 76.2 } }""")!.Figures;
        block.Figures = JsonConvert.DeserializeObject<PartDefinition>("""{ "figures": { "turbo": { "boost": 0.5, "full_boost_rpm": 3500 }, "gears": [ 2.9, 1.9, 1.3, 1.0 ], "lag_up": 0.99 } }""")!.Figures;
        crank.ReadFigures = PartFigures.Read(crank, problems);
        block.ReadFigures = PartFigures.Read(block, problems);
        Assert.Empty(problems);

        try
        {
            var after = EngineEvaluator.Evaluate(catalog, tree);

            Assert.True(after.Runs, after.Problem);
            Assert.False(after.FromFigures);
            Assert.Equal(0.0762, after.Inputs!.Stroke, 9);
            Assert.True(after.Dyno!.Displacement < before.Dyno!.Displacement);
            Assert.Equal(before.Inputs!.Bore, after.Inputs.Bore);
            Assert.Equal(new[] { 2.9, 1.9, 1.3, 1.0 }, after.GearRatios);
            Assert.Equal(before.FinalRatio, after.FinalRatio);
            Assert.Single(after.Inputs.Chargers);
            Assert.Equal(0.99, after.TurboLagUp);
            Assert.Equal(PartFigures.DefaultLagDown, after.TurboLagDown);
        }
        finally
        {
            crank.Figures = block.Figures = null;
            crank.ReadFigures = block.ReadFigures = null;
        }
    }

    [Fact]
    public void An_engine_without_figures_gives_the_dyno_the_very_inputs_the_scripts_made()
    {
        var inputs = new DynoInputs { Bore = 0.1 };
        Assert.Same(inputs, PartFigures.Apply(inputs, Array.Empty<PartFigureSet>()));
    }

    private Func<string, string?> CarData()
    {
        _temp.File("car/engine.ini", "[HEADER]\nVERSION=1\nPOWER_CURVE=power.lut\n[ENGINE_DATA]\nINERTIA=0.120\nLIMITER=6500\nMINIMUM=900\n");
        _temp.File("car/drivetrain.ini", "[TRACTION]\nTYPE=FWD\n[GEARS]\nCOUNT=4\nGEAR_R=-3.000\nGEAR_1=3.000\nGEAR_2=2.000\nGEAR_3=1.400\nGEAR_4=1.000\nFINAL=4.100\n[CLUTCH]\nMAX_TORQUE=250\n");
        return AcCarDataReader.ForFolder(_temp.Combine("car"));
    }
}
