using LiteDB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Storage;

namespace StreetCorsa.Tests;

/// <summary>
/// A field: the player and several rivals in one race. race.ini lines them up, the result is settled as a race of two
/// with each rival, and an event pays its purse by place.
/// </summary>
[Collection(RaceSessionTests.Name)]
public sealed class RaceFieldTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ----- race.ini -----

    private string[] RaceIni(DragRaceIntent intent)
    {
        var cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(cfg);
        Assert.True(new IniModificationService(cfg, _temp.Combine("restore")).ApplyIntent(intent));
        return File.ReadAllLines(Path.Combine(cfg, "race.ini"));
    }

    private static DragRaceIntent FieldIntent(string format) => new()
    {
        PlayerCarId = "car_a", OpponentCarId = "car_b", PlayerName = "P", OpponentName = "O", Format = format,
        TrackId = "ks_highlands", TrackConfig = "layout_int",
        Field =
        [
            new FieldCar { CarId = "car_c", Skin = "red", Name = "Second", AILevel = 93, AIAggression = 40 },
            new FieldCar { CarId = "car_b__sr_opponent", Skin = "blue", Name = "Third", AILevel = 97, AIAggression = 70,
                Start = new RaceStartState { EngineLife = 0.5 } }
        ],
        PlayerGridPosition = 3,
        Stagger = 2,
        Police = new RacePolice { CarId = "dodge_monaco_74", Skins = ["police_a", "police_b"], SpotShare = 0.4 }
    };

    [Fact]
    public void A_field_goes_into_race_ini_rival_after_rival_with_the_police_after_them_all()
    {
        var lines = RaceIni(FieldIntent("circuit"));

        Assert.Contains("CARS=6", lines);
        Assert.Contains("[CAR_2]", lines);
        Assert.Contains("[CAR_3]", lines);
        Assert.Contains("[CAR_5]", lines);
        Assert.DoesNotContain("[CAR_6]", lines);
        Assert.Contains("DRIVER_NAME=Second", lines);
        Assert.Contains("MODEL=car_b__sr_opponent", lines);
        Assert.Contains("AI_LEVEL=97", lines);
        Assert.Contains("RIVALS=3", lines);
        Assert.Contains("STAGGER=2.0", lines);
        Assert.Contains("POLICE=4,5", lines);
        Assert.Contains("STARTING_POSITION=3", lines);
        Assert.Contains(lines, l => l.StartsWith("CAR_3_ENGINE_LIFE="));
        Assert.Equal(2, lines.Count(l => l == "MODEL=dodge_monaco_74"));
    }

    [Fact]
    public void A_format_of_one_on_one_races_the_opponent_alone_whatever_the_field()
    {
        var lines = RaceIni(FieldIntent("drag"));

        Assert.Contains("CARS=2", lines);
        Assert.DoesNotContain("[CAR_2]", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("RIVALS=") || l.StartsWith("STAGGER=") || l.StartsWith("CAR_2_") || l.StartsWith("CAR_3_"));
        Assert.Contains("STARTING_POSITION=2", lines);
    }

    [Fact]
    public void A_race_of_two_says_nothing_of_rivals()
    {
        var intent = FieldIntent("circuit");
        intent.Field = [];
        intent.PlayerGridPosition = null;
        var lines = RaceIni(intent);

        Assert.Contains("CARS=4", lines);
        Assert.Contains("POLICE=2,3", lines);
        Assert.Contains("STARTING_POSITION=1", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("RIVALS=") || l.StartsWith("STAGGER="));
    }

    [Fact]
    public void Every_later_car_of_a_model_races_in_a_copy_of_its_own()
    {
        var models = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["car_a"] = 1 };

        Assert.Equal("car_b", RaceSetupBuilder.RacesAs("car_b", null, models).RacesAs);
        Assert.Equal("car_a__sr_opponent", RaceSetupBuilder.RacesAs("car_a", null, models).RacesAs);
        Assert.Equal("car_a_2__sr_opponent", RaceSetupBuilder.RacesAs("car_a", null, models).RacesAs);
        var copy = RaceSetupBuilder.RacesAs("car_b", null, models);
        Assert.Equal("car_b__sr_opponent", copy.RacesAs);
        Assert.Equal("car_b__sr_opponent", copy.Data!.CloneId);
    }

    // ----- settling a field -----

    private sealed class FakeRepository : IGameStateRepository
    {
        public List<ProcessedRaceSession> Recorded { get; } = new();

        public void Save(GameState state, string saveName, Action<LiteDatabase>? sameTransaction)
        {
            using var db = new LiteDatabase(new MemoryStream());
            sameTransaction?.Invoke(db);
            Recorded.AddRange(db.GetCollection<ProcessedRaceSession>("ProcessedRaceSessions").FindAll());
        }

        public void Save(GameState state, string saveName) => Save(state, saveName, null);
        public GameState? Load(string saveName) => throw new NotSupportedException();
        public bool Exists(string saveName) => throw new NotSupportedException();
        public void Delete(string saveName) => throw new NotSupportedException();
        public List<string> ListSaves() => throw new NotSupportedException();
        public GameState CreateNew(string saveName, string playerName, GameRules? rules = null) => throw new NotSupportedException();
        public void Dispose() { }
    }

    /// <summary>The events' service with one event the tests give, completing its instance as the game's does</summary>
    private sealed class OneEvent(RaceEventDefinition definition) : IRaceEventService
    {
        public IEnumerable<RaceEventDefinition> GetAllEventDefinitions() => [definition];
        public RaceEventDefinition? GetEventDefinition(string eventId) => eventId == definition.Id ? definition : null;
        public IEnumerable<RaceEventDefinition> GetEligibleEvents(CareerState career) => [];
        public IEnumerable<RaceEventInstance> GetActiveEvents(CareerState career, DateTime currentTime) => [];
        public List<RaceEventInstance> GenerateEvents(CareerState career, DateTime currentTime, double pinkSlipFactor = 1.0) => [];
        public bool CanEnterEvent(string eventId, string carDefinitionId, Car? carInstance, CareerState career) => true;

        public EventReward? CompleteEvent(Guid eventInstanceId, bool playerWon, CareerState career, DateTime completedAt)
        {
            var state = career.ActiveEvents.First(e => e.InstanceId == eventInstanceId);
            state.IsCompleted = true;
            state.PlayerWon = playerWon;
            return playerWon ? definition.Reward : null;
        }

        public int CleanupExpiredEvents(CareerState career, DateTime currentTime) => 0;
    }

    private readonly FakeRepository _repository = new();

    private RaceResultProcessor Processor(IRaceEventService? events = null) =>
        new(_repository, new RaceSessionRepository(new SaveDatabase(_temp.Combine("unused-saves"))), new RaceFakes.FakeCareer(), events ?? new RaceFakes.FakeEvents());

    /// <summary>The player and four rivals on a road course: the race's opponent and three more, each in a car of their own</summary>
    private static (GameState State, RaceContext Context, List<Opponent> Rivals) Field()
    {
        var world = RaceFakes.World(wager: 0, format: "circuit");
        var rivals = new List<Opponent> { world.Rival };
        foreach (var name in new[] { "Second", "Third", "Fourth" })
        {
            var rival = new Opponent(name, 30, Gender.Male, 95, 50) { Money = 5000m };
            var car = new Car("car_" + name.ToLowerInvariant()) { InstanceId = Guid.NewGuid(), EngineHealth = 1, TransmissionHealth = 1, BodyCondition = 1, TireCondition = 1 };
            rival.Cars.Add(car);
            world.State.Racers.AddRacer(rival);
            world.Context.Field.Add(new FieldEntrant { Name = name, CarInstanceId = car.InstanceId, CarDefinitionId = car.DefinitionId });
            rivals.Add(rival);
        }

        return (world.State, world.Context, rivals);
    }

    /// <summary>The field's result: the racers by car, each at the place given (car 0 the player)</summary>
    private static RaceResultJson Result(params int[] places)
    {
        var json = RaceResultValidatorTests.Fixture("1.1", RaceFakes.PlayerName, RaceFakes.OpponentName);
        var list = (JArray)json["participants"]!;
        var names = new[] { RaceFakes.PlayerName, RaceFakes.OpponentName, "Second", "Third", "Fourth" };
        for (var i = 2; i < places.Length; i++)
        {
            var rival = (JObject)list[1].DeepClone();
            rival["driver_name"] = names[i];
            rival["car_index"] = i;
            list.Add(rival);
        }
        for (var i = 0; i < places.Length; i++) list[i]["performance"]!["final_position"] = places[i];
        json["session"]!["track_id"] = "ks_highlands";
        return JsonConvert.DeserializeObject<RaceResultJson>(json.ToString())!;
    }

    [Fact]
    public void A_field_is_settled_with_each_rival_as_a_race_of_two()
    {
        var (state, context, rivals) = Field();
        var result = Result(2, 1, 3, 4, 5);
        result.Participants[4].Crash.Crashed = true;

        var messages = Processor().ProcessRaceResult(result, context, state);

        // The player's record takes the field once, a loss: the opponent beat them
        Assert.Equal(0, state.Player.Stats.Wins);
        Assert.Equal(1, state.Player.Stats.Losses);
        Assert.Equal(1, state.Player.Stats.Races);
        Assert.Equal(1, rivals[0].Stats.Wins);
        Assert.All(rivals.Skip(1), r => Assert.Equal(1, r.Stats.Losses));

        // Every rival the player finished ahead of counts as beaten, the crashed one too; the winner doesn't
        Assert.Equal(new[] { "Fourth", "Second", "Third" }, state.Career.DefeatedOpponentIds.Order());
        Assert.Null(state.PendingRace);
        Assert.Equal(1_000_000m, state.Player.Money);

        var session = Assert.Single(_repository.Recorded);
        Assert.Equal(2, session.PlayerPlace);
        Assert.Equal(new[] { "Second", "Third", "Fourth" }, session.Field);
        Assert.Equal(new[] { RaceFakes.OpponentName }, session.AheadOfPlayer);
        Assert.True(session.BeatPlayer(RaceFakes.OpponentName));
        Assert.False(session.BeatPlayer("Third"));
        Assert.True(session.Raced("fourth"));
        Assert.Equal(RaceFakes.OpponentName, session.WinnerName);
        Assert.Equal(nameof(WinCondition.FieldFinish), session.WinCondition);

        // The card first: the place, then everyone in order, the crashed one out
        Assert.Equal("2nd of 5", messages[0].Title);
        Assert.Contains("5. Fourth (out)", messages[0].Text);
        Assert.StartsWith("1. " + RaceFakes.OpponentName, messages[0].Text);
    }

    [Fact]
    public void Beating_every_rival_wins_the_field()
    {
        var (state, context, rivals) = Field();

        var messages = Processor().ProcessRaceResult(Result(1, 2, 3, 4, 5), context, state);

        Assert.Equal(1, state.Player.Stats.Wins);
        Assert.All(rivals, r => Assert.Equal(1, r.Stats.Losses));
        Assert.Equal(4, state.Career.DefeatedOpponentIds.Count);
        Assert.Equal(GameTextTitle("result.field.won.title"), messages[0].Title);
        // The record's loser is the runner-up
        Assert.Equal(RaceFakes.OpponentName, Assert.Single(_repository.Recorded).LoserName);
    }

    private static string GameTextTitle(string key) => StreetCorsa.Services.Text.GameText.Say(key);

    [Fact]
    public void A_false_start_in_a_field_is_no_contest_for_anyone()
    {
        var (state, context, rivals) = Field();
        var result = Result(1, 2, 3, 4, 5);
        result.Participants[0].FalseStart = true;

        var messages = Processor().ProcessRaceResult(result, context, state);

        Assert.Equal(0, state.Player.Stats.Races);
        Assert.All(rivals, r => Assert.Equal(0, r.Stats.Races));
        Assert.Empty(state.Career.DefeatedOpponentIds);
        Assert.Equal(GameTextTitle("result.field.no_contest.title"), messages[0].Title);
    }

    [Fact]
    public void The_police_catching_one_rival_of_a_field_fine_them_and_take_their_car()
    {
        var (state, context, rivals) = Field();
        var result = Result(1, 2, 3, 4, 5);
        result.Pursuit = new PursuitResult
        {
            Police = 2, Started = true, Player = PursuitOutcomes.Escaped,
            Racers = [new PursuitRacer { CarIndex = 3, Chased = true, Outcome = PursuitOutcomes.Busted }]
        };

        Processor().ProcessRaceResult(result, context, state);

        Assert.Equal(1, rivals[2].Stats.PoliceBusts);
        Assert.NotNull(rivals[2].Cars[0].ImpoundedUntil);
        Assert.All(rivals.Where((_, i) => i != 2), r => Assert.Equal(0, r.Stats.PoliceBusts));
        Assert.Equal(1, state.Player.Stats.PoliceEscapes);
        Assert.Equal(1, state.Player.Stats.Wins);
    }

    [Fact]
    public void An_events_purse_is_paid_by_place_from_the_pot_and_the_entry_is_paid_win_or_lose()
    {
        var (state, context, _) = Field();
        var definition = new RaceEventDefinition
        {
            Id = "club_night", Name = "Club Night", Format = "circuit", Entry = 20,
            Field = new EventField { Min = 4, Max = 4 },
            Reward = new EventReward
            {
                Cash = 100, Reputation = 6,
                Places = [new PlaceReward { Share = 0.6, Reputation = 6 }, new PlaceReward { Share = 0.3, Reputation = 3 }, new PlaceReward { Share = 0.1 }]
            }
        };
        var instance = new RaceEventState { EventDefinitionId = definition.Id, AvailableFrom = state.Date };
        state.Career.ActiveEvents.Add(instance);
        context.EventId = definition.Id;
        context.EventInstanceId = instance.InstanceId;
        state.Rules.RacePrizeMultiplier = 1.0;

        Processor(new OneEvent(definition)).ProcessRaceResult(Result(2, 1, 3, 4, 5), context, state);

        // The pot: the prize and five entries, 200; second takes 30% of it, less the player's own entry
        Assert.Equal(1_000_000m - 20m + 60m, state.Player.Money);
        Assert.Equal(3, state.Player.Stats.EventReputationBonus);
        Assert.Equal(2, instance.PlayerPlace);
        Assert.True(instance.IsCompleted);
        Assert.False(instance.PlayerWon);
    }

    /// <summary>A club night of four rivals with a $20 entry and a purse for three places, entered by the player</summary>
    private static (RaceEventDefinition Definition, RaceEventState Instance) ClubNight(GameState state, RaceContext context, string? specialItem = null)
    {
        var definition = new RaceEventDefinition
        {
            Id = "club_night", Name = "Club Night", Format = "circuit", Entry = 20,
            Field = new EventField { Min = 4, Max = 4 },
            Reward = new EventReward
            {
                Cash = 100, Reputation = 6, SpecialItem = specialItem,
                Places = [new PlaceReward { Share = 0.6, Reputation = 6 }, new PlaceReward { Share = 0.3, Reputation = 3 }, new PlaceReward { Share = 0.1 }]
            }
        };
        var instance = new RaceEventState { EventDefinitionId = definition.Id, AvailableFrom = state.Date };
        state.Career.ActiveEvents.Add(instance);
        context.EventId = definition.Id;
        context.EventInstanceId = instance.InstanceId;
        state.Rules.RacePrizeMultiplier = 1.0;
        return (definition, instance);
    }

    [Fact]
    public void A_player_out_of_the_race_is_not_paid_the_place_the_mode_gave_them()
    {
        var (state, context, _) = Field();
        var (definition, instance) = ClubNight(state, context, specialItem: "rare_camshaft");
        // Ahead on the mode's order (a best lap, the pit lane's spline), but wrecked: every rival beat them
        var result = Result(1, 2, 3, 4, 5);
        result.Participants[0].Crash.Crashed = true;

        var messages = Processor(new OneEvent(definition)).ProcessRaceResult(result, context, state);

        Assert.Equal(5, instance.PlayerPlace);
        Assert.False(instance.PlayerWon);
        Assert.Equal(0m, state.Player.Stats.TotalEarnings);
        Assert.Equal(0, state.Player.Stats.EventReputationBonus);
        // No prize camshaft or its worth in cash: the entry is all that moved
        Assert.Equal(1_000_000m - 20m, state.Player.Money);
        Assert.Contains($"5. {RaceFakes.PlayerName} (out)", messages[0].Text);
    }

    [Fact]
    public void A_field_event_no_longer_open_pays_nothing_and_takes_no_entry()
    {
        var (state, context, _) = Field();
        var (definition, instance) = ClubNight(state, context);
        state.Career.ActiveEvents.Remove(instance);

        Processor(new OneEvent(definition)).ProcessRaceResult(Result(1, 2, 3, 4, 5), context, state);

        Assert.Equal(1_000_000m, state.Player.Money);
        Assert.Equal(0, state.Player.Stats.EventReputationBonus);
    }

    [Fact]
    public void A_race_of_two_events_entry_is_paid_and_the_winner_takes_both()
    {
        var world = RaceFakes.World(wager: 0, format: "circuit");
        var (definition, instance) = ClubNight(world.State, world.Context);
        definition.Field = null;
        definition.Reward.Places = [];

        Processor(new OneEvent(definition)).ProcessRaceResult(Result(1, 2), world.Context, world.State);

        // The prize and both cars' entry, less the player's own
        Assert.True(instance.PlayerWon);
        Assert.Equal(1_000_000m - 20m + 140m, world.State.Player.Money);
    }

    [Fact]
    public void A_places_own_cash_is_scaled_by_the_difficulty_as_the_pot_is()
    {
        var reward = new EventReward { Cash = 1000, Places = [new PlaceReward { Share = 0.5 }, new PlaceReward { Cash = 300 }] };

        var halved = reward.ScaledBy(0.5);

        Assert.Equal(250, halved.ForPlace(1, halved.Cash).Cash);
        Assert.Equal(150, halved.ForPlace(2, halved.Cash).Cash);
        Assert.Equal(300, reward.Places[1].Cash);
    }

    // ----- the purse, the grid, the chase, the record -----

    [Fact]
    public void A_place_takes_its_share_of_the_pot_or_its_own_cash()
    {
        var reward = new EventReward
        {
            Cash = 500, Reputation = 9,
            Places = [new PlaceReward { Share = 0.5, Reputation = 9 }, new PlaceReward { Cash = 77, Reputation = 2 }, new PlaceReward { Reputation = -1 }]
        };

        Assert.Equal(new EventReward(500, 9).Cash, reward.ForPlace(1, 1000).Cash);
        Assert.Equal(75, reward.ForPlace(2, 1000).Cash);
        Assert.Equal(-1, reward.ForPlace(3, 1000).Reputation);
        Assert.Equal(0, reward.ForPlace(4, 1000).Cash);

        var winnerTakesAll = new EventReward(500, 9);
        Assert.Equal(1000, winnerTakesAll.ForPlace(1, 1000).Cash);
        Assert.Equal(9, winnerTakesAll.ForPlace(1, 1000).Reputation);
        Assert.Equal(0, winnerTakesAll.ForPlace(2, 1000).Cash);
    }

    private static List<EventOpponentResult> Rivals(params int[] reputations) =>
        reputations.Select((r, i) => new EventOpponentResult { OpponentName = $"R{i}", Reputation = r }).ToList();

    [Fact]
    public void A_grid_by_reputation_puts_the_best_known_at_the_front_and_the_player_among_them_by_theirs()
    {
        var (order, player) = EventGrid.Order(EventFieldGrids.Reputation, 25, Rivals(10, 40, 30, 20), seed: 1);

        Assert.Equal(new[] { "R1", "R2", "R3", "R0" }, order.Select(r => r.OpponentName));
        Assert.Equal(3, player);
        Assert.Equal(5, EventGrid.Order(EventFieldGrids.PlayerLast, 0, Rivals(1, 2, 3, 4), 1).PlayerPosition);
        Assert.Equal(1, EventGrid.Order(EventFieldGrids.PlayerFirst, 0, Rivals(1, 2, 3, 4), 1).PlayerPosition);

        // A random grid is the same every time for one invitation
        var once = EventGrid.Order(EventFieldGrids.Random, 0, Rivals(1, 2, 3, 4, 5, 6), 42);
        var again = EventGrid.Order(EventFieldGrids.Random, 0, Rivals(1, 2, 3, 4, 5, 6), 42);
        Assert.Equal(once.Rivals.Select(r => r.OpponentName), again.Rivals.Select(r => r.OpponentName));
        Assert.Equal(once.PlayerPosition, again.PlayerPosition);
        Assert.InRange(once.PlayerPosition, 1, 7);
    }

    [Fact]
    public void A_field_is_cut_to_a_pit_box_for_every_car()
    {
        var rivals = Rivals(1, 2, 3, 4, 5, 6);

        Assert.Equal(3, EventGrid.Fit(rivals, "4").Count);
        Assert.Equal(6, EventGrid.Fit(rivals, "").Count);
        Assert.Equal(6, EventGrid.Fit(rivals, "40").Count);
        Assert.Single(EventGrid.Fit(rivals, "1"));
    }

    [Fact]
    public void The_chase_between_the_player_and_one_rival_is_read_off_the_racers_list()
    {
        var pursuit = new PursuitResult
        {
            Police = 2, Started = true, Player = PursuitOutcomes.Escaped, Rival = PursuitOutcomes.Escaped,
            Racers = [new PursuitRacer { CarIndex = 3, Chased = true, Outcome = PursuitOutcomes.Busted }]
        };

        Assert.True(pursuit.For(3).RivalBusted);
        Assert.Equal(PursuitOutcomes.Escaped, pursuit.For(1).Rival);
        Assert.Null(pursuit.For(2).Rival);
        Assert.True(pursuit.For(2).PlayerEscaped);
    }

    [Fact]
    public async Task A_rivals_races_with_the_player_are_found_in_the_fields_they_ran_in_too()
    {
        var database = new SaveDatabase(_temp.Combine("Saves"));
        var sessions = new RaceSessionRepository(database);
        database.Use("field", db =>
        {
            sessions.Save(db, new ProcessedRaceSession { SessionId = "a", OpponentName = "Ann", PlayerWon = true });
            sessions.Save(db, new ProcessedRaceSession { SessionId = "b", OpponentName = "Bob", Field = ["Ann", "Cy"], AheadOfPlayer = ["Ann"] });
            sessions.Save(db, new ProcessedRaceSession { SessionId = "c", OpponentName = "Bob" });
        });

        var ann = await sessions.GetWithOpponentAsync("field", "Ann");

        Assert.Equal(new[] { "a", "b" }, ann.Select(s => s.SessionId).Order());
        Assert.True(ann.Single(s => s.SessionId == "b").BeatPlayer("Ann"));
        database.Dispose();
    }

    [Fact]
    public void A_pending_field_holds_every_rivals_cars()
    {
        var (state, _, rivals) = Field();

        Assert.All(rivals, r => Assert.True(StreetCorsa.Services.Opponents.OpponentLifeService.HasPendingRace(state, r)));
        Assert.False(StreetCorsa.Services.Opponents.OpponentLifeService.HasPendingRace(state, new Opponent("Nobody", 30, Gender.Male, 95, 50)));
    }
}
