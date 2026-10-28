using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Rules;

namespace StreetCorsa.Tests;

/// <summary>
/// Chassis tuning phase 1: what a car may adjust (the rules' <c>tuning</c>, the parts' <c>adjust</c>), its setup into the
/// data, and setup.ini ranged around what the car races on (AC holds the data to setup.ini's ranges)
/// </summary>
public sealed class CarSetupTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // The 1969 Nova's own setup.ini items, cut down: its rear toe held to one click (-25..-24), its camber in tenths
    private const string Setup = """
        [DISPLAY_METHOD]
        SHOW_CLICKS=1

        [PRESSURE_LF]
        MIN=20
        MAX=35
        STEP=1
        [PRESSURE_LR]
        MIN=20
        MAX=35
        STEP=1
        [CAMBER_LF]
        MIN=-3.00
        MAX=0.30
        STEP=1
        [TOE_OUT_LF]
        MIN=-200
        MAX=200
        STEP=2
        [TOE_OUT_LR]
        MIN=-25

        MAX=-24
        STEP=5

        [ROD_LENGTH_LF]
        MIN=900
        MAX=1100
        STEP=10
        [DAMP_BUMP_LF]
        MIN=1000
        MAX=3000
        STEP=100
        [FRONT_BIAS]
        MIN=50
        MAX=80
        STEP=1
        [DIFF_POWER]
        MIN=10
        MAX=30
        STEP=5
        """;

    private const string Suspensions = """
        [FRONT]
        ROD_LENGTH=0.100
        TOE_OUT=-0.000100
        STATIC_CAMBER=-0.50
        DAMP_BUMP=2000
        [REAR]
        ROD_LENGTH=0.100
        TOE_OUT=-0.000100
        STATIC_CAMBER=-0.0
        """;

    private const string Tyres = """
        [FRONT]
        PRESSURE_STATIC=28
        [REAR]
        PRESSURE_STATIC=28
        [THERMAL_FRONT]
        PRESSURE_STATIC=99
        """;

    private const string Brakes = """
        [DATA]
        MAX_TORQUE=1000
        FRONT_SHARE=0.71
        """;

    private const string Drivetrain = """
        [TRACTION]
        TYPE=RWD
        [GEARS]
        COUNT=3
        GEAR_R=-2.500
        GEAR_1=2.500
        GEAR_2=1.500
        GEAR_3=1.000
        FINAL=3.550
        [DIFFERENTIAL]
        POWER=0.5
        COAST=0.2
        [CLUTCH]
        MAX_TORQUE=300
        """;

    private const string Engine = """
        [HEADER]
        VERSION=1
        POWER_CURVE=power.lut
        [ENGINE_DATA]
        INERTIA=0.120
        LIMITER=6500
        MINIMUM=900
        """;

    private Func<string, string?> CarData()
    {
        _temp.File("data/setup.ini", Setup);
        _temp.File("data/suspensions.ini", Suspensions);
        _temp.File("data/tyres.ini", Tyres);
        _temp.File("data/brakes.ini", Brakes);
        _temp.File("data/drivetrain.ini", Drivetrain);
        _temp.File("data/engine.ini", Engine);
        _temp.File("data/car.ini", "[CONTROLS]\nSTEER_LOCK=450\n");
        return AcCarDataReader.ForFolder(_temp.Combine("data"));
    }

    private static SetupChoice Choice(Dictionary<string, double>? values = null, IEnumerable<PartDefinition>? parts = null)
    {
        var allowed = CarSetup.Allowed(new TuningRules().Ranges(), parts ?? []);
        return new SetupChoice { Values = CarSetup.Values(values, allowed), Allowed = allowed };
    }

    private static EngineReport Report()
    {
        var curve = new List<(double Rpm, double Torque)>();
        for (var rpm = 1000.0; rpm <= 6000; rpm += 500) curve.Add((rpm, 400 - Math.Abs(rpm - 3500) * 0.05));
        return new EngineReport { Dyno = new DynoResult(0.0057, 9.5, curve), IdleRpm = 800, LimiterRpm = 5800, Inertia = 8, Mass = 250 };
    }

    [Fact]
    public void A_car_with_no_setup_changes_no_data_and_its_ranges_go_round_its_own_figures()
    {
        var data = CarData();
        var choice = Choice();

        Assert.Empty(AcSetupData.Apply(choice, data));
        var setup = new IniText(AcSetupData.Range(choice, PartsCatalog.Empty(_temp.Path), null, data)["setup.ini"]);

        // Camber -0.5 with the rules' half a degree either way, in tenths
        Assert.Equal(-1.0, setup.GetNumber("CAMBER_LF", "MIN")!.Value, 6);
        Assert.Equal(0.0, setup.GetNumber("CAMBER_LF", "MAX")!.Value, 6);
        // 28 psi with 0.3 bar (4.35 psi) either way, in whole psi
        Assert.Equal(24, setup.GetNumber("PRESSURE_LF", "MIN"));
        Assert.Equal(32, setup.GetNumber("PRESSURE_LF", "MAX"));
        // Toe -10 (1/100000 m) with a mm of rod either way, on its step of 2 and the rear's of 5
        Assert.Equal(-110, setup.GetNumber("TOE_OUT_LF", "MIN"));
        Assert.Equal(90, setup.GetNumber("TOE_OUT_LF", "MAX"));
        Assert.Equal(-110, setup.GetNumber("TOE_OUT_LR", "MIN"));
        Assert.Equal(90, setup.GetNumber("TOE_OUT_LR", "MAX"));
        // What nothing lets move stays where the parts have it: the ride height, the bias, the shocks without a part for them
        Assert.Equal(1000, setup.GetNumber("ROD_LENGTH_LF", "MIN"));
        Assert.Equal(1000, setup.GetNumber("ROD_LENGTH_LF", "MAX"));
        Assert.Equal(71, setup.GetNumber("FRONT_BIAS", "MIN"));
        Assert.Equal(71, setup.GetNumber("FRONT_BIAS", "MAX"));
        // An item the game does not set is only widened to take its figure in (the diff's 50%)
        Assert.Equal(10, setup.GetNumber("DIFF_POWER", "MIN"));
        Assert.Equal(50, setup.GetNumber("DIFF_POWER", "MAX"));
        // Without the running gear, the shocks' range only takes their figure in, as it does already
        Assert.Equal(1000, setup.GetNumber("DAMP_BUMP_LF", "MIN"));
        Assert.Equal(3000, setup.GetNumber("DAMP_BUMP_LF", "MAX"));
    }

    [Fact]
    public void The_setup_goes_into_the_data_and_the_range_stays_what_the_rules_allow()
    {
        var data = CarData();
        var choice = Choice(new() { [CarSetup.CamberFront] = -0.3, [CarSetup.PressureRear] = 0.2, [CarSetup.ToeFront] = 0.5 });

        var files = AcSetupData.Apply(choice, data);
        string? Read(string name) => files.GetValueOrDefault(name) ?? data(name);
        var suspensions = new IniText(files["suspensions.ini"]);
        var tyres = new IniText(files["tyres.ini"]);

        Assert.Equal(-0.8, suspensions.GetNumber("FRONT", "STATIC_CAMBER")!.Value, 6);
        Assert.Equal(0.0004, suspensions.GetNumber("FRONT", "TOE_OUT")!.Value, 7);
        Assert.Equal(28, tyres.GetNumber("FRONT", "PRESSURE_STATIC"));
        Assert.Equal(28 + 0.2 * 14.5038, tyres.GetNumber("REAR", "PRESSURE_STATIC")!.Value, 1);

        // The same half a degree either way of the car's own -0.5: the setup moves the figure inside the range, not the range
        var setup = new IniText(AcSetupData.Range(choice, PartsCatalog.Empty(_temp.Path), null, Read)["setup.ini"]);
        Assert.Equal(-1.0, setup.GetNumber("CAMBER_LF", "MIN")!.Value, 6);
        Assert.Equal(0.0, setup.GetNumber("CAMBER_LF", "MAX")!.Value, 6);
        Assert.Equal(-110, setup.GetNumber("TOE_OUT_LF", "MIN"));
        Assert.Equal(90, setup.GetNumber("TOE_OUT_LF", "MAX"));
    }

    [Fact]
    public void A_bent_rear_corner_is_no_longer_held_to_the_authors_one_click_of_toe()
    {
        var data = CarData();
        var build = new CarBuild
        {
            Engine = Report(),
            Damage = new RaceStartState { SuspensionBend = [0, 0, 0.5, 0.5] },
            Setup = Choice()
        };

        var result = AcCarBuild.Generate(PartsCatalog.Empty(_temp.Path), build, data);

        Assert.True(result.CanDrive);
        var toe = new IniText(result.Files["suspensions.ini"]).GetNumber("REAR", "TOE_OUT")!.Value;
        var setup = new IniText(result.Files["setup.ini"]);
        var clicks = toe * 100_000;
        Assert.True(clicks > 1000, $"toe {clicks}");
        Assert.InRange(clicks, setup.GetNumber("TOE_OUT_LR", "MIN")!.Value, setup.GetNumber("TOE_OUT_LR", "MAX")!.Value);
        // The range moves with the bend: straightening it in AC's screen goes no further than the rules' mm
        Assert.Equal(clicks - 100, setup.GetNumber("TOE_OUT_LR", "MIN")!.Value, 0);
    }

    [Fact]
    public void A_part_that_holds_the_ride_height_away_from_nothing_sets_it_with_no_setup_saved()
    {
        _temp.Parts("parts/gear/plates", """
            [ { "id": "gear/plates/rear", "specs": { "adjust": { "ride_height_rear": { "min": -50, "max": -20, "step": 5 } } } } ]
            """);
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        var parts = new List<PartInstance> { new("gear/plates/rear") };

        // On sale (no setup) as once bought: the nearest the plates allow, as the export's setup has it
        Assert.Equal((0.0, -20.0), CarSetup.RideHeights(parts, null, catalog, new TuningRules().Ranges()));
        Assert.Equal(-20, CarSetup.For(parts, null, catalog, new TuningRules().Ranges()).Values[CarSetup.RideHeightRear]);
        Assert.Equal((0.0, -35.0), CarSetup.RideHeights(parts, new Dictionary<string, double> { [CarSetup.RideHeightRear] = -35 }, catalog, null));
    }

    [Fact]
    public void A_part_opens_an_item_and_takes_it_back_when_it_comes_off()
    {
        var plates = new PartDefinition
        {
            Id = "gear/test/plates",
            Specs = { ["adjust"] = Newtonsoft.Json.Linq.JObject.Parse("""{ "ride_height_front": { "min": -50, "max": 0, "step": 5 }, "camber_front": { "min": -3, "max": 1, "step": 0.1 } }""") }
        };
        var saved = new Dictionary<string, double> { [CarSetup.RideHeightFront] = -20, [CarSetup.CamberFront] = -2 };

        var on = Choice(saved, [plates]);
        var off = Choice(saved);

        Assert.Equal(-20, on.Values[CarSetup.RideHeightFront]);
        Assert.Equal(-2, on.Values[CarSetup.CamberFront]);
        Assert.Equal(new SetupRange(-3, 1, 0.1), on.Allowed[CarSetup.CamberFront]);
        // Off the car, the ride height is the parts' again and the camber back inside what the rules allow
        Assert.DoesNotContain(CarSetup.RideHeightFront, off.Values.Keys);
        Assert.Equal(-0.5, off.Values[CarSetup.CamberFront]);

        var data = CarData();
        var files = AcSetupData.Apply(on, data);
        Assert.Equal(0.08, new IniText(files["suspensions.ini"]).GetNumber("FRONT", "ROD_LENGTH")!.Value, 6);
        var setup = new IniText(AcSetupData.Range(on, PartsCatalog.Empty(_temp.Path), null, n => files.GetValueOrDefault(n) ?? data(n))["setup.ini"]);
        // 0.08 m is 800; the plates' -50..0 mm around -20 is -300..+200 of it, on its step of 10
        Assert.Equal(500, setup.GetNumber("ROD_LENGTH_LF", "MIN"));
        Assert.Equal(1000, setup.GetNumber("ROD_LENGTH_LF", "MAX"));
    }

    /// <summary>A catalog of plain running gear and a coilover (damping 1000..4000), the shocks on the corners as given</summary>
    private (PartsCatalog Catalog, CarBuild Parts) Gear(Func<int, PartInstance> shock, Func<int, PartInstance>? spring = null)
    {
        _temp.Parts("parts/gear/test", """
            [
                { "id": "gear/test/rim", "mass": 8, "specs": { "offset": 0.0, "wheel_radius": 0.1905 } },
                { "id": "gear/test/tyre", "mass": 10, "specs": { "friction": 1.0, "radius": 0.32, "tyre_width": 205, "wheel_radius": 190.5 } },
                { "id": "gear/test/brake", "mass": 5, "specs": { "force": 1000, "radius": 0.1 } },
                { "id": "gear/test/spring", "mass": 2, "specs": { "force": 30000 } },
                { "id": "gear/test/drop_coil", "mass": 2, "specs": { "force": 30000, "drop": 0, "adjust": { "drop": { "min": 0, "max": 50, "step": 10 } } } },
                { "id": "gear/test/shock", "mass": 2, "specs": { "damping": 2000 } },
                { "id": "gear/test/coilover", "mass": 2, "specs": { "damping": 2000, "adjust": { "damping": { "min": 1000, "max": 4000, "step": 250 } } } },
                { "id": "gear/test/carrier", "mass": 20, "specs": { "lock": 1 } }
              ]
            """);
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        Assert.DoesNotContain(catalog.Problems, p => p.Contains("adjust"));
        var axle = new RunningGearFactory.AxleParts(catalog.Get("gear/test/tyre")!, catalog.Get("gear/test/rim")!,
            catalog.Get("gear/test/brake")!, catalog.Get("gear/test/spring")!, catalog.Get("gear/test/shock")!);
        var corners = Enumerable.Range(0, RunningGear.Corners).Select(i => new CornerParts
        {
            Rim = new PartInstance("gear/test/rim"),
            Tyre = new PartInstance("gear/test/tyre"),
            Brake = new PartInstance("gear/test/brake"),
            Spring = spring?.Invoke(i) ?? new PartInstance("gear/test/spring"),
            Shock = shock(i)
        }).ToArray();
        return (catalog, new CarBuild { RunningGear = corners, FactoryRunningGear = (axle, axle) });
    }

    /// <summary>setup.ini ranged over the running gear's data</summary>
    private static IniText RangedOverGear(PartsCatalog catalog, CarBuild parts, Func<string, string?> data)
    {
        var gear = AcRunningGearData.Generate(catalog, parts.RunningGear!, parts.FactoryRunningGear!.Value, data, []);
        return new IniText(AcSetupData.Range(Choice(), catalog, parts, n => gear.GetValueOrDefault(n) ?? data(n))["setup.ini"]);
    }

    [Fact]
    public void A_spring_whose_drop_adjusts_lowers_the_car_as_tuned_and_ranges_its_ride_height()
    {
        var (catalog, parts) = Gear(_ => new PartInstance("gear/test/shock"),
            i => RunningGear.IsFront(i) ? new PartInstance("gear/test/drop_coil") { Tuning = { ["drop"] = 20 } } : new PartInstance("gear/test/spring"));
        var data = CarData();

        var gear = AcRunningGearData.Generate(catalog, parts.RunningGear!, parts.FactoryRunningGear!.Value, data, []);
        Assert.Equal(0.08, new IniText(gear["suspensions.ini"]).GetNumber("FRONT", "ROD_LENGTH")!.Value, 6);

        // 0.08 m is 800; the coils' 0..50 mm of drop around 20 is -300..+200 of it
        var setup = RangedOverGear(catalog, parts, data);
        Assert.Equal(500, setup.GetNumber("ROD_LENGTH_LF", "MIN"));
        Assert.Equal(1000, setup.GetNumber("ROD_LENGTH_LF", "MAX"));
    }

    [Fact]
    public void A_tuned_shock_damps_as_tuned_and_its_range_is_its_adjust()
    {
        var (catalog, parts) = Gear(i => RunningGear.IsFront(i) ? new PartInstance("gear/test/coilover") { Tuning = { ["damping"] = 3000 } } : new PartInstance("gear/test/shock"));
        var data = CarData();

        var gear = AcRunningGearData.Generate(catalog, parts.RunningGear!, parts.FactoryRunningGear!.Value, data, []);
        Assert.Equal(3000, new IniText(gear["suspensions.ini"]).GetNumber("FRONT", "DAMP_BUMP"));

        var setup = RangedOverGear(catalog, parts, data);
        Assert.Equal(1000, setup.GetNumber("DAMP_BUMP_LF", "MIN"));
        Assert.Equal(4000, setup.GetNumber("DAMP_BUMP_LF", "MAX"));
    }

    [Fact]
    public void An_axle_with_one_adjustable_shock_ranges_as_far_as_its_tuning_reaches()
    {
        // A coilover at 3000 on the left, the factory 2000 on the right: the data damps at 2000 x (1.5 + 1) / 2
        var (catalog, parts) = Gear(i => i == 0 ? new PartInstance("gear/test/coilover") { Tuning = { ["damping"] = 3000 } } : new PartInstance("gear/test/shock"));
        var data = CarData();

        var setup = RangedOverGear(catalog, parts, data);

        // The coilover from 1000 to 4000 takes the axle from 2000 x (0.5 + 1) / 2 to 2000 x (2 + 1) / 2
        Assert.Equal(1500, setup.GetNumber("DAMP_BUMP_LF", "MIN"));
        Assert.Equal(3000, setup.GetNumber("DAMP_BUMP_LF", "MAX"));
    }

    [Fact]
    public void The_diff_is_held_to_the_mounted_carrier()
    {
        var (catalog, _) = Gear(_ => new PartInstance("gear/test/shock"));
        var carrier = catalog.Get("gear/test/carrier")!;
        var parts = new CarBuild
        {
            RearAxle = new RearAxleParts(null, new PartInstance("gear/test/carrier"), null, null, null),
            FactoryAxle = new RearAxleChoice(carrier, carrier, carrier, carrier)
        };

        var setup = new IniText(AcSetupData.Range(Choice(), catalog, parts, CarData())["setup.ini"]);

        Assert.Equal(50, setup.GetNumber("DIFF_POWER", "MIN"));
        Assert.Equal(50, setup.GetNumber("DIFF_POWER", "MAX"));
    }

    [Fact]
    public void The_rules_leave_out_an_item_they_do_not_know_and_turn_a_range_the_right_way_round()
    {
        var rules = new TuningRules
        {
            Items =
            {
                ["wing_angle"] = new() { Min = 0, Max = 5, Step = 1 },
                [CarSetup.SteerLock] = new() { Min = 90, Max = -30, Step = 10 }
            }
        };
        var problems = new List<string>();

        rules.Check("tuning", problems);

        Assert.DoesNotContain("wing_angle", rules.Items.Keys);
        Assert.Equal(-30, rules.Items[CarSetup.SteerLock].Min);
        Assert.Equal(90, rules.Items[CarSetup.SteerLock].Max);
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void A_part_whose_adjust_is_no_range_is_told()
    {
        var problems = new List<string>();
        var part = new PartDefinition { Id = "x", Specs = { ["damping"] = 2000L, ["adjust"] = Newtonsoft.Json.Linq.JObject.Parse("""{ "camber_front": 3, "damping": { "min": "a", "max": 2 } }""") } };

        Assert.Empty(CarSetup.Read(part, problems));
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void Tuning_goes_over_a_parts_specs_and_figures()
    {
        var part = new PartDefinition { Id = "x", Specs = { ["damping"] = 2000L } };

        Assert.Same(part, part.WithTuning(new Dictionary<string, double>()));
        Assert.Equal(2500, part.WithTuning(new Dictionary<string, double> { ["damping"] = 2500 }).Number("damping"));
        Assert.Equal(2000, part.Number("damping"));

        var set = new PartFigureSet { Numbers = [new PartFigure("timing", FigureOp.Add, 2), new PartFigure("rev_limit", FigureOp.Set, 6000)] };
        Assert.Same(set, PartFigures.Tuned(set, new Dictionary<string, double> { ["other"] = 1 }));
        var tuned = PartFigures.Tuned(set, new Dictionary<string, double> { ["timing"] = 4, ["rev_limit"] = 1e9 })!;
        Assert.Equal(new PartFigure("timing", FigureOp.Add, 4), tuned.Numbers[0]);
        // A tuned figure the part could not give is no tuning: the part's own stands
        Assert.Equal(6000, tuned.Numbers[1].Value);
    }

    [Fact]
    public void A_parts_tuning_is_held_to_its_adjust_and_a_setting_to_its_steps()
    {
        var coilover = new PartDefinition
        {
            Id = "x",
            Specs = { ["damping"] = 2000L, ["adjust"] = Newtonsoft.Json.Linq.JObject.Parse("""{ "damping": { "min": 1500, "max": 6000, "step": 250 } }""") }
        };

        Assert.Equal(6000, coilover.WithTuning(new Dictionary<string, double> { ["damping"] = 20000 }).Number("damping"));
        // A spec the part does not adjust (a rival's tuner's) is not held
        Assert.Equal(9, coilover.WithTuning(new Dictionary<string, double> { ["other"] = 9 }).Number("other"));

        Assert.Equal(0.25, new SetupRange(-1, 1, 0.05).Snap(0.26), 9);
        // Held to the least, then onto the nearest step inside the range
        Assert.Equal(-0.9, new SetupRange(-1, 1, 0.3).Snap(-5), 9);
    }

    [Fact]
    public void A_live_rear_axle_takes_no_camber_or_toe()
    {
        _temp.File("data/suspensions.ini", "[FRONT]\nTYPE=DWB\nSTATIC_CAMBER=-0.5\n[REAR]\nTYPE=AXLE\nROD_LENGTH=0.1\n");

        var files = AcSetupData.Apply(Choice(new() { [CarSetup.CamberRear] = -0.5, [CarSetup.ToeRear] = 1, [CarSetup.CamberFront] = -0.5 }),
            AcCarDataReader.ForFolder(_temp.Combine("data")));

        var ini = new IniText(files["suspensions.ini"]);
        Assert.Equal(-1.0, ini.GetNumber("FRONT", "STATIC_CAMBER")!.Value, 6);
        Assert.Null(ini.Get("REAR", "STATIC_CAMBER"));
        Assert.Null(ini.Get("REAR", "TOE_OUT"));
    }

    [Fact]
    public void The_pressure_range_holds_every_compound_the_car_may_race_on()
    {
        var data = CarData();
        _temp.File("data/tyres.ini", "[COMPOUND_DEFAULT]\nINDEX=1\n[FRONT]\nPRESSURE_STATIC=26\n[REAR]\nPRESSURE_STATIC=26\n[FRONT_1]\nPRESSURE_STATIC=32\n[REAR_1]\nPRESSURE_STATIC=32\n");

        var setup = new IniText(AcSetupData.Range(Choice(), PartsCatalog.Empty(_temp.Path), null, data)["setup.ini"]);

        // On the grid of the 32 psi it starts on, 4 psi either way, and down to the other compound's 26 less its 4
        Assert.Equal(22, setup.GetNumber("PRESSURE_LF", "MIN"));
        Assert.Equal(36, setup.GetNumber("PRESSURE_LF", "MAX"));
    }

    [Fact]
    public void A_mod_that_gives_part_of_an_item_keeps_the_cores_rest()
    {
        var rules = new TuningRules();
        rules.Items[CarSetup.CamberFront] = new TuningItem { Max = 1 };

        rules.Check("tuning", []);

        Assert.Equal(-0.5, rules.Items[CarSetup.CamberFront].Min);
        Assert.Equal(1, rules.Items[CarSetup.CamberFront].Max);
        Assert.Equal(0.1, rules.Items[CarSetup.CamberFront].Step);
    }

    [Fact]
    public void A_spec_engine_parts_tuning_is_held_to_its_adjust()
    {
        var distributor = new PartDefinition
        {
            Id = "x",
            Specs = { ["spark_advance"] = 30L, ["adjust"] = Newtonsoft.Json.Linq.JObject.Parse("""{ "spark_advance": { "min": 20, "max": 40 } }""") }
        };
        var installed = new InstalledPart(distributor);
        installed.Tuning["spark_advance"] = 80;

        Assert.Equal(40, installed.Spec("spark_advance"));
        installed.Tuning["spark_advance"] = double.NaN;
        Assert.Equal(30, installed.Spec("spark_advance"));
    }

    [Fact]
    public void A_tuning_that_is_no_number_leaves_an_adjustable_spec_its_own()
    {
        var coilover = new PartDefinition
        {
            Id = "x",
            Specs = { ["damping"] = 2000L, ["adjust"] = Newtonsoft.Json.Linq.JObject.Parse("""{ "damping": { "min": 1500, "max": 6000 } }""") }
        };

        Assert.Equal(2000, coilover.WithTuning(new Dictionary<string, double> { ["damping"] = double.NaN }).Number("damping"));
    }

    [Fact]
    public void A_live_rear_axle_offers_no_camber_or_toe_and_its_range_is_only_widened()
    {
        var data = CarData();
        _temp.File("data/suspensions.ini", "[FRONT]\nTYPE=DWB\nSTATIC_CAMBER=-0.5\nTOE_OUT=-0.0001\n[REAR]\nTYPE=AXLE\nTOE_OUT=-0.0001\nSTATIC_CAMBER=0\n");

        var choice = AcSetupData.ForData(Choice(new() { [CarSetup.ToeRear] = 1, [CarSetup.ToeFront] = 1 }), data);
        var setup = new IniText(AcSetupData.Range(choice, PartsCatalog.Empty(_temp.Path), null, data)["setup.ini"]);

        Assert.DoesNotContain(CarSetup.ToeRear, choice.Values.Keys);
        Assert.DoesNotContain(CarSetup.CamberRear, choice.Allowed.Keys);
        Assert.Equal(1, choice.Values[CarSetup.ToeFront]);
        // The author's one click, widened to take the data's -10 in
        Assert.Equal(-25, setup.GetNumber("TOE_OUT_LR", "MIN"));
        Assert.Equal(-10, setup.GetNumber("TOE_OUT_LR", "MAX"));
    }

    [Fact]
    public void Another_compounds_pressure_keeps_the_range_on_the_grid_of_the_one_the_car_starts_on()
    {
        var data = CarData();
        _temp.File("data/setup.ini", "[PRESSURE_LF]\nMIN=20\nMAX=40\nSTEP=2\n");
        _temp.File("data/tyres.ini", "[FRONT]\nPRESSURE_STATIC=30\n[REAR]\nPRESSURE_STATIC=30\n[FRONT_1]\nPRESSURE_STATIC=29\n[REAR_1]\nPRESSURE_STATIC=29\n");

        var setup = new IniText(AcSetupData.Range(Choice(), PartsCatalog.Empty(_temp.Path), null, data)["setup.ini"]);

        // 30 with 4 psi either way in steps of 2, and a step more for the other compound's 29
        Assert.Equal(24, setup.GetNumber("PRESSURE_LF", "MIN"));
        Assert.Equal(34, setup.GetNumber("PRESSURE_LF", "MAX"));
    }

    [Fact]
    public void An_item_a_part_holds_away_from_nothing_races_where_the_part_allows()
    {
        var blocks = new PartDefinition
        {
            Id = "x",
            Specs = { ["adjust"] = Newtonsoft.Json.Linq.JObject.Parse("""{ "ride_height_rear": { "min": -50, "max": -20, "step": 5 } }""") }
        };

        Assert.Equal(-20, Choice(parts: [blocks]).Values[CarSetup.RideHeightRear]);
    }

    [Fact]
    public void The_brake_bias_moves_no_further_than_all_of_the_braking_at_the_front()
    {
        var data = CarData();
        _temp.File("data/brakes.ini", "[DATA]\nMAX_TORQUE=1000\nFRONT_SHARE=0.95\n");
        var valve = new PartDefinition
        {
            Id = "x",
            Specs = { ["adjust"] = Newtonsoft.Json.Linq.JObject.Parse("""{ "brake_bias": { "min": -0.1, "max": 0.1, "step": 0.01 } }""") }
        };

        var choice = AcSetupData.ForData(Choice(new() { [CarSetup.BrakeBias] = 0.1 }, [valve]), data);
        var files = AcSetupData.Apply(choice, data);
        var setup = new IniText(AcSetupData.Range(choice, PartsCatalog.Empty(_temp.Path), null, n => files.GetValueOrDefault(n) ?? data(n))["setup.ini"]);

        Assert.Equal(0.05, choice.Values[CarSetup.BrakeBias], 9);
        Assert.Equal(1.0, new IniText(files["brakes.ini"]).GetNumber("DATA", "FRONT_SHARE")!.Value, 6);
        Assert.Equal(85, setup.GetNumber("FRONT_BIAS", "MIN")!.Value, 6);
        Assert.Equal(100, setup.GetNumber("FRONT_BIAS", "MAX")!.Value, 6);
    }

    [Fact]
    public void A_mistyped_adjust_is_told()
    {
        var problems = new List<string>();
        var plates = new PartDefinition
        {
            Id = "x",
            Specs = { ["adjust"] = Newtonsoft.Json.Linq.JObject.Parse("""{ "camber_frnt": { "min": -1, "max": 1 }, "camber_front": { "min": -1, "max": 1, "step": "0.1" } }""") }
        };

        CarSetup.Read(plates, problems);

        Assert.Contains(problems, p => p.Contains("camber_frnt") && p.Contains("nor one of the part's specs"));
        Assert.Contains(problems, p => p.Contains("\"camber_front\"") && p.Contains("must be numbers"));
    }
}
