using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Tests;

/// <summary>Step 31 phase 2: the period drums and discs, their shoes and wheel cylinders, their fade in Assetto Corsa</summary>
public class PeriodBrakesTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private const string Drum = "running_gear/brakes_drum/";
    private const string Disc = "running_gear/brakes_disc/";

    /// <summary>A car whose own data asks for the source game's smallest parts: its brake torque, springs and shocks low</summary>
    private static AcCarSpecs Specs(double mass = 1850) => new()
    {
        Traction = "RWD", TotalMass = mass, FrontWeightShare = 0.55,
        Front = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.33, BrakeTorque = 1600, SpringRate = 30000, DampBump = 3000, DampRebound = 4000, CornerLoad = 450, SuspensionType = "DWB" },
        Rear = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.33, BrakeTorque = 900, SpringRate = 25000, DampBump = 3000, DampRebound = 4000, CornerLoad = 400, SuspensionType = "AXLE" }
    };

    private static readonly CarFacts Chevy66 = new("chevrolet", "gm", 1966, "USA");

    [Fact]
    public void An_american_car_of_the_drum_years_leaves_the_factory_on_drums_sized_by_its_weight()
    {
        var (front, rear) = RunningGearFactory.Choose(Catalog, Specs(), Chevy66)!.Value;
        Assert.Equal(Drum + "front_11x2_75", front.Brake.Id);
        Assert.Equal(Drum + "rear_11x2", rear.Brake.Id);
        Assert.Equal(Drum + "shoes_organic", front.Shoes!.Id);
        Assert.Equal(Drum + "wheel_cylinder_stock", front.Cylinder!.Id);

        Assert.Equal(Drum + "front_9_5x2_5", RunningGearFactory.Choose(Catalog, Specs(1300), Chevy66)!.Value.Front.Brake.Id);
        Assert.Equal(Disc + "single_piston", RunningGearFactory.Choose(Catalog, Specs(), Chevy66 with { Year = 1975 })!.Value.Front.Brake.Id);
    }

    [Fact]
    public void The_dealers_options_are_only_cars_json_picks_and_a_car_of_elsewhere_keeps_the_source_games_discs()
    {
        var ford65 = new CarFacts("ford", "ford", 1965, "USA");
        Assert.StartsWith(Drum, RunningGearFactory.Choose(Catalog, Specs(), ford65)!.Value.Front.Brake.Id);
        Assert.Equal(Disc + "kelsey_hayes", RunningGearFactory.Choose(Catalog, Specs(), ford65, new RunningGearPicks { BrakesFront = Disc + "kelsey_hayes" })!.Value.Front.Brake.Id);

        var jaguar = RunningGearFactory.Choose(Catalog, Specs(), new CarFacts("jaguar", "jaguar", 1965, "UK"))!.Value;
        Assert.Equal(RunningGearFactory.Choose(Catalog, Specs())!.Value.Front.Brake.Id, jaguar.Front.Brake.Id);
        Assert.Null(jaguar.Front.Brake.CameIn);
    }

    [Fact]
    public void A_drum_brakes_with_its_shoes_and_its_cylinder_and_its_lining_wears()
    {
        var drum = Catalog.Get(Drum + "front_10x2_5")!;
        var organic = Catalog.Get(Drum + "shoes_organic")!;
        var sintered = Catalog.Get(Drum + "shoes_sintered")!;
        var bigBore = Catalog.Get(Drum + "wheel_cylinder_big_bore")!;
        Assert.Equal(2400, RunningGear.BrakeTorque(drum, organic, null), 0);
        Assert.True(RunningGear.BrakeTorque(drum, sintered, null) < RunningGear.BrakeTorque(drum, organic, null));
        Assert.Equal(RunningGear.BrakeTorque(drum, organic, null) * 1.1, RunningGear.BrakeTorque(drum, organic, bigBore), 3);

        var corner = new CornerParts { Brake = new PartInstance(drum.Id) { Wear = 1 }, Shoes = new PartInstance(organic.Id) { Wear = 0.3 } };
        Assert.Equal(RunningGear.BrakeTorque(drum, organic, null, 0.3), RunningGear.BrakeTorque(Catalog, corner), 3);

        // Every factory drum is under the source game's smallest disc: a disc is an upgrade
        var smallestDisc = Catalog.Parts.Values.Where(p => PartKinds.GroupOf(p) == PartKinds.Brakes && p.CameIn == null && p.Number("force") > 0).Min(p => RunningGear.BrakeTorque(p));
        Assert.All(Catalog.Parts.Values.Where(p => p.Id.StartsWith(Drum, StringComparison.Ordinal) && PartKinds.GroupOf(p) == PartKinds.Brakes),
            d => Assert.True(RunningGear.BrakeTorque(d, organic, null) < smallestDisc, d.Id));
    }

    [Fact]
    public void The_workbench_puts_shoes_a_wheel_cylinder_and_a_drum_of_its_size_on_a_mounted_backing_plate_and_a_hub_on_a_front_drum()
    {
        var car = new Car("car_a") { HasPartsAssigned = true, HasRunningGearAssigned = true, GearVersion = RunningGearFactory.GearVersion };
        var plate = new PartInstance(Drum + "plate_front_10") { ParentSlot = RunningGear.BrakeSlot(0), OwnSlot = 1 };
        car.Parts.Add(plate);

        Assert.Contains(Workbench.FindPlaces(Catalog, car.Parts, new PartInstance(Drum + "shoes_sintered")), p => p.Parent == plate && p.ParentSlot == RunningGear.ShoesSlot);
        Assert.Contains(Workbench.FindPlaces(Catalog, car.Parts, new PartInstance(Drum + "wheel_cylinder_big_bore")), p => p.Parent == plate && p.ParentSlot == RunningGear.WheelCylinderSlot);

        // A drum goes on a plate of its size, never on the corner itself
        var drum = new PartInstance(Drum + "front_10x2_5");
        Assert.Equal([new MountPlace(plate, RunningGear.DrumSlot, 1)], Workbench.FindPlaces(Catalog, car.Parts, drum));
        Assert.Empty(Workbench.FindPlaces(Catalog, car.Parts, new PartInstance(Drum + "front_9x2")));

        // A plate goes on the free corners of its axle only
        Assert.Equal([RunningGear.BrakeSlot(1)], Workbench.FindPlaces(Catalog, car.Parts, new PartInstance(Drum + "plate_front_9")).Select(p => p.ParentSlot));

        Workbench.Mount(car.Parts, new MountPlace(plate, RunningGear.DrumSlot, 1), drum);
        Assert.Contains(Workbench.FindPlaces(Catalog, car.Parts, new PartInstance(Drum + "front_hub")), p => p.Parent == drum && p.ParentSlot == RunningGear.HubSlot);
    }

    [Fact]
    public void A_drum_of_an_older_save_goes_onto_its_backing_plate_with_its_shoes_and_wheel_cylinder()
    {
        var drum = new PartInstance(Drum + "front_10x2_5") { ParentSlot = RunningGear.BrakeSlot(0), OwnSlot = 1, Wear = 0.7 };
        var shoes = new PartInstance(Drum + "shoes_organic") { ParentSlot = RunningGear.ShoesSlot, OwnSlot = 1, Wear = 0.4 };
        var cylinder = new PartInstance(Drum + "wheel_cylinder_stock") { ParentSlot = RunningGear.WheelCylinderSlot, OwnSlot = 1 };
        drum.Children.AddRange([shoes, cylinder]);
        var parts = new List<PartInstance> { drum };

        Assert.Equal(1, RunningGearFactory.PutDrumsOnPlates(Catalog, parts));
        var plate = parts.Single();
        Assert.Equal(Drum + "plate_front_10", plate.DefinitionId);
        Assert.Equal(RunningGear.BrakeSlot(0), plate.ParentSlot);
        Assert.Equal(0.7, plate.Wear);
        Assert.Equal([(RunningGear.ShoesSlot, shoes), (RunningGear.WheelCylinderSlot, cylinder), (RunningGear.DrumSlot, drum)],
            plate.Children.Select(c => (c.ParentSlot, c)).OrderBy(c => c.ParentSlot));
        Assert.Empty(drum.Children);

        var corner = RunningGear.Mounted(parts, Catalog)[0];
        Assert.Same(drum, corner.Brake);
        Assert.Same(shoes, corner.Shoes);

        // Once is enough
        Assert.Equal(0, RunningGearFactory.PutDrumsOnPlates(Catalog, parts));
    }

    // ----- Assetto Corsa -----

    private const string BrakesIni = "[DATA]\nMAX_TORQUE=2600\nFRONT_SHARE=0.62\nHANDBRAKE_TORQUE=300\n";
    private const string AuthorsTemps = BrakesIni + "\n[TEMPS_FRONT]\nTORQUE_K=0.3\nCOOL_TRANSFER=0.01\nCOOL_SPEED_FACTOR=0.02\nPERF_CURVE=(|0=1|1000=1|)\n";

    private static Dictionary<string, string> Export(Action<List<PartInstance>>? change, string brakes, List<string> problems)
    {
        var specs = Specs();
        var parts = RunningGearFactory.Create(Catalog, specs, 1, null, Chevy66);
        change?.Invoke(parts);
        return AcRunningGearData.Generate(Catalog, RunningGear.Mounted(parts), RunningGearFactory.Choose(Catalog, specs, Chevy66)!.Value,
            name => name == "brakes.ini" ? brakes : null, problems);
    }

    [Fact]
    public void A_stock_car_without_brake_temperatures_gets_its_drums_fade_and_one_whose_author_wrote_them_keeps_them()
    {
        var problems = new List<string>();
        var ini = new IniText(Export(null, BrakesIni, problems)["brakes.ini"]);
        Assert.Empty(problems);
        Assert.Equal(0.9, ini.GetNumber("TEMPS_FRONT", "TORQUE_K"));
        Assert.Equal(1.0, ini.GetNumber("TEMPS_REAR", "TORQUE_K"));
        Assert.StartsWith("(|0=0.90|", ini.Get("TEMPS_FRONT", "PERF_CURVE"));
        Assert.Equal(2600, ini.GetNumber("DATA", "MAX_TORQUE"));

        // The author's fade stays on the factory brakes; the rear, which they gave none, gets the drums'
        var authors = new IniText(Export(null, AuthorsTemps, problems)["brakes.ini"]);
        Assert.Equal(0.3, authors.GetNumber("TEMPS_FRONT", "TORQUE_K"));
        Assert.Equal(1.0, authors.GetNumber("TEMPS_REAR", "TORQUE_K"));
    }

    [Fact]
    public void Other_shoes_write_their_curve_a_source_game_disc_leaves_the_fade_as_it_was_and_a_drum_without_shoes_cannot_brake()
    {
        var problems = new List<string>();
        var sintered = new IniText(Export(parts => parts.Where(p => p.ParentSlot == RunningGear.BrakeSlot(0) || p.ParentSlot == RunningGear.BrakeSlot(1))
            .ToList().ForEach(b => b.Children.Single(c => c.ParentSlot == RunningGear.ShoesSlot).DefinitionId = Drum + "shoes_sintered"), AuthorsTemps, problems)["brakes.ini"]);
        Assert.StartsWith("(|0=0.70|", sintered.Get("TEMPS_FRONT", "PERF_CURVE"));
        Assert.True(sintered.GetNumber("DATA", "MAX_TORQUE") < 2600);

        var disc = Catalog.Parts.Values.First(p => PartKinds.GroupOf(p) == PartKinds.Brakes && p.CameIn == null && p.Number("force") > 0);
        var discs = new IniText(Export(parts => parts.Where(p => RunningGear.CornerOf(p.ParentSlot) is 0 or 1 && p.ParentSlot >= 111 && p.ParentSlot < 115)
            .ToList().ForEach(b => { b.DefinitionId = disc.Id; b.Children.Clear(); }), AuthorsTemps, problems)["brakes.ini"]);
        Assert.Equal(0.3, discs.GetNumber("TEMPS_FRONT", "TORQUE_K"));
        Assert.True(discs.GetNumber("DATA", "MAX_TORQUE") > 2600);
        Assert.Empty(problems);

        Export(parts => parts.Single(p => p.ParentSlot == RunningGear.BrakeSlot(2)).Children.RemoveAll(c => c.ParentSlot == RunningGear.ShoesSlot), BrakesIni, problems);
        Assert.Equal(["the drum brake needs its shoes (rear left)"], problems);
    }

    // ----- older saves -----

    [Fact]
    public void An_older_cars_factory_brakes_become_todays_with_their_wear_and_the_players_own_stay()
    {
        var specs = Specs();
        var parts = RunningGearFactory.Create(Catalog, specs, 0.6);
        var (oldFront, _) = RunningGearFactory.Choose(Catalog, specs)!.Value;
        var upgrade = Catalog.Parts.Values.First(p => PartKinds.GroupOf(p) == PartKinds.Brakes && p.CameIn == null && p.Id != oldFront.Brake.Id && p.Number("force") > 0);
        parts.Single(p => p.ParentSlot == RunningGear.BrakeSlot(1)).DefinitionId = upgrade.Id;

        // Three brakes and the two rear coils (leaf springs on a live axle now), and each leaf's U-bolts, hanger and shackle;
        // the player's brake stays
        Assert.Equal(11, RunningGearFactory.BringUpToDate(Catalog, specs, Chevy66, null, parts));
        Assert.All(parts.Where(p => p.ParentSlot == RunningGear.SpringSlot(2) || p.ParentSlot == RunningGear.SpringSlot(3)),
            s => Assert.StartsWith("running_gear/springs_leaf/", s.DefinitionId));
        var left = parts.Single(p => p.ParentSlot == RunningGear.BrakeSlot(0));
        Assert.Equal(Drum + "plate_front_11", left.DefinitionId);
        Assert.Equal(0.6, left.Wear);
        Assert.Equal([RunningGear.ShoesSlot, RunningGear.WheelCylinderSlot, RunningGear.DrumSlot], left.Children.Select(c => c.ParentSlot).Order());
        var drum = left.Children.Single(c => c.ParentSlot == RunningGear.DrumSlot);
        Assert.Equal(Drum + "front_11x2_75", drum.DefinitionId);
        Assert.Equal([RunningGear.HubSlot], drum.Children.Select(c => c.ParentSlot));
        Assert.Equal(upgrade.Id, parts.Single(p => p.ParentSlot == RunningGear.BrakeSlot(1)).DefinitionId);
        var rearLeft = RunningGear.Mounted(parts, Catalog)[2];
        Assert.NotNull(rearLeft.UBolts);
        Assert.NotNull(rearLeft.Hanger);
        Assert.NotNull(rearLeft.Shackle);

        // Brought up once: nothing more to do
        Assert.Equal(0, RunningGearFactory.BringUpToDate(Catalog, specs, Chevy66, null, parts));
    }
}
