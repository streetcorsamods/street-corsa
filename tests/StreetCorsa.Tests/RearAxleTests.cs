using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Tests;

/// <summary>Step 31's rear axle: the factory guess, the tree, the workbench and the Assetto Corsa data</summary>
public class RearAxleTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private const string Gm = "running_gear/axles_gm/";
    private const string Ford = "running_gear/axles_ford/";
    private const string Dana = "running_gear/axles_dana/";

    private static AcCarSpecs Specs(double torque = 390, double final = 3.55, double power = 0.05, double coast = 0.05, string traction = "RWD",
        string rear = "AXLE") => new()
    {
        Traction = traction, PeakTorque = torque, FinalRatio = final, Differential = (power, coast, 0),
        Rear = new AcAxleSpecs { SuspensionType = rear }
    };

    private static readonly CarFacts Chevy69 = new("chevrolet", "gm", 1969);

    [Fact]
    public void A_chevy_gets_a_ten_bolt_open_or_posi_as_its_data_locks_and_its_own_ratio()
    {
        var open = RunningGearGuess.ChooseAxle(Catalog, Specs(), Chevy69, null)!;
        Assert.Equal(Gm + "gm_10bolt", open.Housing.Id);
        Assert.Equal(Gm + "gm_10bolt_open_high", open.Carrier.Id);
        Assert.Equal(3.55, RearAxle.Ratio(open.Gears));
        Assert.Equal(Gm + "gm_10bolt_shaft_28", open.Shaft.Id);

        var posi = RunningGearGuess.ChooseAxle(Catalog, Specs(power: 0.45, coast: 0.3, final: 3.08), Chevy69, null)!;
        Assert.Equal(Gm + "gm_10bolt_posi_low", posi.Carrier.Id);
        Assert.Equal(3.08, RearAxle.Ratio(posi.Gears));
    }

    [Fact]
    public void A_big_block_chevy_gets_a_twelve_bolt_and_a_car_no_axle_names_a_dana_44()
    {
        Assert.Equal(Gm + "gm_12bolt", RunningGearGuess.ChooseAxle(Catalog, Specs(torque: 520), Chevy69, null)!.Housing.Id);
        Assert.Equal(Gm + "gm_10bolt", RunningGearGuess.ChooseAxle(Catalog, Specs(torque: 520), new CarFacts("chevrolet", "gm", 1962), null)!.Housing.Id);
        Assert.Equal(Dana + "dana_44", RunningGearGuess.ChooseAxle(Catalog, Specs(), new CarFacts("studebaker", null, 1955), null)!.Housing.Id);
        Assert.Equal(Ford + "ford_9", RunningGearGuess.ChooseAxle(Catalog, Specs(torque: 450), new CarFacts("ford", "ford", 1966), null)!.Housing.Id);
    }

    [Fact]
    public void A_car_that_does_not_drive_its_rear_wheels_has_no_axle()
    {
        Assert.Null(RunningGearGuess.ChooseAxle(Catalog, Specs(traction: "FWD"), Chevy69, null));
    }

    [Fact]
    public void Cars_json_picks_win_and_what_is_not_picked_is_guessed()
    {
        var picks = new RunningGearPicks { Axle = Ford + "ford_9", Ratio = 4.11 };
        var axle = RunningGearGuess.ChooseAxle(Catalog, Specs(power: 0.45, coast: 0.3), Chevy69, picks)!;
        Assert.Equal(Ford + "ford_9", axle.Housing.Id);
        Assert.Equal(4.11, RearAxle.Ratio(axle.Gears));
        Assert.Equal(Ford + "ford_9_posi", axle.Carrier.Id);

        // A pick of the wrong kind, or of nothing, is no pick
        var wrong = RunningGearGuess.ChooseAxle(Catalog, Specs(), Chevy69, new RunningGearPicks { Axle = Gm + "gm_10bolt_open_high", Diff = "nothing" })!;
        Assert.Equal(Gm + "gm_10bolt", wrong.Housing.Id);
    }

    [Fact]
    public void A_ten_bolt_carrier_takes_the_gears_of_its_series_only()
    {
        var low = Catalog.Get(Gm + "gm_10bolt_open_low")!;
        var gearsSlot = low.Slots.Single(s => s.Id == RearAxle.GearsSlot);
        var fits = Catalog.FindMountable(low, gearsSlot).Select(m => RearAxle.Ratio(m.Part)).Order().ToList();
        Assert.Equal([2.56, 2.73, 3.08], fits);
    }

    [Fact]
    public void The_factory_axle_is_a_tree_on_the_axle_slot_and_mates_as_the_catalog_says()
    {
        var choice = RunningGearGuess.ChooseAxle(Catalog, Specs(), Chevy69, null)!;
        var axle = RunningGearFactory.CreateAxle(choice, 1);

        Assert.Equal(RearAxle.CarSlot, axle.ParentSlot);
        var mounted = RearAxle.Mounted([axle]);
        Assert.Equal(choice.Carrier.Id, mounted.Carrier!.DefinitionId);
        Assert.Equal(choice.Gears.Id, mounted.Gears!.DefinitionId);
        Assert.Equal(choice.Shaft.Id, mounted.LeftShaft!.DefinitionId);
        Assert.Equal(choice.Shaft.Id, mounted.RightShaft!.DefinitionId);
        Assert.Equal(5, mounted.All.Count());

        // Every joint of it is one the catalog mates
        foreach (var parent in axle.SelfAndDescendants())
        {
            var parentDefinition = Catalog.Get(parent.DefinitionId)!;
            foreach (var child in parent.Children)
            {
                var childDefinition = Catalog.Get(child.DefinitionId)!;
                Assert.True(Catalog.CanMate(childDefinition, childDefinition.Slots.Single(s => s.Id == child.OwnSlot),
                    parentDefinition, parentDefinition.Slots.Single(s => s.Id == child.ParentSlot)), $"{child.DefinitionId} on {parent.DefinitionId}");
            }
        }
    }

    [Fact]
    public void The_workbench_puts_a_housing_on_a_free_axle_slot_and_a_carrier_into_a_housing_it_fits()
    {
        var car = new Car("car_a") { HasPartsAssigned = true, HasRunningGearAssigned = true, GearVersion = RunningGearFactory.GearVersion };
        var housing = new PartInstance(Gm + "gm_10bolt");
        Assert.Equal([new MountPlace(null, RearAxle.CarSlot, 1)], Workbench.FindPlaces(Catalog, car.Parts, housing));

        Workbench.Mount(car.Parts, new MountPlace(null, RearAxle.CarSlot, 1), housing);
        Assert.Empty(Workbench.FindPlaces(Catalog, car.Parts, new PartInstance(Gm + "gm_12bolt")));

        var carrier = Workbench.FindPlaces(Catalog, car.Parts, new PartInstance(Gm + "gm_10bolt_posi_high"));
        Assert.Contains(carrier, p => p.Parent == housing && p.ParentSlot == RearAxle.CarrierSlot);
        Assert.Empty(Workbench.FindPlaces(Catalog, car.Parts, new PartInstance(Ford + "ford_9_posi")));
        Assert.Equal(2, Workbench.FindPlaces(Catalog, car.Parts, new PartInstance(Gm + "gm_10bolt_shaft_28")).Count);
    }

    // ----- the catalog editor -----

    [Fact]
    public void The_editor_writes_only_what_is_picked_and_cars_json_reads_it_back()
    {
        var picks = new RunningGearPicks { Axle = Dana + "dana_44", Ratio = 3.7 };
        var text = StreetCorsa.Services.Catalog.BaseCarsFile.With("{ \"cars\": { \"vette\": { \"common\": 0.4 } } }",
            new Dictionary<string, StreetCorsa.Services.Catalog.CarFields> { ["vette"] = new(null, null, 0.4f, picks) });
        Assert.Contains("\"running_gear\": {\"axle\":\"running_gear/axles_dana/dana_44\",\"ratio\":3.7}", text);

        var read = CarBook.Read(StreetCorsa.Mods.ModMerge.Parse(text)!, []).Car("vette")!.RunningGear!;
        Assert.Equal(Dana + "dana_44", read.Axle);
        Assert.Equal(3.7, read.Ratio);

        var cleared = StreetCorsa.Services.Catalog.BaseCarsFile.With(text,
            new Dictionary<string, StreetCorsa.Services.Catalog.CarFields> { ["vette"] = new(null, null, 0.4f, new RunningGearPicks()) });
        Assert.DoesNotContain("running_gear", cleared);
    }

    [Fact]
    public void The_editor_offers_the_guess_first_and_lets_a_carrier_go_that_the_new_housing_does_not_take()
    {
        var picks = new StreetCorsa.Screens.CarCatalogEditor.AxlePicks(Catalog, p => RunningGearGuess.ChooseAxle(Catalog, Specs(), Chevy69, p), null);
        Assert.Equal("Guessed: Chevy 10-bolt 8.2-inch rear axle housing", picks.Housing!.Label);
        Assert.Null(picks.Picks);
        Assert.All(picks.CarrierOptions.Skip(1), o => Assert.Contains("10-bolt 8.2", o.Label));

        var changed = 0;
        picks.Changed += () => changed++;
        picks.Carrier = picks.CarrierOptions.First(o => o.Id == Gm + "gm_10bolt_posi_high");
        picks.Housing = picks.HousingOptions.First(o => o.Id == Ford + "ford_9");
        Assert.Equal(2, changed);
        Assert.Equal(Ford + "ford_9", picks.Picks!.Axle);
        Assert.Null(picks.Picks.Diff);
        Assert.Contains(picks.RatioOptions, o => o.Id == "5.14");

        // Something that is not this row's is no pick
        picks.Housing = new StreetCorsa.Screens.CarCatalogEditor.PickOption("nothing", "nothing");
        Assert.Equal(Ford + "ford_9", picks.Picks!.Axle);
    }

    // ----- working on it -----

    [Fact]
    public void Gears_are_set_up_at_home_with_a_rougher_roll_or_by_the_machinist_and_a_posi_is_rebuilt_off_the_car()
    {
        var gears = new PartInstance(Gm + "gm_10bolt_gears_373") { Wear = 0.5 };
        var setup = Rebuilding.Offers(Catalog, gears, loose: false).Single(o => o.Job.Id == "gear_setup");
        Assert.Null(setup.WhyNot);
        Assert.True(setup.Job.Roll > 0.08);
        Assert.Contains(Machining.Offers(Catalog, gears), o => o.Job.Id == "gear_setup_shop");

        var posi = new PartInstance(Gm + "gm_10bolt_posi_high") { Wear = 0.4 };
        Assert.NotNull(Rebuilding.Offers(Catalog, posi, loose: false).Single(o => o.Job.Id == "posi_rebuild").WhyNot);
        Assert.Null(Rebuilding.Offers(Catalog, posi, loose: true).Single(o => o.Job.Id == "posi_rebuild").WhyNot);
        Assert.Contains(Machining.Offers(Catalog, new PartInstance(Gm + "gm_10bolt") { Tear = 0.4 }), o => o.Job.Id == "housing_true");
    }

    // ----- breaking it -----

    private static Car CarWithAxle(string shaft = Gm + "gm_10bolt_shaft_28")
    {
        var car = new Car("car_a") { HasPartsAssigned = true, HasRunningGearAssigned = true, GearVersion = RunningGearFactory.GearVersion };
        var choice = RunningGearGuess.ChooseAxle(Catalog, Specs(), Chevy69, null)!;
        car.Parts.Add(RunningGearFactory.CreateAxle(choice with { Shaft = Catalog.Get(shaft)! }, 1));
        return car;
    }

    [Fact]
    public void The_race_mode_is_told_what_the_weakest_part_holds_and_a_broken_axle_wrecks_that_part()
    {
        var car = CarWithAxle();
        var weakest = RearAxle.Weakest(Catalog, RearAxle.Mounted(car.Parts))!.Value;
        Assert.Equal(Catalog.Get(Gm + "gm_10bolt_open_high")!.Id, weakest.Part.DefinitionId);
        Assert.Contains(("CAR_0_AXLE", weakest.Strength.ToString("0", System.Globalization.CultureInfo.InvariantCulture)),
            new StreetCorsa.Models.Race.RaceStartState { AxleStrength = weakest.Strength }.IniKeys(0));
        Assert.DoesNotContain(new StreetCorsa.Models.Race.RaceStartState().IniKeys(0), k => k.Key == "CAR_0_AXLE");

        var groupOf = CarCondition.Groups(Catalog);
        var report = CarCondition.ApplyRace(car, new StreetCorsa.Models.Race.RaceCarCondition { AxleBroken = true }, 0.4, groupOf, catalog: Catalog);
        Assert.Equal(0, weakest.Part.Tear);
        Assert.Contains(report, line => line.Text.Contains(Catalog.Get(weakest.Part.DefinitionId)!.DisplayName!, StringComparison.Ordinal));
        Assert.Contains(CarCondition.WhyCannotRace(car, groupOf, counting: false), p => p == PartWords.Say("condition.problem.axle"));

        // The garage rebuilds it
        var job = RepairShop.Jobs(car, Catalog).Single(j => j.Id == RepairShop.Axle);
        Assert.True(job.Cost > 0);
        job.Apply();
        Assert.DoesNotContain(CarCondition.WhyCannotRace(car, groupOf, counting: false), p => p == PartWords.Say("condition.problem.axle"));
    }

    [Fact]
    public void A_race_that_did_not_break_the_axle_only_wears_it()
    {
        var car = CarWithAxle();
        CarCondition.ApplyRace(car, new StreetCorsa.Models.Race.RaceCarCondition { AxleBroken = false, AxlePeakNm = 5000 }, 400, CarCondition.Groups(Catalog), catalog: Catalog);
        Assert.All(RearAxle.Mounted(car.Parts).All, p => Assert.Equal(1, p.Tear));
        Assert.All(RearAxle.Mounted(car.Parts).All, p => Assert.InRange(p.Wear, 0.9, 0.9999));
    }

    // ----- Assetto Corsa -----

    private const string Drivetrain = "[GEARS]\nCOUNT=4\nGEAR_1=2.52\nFINAL=3.31\n\n[DIFFERENTIAL]\nPOWER=0.05\nCOAST=0.05\nPRELOAD=0\n";
    private const string Suspensions = "[REAR]\nTYPE=AXLE\nHUB_MASS=85\n";
    private const string CarIni = "[BASIC]\nTOTALMASS=1500\n";
    private const string Engine = "[HEADER]\nPOWER_CURVE=power.lut\n";
    private const string Lut = "1000|300\n3000|400\n";

    private static Func<string, string?> Files(string drivetrain = Drivetrain) => name => name switch
    {
        "drivetrain.ini" => drivetrain,
        "suspensions.ini" => Suspensions,
        "car.ini" => CarIni,
        "engine.ini" => Engine,
        "power.lut" => Lut,
        _ => null
    };

    private static (RearAxleChoice Factory, List<PartInstance> Car) Factory(double power = 0.05)
    {
        var factory = RunningGearGuess.ChooseAxle(Catalog, Specs(final: 3.31, power: power, coast: power), Chevy69, null)!;
        return (factory, [RunningGearFactory.CreateAxle(factory, 1)]);
    }

    [Fact]
    public void On_its_factory_axle_a_car_keeps_its_own_final_drive_and_differential_over_the_gearbox()
    {
        var (factory, car) = Factory();
        var problems = new List<string>();

        // The engine's gearbox wrote its own figures; the car's are put back
        var written = "[GEARS]\nCOUNT=4\nGEAR_1=2.52\nFINAL=3.73\n\n[DIFFERENTIAL]\nPOWER=0.40\nCOAST=0.30\n";
        var files = AcAxleData.Generate(Catalog, RearAxle.Mounted(car), factory, Files(written), Files(), problems);

        Assert.Empty(problems);
        var drivetrain = new IniText(files["drivetrain.ini"]);
        Assert.Equal("3.31", drivetrain.Get("GEARS", "FINAL"));
        Assert.Equal("0.05", drivetrain.Get("DIFFERENTIAL", "POWER"));
        Assert.Equal("0", drivetrain.Get("DIFFERENTIAL", "PRELOAD"));
        Assert.False(files.ContainsKey("car.ini"));
        Assert.False(files.ContainsKey("power.lut"));

        // On the car's own files the factory axle changes nothing at all
        Assert.Empty(AcAxleData.Generate(Catalog, RearAxle.Mounted(car), factory, Files(), Files(), problems));
    }

    [Fact]
    public void Other_gears_write_their_ratio_and_another_carrier_its_lock()
    {
        var (factory, car) = Factory();
        var axle = RearAxle.Mounted(car);
        axle.Carrier!.DefinitionId = Gm + "gm_10bolt_posi_high";
        axle.Gears!.DefinitionId = Gm + "gm_10bolt_gears_411";

        var problems = new List<string>();
        var drivetrain = new IniText(AcAxleData.Generate(Catalog, axle, factory, Files(), Files(), problems)["drivetrain.ini"]);

        Assert.Empty(problems);
        Assert.Equal(4.11, drivetrain.GetNumber("GEARS", "FINAL"));
        Assert.Equal(0.25, drivetrain.GetNumber("DIFFERENTIAL", "POWER"));
        Assert.Equal(0.15, drivetrain.GetNumber("DIFFERENTIAL", "COAST"));
        Assert.Equal(50, drivetrain.GetNumber("DIFFERENTIAL", "PRELOAD"));

        // A worn posi slips: its lock and preload fall off with its clutches; a spool's do not
        axle.Carrier.Wear = 0;
        var worn = new IniText(AcAxleData.Generate(Catalog, axle, factory, Files(), Files(), problems)["drivetrain.ini"]);
        Assert.True(worn.GetNumber("DIFFERENTIAL", "PRELOAD") < 30);

        axle.Carrier.DefinitionId = "running_gear/axle_speed/gm_10bolt_spool_high";
        var spool = new IniText(AcAxleData.Generate(Catalog, axle, factory, Files(), Files(), problems)["drivetrain.ini"]);
        Assert.Equal(1, spool.GetNumber("DIFFERENTIAL", "POWER"));
        Assert.Equal(1, spool.GetNumber("DIFFERENTIAL", "COAST"));
    }

    [Fact]
    public void A_heavier_axle_weighs_on_the_car_and_its_live_axle_and_a_nine_inch_takes_its_drag()
    {
        var (factory, _) = Factory();
        var nine = RunningGearGuess.ChooseAxle(Catalog, Specs(final: 3.31), Chevy69, new RunningGearPicks { Axle = Ford + "ford_9" })!;
        var problems = new List<string>();
        var files = AcAxleData.Generate(Catalog, RearAxle.Mounted([RunningGearFactory.CreateAxle(nine, 1)]), factory, Files(), Files(), problems);

        Assert.Empty(problems);
        var heavier = nine.Housing.Mass + nine.Carrier.Mass + nine.Gears.Mass + 2 * nine.Shaft.Mass
                      - (factory.Housing.Mass + factory.Carrier.Mass + factory.Gears.Mass + 2 * factory.Shaft.Mass);
        Assert.True(heavier > 0);
        Assert.Equal(85 + heavier, new IniText(files["suspensions.ini"]).GetNumber("REAR", "HUB_MASS")!.Value, 1);
        Assert.Equal(1500 + heavier, new IniText(files["car.ini"]).GetNumber("BASIC", "TOTALMASS")!.Value, 1);

        var drag = (1 - RearAxle.Drag(nine.Housing)) / (1 - RearAxle.Drag(factory.Housing));
        Assert.Contains("1000|" + (300 * drag).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), files["power.lut"]);
    }

    [Fact]
    public void A_car_with_no_axle_or_an_axle_short_of_a_part_cannot_drive()
    {
        var (factory, car) = Factory();
        var problems = new List<string>();
        AcAxleData.Generate(Catalog, RearAxleParts.None, factory, Files(), Files(), problems);
        Assert.Equal(["no rear axle"], problems);

        problems.Clear();
        var axle = car[0];
        axle.Children.RemoveAll(c => c.ParentSlot == RearAxle.LeftShaftSlot);
        axle.Children.Single(c => c.ParentSlot == RearAxle.CarrierSlot).Children.Clear();
        AcAxleData.Generate(Catalog, RearAxle.Mounted(car), factory, Files(), Files(), problems);
        Assert.Equal(["the rear axle needs its left axle shaft", "the differential needs its ring and pinion"], problems);
    }
}
