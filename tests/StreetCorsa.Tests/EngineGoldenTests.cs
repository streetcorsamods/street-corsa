using StreetCorsa.Parts;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>
/// Real engines still make their figures, through the API EngineBench uses: a handful of rated builds kept as data in
/// Fixtures\goldens (copied from the engine mods when the tests' copies of them went, roadmap step 38), in a catalog of
/// their own with the base game's parts, not the tests' engines. The figures are what `EngineBench rated` printed for
/// these builds on 2026-09-24 (after the VM fixes).
/// </summary>
public class EngineGoldenTests
{
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.GoldenRoots));

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

    /// <summary>
    /// What `EngineBench rated` holds the content to (half the rating), over the base game and the goldens: the base game has no
    /// engine of its own since the engines became mods
    /// </summary>
    [Fact]
    public void Every_rated_build_runs_within_half_its_rating()
    {
        var catalog = Catalog.Value;
        var rated = catalog.EngineBuilds.Where(b => b.RatedPower != null).ToList();
        Assert.NotEmpty(rated);

        var failures = new List<string>();
        foreach (var build in rated)
        {
            var tree = PartTreeBuilder.BuildEngine(catalog, build);
            var report = tree == null ? null : EngineEvaluator.Evaluate(catalog, tree);
            if (report?.Dyno is not { } dyno || report.Problem != null) failures.Add($"{build.Id}: {report?.Problem ?? "no engine block among its parts"}");
            else if (!build.RunsToItsRating(dyno.MaxPowerHp)) failures.Add($"{build.Id}: {dyno.MaxPowerHp:0} hp, rated {build.RatedPower:0}");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void The_catalog_loads_without_problems()
    {
        var catalog = Catalog.Value;
        // The base game's own build and the goldens' three
        Assert.Equal(4, catalog.EngineBuilds.Count);
        // The base game's radiator flush is for a radiator with an electric fan too, which only an engine mod (or the
        // tests' own engines) has: a kind of the work order, so no problem
        Assert.Empty(catalog.Problems);
    }
}
