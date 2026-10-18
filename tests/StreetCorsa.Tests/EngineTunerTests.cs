using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>The rivals' tuning on the real parts and the real dyno</summary>
public class EngineTunerTests(ITestOutputHelper output)
{
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsFolder));
    private static readonly Lazy<EngineBuildIndex> Builds = new(() => EngineBuildIndex.Create(Catalog.Value));

    private static RatedBuild Build(string idEnd) => Builds.Value.Runnable.Single(b => b.Build.Id.EndsWith(idEnd, StringComparison.Ordinal));

    [Theory]
    [InlineData("rated/chrysler-la-340-275-hp")]
    [InlineData("rated/ford-221-132-hp")]
    public void Money_buys_power_that_the_dyno_confirms(string buildId)
    {
        var catalog = Catalog.Value;
        var stock = EngineFactory.CreateStock(catalog, Build(buildId), 0.8, new Random(1))!;
        var before = stock.Report.Dyno!.MaxPowerHp;

        var started = DateTime.Now;
        var result = EngineTuner.TuneUp(catalog, Builds.Value, stock.Root, 1_000_000m, 1.0, new Random(7));
        output.WriteLine($"{buildId}: {(DateTime.Now - started).TotalMilliseconds:0} ms");

        Assert.NotNull(result);
        foreach (var upgrade in result!.Upgrades)
            output.WriteLine($"  {upgrade.Group}: {upgrade.Change}, {upgrade.PowerBefore:0} -> {upgrade.PowerAfter:0} hp, ${upgrade.Cost} (trade-in ${upgrade.TradeIn})");

        Assert.True(result.Report.Runs);
        Assert.True(result.Report.Dyno!.MaxPowerHp >= before * (1 + EngineTuner.MinGain));
        Assert.All(result.Upgrades, u => Assert.True(u.PowerAfter >= u.PowerBefore * (1 + EngineTuner.MinGain)));
        Assert.Equal(result.Upgrades.Count, result.Upgrades.Select(u => u.Group).Distinct().Count());
    }

    [Fact]
    public void No_money_no_upgrade()
    {
        var catalog = Catalog.Value;
        var stock = EngineFactory.CreateStock(catalog, Build("rated/chrysler-la-340-275-hp"), 0.8, new Random(1))!;

        Assert.Null(EngineTuner.TuneUp(catalog, Builds.Value, stock.Root, 0m, 1.0, new Random(7)));
    }

    [Fact]
    public void The_upgrades_stay_within_the_budget()
    {
        var catalog = Catalog.Value;
        var stock = EngineFactory.CreateStock(catalog, Build("rated/chrysler-la-340-275-hp"), 0.8, new Random(1))!;

        foreach (var budget in new[] { 50m, 200m, 800m })
        {
            var result = EngineTuner.TuneUp(catalog, Builds.Value, stock.Root, budget, 1.0, new Random(3));
            output.WriteLine($"${budget}: {(result == null ? "nothing" : string.Join(", ", result.Upgrades.Select(u => $"{u.Change} ${u.Cost}")))}");
            if (result != null) Assert.True(result.Cost <= budget);
        }
    }

    [Fact]
    public void Several_rounds_never_swap_away_a_bolt_on_already_paid_for()
    {
        var catalog = Catalog.Value;
        var multiRound = 0;
        foreach (var buildId in new[] { "rated/chrysler-la-340-275-hp", "rated/ford-221-132-hp" })
        {
            var stock = EngineFactory.CreateStock(catalog, Build(buildId), 0.8, new Random(1))!;
            var before = Describe(stock.Root);

            foreach (var seed in Enumerable.Range(1, 6))
            {
                var result = EngineTuner.TuneUp(catalog, Builds.Value, stock.Root, 1_000_000m, 1.0, new Random(seed), maxUpgrades: 4);
                if (result == null) continue;
                output.WriteLine($"{buildId} #{seed}: {string.Join(", ", result.Upgrades.Select(u => $"{u.Group}: {u.Change}"))}");

                // A block swap throws the old engine's parts away: it may only come first
                Assert.DoesNotContain(result.Upgrades.Skip(1), u => u.Group == EngineTuner.BlockGroup);
                // Each round starts from the engine the one before made
                for (var i = 1; i < result.Upgrades.Count; i++)
                    Assert.Equal(result.Upgrades[i - 1].PowerAfter, result.Upgrades[i].PowerBefore);
                Assert.Equal(result.Upgrades[^1].PowerAfter, result.Report.Dyno!.MaxPowerHp);
                if (result.Upgrades.Count > 1) multiRound++;
            }

            // The engine handed in is left as it was: the tuned one is a copy
            Assert.Equal(before, Describe(stock.Root));
        }

        Assert.True(multiRound > 0, "no run tuned more than once: the test proves nothing");

        static string Describe(PartInstance root) =>
            string.Join("|", root.SelfAndDescendants().Select(p => $"{p.InstanceId}:{p.DefinitionId}:{p.ParentSlot}:{p.OwnSlot}:{p.Children.Count}"));
    }

    [Fact]
    public void A_saved_tuning_key_outside_an_array_is_left_alone()
    {
        var catalog = Catalog.Value;
        var stock = EngineFactory.CreateStock(catalog, Build("rated/chrysler-la-340-275-hp"), 0.8, new Random(1))!;
        var tree = PartTrees.ToInstalled(catalog, stock.Root)!;

        // The gearbox: a tuned gear outside its list is left alone, one in it takes
        var gearbox = tree.Root.SelfAndDescendants().Single(p => p.Is(PartClasses.Transmission));
        var gears = EngineEvaluator.Evaluate(catalog, tree.AsPartTree()).GearRatios.Count;
        gearbox.Tuning["gears[-5]"] = 9;
        gearbox.Tuning["gears[100000]"] = 9;
        gearbox.Tuning["gears[0]"] = 3.25;

        var tuned = EngineEvaluator.Evaluate(catalog, tree.AsPartTree());
        Assert.Equal(gears, tuned.GearRatios.Count);
        Assert.Equal(3.25, tuned.GearRatios[0]);
    }
}
