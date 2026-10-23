using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services.PartShops;

namespace StreetCorsa.Tests;

/// <summary>Step 31 phase 3: leaf springs and their traction bars, torsion bars, sway bars, period shocks</summary>
public class SpringsAndBarsTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private const string Leaf = "running_gear/springs_leaf/";
    private const string Torsion = "running_gear/springs_torsion/";
    private const string Sway = "running_gear/sway_bars/";
    private const string Shocks = "running_gear/shocks_period/";

    /// <summary>A car on a live axle at the rear, double wishbones in front, a front sway bar and none at the rear</summary>
    private static AcCarSpecs Specs(double mass = 1600, string front = "DWB", string rear = "AXLE", double rearBar = 0) => new()
    {
        Traction = "RWD", TotalMass = mass, FrontWeightShare = 0.55,
        Front = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.33, BrakeTorque = 1600, SpringRate = 30000, DampBump = 3000, DampRebound = 4000, CornerLoad = 450, SuspensionType = front, SwayBarRate = 8000 },
        Rear = new AcAxleSpecs { TyreWidth = 0.205, TyreRadius = 0.33, BrakeTorque = 900, SpringRate = 25000, DampBump = 3000, DampRebound = 4000, CornerLoad = 400, SuspensionType = rear, SwayBarRate = rearBar }
    };

    private static readonly CarFacts Chevy66 = new("chevrolet", "gm", 1966, "USA");
    private static readonly CarFacts Dodge68 = new("dodge", "mopar", 1968, "USA");

    [Fact]
    public void A_live_axle_rides_on_the_leaf_nearest_its_own_rate_and_a_mopar_on_torsion_bars_by_its_weight()
    {
        var (front, rear) = RunningGearFactory.Choose(Catalog, Specs(), Chevy66)!.Value;
        Assert.Equal(Leaf + "rear_150", rear.Spring.Id);
        Assert.Null(front.Spring.CameIn);

        Assert.Equal(Torsion + "bar_92", RunningGearFactory.Choose(Catalog, Specs(1850), Dodge68)!.Value.Front.Spring.Id);
        Assert.Equal(Torsion + "bar_85", RunningGearFactory.Choose(Catalog, Specs(1150), Dodge68)!.Value.Front.Spring.Id);

        // Not on a strut front, not on a rear that is not a live axle, not on a car from elsewhere
        Assert.Null(RunningGearFactory.Choose(Catalog, Specs(1850, front: "STRUT"), Dodge68)!.Value.Front.Spring.CameIn);
        Assert.Null(RunningGearFactory.Choose(Catalog, Specs(rear: "DWB"), Chevy66)!.Value.Rear.Spring.CameIn);
        Assert.Null(RunningGearFactory.Choose(Catalog, Specs(), new CarFacts("jaguar", "jaguar", 1965, "UK"))!.Value.Rear.Spring.CameIn);

        // The aftermarket ones are never a car's factory parts
        var old = RunningGearFactory.Choose(Catalog, Specs())!.Value;
        Assert.DoesNotContain("running_gear/", old.Front.Shock.Id + old.Rear.Shock.Id + old.Front.Spring.Id + old.Rear.Spring.Id);
    }

    [Fact]
    public void The_factory_sway_bars_are_the_nearest_to_the_cars_own_and_none_where_it_has_none()
    {
        var (front, rear) = RunningGearFactory.Choose(Catalog, Specs(), Chevy66)!.Value;
        Assert.Equal(Sway + "front_8750", front.SwayBar!.Id);
        Assert.Null(rear.SwayBar);
        Assert.Equal(Sway + "rear_6875", RunningGearFactory.Choose(Catalog, Specs(rearBar: 1300), Chevy66)!.Value.Rear.SwayBar!.Id);

        var parts = RunningGearFactory.Create(Catalog, Specs(), 1, null, Chevy66);
        Assert.Equal((parts.Single(p => p.ParentSlot == RunningGear.FrontSwayBarSlot), null), RunningGear.SwayBars(parts));
    }

    // ----- Assetto Corsa -----

    private const string SuspensionsIni = "[FRONT]\nTYPE=DWB\nSPRING_RATE=30000\n\n[REAR]\nTYPE=AXLE\nSPRING_RATE=25000\n\n[AXLE]\nTORQUE_REACTION=-0.5\n\n[ARB]\nFRONT=8000\nREAR=0\n";

    private static Dictionary<string, string> Export(Action<List<PartInstance>>? change, List<string> problems, string suspensions = SuspensionsIni, string rear = "AXLE")
    {
        var specs = Specs(rear: rear);
        var parts = RunningGearFactory.Create(Catalog, specs, 1, null, Chevy66);
        change?.Invoke(parts);
        return AcRunningGearData.Generate(Catalog, RunningGear.Mounted(parts), RunningGearFactory.Choose(Catalog, specs, Chevy66)!.Value,
            name => name == "suspensions.ini" ? suspensions : null, problems, RunningGear.SwayBars(parts));
    }

    private static void OnRearSprings(List<PartInstance> parts, Action<PartInstance> change) =>
        parts.Where(p => p.ParentSlot == RunningGear.SpringSlot(2) || p.ParentSlot == RunningGear.SpringSlot(3)).ToList().ForEach(change);

    [Fact]
    public void A_stock_car_keeps_its_suspension_as_its_author_wrote_it()
    {
        var problems = new List<string>();
        Assert.DoesNotContain("suspensions.ini", Export(null, problems).Keys);
        Assert.Empty(problems);
    }

    [Fact]
    public void Traction_bars_and_stiffer_leaves_hold_the_axle_still_and_a_mono_leaf_winds_up_more()
    {
        var problems = new List<string>();
        var bars = new IniText(Export(parts => OnRearSprings(parts, s => s.Children.Add(new PartInstance(Leaf + "ladder") { ParentSlot = RunningGear.TractionBarSlot, OwnSlot = 1 })),
            problems)["suspensions.ini"]);
        Assert.Equal(-0.125, bars.GetNumber("AXLE", "TORQUE_REACTION"));

        var mono = new IniText(Export(parts => OnRearSprings(parts, s => s.DefinitionId = Leaf + "mono_110"), problems)["suspensions.ini"]);
        Assert.Equal(-0.65, mono.GetNumber("AXLE", "TORQUE_REACTION"));
        Assert.Equal(Math.Round(25000 * 110 * 175.1 / Math.Round(150 * 175.1)), mono.GetNumber("REAR", "SPRING_RATE"));
        Assert.Empty(problems);
    }

    [Fact]
    public void A_sway_bar_moves_the_cars_own_by_its_ratio_adds_its_rate_where_there_was_none_and_leaves_none_taken_off()
    {
        var problems = new List<string>();
        var front = Catalog.Get(Sway + "front_8750")!;
        var bigger = Catalog.Get(Sway + "front_11250")!;
        var rearBar = Catalog.Get(Sway + "rear_7500")!;

        var ini = new IniText(Export(parts =>
        {
            parts.Single(p => p.ParentSlot == RunningGear.FrontSwayBarSlot).DefinitionId = bigger.Id;
            parts.Add(new PartInstance(rearBar.Id) { ParentSlot = RunningGear.RearSwayBarSlot, OwnSlot = 1 });
        }, problems)["suspensions.ini"]);
        Assert.Equal(Math.Round(8000 * RunningGear.SwayBarRate(bigger) / RunningGear.SwayBarRate(front)), ini.GetNumber("ARB", "FRONT"));
        Assert.Equal(RunningGear.SwayBarRate(rearBar), ini.GetNumber("ARB", "REAR"));

        var off = new IniText(Export(parts => parts.RemoveAll(p => p.ParentSlot == RunningGear.FrontSwayBarSlot), problems)["suspensions.ini"]);
        Assert.Equal(0, off.GetNumber("ARB", "FRONT"));
        Assert.Empty(problems);
    }

    [Fact]
    public void A_leaf_spring_under_a_car_whose_rear_is_not_a_live_axle_keeps_it_home()
    {
        var problems = new List<string>();
        Export(parts => OnRearSprings(parts, s => s.DefinitionId = Leaf + "rear_150"), problems, SuspensionsIni.Replace("TYPE=AXLE", "TYPE=DWB"), rear: "DWB");
        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Contains("does not fit this car's suspension", p));
    }

    // ----- the workbench, the shops, older saves -----

    [Fact]
    public void Each_goes_on_the_axle_it_is_made_for_and_a_traction_bar_on_a_leaf()
    {
        var parts = RunningGearFactory.Create(Catalog, Specs(), 1, null, Chevy66);
        parts.RemoveAll(p => RunningGear.CornerOf(p.ParentSlot) >= 0 && p.ParentSlot >= 301 && p.ParentSlot < 315);
        parts.RemoveAll(p => RunningGear.IsSwayBarSlot(p.ParentSlot));

        Slots(parts, Leaf + "rear_150", RunningGear.SpringSlot(2), RunningGear.SpringSlot(3));
        Slots(parts, Torsion + "bar_89", RunningGear.SpringSlot(0), RunningGear.SpringSlot(1));
        Slots(parts, Shocks + "drag_90_10", RunningGear.ShockSlot(0), RunningGear.ShockSlot(1));
        Slots(parts, Sway + "front_8750", RunningGear.FrontSwayBarSlot);
        Slots(parts, Sway + "rear_7500", RunningGear.RearSwayBarSlot);

        var leaf = new PartInstance(Leaf + "rear_150") { ParentSlot = RunningGear.SpringSlot(2), OwnSlot = 1 };
        parts.Add(leaf);
        Assert.Contains(Workbench.FindPlaces(Catalog, parts, new PartInstance(Leaf + "slapper")), p => p.Parent == leaf && p.ParentSlot == RunningGear.TractionBarSlot);
    }

    private static void Slots(List<PartInstance> parts, string id, params int[] expected) =>
        Assert.Equal(expected, Workbench.FindPlaces(Catalog, parts, new PartInstance(id)).Where(p => p.Parent == null).Select(p => p.ParentSlot).Order());

    [Fact]
    public void Nobody_sells_the_source_games_sway_bars_that_do_nothing()
    {
        var wheels = new PartShopDefinition { Kind = "wheels" };
        // A bar of no rate, as the source game's stock bars were (gone with their models, 2026-10-22; a mod may have one)
        var working = Catalog.Get(Sway + "front_11250")!;
        var inert = new PartDefinition { Id = "suspension/stock/sway_bar", Class = working.Class, Slots = working.Slots };
        Assert.Equal(PartKinds.SwayBars, PartKinds.GroupOf(inert));
        Assert.False(PartShopRules.Deals(wheels, inert, new HashSet<string>()));
        Assert.True(PartShopRules.Deals(wheels, Catalog.Get(Sway + "front_11250")!, new HashSet<string>()));
    }

    [Fact]
    public void An_older_car_gets_its_leaves_and_its_sway_bar_once()
    {
        var specs = Specs();
        var parts = RunningGearFactory.Create(Catalog, specs, 0.7);
        parts.RemoveAll(p => RunningGear.IsSwayBarSlot(p.ParentSlot));

        Assert.Equal(7, RunningGearFactory.BringUpToDate(Catalog, specs, Chevy66, null, parts));
        var bar = parts.Single(p => p.ParentSlot == RunningGear.FrontSwayBarSlot);
        Assert.Equal(Sway + "front_8750", bar.DefinitionId);
        Assert.Equal(0.7, bar.Wear, 6);
        Assert.Equal(0, RunningGearFactory.BringUpToDate(Catalog, specs, Chevy66, null, parts));
    }

    [Fact]
    public void A_sway_bar_the_player_took_off_is_not_given_back_to_a_car_that_had_its_bars()
    {
        var specs = Specs();
        var parts = RunningGearFactory.Create(Catalog, specs, 0.7);
        parts.RemoveAll(p => RunningGear.IsSwayBarSlot(p.ParentSlot));

        RunningGearFactory.BringUpToDate(Catalog, specs, Chevy66, null, parts, fromVersion: 2);
        Assert.DoesNotContain(parts, p => RunningGear.IsSwayBarSlot(p.ParentSlot));
    }
}
