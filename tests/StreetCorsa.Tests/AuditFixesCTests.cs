using System.Diagnostics;
using System.Numerics;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-09-29 audit's fixes in the parts and the mods: the tuner's cooling before a block swap, the old part logic's
/// budgets, loops in a work order, cooling said by data instead of names, and what a hostile or broken mod or save
/// can no longer do.
/// </summary>
public sealed class AuditFixesCTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ---- The tuner: a radiator bought first is never thrown away with a swapped block ----

    private static Lazy<(PartsCatalog Catalog, EngineBuildIndex Builds, EngineCooling Cooling)> Real => TestCars.Cooling;

    [Fact]
    public void An_engine_cooled_first_is_not_swapped_for_another_block_in_the_same_tune_up()
    {
        var (catalog, builds, _) = Real.Value;

        // Engines with a bigger one of their family to swap in: the swap a tuner would make if nothing stopped it
        var swappable = builds.Runnable
            .Where(b => b.Family.Length > 0 && builds.Runnable.Any(o => o.Family == b.Family && o.PowerHp > b.PowerHp * 1.05 && o.PowerHp < b.PowerHp * 1.5))
            .ToList();
        Assert.NotEmpty(swappable);

        var cooledFirst = 0;
        foreach (var build in swappable)
        {
            var engine = EngineFactory.CreateStock(catalog, build, 1.0, new Random(1))!.Root;

            // Its own block runs hot until a performance radiator goes on; any other block is cooled well enough
            double CoolingOf(PartInstance e, double hp) =>
                !e.DefinitionId.Equals(engine.DefinitionId, StringComparison.OrdinalIgnoreCase)
                || e.SelfAndDescendants().Any(p => catalog.Get(p.DefinitionId) is { } d && EngineCooling.KindOf(d) == EngineCooling.Kind.Radiator && EngineCooling.IsPerformanceRadiator(d))
                    ? 1.0
                    : 0.9;

            foreach (var seed in Enumerable.Range(1, 3))
            {
                var result = EngineTuner.TuneUp(catalog, builds, engine, 1_000_000m, 1.0, new Random(seed), maxUpgrades: 1, coolingOf: CoolingOf);
                if (result is not { Upgrades: [{ Group: EngineTuner.CoolingGroup }, ..] }) continue;

                cooledFirst++;
                Assert.DoesNotContain(result.Upgrades, u => u.Group == EngineTuner.BlockGroup);

                // What was billed for the cooling is on the engine that comes out
                Assert.All(result.Upgrades[0].Fitted, p => Assert.Contains(result.Engine.SelfAndDescendants(), q => q.InstanceId == p.InstanceId));
            }
        }

        Assert.True(cooledFirst > 0, "no engine was cooled before its upgrades");
    }

    // ---- The work order: loops found in a dense file, cooling said by the mods ----

    [Fact]
    public void Rules_that_all_wait_for_each_other_are_one_loop_told_at_once()
    {
        // Thirty kinds, each waiting for every other: every simple loop among them would never be listed
        var ids = Enumerable.Range(0, 30).Select(i => $"k{i:00}").ToList();
        var rules = string.Join(", ", ids.Select(id => $$"""{ "kind": "{{id}}", "after": [ {{string.Join(", ", ids.Where(o => o != id).Select(o => $"\"{o}\""))}} ] }"""));
        _temp.File("base/work_order.json", $$"""{ "kinds": [ {{string.Join(", ", ids.Select(id => $$"""{ "id": "{{id}}" }"""))}} ], "rules": [ {{rules}} ] }""");

        var problems = new List<string>();
        var clock = Stopwatch.StartNew();
        WorkOrder.Load([("streetcorsa", true, Path.Combine(_temp.Path, "base"))], problems);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        var loop = Assert.Single(problems);
        Assert.StartsWith("work_order.json: k00, ", loop);
        Assert.All(ids, id => Assert.Contains(id, loop));
    }

    [Fact]
    public void Two_loops_through_different_kinds_are_told_apart_and_a_chain_is_no_loop()
    {
        _temp.File("base/work_order.json", """
            { "kinds": [ { "id": "a" }, { "id": "b" }, { "id": "c" }, { "id": "d" }, { "id": "e" } ],
              "rules": [ { "kind": "a", "after": [ "b" ] }, { "kind": "b", "after": [ "A" ] },
                         { "kind": "c", "after": [ "d" ] }, { "kind": "d", "after": [ "e" ] }, { "kind": "e", "after": [ "d" ] } ] }
            """);

        var problems = new List<string>();
        WorkOrder.Load([("streetcorsa", true, Path.Combine(_temp.Path, "base"))], problems);

        Assert.Equal(
            ["work_order.json: a, b wait for each other to be off: none of them would come off",
             "work_order.json: d, e wait for each other to be off: none of them would come off"],
            problems);
    }

    /// <summary>A mod's parts folder with a work order and one pack: the catalog of the base game's empty folder and it</summary>
    private PartsCatalog ModCatalog(string workOrder, string parts)
    {
        _temp.File("base/parts/work_order.json", """{ "kinds": [] }""");
        _temp.File("mod/parts/work_order.json", workOrder);
        _temp.File("mod/parts/cooling/pack.json", $$"""{ "id": "cooling", "parts": [ {{parts}} ] }""");
        return PartsCatalog.Load([
            (new ModInfo { Id = ModIds.BaseId }, Path.Combine(_temp.Path, "base", "parts")),
            (new ModInfo { Id = "mymod" }, Path.Combine(_temp.Path, "mod", "parts"))
        ]);
    }

    [Fact]
    public void A_mod_says_what_its_parts_do_for_the_cooling_whatever_their_names()
    {
        var catalog = ModCatalog("""
            { "kinds": [ { "id": "pump", "cooling": "water_pump" }, { "id": "rad", "cooling": "radiator", "serves": [ "blower" ] },
                         { "id": "blower", "cooling": "fan" }, { "id": "bracket", "cooling": "none" }, { "id": "odd", "cooling": "heater" } ] }
            """, """
            { "id": "p", "name": "Mr. Gasket water pump", "kind": "pump", "performance": true },
            { "id": "r", "name": "Core", "kind": "rad" },
            { "id": "f", "name": "Seven blade flex fan", "kind": "blower", "performance": false },
            { "id": "h", "name": "Water pump", "kind": "bracket" },
            { "id": "q", "name": "Stock water pump" }
            """);

        PartDefinition Part(string id) => catalog.Get("mymod:" + id)!;

        // By their kinds, not their words: a "gasket" pump is a pump, a pump-named bracket is none
        Assert.Equal(EngineCooling.Kind.WaterPump, EngineCooling.KindOf(Part("p")));
        Assert.True(EngineCooling.IsPerformancePump(Part("p")));
        Assert.Equal(EngineCooling.Kind.Radiator, EngineCooling.KindOf(Part("r")));
        Assert.True(EngineCooling.HasElectricFan(Part("r")));
        Assert.False(EngineCooling.IsPerformanceRadiator(Part("r")));
        Assert.Equal(EngineCooling.Kind.Fan, EngineCooling.KindOf(Part("f")));
        Assert.False(EngineCooling.IsPerformanceFan(Part("f")));
        Assert.Equal(EngineCooling.Kind.Other, EngineCooling.KindOf(Part("h")));

        // A part that says nothing goes by its name
        Assert.Equal(EngineCooling.Kind.WaterPump, EngineCooling.KindOf(Part("q")));
        Assert.Contains(catalog.Problems, p => p.Contains("\"heater\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Cooling_words_are_whole_words()
    {
        PartDefinition Named(string name) => new() { Id = "x", Name = name };

        Assert.False(EngineCooling.IsPerformanceRadiator(Named("Brace kit radiator")));
        Assert.True(EngineCooling.IsPerformanceRadiator(Named("Racing radiator")));
        Assert.True(EngineCooling.IsPerformanceRadiator(Named("Aluminium radiator")));
        Assert.Equal(EngineCooling.StockSumpG, EngineCooling.SumpG(Named("Embraced oil pan")));
        Assert.Equal(EngineCooling.RaceSumpG, EngineCooling.SumpG(Named("Drag race oil pan")));
        Assert.Equal(EngineCooling.RaceSumpG, EngineCooling.SumpG(new PartDefinition { Id = "x", Name = "Oil pan", Performance = true }));
        Assert.Equal(EngineCooling.StockSumpG, EngineCooling.SumpG(new PartDefinition { Id = "x", Name = "Milodon deep pan", Performance = false }));
        Assert.Equal(EngineCooling.Kind.Other, EngineCooling.KindOf(Named("Fantastic bracket")));
    }

    // ---- Saves and models that are anyone's ----

    [Fact]
    public void A_part_tree_saved_far_too_deep_or_with_nulls_is_copied_without_overflowing()
    {
        var root = new PartInstance("a") { Tuning = null! };
        var at = root;
        for (var i = 0; i < 20_000; i++)
        {
            var child = new PartInstance("a");
            at.Children.Add(child);
            at = child;
        }

        at.Children = null!;
        var copy = PartTrees.Clone(root, keepIds: true);

        var depth = 0;
        for (var part = copy; part.Children.Count > 0; part = part.Children[0]) depth++;
        Assert.Equal(PartTrees.MaxDepth, depth);
        Assert.Empty(copy.Tuning);
    }

    [Fact]
    public void A_part_model_that_does_not_read_is_a_box_not_a_failed_assembly()
    {
        _temp.File("parts/junk/pack.json", """{ "id": "junk", "parts": [ { "id": "junk/p", "name": "p", "model": "p.kn5" } ] }""");
        _temp.Bytes("parts/junk/p.kn5", [1, 2, 3, 4, 5, 6, 7, 8]);
        var catalog = PartsCatalog.Load(Path.Combine(_temp.Path, "parts"));
        var part = catalog.Get("junk/p")!;

        var model = PartAssembler.BuildModel(catalog, "test", [new PlacedPart(part, Matrix4x4.Identity, new InstalledPart(part))]);

        Assert.Single(model.Nodes);
    }

    [Fact]
    public void A_price_of_a_part_whose_wear_is_no_number_is_the_price_of_a_new_one()
    {
        var part = new PartDefinition { Id = "x", Name = "x" };
        var worth = PartPricing.Worth(part, new PartInstance("x") { Wear = double.NaN, Tear = double.NaN });
        Assert.Equal(PartPricing.NewPrice(part), worth);
    }

    // ---- Mods ----

    [Fact]
    public void A_mod_json_is_read_as_leniently_as_it_always_was()
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("Content/mymod/mod.json", """
            {
              // written by hand
              "ID": "mymod",
              "name": "My mod",
              "version": 1.10,
              "order": "3",
              "author": null,
              "extra": { "anything": [ 1, 2 ] },
            }
            """);

        var mods = ModSet.Load(_temp.Combine("Content"));

        var mod = mods.Get("mymod");
        Assert.NotNull(mod);
        Assert.Equal("1.10", mod!.Version);
        Assert.Equal(3, mod.Order);
        Assert.Equal("My mod", mod.Name);
        Assert.Equal(string.Empty, mod.Author);
        Assert.Empty(mods.Problems);
    }

    [Fact]
    public void A_mod_json_that_is_no_manifest_leaves_the_mod_out_with_a_line()
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("Content/bad/mod.json", """{ "id": "bad", "order": 1.5 }""");

        var mods = ModSet.Load(_temp.Combine("Content"));

        Assert.Null(mods.Get("bad"));
        Assert.Contains(mods.Problems, p => p.StartsWith("bad: mod.json could not be read", StringComparison.Ordinal));
    }

    private sealed record Scene(string Id);

    [Fact]
    public void Scenes_with_a_taken_id_are_problem_lines_without_a_logger()
    {
        _temp.File("Content/streetcorsa/streets/one/street.json", "{}");
        _temp.File("Content/streetcorsa/streets/two/street.json", "{}");
        var mods = ModSet.Load(_temp.Combine("Content"));

        var problems = new List<string>();
        var scenes = ModScenes.LoadAll(mods, ContentTypes.Streets, problems, _ => new Scene("same"), s => s.Id, (s, id) => s with { Id = id });

        Assert.Equal("same", Assert.Single(scenes).Id);
        Assert.Equal("streetcorsa/two has the id same, which is taken or has a ':' in it; left out", Assert.Single(problems));
    }
}
