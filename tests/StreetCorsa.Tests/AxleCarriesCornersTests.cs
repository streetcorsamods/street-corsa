using System.Numerics;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Tests;

/// <summary>
/// The rear axle carries the rear brakes, coil springs, shocks and sway bar (the owner's "diferencial": the whole rear axle);
/// it hangs on its leaf springs, which stay on the car, and an independent rear keeps all of it on the car
/// </summary>
public class AxleCarriesCornersTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private static readonly CarFacts Chevy66 = new("chevrolet", "gm", 1966, "USA");

    private static AcCarSpecs Specs(string rear = "AXLE") => new()
    {
        Traction = "RWD", TotalMass = 1600, FrontWeightShare = 0.55, PeakTorque = 390, FinalRatio = 3.55,
        Front = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.33, BrakeTorque = 1600, SpringRate = 30000, DampBump = 3000, DampRebound = 4000, CornerLoad = 450, SuspensionType = "DWB", SwayBarRate = 8000 },
        Rear = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.33, BrakeTorque = 900, SpringRate = 25000, DampBump = 3000, DampRebound = 4000, CornerLoad = 400, SuspensionType = rear, SwayBarRate = 1300 }
    };

    private static List<PartInstance> FactoryParts(double condition = 1, string rear = "AXLE", CarFacts? car = null)
    {
        var axle = RunningGearGuess.ChooseAxle(Catalog, Specs(rear), car ?? Chevy66, null)!;
        return RunningGearFactory.Create(Catalog, Specs(rear), condition, axle, car ?? Chevy66);
    }

    private static int[] OnTheAxle(List<PartInstance> parts) =>
        parts.Single(p => p.ParentSlot == RearAxle.CarSlot).Children.Select(c => c.ParentSlot).Where(s => s >= RearAxle.LeftBrakeSlot).Order().ToArray();

    [Fact]
    public void A_car_leaves_the_factory_with_its_rear_brakes_shocks_and_bar_on_its_rear_axle_and_the_axle_on_its_leaves()
    {
        var parts = FactoryParts();
        Assert.Equal([RearAxle.LeftBrakeSlot, RearAxle.RightBrakeSlot, RearAxle.LeftShockSlot, RearAxle.RightShockSlot, RearAxle.SwayBarSlot], OnTheAxle(parts));
        Assert.DoesNotContain(parts, p => p.ParentSlot == RunningGear.BrakeSlot(2) || p.ParentSlot == RunningGear.RearSwayBarSlot);
        Assert.All(parts.Where(p => p.ParentSlot == RunningGear.SpringSlot(2) || p.ParentSlot == RunningGear.SpringSlot(3)),
            s => Assert.StartsWith("running_gear/test_leaves/", s.DefinitionId));
        Assert.Equal(2, parts.Count(p => p.ParentSlot == RunningGear.SpringSlot(2) || p.ParentSlot == RunningGear.SpringSlot(3)));
        Assert.Contains(parts, p => p.ParentSlot == RunningGear.BrakeSlot(0));

        // Read as if on the car's own corners: every corner whole, the drums with their shoes
        var corners = RunningGear.Mounted(parts, Catalog);
        Assert.All(corners, c => Assert.True(c.Brake != null && c.Spring != null && c.Shock != null && c.Rim != null));
        Assert.NotNull(corners[2].Shoes);
        Assert.NotNull(corners[2].BackingPlate);
        Assert.NotNull(RunningGear.SwayBars(parts).Rear);
    }

    [Fact]
    public void A_live_axle_on_coils_carries_its_springs_and_an_independent_rear_keeps_everything_on_the_car()
    {
        // A British car's live axle: the stock coil springs, carried on the axle
        var coils = FactoryParts(car: new CarFacts("jaguar", "jaguar", 1965, "UK"));
        Assert.Contains(RearAxle.LeftSpringSlot, OnTheAxle(coils));
        Assert.Contains(RearAxle.RightSpringSlot, OnTheAxle(coils));

        var independent = FactoryParts(rear: "DWB");
        Assert.Empty(OnTheAxle(independent));
        Assert.Contains(independent, p => p.ParentSlot == RunningGear.BrakeSlot(2));
        Assert.Contains(independent, p => p.ParentSlot == RunningGear.RearSwayBarSlot);
        Assert.Contains(independent, p => p.ParentSlot == RunningGear.ShockSlot(3));
    }

    [Fact]
    public void The_workbench_puts_a_rear_part_on_the_axle_and_a_front_one_on_the_car()
    {
        var parts = FactoryParts();
        var housing = parts.Single(p => p.ParentSlot == RearAxle.CarSlot);
        housing.Children.RemoveAll(c => c.ParentSlot == RearAxle.LeftBrakeSlot);
        parts.RemoveAll(p => p.ParentSlot == RunningGear.BrakeSlot(0));

        var (frontPlate, rearPlate) = ("running_gear/test_drums/plate_front_10", "running_gear/test_drums/plate_rear_10");
        var places = Workbench.FindPlaces(Catalog, parts, new PartInstance(rearPlate));
        Assert.Contains(places, p => p.Parent == housing && p.ParentSlot == RearAxle.LeftBrakeSlot);
        Assert.DoesNotContain(places, p => p.Parent == null && p.ParentSlot == RunningGear.BrakeSlot(2));
        Assert.Contains(Workbench.FindPlaces(Catalog, parts, new PartInstance(frontPlate)), p => p.Parent == null && p.ParentSlot == RunningGear.BrakeSlot(0));

        // A leaf spring goes on the car, never on the axle; an independent rear's brake on the car too
        parts.RemoveAll(p => p.ParentSlot == RunningGear.SpringSlot(2));
        var leaf = Workbench.FindPlaces(Catalog, parts, new PartInstance("running_gear/test_leaves/rear_150"));
        Assert.Equal([new MountPlace(null, RunningGear.SpringSlot(2), 1)], leaf);
        Assert.Contains(Workbench.FindPlaces(Catalog, parts, new PartInstance(rearPlate), liveAxle: false), p => p.Parent == null && p.ParentSlot == RunningGear.BrakeSlot(2));
    }

    [Fact]
    public void An_older_cars_rear_corners_go_onto_its_axle_once_with_what_is_on_them()
    {
        var parts = FactoryParts(0.7);
        var housing = parts.Single(p => p.ParentSlot == RearAxle.CarSlot);
        // As a car of an older save has them: on the car's own slots
        foreach (var (part, carSlot) in RearAxle.CornerParts(parts).ToList())
        {
            housing.Children.Remove(part);
            part.ParentSlot = carSlot;
            parts.Add(part);
        }

        Assert.Equal(5, RunningGearFactory.BringUpToDate(Catalog, Specs(), Chevy66, null, parts));
        Assert.Equal(5, RearAxle.CornerParts(parts).Count());
        Assert.Equal(0, RunningGearFactory.BringUpToDate(Catalog, Specs(), Chevy66, null, parts));
    }

    [Fact]
    public void A_car_whose_leaves_went_onto_its_axle_gets_them_back_on_the_car_and_an_independent_rear_all_of_it()
    {
        var parts = FactoryParts();
        var housing = parts.Single(p => p.ParentSlot == RearAxle.CarSlot);
        foreach (var leaf in parts.Where(p => p.ParentSlot == RunningGear.SpringSlot(2) || p.ParentSlot == RunningGear.SpringSlot(3)).ToList())
        {
            parts.Remove(leaf);
            leaf.ParentSlot = RearAxle.HousingSlotFor(leaf.ParentSlot)!.Value;
            housing.Children.Add(leaf);
        }

        Assert.Equal(2, RunningGearFactory.HangOnAxle(Catalog, parts, liveAxle: true));
        Assert.DoesNotContain(RearAxle.LeftSpringSlot, OnTheAxle(parts));
        Assert.Equal(5, RunningGearFactory.HangOnAxle(Catalog, parts, liveAxle: false));
        Assert.Empty(OnTheAxle(parts));
    }

    [Fact]
    public void A_stock_car_races_the_same_with_its_rear_corners_on_the_axle()
    {
        var parts = FactoryParts();
        var problems = new List<string>();
        var files = AcRunningGearData.Generate(Catalog, RunningGear.Mounted(parts), RunningGearFactory.Choose(Catalog, Specs(), Chevy66)!.Value,
            name => name == "suspensions.ini" ? "[FRONT]\nTYPE=DWB\nSPRING_RATE=30000\n\n[REAR]\nTYPE=AXLE\nSPRING_RATE=25000\n\n[ARB]\nFRONT=8000\nREAR=1300\n" : null,
            problems, RunningGear.SwayBars(parts));
        Assert.Empty(problems);
        Assert.DoesNotContain("suspensions.ini", files.Keys);
    }

    [Fact]
    public void A_drum_on_the_axle_is_drawn_where_it_would_be_on_the_car()
    {
        var anchors = new CarAnchors(new Vector3(0.75f, 0.3f, -1.3f), new Vector3(-0.75f, 0.3f, -1.3f), new Vector3(0.78f, 0.3f, 1.3f), new Vector3(-0.78f, 0.3f, 1.3f));
        var parts = FactoryParts();
        var housing = parts.Single(p => p.ParentSlot == RearAxle.CarSlot);
        var drum = housing.Children.Single(c => c.ParentSlot == RearAxle.RightBrakeSlot);

        var tree = PartTrees.ToInstalled(Catalog, housing)!;
        var placed = CarPartsLayout.Build(Catalog, anchors, null, [new CarPart(RearAxle.CarSlot, tree.Root)]);
        var onAxle = placed.Single(p => p.Source?.Definition.Id == drum.DefinitionId && p.Source.ParentSlot == RearAxle.RightBrakeSlot).World;
        var onCar = CarPartsLayout.CarLevelPlacement(Catalog, anchors, Catalog.Get(drum.DefinitionId)!, RunningGear.BrakeSlot(3), EngineLayout.Front)!.Value;
        Assert.Equal(onCar.Translation.X, onAxle.Translation.X, 3);
        Assert.Equal(onCar.Translation.Z, onAxle.Translation.Z, 3);
        Assert.Equal(onCar.M11, onAxle.M11, 3);
    }

    /// <summary>The save loaded: what the rear axle carries stays on it (the plates, the shocks, the sway bar came off)</summary>
    [Fact]
    public void A_saved_axle_keeps_what_it_carries_when_the_save_is_loaded()
    {
        var parts = FactoryParts();
        var before = parts.SelectMany(p => p.SelfAndDescendants()).Select(p => (p.DefinitionId, p.ParentSlot, p.OwnSlot)).ToList();

        var loose = new List<PartInstance>();
        foreach (var root in parts) SavedParts.BringUpToDate(Catalog, root, loose);

        Assert.Empty(loose.Select(p => p.DefinitionId));
        Assert.Equal(before, parts.SelectMany(p => p.SelfAndDescendants()).Select(p => (p.DefinitionId, p.ParentSlot, p.OwnSlot)).ToList());
    }
}
