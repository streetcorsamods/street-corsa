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
        var setup = new IniText(AcSetupData.Range(choice, catalog: PartsCatalog.Empty(_temp.Path), null, null, data)["setup.ini"]);

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
        var setup = new IniText(AcSetupData.Range(choice, PartsCatalog.Empty(_temp.Path), null, null, Read)["setup.ini"]);
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
        var setup = new IniText(AcSetupData.Range(on, PartsCatalog.Empty(_temp.Path), null, null, n => files.GetValueOrDefault(n) ?? data(n))["setup.ini"]);
        // 0.08 m is 800; the plates' -50..0 mm around -20 is -300..+200 of it, on its step of 10
        Assert.Equal(500, setup.GetNumber("ROD_LENGTH_LF", "MIN"));
        Assert.Equal(1000, setup.GetNumber("ROD_LENGTH_LF", "MAX"));
    }

    [Fact]
    public void A_tuned_shock_damps_as_tuned_and_its_range_is_its_adjust()
    {
        _temp.Parts("parts/gear/test", """
            [
                { "id": "gear/test/rim", "mass": 8, "specs": { "offset": 0.0, "wheel_radius": 0.1905 } },
                { "id": "gear/test/tyre", "mass": 10, "specs": { "friction": 1.0, "radius": 0.32, "tyre_width": 205, "wheel_radius": 190.5 } },
                { "id": "gear/test/brake", "mass": 5, "specs": { "force": 1000, "radius": 0.1 } },
                { "id": "gear/test/spring", "mass": 2, "specs": { "force": 30000 } },
                { "id": "gear/test/shock", "mass": 2, "specs": { "damping": 2000 } },
                { "id": "gear/test/coilover", "mass": 2, "specs": { "damping": 2000, "adjust": { "damping": { "min": 1000, "max": 4000, "step": 250 } } } }
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
            Spring = new PartInstance("gear/test/spring"),
            Shock = RunningGear.IsFront(i) ? new PartInstance("gear/test/coilover") { Tuning = { ["damping"] = 3000 } } : new PartInstance("gear/test/shock")
        }).ToArray();
        var data = CarData();

        var gear = AcRunningGearData.Generate(catalog, corners, (axle, axle), data, []);
        Assert.Equal(3000, new IniText(gear["suspensions.ini"]).GetNumber("FRONT", "DAMP_BUMP"));

        var setup = new IniText(AcSetupData.Range(Choice(), catalog, corners, null, n => gear.GetValueOrDefault(n) ?? data(n))["setup.ini"]);
        Assert.Equal(1000, setup.GetNumber("DAMP_BUMP_LF", "MIN"));
        Assert.Equal(4000, setup.GetNumber("DAMP_BUMP_LF", "MAX"));
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
        var part = new PartDefinition { Id = "x", Specs = { ["adjust"] = Newtonsoft.Json.Linq.JObject.Parse("""{ "camber_front": 3, "damping": { "min": "a", "max": 2 } }""") } };

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

        var setup = new IniText(AcSetupData.Range(Choice(), PartsCatalog.Empty(_temp.Path), null, null, data)["setup.ini"]);

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
}
