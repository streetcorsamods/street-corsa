using StreetCorsa.Configuration;
using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.Diners;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Screens.Diner;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Diner;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Talk;

namespace StreetCorsa.Tests;

/// <summary>
/// Crews: racers who ride together (a mod's <c>crews</c>). A racer names their crew, the leader belongs by being named;
/// an event's field may be the crew, its leader in only when asked for; the crew hangs out at its home diner; it has
/// lines of its own and a place in the fact sheet.
/// </summary>
public sealed class CrewTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _acBefore = AppSettings.Instance.AssettoCorsaPath;

    public void Dispose()
    {
        CrewBook.Current = null!;
        AppSettings.Instance.AssettoCorsaPath = _acBefore;
        _temp.Dispose();
    }

    // ----- the mods -----

    private const string Crews = """
        { "crews": [
          { "id": "road_kings", "name": "The Road Kings", "description": "Red jackets, the diner's back booth.", "colour": "#B22222", "home": "drivein", "boss": "king" },
          { "id": "deuces", "name": "The Deuces", "colour": "plaid" }
        ] }
        """;

    private const string Racers = """
        { "opponents": [
          { "opponentId": "king", "name": "The King", "age": 41, "gender": "Male", "skill": 100, "aggression": 65, "boss": true },
          { "opponentId": "eddie", "name": "Eddie Collins", "age": 32, "gender": "Male", "skill": 92, "aggression": 72, "crew": "road_kings" },
          { "opponentId": "ray", "name": "Raymond Turner", "age": 41, "gender": "Male", "skill": 90, "aggression": 64, "crew": "road_kings" },
          { "opponentId": "lou", "name": "Louise Bennett", "age": 29, "gender": "Female", "skill": 99, "aggression": 74, "crew": "nowhere" },
          { "opponentId": "sam", "name": "Sam Whitaker", "age": 35, "gender": "Male", "skill": 93, "aggression": 50 }
        ] }
        """;

    /// <summary>The core alone, with the crews and racers above as its own (bare ids)</summary>
    private ModSet Core()
    {
        _temp.File("streetcorsa/mod.json", "{}");
        _temp.File("streetcorsa/crews/crews.json", Crews);
        _temp.File("streetcorsa/racers/racers.json", Racers);
        return ModSet.Load(_temp.Path);
    }

    /// <summary>An add-on with the same, its ids namespaced</summary>
    private ModSet AddOn(string? more = null)
    {
        _temp.File("streetcorsa/mod.json", "{}");
        _temp.File("rivals/mod.json", """{ "id": "rivals" }""");
        _temp.File("rivals/crews/crews.json", Crews);
        _temp.File("rivals/racers/racers.json", Racers);
        if (more != null) _temp.File("later/mod.json", more);
        return ModSet.Load(_temp.Path);
    }

    [Fact]
    public void Crews_load_with_their_mods_namespace_and_a_colour_that_is_none_is_dropped()
    {
        var book = CrewBook.Load(AddOn());

        var kings = book.Get("rivals:road_kings");
        Assert.NotNull(kings);
        Assert.Equal("The Road Kings", kings!.Name);
        Assert.Equal("#B22222", kings.Colour);
        Assert.Equal("drivein", kings.Home);
        Assert.Equal("king", kings.Boss);
        Assert.Equal("rivals", kings.ModId);

        var deuces = book.Get("rivals:deuces")!;
        Assert.Null(deuces.Colour);
        Assert.Contains(book.Problems, p => p.Contains("plaid") && p.Contains("no colour"));
    }

    [Fact]
    public void A_racer_rides_with_the_crew_he_names_and_the_leader_with_the_crew_that_names_him()
    {
        var racers = new OpponentRepository(AddOn()).LoadAllOpponents();
        Opponent Of(string id) => racers.Single(r => r.DefinitionId == "rivals:" + id);

        Assert.Equal("rivals:road_kings", Of("eddie").Crew);
        Assert.Equal("rivals:road_kings", Of("ray").Crew);
        Assert.Equal("rivals:road_kings", Of("king").Crew);
        Assert.Equal("", Of("sam").Crew);

        // A crew no mod has: told, and she rides alone
        Assert.Equal("", Of("lou").Crew);
        Assert.Contains(ModProblems.All, l => l.Contains("nowhere") && l.Contains("rides alone"));
    }

    [Fact]
    public void A_crew_a_later_mod_removes_is_gone_and_its_racers_ride_alone()
    {
        var mods = AddOn("""{ "id": "later", "requires": { "rivals": "" }, "removes": { "crews": ["rivals:road_kings"] } }""");

        var book = CrewBook.Load(mods);
        Assert.Null(book.Get("rivals:road_kings"));
        Assert.NotNull(book.Get("rivals:deuces"));

        var racers = new OpponentRepository(mods).LoadAllOpponents();
        Assert.Equal("", racers.Single(r => r.DefinitionId == "rivals:eddie").Crew);
    }

    // ----- a field from a crew -----

    private static CarDefinition Car(string id, string brand) => new() { Id = id, Name = id, Brand = brand, Status = ContentStatus.Active };

    private MemoryCatalog Installed(params CarDefinition[] cars)
    {
        var catalog = new MemoryCatalog();
        foreach (var car in cars)
        {
            catalog.UpsertCar(car);
            Directory.CreateDirectory(_temp.Combine("ac", "content", "cars", car.Id));
        }

        AppSettings.Instance.AssettoCorsaPath = _temp.Combine("ac");
        return catalog;
    }

    private static Opponent Out(GameState state, string id, string name, string crew = "", bool boss = false)
    {
        var racer = new Opponent(name, 30, Gender.Male, 95, 50) { DefinitionId = id, Crew = crew, IsBoss = boss, Status = RacerStatus.ReadyToRace, Money = 1000m };
        racer.Cars.Add(new Car("car_" + id));
        state.Racers.AddRacer(racer);
        return racer;
    }

    /// <summary>The scene: the crew's two members and its leader (a boss) out, and two racers of no crew</summary>
    private (GameState State, EventOpponentService Events) Scene()
    {
        CrewBook.Current = CrewBook.Load(Core());
        var catalog = Installed(Car("car_eddie", "Ford"), Car("car_ray", "Ford"), Car("car_king", "Ford"), Car("car_sam", "Ford"), Car("car_lou", "Ford"));
        var state = GameState.CreateNew("P");
        Out(state, "eddie", "Eddie Collins", "road_kings");
        Out(state, "ray", "Raymond Turner", "road_kings");
        Out(state, "king", "The King", "road_kings", boss: true);
        Out(state, "sam", "Sam Whitaker");
        Out(state, "lou", "Louise Bennett");
        return (state, new EventOpponentService(new CarFilterService(), catalog, random: new Random(3)));
    }

    private static RaceEventDefinition CrewNight(int count, bool boss) => new()
    {
        Id = "club_night", Format = "circuit",
        Field = new EventField { Count = count, Min = count, Max = count, From = EventFieldFrom.Crew, Crew = "road_kings", Boss = boss }
    };

    [Fact]
    public void A_field_from_a_crew_is_its_members_and_nobody_else_the_leader_sitting_out()
    {
        var (state, events) = Scene();

        var field = events.GetOpponentForEvent(CrewNight(5, boss: false), state)!.All().Select(r => r.OpponentName).ToList();

        Assert.Equal(2, field.Count);
        Assert.Contains("Eddie Collins", field);
        Assert.Contains("Raymond Turner", field);
    }

    [Fact]
    public void Asked_for_the_boss_the_crews_leader_races_with_his_crew_though_a_boss_races_no_other_event()
    {
        var (state, events) = Scene();

        var field = events.GetOpponentForEvent(CrewNight(5, boss: true), state)!.All().Select(r => r.OpponentName).ToList();
        Assert.Equal(3, field.Count);
        Assert.Contains("The King", field);

        // The pool's events still leave him out
        var pool = events.GetOpponentForEvent(new RaceEventDefinition { Id = "open", Format = "circuit", Field = new EventField { Count = 5, Min = 5, Max = 5 } }, state)!
            .All().Select(r => r.OpponentName).ToList();
        Assert.DoesNotContain("The King", pool);
        Assert.Equal(5, pool.Count); // four of the scene and one made up
    }

    [Fact]
    public void A_field_of_named_racers_is_those_who_are_there_with_no_strangers_made_up()
    {
        var (state, events) = Scene();
        var named = new RaceEventDefinition
        {
            Id = "named", Format = "circuit",
            Field = new EventField { Count = 6, Min = 6, Max = 6, From = EventFieldFrom.Racers, Racers = ["sam", "lou", "nobody"] }
        };

        var field = events.GetOpponentForEvent(named, state)!.All().Select(r => r.OpponentName).ToList();

        Assert.Equal(["Louise Bennett", "Sam Whitaker"], field.OrderBy(n => n).ToList());
    }

    [Fact]
    public void A_crew_night_with_nobody_of_the_crew_out_has_no_rival()
    {
        var (state, events) = Scene();
        foreach (var racer in state.Racers.ReadyToRace.Values.OfType<Opponent>().Where(o => o.Crew.Length > 0).ToList())
            state.Racers.MoveRacer(racer.Name, RacerStatus.Inactive);

        Assert.Null(events.GetOpponentForEvent(CrewNight(3, boss: true), state));
    }

    [Fact]
    public void An_events_field_from_a_crew_is_held_to_a_crew_some_mod_has_and_boss_to_a_crews_field()
    {
        _temp.File("streetcorsa/formats/formats.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "formats", "formats.json")));
        _temp.File("streetcorsa/events/events.json", """
            { "events": [
              { "id": "club_night", "format": "circuit", "field": { "count": 3, "from": "crew", "crew": "road_kings", "boss": true } },
              { "id": "ghost_night", "format": "circuit", "field": { "count": 3, "from": "crew", "crew": "ghosts" } },
              { "id": "nameless_night", "format": "circuit", "field": { "count": 3, "from": "crew" } },
              { "id": "open_night", "format": "circuit", "field": { "count": 3, "boss": true } }
            ] }
            """);
        var career = CareerContent.Load(Core());

        var club = career.Events["club_night"].Field!;
        Assert.Equal(EventFieldFrom.Crew, club.From);
        Assert.Equal("road_kings", club.Crew);
        Assert.True(club.Boss);

        Assert.Equal(EventFieldFrom.Pool, career.Events["ghost_night"].Field!.From);
        Assert.Contains(career.Problems, p => p.Contains("ghost_night") && p.Contains("ghosts") && p.Contains("no mod has"));
        Assert.Equal(EventFieldFrom.Pool, career.Events["nameless_night"].Field!.From);
        Assert.Contains(career.Problems, p => p.Contains("nameless_night") && p.Contains("names none"));
        Assert.False(career.Events["open_night"].Field!.Boss);
        Assert.Contains(career.Problems, p => p.Contains("open_night") && p.Contains("not a crew's"));
    }

    // ----- where they hang out, what they say -----

    [Fact]
    public void A_crews_members_are_regulars_of_its_home_diner_whatever_they_drive()
    {
        CrewBook.Current = CrewBook.Load(Core());
        var book = DinerBook.From([new Diner { Id = "corner", PowerFrom = 0 }, new Diner { Id = "drivein", PowerFrom = 380 }]);

        var eddie = new Opponent("Eddie Collins", 32, Gender.Male, 92, 72) { DefinitionId = "eddie", Crew = "road_kings" };
        eddie.Cars.Add(new Car("car") { PowerHp = 120 });
        var sam = new Opponent("Sam Whitaker", 35, Gender.Male, 93, 50) { DefinitionId = "sam" };
        sam.Cars.Add(new Car("car") { PowerHp = 120 });
        var deuce = new Opponent("Dee", 25, Gender.Female, 93, 50) { DefinitionId = "dee", Crew = "deuces" };
        deuce.Cars.Add(new Car("car") { PowerHp = 400 });

        Assert.Equal("drivein", book.Of(eddie, eddie.Cars[0], null)!.Id);
        Assert.Equal("corner", book.Of(sam, sam.Cars[0], null)!.Id);
        // A crew with no home: each where their car puts them
        Assert.Equal("drivein", book.Of(deuce, deuce.Cars[0], null)!.Id);
    }

    [Fact]
    public void A_racers_keys_are_his_own_then_his_crews_then_everybodys()
    {
        var eddie = new Opponent("Eddie", 32, Gender.Male, 92, 72) { DefinitionId = "rivals:eddie", Crew = "rivals:road_kings" };

        Assert.Equal(["rivals:eddie", "eddie", "crew.rivals:road_kings", "crew.road_kings"], RacerWords.Suffixes(eddie).ToList());
        Assert.Empty(RacerWords.CrewSuffixes(""));
        Assert.Equal("talk.pulled_up", RacerWords.Key("talk.pulled_up", eddie));
    }

    [Fact]
    public void The_fact_sheet_tells_the_crew_its_leader_and_that_the_player_beat_him()
    {
        CrewBook.Current = CrewBook.Load(Core());
        var state = GameState.CreateNew("P");
        var king = Out(state, "king", "The King", "road_kings", boss: true);
        var eddie = Out(state, "eddie", "Eddie Collins", "road_kings");
        Out(state, "sam", "Sam Whitaker");

        var facts = TalkFacts.Rival(eddie, id => id, TalkFacts.CrewFacts.Of(eddie, CrewBook.Current, state.Racers.All, state.Career.DefeatedOpponentIds)).ToList();
        Assert.Contains(facts, f => f.Contains("The Road Kings"));
        Assert.Contains("Red jackets, the diner's back booth.", facts);
        Assert.Contains(facts, f => f.Contains("The King leads The Road Kings"));
        Assert.DoesNotContain(facts, f => f.Contains("not forgotten"));

        state.Career.RecordDefeatedOpponent(king.Name);
        facts = TalkFacts.Rival(eddie, id => id, TalkFacts.CrewFacts.Of(eddie, CrewBook.Current, state.Racers.All, state.Career.DefeatedOpponentIds)).ToList();
        Assert.Contains(facts, f => f.Contains("not forgotten"));

        var own = TalkFacts.Rival(king, id => id, TalkFacts.CrewFacts.Of(king, CrewBook.Current, state.Racers.All, state.Career.DefeatedOpponentIds)).ToList();
        Assert.Contains(own, f => f.Contains("You lead The Road Kings"));

        var sam = state.Racers.ReadyToRace.Values.OfType<Opponent>().Single(o => o.DefinitionId == "sam");
        Assert.Null(TalkFacts.CrewFacts.Of(sam, CrewBook.Current, state.Racers.All, state.Career.DefeatedOpponentIds));
    }

    [Fact]
    public void The_diner_shows_the_crew_under_the_nickname_in_its_colour_the_leader_marked()
    {
        CrewBook.Current = CrewBook.Load(Core());
        var eddie = new OpponentDisplayViewModel { Opponent = new Opponent("Eddie", 32, Gender.Male, 92, 72) { DefinitionId = "eddie", Crew = "road_kings" } };
        var king = new OpponentDisplayViewModel { Opponent = new Opponent("The King", 41, Gender.Male, 100, 65) { DefinitionId = "king", Crew = "road_kings" } };
        var sam = new OpponentDisplayViewModel { Opponent = new Opponent("Sam", 35, Gender.Male, 93, 50) { DefinitionId = "sam" } };

        Assert.True(eddie.HasCrew);
        Assert.Equal("The Road Kings", eddie.CrewDisplay);
        Assert.NotNull(eddie.CrewBrush);
        Assert.Contains("The Road Kings", king.CrewDisplay);
        Assert.Contains("leader", king.CrewDisplay);
        Assert.False(sam.HasCrew);
        Assert.Null(OpponentDisplayViewModel.CrewColour("plaid"));
        Assert.Null(OpponentDisplayViewModel.CrewColour(null));
    }
}
