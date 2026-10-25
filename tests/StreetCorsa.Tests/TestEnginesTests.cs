using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// The tests' own engines (Fixtures\test_engines, written by Fixtures\make_test_engines.py): each kind
/// goes together and does the job the tests take it for. Their figures are round on purpose; a change to the engine
/// model shows here as well as in the goldens.
/// </summary>
public class TestEnginesTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private static PartDefinition Part(string id) => Catalog.Get("engines/" + id)!;

    private static PartTree Build(string id) => PartTreeBuilder.BuildEngine(Catalog, Catalog.EngineBuilds.Single(b => b.Id == id))!;

    private static EngineReport Report(string id) => EngineEvaluator.Evaluate(Catalog, Build(id));

    // The battery on the V8's builds: no slot of an engine takes it, so it never goes into the tree
    private const string Battery = "engines/test_v8/battery";

    private static readonly string[] RunningIds =
    [
        "factory/test-v8", "rated/test-v8", "factory/test-v8-big", "kit/test-v8-stroker", "kit/test-v8-racer", "kit/test-v8-blown",
        "kit/test-v8-nitrous", "factory/test-six", "factory/test-flathead", "kit/test-flathead-dual", "kit/test-flathead-quad", "factory/test-four"
    ];

    public static TheoryData<string> Running => new(RunningIds);

    /// <summary>
    /// Every build of the tests' engines runs: the catalog is shared, and a build short of a part on purpose would be one the
    /// shops, the matcher and the tuner hand out. An engine short of a part is made in its test.
    /// </summary>
    [Fact]
    public void Every_test_build_is_one_that_runs()
    {
        var ours = Catalog.EngineBuilds.Where(b => b.Id.Contains("/test-", StringComparison.Ordinal)).Select(b => b.Id);

        Assert.Equal(RunningIds.Order(StringComparer.Ordinal), ours.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Running))]
    public void Every_test_build_goes_together_whole_and_runs(string id)
    {
        var build = Catalog.EngineBuilds.Single(b => b.Id == id);
        var tree = Build(id);

        Assert.Equal(build.Parts.Count(p => p != Battery), tree.Root.SelfAndDescendants().Count());
        Assert.True(EngineEvaluator.Evaluate(Catalog, tree).Runs, id);
    }

    [Fact]
    public void A_v8_short_of_its_water_pump_does_not_run_and_says_so()
    {
        var tree = Build("factory/test-v8");
        Assert.True(tree.Root.Children.Remove(tree.Root.Children.Single(c => c.Value.Definition.Id == "engines/test_v8/water_pump").Key));

        var report = EngineEvaluator.Evaluate(Catalog, tree);

        Assert.False(report.Runs);
        Assert.Contains("water pump", report.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_compression_reads_off_the_chamber_and_the_speed_parts_make_power()
    {
        Assert.Equal(9.4, Report("factory/test-v8").Dyno!.Compression, 1);
        Assert.Equal(10.4, Report("kit/test-v8-stroker").Dyno!.Compression, 1);
        Assert.Equal(9.5, Report("factory/test-six").Dyno!.Compression, 1);
        Assert.Equal(8.5, Report("factory/test-flathead").Dyno!.Compression, 1);

        // Every kit and the big block well over the small block as it comes; the stroker, with the speed shop's top end, over that
        var stock = Report("factory/test-v8").Dyno!.MaxPowerHp;
        foreach (var id in new[] { "factory/test-v8-big", "kit/test-v8-stroker", "kit/test-v8-racer", "kit/test-v8-blown" })
            Assert.True(Report(id).Dyno!.MaxPowerHp > stock * 1.1, id);
        Assert.True(Report("kit/test-v8-stroker").Dyno!.MaxPowerHp > Report("kit/test-v8-racer").Dyno!.MaxPowerHp);
    }

    [Fact]
    public void The_rated_build_runs_to_its_rating()
    {
        var build = Catalog.EngineBuilds.Single(b => b.Id == "rated/test-v8");

        Assert.True(build.RunsToItsRating(Report(build.Id).Dyno!.MaxPowerHp));
    }

    private static EngineBuildIndex Index => TestCars.Cooling.Value.Builds;

    [Fact]
    public void A_speed_shop_build_is_never_a_cars_factory_engine_nor_swapped_in()
    {
        var aftermarket = Index.Runnable.Where(b => b.Build.Aftermarket && b.Build.Id.Contains("/test-", StringComparison.Ordinal)).ToList();
        Assert.Equal(["kit/test-flathead-dual", "kit/test-flathead-quad", "kit/test-v8-racer"], aftermarket.Select(b => b.Build.Id).Order());

        foreach (var hot in aftermarket)
            Assert.False(StockEngineMatcher.Best(Index, null, "Dodge", hot.Build.Name, hot.PowerHp)?.Build.Aftermarket ?? false, hot.Build.Name);
        Assert.False(StockEngineMatcher.Best(Index, null, "Dodge", "Test Mopar V8", 350)?.Build.Aftermarket ?? false);

        // The small block worked on as much as it gets: the swaps it takes are the family's factory engines and kits, never
        // the speed shop's build, which is in the swaps' power band
        var stock = Index.Runnable.Single(b => b.Build.Id == "factory/test-v8");
        var racer = Index.Runnable.Single(b => b.Build.Id == "kit/test-v8-racer");
        Assert.InRange(racer.PowerHp, stock.PowerHp * EngineRules.Now.Factory.SwapMinPower, stock.PowerHp * EngineRules.Now.Factory.SwapMaxPower);
        var random = new Random(1);
        var swaps = 0;
        for (var i = 0; i < 200; i++)
        {
            var engine = EngineFactory.CreateTuned(Catalog, Index, stock, 0.8, 1.0, random)!;
            swaps += engine.Changes.Count(c => c.StartsWith("engine ", StringComparison.Ordinal));
            Assert.DoesNotContain(engine.Changes, c => aftermarket.Any(a => c.Contains(a.Build.Name, StringComparison.Ordinal)));
        }

        Assert.True(swaps > 10, $"{swaps} swaps");
    }

    [Fact]
    public void Each_kind_has_its_family()
    {
        string FamilyOf(string id) => Index.Runnable.Single(b => b.Build.Id == id).Family;

        Assert.Equal("mopar", FamilyOf("factory/test-v8"));
        Assert.Equal("ford", FamilyOf("factory/test-six"));
        Assert.Equal("ford", FamilyOf("factory/test-flathead"));
        Assert.Equal("peugeot", FamilyOf("factory/test-four"));
    }

    [Theory]
    [InlineData("test_v8/distributor", "distributor")]
    [InlineData("test_six/distributor", "side_distributor")]
    [InlineData("test_v8/radiator", "radiator")]
    [InlineData("test_v8/radiator_electric_fan", "radiator_fan")]
    [InlineData("test_v8/water_pump", "water_pump")]
    [InlineData("test_v8/fan", "fan")]
    [InlineData("test_v8/drive_belt", "belt")]
    [InlineData("test_v8/steering_pump", "steering_pump")]
    [InlineData("test_v8/steering_belt", "steering_belt")]
    [InlineData("test_v8/left_valve_cover", "valve_cover")]
    [InlineData("test_v8/block_cover", "block_cover")]
    [InlineData("test_v8/timing_cover", "timing_cover")]
    [InlineData("test_v8/timing_chain", "timing_chain")]
    [InlineData("test_v8/camshaft_timing_gear", "timing_gear")]
    [InlineData("test_v8/crankshaft_pulley", "crank_pulley")]
    [InlineData("test_v8/roots_blower", "charger")]
    [InlineData("test_v8/blower_belt", "charger_belt")]
    [InlineData("test_v8/nitrous", "nitrous")]
    [InlineData("test_flathead/velocity_stack", "air_filter")]
    public void Each_part_is_of_the_kind_its_tests_take_it_for(string id, string kind) =>
        Assert.Equal(kind, Catalog.WorkOrder.KindOf(Part(id)));

    [Theory]
    [InlineData("kit/test-flathead-dual", "test_flathead/dual_intake_manifold", 2)]
    [InlineData("kit/test-flathead-quad", "test_flathead/quad_intake_manifold", 4)]
    public void A_row_intake_runs_only_with_a_carburettor_on_every_pad(string build, string intake, int pads)
    {
        var carbPads = Part(intake).Slots.Where(s => s.Takes.Contains("carb:97")).Select(s => s.Id).ToList();
        Assert.Equal(pads, carbPads.Count);
        Assert.True(Report(build).Runs);

        foreach (var pad in carbPads)
        {
            var tree = Build(build);
            var manifold = tree.Root.SelfAndDescendants().Single(p => p.Definition.Id == "engines/" + intake);
            Assert.True(manifold.Children.Remove(pad));

            var report = EngineEvaluator.Evaluate(Catalog, tree);
            Assert.False(report.Runs);
            Assert.Contains("carburettor", report.Problem, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_worn_velocity_stack_gives_the_attendant_nothing_but_a_worn_cleaner_does()
    {
        Car CarWith(string build, string cleaner)
        {
            var car = new Car("car_a") { HasPartsAssigned = true };
            car.Parts.Add(PartTrees.FromInstalled(Build(build).Root, PartInstance.CarEngineSlot));
            foreach (var part in car.Engine!.SelfAndDescendants().Where(p => p.DefinitionId == "engines/" + cleaner)) part.Wear = 0.05;
            return car;
        }

        Assert.DoesNotContain(AttendantCheck.Look(CarWith("kit/test-flathead-quad", "test_flathead/velocity_stack"), Catalog), h => h.Key == "station.hint.air_filter");
        Assert.Contains(AttendantCheck.Look(CarWith("kit/test-flathead-dual", "test_flathead/air_cleaner"), Catalog), h => h.Key == "station.hint.air_filter");
    }

    [Fact]
    public void The_four_drives_the_front_wheels_the_others_the_rear()
    {
        Assert.Equal(1, Report("factory/test-four").DriveType);
        Assert.Equal(2, Report("factory/test-v8").DriveType);
    }

    [Fact]
    public void The_blower_carries_the_carburettor_and_adds_torque()
    {
        var blower = Build("kit/test-v8-blown").Root.SelfAndDescendants().Single(p => p.Definition.Id == "engines/test_v8/roots_blower");

        Assert.Contains(blower.Children.Values, p => p.Definition.Id == "engines/test_v8/carburetor");
        Assert.True(Report("kit/test-v8-blown").Dyno!.MaxTorque > Report("factory/test-v8").Dyno!.MaxTorque);
        Assert.True(Report("kit/test-v8-blown").Dyno!.MaxPowerHp > Report("factory/test-v8").Dyno!.MaxPowerHp);
    }

    /// <summary>A speed part makes power on the dyno over the stock part in its place, and so costs more</summary>
    [Theory]
    [InlineData("test_flathead/right_aluminium_head", "test_flathead/right_head")]
    [InlineData("test_flathead/dual_intake_manifold", "test_flathead/intake_manifold")]
    [InlineData("test_flathead/quad_intake_manifold", "test_flathead/dual_intake_manifold")]
    [InlineData("test_flathead/right_tube_header", "test_flathead/right_header")]
    [InlineData("test_v8/speed_camshaft", "test_v8/camshaft")]
    [InlineData("test_v8/high_rise_intake_manifold", "test_v8/intake_manifold")]
    [InlineData("test_v8/racer_carburetor", "test_v8/carburetor")]
    [InlineData("test_v8/left_tube_header", "test_v8/left_header")]
    public void A_speed_part_costs_more_than_the_part_it_replaces(string speed, string stock) =>
        Assert.True(TestCars.Prices.Value.Price(Part(speed)) > TestCars.Prices.Value.Price(Part(stock)), $"{speed} against {stock}");
}
