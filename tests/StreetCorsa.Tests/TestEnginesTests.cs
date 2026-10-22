using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// The tests' own engines (roadmap step 38, Fixtures\test_engines, written by Fixtures\make_test_engines.py): each kind
/// goes together and does the job the tests take it for. Their figures are round on purpose; a change to the engine
/// model shows here as well as in the goldens.
/// </summary>
public class TestEnginesTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private static PartDefinition Part(string id) => Catalog.Get("engines/" + id)!;

    private static PartTree Build(string id) => PartTreeBuilder.BuildEngine(Catalog, Catalog.EngineBuilds.Single(b => b.Id == id))!;

    private static EngineReport Report(string id) => EngineEvaluator.Evaluate(Catalog, Build(id));

    public static TheoryData<string> Running =>
    [
        "factory/test-v8", "rated/test-v8", "kit/test-v8-stroker", "kit/test-v8-racer", "kit/test-v8-blown", "kit/test-v8-nitrous",
        "factory/test-six", "factory/test-flathead", "kit/test-flathead-dual", "kit/test-flathead-quad", "factory/test-four"
    ];

    [Theory]
    [MemberData(nameof(Running))]
    public void Every_test_build_goes_together_whole_and_runs(string id)
    {
        var build = Catalog.EngineBuilds.Single(b => b.Id == id);
        var tree = Build(id);

        Assert.Equal(build.Parts.Count, tree.Root.SelfAndDescendants().Count());
        Assert.True(EngineEvaluator.Evaluate(Catalog, tree).Runs, id);
    }

    [Fact]
    public void The_build_short_of_its_water_pump_does_not_run_and_says_so()
    {
        var report = Report("factory/test-v8-no-pump");

        Assert.False(report.Runs);
        Assert.Contains("water pump", report.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_compression_reads_off_the_chamber_and_the_stroker_makes_more()
    {
        Assert.Equal(9.4, Report("factory/test-v8").Dyno!.Compression, 1);
        Assert.Equal(10.4, Report("kit/test-v8-stroker").Dyno!.Compression, 1);
        Assert.Equal(9.5, Report("factory/test-six").Dyno!.Compression, 1);
        Assert.Equal(8.5, Report("factory/test-flathead").Dyno!.Compression, 1);

        var stock = Report("factory/test-v8").Dyno!.MaxPowerHp;
        Assert.True(Report("kit/test-v8-stroker").Dyno!.MaxPowerHp > stock);
        Assert.True(Report("kit/test-v8-racer").Dyno!.MaxPowerHp > stock);
    }

    [Fact]
    public void The_rated_build_runs_to_its_rating()
    {
        var build = Catalog.EngineBuilds.Single(b => b.Id == "rated/test-v8");

        Assert.True(build.RunsToItsRating(Report(build.Id).Dyno!.MaxPowerHp));
    }

    [Fact]
    public void The_speed_shop_builds_are_never_a_factory_engine()
    {
        var index = EngineBuildIndex.Create(Catalog);
        var ours = index.Runnable.Where(b => b.Build.Id.Contains("/test-", StringComparison.Ordinal)).ToList();

        Assert.Equal(["kit/test-flathead-dual", "kit/test-flathead-quad", "kit/test-v8-racer"],
            ours.Where(b => b.Build.Aftermarket).Select(b => b.Build.Id).Order());
        Assert.False(StockEngineMatcher.Best(index, null, "Dodge", "Test Mopar V8", 350)?.Build.Aftermarket ?? false);
    }

    [Fact]
    public void Each_kind_has_its_family()
    {
        var index = EngineBuildIndex.Create(Catalog);
        string FamilyOf(string id) => index.Runnable.Single(b => b.Build.Id == id).Family;

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
    }
}
