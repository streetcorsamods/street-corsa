using Newtonsoft.Json;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Parts.Scripting;

namespace StreetCorsa.Tests;

/// <summary>
/// A part definition's "overrides": figures of its own over what its script constructs (a mod part sharing a stock
/// script). These tests change definitions, so every one loads a catalog of its own.
/// </summary>
public class PartOverridesTests
{
    private const string BuildId = "chrysler-v8-pack/chrysler-la-340-275-hp";

    private sealed record Engine(PartsCatalog Catalog, PartTree Tree)
    {
        public InstalledPart Part(string className) => Tree.Root.SelfAndDescendants().First(p => p.Is(className));

        /// <summary>Overrides as a pack.json writes them, read by the catalog as it loads; what it told is returned</summary>
        public List<string> Override(InstalledPart part, string json)
        {
            part.Definition.Overrides = JsonConvert.DeserializeObject<PartDefinition>($"{{ \"overrides\": {json} }}")!.Overrides;
            var problems = new List<string>();
            PartOverrides.Apply(Catalog.Scripts, part.Definition, problems);
            return problems;
        }

        public PartScriptRuntime Run() => new(Catalog, Tree.Root);
    }

    private static Engine Stock()
    {
        var catalog = PartsCatalog.Load(RepoPaths.PartsFolder);
        var build = catalog.EngineBuilds.Single(b => b.Id.EndsWith(BuildId, StringComparison.Ordinal));
        return new(catalog, PartTreeBuilder.BuildEngine(catalog, build)!);
    }

    private static ScriptNumber Field(PartScriptRuntime runtime, InstalledPart part, string field) =>
        Assert.IsType<ScriptNumber>(runtime.ObjectOf(part)!.Fields[field]);

    private static ScriptNumber Element(PartScriptRuntime runtime, InstalledPart part, string field, int index) =>
        Assert.IsType<ScriptNumber>(Assert.IsType<ScriptArray>(runtime.ObjectOf(part)!.Fields[field]).Get(index));

    [Fact]
    public void Overrides_go_over_the_script_and_the_players_tuning_over_them()
    {
        var engine = Stock();
        var crank = engine.Part("Crankshaft");
        var gearbox = engine.Part("Transmission");
        var untouched = Element(engine.Run(), gearbox, "ratio", 4).Amount;

        Assert.Empty(engine.Override(crank, """{ "stroke": 76.2 }"""));
        // The element named on its own wins over the list, though the list comes after it; null keeps an element
        Assert.Empty(engine.Override(gearbox, """{ "ratio[3]": 0.9, "ratio": [0, 1.76, 1.0, 1.2, null], "gears": 2 }"""));
        gearbox.Tuning["ratio[1]"] = 2.0;                 // the player's own setting wins

        var runtime = engine.Run();
        Assert.Equal(76.2, Field(runtime, crank, "stroke").Amount);
        Assert.Equal(2.0, Element(runtime, gearbox, "ratio", 1).Amount);
        Assert.Equal(1.0, Element(runtime, gearbox, "ratio", 2).Amount);
        Assert.Equal(0.9, Element(runtime, gearbox, "ratio", 3).Amount);
        Assert.Equal(untouched, Element(runtime, gearbox, "ratio", 4).Amount);
        Assert.Equal(new ScriptNumber(2, true), Field(runtime, gearbox, "gears"));
    }

    [Fact]
    public void What_is_read_from_the_pack_gets_the_overrides_too()
    {
        var engine = Stock();
        var block = engine.Part("Block");
        var gearbox = engine.Part("Transmission");

        Assert.Empty(engine.Override(block, """{ "value": 900, "RPM_limit": 5000 }"""));
        Assert.Empty(engine.Override(gearbox, """{ "ratio": [0, 3.1] }"""));

        Assert.Equal(900, block.Definition.Number("value"));
        Assert.Equal(900, Parts.Cars.PartPricing.NewPrice(block.Definition) / Parts.Cars.PartPricing.Scale);
        Assert.Equal(5000, block.Definition.Number("RPM_limit"));
        Assert.Equal(3.1, (double)((Newtonsoft.Json.Linq.JArray)gearbox.Definition.Properties["ratio"])[1]);
    }

    [Fact]
    public void A_whole_field_takes_a_whole_number_as_the_script_would_store_it()
    {
        var engine = Stock();
        var block = engine.Part("Block");

        var problems = engine.Override(block, """{ "cylinders": 6.5 }""");

        Assert.Contains(problems, p => p.Contains("\"cylinders\"") && p.Contains("taken as 6"));
        Assert.Equal(new ScriptNumber(6, true), Field(engine.Run(), block, "cylinders"));
        Assert.Equal(6, block.Definition.Number("cylinders"));
    }

    [Fact]
    public void An_array_element_is_stored_as_the_arrays_type()
    {
        var engine = Stock();
        var gearbox = engine.Part("Transmission");
        var type = Assert.IsType<ScriptArray>(engine.Run().ObjectOf(gearbox)!.Fields["ratio"]).ElementType;

        Assert.Empty(engine.Override(gearbox, """{ "ratio[2]": 1 }"""));

        Assert.Equal(ScriptTypes.Convert(ScriptValue.Of(1.0), type), Element(engine.Run(), gearbox, "ratio", 2));
    }

    [Theory]
    [InlineData("Crankshaft", """{ "Stroke": 76.2 }""", "no such field")]
    [InlineData("Crankshaft", """{ "stroke": "76.2" }""", "not a number")]
    [InlineData("Crankshaft", """{ "stroke": NaN }""", "not a finite number")]
    [InlineData("Crankshaft", """{ "stroke": 1e400 }""", "not a finite number")]
    [InlineData("Crankshaft", """{ "stroke[1]": 70 }""", "not an array")]
    [InlineData("Crankshaft", """{ "stroke]": 70 }""", "neither a field")]
    [InlineData("Transmission", """{ "ratio": 2.0 }""", "give a list")]
    [InlineData("Transmission", """{ "ratio[99]": 2.0 }""", "elements; left out")]
    [InlineData("Transmission", """{ "name": 5 }""", "other than numbers")]
    public void A_bad_override_is_told_and_left_out(string className, string json, string told)
    {
        var engine = Stock();
        var part = engine.Part(className);
        var before = engine.Run().ObjectOf(part)!.Fields.ToDictionary(f => f.Key, f => f.Value.ToString());

        var problems = engine.Override(part, json);

        Assert.Contains(problems, p => p.Contains(part.Definition.Id) && p.Contains(told));
        Assert.Empty(part.Definition.FieldOverrides);
        var after = engine.Run().ObjectOf(part)!.Fields;
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        Assert.All(before, f => Assert.Equal(f.Value, after[f.Key].ToString()));
    }

    [Fact]
    public void A_field_the_script_works_out_itself_is_told_and_left_out()
    {
        var engine = Stock();
        var part = engine.Tree.Root.SelfAndDescendants().First(p => p.Definition.Derived.Count > 0 && p.Definition.SourceScript != null);
        var field = part.Definition.Derived.Keys.First();

        var problems = engine.Override(part, $$"""{ "{{field}}": 1 }""");

        Assert.Contains(problems, p => p.Contains($"\"{field}\"") && p.Contains("works that field out"));
        Assert.Empty(part.Definition.FieldOverrides);
    }

    [Fact]
    public void A_list_longer_than_the_array_keeps_what_fits()
    {
        var engine = Stock();
        var gearbox = engine.Part("Transmission");
        var length = Assert.IsType<ScriptArray>(engine.Run().ObjectOf(gearbox)!.Fields["ratio"]).Length;
        var list = string.Join(", ", Enumerable.Range(0, length + 2).Select(i => i == 1 ? "3.3" : "null"));

        var problems = engine.Override(gearbox, $$"""{ "ratio": [{{list}}] }""");

        Assert.Contains(problems, p => p.Contains("past it are left out"));
        Assert.Equal(3.3, Element(engine.Run(), gearbox, "ratio", 1).Amount);
    }

    [Fact]
    public void The_dyno_takes_an_overridden_stroke()
    {
        var engine = Stock();
        var before = EngineEvaluator.Evaluate(engine.Catalog, engine.Tree).Dyno!.Displacement;
        var crank = engine.Part("Crankshaft");
        var stroke = Field(engine.Run(), crank, "stroke").Amount;

        Assert.Empty(engine.Override(crank, $$"""{ "stroke": {{(stroke * 0.8).ToString(System.Globalization.CultureInfo.InvariantCulture)}} }"""));

        var after = EngineEvaluator.Evaluate(engine.Catalog, engine.Tree).Dyno!.Displacement;
        Assert.Equal(before * 0.8, after, before * 0.01);
    }
}
