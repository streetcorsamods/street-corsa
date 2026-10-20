using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Tests;

/// <summary>
/// The factory engine an Assetto Corsa car is matched to, now that the engine packs are mods a player may not have:
/// a car of a known family gets a build of its family or none (a Chevrolet with no GM pack installed races on its own
/// data, never on a Ford six), a car of no known family takes the best there is, and the refusal is a rule a mod may
/// turn off.
/// </summary>
[Collection(GlobalsCollection.Name)]
public class StockEngineMatcherTests
{
    private static EngineBuildIndex Index => EngineBuildIndex.Create(TestCars.Catalog.Value);

    [Fact]
    public void A_car_of_the_builds_family_gets_a_build_of_its_own()
    {
        var best = StockEngineMatcher.Best(Index, null, "Chevrolet", "Camaro SS", 325);

        Assert.NotNull(best);
        Assert.Equal("gm", best!.Family);
    }

    [Fact]
    public void A_car_whose_family_has_no_build_installed_gets_no_engine()
    {
        var index = Index;
        Assert.DoesNotContain(index.Runnable, b => b.Family == "jaguar");

        Assert.Null(StockEngineMatcher.Best(index, null, "Jaguar", "XK 150", 250));
        Assert.NotEmpty(StockEngineMatcher.Rank(index, null, "Jaguar", "XK 150", 250));
    }

    [Fact]
    public void A_car_of_no_known_family_takes_the_best_there_is()
    {
        Assert.NotNull(StockEngineMatcher.Best(Index, null, "Toyota", "Celica", 100));
    }

    [Fact]
    public void With_the_refusal_off_a_build_of_another_family_serves()
    {
        var was = EngineRules.ProgramSource;
        var rules = new EngineRules();
        rules.Matcher.RefuseOtherFamily = false;
        EngineRules.ProgramSource = () => rules;
        try
        {
            Assert.NotNull(StockEngineMatcher.Best(Index, null, "Jaguar", "XK 150", 250));
        }
        finally
        {
            EngineRules.ProgramSource = was;
        }
    }
}
