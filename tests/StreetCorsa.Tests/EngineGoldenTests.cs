using StreetCorsa.Parts;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// The real parts of Content/streetcorsa/parts still make their engines, through the API EngineBench uses. The figures are
/// what `EngineBench "Street Corsa/Content/streetcorsa/parts" rated` printed for these builds on 2026-09-24 (after the VM fixes).
/// </summary>
public class EngineGoldenTests
{
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsRoots));

    [Theory]
    //          build id ends with                               hp   @rpm   Nm  @rpm
    [InlineData("rated/chrysler-la-340-275-hp", 301, 5750, 426, 2750)]
    [InlineData("rated/chrysler-426-hemi-425-hp", 387, 6000, 553, 2750)]
    [InlineData("rated/ford-221-132-hp", 118, 5000, 283, 1000)]
    public void A_rated_build_runs_with_the_same_dyno_figures(string buildId, int hp, int hpRpm, int nm, int nmRpm)
    {
        var catalog = Catalog.Value;
        var build = catalog.EngineBuilds.Single(b => b.Id.EndsWith(buildId, StringComparison.Ordinal));

        var tree = PartTreeBuilder.BuildEngine(catalog, build);
        Assert.NotNull(tree);
        var report = EngineEvaluator.Evaluate(catalog, tree!);

        Assert.Null(report.Problem);
        Assert.True(report.Runs);
        var dyno = report.Dyno!;
        Assert.Equal(hp, (int)Math.Round(dyno.MaxPowerHp));
        Assert.Equal(hpRpm, (int)Math.Round(dyno.MaxPowerRpm));
        Assert.Equal(nm, (int)Math.Round(dyno.MaxTorque));
        Assert.Equal(nmRpm, (int)Math.Round(dyno.MaxTorqueRpm));
    }

    [Fact]
    public void The_catalog_loads_without_problems()
    {
        var catalog = Catalog.Value;
        Assert.Equal(161, catalog.EngineBuilds.Count);
        Assert.Empty(catalog.Problems);
    }
}
