using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// The flathead mod's speed parts (Edelbrock heads, 2x and 4x Stromberg 97 intakes) and the rules they brought: every
/// carburettor pad of a row enforced, speed-shop builds kept out of the factory engines and the swaps, the old factory
/// ids of saves following the stock parts, the 97-size fittings, an open velocity stack that never clogs.
/// </summary>
public class FlatheadSpeedPartsTests
{
    private const string Pack = "flathead:engines/flathead/";

    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(ModSet.Load(RepoPaths.Content).Folders(ContentTypes.Parts).ToList()));

    private static PartDefinition Part(string name) => Catalog.Value.Get(Pack + name)!;

    private static PartTree Build(string idEnd)
    {
        var catalog = Catalog.Value;
        var build = catalog.EngineBuilds.Single(b => b.Id.EndsWith(idEnd, StringComparison.Ordinal));
        return PartTreeBuilder.BuildEngine(catalog, build)!;
    }

    [Theory]
    [InlineData("mercury-255-race-3spd", "FH_intake_4x97", 4)]
    [InlineData("mercury-255-edelbrock-3spd", "FH_intake_2x97", 2)]
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
    public void Rules_read_out_of_scripts_are_left_to_the_scripts()
    {
        // The converted packs' required_slots hold rules their scripts ask only on some cars: none is enforced
        var converted = Catalog.Value.Parts.Values.Where(p => ModIds.IsBase(ModIds.ModOf(p.Id))).SelectMany(p => p.RequiredSlots);
        Assert.DoesNotContain(converted, r => r.Enforce);
    }

    [Fact]
    public void A_speed_shop_build_is_never_a_cars_factory_engine_nor_swapped_in()
    {
        var catalog = Catalog.Value;
        var index = EngineBuildIndex.Create(catalog);
        var aftermarket = index.Runnable.Where(b => b.Build.Aftermarket).ToList();
        Assert.Equal(4, aftermarket.Count(b => b.Build.Id.StartsWith(Pack, StringComparison.Ordinal)));

        foreach (var hot in aftermarket)
            Assert.False(StockEngineMatcher.Best(index, "Ford", hot.Build.Name, hot.PowerHp)?.Build.Aftermarket ?? false, hot.Build.Name);

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

    [Theory]
    [InlineData("FH_L_header", "FH_L_manifold")]
    [InlineData("FH_R_header", "FH_R_manifold")]
    [InlineData("FH_air_cleaner", "FH_oil_bath")]
    public void The_old_factory_ids_of_a_save_are_the_stock_parts_now(string old, string stock) =>
        Assert.Equal(Pack + stock, Catalog.Value.CurrentId(Pack + old));

    [Fact]
    public void Row_pads_take_the_Stromberg_and_a_Strombergs_horn_takes_only_97_size_cleaners()
    {
        var catalog = Catalog.Value;
        bool Mates(string child, int childSlot, string parent, int parentSlot)
        {
            var c = Part(child);
            var p = Part(parent);
            return catalog.CanMate(c, c.Slots.Single(s => s.Id == childSlot), p, p.Slots.Single(s => s.Id == parentSlot));
        }

        Assert.True(Mates("FH_stromberg97", 10, "FH_intake_4x97", 300));
        Assert.True(Mates("FH_stromberg97", 10, "FH_intake", 7));
        Assert.False(Mates("FH_carburetor", 10, "FH_intake_2x97", 300));
        Assert.True(Mates("FH_carburetor", 10, "FH_intake", 7));

        Assert.True(Mates("FH_strom_cleaner", 12, "FH_stromberg97", 11));
        Assert.True(Mates("FH_elbow_stack", 12, "FH_stromberg97", 11));
        Assert.False(Mates("FH_oil_bath", 12, "FH_stromberg97", 11));
        Assert.False(Mates("FH_chrome_cleaner", 12, "FH_stromberg97", 11));
        Assert.True(Mates("FH_oil_bath", 12, "FH_carburetor", 11));
        Assert.True(Mates("FH_strom_cleaner", 12, "FH_carburetor", 11));
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

        Assert.DoesNotContain(AttendantCheck.Look(CarWith("mercury-255-race-3spd", "FH_elbow_stack"), catalog), h => h.Key == "station.hint.air_filter");
        Assert.Contains(AttendantCheck.Look(CarWith("mercury-255-edelbrock-3spd", "FH_strom_cleaner"), catalog), h => h.Key == "station.hint.air_filter");
    }

    [Theory]
    [InlineData("FH_L_head_edel", "FH_L_head")]
    [InlineData("FH_intake_2x97", "FH_intake")]
    [InlineData("FH_intake_4x97", "FH_intake_2x97")]
    [InlineData("FH_L_tube_header", "FH_L_manifold")]
    public void A_speed_part_costs_more_than_the_part_it_replaces(string speed, string stock) =>
        Assert.True(PartPricing.NewPrice(Part(speed)) > PartPricing.NewPrice(Part(stock)));
}
