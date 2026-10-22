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
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsRoots));
    private static readonly Lazy<EngineBuildIndex> Rated = new(() => EngineBuildIndex.Create(Catalog.Value));

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
        var engine = Engine("factory/test-v8");
        var parts = engine.SelfAndDescendants().Select(p => catalog.Get(p.DefinitionId)!).DistinctBy(d => d.Id).ToList();

        // The part on this engine whose id ends so, in whichever pack it is
        string? Kind(string localId) =>
            catalog.WorkOrder.KindOf(Assert.Single(parts, d => d.Id.EndsWith("/" + localId, StringComparison.Ordinal)));

        Assert.Equal("block", Kind("block"));
        Assert.Equal("crankshaft", Kind("crankshaft"));
        Assert.Equal("connecting_rods", Kind("connecting_rods"));
        Assert.Equal("pistons", Kind("pistons"));
        Assert.Equal("damper", Kind("harmonic_damper"));
        Assert.Equal("timing_gear", Kind("crankshaft_timing_gear"));
        Assert.Equal("timing_chain", Kind("timing_chain"));
        Assert.Equal("timing_cover", Kind("timing_cover"));
        Assert.Equal("water_pump", Kind("water_pump"));
        Assert.Equal("fan", Kind("fan"));
        Assert.Equal("belt", Kind("drive_belt"));
        Assert.Equal("alternator", Kind("alternator"));
        Assert.Equal("distributor", Kind("distributor"));
        Assert.Equal("valve_cover", Kind("left_valve_cover"));
        Assert.Equal("cylinder_head", Kind("left_head"));
        Assert.Equal("radiator", Kind("radiator"));
        Assert.Equal("oil_pan", Kind("oil_pan"));
        Assert.Equal("transmission", Kind("transmission"));
    }

    [Fact]
    public void The_internals_stay_in_while_the_engine_is_in_the_car()
    {
        var catalog = Catalog.Value;
        var engine = Engine("factory/test-v8");

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
        var engine = Engine("factory/test-v8");
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
        var engine = Engine("factory/test-v8");

        var taken = EngineWork.StripForStand(catalog, engine);

        // One at a time, in the order they come off a real engine; the radiator stays with the car
        Assert.Equal(new[] { "radiator", "starter", "transmission", "clutch", "flywheel" }, taken.Select(p => catalog.WorkOrder.KindOf(catalog.Get(p.DefinitionId)!)));
        Assert.All(taken, p => Assert.Empty(p.Children));
        Assert.DoesNotContain(engine.SelfAndDescendants(), p => taken.Contains(p));
    }

    /// <summary>
    /// The inline six: its distributor is on the side of the block, not through the intake; its intake manifold comes off to
    /// the side of the head and its alternator upwards; the timing cover and the water pump come off each on their own; the
    /// alternator hangs on the water pump; the starter is bolted to the bellhousing
    /// </summary>
    [Fact]
    public void The_ford_six_comes_apart_in_its_own_order()
    {
        var catalog = Catalog.Value;
        var engine = Engine("factory/test-six");

        Assert.Equal("side_distributor", catalog.WorkOrder.KindOf(catalog.Get("engines/test_six/distributor")!));
        // The intake and the alternator stay of their kinds, rules and all; only the way they come off is their own
        Assert.Null(EngineWork.WhyNotOff(catalog, engine, Find(engine, "intake_manifold"), onStand: false));
        Assert.Equal(new[] { PartWay.Out }, catalog.WorkOrder.WayOf(catalog.Get(Find(engine, "intake_manifold").DefinitionId)!));
        Assert.Contains("the intake manifold", EngineWork.WhyNotOff(catalog, engine, Find(engine, "cylinder_head"), onStand: false));
        Assert.Equal(new[] { PartWay.Up }, catalog.WorkOrder.WayOf(catalog.Get(Find(engine, "alternator").DefinitionId)!));
        Assert.Equal("Take off the crankshaft pulley first.", EngineWork.WhyNotOff(catalog, engine, Find(engine, "timing_cover"), onStand: false));
        Assert.Contains("the alternator", EngineWork.WhyNotOff(catalog, engine, Find(engine, "water_pump"), onStand: false));
        Assert.Equal("Take off the starter first.", EngineWork.WhyNotOff(catalog, engine, Find(engine, "transmission"), onStand: false));
        Assert.Equal(new[] { PartWay.Forward }, catalog.WorkOrder.WayOf(catalog.Get(Find(engine, "fan").DefinitionId)!));

        var taken = EngineWork.StripForStand(catalog, engine);
        Assert.Equal(new[] { "radiator", "starter", "transmission", "clutch", "flywheel" }, taken.Select(p => catalog.WorkOrder.KindOf(catalog.Get(p.DefinitionId)!)));

        // What came off for the stand does not go back on while the engine is on it
        var gearbox = taken.Single(p => catalog.WorkOrder.KindOf(catalog.Get(p.DefinitionId)!) == "transmission");
        Assert.Equal("The transmission goes on with the engine in the car, not on the stand.", EngineWork.WhyNotOn(catalog, engine, gearbox, onStand: true));
        Assert.Null(EngineWork.WhyNotOn(catalog, engine, taken.Single(p => catalog.WorkOrder.KindOf(catalog.Get(p.DefinitionId)!) == "flywheel"), onStand: false));

        // The rods have bearings of their own: the main bearing bridge only holds the crankshaft
        foreach (var kind in new[] { "cylinder_head", "oil_pan" })
        {
            var part = Find(engine, kind);
            PartTrees.FindParent(engine, part)!.Children.Remove(part);
        }

        Assert.Contains(engine.SelfAndDescendants(), p => catalog.WorkOrder.KindOf(catalog.Get(p.DefinitionId)!) == "bearing_bridge");
        Assert.DoesNotContain("bearing", EngineWork.WhyNotOff(catalog, engine, Find(engine, "connecting_rods"), onStand: true) ?? string.Empty);
        Assert.Contains("main bearing bridge", EngineWork.WhyNotOff(catalog, engine, Find(engine, "crankshaft"), onStand: true));
    }

    /// <summary>
    /// A head waits for the valve cover and the exhaust header on it, not for the other head's: of a V8 one head comes
    /// off (and goes back on) with the other bank still dressed
    /// </summary>
    [Fact]
    public void A_cylinder_head_waits_only_for_its_own_valve_cover_and_header()
    {
        var catalog = Catalog.Value;
        var engine = Engine("factory/test-v8");
        string? KindOf(PartInstance p) => catalog.WorkOrder.KindOf(catalog.Get(p.DefinitionId)!);
        void Remove(PartInstance p) => PartTrees.FindParent(engine, p)!.Children.Remove(p);

        Remove(Find(engine, "intake_manifold"));
        var heads = engine.SelfAndDescendants().Where(p => KindOf(p) == "cylinder_head").ToList();
        Assert.Equal(2, heads.Count);
        var (head, other) = (heads[0], heads[1]);
        Assert.Equal("Take off the valve cover and the exhaust header first.", EngineWork.WhyNotOff(catalog, engine, head, onStand: false));

        foreach (var dressing in head.Children.Where(p => KindOf(p) is "valve_cover" or "exhaust_header").ToList()) head.Children.Remove(dressing);
        Assert.Contains(other.Children, p => KindOf(p) == "valve_cover");
        Assert.Contains(other.Children, p => KindOf(p) == "exhaust_header");
        Assert.Null(EngineWork.WhyNotOff(catalog, engine, head, onStand: false));

        Remove(head);
        Assert.Null(EngineWork.WhyNotOn(catalog, engine, head, onStand: false));
    }

    /// <summary>The belt runs on the fan's pulley: it goes on after the water pump and the fan, and holds them on</summary>
    [Fact]
    public void The_drive_belt_needs_the_water_pump_and_the_fan()
    {
        var catalog = Catalog.Value;
        var engine = Engine("factory/test-six");

        var belt = Find(engine, "belt");
        PartTrees.FindParent(engine, belt)!.Children.Remove(belt);
        var fan = Find(engine, "fan");
        Assert.Null(EngineWork.WhyNotOff(catalog, engine, fan, onStand: false));
        var pump = PartTrees.FindParent(engine, fan)!;
        pump.Children.Remove(fan);
        Assert.Equal("Put on the fan first.", EngineWork.WhyNotOn(catalog, engine, belt, onStand: false));

        Assert.Contains("the alternator", EngineWork.WhyNotOff(catalog, engine, pump, onStand: false));
        var alternator = Find(engine, "alternator");
        PartTrees.FindParent(engine, alternator)!.Children.Remove(alternator);
        PartTrees.FindParent(engine, pump)!.Children.Remove(pump);
        // No place for a fan without the pump: the pump first, then the fan
        Assert.Equal("Put on the water pump first.", EngineWork.WhyNotOn(catalog, engine, belt, onStand: false));
    }

    /// <summary>
    /// An engine that comes into the game with a belt and nothing for it to run on (a build short of its water pump,
    /// a save from before the fan was needed) has the belt taken off: it could come off but never go back on
    /// </summary>
    [Fact]
    public void A_belt_with_nothing_to_run_on_comes_off_as_the_engine_comes_into_the_game()
    {
        var catalog = Catalog.Value;

        // The build is short of its fan: the belt runs on nothing and comes off
        var engine = Engine("factory/test-v8-no-fan");
        Assert.Contains(engine.SelfAndDescendants(), p => catalog.WorkOrder.KindOf(catalog.Get(p.DefinitionId)!) == "belt");
        var taken = EngineWork.TakeOffWhatLacksItsNeeds(catalog, engine);
        Assert.Equal("belt", catalog.WorkOrder.KindOf(catalog.Get(Assert.Single(taken).DefinitionId)!));
        Assert.DoesNotContain(engine.SelfAndDescendants(), p => taken.Contains(p));
        Assert.Empty(EngineWork.TakeOffWhatLacksItsNeeds(catalog, engine));

        // A V8 saved when its build's fan did not fit the pump: the belt goes on the shelf
        var saved = Engine("factory/test-v8");
        var fan = Find(saved, "fan");
        PartTrees.FindParent(saved, fan)!.Children.Remove(fan);
        var loose = new List<PartInstance>();
        Assert.True(SavedParts.BringUpToDate(catalog, saved, loose));
        Assert.Equal("belt", catalog.WorkOrder.KindOf(catalog.Get(Assert.Single(loose).DefinitionId)!));
        Assert.Null(EngineWork.WhyNotOn(catalog, saved, fan, onStand: false));
    }

    /// <summary>The engines the game makes have every part they need: nothing is left off them</summary>
    [Fact]
    public void The_builds_that_run_have_all_that_their_parts_need()
    {
        var catalog = Catalog.Value;
        // But the build the tests keep short of its fan on purpose
        foreach (var build in Rated.Value.Runnable.Where(b => b.Build.Id != "factory/test-v8-no-fan"))
        {
            var engine = PartTrees.FromInstalled(PartTreeBuilder.BuildEngine(catalog, build.Build)!.Root, PartInstance.CarEngineSlot);
            Assert.True(EngineWork.TakeOffWhatLacksItsNeeds(catalog, engine).Count == 0, build.Build.Id);
        }
    }

    /// <summary>
    /// A power steering belt runs on the steering pump, not the water pump; a radiator with an electric fan does the
    /// fan's job for the drive belt
    /// </summary>
    [Fact]
    public void Steering_belts_and_electric_fans_are_kinds_of_their_own()
    {
        var catalog = Catalog.Value;
        var order = catalog.WorkOrder;
        Assert.Equal("steering_belt", order.KindOf(catalog.Get("engines/test_v8/steering_belt")!));
        Assert.Equal("steering_pump", order.KindOf(catalog.Get("engines/test_v8/steering_pump")!));
        Assert.Equal("radiator_fan", order.KindOf(catalog.Get("engines/test_v8/radiator_electric_fan")!));
        Assert.True(order.DoesJobOf("radiator_fan", "fan"));
        Assert.False(order.DoesJobOf("fan", "radiator_fan"));
        Assert.Equal("the radiator, the starter, the transmission, the clutch and the flywheel", EngineWork.StandStripsDisplay(catalog));

        // The Charger's 383 with the electric-fan radiator in place of its own: the belt goes on without a fan
        var engine = Engine("factory/test-v8");
        var belt = Find(engine, "belt");
        var fan = Find(engine, "fan");
        var radiator = Find(engine, "radiator");
        foreach (var part in new[] { belt, fan, radiator }) PartTrees.FindParent(engine, part)!.Children.Remove(part);
        Assert.Equal("Put on the fan first.", EngineWork.WhyNotOn(catalog, engine, belt, onStand: false));

        var electric = new PartInstance { DefinitionId = "engines/test_v8/radiator_electric_fan" };
        Workbench.Mount(new List<PartInstance> { engine }, Workbench.FindPlacesOnStand(catalog, engine, electric).First(), electric);
        Assert.Null(EngineWork.WhyNotOn(catalog, engine, belt, onStand: false));

        // It goes on the stand's way, as a radiator does
        Assert.Contains(electric, EngineWork.StripForStand(catalog, engine));
    }

    /// <summary>
    /// What a part needs holds it on even where no rule says so the other way round, and a need that may not go on
    /// where the engine is does not hold up what needs it
    /// </summary>
    [Fact]
    public void A_mod_needs_rule_holds_by_itself()
    {
        _temp.File("base/work_order.json", """
            { "kinds": [ { "id": "fan", "names": [ "fan" ] }, { "id": "belt", "name": "drive belt", "names": [ "belt" ] },
                         { "id": "water_pump", "names": [ "water pump", "water pomp" ] } ],
              "rules": [ { "kind": "belt", "needs": [ "fan" ] } ],
              "stand_strips": [ "fan" ] }
            """);
        var catalog = PartsCatalog.Load(RepoPaths.PartsRoots);
        catalog.WorkOrder = WorkOrder.Load(new[] { ("streetcorsa", true, Path.Combine(_temp.Path, "base")) }, new List<string>());

        var engine = Engine("factory/test-v8");
        var fan = Find(engine, "fan");
        Assert.Equal("Take off the drive belt first.", EngineWork.WhyNotOff(catalog, engine, fan, onStand: false));
        var pump = PartTrees.FindParent(engine, fan)!;

        // On the stand the fan stays off (it is one of the stand strips), so the belt does not wait for it
        var belt = Find(engine, "belt");
        PartTrees.FindParent(engine, belt)!.Children.Remove(belt);
        pump.Children.Remove(fan);
        Assert.Equal("Put on the fan first.", EngineWork.WhyNotOn(catalog, engine, belt, onStand: false));
        Assert.Null(EngineWork.WhyNotOn(catalog, engine, belt, onStand: true));
    }

    [Fact]
    public void A_mod_rule_for_a_ruled_kind_adds_to_it_and_rules_that_lock_are_reported()
    {
        _temp.File("base/work_order.json", """
            { "kinds": [ { "id": "fan" }, { "id": "belt" }, { "id": "pump" }, { "id": "head" }, { "id": "cover" } ],
              "rules": [ { "kind": "belt", "after": [ "pump" ] }, { "kind": "fan", "after": [ "belt" ] } ] }
            """);
        _temp.File("mod/work_order.json", """
            { "kinds": [ { "id": "fan", "serves": [ "cooling" ] } ],
              "rules": [ { "kind": "belt", "stand_only": true, "after": [ "cover" ], "needs": [ "fan" ] },
                         { "kind": "pump", "after": [ "fan" ] },
                         { "kind": "head", "after": [ "head" ] },
                         { "kind": "cover", "needs": [ "head", "cover" ], "after": [ "head" ] } ] }
            """);

        var problems = new List<string>();
        var order = WorkOrder.Load(new[] { ("streetcorsa", true, Path.Combine(_temp.Path, "base")), ("mymod", false, Path.Combine(_temp.Path, "mod")) }, problems);

        // Written before the base game had a rule for the belt, the mod's still counts: added to the base game's
        var belt = order.RuleFor("belt")!;
        Assert.True(belt.StandOnly);
        Assert.Equal(new[] { "pump", "cover" }, belt.After);
        Assert.Equal(new[] { "fan" }, belt.Needs);
        Assert.True(order.DoesJobOf("fan", "cooling"));

        Assert.Equal(3, problems.Count);
        Assert.Contains("work_order.json: belt, pump, fan wait for each other to be off: none of them would come off", problems);
        Assert.Contains("work_order.json: head wait for each other to be off: none of them would come off", problems);
        Assert.Contains("work_order.json: the rule of cover both needs head and waits for it to be off: cover would never go on", problems);
    }

    [Fact]
    public void A_rule_needing_a_kind_no_work_order_lists_is_reported()
    {
        _temp.File("base/work_order.json", """
            { "kinds": [ { "id": "fan", "names": [ "fan" ] }, { "id": "belt", "names": [ "belt" ] } ],
              "rules": [ { "kind": "belt", "needs": [ "fan", "pulley" ] } ] }
            """);

        var problems = new List<string>();
        var order = WorkOrder.Load(new[] { ("streetcorsa", true, Path.Combine(_temp.Path, "base")) }, problems);

        Assert.Equal(new[] { "fan", "pulley" }, order.Rules.Single().Needs);
        Assert.Equal("work_order.json: the rule of belt needs pulley, a kind no work order lists", Assert.Single(problems));
    }

    [Fact]
    public void A_loose_part_goes_on_only_with_its_way_clear()
    {
        var catalog = Catalog.Value;
        var engine = Engine("factory/test-v8");
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

    /// <summary>
    /// Every build, those that do not run too: one short of parts is what a mod's build or an old save may bring in
    /// </summary>
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

        // As the game makes an engine: a part without what it needs is left off
        var engine = PartTrees.FromInstalled(tree.Root);
        EngineWork.TakeOffWhatLacksItsNeeds(catalog, engine);
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
        EngineWork.TakeOffWhatLacksItsNeeds(catalog, engine);
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
        var build = catalog.EngineBuilds.Single(b => b.Id == "factory/test-v8");
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
        var engine = Engine("factory/test-v8");
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
        Assert.NotNull(workbench.Engine);
        workbench.IsCarView = true;

        InstalledPart Live(string kind) => workbench.Engine!.SelfAndDescendants().First(p => catalog.WorkOrder.KindOf(p.Definition) == kind);

        // In the car the crankshaft stays in, and there is no turning the engine over
        workbench.OnPartClicked(Live("crankshaft"));
        Assert.False(workbench.CanTurnEngineOver);
        Assert.False(workbench.IsEngineTurnedOver);
        Assert.Contains("engine stand", workbench.SelectedBlockedDisplay);
        Assert.False(workbench.RemoveSelectedCommand.CanExecute(null));

        // The engine comes out onto the stand, what is in the way of the stand's head onto the shelf
        workbench.OnPartClicked(workbench.Engine);
        Assert.Equal("Pull the engine", workbench.RemoveButtonText);
        workbench.RemoveSelectedCommand.Execute(null);
        Assert.Same(engine, state.Player.EngineStand);
        Assert.Null(car.Engine);
        Assert.Equal(5, state.Player.Parts.Count);
        Assert.Equal(120 + 5 * 5, minutes);

        // On the stand the pistons wait for the heads and the pan
        workbench.IsStandView = true;
        Assert.Equal("ENGINE STAND", workbench.EngineTitle);
        Assert.Same(engine, state.Player.EngineStand);
        workbench.OnPartClicked(Live("pistons"));
        Assert.Equal("Take off the cylinder heads and the oil pan first.", workbench.SelectedBlockedDisplay);
        Assert.True(workbench.CanTurnEngineOver);
        Assert.False(workbench.IsEngineTurnedOver);
        workbench.OnPartClicked(Live("oil_pan"));
        Assert.Empty(workbench.SelectedBlockedDisplay);

        // The pan is worked on from below: the engine turns over for it, and stays so while the block is picked
        Assert.True(workbench.IsEngineTurnedOver);
        workbench.RemoveSelectedCommand.Execute(null);
        Assert.Equal(6, state.Player.Parts.Count);
        Assert.True(workbench.IsEngineTurnedOver);

        // The pan does not go back on the car's empty bay; the engine from the stand does, right way up
        workbench.OnPartClicked(workbench.Engine);
        Assert.True(workbench.IsEngineTurnedOver);
        Assert.True(workbench.CanInstallFromStand);
        workbench.InstallFromStandCommand.Execute(null);
        Assert.Null(state.Player.EngineStand);
        Assert.Same(engine, car.Engine);
        Assert.True(workbench.IsCarView);
        Assert.False(workbench.IsEngineTurnedOver);
        Assert.False(workbench.CanTurnEngineOver);
    }

    [Fact]
    public async Task An_engine_taken_off_the_stand_turned_over_is_upright()
    {
        var catalog = Catalog.Value;
        var state = new GameState();
        var engine = Engine("factory/test-v8");
        var car = new Car { DefinitionId = "camaro", Parts = [engine], HasPartsAssigned = true };
        state.Player.Cars.Add(car);

        var workbench = new Screens.Garage.PartsWorkbenchViewModel(new CatalogParts(), state, () => { }, _ => Task.CompletedTask);
        workbench.SetCar(car);
        for (var waited = 0; workbench.Engine == null && waited < 10_000; waited += 20) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.NotNull(workbench.Engine);
        workbench.IsCarView = true;

        InstalledPart Live(string kind) => workbench.Engine!.SelfAndDescendants().First(p => catalog.WorkOrder.KindOf(p.Definition) == kind);

        workbench.OnPartClicked(workbench.Engine);
        workbench.RemoveSelectedCommand.Execute(null);
        workbench.IsStandView = true;
        workbench.OnPartClicked(Live("oil_pan"));
        Assert.True(workbench.IsEngineTurnedOver);

        // Off the stand onto the shelf: nothing is left on it to be turned over
        workbench.OnPartClicked(workbench.Engine);
        Assert.Equal("Off the stand", workbench.RemoveButtonText);
        workbench.RemoveSelectedCommand.Execute(null);
        Assert.Null(state.Player.EngineStand);
        Assert.True(workbench.IsStandView);
        Assert.False(workbench.IsEngineTurnedOver);
        Assert.False(workbench.CanTurnEngineOver);

        // Nor can it be asked for with the stand empty
        workbench.IsEngineTurnedOver = true;
        Assert.False(workbench.IsEngineTurnedOver);
    }

    [Fact]
    public void The_stand_turns_the_engine_about_its_crankshaft()
    {
        var catalog = Catalog.Value;
        var block = catalog.Get(Engine("factory/test-v8").DefinitionId)!;
        var crank = block.Slots.First(s => s.Name.Contains("crank", StringComparison.OrdinalIgnoreCase));

        // The crankshaft's slot lands on the spindle
        var onStand = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(crank.Position[0], crank.Position[1], 0f), EngineStandLayout.EnginePlacement(catalog, block));
        Assert.Equal(EngineStandLayout.Spindle.X, onStand.X, 3);
        Assert.Equal(EngineStandLayout.Spindle.Y, onStand.Y, 3);
        Assert.Equal(EngineStandLayout.CrankHeight, EngineStandLayout.Spindle.Y - EngineStandLayout.Spot.Y, 3);
    }

    [Fact]
    public void The_engine_turns_over_for_the_parts_worked_on_from_below()
    {
        var order = Catalog.Value.WorkOrder;
        var engine = Engine("factory/test-v8");
        EngineSide? SideOf(string kind) => order.SideOf(Catalog.Value.Get(Find(engine, kind).DefinitionId)!);

        Assert.Equal(EngineSide.Below, SideOf("crankshaft"));
        Assert.Equal(EngineSide.Below, SideOf("oil_pan"));
        Assert.Equal(EngineSide.Above, SideOf("cylinder_head"));
        Assert.Equal(EngineSide.Above, SideOf("pistons"));
        Assert.Null(SideOf("water_pump"));
        Assert.Null(SideOf("block"));
    }

    [Fact]
    public void A_mod_may_say_which_side_a_kind_is_worked_from()
    {
        _temp.File("base/work_order.json", """
            { "kinds": [ { "id": "sump", "names": [ "sump" ], "worked_from": "below" }, { "id": "head", "names": [ "head" ], "worked_from": "above" } ] }
            """);
        _temp.File("mod/work_order.json", """
            { "kinds": [ { "id": "head", "names": [ "top end" ], "worked_from": "Below" }, { "id": "sump", "worked_from": "sideways" } ] }
            """);

        var problems = new List<string>();
        var order = WorkOrder.Load(new[] { ("streetcorsa", true, Path.Combine(_temp.Path, "base")), ("mymod", false, Path.Combine(_temp.Path, "mod")) }, problems);

        Assert.Equal(EngineSide.Below, order.SideOf(new PartDefinition { Name = "Cast head" }));
        Assert.Equal(EngineSide.Below, order.SideOf(new PartDefinition { Name = "Deep sump" }));
        Assert.Null(order.SideOf(new PartDefinition { Name = "Fantastic widget" }));
        Assert.Equal(new[] { "mymod/work_order.json: sump is worked from \"sideways\", which is neither above nor below; left out" }, problems);
    }

    [Fact]
    public void A_mod_names_its_own_kinds_and_may_take_back_a_rule_only_on_purpose()
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
        Assert.Contains("without a kind", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_kind_says_which_way_it_comes_off()
    {
        var order = Catalog.Value.WorkOrder;
        var engine = Engine("factory/test-v8");

        foreach (var part in engine.SelfAndDescendants().Select(p => Catalog.Value.Get(p.DefinitionId)!).Where(d => order.KindOf(d) != null))
            Assert.NotEmpty(order.WayOf(part));

        // The gearbox slides off the input shaft before it drops; the engine lifts out
        Assert.Equal(new[] { PartWay.Back, PartWay.Down }, order.WayOf(Catalog.Value.Get(Find(engine, "transmission").DefinitionId)!));
        Assert.Equal(new[] { PartWay.Up }, order.WayOf(Catalog.Value.Get(engine.DefinitionId)!));
    }

    [Fact]
    public void A_mod_may_change_the_way_a_kind_comes_off()
    {
        _temp.File("base/work_order.json", """
            { "kinds": [ { "id": "fan", "names": [ "fan" ], "comes_off": [ "up" ] }, { "id": "belt", "names": [ "belt" ], "comes_off": [ "forward" ] } ] }
            """);
        _temp.File("mod/work_order.json", """
            { "kinds": [ { "id": "fan", "names": [ "propeller" ], "comes_off": [ "Back", "sideways" ] }, { "id": "belt", "names": [ "fan belt" ] } ] }
            """);

        var problems = new List<string>();
        var order = WorkOrder.Load(new[] { ("streetcorsa", true, Path.Combine(_temp.Path, "base")), ("mymod", false, Path.Combine(_temp.Path, "mod")) }, problems);

        Assert.Equal(new[] { PartWay.Back }, order.WayOf(new PartDefinition { Name = "Cooling fan" }));
        Assert.Equal(new[] { PartWay.Forward }, order.WayOf(new PartDefinition { Name = "Drive belt" }));
        Assert.Empty(order.WayOf(new PartDefinition { Name = "Fantastic widget" }));
        Assert.Contains(problems, p => p.StartsWith("mymod/work_order.json: fan comes off \"sideways\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_part_may_come_off_its_own_way_and_keep_the_rules_of_its_kind()
    {
        _temp.File("base/work_order.json", """
            { "kinds": [ { "id": "intake_manifold", "names": [ "intake" ], "comes_off": [ "up" ] }, { "id": "distributor", "names": [ "distributor" ] } ],
              "rules": [ { "kind": "intake_manifold", "after": [ "distributor" ] } ] }
            """);

        var problems = new List<string>();
        var order = WorkOrder.Load(new[] { ("streetcorsa", true, Path.Combine(_temp.Path, "base")) }, problems);
        var side = new PartDefinition { Id = "mymod/side_intake", Name = "Side intake", ComesOff = new List<string?> { "out" } };

        Assert.Equal("intake_manifold", order.KindOf(side));
        Assert.Equal(new[] { "distributor" }, order.RuleOf(side)!.After);
        Assert.Equal(new[] { PartWay.Out }, order.WayOf(side));
        Assert.Equal(new[] { PartWay.Up }, order.WayOf(new PartDefinition { Name = "Stock intake" }));

        // Not one leg that reads: the kind's way; an empty list on purpose: none
        var misspelt = new PartDefinition { Id = "mymod/odd_intake", Name = "Odd intake", ComesOff = new List<string?> { "sideways" } };
        Assert.Equal(new[] { PartWay.Up }, order.WayOf(misspelt));
        Assert.Empty(order.WayOf(new PartDefinition { Name = "Fixed intake", ComesOff = new List<string?>() }));

        WorkOrder.CheckWays(new[] { side, misspelt }, problems);
        Assert.Equal(new[] { "mymod/odd_intake comes off \"sideways\", which is none of up, down, forward, back, out; left out" }, problems);
    }

    [Fact]
    public void A_way_that_does_not_read_leaves_the_way_as_it_was()
    {
        _temp.File("base/work_order.json", """
            { "kinds": [ { "id": "fan", "names": [ "fan" ], "comes_off": [ "up" ] }, { "id": "belt", "names": [ "belt" ], "comes_off": [ "forward" ] },
                         { "id": "pump", "names": [ "pump" ], "comes_off": [ "out" ] } ] }
            """);
        _temp.File("mod/work_order.json", """
            { "kinds": [ { "id": "fan", "comes_off": [ "backwards" ] }, { "id": "belt", "comes_off": [ "down, forward", "2" ] },
                         { "id": "pump", "comes_off": [] } ] }
            """);

        var problems = new List<string>();
        var order = WorkOrder.Load(new[] { ("streetcorsa", true, Path.Combine(_temp.Path, "base")), ("mymod", false, Path.Combine(_temp.Path, "mod")) }, problems);

        // A list of names in one string, or a number, is no way: never the sum of the ways the enum would make of it
        Assert.Equal(new[] { PartWay.Up }, order.WayOf(new PartDefinition { Name = "Cooling fan" }));
        Assert.Equal(new[] { PartWay.Forward }, order.WayOf(new PartDefinition { Name = "Drive belt" }));
        Assert.Contains(problems, p => p.Contains("\"down, forward\"", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("\"2\"", StringComparison.Ordinal));
        Assert.Contains(problems, p => p == "mymod/work_order.json: fan keeps the way it comes off as before");

        // An empty list on purpose is the way away from what stays
        Assert.Empty(order.WayOf(new PartDefinition { Name = "Water pump" }));
    }

    [Fact]
    public void The_stand_strips_are_told_apart_in_any_case()
    {
        _temp.File("base/work_order.json", """
            { "kinds": [ { "id": "radiator", "names": [ "radiator" ] }, { "id": "transmission", "names": [ "gearbox" ] } ],
              "stand_strips": [ "Radiator", "transmission" ] }
            """);

        var order = WorkOrder.Load(new[] { ("streetcorsa", true, Path.Combine(_temp.Path, "base")) }, new List<string>());

        Assert.Equal(0, order.StandStripTurnOf("radiator"));
        Assert.Equal(1, order.StandStripTurnOf("Transmission"));
        Assert.Equal(int.MaxValue, order.StandStripTurnOf("fan"));
    }
}
