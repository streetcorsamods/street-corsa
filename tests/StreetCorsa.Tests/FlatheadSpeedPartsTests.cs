using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// The flathead mod's speed parts (Edelbrock heads, 2x and 4x Stromberg 97 intakes) and the rules they brought: every
/// carburettor pad of a row enforced, speed-shop builds kept out of the factory engines and the swaps, the 97-size
/// fittings, an open velocity stack that never clogs. The mod is outside the repository: its parts and builds are in the
/// tests' fixture (Fixtures\parts\engines\flathead, data only), read as the base game's, so its ids go without its
/// namespace here.
/// </summary>
public class FlatheadSpeedPartsTests
{
    private const string Pack = "engines/flathead/";

    private static Lazy<PartsCatalog> Catalog => TestCars.Catalog;

    private static PartDefinition Part(string name) => Catalog.Value.Get(Pack + name)!;

    private static PartTree Build(string idEnd)
    {
        var catalog = Catalog.Value;
        var build = catalog.EngineBuilds.Single(b => b.Id.EndsWith(idEnd, StringComparison.Ordinal));
        return PartTreeBuilder.BuildEngine(catalog, build)!;
    }

    [Theory]
    [InlineData("mercury-255-race-3spd", "polished_4x2_bbl_racing_intake_manifold_flathead_no_fuel_pump_stand", 4)]
    [InlineData("mercury-255-edelbrock-3spd", "edelbrock_2x2_bbl_intake_manifold_linkage_and_fuel_pump_flathead", 2)]
    public void A_row_intake_runs_only_with_a_carburettor_on_every_pad(string build, string intake, int pads)
    {
        var catalog = Catalog.Value;
        Assert.True(EngineEvaluator.Evaluate(catalog, Build(build)).Runs);

        var carbPads = Part(intake).Slots.Where(s => s.Takes.Contains("carb:97")).Select(s => s.Id).ToList();
        Assert.Equal(pads, carbPads.Count);
        Assert.Contains(7, carbPads);
        foreach (var pad in carbPads)
        {
            var tree = Build(build);
            var manifold = tree.Root.SelfAndDescendants().Single(p => p.Definition.Id == Pack + intake);
            Assert.True(manifold.Children.Remove(pad));

            var report = EngineEvaluator.Evaluate(catalog, tree);
            Assert.False(report.Runs);
            Assert.Contains("carburettor", report.Problem, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_speed_shop_build_is_never_a_cars_factory_engine_nor_swapped_in()
    {
        var catalog = Catalog.Value;
        var index = EngineBuildIndex.Create(catalog);
        var aftermarket = index.Runnable.Where(b => b.Build.Aftermarket).ToList();
        Assert.Equal(4, aftermarket.Count(b => b.Build.Id.StartsWith(Pack, StringComparison.Ordinal)));

        foreach (var hot in aftermarket)
            Assert.False(StockEngineMatcher.Best(index, null, "Ford", hot.Build.Name, hot.PowerHp)?.Build.Aftermarket ?? false, hot.Build.Name);

        var random = new Random(1);
        foreach (var stock in new[] { "mercury-255-mercomatic", "ford-221-1940-3spd", "ford-221-1932-3spd" }.Select(id => index.Runnable.Single(b => b.Build.Id.EndsWith(id, StringComparison.Ordinal))))
        {
            for (var i = 0; i < 40; i++)
            {
                var engine = EngineFactory.CreateTuned(catalog, index, stock, 0.8, 1.0, random)!;
                Assert.DoesNotContain(engine.Changes, c => aftermarket.Any(a => c.Contains(a.Build.Name, StringComparison.Ordinal)));
            }
        }
    }

    [Fact]
    public void Row_pads_take_the_Stromberg_and_a_Strombergs_horn_takes_only_97_size_cleaners()
    {
        bool Mates(string child, int childSlot, string parent, int parentSlot) =>
            PartsCatalog.CanMate(Part(child).Slots.Single(s => s.Id == childSlot), Part(parent).Slots.Single(s => s.Id == parentSlot));

        Assert.True(Mates("stromberg_97_carburetor_2_bbl_150_cfm", 10, "polished_4x2_bbl_racing_intake_manifold_flathead_no_fuel_pump_stand", 300));
        Assert.True(Mates("stromberg_97_carburetor_2_bbl_150_cfm", 10, "flathead_v8_2_bbl_intake_manifold", 7));
        Assert.False(Mates("holley_2_bbl_carburetor_flathead", 10, "edelbrock_2x2_bbl_intake_manifold_linkage_and_fuel_pump_flathead", 300));
        Assert.True(Mates("holley_2_bbl_carburetor_flathead", 10, "flathead_v8_2_bbl_intake_manifold", 7));

        Assert.True(Mates("chrome_helmet_air_cleaner_stromberg_97", 12, "stromberg_97_carburetor_2_bbl_150_cfm", 11));
        Assert.True(Mates("polished_elbow_velocity_stack_stromberg_97", 12, "stromberg_97_carburetor_2_bbl_150_cfm", 11));
        Assert.False(Mates("ford_oil_bath_air_cleaner_stock", 12, "stromberg_97_carburetor_2_bbl_150_cfm", 11));
        Assert.False(Mates("chrome_round_air_cleaner_flathead", 12, "stromberg_97_carburetor_2_bbl_150_cfm", 11));
        Assert.True(Mates("ford_oil_bath_air_cleaner_stock", 12, "holley_2_bbl_carburetor_flathead", 11));
        Assert.True(Mates("chrome_helmet_air_cleaner_stromberg_97", 12, "holley_2_bbl_carburetor_flathead", 11));
    }

    [Fact]
    public void A_worn_velocity_stack_gives_the_attendant_nothing_but_a_worn_cleaner_does()
    {
        var catalog = Catalog.Value;
        Car CarWith(string build, string cleaner)
        {
            var car = new Car("car_a") { HasPartsAssigned = true };
            car.Parts.Add(PartTrees.FromInstalled(Build(build).Root, PartInstance.CarEngineSlot));
            foreach (var part in car.Engine!.SelfAndDescendants().Where(p => p.DefinitionId == Pack + cleaner)) part.Wear = 0.05;
            return car;
        }

        Assert.DoesNotContain(AttendantCheck.Look(CarWith("mercury-255-race-3spd", "polished_elbow_velocity_stack_stromberg_97"), catalog), h => h.Key == "station.hint.air_filter");
        Assert.Contains(AttendantCheck.Look(CarWith("mercury-255-edelbrock-3spd", "chrome_helmet_air_cleaner_stromberg_97"), catalog), h => h.Key == "station.hint.air_filter");
    }

    [Theory]
    [InlineData("edelbrock_aluminium_right_hand_cylinder_head_flathead_65_cc", "flathead_v8_right_hand_cylinder_head_aluminium_red")]
    [InlineData("edelbrock_2x2_bbl_intake_manifold_linkage_and_fuel_pump_flathead", "flathead_v8_2_bbl_intake_manifold")]
    [InlineData("polished_4x2_bbl_racing_intake_manifold_flathead_no_fuel_pump_stand", "edelbrock_2x2_bbl_intake_manifold_linkage_and_fuel_pump_flathead")]
    [InlineData("flathead_v8_right_hand_tube_exhaust_header", "ford_flathead_v8_right_hand_cast_iron_exhaust_manifold_stock")]
    public void A_speed_part_costs_more_than_the_part_it_replaces(string speed, string stock) =>
        Assert.True(TestCars.Prices.Value.Price(Part(speed)) > TestCars.Prices.Value.Price(Part(stock)));
}
