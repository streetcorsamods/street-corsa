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
    public void A_corner_with_a_wheel_names_it_in_race_ini_by_acs_wheel_name()
    {
        var rim = new ShownModel(ShownParts.FileName(Cragar), "x.kn5");
        var tyre = new ShownModel(ShownParts.FileName(Slick), "y.kn5");
        var start = new RaceStartState
        {
            Wheels = [null, null, new ShownWheel(rim), new ShownWheel(rim, tyre, Matrix4x4.CreateTranslation(0.027f, 0, 0))]
        };

        var keys = start.IniKeys(1).ToDictionary(k => k.Key, k => k.Value);

        Assert.False(keys.ContainsKey("CAR_1_RIM_LF"));
        Assert.Equal(@"parts\rims_mopar_cragar_8.kn5", keys["CAR_1_RIM_LR"]);
        Assert.False(keys.ContainsKey("CAR_1_TYRE_LR"));
        Assert.Equal(@"parts\tyres_mopar_Firestone_11_slick_3.kn5", keys["CAR_1_TYRE_RR"]);
        Assert.Equal("1,0,0,0,1,0,0,0,1,0.027,0,0", keys["CAR_1_TYRE_RR_AT"]);
        Assert.Equal(2, start.Models.Count());
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
}
