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
    public void Lowering_blocks_and_traction_bars_are_for_sale()
    {
        Assert.True(PartGroups.Current.ForSale("lowering_blocks"));
        Assert.True(PartGroups.Current.ForSale("traction_bars"));
        Assert.True(PartGroups.Current.SoldBySpeedShops("lowering_blocks"));
    }

    [Fact]
    public void A_drop_reads_as_millimetres_held_to_what_a_part_can_do()
    {
        Assert.Equal(50, RunningGear.Drop(Catalog.Get(Block)!));
        Assert.Equal(0, RunningGear.Drop(Catalog.Get("running_gear/test_leaves/ladder")!));
        Assert.Equal(50, RunningGear.Drop(Catalog, Parts(blocks: true).First(p => p.ParentSlot == RunningGear.SpringSlot(2))));
    }

    [Fact]
    public void Acs_setup_screen_lowers_the_car_no_further_than_the_race_holds_it()
    {
        // 60 mm of rear travel holds 30 mm: the setup's -50 reaches no lower than 0.07 (700), its +20 is inside
        const string setupIni = "[ROD_LENGTH_LR]\nMIN=900\nMAX=1100\nSTEP=1\n\n[ROD_LENGTH_RR]\nMIN=900\nMAX=1100\nSTEP=1\n";
        var own = Suspensions("0.060");
        var choice = new SetupChoice { Allowed = new Dictionary<string, SetupRange> { [CarSetup.RideHeightRear] = new(-50, 20, 5) } };
        IniText Ranged(SetupChoice setup, string raced, bool held) => new(AcSetupData.Range(setup, Catalog, null,
            n => n == "setup.ini" ? setupIni : n == "suspensions.ini" ? raced : null, held ? n => n == "suspensions.ini" ? own : null : null)["setup.ini"]);

        var setup = Ranged(choice, own, held: true);
        Assert.Equal(700, setup.GetNumber("ROD_LENGTH_LR", "MIN"));
        Assert.Equal(1200, setup.GetNumber("ROD_LENGTH_LR", "MAX"));
        Assert.Equal(500, Ranged(choice, own, held: false).GetNumber("ROD_LENGTH_LR", "MIN"));

        // Set at -40 over 50 mm of blocks the car races unheld at 0.01: the whole range comes to the hold's 0.07
        var lowered = new SetupChoice { Values = new Dictionary<string, double> { [CarSetup.RideHeightRear] = -40 }, Allowed = choice.Allowed };
        setup = Ranged(lowered, own.Replace("ROD_LENGTH=0.1000", "ROD_LENGTH=0.0100"), held: true);
        Assert.Equal(700, setup.GetNumber("ROD_LENGTH_LR", "MIN"));
        Assert.Equal(700, setup.GetNumber("ROD_LENGTH_LR", "MAX"));
    }

    [Fact]
    public void The_axles_of_the_data_without_a_push_rod_show_no_sag_as_the_race_moves_none()
    {
        var corners = RunningGear.Mounted(Parts(blocks: false));
        corners[2].Spring!.Tear = 0;
        var factory = RunningGearFactory.Choose(Catalog, TestCars.LiveAxleSpecs(), TestCars.Chevy66)!.Value;
        Assert.Equal(-0.05, CarLooks.Sag(corners)[2], 6);

        Assert.All(CarStance.Of(Catalog, new CarStance.Inputs(CarLooks.Sag(corners), corners, factory, default, TestCars.LiveAxleSpecs())), h => Assert.Equal(0, h));

        // No data read: the sag alone, as before
        Assert.Equal(-0.05, CarStance.Of(Catalog, new CarStance.Inputs(CarLooks.Sag(corners), corners, factory, default, null))[2], 6);
    }

    [Fact]
    public void The_race_and_the_scenes_take_an_axles_drop_from_one_rule()
    {
        var parts = Parts(blocks: false);
        var factory = RunningGearFactory.Choose(Catalog, TestCars.LiveAxleSpecs(), TestCars.Chevy66)!.Value;
        Assert.Equal(0, CarStance.Drop(Catalog, RunningGear.Mounted(parts).Skip(2), factory.Rear.Spring));

        parts.First(p => p.ParentSlot == RunningGear.SpringSlot(2)).Children.Add(new PartInstance(Block) { ParentSlot = 3, OwnSlot = 1 });
        Assert.Equal(25, CarStance.Drop(Catalog, RunningGear.Mounted(parts).Skip(2), factory.Rear.Spring));
    }

    [Fact]
    public void An_absurd_drop_is_no_figure_and_a_chassis_part_that_does_nothing_is_not_sold()
    {
        Assert.Equal(0, RunningGear.Drop(new PartDefinition { Id = "x", Class = "lowering_block", Specs = { ["drop"] = 2000.0 } }));
        Assert.Equal(-30, RunningGear.Drop(new PartDefinition { Id = "x", Class = "lowering_block", Specs = { ["drop"] = -30.0 } }));

        Assert.True(RunningGear.DoesSomething(Catalog.Get(Block)!));
        Assert.True(RunningGear.DoesSomething(Catalog.Get("running_gear/test_leaves/ladder")!));
        Assert.False(RunningGear.DoesSomething(new PartDefinition { Id = "x", Class = "lowering_block" }));
        Assert.False(RunningGear.DoesSomething(new PartDefinition { Id = "x", Class = "traction_bar" }));
        Assert.False(RunningGear.DoesSomething(new PartDefinition { Id = "x", Class = "traction_bar", Specs = { ["wrap_factor"] = 1.0 } }));
        Assert.True(RunningGear.DoesSomething(new PartDefinition { Id = "x", Class = "oil_pan" }));
    }

    [Fact]
    public void Lowering_blocks_and_traction_bars_fit_a_car_on_leaf_springs()
    {
        var car = new Car("car_a") { HasPartsAssigned = true, HasRunningGearAssigned = true };
        car.Parts.AddRange(Parts(blocks: false));

        var fitting = new NewspaperPartsService(new CatalogParts()).FindFittingParts(car);
        Assert.Contains(Block, fitting);
        Assert.Contains("running_gear/test_leaves/ladder", fitting);
    }

    [Fact]
    public void What_is_on_a_leaf_spring_weighs_on_the_car_and_on_its_axle()
    {
        var parts = Parts(blocks: true);
        var factory = RunningGearFactory.Choose(Catalog, TestCars.LiveAxleSpecs(), TestCars.Chevy66)!.Value;
        const string carIni = "[BASIC]\nTOTALMASS=1500\n";
        var suspensions = Suspensions("0.140").Replace("[REAR]\n", "[REAR]\nHUB_MASS=60\n");
        var files = AcRunningGearData.Generate(Catalog, RunningGear.Mounted(parts), factory,
            n => n == "car.ini" ? carIni : n == "suspensions.ini" ? suspensions : null, [], RunningGear.SwayBars(parts));

        // Two 4 kg blocks
        Assert.Equal(1508, new IniText(files["car.ini"]).GetNumber("BASIC", "TOTALMASS"));
        Assert.Equal(64, new IniText(files["suspensions.ini"]).GetNumber("REAR", "HUB_MASS"));
    }

    [Fact]
    public void How_far_a_car_may_be_lowered_is_a_rule()
    {
        var problems = new List<string>();
        new StreetCorsa.Rules.TuningRules { MostTravelLowered = 2 }.Check("tuning", problems);
        Assert.Contains(problems, p => p.Contains("MostTravelLowered", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0.5, new StreetCorsa.Rules.TuningRules().MostTravelLowered);
    }

    private const string Valve = "running_gear/test_chassis/valve";
    private const string AngleKit = "running_gear/test_chassis/angle_kit";

    [Fact]
    public void The_brake_valve_and_the_angle_kit_go_on_their_own_car_slots_and_no_car_comes_with_them()
    {
        var parts = Parts(blocks: false);
        Assert.DoesNotContain(parts, p => RunningGear.IsChassisSlot(p.ParentSlot));

        Assert.Equal([(null, RunningGear.BrakeValveSlot)], Workbench.FindPlaces(Catalog, parts, new PartInstance(Valve)).Select(p => (p.Parent, p.ParentSlot)));
        Assert.Equal([(null, RunningGear.SteeringSlot)], Workbench.FindPlaces(Catalog, parts, new PartInstance(AngleKit)).Select(p => (p.Parent, p.ParentSlot)));

        // One on each: the slot is taken
        parts.Add(new PartInstance(Valve) { ParentSlot = RunningGear.BrakeValveSlot, OwnSlot = 1 });
        Assert.Empty(Workbench.FindPlaces(Catalog, parts, new PartInstance(Valve)));

        // Nothing on the engine on the stand
        Assert.Empty(Workbench.FindPlacesOnStand(Catalog, new PartInstance("x"), new PartInstance(AngleKit)));
    }

    [Fact]
    public void The_brake_valve_and_the_angle_kit_are_sold_to_a_car_while_one_that_adjusts_nothing_is_not()
    {
        var car = new Car("car_a") { HasPartsAssigned = true, HasRunningGearAssigned = true };
        car.Parts.AddRange(Parts(blocks: false));

        var fitting = new NewspaperPartsService(new CatalogParts()).FindFittingParts(car);
        Assert.Contains(Valve, fitting);
        Assert.Contains(AngleKit, fitting);

        Assert.True(RunningGear.DoesSomething(Catalog.Get(Valve)!));
        Assert.True(RunningGear.DoesSomething(Catalog.Get(AngleKit)!));
        Assert.False(RunningGear.DoesSomething(new PartDefinition { Id = "x", Class = "proportioning_valve" }));
        Assert.False(RunningGear.DoesSomething(new PartDefinition
        {
            Id = "x", Class = "angle_kit", Specs = { ["adjust"] = Newtonsoft.Json.Linq.JObject.Parse("""{ "steer_lock": { "min": 0, "max": 0 } }""") }
        }));
    }

    [Fact]
    public void An_angle_kit_puts_its_lock_on_at_once_and_a_valve_lets_the_bias_move()
    {
        var parts = Parts(blocks: false);
        parts.Add(new PartInstance(Valve) { ParentSlot = RunningGear.BrakeValveSlot, OwnSlot = 1 });
        parts.Add(new PartInstance(AngleKit) { ParentSlot = RunningGear.SteeringSlot, OwnSlot = 1 });

        var data = new Dictionary<string, string>
        {
            ["car.ini"] = "[CONTROLS]\nSTEER_LOCK=450\nSTEER_RATIO=15\n",
            ["brakes.ini"] = "[DATA]\nMAX_TORQUE=1000\nFRONT_SHARE=0.60\n"
        };
        string? Read(string name) => data.GetValueOrDefault(name);

        var setup = CarSetup.For(parts, new Dictionary<string, double> { [CarSetup.BrakeBias] = 0.04 }, Catalog, null);
        var files = AcSetupData.Apply(AcSetupData.ForData(setup, Read), Read);

        Assert.Equal(570, new IniText(files["car.ini"]).GetNumber("CONTROLS", "STEER_LOCK"));
        Assert.Equal(0.64, new IniText(files["brakes.ini"]).GetNumber("DATA", "FRONT_SHARE")!.Value, 6);

        // Off the car, the steering and the bias are the factory's again
        parts.RemoveAll(p => RunningGear.IsChassisSlot(p.ParentSlot));
        Assert.Empty(AcSetupData.Apply(CarSetup.For(parts, new Dictionary<string, double> { [CarSetup.BrakeBias] = 0.04 }, Catalog, null), Read));
    }
}
