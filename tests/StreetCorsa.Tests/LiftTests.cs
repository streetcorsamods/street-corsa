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

            // The middle of its tyre (the rim's tyre seat) on the hub, its outer face (+X) away from the car
            var seat = drawn.Part.Slots.FirstOrDefault(s => s.Id == RunningGear.TyreSlotOnRim) ?? drawn.Part.Slots.First(s => s.Id == 1);
            var middle = Vector3.Transform(new Vector3(seat.Position[0], seat.Position[1], seat.Position[2]), drawn.World);
            Assert.Equal(Anchors.Hub(corner).X, middle.X, 3);
            Assert.Equal(Anchors.Hub(corner).Y, middle.Y, 3);
            Assert.Equal(Anchors.Hub(corner).Z, middle.Z, 3);
            var outboard = Vector3.TransformNormal(Vector3.UnitX, drawn.World);
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
    public void The_lift_steps_round_its_stops_and_its_saddle_catches_the_car_by_the_first()
    {
        Assert.Equal(LiftStop.WheelsClear, LiftLayout.Next(LiftStop.Down));
        Assert.Equal(LiftStop.Full, LiftLayout.Next(LiftStop.WheelsClear));
        Assert.Equal(LiftStop.Down, LiftLayout.Next(LiftStop.Full));

        Assert.Equal(LiftLayout.SaddleDown, LiftLayout.SaddleTop(0f));
        Assert.Equal(LiftLayout.WheelsClearHeight + LiftLayout.UnderBody, LiftLayout.SaddleTop(LiftLayout.WheelsClearHeight), 4);
        Assert.Equal(LiftLayout.FullHeight + LiftLayout.UnderBody, LiftLayout.SaddleTop(LiftLayout.FullHeight), 4);

        // Up all the way without a jump
        var last = LiftLayout.SaddleTop(0f);
        for (var h = 0.01f; h <= LiftLayout.FullHeight; h += 0.01f)
        {
            var top = LiftLayout.SaddleTop(h);
            Assert.InRange(top - last, 0f, 0.02f);
            last = top;
        }
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
        workbench.IsLifted = true;
        Assert.Equal(LiftStop.WheelsClear, car.LiftStop);
        Assert.Equal(Rules.RuleBook.Current.Time.Lift, minutes);
        Assert.Equal(string.Empty, workbench.SelectedBlockedDisplay);
        workbench.RemoveSelectedCommand.Execute(null);
        Assert.Contains(rim, state.Player.Parts);

        // Up high, and no coming down with the wheel off
        workbench.IsLifted = false;
        Assert.Equal(LiftStop.Full, car.LiftStop);
        Assert.Contains("wheels back on", workbench.LiftTip);
        workbench.IsLifted = false;
        Assert.Equal(LiftStop.Full, car.LiftStop);

        // Another car in the bay: this one stays up, its wheel off; the other one, on its wheels, comes down as it goes
        workbench.SetCar(other);
        Assert.Equal(LiftStop.Full, car.LiftStop);
        workbench.IsLifted = true;
        Assert.Equal(LiftStop.WheelsClear, other.LiftStop);
        workbench.SetCar(car);
        Assert.Equal(LiftStop.Down, other.LiftStop);
        Assert.Equal(LiftStop.Full, car.LiftStop);
    }
}
