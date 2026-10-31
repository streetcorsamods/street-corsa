using Newtonsoft.Json;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// The engine of specs boosted by a charger wherever it hangs: off one header of a vee, on the block, carrying the
/// fuel system that blows through it. The chargers are the engine's, not a bank's.
/// </summary>
public class EngineChargerTests
{
    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private const int TurboSlot = 90;
    private const int FuelSlot = 91;

    private static readonly PartDefinition Turbo = JsonConvert.DeserializeObject<PartDefinition>($$"""
        { "id": "engines/test/turbo", "class": "turbocharger", "mass": 12,
          "specs": { "boost": 0.6, "wastegate": 0.6, "boost_rpm": 3500, "boost_rpm_range": 2500 },
          "slots": [ { "id": 1, "name": "mount" }, { "id": {{FuelSlot}}, "name": "fuel" } ] }
        """)!;

    private static PartTree Build(string id) => PartTreeBuilder.BuildEngine(Catalog, Catalog.EngineBuilds.Single(b => b.Id == id))!;

    /// <summary>The part's definition with one slot more, and the part in its place on that copy with all it carried</summary>
    private static InstalledPart WithSlot(InstalledPart part, int slot)
    {
        var definition = JsonConvert.DeserializeObject<PartDefinition>(JsonConvert.SerializeObject(part.Definition))!;
        definition.Slots.Add(new PartSlot { Id = slot, Name = "added" });
        var copy = new InstalledPart(definition);
        foreach (var (at, child) in part.Children.ToList()) copy.Mount(at, child, child.OwnSlot);
        part.Parent?.Mount(part.ParentSlot, copy, part.OwnSlot);
        return copy;
    }

    private static InstalledPart LeftHeader(PartTree tree) =>
        tree.Root.SelfAndDescendants().Single(p => p.Definition.Id == "engines/test_v8/left_header");

    [Fact]
    public void One_turbo_off_a_vees_left_header_boosts_the_whole_engine()
    {
        var tree = Build("factory/test-v8");
        WithSlot(LeftHeader(tree), TurboSlot).Mount(TurboSlot, new InstalledPart(Turbo), 1);

        var inputs = EngineAssembly.Of(tree.Root).Inputs;

        Assert.Equal(0.6, inputs.BoostMax, 6);
        Assert.Equal(0.6, inputs.BoostWastegate, 6);
        Assert.True(EngineEvaluator.Evaluate(Catalog, tree).Dyno!.MaxTorque > EngineEvaluator.Evaluate(Catalog, Build("factory/test-v8")).Dyno!.MaxTorque);
    }

    [Fact]
    public void Turbos_off_both_headers_boost_as_one_does_and_the_bigger_of_two_sets_it()
    {
        var tree = Build("factory/test-v8");
        WithSlot(LeftHeader(tree), TurboSlot).Mount(TurboSlot, new InstalledPart(Turbo), 1);
        var bigger = JsonConvert.DeserializeObject<PartDefinition>(JsonConvert.SerializeObject(Turbo))!;
        bigger.Specs["boost"] = 0.9;
        bigger.Specs["wastegate"] = 0.8;
        var right = tree.Root.SelfAndDescendants().Single(p => p.Definition.Id == "engines/test_v8/right_header");
        WithSlot(right, TurboSlot).Mount(TurboSlot, new InstalledPart(bigger), 1);

        var inputs = EngineAssembly.Of(tree.Root).Inputs;

        Assert.Equal(0.9, inputs.BoostMax, 6);
        Assert.Equal(0.8, inputs.BoostWastegate, 6);
    }

    [Fact]
    public void A_carburettor_blowing_through_a_turbo_on_the_block_feeds_the_engine()
    {
        var tree = Build("factory/test-v8");
        var intake = tree.Root.Children[tree.Root.RoleSlot(EngineAssembly.Roles.IntakeManifold)];
        var (carbSlot, carb) = intake.Children.Single(c => c.Value.Is(PartClasses.FuelSystem));
        Assert.True(intake.Children.Remove(carbSlot));
        var bare = EngineAssembly.Of(tree.Root).Inputs;

        var block = WithSlot(tree.Root, TurboSlot);
        var turbo = new InstalledPart(Turbo);
        block.Mount(TurboSlot, turbo, 1);
        turbo.Mount(FuelSlot, carb, carb.OwnSlot);
        var fed = EngineAssembly.Of(block).Inputs;

        Assert.Equal(0, bare.MaxFuelFlow);
        Assert.True(fed.MaxFuelFlow > 0);
        Assert.Equal(0.6, fed.BoostMax, 6);
    }

    [Fact]
    public void The_charger_that_makes_the_most_past_its_wastegate_sets_the_boost()
    {
        var tree = Build("factory/test-v8");
        var held = JsonConvert.DeserializeObject<PartDefinition>(JsonConvert.SerializeObject(Turbo))!;
        held.Specs["boost"] = 1.5;
        held.Specs["wastegate"] = 0.5;
        WithSlot(LeftHeader(tree), TurboSlot).Mount(TurboSlot, new InstalledPart(held), 1);
        var free = JsonConvert.DeserializeObject<PartDefinition>(JsonConvert.SerializeObject(Turbo))!;
        free.Specs["boost"] = 1.0;
        free.Specs["wastegate"] = 1.0;
        var right = tree.Root.SelfAndDescendants().Single(p => p.Definition.Id == "engines/test_v8/right_header");
        WithSlot(right, TurboSlot).Mount(TurboSlot, new InstalledPart(free), 1);

        var inputs = EngineAssembly.Of(tree.Root).Inputs;

        Assert.Equal(1.0, inputs.BoostMax, 6);
        Assert.Equal(1.0, inputs.BoostWastegate, 6);
        Assert.True(inputs.ChargerTurbo);
        Assert.True(EngineEvaluator.Evaluate(Catalog, tree).Turbocharged);
    }

    [Fact]
    public void A_blower_that_outboosts_a_turbo_is_no_turbo_in_AC()
    {
        var tree = Build("kit/test-v8-blown");
        WithSlot(LeftHeader(tree), TurboSlot).Mount(TurboSlot, new InstalledPart(Turbo), 1);

        var inputs = EngineAssembly.Of(tree.Root).Inputs;

        Assert.Equal(1.15, inputs.BoostMax, 6);
        Assert.False(inputs.ChargerTurbo);
        Assert.False(EngineEvaluator.Evaluate(Catalog, tree).Turbocharged);
    }

    [Fact]
    public void The_blower_on_the_intake_boosts_as_it_did()
    {
        var inputs = EngineAssembly.Of(Build("kit/test-v8-blown").Root).Inputs;

        Assert.Equal(1.15, inputs.BoostMax, 6);
        Assert.Equal(2.5, inputs.TurboRpmMultiplier, 6);
    }
}
