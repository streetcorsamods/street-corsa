using Newtonsoft.Json;
using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Rules;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-11-05 tech audit's fixes in the parts: a speed of none hides no other part's, part speeds the dyno can
/// reach, setup.ini's ride height on its steps from the held figure, the rivals' dyno budget, the shared wheel rules,
/// and the tuning a part takes
/// </summary>
public sealed class Audit1105DTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private static PartTree Build(string id) => PartTreeBuilder.BuildEngine(Catalog, Catalog.EngineBuilds.Single(b => b.Id == id))!;

    /// <summary>The part put back in its place as a copy of its definition with this max_rpm, all it carried on it</summary>
    private static void WithMaxRpm(InstalledPart part, double maxRpm)
    {
        var definition = JsonConvert.DeserializeObject<PartDefinition>(JsonConvert.SerializeObject(part.Definition))!;
        definition.Specs["max_rpm"] = maxRpm;
        var copy = new InstalledPart(definition);
        foreach (var (at, child) in part.Children.ToList()) copy.Mount(at, child, child.OwnSlot);
        part.Parent!.Mount(part.ParentSlot, copy, part.OwnSlot);
    }

    // ---- part speeds ----

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    public void A_head_or_a_crank_that_says_a_speed_of_none_hides_no_other_parts(double none)
    {
        var stock = EngineAssembly.Of(Build("factory/test-v8").Root).Inputs;
        Assert.True(stock.FloatRpm >= EngineDyno.LeastSpeed);
        Assert.True(stock.MaxRpm >= EngineDyno.LeastSpeed);

        var tree = Build("factory/test-v8");
        foreach (var head in tree.Root.SelfAndDescendants().Where(p => p.Is(PartClasses.CylinderHead)).ToList()) WithMaxRpm(head, none);
        WithMaxRpm(tree.Root.SelfAndDescendants().First(p => p.Is(PartClasses.Crankshaft)), none);
        var inputs = EngineAssembly.Of(tree.Root).Inputs;

        Assert.Equal(stock.FloatRpm, inputs.FloatRpm);
        Assert.Equal(stock.MaxRpm, inputs.MaxRpm);
    }

    [Theory]
    [InlineData("max_rpm", 0)]
    [InlineData("max_rpm", 50)]
    [InlineData("float_rpm", 99)]
    [InlineData("float_rpm", 26_000)]
    [InlineData("max_rpm", 29_000)]
    public void A_part_speed_under_any_or_one_the_engine_would_rev_past_the_dyno_off_is_left_out(string figure, double rpm)
    {
        var part = JsonConvert.DeserializeObject<PartDefinition>($$"""{ "id": "engines/data/bad", "figures": { "{{figure}}": {{rpm}} } }""")!;
        var problems = new List<string>();

        Assert.Null(PartFigures.Read(part, problems));
        Assert.Contains("is no engine speed", Assert.Single(problems));
    }

    [Theory]
    [InlineData("float_rpm")]
    [InlineData("max_rpm")]
    public void The_most_a_part_speed_may_say_keeps_the_engine_within_the_dyno(string figure)
    {
        // The highest whole speed the check lets through, found by asking it
        var most = 0.0;
        for (var rpm = 31_000.0; rpm > 20_000; rpm -= 1)
        {
            var part = JsonConvert.DeserializeObject<PartDefinition>($$"""{ "id": "engines/data/fast", "figures": { "{{figure}}": {{rpm}} } }""")!;
            if (PartFigures.Read(part, []) == null) continue;
            most = rpm;
            break;
        }

        var inputs = figure == "float_rpm" ? PartFigures.DataEngineBase with { FloatRpm = most } : PartFigures.DataEngineBase with { MaxRpm = most };
        inputs = inputs with { RpmLimit = 0 };
        Assert.True(most > 20_000);
        Assert.InRange(EngineDyno.TopRpm(inputs), 0, EngineDyno.MaxRpm);
    }

    // ---- setup.ini's ride height ----

    [Fact]
    public void The_ride_heights_range_steps_from_the_figure_the_travel_holds_it_to()
    {
        // A step of 7 tenths of a mm that does not divide the distance from the setup's figure to the travel's edge
        _temp.File("data/setup.ini", "[ROD_LENGTH_LF]\nMIN=900\nMAX=1100\nSTEP=7\n");
        _temp.File("own/suspensions.ini", "[FRONT]\nROD_LENGTH=0.1000\nPACKER_RANGE=0.040\n[REAR]\nROD_LENGTH=0.1000\n");
        // Raised 30 mm by the setup, past what the travel lets it move
        _temp.File("data/suspensions.ini", "[FRONT]\nROD_LENGTH=0.1300\nPACKER_RANGE=0.040\n[REAR]\nROD_LENGTH=0.1000\n");
        var race = AcCarDataReader.ForFolder(_temp.Combine("data"));
        var own = AcCarDataReader.ForFolder(_temp.Combine("own"));
        var choice = new SetupChoice
        {
            Values = new Dictionary<string, double> { [CarSetup.RideHeightFront] = 30 },
            Allowed = new Dictionary<string, SetupRange> { [CarSetup.RideHeightFront] = new(-40, 40, 1) }
        };

        var setup = new IniText(AcSetupData.Range(choice, PartsCatalog.Empty(_temp.Path), null, race, own)["setup.ini"]);
        var held = AcRunningGearData.HeldToTravel(own("suspensions.ini")!, race("suspensions.ini")!);
        var heldUnits = new IniText(held!).GetNumber("FRONT", "ROD_LENGTH")!.Value * 10_000;
        var (min, max) = (setup.GetNumber("ROD_LENGTH_LF", "MIN")!.Value, setup.GetNumber("ROD_LENGTH_LF", "MAX")!.Value);

        // The figure AC is given is inside the range, on its steps from MIN
        Assert.True(heldUnits < 1300);
        Assert.InRange(heldUnits, min - 1e-6, max + 1e-6);
        var steps = (heldUnits - min) / 7;
        Assert.Equal(Math.Round(steps), steps, 6);
    }

    // ---- the rivals' dyno ----

    [Fact]
    public void A_rivals_engine_of_many_adjustable_specs_costs_no_more_than_the_dyno_budget()
    {
        var adjust = string.Join(", ", Enumerable.Range(0, 30).Select(i => $$"""
            "s{{i}}": { "min": 0, "max": 100, "step": 1 }
            """));
        var specs = string.Join(", ", Enumerable.Range(0, 30).Select(i => $"\"s{i}\": 50"));
        _temp.Parts("parts/rival/many", $$"""[ { "id": "rival/many/block", "specs": { {{specs}}, "adjust": { {{adjust}} } } } ]""");
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        var car = new Car
        {
            DefinitionId = "test_car", HasPartsAssigned = true,
            Parts = [new PartInstance("rival/many/block") { ParentSlot = PartInstance.CarEngineSlot }]
        };
        var rows = CarTune.Rows(car, catalog, CarSetup.For(car.Parts, car.Setup, catalog, new TuningRules().Ranges()));
        Assert.Equal(30, rows.Count(r => r.Tab == TuneTab.Engine));

        // Every spec does a little better higher: nothing would end the search early
        var runs = 0;
        double? Dyno(PartInstance engine)
        {
            runs++;
            return engine.Tuning.Values.Sum();
        }

        var wanted = RivalSetup.Wanted(car, rows, new Dictionary<string, string> { [RivalSetup.Engine] = RivalSetup.Power }, Dyno);

        Assert.InRange(runs, 1, RivalSetup.MostDynoRuns);
        Assert.Equal(RivalSetup.MostEngineRows, wanted.Count);
        Assert.Equal(100, wanted[0].Value);
    }

    [Fact]
    public void The_rows_without_the_locked_ones_are_the_rows_that_move()
    {
        var catalog = Catalog;
        var car = new Car { DefinitionId = "test_car", HasPartsAssigned = true, Parts = [] };
        var setup = CarSetup.For(car.Parts, car.Setup, catalog, new TuningRules().Ranges());

        var all = CarTune.Rows(car, catalog, setup);
        var moving = CarTune.Rows(car, catalog, setup, withLocked: false);

        Assert.Equal(all.Where(r => !r.IsLocked).Select(r => r.Key), moving.Select(r => r.Key));
        Assert.DoesNotContain(moving, r => r.IsLocked);
    }

    [Fact]
    public void A_list_elements_tuning_starts_from_that_element()
    {
        var part = JsonConvert.DeserializeObject<PartDefinition>("""{ "id": "x", "specs": { "ratio": [3.0, 2.0, 1.5] } }""")!;
        Assert.Equal(1.5, CarTune.StockOf(part, "ratio[2]"));
    }

    // ---- wheels ----

    private static PartInstance Rim(int corner, bool tyre)
    {
        var rim = new PartInstance("rims/test/rim") { ParentSlot = 101 + corner };
        if (tyre) rim.Children.Add(new PartInstance("tyres/test/tyre") { ParentSlot = RunningGear.TyreSlotOnRim });
        return rim;
    }

    [Fact]
    public void The_lift_the_race_and_the_picture_read_a_wheel_off_and_a_bare_rim_alike()
    {
        var parts = new List<PartInstance> { Rim(0, tyre: true), Rim(1, tyre: false), Rim(3, tyre: true) };

        Assert.Equal(1 << 2, RunningGear.WheelsOff(parts));
        Assert.Equal(1 << 1, RunningGear.BareRims(parts));
        Assert.Equal(RunningGear.WheelsOff(parts), ShownParts.MissingWheels(parts));
        Assert.Equal(PartWords.Say("work.wheels_first"), CarWork.WhyNotDown(parts, hasRunningGear: true));

        parts.Add(Rim(2, tyre: true));
        Assert.Equal(0, RunningGear.WheelsOff(parts));
        Assert.Equal(PartWords.Say("work.tyres_first"), CarWork.WhyNotDown(parts, hasRunningGear: true));

        parts[1] = Rim(1, tyre: true);
        Assert.Equal(0, RunningGear.BareRims(parts));
        Assert.Null(CarWork.WhyNotDown(parts, hasRunningGear: true));
    }

    [Theory]
    [InlineData(0.3, 0.3, 1)]
    [InlineData(0.33, 0.3, 1.1)]
    [InlineData(0.3, 0, 1)]
    [InlineData(0, 0.3, 1)]
    [InlineData(1e308, 1e-308, 1)]
    [InlineData(double.PositiveInfinity, 0.3, 1)]
    public void A_tyres_size_over_the_factorys_is_a_finite_ratio_or_none(double mounted, double factory, double ratio)
    {
        Assert.Equal(ratio, AcRunningGearData.Ratio(mounted, factory), 9);
    }

    [Fact]
    public void Some_tyre_fits_reads_the_catalogs_tyres_each_time_it_is_asked()
    {
        _temp.Parts("parts/wheels/test", """
            [
              { "id": "wheels/test/rim", "class": "rim", "specs": { "wheel_radius": 0.1905, "rim_width": 6 } },
              { "id": "wheels/test/odd_rim", "class": "rim", "specs": { "wheel_radius": 0.2, "rim_width": 6 } },
              { "id": "wheels/test/tyre", "class": "tyre", "specs": { "wheel_radius": 190.5 } }
            ]
            """);
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));

        Assert.True(RunningGear.SomeTyreFits(catalog, catalog.Get("wheels/test/rim")!));
        Assert.False(RunningGear.SomeTyreFits(catalog, catalog.Get("wheels/test/odd_rim")!));
        Assert.True(RunningGear.SomeTyreFits(catalog, catalog.Get("wheels/test/rim")!));
    }

    // ---- the tuning a part takes ----

    private static PartDefinition Coilover() => new()
    {
        Id = "x",
        Specs = { ["damping"] = 2000L, ["adjust"] = Newtonsoft.Json.Linq.JObject.Parse("""{ "damping": { "min": 1500, "max": 6000, "step": 250 } }""") }
    };

    [Fact]
    public void A_tuning_past_the_adjust_range_on_a_save_is_held_and_one_inside_it_is_the_very_tuning()
    {
        var coilover = Coilover();
        var inside = new Dictionary<string, double> { ["damping"] = 3000, ["other"] = 1e9 };
        var past = new Dictionary<string, double> { ["damping"] = 20000, ["other"] = 1e9 };

        Assert.Same(inside, coilover.TuningHeld(inside));
        var held = coilover.TuningHeld(past)!;
        Assert.Equal(6000, held["damping"]);
        Assert.Equal(1e9, held["other"]);
        Assert.Equal(1500, coilover.Held("damping", -5));
        Assert.Null(coilover.TuningHeld(null));
    }

    [Fact]
    public void A_tuning_that_is_no_number_stays_none_and_the_parts_own_spec_stands()
    {
        var coilover = Coilover();
        var nan = new Dictionary<string, double> { ["damping"] = double.NaN };

        Assert.True(double.IsNaN(coilover.Held("damping", double.NaN)));
        Assert.Same(nan, coilover.TuningHeld(nan));
        Assert.Same(coilover, coilover.WithTuning(nan));
        Assert.Same(coilover, coilover.WithTuning(new Dictionary<string, double> { ["damping"] = double.PositiveInfinity }));
    }

    [Fact]
    public void A_list_elements_tuning_is_left_to_the_specs_path_and_never_becomes_a_spec()
    {
        var gearbox = JsonConvert.DeserializeObject<PartDefinition>("""{ "id": "x", "specs": { "ratio": [3.0, 2.0, 1.5], "final": 3.5 } }""")!;

        Assert.Same(gearbox, gearbox.WithTuning(new Dictionary<string, double> { ["ratio[2]"] = 1.2 }));
        var tuned = gearbox.WithTuning(new Dictionary<string, double> { ["ratio[2]"] = 1.2, ["final"] = 3.9 });
        Assert.NotSame(gearbox, tuned);
        Assert.False(tuned.Specs.ContainsKey("ratio[2]"));
        Assert.Equal(3.9, tuned.Number("final"));
        Assert.Equal(3.5, gearbox.Number("final"));
    }
}
