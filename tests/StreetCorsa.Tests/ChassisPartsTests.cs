using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;

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

    private static IniText? Export(bool blocks, string packerRange = "0.140")
    {
        var specs = TestCars.LiveAxleSpecs();
        var parts = Parts(blocks);
        var problems = new List<string>();
        var files = AcRunningGearData.Generate(Catalog, RunningGear.Mounted(parts), RunningGearFactory.Choose(Catalog, specs, TestCars.Chevy66)!.Value,
            name => name == "suspensions.ini" ? string.Format(System.Globalization.CultureInfo.InvariantCulture, SuspensionsIni, packerRange) : null,
            problems, RunningGear.SwayBars(parts));
        Assert.Empty(problems);
        return files.TryGetValue("suspensions.ini", out var text) ? new IniText(text) : null;
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
    public void A_lowered_car_sits_lower_in_the_scenes_but_starts_a_race_with_no_damage_for_it()
    {
        var car = new Car("car_a") { HasPartsAssigned = true, HasRunningGearAssigned = true };
        car.Parts.AddRange(Parts(blocks: true));

        var stance = CarLooks.Stance(car, Catalog);
        Assert.Equal([0, 0, -0.05, -0.05], stance.Select(h => Math.Round(h, 6)));
        Assert.All(CarLooks.Sag(car), h => Assert.Equal(0, h));

        // A broken rear spring sags on top of the drop
        RunningGear.Mounted(car.Parts)[2].Spring!.Tear = 0;
        Assert.Equal(-0.05 + CarLooks.Sag(car)[2], CarLooks.Stance(car, Catalog)[2], 6);
    }

    [Fact]
    public void A_drop_reads_as_millimetres_held_to_what_a_part_can_do()
    {
        Assert.Equal(50, RunningGear.Drop(Catalog.Get(Block)!));
        Assert.Equal(0, RunningGear.Drop(Catalog.Get("running_gear/test_leaves/ladder")!));
        Assert.Equal(50, RunningGear.Drop(Catalog, Parts(blocks: true).First(p => p.ParentSlot == RunningGear.SpringSlot(2))));
    }
}
