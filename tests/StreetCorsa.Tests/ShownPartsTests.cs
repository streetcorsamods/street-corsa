using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services;

namespace StreetCorsa.Tests;

/// <summary>The rims a car shows in AC that its model does not have: which corners, race.ini, the files next to the mode</summary>
public sealed class ShownPartsTests : IDisposable
{
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsFolder));

    private const string Factory = "rims/mopar/rim_8_5";
    private const string Cragar = "rims/mopar/cragar_8";

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static (RunningGearFactory.AxleParts, RunningGearFactory.AxleParts) FactoryGear()
    {
        var rim = Catalog.Value.Get(Factory)!;
        var axle = new RunningGearFactory.AxleParts(rim, rim, rim, rim, rim);
        return (axle, axle);
    }

    private static List<PartInstance> Rims(params string[] corners) =>
        corners.Select((id, corner) => new PartInstance(id) { ParentSlot = RunningGear.WheelSlot(corner) }).ToList();

    [Fact]
    public void Only_the_corners_off_their_factory_rim_show_a_rim()
    {
        var shown = ShownParts.Rims(Rims(Factory, Factory, Cragar, Cragar), Catalog.Value, FactoryGear());

        Assert.Null(shown[0]);
        Assert.Null(shown[1]);
        Assert.Equal(Path.Combine("parts", "rims_mopar_cragar_8.kn5"), shown[2]!.Name);
        Assert.True(File.Exists(shown[3]!.Source));
    }

    [Fact]
    public void A_car_whose_factory_rims_are_not_known_keeps_its_own()
    {
        Assert.All(ShownParts.Rims(Rims(Cragar, Cragar, Cragar, Cragar), Catalog.Value, null), Assert.Null);
    }

    [Fact]
    public void A_corner_with_a_rim_names_it_in_race_ini_by_acs_wheel_name()
    {
        var rim = new ShownModel(ShownParts.FileName(Cragar), "x.kn5");
        var start = new RaceStartState { Rims = [null, null, rim, rim] };

        var keys = start.IniKeys(1).ToDictionary(k => k.Key, k => k.Value);

        Assert.False(keys.ContainsKey("CAR_1_RIM_LF"));
        Assert.Equal(@"parts\rims_mopar_cragar_8.kn5", keys["CAR_1_RIM_LR"]);
        Assert.Equal(@"parts\rims_mopar_cragar_8.kn5", keys["CAR_1_RIM_RR"]);
        Assert.Single(start.Models);
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
