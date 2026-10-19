using System.Numerics;
using StreetCorsa.Controls;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// Step 31 phase 6: the wheels drawn in the parts view and a show to hide them, and the garage's lift with its two stops:
/// a wheel comes off and goes on only with the car up, the car comes down only on its four wheels, and it goes nowhere
/// while it is up
/// </summary>
public class LiftTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private static readonly CarFacts Chevy66 = new("chevrolet", "gm", 1966, "USA");

    private static AcCarSpecs Specs() => new()
    {
        Traction = "RWD", TotalMass = 1600, FrontWeightShare = 0.55, PeakTorque = 390, FinalRatio = 3.55,
        Front = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.33, BrakeTorque = 1600, SpringRate = 30000, DampBump = 3000, DampRebound = 4000, CornerLoad = 450, SuspensionType = "DWB", SwayBarRate = 8000 },
        Rear = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.33, BrakeTorque = 900, SpringRate = 25000, DampBump = 3000, DampRebound = 4000, CornerLoad = 400, SuspensionType = "AXLE", SwayBarRate = 1300 }
    };

    private static List<PartInstance> FactoryParts()
    {
        var axle = RunningGearGuess.ChooseAxle(Catalog, Specs(), Chevy66, null)!;
        return RunningGearFactory.Create(Catalog, Specs(), 1, axle, Chevy66);
    }

    private static PartInstance Wheel(List<PartInstance> parts, int corner) => parts.Single(p => p.ParentSlot == RunningGear.WheelSlot(corner));

    // The car's front towards +Z, its left +X, the hubs 0.3 m up
    private static readonly CarAnchors Anchors = new(new Vector3(0.75f, 0.3f, 1.3f), new Vector3(-0.75f, 0.3f, 1.3f), new Vector3(0.78f, 0.3f, -1.3f), new Vector3(-0.78f, 0.3f, -1.3f));

    [Fact]
    public void The_wheels_are_drawn_on_their_hubs_their_tyres_on_them()
    {
        var parts = FactoryParts();
        var gear = parts.Where(p => RunningGear.IsWheelSlot(p.ParentSlot)).Select(p => new CarPart(p.ParentSlot, PartTrees.ToInstalled(Catalog, p)!.Root)).ToList();
        var placed = CarPartsLayout.Build(Catalog, Anchors, null, gear);

        for (var corner = 0; corner < RunningGear.Corners; corner++)
        {
            var rim = Wheel(parts, corner);
            var drawn = placed.Single(p => p.Source?.InstanceId == rim.InstanceId);

            // The middle of its tyre (the rim's tyre seat) on the hub, its outer face (-X in the stock rims) away from the car
            var seat = drawn.Part.Slots.FirstOrDefault(s => s.Id == RunningGear.TyreSlotOnRim) ?? drawn.Part.Slots.First(s => s.Id == 1);
            var middle = Vector3.Transform(new Vector3(seat.Position[0], seat.Position[1], seat.Position[2]), drawn.World);
            Assert.Equal(Anchors.Hub(corner).X, middle.X, 3);
            Assert.Equal(Anchors.Hub(corner).Y, middle.Y, 3);
            Assert.Equal(Anchors.Hub(corner).Z, middle.Z, 3);
            var outboard = Vector3.TransformNormal(-Vector3.UnitX, drawn.World);
            Assert.True(MathF.Sign(outboard.X) == MathF.Sign(Anchors.Hub(corner).X), $"corner {corner}'s wheel faces the car");

            var tyre = rim.Children.Single(c => c.ParentSlot == RunningGear.TyreSlotOnRim);
            Assert.Contains(placed, p => p.Source?.InstanceId == tyre.InstanceId);
        }
    }

    [Fact]
    public void A_loose_wheel_has_its_place_drawn_on_the_hub_too()
    {
        var parts = FactoryParts();
        var rim = Catalog.Get(Wheel(parts, 2).DefinitionId)!;
        Assert.NotNull(CarPartsLayout.CarLevelPlacement(Catalog, Anchors, rim, RunningGear.WheelSlot(2), EngineLayout.Front));
    }

    [Fact]
    public void The_wheels_have_a_show_of_their_own()
    {
        var show = PartShowToggle.ForInstalled().SingleOrDefault(s => s.Id == "wheels");
        Assert.NotNull(show);
        Assert.Equal("The wheels", show.Name);
    }

    [Fact]
    public void A_wheel_comes_off_and_goes_back_on_only_with_the_car_up()
    {
        var parts = FactoryParts();
        var wheel = Wheel(parts, 0);
        Assert.Contains("lift", CarWork.WhyNotOff(Catalog, parts, wheel, lifted: false));
        Assert.Null(CarWork.WhyNotOff(Catalog, parts, wheel, lifted: true));

        Workbench.Remove(parts, wheel);
        var place = new MountPlace(null, RunningGear.WheelSlot(0), 1);
        Assert.Contains("lift", CarWork.WhyNotOn(Catalog, parts, wheel, place, liveAxle: true, lifted: false));
        Assert.Null(CarWork.WhyNotOn(Catalog, parts, wheel, place, liveAxle: true, lifted: true));

        // A tyre on a wheel on the car the same (clicking a wheel mostly hits its tyre): off and on only with the car up
        var other = Wheel(parts, 1);
        var tyre = other.Children.Single(c => c.ParentSlot == RunningGear.TyreSlotOnRim);
        Assert.Contains("lift", CarWork.WhyNotOff(Catalog, parts, tyre, lifted: false));
        Assert.Null(CarWork.WhyNotOff(Catalog, parts, tyre, lifted: true));
        Workbench.Remove(parts, tyre);
        var onRim = new MountPlace(other, RunningGear.TyreSlotOnRim, 1);
        Assert.Contains("lift", CarWork.WhyNotOn(Catalog, parts, tyre, onRim, liveAxle: true, lifted: false));
        Assert.Null(CarWork.WhyNotOn(Catalog, parts, tyre, onRim, liveAxle: true, lifted: true));

        // What is behind the wheel waits for it as before, up or down: the car up does not take the wheel off
        var drum = RunningGear.Mounted(parts, Catalog)[1].Brake!;
        Assert.NotNull(CarWork.WhyNotOff(Catalog, parts, drum, lifted: true));
    }

    [Fact]
    public void The_car_comes_down_only_on_its_four_wheels()
    {
        var parts = FactoryParts();
        Assert.Null(CarWork.WhyNotDown(parts, hasRunningGear: true));

        Workbench.Remove(parts, Wheel(parts, 3));
        Assert.Contains("wheels back on", CarWork.WhyNotDown(parts, hasRunningGear: true));

        // A car of an older save has no wheels as parts to tell of
        Assert.Null(CarWork.WhyNotDown([], hasRunningGear: false));
    }

    [Fact]
    public void A_car_on_the_lift_goes_nowhere_and_leaves_it_when_it_changes_hands()
    {
        var car = new Car("camaro") { LiftStop = LiftStop.WheelsClear };
        Assert.Contains(CarCondition.WhyCannotRace(car, null), p => p.Contains("lift"));
        Assert.Equal(LiftStop.WheelsClear, car.Snapshot().LiftStop);

        car.ClearPlace();
        Assert.False(car.IsLifted);
        Assert.DoesNotContain(CarCondition.WhyCannotRace(car, null), p => p.Contains("lift"));
    }

    [Fact]
    public void The_lift_steps_round_its_stops_and_its_saddle_comes_up_to_the_body_before_the_car_goes_up()
    {
        Assert.Equal(LiftStop.WheelsClear, LiftLayout.Next(LiftStop.Down));
        Assert.Equal(LiftStop.Full, LiftLayout.Next(LiftStop.WheelsClear));
        Assert.Equal(LiftStop.Down, LiftLayout.Next(LiftStop.Full));

        const float under = 0.21f;
        const float down = 0.07f;
        Assert.Equal(down, LiftLayout.SaddleGoal(LiftStop.Down, under, down));
        Assert.Equal(LiftLayout.WheelsClearHeight + under, LiftLayout.SaddleGoal(LiftStop.WheelsClear, under, down), 4);
        Assert.Equal(LiftLayout.FullHeight + under, LiftLayout.SaddleGoal(LiftStop.Full, under, down), 4);

        // The car stays on its wheels while the saddle comes up to its body, then goes up with it, step for step
        Assert.Equal(0f, LiftLayout.CarHeight(down, under));
        Assert.Equal(0f, LiftLayout.CarHeight(under, under));
        Assert.Equal(0.5f, LiftLayout.CarHeight(under + 0.5f, under), 4);

        // Arms that lie on the floor higher than a low body take it at once: up from there, never the car left behind
        Assert.Equal(LiftLayout.FullHeight + 0.24f, LiftLayout.SaddleGoal(LiftStop.Full, 0.18f, 0.24f), 4);
    }

    [Fact]
    public void A_cars_body_underside_is_held_to_what_a_car_can_have()
    {
        Assert.Equal(LiftLayout.UnderBody, LiftLayout.UnderBodyOf(null));
        Assert.Equal(LiftLayout.UnderBody, LiftLayout.UnderBodyOf(float.NaN));
        Assert.Equal(0.21f, LiftLayout.UnderBodyOf(0.21f));
        Assert.Equal(LiftLayout.LowestUnderBody, LiftLayout.UnderBodyOf(-0.3f));
        Assert.Equal(LiftLayout.HighestUnderBody, LiftLayout.UnderBodyOf(2f));
    }

    [Fact]
    public void The_wheels_stay_on_the_floor_until_the_springs_are_let_out_then_hang()
    {
        Assert.Equal(0f, LiftLayout.Droop(0f));
        Assert.Equal(0.05f, LiftLayout.Droop(0.05f), 4);
        Assert.Equal(LiftLayout.WheelDrop, LiftLayout.Droop(LiftLayout.WheelsClearHeight), 4);
        Assert.Equal(LiftLayout.WheelDrop, LiftLayout.Droop(LiftLayout.FullHeight), 4);

        // At the first stop the wheels hang clear of the floor all the same
        Assert.True(LiftLayout.WheelsClearHeight - LiftLayout.Droop(LiftLayout.WheelsClearHeight) > 0.15f);
    }

    [Fact]
    public void A_wheel_taken_off_takes_the_cars_own_out_of_sight_there()
    {
        var parts = FactoryParts();
        Assert.Equal(0, ShownParts.MissingWheels(parts));

        Workbench.Remove(parts, Wheel(parts, 2));
        Assert.Equal(1 << 2, ShownParts.MissingWheels(parts));
        var shown = ShownSet.Of(null, missingWheels: ShownParts.MissingWheels(parts));
        Assert.NotNull(shown);
        Assert.NotEqual(shown, ShownSet.Of(null, missingWheels: 1));

        // A car without its running gear as parts keeps its own wheels
        Assert.Equal(0, ShownParts.MissingWheels([]));
    }

    [Fact]
    public void A_bare_corner_shows_its_brake_and_suspension_on_the_car()
    {
        var parts = FactoryParts();
        Assert.Empty(ShownCornerParts.Of(Catalog, parts, Anchors, ShownParts.MissingWheels(parts)));

        // The left rear wheel off: what the rear axle carries on that side shows, always, by the left rear hub; not the axle,
        // nor the other side's
        Workbench.Remove(parts, Wheel(parts, 2));
        var shown = ShownCornerParts.Of(Catalog, parts, Anchors, ShownParts.MissingWheels(parts));
        Assert.NotEmpty(shown);
        Assert.All(shown, s => Assert.True(s.Always));
        Assert.All(shown, s => Assert.True(s.At.Translation.X > 0 && s.At.Translation.Z < 0, $"{s.Model.Name} is not at the left rear"));
        Assert.Contains(shown, s => s.Model.Name.Contains("plate_rear"));
        Assert.DoesNotContain(shown, s => s.Model.Name.Contains("axles_gm"));
    }

    [Fact]
    public void The_lifts_own_model_has_a_post_that_rises()
    {
        var kn5 = LiftLayout.CreateModel();
        var post = kn5.RootNode.Children.Single(n => n.Name == LiftLayout.PostNode);
        Assert.NotEmpty(post.Children);
        Assert.Equal(LiftLayout.SaddleDown, post.Transform.M42, 4);
    }

    [Fact]
    public void The_lift_stands_under_the_middle_of_the_car()
    {
        var at = LiftLayout.UnderCar(Anchors, Matrix4x4.CreateTranslation(3f, 0.01f, 3f));
        Assert.Equal(0f, at.Translation.X, 3);
        Assert.Equal(0.01f, at.Translation.Y, 3);
        Assert.Equal(0f, at.Translation.Z, 3);
        Assert.Equal(1f, Vector3.TransformNormal(Vector3.UnitZ, at).Z, 3);
        Assert.Equal(1f, at.GetDeterminant(), 3);
    }

    /// <summary>The workbench with the lift, as the player goes: up, a wheel off, no coming down without it, another car</summary>
    [Fact]
    public async Task The_workbench_raises_the_car_for_a_wheel_and_lowers_it_only_with_the_wheel_back()
    {
        var state = new GameState();
        var car = new Car { DefinitionId = "camaro", Parts = FactoryParts(), HasPartsAssigned = true, HasRunningGearAssigned = true, GearVersion = RunningGearFactory.GearVersion };
        var other = new Car { DefinitionId = "camaro", Parts = FactoryParts(), HasPartsAssigned = true, HasRunningGearAssigned = true, GearVersion = RunningGearFactory.GearVersion };
        state.Player.Cars.Add(car);
        state.Player.Cars.Add(other);

        var minutes = 0;
        var workbench = new Screens.Garage.PartsWorkbenchViewModel(new CatalogParts(), state, () => { }, m =>
        {
            minutes += m;
            return Task.CompletedTask;
        });
        workbench.SetCar(car);
        for (var waited = 0; (workbench.Gear?.Count ?? 0) == 0 && waited < 10_000; waited += 20) await Task.Delay(20, TestContext.Current.CancellationToken);
        workbench.IsCarView = true;
        Assert.True(workbench.CanUseLift);

        var rim = Wheel(car.Parts, 0);
        InstalledPart Live() => workbench.Gear!.Single(g => g.CarSlot == RunningGear.WheelSlot(0)).Root;

        // On the floor the wheel stays on
        workbench.OnPartClicked(Live());
        Assert.Contains("lift", workbench.SelectedBlockedDisplay);
        Assert.False(workbench.RemoveSelectedCommand.CanExecute(null));

        // Up till the wheels hang clear, and it comes off
        workbench.StepLift();
        Assert.Equal(LiftStop.WheelsClear, car.LiftStop);
        Assert.Equal(Rules.RuleBook.Current.Time.Lift, minutes);
        Assert.Equal(string.Empty, workbench.SelectedBlockedDisplay);
        workbench.RemoveSelectedCommand.Execute(null);
        Assert.Contains(rim, state.Player.Parts);

        // Up high, and no coming down with the wheel off
        workbench.StepLift();
        Assert.Equal(LiftStop.Full, car.LiftStop);
        Assert.Contains("wheels back on", workbench.LiftTip);
        workbench.StepLift();
        Assert.Equal(LiftStop.Full, car.LiftStop);

        // Lower takes it a stop down, but not onto its hub
        workbench.LowerCommand.Execute(null);
        Assert.Equal(LiftStop.WheelsClear, car.LiftStop);
        Assert.Contains("wheels back on", workbench.LowerTip);
        workbench.LowerCommand.Execute(null);
        Assert.Equal(LiftStop.WheelsClear, car.LiftStop);
        workbench.StepLift();
        Assert.Equal(LiftStop.Full, car.LiftStop);

        // Another car in the bay: this one stays up, its wheel off; the other one, on its wheels, comes down from the
        // lower stop with Lower, and as it goes
        workbench.SetCar(other);
        Assert.Equal(LiftStop.Full, car.LiftStop);
        workbench.StepLift();
        Assert.Equal(LiftStop.WheelsClear, other.LiftStop);
        Assert.True(workbench.LowerCommand.CanExecute(null));
        workbench.LowerCommand.Execute(null);
        Assert.Equal(LiftStop.Down, other.LiftStop);
        Assert.False(workbench.LowerCommand.CanExecute(null));
        workbench.StepLift();
        Assert.Equal(LiftStop.WheelsClear, other.LiftStop);
        workbench.SetCar(car);
        Assert.Equal(LiftStop.Down, other.LiftStop);
        Assert.Equal(LiftStop.Full, car.LiftStop);
    }

    [Fact]
    public void A_car_parked_in_the_car_lot_comes_down_off_the_lift_unless_a_wheel_is_off()
    {
        var up = new Car("camaro") { HasRunningGearAssigned = true, LiftStop = LiftStop.WheelsClear };
        up.Parts.AddRange(FactoryParts());
        StreetCorsa.Services.CarLot.CarStorage.TakeOut(new Car("nova") { Warehouse = 1, Bay = 2 }, up);
        Assert.Equal(LiftStop.Down, up.LiftStop);
        Assert.Equal((1, 2), (up.Warehouse, up.Bay));

        var wheelOff = new Car("camaro") { HasRunningGearAssigned = true, LiftStop = LiftStop.Full };
        wheelOff.Parts.AddRange(FactoryParts());
        wheelOff.Parts.Remove(Wheel(wheelOff.Parts, 0));
        StreetCorsa.Services.CarLot.CarStorage.TakeOut(new Car("nova") { Warehouse = 1, Bay = 3 }, wheelOff);
        Assert.Equal(LiftStop.Full, wheelOff.LiftStop);
    }
}
