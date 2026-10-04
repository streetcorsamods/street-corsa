using System.Numerics;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services;

namespace StreetCorsa.Tests;

/// <summary>The wheels a car shows in AC that its model does not have: which corners, race.ini, the files next to the mode</summary>
public sealed class ShownPartsTests : IDisposable
{
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsFolder));

    private const string Factory = "rims/mopar/rim_8_5";
    private const string Cragar = "rims/mopar/cragar_8";
    private const string FactoryTyre = "tyres/mopar/Tyre_15_8";
    private const string Slick = "tyres/mopar/Firestone_11_slick_3";

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static (RunningGearFactory.AxleParts, RunningGearFactory.AxleParts) FactoryGear()
    {
        var rim = Catalog.Value.Get(Factory)!;
        var axle = new RunningGearFactory.AxleParts(Catalog.Value.Get(FactoryTyre)!, rim, rim, rim, rim);
        return (axle, axle);
    }

    /// <summary>A rim on each corner, with the tyre given (the factory's when none)</summary>
    private static List<PartInstance> Wheels(string[] rims, string[]? tyres = null) =>
        rims.Select((id, corner) =>
        {
            var rim = new PartInstance(id) { ParentSlot = RunningGear.WheelSlot(corner) };
            rim.Children.Add(new PartInstance(tyres?[corner] ?? FactoryTyre) { ParentSlot = RunningGear.TyreSlotOnRim, OwnSlot = 1 });
            return rim;
        }).ToList();

    [Fact]
    public void Only_the_corners_off_their_factory_rim_show_a_rim_round_the_cars_tyre()
    {
        var shown = ShownParts.Wheels(Wheels([Factory, Factory, Cragar, Cragar]), Catalog.Value, FactoryGear());

        Assert.Null(shown[0]);
        Assert.Null(shown[1]);
        Assert.Equal(Path.Combine("parts", "rims_mopar_cragar_8.kn5"), shown[2]!.Rim.Name);
        Assert.True(File.Exists(shown[3]!.Rim.Source));
        Assert.Null(shown[3]!.Tyre);
    }

    [Fact]
    public void Another_tyre_brings_the_whole_wheel_the_factory_rim_too_the_tyre_on_the_rims_slot()
    {
        var shown = ShownParts.Wheels(Wheels([Factory, Factory, Factory, Factory], [FactoryTyre, FactoryTyre, Slick, Slick]),
            Catalog.Value, FactoryGear());

        Assert.Null(shown[0]);
        Assert.Equal(Path.Combine("parts", "rims_mopar_rim_8_5.kn5"), shown[2]!.Rim.Name);
        Assert.Equal(Path.Combine("parts", "tyres_mopar_Firestone_11_slick_3.kn5"), shown[2]!.Tyre!.Name);
        // The E-T mag's tyre slot is 27 mm out along the axle
        Assert.Equal(0.027f, shown[2]!.TyreOnRim.M41, 3);
    }

    [Fact]
    public void A_car_whose_factory_running_gear_is_not_known_keeps_its_own()
    {
        Assert.All(ShownParts.Wheels(Wheels([Cragar, Cragar, Cragar, Cragar]), Catalog.Value, null), Assert.Null);
    }

    [Fact]
    public void A_corner_with_a_wheel_names_it_in_race_ini_by_acs_wheel_name_and_an_engine_part_by_its_number()
    {
        var rim = new ShownModel(ShownParts.FileName(Cragar), "x.kn5");
        var tyre = new ShownModel(ShownParts.FileName(Slick), "y.kn5");
        var start = new RaceStartState
        {
            Shown = ShownSet.Of([null, null, new ShownWheel(rim), new ShownWheel(rim, tyre, Matrix4x4.CreateTranslation(0.027f, 0, 0))],
                [new ShownModelAt(new ShownModel(ShownParts.FileName("engines/gm/blower"), "z.kn5"), Matrix4x4.CreateTranslation(0, 0.8f, 1.2f))])
        };

        var keys = start.IniKeys(1).ToDictionary(k => k.Key, k => k.Value);

        Assert.False(keys.ContainsKey("CAR_1_RIM_LF"));
        Assert.Equal(@"parts\rims_mopar_cragar_8.kn5", keys["CAR_1_RIM_LR"]);
        Assert.False(keys.ContainsKey("CAR_1_TYRE_LR"));
        Assert.Equal(@"parts\tyres_mopar_Firestone_11_slick_3.kn5", keys["CAR_1_TYRE_RR"]);
        Assert.Equal("1,0,0,0,1,0,0,0,1,0.027,0,0", keys["CAR_1_TYRE_RR_AT"]);
        // An engine part that stands out: its model and where it sits in the car's model
        Assert.Equal(@"parts\engines_gm_blower.kn5", keys["CAR_1_PART_1"]);
        Assert.Equal("1,0,0,0,1,0,0,0,1,0,0.8,1.2", keys["CAR_1_PART_1_AT"]);
        Assert.Equal(3, start.Models.Count());
    }

    [Fact]
    public void The_models_go_next_to_the_mode_and_leave_with_the_next_race()
    {
        var source = _temp.Combine("build", "AcModes", "sc_race");
        _temp.File(Path.Combine("build", "AcModes", "sc_race", "mode.lua"), "-- mode");
        _temp.File("cragar.kn5", "kn5");
        var ac = _temp.Combine("ac");
        Directory.CreateDirectory(Path.Combine(ac, "extension"));
        var installed = Path.Combine(ScRaceMode.InstalledFolder(ac), "parts", "rims_mopar_cragar_8.kn5");

        Assert.Null(new ScRaceMode(source).Install(ac, models: [new ShownModel(ShownParts.FileName(Cragar), _temp.Combine("cragar.kn5"))]));
        Assert.Equal("kn5", File.ReadAllText(installed));

        Assert.Null(new ScRaceMode(source).Install(ac));
        Assert.False(File.Exists(installed));
    }

    [Fact]
    public void A_free_run_of_a_car_with_part_wheels_runs_in_its_mode_and_names_them()
    {
        var cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(cfg);
        var ini = new StreetCorsa.Services.Configuration.IniModificationService(cfg, _temp.Combine("restore"));
        var rim = new ShownModel(ShownParts.FileName(Cragar), "x.kn5");

        Assert.True(ini.ApplyIntent(new StreetCorsa.Services.Configuration.Models.FreeRunIntent
        {
            CarId = "car", PlayerName = "P", TrackId = "track", Shown = ShownSet.Of([new ShownWheel(rim), null, null, null])
        }));
        var race = File.ReadAllLines(Path.Combine(cfg, "race.ini"));
        Assert.Contains("__CM_CUSTOM_MODE=" + StreetCorsa.Services.Configuration.IniModificationService.FreeRunModeId, race);
        Assert.Contains("[STREET_CORSA]", race);
        Assert.Contains(@"CAR_0_RIM_LF=parts\rims_mopar_cragar_8.kn5", race);

        // On its own wheels: no mode, as a free run always was
        Assert.True(ini.ApplyIntent(new StreetCorsa.Services.Configuration.Models.FreeRunIntent { CarId = "car", PlayerName = "P", TrackId = "track" }));
        race = File.ReadAllLines(Path.Combine(cfg, "race.ini"));
        Assert.DoesNotContain(race, l => l.StartsWith("__CM_CUSTOM_MODE"));
        Assert.DoesNotContain("[STREET_CORSA]", race);
    }

    [Fact]
    public void The_free_run_mode_goes_in_with_the_race_modes_parts_script_and_the_models()
    {
        var source = _temp.Combine("build", "AcModes", "sc_race");
        _temp.File(Path.Combine("build", "AcModes", "sc_race", "mode.lua"), "-- race");
        _temp.File(Path.Combine("build", "AcModes", "sc_race", "car_parts.lua"), "-- parts");
        _temp.File(Path.Combine("build", "AcModes", "sc_free", "mode.lua"), "-- free");
        _temp.File("cragar.kn5", "kn5");
        var ac = _temp.Combine("ac");
        Directory.CreateDirectory(Path.Combine(ac, "extension"));

        Assert.Null(new ScRaceMode(source).InstallFreeRun(ac, [new ShownModel(ShownParts.FileName(Cragar), _temp.Combine("cragar.kn5"))]));

        var free = Path.Combine(ac, "extension", "lua", "new-modes", "sc_free");
        Assert.Equal("-- free", File.ReadAllText(Path.Combine(free, "mode.lua")));
        Assert.Equal("-- parts", File.ReadAllText(Path.Combine(free, "car_parts.lua")));
        Assert.Equal("kn5", File.ReadAllText(Path.Combine(free, "parts", "rims_mopar_cragar_8.kn5")));
    }

    [Fact]
    public void A_supercharger_and_what_sits_on_it_are_shown_the_rest_of_the_engine_is_not()
    {
        // A GM small-block with a blower, mounted by the workbench's rules
        var catalog = Catalog.Value;
        var parts = new List<PartInstance> { new("engines/gm/GM_327_block") { ParentSlot = PartInstance.CarEngineSlot } };
        foreach (var id in new[] { "engines/gm/GM_small_block_L_head", "engines/gm/GM_small_block_R_head", "engines/gm/Weiand_pro_street_manifold",
                     "engines/generic/Weiand_8_71_supercharger", "engines/gm/stock_4brl_carburator" })
        {
            var loose = new PartInstance(id);
            Workbench.Mount(parts, Workbench.FindPlaces(catalog, parts, loose)[0], loose);
        }
        var hubs = new CarAnchors(new Vector3(0.74f, 0.08f, 1.6f), new Vector3(-0.74f, 0.08f, 1.6f), new Vector3(0.77f, 0.05f, -1.3f), new Vector3(-0.77f, 0.05f, -1.3f));

        var shown = ShownEngineParts.Of(catalog, PartTrees.ToInstalled(catalog, parts[0])!.Root, hubs, EngineLayout.Front);

        Assert.Equal(["engines_generic_Weiand_8_71_supercharger.kn5", "engines_gm_stock_4brl_carburator.kn5"], shown.Select(s => Path.GetFileName(s.Model.Name)));
        Assert.True(shown[1].At.Translation.Y > shown[0].At.Translation.Y);
        Assert.Empty(ShownEngineParts.Of(catalog, PartTrees.ToInstalled(catalog, parts[0])!.Root, null, EngineLayout.Front));
    }

    [Fact]
    public void Engine_parts_under_the_hood_go_up_onto_it_never_down()
    {
        ShownModelAt Part(float y) => new(new ShownModel("p.kn5", "p.kn5"), Matrix4x4.CreateTranslation(0, y, 1.3f));

        Assert.Equal(0.34f, ShownEngineParts.Lift([Part(0.38f), Part(0.67f)], _ => 0.72f), 3);
        Assert.Equal(0f, ShownEngineParts.Lift([Part(0.38f)], _ => 0.2f));
        Assert.Equal(0f, ShownEngineParts.Lift([Part(0.38f)], _ => null));
    }
}
