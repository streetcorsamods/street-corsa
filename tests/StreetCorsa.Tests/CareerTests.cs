using System.Globalization;
using StreetCorsa.Models.Career.Filters;
using StreetCorsa.Models.Career.Milestones;
using StreetCorsa.Models.Career.Victory;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Scheduler;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Tests;

public class OriginFilterTests
{
    [Theory]
    [InlineData("UK", "Great Britain", true)]
    [InlineData("UK", "United Kingdom", true)]
    [InlineData("UK", "England", true)]
    [InlineData("UK", "Scotland", true)]
    [InlineData("British", "Great Britain", true)]
    [InlineData("European", "United Kingdom", true)]
    [InlineData("USA", "United States of America", true)]
    [InlineData("American", "USA", true)]
    [InlineData("Germany", "West Germany", true)]
    [InlineData("UK", "Japan", false)]
    [InlineData("USA", "Great Britain", false)]
    public void A_country_matches_by_any_of_its_names(string origin, string country, bool matches)
    {
        var car = new CarDefinition { Id = "car", Brand = "Unknown", Country = country };
        Assert.Equal(matches, new OriginFilter(origin).Matches(car));
    }
}

public class CarKindTests
{
    private static readonly CarDefinition Fiat = new() { Id = "fiat", Brand = "Fiat", Country = "Italy" };

    [Fact]
    public void A_car_on_offer_is_worth_what_it_is_offered_for_and_has_the_power_quoted_for_it()
    {
        Assert.True(new ValueFilter(null, 1000m).MatchesOffer(Fiat, null, 900m, null));
        Assert.False(new ValueFilter(null, 1000m).MatchesOffer(Fiat, null, 1100m, null));
        Assert.True(new PowerFilter(null, 100).MatchesOffer(Fiat, null, 900m, 60));
        Assert.False(new PowerFilter(null, 100).MatchesOffer(Fiat, null, 900m, 160));

        // Through an all or any filter too
        var cheapItalian = CompositeCarFilter.And(new OriginFilter("Italy"), new ValueFilter(null, 1000m));
        Assert.True(cheapItalian.MatchesOffer(Fiat, null, 900m, null));
        Assert.False(cheapItalian.MatchesOffer(Fiat, null, 1100m, null));
        Assert.True(cheapItalian.Matches(Fiat, new Car("fiat"), _ => 900m));
    }

    [Theory]
    [InlineData("\"European\"")]
    [InlineData("[ { \"type\": \"origin\", \"origin\": \"European\" } ]")]
    [InlineData("{ \"type\": \"country\" }")]
    public void A_filter_that_does_not_read_is_an_error_not_no_filter(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Throws<System.Text.Json.JsonException>(() => CarFilterJsonConverter.Read(document.RootElement));
    }

    [Fact]
    public void No_filter_is_none()
    {
        using var document = System.Text.Json.JsonDocument.Parse("null");
        Assert.Null(CarFilterJsonConverter.Read(document.RootElement));
    }
}

/// <summary>The victory paths, a made-up setting's (<see cref="TestCareer"/>), played for each test</summary>
[Collection(GlobalsCollection.Name)]
public class VictoryTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IDisposable _played;

    public VictoryTests() => _played = TestPlaces.Play(TestCareer.Mods(_temp));

    public void Dispose()
    {
        _played.Dispose();
        _temp.Dispose();
    }

    private static GameState Career(int wins, int reputation)
    {
        var state = GameState.CreateNew("P");
        state.Career.SetCounter(MilestoneTrigger.TotalWins, wins);
        state.Career.SetCounter(MilestoneTrigger.ReputationReached, reputation);
        return state;
    }

    [Fact]
    public void A_boss_is_known_by_his_mark_not_by_his_name()
    {
        var state = Career(10, 50);
        state.Racers.AddRacer(new Opponent("Elvis", 40, Gender.Male, 99, 60) { IsBoss = true, BossPath = "Crown", Status = RacerStatus.ReadyToRace });
        state.Career.RecordDefeatedOpponent("Elvis");
        var king = new VictoryConditionService().GetVictoryCondition("Crown")!;

        Assert.False(king.IsAchieved(state.Career)); // not looked up yet: no boss to beat

        new VictoryConditionService().RefreshStanding(state);

        Assert.Equal("Elvis", Assert.Single(state.Career.Bosses).Name);
        Assert.True(king.IsAchieved(state.Career));
    }

    [Fact]
    public void A_path_with_two_bosses_is_won_once_both_are_beaten_and_shows_how_far_along()
    {
        var state = Career(10, 50);
        state.Racers.AddRacer(new Opponent("Elvis", 40, Gender.Male, 99, 60) { IsBoss = true, BossPath = "Crown", Status = RacerStatus.ReadyToRace });
        state.Racers.AddRacer(new Opponent("Jerry", 35, Gender.Male, 98, 60) { DefinitionId = "jerry", IsBoss = true, BossPath = "Crown" });
        state.Racers.AddRacer(new Opponent("Other", 35, Gender.Male, 98, 60) { IsBoss = true, BossPath = "Elsewhere" });
        new VictoryConditionService().RefreshStanding(state);
        var king = new VictoryConditionService().GetVictoryCondition("Crown")!;

        state.Career.RecordDefeatedOpponent("Elvis");
        Assert.False(king.IsAchieved(state.Career));
        Assert.Equal(100f * 2.5f / 3f, king.GetProgress(state.Career).Percentage, 0.01f); // both unlock steps and half the goal

        state.Career.RecordDefeatedOpponent("Jerry");
        Assert.True(king.IsAchieved(state.Career));
    }

    [Fact]
    public void A_boss_step_that_names_one_boss_asks_for_him_only()
    {
        var career = new CareerState();
        career.Bosses = [new BossStanding { Id = "king", Name = "Elvis", Path = "King" }, new BossStanding { Id = "big_jim", Name = "Jim", Path = "" }];
        var path = new DataVictory(new VictoryDefinition { Id = "Jims", Goal = [new VictoryStep { Type = VictoryStep.BeatBossType, Boss = "big_jim" }] });

        Assert.False(path.IsAchieved(career));
        career.RecordDefeatedOpponent("Elvis");
        Assert.False(path.IsAchieved(career));
        career.RecordDefeatedOpponent("Jim");
        Assert.True(path.IsAchieved(career));

        // One the game does not have is never beaten
        var nobody = new DataVictory(new VictoryDefinition { Id = "X", Goal = [new VictoryStep { Type = VictoryStep.BeatBossType, Boss = "nobody" }] });
        Assert.False(nobody.IsAchieved(career));
    }

    [Fact]
    public void A_mods_boss_is_its_own_racer_first_and_a_mods_own_path_is_not_the_base_games()
    {
        var career = new CareerState();
        career.Bosses =
        [
            new BossStanding { Id = "king", Name = "Elvis", Path = "King" },
            new BossStanding { Id = "my_mod:jerry", Name = "Jerry", Path = "my_mod:King" },
            new BossStanding { Id = "my_mod:king", Name = "Mod King", Path = "" }
        ];
        var basePath = new DataVictory(new VictoryDefinition { Id = "King", Goal = [new VictoryStep { Type = VictoryStep.BeatBossType }] });
        var modPath = new DataVictory(new VictoryDefinition { Id = "my_mod:King", ModId = "my_mod", Goal = [new VictoryStep { Type = VictoryStep.BeatBossType }] });
        var modNamesKing = new DataVictory(new VictoryDefinition { Id = "my_mod:Other", ModId = "my_mod", Goal = [new VictoryStep { Type = VictoryStep.BeatBossType, Boss = "king" }] });
        var modNamesBaseKing = new DataVictory(new VictoryDefinition { Id = "my_mod:Base", ModId = "my_mod", Goal = [new VictoryStep { Type = VictoryStep.BeatBossType, Boss = "streetcorsa:king" }] });

        career.RecordDefeatedOpponent("Elvis");

        Assert.True(basePath.IsAchieved(career)); // Jerry is my_mod's King path's, not the base game's
        Assert.False(modPath.IsAchieved(career));
        Assert.False(modNamesKing.IsAchieved(career)); // "king" in my_mod is its own king
        Assert.True(modNamesBaseKing.IsAchieved(career));

        career.RecordDefeatedOpponent("Jerry");
        Assert.True(modPath.IsAchieved(career));
    }

    [Fact]
    public void A_bosss_own_lines_go_by_his_full_racer_id_then_as_his_mod_wrote_it()
    {
        Assert.Equal(["my_mod:big_jim", "big_jim"], RacerWords.OwnSuffixes("my_mod:big_jim"));
        Assert.Equal(["king"], RacerWords.OwnSuffixes("king"));
        Assert.Empty(RacerWords.OwnSuffixes(""));
        Assert.Empty(RacerWords.OwnSuffixes(null));

        // A racer of an old save, with no racer id, says what every racer says
        Assert.Equal("replies.decline", RacerWords.Key("replies.decline", new Opponent { DefinitionId = null! }));
    }

    [Fact]
    public void Progress_read_from_the_career_alone_shows_the_standing_worked_out_from_the_game()
    {
        var state = Career(0, 75);
        var service = new VictoryConditionService(getTotalOpponents: _ => 4, playerHasMostWins: _ => true);
        state.Career.RecordDefeatedOpponent("A");

        service.RefreshStanding(state);

        var domination = service.GetVictoryCondition("Domination")!;
        Assert.Equal("Opponents defeated: 1/4", domination.GetProgress(state.Career).ProgressDescription);
        Assert.True(state.Career.PlayerHasMostWins);

        // Another service (the game after a load) reads the same from the save
        var loaded = new VictoryConditionService().GetAllProgress(state.Career);
        Assert.Equal("Opponents defeated: 1/4", loaded["Domination"].ProgressDescription);
    }

    [Fact]
    public void A_won_game_still_keeps_its_standing_up_to_date()
    {
        var state = Career(0, 50);
        state.Career.HasWonGame = true;
        var service = new VictoryConditionService(getTotalOpponents: _ => 7);

        Assert.Null(service.ClaimVictory(state));
        Assert.Equal(7, state.Career.TotalOpponents);
    }
}

public class GameTimeSchedulerTests
{
    private sealed class CountingTask(string id, bool fails) : IScheduledTask
    {
        public string TaskId => id;
        public int IntervalDays => 1;
        public int Runs { get; private set; }

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            Runs++;
            return fails ? throw new InvalidOperationException("broken") : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_task_that_fails_stays_due_and_the_others_run()
    {
        TestLogging.SilenceLogging();
        var state = GameState.CreateNew("P");
        var scheduler = new GameTimeScheduler();
        var broken = new CountingTask("broken", fails: true);
        var fine = new CountingTask("fine", fails: false);
        scheduler.RegisterTask(broken);
        scheduler.RegisterTask(fine);
        var day = new DateTime(1971, 6, 1, 8, 0, 0);

        var failed = await scheduler.OnTimeAdvancedAsync(state, day.AddDays(-1), day);
        Assert.Equal(new[] { "broken" }, failed);
        Assert.Equal(day, state.ScheduledTasks.Single(t => t.TaskId == "fine").LastExecuted);
        Assert.Equal(DateTime.MinValue, state.ScheduledTasks.Single(t => t.TaskId == "broken").LastExecuted);

        // The same day again: the one that ran is not due, the one that failed is
        await scheduler.OnTimeAdvancedAsync(state, day, day.AddHours(2));
        Assert.Equal(2, broken.Runs);
        Assert.Equal(1, fine.Runs);
    }
}

public class GameTimeServiceTests
{
    [Fact]
    public void The_clock_and_the_calendar_read_in_English_on_any_machine()
    {
        TestLogging.SilenceLogging();
        var service = new GameTimeService(new GameTimeScheduler());
        var state = GameState.CreateNew("P");
        state.Date = new DateTime(1971, 6, 3, 14, 5, 0);

        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("2:05 PM", service.GetFormattedTime(state));
            Assert.Equal("June 3, 1971", service.GetFormattedDate(state));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}

public class OpponentChallengeServiceTests
{
    private readonly MemoryCatalog _catalog = new();
    private readonly MemoryProfiles _profiles = new();
    private readonly OpponentChallengeService _service;
    private readonly Player _player;

    public OpponentChallengeServiceTests()
    {
        TestLogging.SilenceLogging();
        _service = new OpponentChallengeService(_catalog, _profiles);
        _player = GameState.CreateNew("P").Player;
        Model("cheap", 1000m);
        Model("dear", 10000m);
    }

    private void Model(string id, decimal price)
    {
        _catalog.UpsertCar(new CarDefinition { Id = id, Name = id, Brand = "Ford" });
        _profiles.UpsertProfile(new CarProfile { CarDefinitionId = id, BasePrice = price });
    }

    private static Opponent Rival(bool king = false) => new("Rex", 30, Gender.Male, 95, 50) { IsBoss = king, Money = 5000m };

    [Fact]
    public void The_King_races_for_pink_slips_and_nothing_else()
    {
        var king = Rival(king: true);

        Assert.True(_service.EvaluateChallenge(king, _player, new Car("cheap"), new Car("dear"), isPinkSlip: true).Accepted);

        var cash = _service.EvaluateChallenge(king, _player, new Car("cheap"), new Car("dear"), isPinkSlip: false, cashWager: 100m);
        Assert.False(cash.Accepted);
        Assert.Equal(ChallengeDeclineReason.NotInterested, cash.DeclineReason);
    }

    [Fact]
    public void A_rival_whose_car_is_laid_up_does_not_race()
    {
        var wreck = new Car("dear") { BodyDamageKmh = [250, 0, 0, 0] };

        var response = _service.EvaluateChallenge(Rival(), _player, new Car("cheap"), wreck, isPinkSlip: false, cashWager: 100m);

        Assert.False(response.Accepted);
        Assert.Equal(ChallengeDeclineReason.CarNotAvailable, response.DeclineReason);
    }

    [Fact]
    public void Nobody_stakes_a_dear_car_against_a_cheap_one()
    {
        var response = _service.EvaluateChallenge(Rival(), _player, new Car("cheap"), new Car("dear"), isPinkSlip: true);

        Assert.False(response.Accepted);
        Assert.Equal(ChallengeDeclineReason.CarValueMismatch, response.DeclineReason);
    }

    [Fact]
    public void A_car_the_catalog_does_not_know_is_not_raced_for()
    {
        var response = _service.EvaluateChallenge(Rival(), _player, new Car("unknown"), new Car("dear"), isPinkSlip: true);

        Assert.False(response.Accepted);
        Assert.Equal(ChallengeDeclineReason.CarNotAvailable, response.DeclineReason);
    }
}
