using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Tests;

/// <summary>Chassis tuning phase 2: parts that lower the car (a spring's <c>drop</c>, a lowering block on a leaf)</summary>
public class ChassisPartsTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private const string Block = "running_gear/test_leaves/block_2in";

    private const string SuspensionsIni =
        "[FRONT]\nTYPE=DWB\nSPRING_RATE=30000\nROD_LENGTH=0.0700\nPACKER_RANGE=0.130\n\n" +
        "[REAR]\nTYPE=AXLE\nSPRING_RATE=25000\nROD_LENGTH=0.1000\nPACKER_RANGE={0}\n\n[AXLE]\nTORQUE_REACTION=-0.5\n\n[ARB]\nFRONT=8000\nREAR=0\n";

    private static List<PartInstance> Parts(bool blocks)
    {
        var parts = RunningGearFactory.Create(Catalog, TestCars.LiveAxleSpecs(), 1, null, TestCars.Chevy66);
        if (blocks)
        {
            foreach (var spring in parts.Where(p => p.ParentSlot == RunningGear.SpringSlot(2) || p.ParentSlot == RunningGear.SpringSlot(3)))
                spring.Children.Add(new PartInstance(Block) { ParentSlot = 3, OwnSlot = 1 });
        }

        return parts;
    }

    private static string Suspensions(string packerRange) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, SuspensionsIni, packerRange);

    /// <summary>The suspensions.ini the car races on, through the whole export (no engine: only the running gear's files)</summary>
    private static IniText? Export(bool blocks, string packerRange = "0.140", Dictionary<string, double>? setup = null, double[]? sag = null)
    {
        var specs = TestCars.LiveAxleSpecs();
        var parts = Parts(blocks);
        var build = new CarBuild
        {
            RunningGear = RunningGear.Mounted(parts),
            SwayBars = RunningGear.SwayBars(parts),
            FactoryRunningGear = RunningGearFactory.Choose(Catalog, specs, TestCars.Chevy66)!.Value,
            Setup = setup == null ? null : new SetupChoice { Values = setup },
            Damage = sag == null ? null : new StreetCorsa.Models.Race.RaceStartState { RideHeight = sag }
        };
        var result = AcCarBuild.Generate(Catalog, build, name => name == "suspensions.ini" ? Suspensions(packerRange) : null);
        return result.Files.TryGetValue("suspensions.ini", out var text) ? new IniText(text) : null;
    }

    /// <summary>The scenes' stance of the test car on its data's axles (both with a ROD_LENGTH, the travel given)</summary>
    private static double[] Stance(List<PartInstance> parts, (double Front, double Rear) rideHeight = default, double? rearTravel = 0.140,
        PartDefinition? factorySpring = null)
    {
        var specs = TestCars.LiveAxleSpecs();
        specs = specs with
        {
            Front = specs.Front with { HasRodLength = true, PackerRange = 0.130 },
            Rear = specs.Rear with { HasRodLength = true, PackerRange = rearTravel }
        };
        var factory = RunningGearFactory.Choose(Catalog, specs, TestCars.Chevy66)!.Value;
        if (factorySpring != null) factory = (factory.Front, factory.Rear with { Spring = factorySpring });
        var corners = RunningGear.Mounted(parts);
        return CarStance.Of(Catalog, new CarStance.Inputs(CarLooks.Sag(corners), corners, factory, rideHeight, specs)).Select(h => Math.Round(h, 6)).ToArray();
    }

    [Fact]
    public void Lowering_blocks_on_the_rear_leaves_shorten_the_rear_push_rod_by_their_drop()
    {
        Assert.Null(Export(blocks: false));

        var ini = Export(blocks: true)!;
        Assert.Equal(0.05, ini.GetNumber("REAR", "ROD_LENGTH")!.Value, 6);
        Assert.Equal(0.07, ini.GetNumber("FRONT", "ROD_LENGTH")!.Value, 6);
    }

    [Fact]
    public void A_drop_is_held_to_half_the_axles_travel_and_taken_whole_where_the_data_gives_none()
    {
        // 60 mm of travel: 30 mm of it, not the blocks' 50
        Assert.Equal(0.07, Export(blocks: true, packerRange: "0.060")!.GetNumber("REAR", "ROD_LENGTH")!.Value, 6);

        // A figure that is no travel in metres holds nothing back
        Assert.Equal(0.05, Export(blocks: true, packerRange: "13")!.GetNumber("REAR", "ROD_LENGTH")!.Value, 6);
    }

    [Fact]
    public void The_drop_the_setup_and_the_sag_together_are_held_to_the_travel()
    {
        // The held figures written as the parts and the setup make them, past the travel: what the hold has to catch
        var held = AcRunningGearData.HeldToTravel(Suspensions("0.140"), Suspensions("0.140").Replace("ROD_LENGTH=0.1000", "ROD_LENGTH=-0.0400"));
        Assert.Equal(0.03, new IniText(held).GetNumber("REAR", "ROD_LENGTH")!.Value, 6);
        Assert.Null(AcRunningGearData.HeldToTravel(Suspensions("0.140"), Suspensions("0.140").Replace("ROD_LENGTH=0.1000", "ROD_LENGTH=0.0400")));

        // Through the export: no engine stops the setup and the damage, so only the parts' 50 is there, inside 70
        Assert.Equal(0.05, Export(blocks: true, setup: new() { [CarSetup.RideHeightRear] = -40 }, sag: [0, 0, -0.05, -0.05])!.GetNumber("REAR", "ROD_LENGTH")!.Value, 6);
    }

    [Fact]
    public void A_lowered_car_sits_in_the_scenes_as_it_races_but_starts_a_race_with_no_damage_for_it()
    {
        var car = new Car("car_a") { HasPartsAssigned = true, HasRunningGearAssigned = true };
        car.Parts.AddRange(Parts(blocks: true));

        Assert.Equal([0, 0, -0.05, -0.05], Stance(car.Parts));
        Assert.All(CarLooks.Sag(car), h => Assert.Equal(0, h));

        // Held to the travel as the race's data is: 60 mm of it takes 30 mm
        Assert.Equal([0, 0, -0.03, -0.03], Stance(car.Parts, rearTravel: 0.060));

        // The setup's ride height on top, held with the drop (140 mm of travel holds 70)
        Assert.Equal([0.01, 0.01, -0.07, -0.07], Stance(car.Parts, rideHeight: (10, -40)));

        // A broken rear spring (50 mm of sag) on top of the drop: the axle's 75 mm held to 70, the corner leaning about it
        RunningGear.Mounted(car.Parts)[2].Spring!.Tear = 0;
        Assert.Equal(-0.05, CarLooks.Sag(car)[2], 6);
        Assert.Equal([0, 0, -0.095, -0.045], Stance(car.Parts));
    }

    [Fact]
    public void A_block_on_one_side_lowers_its_axle_level_as_the_race_does()
    {
        var parts = Parts(blocks: false);
        parts.First(p => p.ParentSlot == RunningGear.SpringSlot(2)).Children.Add(new PartInstance(Block) { ParentSlot = 3, OwnSlot = 1 });

        Assert.Equal([0, 0, -0.025, -0.025], Stance(parts));
    }

    [Fact]
    public void A_car_that_comes_lowered_from_the_factory_sits_on_its_model()
    {
        // The factory spring already drops what is mounted: the model sits there, nothing more to lower
        Assert.Equal([0, 0, 0, 0], Stance(Parts(blocks: true), factorySpring: Catalog.Get(Block)));
    }

    [Fact]
    public void A_car_without_its_data_shows_its_sag_alone()
    {
        var parts = Parts(blocks: true);
        var corners = RunningGear.Mounted(parts);
        var factory = RunningGearFactory.Choose(Catalog, TestCars.LiveAxleSpecs(), TestCars.Chevy66)!.Value;

        // No ROD_LENGTH in the data (the test specs give none): AC moves nothing; no data read, nothing either
        Assert.All(CarStance.Of(Catalog, new CarStance.Inputs(CarLooks.Sag(corners), corners, factory, (0, -40), TestCars.LiveAxleSpecs())), h => Assert.Equal(0, h));
        Assert.All(CarStance.Of(Catalog, new CarStance.Inputs(CarLooks.Sag(corners), corners, factory, (0, -40), null)), h => Assert.Equal(0, h));
    }

    [Fact]
    public void A_parts_service_without_a_catalog_shows_a_car_as_it_did()
    {
        ICarPartsService parts = new NoParts();
        var car = new Car("car_a");

        Assert.Equal(new double[4], parts.Stance(car));
        Assert.Null(parts.Shown(car));
        Assert.Null(parts.Shown(new UsedCarListing { CarDefinitionId = "car_a" }));
    }

    [Fact]
    public void A_drop_reads_as_millimetres_held_to_what_a_part_can_do()
    {
        Assert.Equal(50, RunningGear.Drop(Catalog.Get(Block)!));
        Assert.Equal(0, RunningGear.Drop(Catalog.Get("running_gear/test_leaves/ladder")!));
        Assert.Equal(50, RunningGear.Drop(Catalog, Parts(blocks: true).First(p => p.ParentSlot == RunningGear.SpringSlot(2))));
    }
}
