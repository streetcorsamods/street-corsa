using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// The engine stand's work order (Content/streetcorsa/parts/work_order.json) on the real engines: what kind every part
/// is, what may come off in the car and on the stand, and that every engine comes apart and goes back together.
/// </summary>
public sealed class EngineStandTests : IDisposable
{
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsFolder));

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static PartInstance Engine(string buildIdEnd)
    {
        var catalog = Catalog.Value;
        var build = catalog.EngineBuilds.Single(b => b.Id.EndsWith(buildIdEnd, StringComparison.Ordinal));
        return PartTrees.FromInstalled(PartTreeBuilder.BuildEngine(catalog, build)!.Root, PartInstance.CarEngineSlot);
    }

    private static PartInstance Find(PartInstance engine, string kind) =>
        engine.SelfAndDescendants().First(p => Catalog.Value.WorkOrder.KindOf(Catalog.Value.Get(p.DefinitionId)!) == kind);

    [Fact]
    public void Every_part_of_a_small_block_has_its_kind()
    {
        var catalog = Catalog.Value;
        var engine = Engine("camaro_mk2/0006/stock");
        var kinds = engine.SelfAndDescendants()
            .Select(p => catalog.Get(p.DefinitionId)!)
            .DistinctBy(d => d.Id)
            .ToDictionary(d => d.DisplayName ?? d.Name, d => catalog.WorkOrder.KindOf(d));

        Assert.Equal("block", kinds["GM 327-396 small block"]);
        Assert.Equal("crankshaft", kinds["GM 350 cui small block crankshaft"]);
        Assert.Equal("connecting_rods", kinds["GM 350 cui small block stock connecting rods"]);
        Assert.Equal("pistons", kinds["GM 327-396 cui small block stock pistons"]);
        Assert.Equal("damper", kinds["1963 Corvette stock harmonic damper"]);
        Assert.Equal("timing_gear", kinds["GM small block stock crankshaft timing gear (327-396)"]);
        Assert.Equal("timing_chain", kinds["GM small block stock timing chain (327-396)"]);
        Assert.Equal("timing_cover", kinds["GM small block stock timing chain cover (327-396)"]);
        Assert.Equal("water_pump", kinds["GM small block stock water pump (327-396)"]);
        Assert.Equal("fan", kinds["GM small block stock fan (327-396)"]);
        Assert.Equal("belt", kinds["GM small block stock alternator drive belt (327-396)"]);
        Assert.Equal("alternator", kinds["GM small block stock alternator (327-396)"]);
        Assert.Equal("distributor", kinds["GM 283-396 stock ignition distributor"]);
        Assert.Equal("valve_cover", kinds["MOROSO aluminium left valve cover (327-396, paintable)"]);
        Assert.Equal("cylinder_head", kinds["GM small block stock left cylinder head (350-396)"]);
        Assert.Equal("radiator", kinds["GM stock radiator (all GM, modern type)"]);
        Assert.Equal("oil_pan", kinds["GM small block stock oil pan (327-396)"]);
        Assert.Equal("transmission", kinds["Richmond Gear Super T-10 Plus 4-Speed (GM 283-396)"]);
    }

    [Fact]
    public void The_internals_stay_in_while_the_engine_is_in_the_car()
    {
        var catalog = Catalog.Value;
        var engine = Engine("camaro_mk2/0006/stock");

        var crank = Find(engine, "crankshaft");
        Assert.Equal("The crankshaft only comes out on the engine stand: pull the engine first.", EngineWork.WhyNotOff(catalog, engine, crank, onStand: false));
        Assert.Contains("only come out on the engine stand", EngineWork.WhyNotOff(catalog, engine, Find(engine, "pistons"), onStand: false));

        // Peripherals come off in the car, in their order: the belt first, the fan with nothing in its way
        Assert.Null(EngineWork.WhyNotOff(catalog, engine, Find(engine, "belt"), onStand: false));
        Assert.Equal("Take off the drive belt first.", EngineWork.WhyNotOff(catalog, engine, Find(engine, "fan"), onStand: false));
        Assert.Equal("Take off the distributor first.", EngineWork.WhyNotOff(catalog, engine, Find(engine, "intake_manifold"), onStand: false));
        Assert.Equal("Take off the clutch and the transmission first.", EngineWork.WhyNotOff(catalog, engine, Find(engine, "flywheel"), onStand: false));

        // The whole engine comes out whatever is on it
        Assert.Null(EngineWork.WhyNotOff(catalog, engine, engine, onStand: false));
    }

    [Fact]
    public void On_the_stand_the_crankshaft_waits_for_everything_on_it_and_around_it()
    {
        var catalog = Catalog.Value;
        var engine = Engine("camaro_mk2/0006/stock");
        EngineWork.StripForStand(catalog, engine);

        var why = EngineWork.WhyNotOff(catalog, engine, Find(engine, "crankshaft"), onStand: true)!;
        foreach (var name in new[] { "harmonic damper", "timing gear", "timing chain", "connecting rods", "pistons", "oil pan", "timing cover" })
            Assert.Contains("the " + name, why);
        Assert.DoesNotContain("flywheel", why);

        Assert.Equal("Take off the cylinder heads and the oil pan first.", EngineWork.WhyNotOff(catalog, engine, Find(engine, "pistons"), onStand: true));
    }

    [Fact]
    public void Bolting_an_engine_to_the_stand_takes_the_gearbox_clutch_flywheel_and_radiator_off()
    {
        var catalog = Catalog.Value;
        var engine = Engine("camaro_mk2/0006/stock");

        var taken = EngineWork.StripForStand(catalog, engine);

        // One at a time, in the order they come off a real engine; the radiator stays with the car
        Assert.Equal(new[] { "transmission", "clutch", "flywheel", "radiator" }, taken.Select(p => catalog.WorkOrder.KindOf(catalog.Get(p.DefinitionId)!)));
        Assert.All(taken, p => Assert.Empty(p.Children));
        Assert.DoesNotContain(engine.SelfAndDescendants(), p => taken.Contains(p));
    }

    [Fact]
    public void A_loose_part_goes_on_only_with_its_way_clear()
    {
        var catalog = Catalog.Value;
        var engine = Engine("camaro_mk2/0006/stock");
        EngineWork.StripForStand(catalog, engine);

        // Pistons go in on their own, not on their rods, and only with the heads off
        var rods = Find(engine, "connecting_rods");
        var crank = PartTrees.FindParent(engine, rods)!;
        crank.Children.Remove(rods);
        Assert.Equal("Take the pistons off the connecting rods first: they go in by themselves.", EngineWork.WhyNotOn(catalog, engine, rods, onStand: true));

        var pistons = rods.Children.Single();
        rods.Children.Clear();
        Assert.Equal("Take off the cylinder heads and the oil pan first.", EngineWork.WhyNotOn(catalog, engine, rods, onStand: true));
        Assert.Equal("The pistons only go in on the engine stand.", EngineWork.WhyNotOn(catalog, engine, pistons, onStand: false));
    }

    public static TheoryData<string> Builds() => new(Catalog.Value.EngineBuilds.Select(b => b.Id));

    /// <summary>
    /// On the stand, whatever may come off is taken off till the bare block is left (the rules never lock each other),
    /// then everything goes back on in the opposite order, each part allowed where it came from
    /// </summary>
    [Theory]
    [MemberData(nameof(Builds))]
    public void Every_engine_comes_apart_on_the_stand_and_goes_back_together(string buildId)
    {
        var catalog = Catalog.Value;
        var build = catalog.EngineBuilds.Single(b => b.Id == buildId);
        if (PartTreeBuilder.BuildEngine(catalog, build) is not { } tree) return;

        var engine = PartTrees.FromInstalled(tree.Root);
        EngineWork.StripForStand(catalog, engine);

        var removed = new List<(PartInstance Parent, PartInstance Part)>();
        while (engine.Children.Count > 0)
        {
            var next = engine.SelfAndDescendants().Skip(1).FirstOrDefault(p => EngineWork.WhyNotOff(catalog, engine, p, onStand: true) == null);
            if (next == null)
            {
                var stuck = engine.SelfAndDescendants().Skip(1)
                    .Select(p => $"{p.DefinitionId}: {EngineWork.WhyNotOff(catalog, engine, p, onStand: true)}");
                Assert.Fail($"Nothing more comes off {buildId}:\n" + string.Join("\n", stuck));
            }

            var parent = PartTrees.FindParent(engine, next)!;
            parent.Children.Remove(next);
            removed.Add((parent, next));
        }

        for (var i = removed.Count - 1; i >= 0; i--)
        {
            var (parent, part) = removed[i];
            Assert.Null(EngineWork.WhyNotOn(catalog, engine, part, onStand: true));
            parent.Children.Add(part);
        }
    }

    /// <summary>
    /// In the car, whatever may come off may go straight back on: a part with a stand-only one on it (a head with the
    /// camshaft in it, on some mod engine) must not leave a car it could never go back into
    /// </summary>
    [Theory]
    [MemberData(nameof(Builds))]
    public void In_the_car_whatever_comes_off_goes_back_on(string buildId)
    {
        var catalog = Catalog.Value;
        var build = catalog.EngineBuilds.Single(b => b.Id == buildId);
        if (PartTreeBuilder.BuildEngine(catalog, build) is not { } tree) return;

        var engine = PartTrees.FromInstalled(tree.Root, PartInstance.CarEngineSlot);
        foreach (var part in engine.SelfAndDescendants().Skip(1).ToList())
        {
            if (EngineWork.WhyNotOff(catalog, engine, part, onStand: false) != null) continue;

            var parent = PartTrees.FindParent(engine, part)!;
            var index = parent.Children.IndexOf(part);
            parent.Children.Remove(part);
            Assert.Null(EngineWork.WhyNotOn(catalog, engine, part, onStand: false));
            parent.Children.Insert(index, part);
        }
    }

    [Fact]
    public void A_part_in_the_way_twice_over_is_named_once()
    {
        var catalog = Catalog.Value;
        var build = catalog.EngineBuilds.First(b => b.Name == "Dodge Charger 383 '68");
        var engine = PartTrees.FromInstalled(PartTreeBuilder.BuildEngine(catalog, build)!.Root);
        EngineWork.StripForStand(catalog, engine);

        // The chain hangs on the camshaft's gear, and has to be off before the camshaft comes out besides
        var why = EngineWork.WhyNotOff(catalog, engine, Find(engine, "camshaft"), onStand: true)!;
        Assert.Single(engine.SelfAndDescendants(), p => catalog.WorkOrder.KindOf(catalog.Get(p.DefinitionId)!) == "timing_chain");
        Assert.Contains("the timing chain", why);
        Assert.DoesNotContain("timing chains", why);
    }

    /// <summary>The garage's workbench, as the player goes: pull the engine, work on it on the stand, put it back</summary>
    [Fact]
    public async Task The_workbench_pulls_an_engine_onto_the_stand_and_puts_it_back()
    {
        var catalog = Catalog.Value;
        var state = new GameState();
        var engine = Engine("camaro_mk2/0006/stock");
        var car = new Car { DefinitionId = "camaro", Parts = [engine], HasPartsAssigned = true };
        state.Player.Cars.Add(car);

        var minutes = 0;
        var workbench = new Screens.Garage.PartsWorkbenchViewModel(new CatalogParts(), state, () => { }, m =>
        {
            minutes += m;
            return Task.CompletedTask;
        });
        workbench.SetCar(car);
        for (var waited = 0; workbench.Engine == null && waited < 10_000; waited += 20) await Task.Delay(20, TestContext.Current.CancellationToken);
        workbench.IsCarView = true;

        InstalledPart Live(string kind) => workbench.Engine!.SelfAndDescendants().First(p => catalog.WorkOrder.KindOf(p.Definition) == kind);

        // In the car the crankshaft stays in
        workbench.OnPartClicked(Live("crankshaft"));
        Assert.Contains("engine stand", workbench.SelectedBlockedDisplay);
        Assert.False(workbench.RemoveSelectedCommand.CanExecute(null));

        // The engine comes out onto the stand, what is in the way of the stand's head onto the shelf
        workbench.OnPartClicked(workbench.Engine);
        Assert.Equal("Pull the engine", workbench.RemoveButtonText);
        workbench.RemoveSelectedCommand.Execute(null);
        Assert.Same(engine, state.Player.EngineStand);
        Assert.Null(car.Engine);
        Assert.Equal(4, state.Player.Parts.Count);
        Assert.Equal(120 + 4 * 5, minutes);

        // On the stand the pistons wait for the heads and the pan
        workbench.IsStandView = true;
        Assert.Equal("ENGINE STAND", workbench.EngineTitle);
        Assert.Same(engine, state.Player.EngineStand);
        workbench.OnPartClicked(Live("pistons"));
        Assert.Equal("Take off the cylinder heads and the oil pan first.", workbench.SelectedBlockedDisplay);
        workbench.OnPartClicked(Live("oil_pan"));
        Assert.Empty(workbench.SelectedBlockedDisplay);
        workbench.RemoveSelectedCommand.Execute(null);
        Assert.Equal(5, state.Player.Parts.Count);

        // The pan does not go back on the car's empty bay; the engine from the stand does
        workbench.OnPartClicked(workbench.Engine);
        Assert.True(workbench.CanInstallFromStand);
        workbench.InstallFromStandCommand.Execute(null);
        Assert.Null(state.Player.EngineStand);
        Assert.Same(engine, car.Engine);
        Assert.True(workbench.IsCarView);
    }

    [Fact]
    public void A_mod_names_its_own_kinds_and_may_change_a_rule_only_on_purpose()
    {
        _temp.File("base/work_order.json", """
            { "kinds": [ { "id": "fan", "name": "fan", "names": [ "fan" ] }, { "id": "belt", "names": [ "belt" ] } ],
              "rules": [ { "kind": "fan", "after": [ "belt" ] } ],
              "stand_strips": [ "belt" ] }
            """);
        _temp.File("mod/work_order.json", """
            { "kinds": [ { "id": "fan", "names": [ "propeller" ] }, { "id": "belt", "names": [ "fan belt" ] } ],
              "rules": [ { "kind": "fan", "after": [] }, { "kind": "belt", "after": [ "fan" ] }, null, { "kind": "fan", "after": [], "replace": true } ] }
            """);

        var problems = new List<string>();
        var order = WorkOrder.Load(new[] { ("streetcorsa", true, Path.Combine(_temp.Path, "base")), ("mymod", false, Path.Combine(_temp.Path, "mod")) }, problems);

        // The mod's words first: its "fan belt" is a belt, not a fan
        Assert.Equal("belt", order.KindOf(new PartDefinition { Name = "Big fan belt" }));
        Assert.Equal("fan", order.KindOf(new PartDefinition { Name = "Wooden propeller" }));
        Assert.Equal("fan", order.KindOf(new PartDefinition { Name = "Cooling fan" }));
        Assert.Null(order.KindOf(new PartDefinition { Name = "Fantastic widget" }));
        Assert.Equal("starter", order.KindOf(new PartDefinition { Name = "Fan", WorkKind = "starter" }));

        Assert.Empty(order.Rules.Single(r => r.Kind == "fan").After);
        Assert.Equal(new[] { "fan" }, order.Rules.Single(r => r.Kind == "belt").After);
        Assert.Equal(new[] { "belt" }, order.StandStrips);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("mymod/work_order.json: fan has a rule already", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("without a kind", StringComparison.Ordinal));
    }
}
