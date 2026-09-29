using Newtonsoft.Json.Linq;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Parts.Scripting;

namespace StreetCorsa.Tests;

/// <summary>
/// A part definition's "overrides": figures of its own over what its script constructs (a mod part sharing a stock
/// script). These tests change definitions, so the class has a catalog of its own.
/// </summary>
public class PartOverridesTests
{
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsFolder));
    private static readonly Lazy<EngineBuildIndex> Builds = new(() => EngineBuildIndex.Create(Catalog.Value));

    private static LiveTree Stock()
    {
        var build = Builds.Value.Runnable.Single(b => b.Build.Id.EndsWith("chrysler-v8-pack/chrysler-la-340-275-hp", StringComparison.Ordinal));
        var stock = EngineFactory.CreateStock(Catalog.Value, build, 1.0, new Random(1))!;
        return PartTrees.ToInstalled(Catalog.Value, stock.Root)!;
    }

    private static InstalledPart WithField(LiveTree tree, string field, Func<ScriptValue?, bool> test)
    {
        var runtime = new PartScriptRuntime(Catalog.Value, tree.Root);
        return tree.Root.SelfAndDescendants().First(p => test(runtime.ObjectOf(p)?.Fields.GetValueOrDefault(field)));
    }

    private static double Number(PartScriptRuntime runtime, InstalledPart part, string field) =>
        Assert.IsType<ScriptNumber>(runtime.ObjectOf(part)!.Fields[field]).Amount;

    private static double Element(PartScriptRuntime runtime, InstalledPart part, string field, int index) =>
        Assert.IsType<ScriptNumber>(Assert.IsType<ScriptArray>(runtime.ObjectOf(part)!.Fields[field]).Get(index)).Amount;

    [Fact]
    public void Overrides_go_over_the_script_and_the_players_tuning_over_them()
    {
        var tree = Stock();
        var crank = WithField(tree, "stroke", v => v is ScriptNumber { Amount: > 0 });
        var gearbox = WithField(tree, "ratio", v => v is ScriptArray { Length: > 4 });
        var untouched = Element(new PartScriptRuntime(Catalog.Value, tree.Root), gearbox, "ratio", 4);
        try
        {
            crank.Definition.Overrides = new() { ["stroke"] = 76.2 };
            gearbox.Definition.Overrides = new()
            {
                ["ratio"] = new JArray(0.0, 1.76, 1.0),      // a list from the first element on
                ["ratio[3]"] = 0.9,                           // one element
                ["gears"] = 2,
                ["name"] = "not a number: left alone"
            };
            gearbox.Tuning["ratio[1]"] = 2.0;                 // the player's own setting wins

            var runtime = new PartScriptRuntime(Catalog.Value, tree.Root);
            Assert.Equal(76.2, Number(runtime, crank, "stroke"));
            Assert.Equal(2.0, Element(runtime, gearbox, "ratio", 1));
            Assert.Equal(1.0, Element(runtime, gearbox, "ratio", 2));
            Assert.Equal(0.9, Element(runtime, gearbox, "ratio", 3));
            Assert.Equal(untouched, Element(runtime, gearbox, "ratio", 4));
            Assert.Equal(2, Number(runtime, gearbox, "gears"));
            Assert.IsNotType<ScriptNumber>(runtime.ObjectOf(gearbox)!.Fields.GetValueOrDefault("name"));
        }
        finally
        {
            crank.Definition.Overrides = null; gearbox.Definition.Overrides = null;
        }
    }

    [Fact]
    public void The_dyno_takes_an_overridden_stroke()
    {
        var tree = Stock();
        var before = EngineEvaluator.Evaluate(Catalog.Value, tree.AsPartTree()).Dyno!.Displacement;
        var crank = WithField(tree, "stroke", v => v is ScriptNumber { Amount: > 0 });
        var stroke = Number(new PartScriptRuntime(Catalog.Value, tree.Root), crank, "stroke");
        try
        {
            crank.Definition.Overrides = new() { ["stroke"] = stroke * 0.8 };
            var after = EngineEvaluator.Evaluate(Catalog.Value, tree.AsPartTree()).Dyno!.Displacement;
            Assert.Equal(before * 0.8, after, before * 0.01);
        }
        finally
        {
            crank.Definition.Overrides = null;
        }
    }
}
