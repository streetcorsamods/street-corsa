using StreetCorsa.Dialogs.RaceHub;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.Diners;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Screens.Diner;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Diner;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// Step 37, phase 3: the diners by level. Each takes the cars from a horsepower up; a racer is a regular of the diner
/// their car's power puts them in (a boss of the top one); the regulars of a diner above the player's car will not race
/// it; the racing hub lists the diners.
/// </summary>
public sealed class DinerLevelsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static ModSet BaseMods => ModSet.Load(RepoPaths.Content);

    private static DinerBook Base => DinerBook.Load(BaseMods, DinerScenes.LoadAll(BaseMods));

    /// <summary>Four diners with the base game's floors, over no streets</summary>
    private static DinerBook Four() => DinerBook.From(
    [
        new Diner { Id = "starlite", PowerFrom = 380, Stakes = 3 },
        new Diner { Id = "mainstreet", PowerFrom = 0 },
        new Diner { Id = "surfside", PowerFrom = 300, Stakes = 2 },
        new Diner { Id = "hiway", PowerFrom = 220, Stakes = 1.5m }
    ]);

    private static Opponent Racer(string name, double? powerHp, bool boss = false)
    {
        var racer = new Opponent(name, 30, Gender.Male, 95, 50) { IsBoss = boss };
        racer.Cars.Add(new Car("car") { PowerHp = powerHp });
        return racer;
    }

    private static RacerOut Out(Opponent racer, string? bhp = null) =>
        new(racer, racer.Cars[0], new CarDefinition { Id = "car", Specs = bhp == null ? null : new CarSpecsData { Bhp = bhp } });

    // ----- the base game's diners -----

    [Fact]
    public void The_base_game_has_four_diners_from_main_street_up_each_with_a_name_and_a_word_about_it()
    {
        var book = Base;

        Assert.Equal(["mainstreet", "hiway", "surfside", "starlite"], book.All.Select(d => d.Id));
        Assert.Equal([0, 1, 2, 3], book.All.Select(d => d.Level));
        Assert.Equal(0, book.First!.PowerFrom);
        Assert.True(book.First.IsFirst);
        Assert.Same(book.All[^1], book.Top);
        for (var i = 1; i < book.Count; i++)
        {
            Assert.True(book.All[i].PowerFrom > book.All[i - 1].PowerFrom, book.All[i].Id);
            Assert.True(book.All[i].Stakes > book.All[i - 1].Stakes, book.All[i].Id);
        }

        var text = GameText.Load(BaseMods);
        foreach (var diner in book.All)
        {
            Assert.True(DinerBook.NameKeys(diner).Any(text.Has), diner.Id);
            Assert.True(DinerBook.AboutKeys(diner).Any(text.Has), diner.Id);
        }

        Assert.Equal("The Main Street Diner", book.NameOf(book.First));
        Assert.True(text.Has("replies.snub"));
    }

    [Fact]
    public void The_first_diner_is_on_main_street_and_the_others_are_seen_there_until_their_streets_are_built()
    {
        var scenes = DinerScenes.LoadAll(BaseMods);
        var book = DinerBook.Load(BaseMods, scenes);
        var mainStreet = Assert.Single(scenes);

        Assert.Equal(mainStreet.Id, book.First!.Street);
        foreach (var diner in book.All) Assert.Same(mainStreet, book.SceneOf(diner));

        // The streets still to build are named all the same, for when they are; and they are no problem to show the
        // player at every start
        Assert.Equal(["mainstreet", "hiway", "surfside", "starlite"], book.All.Select(d => d.Street));
        Assert.Empty(book.Problems);
    }

    // ----- the classes -----

    [Fact]
    public void A_car_belongs_at_the_highest_diner_whose_floor_it_reaches()
    {
        var book = Four();

        Assert.Equal("mainstreet", book.Of(150)!.Id);
        Assert.Equal("mainstreet", book.Of(219.9)!.Id);
        Assert.Equal("hiway", book.Of(220)!.Id);
        Assert.Equal("hiway", book.Of(299.9)!.Id);
        Assert.Equal("surfside", book.Of(300)!.Id);
        Assert.Equal("starlite", book.Of(380)!.Id);
        Assert.Equal("starlite", book.Of(1000)!.Id);

        // No figure, or no figure that is one: the beginners' diner
        Assert.Equal("mainstreet", book.Of(null)!.Id);
        Assert.Equal("mainstreet", book.Of(double.NaN)!.Id);
        Assert.Equal("mainstreet", book.Of(-5)!.Id);
        Assert.Null(DinerBook.From([]).Of(300));
    }

    [Fact]
    public void The_regulars_of_a_diner_above_the_players_car_will_not_race_it_and_the_first_diner_turns_nobody_away()
    {
        var book = Four();
        var first = book.Get("mainstreet")!;
        var hiway = book.Get("hiway")!;
        var top = book.Get("starlite")!;

        Assert.False(DinerBook.Snubs(first, 0));
        Assert.False(DinerBook.Snubs(first, null));
        Assert.True(DinerBook.Snubs(hiway, 150));
        Assert.True(DinerBook.Snubs(hiway, 219.9));
        Assert.False(DinerBook.Snubs(hiway, 220));
        Assert.False(DinerBook.Snubs(hiway, 400));
        Assert.True(DinerBook.Snubs(top, null));
        Assert.True(DinerBook.Snubs(top, double.NaN));
    }

    [Fact]
    public void The_racers_out_keep_to_the_diner_of_their_cars_power_and_a_boss_to_the_top_one()
    {
        var book = Four();
        var today = new List<RacerOut>
        {
            Out(Racer("Six", 140)),
            Out(Racer("Small block", 250)),
            Out(Racer("Big block", 410)),
            Out(Racer("The King", 100, boss: true)),
            // No dyno figure: the spec sheet's power stands in
            Out(Racer("Spec sheet", null), bhp: "350bhp"),
            Out(Racer("Unknown", null))
        };

        Assert.Equal(["Six", "Unknown"], RacersOut.AtDiner(today, book.Get("mainstreet")!, book).Select(r => r.Opponent.Name));
        Assert.Equal(["Small block"], RacersOut.AtDiner(today, book.Get("hiway")!, book).Select(r => r.Opponent.Name));
        Assert.Equal(["Spec sheet"], RacersOut.AtDiner(today, book.Get("surfside")!, book).Select(r => r.Opponent.Name));
        Assert.Equal(["Big block", "The King"], RacersOut.AtDiner(today, book.Get("starlite")!, book).Select(r => r.Opponent.Name));
    }

    // ----- the stakes -----

    [Fact]
    public void The_stakes_of_the_place_raise_the_house_limit_and_the_curb_plays_for_the_usual()
    {
        var noon = new DateTime(1969, 6, 1, 12, 0, 0);
        var (_, usual) = MatchupCalculator.WagerLimits("drag", 10_000m, 10_000m, 50, noon);
        var (_, doubled) = MatchupCalculator.WagerLimits("drag", 10_000m, 10_000m, 50, noon, stakes: 2m);
        var (_, odd) = MatchupCalculator.WagerLimits("drag", 10_000m, 10_000m, 50, noon, stakes: 0m);

        Assert.Equal(usual * 2, doubled);
        Assert.Equal(usual, odd);
    }

    // ----- the racing hub -----

    [Fact]
    public void The_hubs_card_says_the_class_who_is_in_the_stakes_and_whether_the_regulars_race_the_players_car()
    {
        var book = Four();
        var gone = new List<string>();
        var first = new DinerCardViewModel(book.First!, book, 0, snubs: false, gone.Add, () => true);
        var top = new DinerCardViewModel(book.Top!, book, 3, snubs: true, gone.Add, () => true);

        Assert.Equal(GameText.Say("racehub.diner.class.any"), first.ClassDisplay);
        Assert.False(first.HasStakes);
        Assert.False(first.Snubs);
        Assert.Equal(GameText.Say("racehub.diner.racers_in.none"), first.RacersInDisplay);

        Assert.Contains("380", top.ClassDisplay);
        Assert.True(top.HasStakes);
        Assert.Contains("3", top.StakesDisplay);
        Assert.True(top.Snubs);
        Assert.Contains("3", top.RacersInDisplay);

        top.GoCommand.Execute(null);
        Assert.Equal(["starlite"], gone);
    }

    // ----- a mod's diners -----

    private ModSet Mods(string? dinersJson, bool baseDiners = true)
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        foreach (var name in new[] { "street.json", "diner.json" })
            _temp.File($"{ModIds.BaseId}/streets/mainstreet/{name}", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "streets", "mainstreet", name)));
        if (baseDiners)
            _temp.File($"{ModIds.BaseId}/diners/diners.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "diners", "diners.json")));
        if (dinersJson != null)
        {
            _temp.File("mymod/mod.json", """{ "id": "mymod" }""");
            _temp.File("mymod/diners/diners.json", dinersJson);
        }

        return ModSet.Load(_temp.Path);
    }

    [Fact]
    public void A_mod_may_replace_a_diner_add_one_above_the_top_and_put_one_on_the_base_games_street()
    {
        var mods = Mods("""
            { "diners": [
                { "replaces": "hiway", "street": "mainstreet", "powerFrom": 200, "stakes": 1.2 },
                { "id": "top", "powerFrom": 500, "stakes": 5 }
            ] }
            """);
        var scenes = DinerScenes.LoadAll(mods);
        var book = DinerBook.Load(mods, scenes);

        Assert.Equal(["mainstreet", "hiway", "surfside", "starlite", "mymod:top"], book.All.Select(d => d.Id));
        Assert.Equal(200, book.Get("hiway")!.PowerFrom);
        Assert.Equal("mymod", book.Get("hiway")!.ModId);
        Assert.Equal(4, book.Top!.Level);
        Assert.Equal("mymod:top", book.Of(600)!.Id);

        // "mainstreet" from a mod means the base game's street, as the mod has none of its own; the new top diner names
        // no street and is seen on the first's, without a word about it
        var mainStreet = Assert.Single(scenes);
        Assert.Same(mainStreet, book.SceneOf(book.Get("hiway")));
        Assert.Null(book.Top.Street);
        Assert.Same(mainStreet, book.SceneOf(book.Top));
        Assert.Empty(book.Problems);
    }

    [Fact]
    public void A_diner_with_no_floor_is_left_out_and_odd_stakes_are_the_usual()
    {
        var mods = Mods("""
            { "diners": [
                { "id": "nowhere", "powerFrom": -10 },
                { "id": "cheap", "powerFrom": 250, "stakes": 0 }
            ] }
            """);
        var book = DinerBook.Load(mods, DinerScenes.LoadAll(mods));

        Assert.Null(book.Get("mymod:nowhere"));
        Assert.Equal(1m, book.Get("mymod:cheap")!.Stakes);
        Assert.Contains(book.Problems, p => p.Contains("nowhere") && p.Contains("left out"));
        Assert.Contains(book.Problems, p => p.Contains("stakes 0"));
    }

    [Fact]
    public void With_no_diners_listed_every_street_with_a_diner_on_it_is_a_diner_of_the_first_level()
    {
        var mods = Mods(null, baseDiners: false);
        var scenes = DinerScenes.LoadAll(mods);
        var book = DinerBook.Load(mods, scenes);

        var only = Assert.Single(book.All);
        Assert.Equal(Assert.Single(scenes).Id, only.Id);
        Assert.Equal(only.Id, only.Street);
        Assert.True(only.IsFirst);
        Assert.Equal(0, only.PowerFrom);
        Assert.False(DinerBook.Snubs(only, 50));
        Assert.Same(scenes[0], book.SceneOf(only));
        Assert.NotEmpty(book.NameOf(only));
    }

    [Fact]
    public void A_way_to_the_diner_that_names_none_or_one_that_is_gone_leads_to_the_first()
    {
        var book = Four();

        Assert.Equal("mainstreet", book.GetOrFirst(null)!.Id);
        Assert.Equal("mainstreet", book.GetOrFirst("closed_down")!.Id);
        Assert.Equal("surfside", book.GetOrFirst("SURFSIDE")!.Id);
    }
}
