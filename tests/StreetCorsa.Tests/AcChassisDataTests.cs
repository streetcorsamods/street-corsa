using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Tests;

/// <summary>Chassis tuning phase 2: a spoiler as a wing of its own, an electronics unit's ABS and traction control</summary>
public class AcChassisDataTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private const string Spoiler = "running_gear/test_chassis/spoiler";
    private const string AbsDelete = "running_gear/test_chassis/abs_delete";
    private const string TractionControl = "running_gear/test_chassis/traction_control";

    private const string Aero =
        "[HEADER]\nVERSION=2\n\n[WING_0]\nNAME=BODY\nCHORD=1\nSPAN=1.6\nPOSITION=0,0.2,0\nCL_GAIN=0\n\n" +
        "[WING_1]\nNAME=FRONT\nPOSITION=0,0.1,1.9\nCL_GAIN=1\n\n[WING_2]\nNAME=REAR\nPOSITION=0,0.33,-2.00\nCL_GAIN=1\n";

    private const string Electronics =
        "[ABS]\nSLIP_RATIO_LIMIT=0.13\nCURVE=\nPRESENT=1\nACTIVE=1\nRATE_HZ=200\n\n[TRACTION_CONTROL]\nSLIP_RATIO_LIMIT=0.10\nCURVE=\nPRESENT=0\nACTIVE=1\nRATE_HZ=120\nMIN_SPEED_KMH=50\n";

    private static Dictionary<string, string> Generate(Dictionary<string, string> data, params string[] ids) =>
        AcChassisData.Generate(ids.Select(id => Catalog.Get(id)!).ToList(), data.GetValueOrDefault);

    [Fact]
    public void A_spoiler_is_a_wing_of_its_own_by_the_cars_rear_wing_with_its_own_tables()
    {
        var files = Generate(new() { ["aero.ini"] = Aero }, Spoiler);
        var aero = new IniText(files["aero.ini"]);

        Assert.Equal(AcChassisData.SpoilerWing, aero.Get("WING_3", "NAME"));
        Assert.Equal("0,0.33,-2.00", aero.Get("WING_3", "POSITION"));
        Assert.Equal(0.3, aero.GetNumber("WING_3", "CHORD"));
        Assert.Equal(1.4, aero.GetNumber("WING_3", "SPAN"));
        Assert.Equal("1", aero.Get("WING_2", "CL_GAIN"));

        // Downforce is negative lift, at every angle
        Assert.All(files[aero.Get("WING_3", "LUT_AOA_CL")!].Split("\r\n", StringSplitOptions.RemoveEmptyEntries), l => Assert.EndsWith("|-0.4000", l));
        Assert.All(files[aero.Get("WING_3", "LUT_AOA_CD")!].Split("\r\n", StringSplitOptions.RemoveEmptyEntries), l => Assert.EndsWith("|0.0600", l));
    }

    [Fact]
    public void A_car_with_no_rear_wing_gets_the_spoiler_over_the_boot_and_one_with_no_aero_none()
    {
        var aero = new IniText(Generate(new() { ["aero.ini"] = "[WING_0]\nNAME=BODY\n" }, Spoiler)["aero.ini"]);
        Assert.Equal("0,0.35,-2.00", aero.Get("WING_1", "POSITION"));

        Assert.Empty(Generate(new(), Spoiler));
    }

    [Fact]
    public void An_electronics_unit_takes_abs_out_and_puts_traction_control_in()
    {
        var files = Generate(new() { ["electronics.ini"] = Electronics }, AbsDelete, TractionControl);
        var ini = new IniText(files["electronics.ini"]);

        Assert.Equal("0", ini.Get("ABS", "PRESENT"));
        Assert.Equal("0", ini.Get("ABS", "ACTIVE"));
        Assert.Equal("1", ini.Get("TRACTION_CONTROL", "PRESENT"));
        Assert.Equal(0.10, ini.GetNumber("TRACTION_CONTROL", "SLIP_RATIO_LIMIT"));
    }

    [Fact]
    public void Traction_control_on_a_car_without_any_gets_a_whole_section_and_abs_out_on_one_without_is_nothing()
    {
        var ini = new IniText(Generate(new(), TractionControl)["electronics.ini"]);
        Assert.Equal("1", ini.Get("TRACTION_CONTROL", "PRESENT"));
        Assert.Equal(0.12, ini.GetNumber("TRACTION_CONTROL", "SLIP_RATIO_LIMIT"));
        Assert.NotNull(ini.Get("TRACTION_CONTROL", "RATE_HZ"));

        Assert.Empty(Generate(new(), AbsDelete));
    }

    [Fact]
    public void The_spoiler_and_the_unit_go_on_their_own_slots_are_sold_and_reach_the_race_export()
    {
        var parts = RunningGearFactory.Create(Catalog, TestCars.LiveAxleSpecs(), 1, null, TestCars.Chevy66);
        Assert.Equal([RunningGear.SpoilerSlot], Workbench.FindPlaces(Catalog, parts, new PartInstance(Spoiler)).Select(p => p.ParentSlot));
        Assert.Equal([RunningGear.ElectronicsSlot], Workbench.FindPlaces(Catalog, parts, new PartInstance(AbsDelete)).Select(p => p.ParentSlot));

        Assert.True(RunningGear.DoesSomething(Catalog.Get(Spoiler)!));
        Assert.True(RunningGear.DoesSomething(Catalog.Get(AbsDelete)!));
        Assert.False(RunningGear.DoesSomething(new PartDefinition { Id = "x", Class = "spoiler" }));
        Assert.False(RunningGear.DoesSomething(new PartDefinition { Id = "x", Class = "electronics_unit" }));

        parts.Add(new PartInstance(Spoiler) { ParentSlot = RunningGear.SpoilerSlot, OwnSlot = 1 });
        var build = new CarBuild { Chassis = RunningGear.Chassis(parts, Catalog) };
        var result = AcCarBuild.Generate(Catalog, build, n => n == "aero.ini" ? Aero : null);
        Assert.Contains("aero.ini", result.Files.Keys);
    }
}
