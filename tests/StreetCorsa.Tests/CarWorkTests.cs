using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Tests;

/// <summary>
/// The running gear's work order (<see cref="CarWork"/>): rules at the part's own corner, what goes along never in the
/// way, the rear axle on its leaf springs
/// </summary>
public class CarWorkTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private static CarFacts Chevy66 => TestCars.Chevy66;
    private static readonly CarFacts Jaguar65 = new("jaguar", "jaguar", 1965, "UK");

    private static AcCarSpecs Specs() => TestCars.LiveAxleSpecs();

    private static List<PartInstance> FactoryParts(CarFacts car)
    {
        var axle = RunningGearGuess.ChooseAxle(Catalog, Specs(), car, null)!;
        return RunningGearFactory.Create(Catalog, Specs(), 1, axle, car);
    }

    private static List<PartDefinition> Factory(CarFacts car) =>
        RunningGearFactory.Choose(Catalog, Specs(), car) is var (front, rear) ? front.All.Concat(rear.All).ToList() : [];

    private static PartInstance OnSlot(List<PartInstance> parts, int carSlot) => parts.Single(p => p.ParentSlot == carSlot);

    private static PartInstance Housing(List<PartInstance> parts) => OnSlot(parts, RearAxle.CarSlot);

    private static PartInstance OnHousing(List<PartInstance> parts, int slot) => Housing(parts).Children.Single(c => c.ParentSlot == slot);

    private static string? WhyNotOn(List<PartInstance> parts, PartInstance loose, MountPlace place, CarFacts car) =>
        CarWork.WhyNotOn(Catalog, parts, loose, place, liveAxle: true, Factory(car));

    [Fact]
    public void A_drum_waits_for_its_own_wheel_only()
    {
        var parts = FactoryParts(Chevy66);
        var corners = RunningGear.Mounted(parts, Catalog);
        var (leftFront, rightFront) = (corners[0].Brake!, corners[1].Brake!);
        Assert.NotNull(CarWork.WhyNotOff(Catalog, parts, leftFront));

        var wheel = OnSlot(parts, RunningGear.WheelSlot(0));
        Workbench.Remove(parts, wheel);
        Assert.Null(CarWork.WhyNotOff(Catalog, parts, leftFront));
        Assert.NotNull(CarWork.WhyNotOff(Catalog, parts, rightFront));

        // And goes back on only with its wheel off
        Workbench.Remove(parts, leftFront);
        var place = new MountPlace(corners[0].BackingPlate, RunningGear.DrumSlot, 1);
        Assert.Null(WhyNotOn(parts, leftFront, place, Chevy66));
        Workbench.Mount(parts, new MountPlace(null, RunningGear.WheelSlot(0), 1), wheel);
        Assert.NotNull(WhyNotOn(parts, leftFront, place, Chevy66));
    }

    [Fact]
    public void A_drum_brake_comes_apart_in_its_order_and_goes_together_the_other_way_round()
    {
        var parts = FactoryParts(Chevy66);
        Workbench.Remove(parts, OnSlot(parts, RunningGear.WheelSlot(0)));
        var corner = RunningGear.Mounted(parts, Catalog)[0];
        var (plate, drum, shoes, cylinder) = (corner.BackingPlate!, corner.Brake!, corner.Shoes!, corner.WheelCylinder!);
        Assert.NotNull(corner.Hub);

        string? Off(PartInstance part) => CarWork.WhyNotOff(Catalog, parts, part);
        Assert.NotNull(Off(shoes));
        Assert.NotNull(Off(cylinder));
        Assert.NotNull(Off(plate));

        Workbench.Remove(parts, drum);
        Assert.Null(Off(shoes));
        Assert.NotNull(Off(cylinder));
        Workbench.Remove(parts, shoes);
        Assert.Null(Off(cylinder));
        Assert.NotNull(Off(plate));
        Workbench.Remove(parts, cylinder);
        Assert.Null(Off(plate));

        // The drum goes on over its shoes and wheel cylinder; the shoes over the cylinder
        string? On(PartInstance part, int slot) => WhyNotOn(parts, part, new MountPlace(plate, slot, 1), Chevy66);
        Assert.NotNull(On(drum, RunningGear.DrumSlot));
        Assert.NotNull(On(shoes, RunningGear.ShoesSlot));
        Assert.Null(On(cylinder, RunningGear.WheelCylinderSlot));
        Workbench.Mount(parts, new MountPlace(plate, RunningGear.WheelCylinderSlot, 1), cylinder);
        Assert.Null(On(shoes, RunningGear.ShoesSlot));
        Workbench.Mount(parts, new MountPlace(plate, RunningGear.ShoesSlot, 1), shoes);
        Assert.Null(On(drum, RunningGear.DrumSlot));
    }

    [Fact]
    public void The_carrier_comes_out_after_the_shafts_and_a_shaft_after_its_drum()
    {
        var parts = FactoryParts(Chevy66);
        var carrier = OnHousing(parts, RearAxle.CarrierSlot);
        var leftShaft = OnHousing(parts, RearAxle.LeftShaftSlot);
        var rightShaft = OnHousing(parts, RearAxle.RightShaftSlot);
        Assert.NotNull(CarWork.WhyNotOff(Catalog, parts, carrier));
        Assert.NotNull(CarWork.WhyNotOff(Catalog, parts, leftShaft));

        Workbench.Remove(parts, OnSlot(parts, RunningGear.WheelSlot(2)));
        Workbench.Remove(parts, OnHousing(parts, RearAxle.LeftBrakeSlot));
        Assert.Null(CarWork.WhyNotOff(Catalog, parts, leftShaft));
        Assert.NotNull(CarWork.WhyNotOff(Catalog, parts, rightShaft));

        Workbench.Remove(parts, leftShaft);
        Workbench.Remove(parts, OnSlot(parts, RunningGear.WheelSlot(3)));
        Workbench.Remove(parts, OnHousing(parts, RearAxle.RightBrakeSlot));
        Workbench.Remove(parts, rightShaft);
        Assert.Null(CarWork.WhyNotOff(Catalog, parts, carrier));

        // A shaft goes in only into a carrier
        Workbench.Remove(parts, carrier);
        Assert.NotNull(WhyNotOn(parts, leftShaft, new MountPlace(Housing(parts), RearAxle.LeftShaftSlot, 1), Chevy66));
        Workbench.Mount(parts, new MountPlace(Housing(parts), RearAxle.CarrierSlot, 1), carrier);
        Assert.Null(WhyNotOn(parts, leftShaft, new MountPlace(Housing(parts), RearAxle.LeftShaftSlot, 1), Chevy66));
    }

    [Fact]
    public void The_axle_drops_whole_with_what_it_carries_once_the_rear_wheels_are_off()
    {
        var parts = FactoryParts(Chevy66);
        var housing = Housing(parts);
        Assert.NotNull(CarWork.WhyNotOff(Catalog, parts, housing));

        Workbench.Remove(parts, OnSlot(parts, RunningGear.WheelSlot(2)));
        Workbench.Remove(parts, OnSlot(parts, RunningGear.WheelSlot(3)));
        Assert.Null(CarWork.WhyNotOff(Catalog, parts, housing));
    }

    [Fact]
    public void The_axle_sits_on_its_leaves_it_goes_on_after_both_and_a_leaf_comes_off_after_it()
    {
        var parts = FactoryParts(Chevy66);
        var housing = Housing(parts);
        var leaf = OnSlot(parts, RunningGear.SpringSlot(2));
        var axlePlace = new MountPlace(null, RearAxle.CarSlot, 1);

        // The axle on: neither leaf comes off; the front springs do
        Assert.NotNull(CarWork.WhyNotOff(Catalog, parts, leaf));
        Assert.Null(CarWork.WhyNotOff(Catalog, parts, OnSlot(parts, RunningGear.SpringSlot(0))));

        // The axle off: the leaf comes off, and then the axle cannot go back on until it is on again
        Workbench.Remove(parts, OnSlot(parts, RunningGear.WheelSlot(2)));
        Workbench.Remove(parts, OnSlot(parts, RunningGear.WheelSlot(3)));
        Workbench.Remove(parts, housing);
        Assert.Null(CarWork.WhyNotOff(Catalog, parts, leaf));
        Workbench.Remove(parts, leaf);
        Assert.NotNull(WhyNotOn(parts, housing, axlePlace, Chevy66));
        Workbench.Mount(parts, new MountPlace(null, RunningGear.SpringSlot(2), 1), leaf);
        Assert.Null(WhyNotOn(parts, housing, axlePlace, Chevy66));

        // A coil-sprung axle carries its springs: no order
        var coils = FactoryParts(Jaguar65);
        var coilHousing = Housing(coils);
        Workbench.Remove(coils, OnSlot(coils, RunningGear.WheelSlot(2)));
        Workbench.Remove(coils, OnSlot(coils, RunningGear.WheelSlot(3)));
        Workbench.Remove(coils, coilHousing);
        Assert.Null(WhyNotOn(coils, coilHousing, axlePlace, Jaguar65));
    }
}
