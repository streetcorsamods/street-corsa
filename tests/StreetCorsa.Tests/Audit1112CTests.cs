using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreetCorsa.Models.Career;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Services;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Race.Validation;
using StreetCorsa.Services.Storage;

namespace StreetCorsa.Tests;

/// <summary>
/// Tech audit 2026-11-12, worker C (services): the last good pass left only as .json.bak, the install stamp through a
/// race on a car's data folder, a field's entry marked as the player, a crew leader's id before the racers are read.
/// </summary>
[Collection(RaceSessionTests.Name)]
public sealed class Audit1112CIngestionTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly SaveDatabase _database;
    private readonly GameStateRepository _repository;
    private readonly RaceSessionRepository _sessions;

    public Audit1112CIngestionTests()
    {
        _database = new SaveDatabase(_temp.Combine("Saves"));
        _repository = new GameStateRepository(null, _database);
        _sessions = new RaceSessionRepository(_database);
    }

    public void Dispose()
    {
        _repository.Dispose();
        _temp.Dispose();
    }

    private string Inbox => _temp.Combine("inbox");

    private RaceResultIngestionService Service(GameState state, bool? acRunning) => new(
        new RaceResultValidator(),
        new SessionDeduplicator(_sessions),
        new RaceResultProcessor(_repository, _sessions, new RaceFakes.FakeCareer(), new RaceFakes.FakeEvents()),
        _sessions,
        () => state,
        Inbox,
        _temp.Combine("quarantine"),
        _temp.Combine("archive"),
        () => acRunning,
        new ModeStartMark(_temp.Combine("marks")));

    private static (GameState State, RaceContext Context, Opponent Rival) World()
    {
        var world = RaceFakes.World(100m, saveName: "t");
        world.Context.CreatedAt = DateTime.Now.AddMinutes(-5);
        world.Context.LaunchedAt = DateTime.Now.AddMinutes(-4);
        return (world.State, world.Context, world.Rival);
    }

    /// <summary>The player's win of <paramref name="contextId"/>, written to the inbox under <c>{session}.json</c> plus <paramref name="suffix"/></summary>
    private string ResultFile(Guid contextId, string suffix)
    {
        var json = RaceResultValidatorTests.Fixture("1.1", RaceFakes.PlayerName, RaceFakes.OpponentName);
        var session = (JObject)json["session"]!;
        session["start_timestamp"] = DateTime.UtcNow.AddMinutes(-3).ToString("yyyy-MM-ddTHH:mm:ssZ");
        session["context_id"] = contextId.ToString("D");
        json["participants"]![0]!["performance"]!["final_position"] = 1;
        json["participants"]![1]!["performance"]!["final_position"] = 2;
        return _temp.File(Path.Combine("inbox", Guid.NewGuid() + ".json" + suffix), json.ToString());
    }

    [Fact]
    public async Task A_result_left_only_as_bak_once_AC_closed_is_put_back_and_applied()
    {
        var (state, context, rival) = World();
        var backup = ResultFile(context.ContextId, ".bak");

        var result = await Service(state, acRunning: false).ProcessOrphanedResultsAsync();

        Assert.Equal(1, result.FilesProcessed);
        Assert.False(result.ForfeitApplied);
        Assert.Null(state.PendingRace);
        Assert.Equal(5000m - 100m, rival.Money);
        Assert.False(File.Exists(backup));
        Assert.Empty(Directory.GetFiles(Inbox));
    }

    [Fact]
    public async Task A_bak_with_an_unfinished_tmp_beside_it_is_the_result_and_the_tmp_is_quarantined()
    {
        var (state, context, rival) = World();
        var backup = ResultFile(context.ContextId, ".bak");
        _temp.File(Path.Combine("inbox", Path.GetFileName(backup)[..^".bak".Length] + ".tmp"), "{ \"cut\": ");

        var result = await Service(state, acRunning: false).ProcessOrphanedResultsAsync();

        Assert.Equal(1, result.FilesProcessed);
        Assert.Equal(1, result.FilesQuarantined);
        Assert.Null(state.PendingRace);
        Assert.Equal(5000m - 100m, rival.Money);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task While_AC_may_run_a_bak_is_left_alone(bool? acRunning)
    {
        var (state, context, _) = World();
        var backup = ResultFile(context.ContextId, ".bak");

        var result = await Service(state, acRunning).ProcessOrphanedResultsAsync();

        Assert.Equal(0, result.FilesProcessed);
        Assert.True(File.Exists(backup));
        Assert.Same(context, state.PendingRace);
    }

    [Fact]
    public async Task A_bak_beside_its_result_is_not_put_over_it()
    {
        var (state, context, _) = World();
        var backup = ResultFile(context.ContextId, ".bak");
        var current = backup[..^".bak".Length];
        File.WriteAllText(current, File.ReadAllText(backup));

        var result = await Service(state, acRunning: false).ProcessOrphanedResultsAsync();

        // The .json is the one applied; the old pass of the same session is a duplicate the next pass deletes
        Assert.Equal(1, result.FilesProcessed);
        Assert.Null(state.PendingRace);
        var again = await Service(state, acRunning: false).ProcessOrphanedResultsAsync();
        Assert.Equal(0, again.FilesProcessed);
        Assert.Equal(1, again.FilesDuplicate);
        Assert.Empty(Directory.GetFiles(Inbox));
    }
}

[Collection(RaceSessionTests.Name)]
public sealed class Audit1112CTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ----- the install stamp -----

    [Fact]
    public void A_race_that_writes_into_a_cars_data_and_puts_it_back_leaves_the_stamp_as_it_was()
    {
        _temp.File("ac/content/tracks/strip/ui/ui_track.json", "{}");
        _temp.File("ac/content/cars/coupe/ui/ui_car.json", "{}");
        var engine = _temp.File("ac/content/cars/coupe/data/engine.ini", "[HEADER]\nVERSION=1\n");
        var data = Path.GetDirectoryName(engine)!;
        var stamp = AcInstallStamp.Of(_temp.Combine("ac"));
        Assert.NotNull(stamp);

        // The race's file in and out again, the original's write time kept: the folder's own write time moves
        var written = File.GetLastWriteTimeUtc(engine);
        var raceFile = Path.Combine(data, "race.tmp");
        File.WriteAllText(raceFile, "x");
        File.Delete(raceFile);
        File.WriteAllText(engine, "[HEADER]\nVERSION=1\n");
        File.SetLastWriteTimeUtc(engine, written);
        Directory.SetLastWriteTimeUtc(data, DateTime.UtcNow.AddMinutes(5));
        Assert.Equal(stamp, AcInstallStamp.Of(_temp.Combine("ac")));

        // A data file that changed is a changed install
        File.WriteAllText(engine, "[HEADER]\nVERSION=2\n");
        File.SetLastWriteTimeUtc(engine, written.AddSeconds(10));
        Assert.NotEqual(stamp, AcInstallStamp.Of(_temp.Combine("ac")));
    }

    // ----- a field's entries -----

    [Fact]
    public void An_entry_marked_as_the_player_is_never_handed_to_a_rival_by_its_place()
    {
        var world = RaceFakes.World(wager: 0, format: "circuit");
        var stranger = new Opponent("Second", 30, Gender.Male, 95, 50) { Money = 5000m };
        var car = new Car("car_second") { InstanceId = Guid.NewGuid(), EngineHealth = 1, TransmissionHealth = 1, BodyCondition = 1, TireCondition = 1 };
        stranger.Cars.Add(car);
        world.State.Racers.AddRacer(stranger);
        world.Context.Field.Add(new FieldEntrant { Name = "Second", CarInstanceId = car.InstanceId, CarDefinitionId = car.DefinitionId });

        // Three entries: the player, the opponent, and one more that claims to be the player under no rival's car or name
        var json = RaceResultValidatorTests.Fixture("1.1", RaceFakes.PlayerName, RaceFakes.OpponentName);
        var list = (JArray)json["participants"]!;
        var fake = (JObject)list[1].DeepClone();
        fake["driver_name"] = "Somebody";
        fake["car_index"] = 7;
        fake["is_player"] = true;
        list.Add(fake);
        for (var i = 0; i < list.Count; i++) list[i]["performance"]!["final_position"] = i + 1;
        json["session"]!["track_id"] = "ks_highlands";
        var result = JsonConvert.DeserializeObject<RaceResultJson>(json.ToString())!;

        var processor = new RaceResultProcessor(new RecordingRepository(), new RaceSessionRepository(new SaveDatabase(_temp.Combine("saves"))),
            new RaceFakes.FakeCareer(), new RaceFakes.FakeEvents());
        processor.ProcessRaceResult(result, world.Context, world.State);

        // The opponent raced; the second rival had no entry of their own, so sat it out
        Assert.Equal(1, world.Rival.Stats.Races);
        Assert.Equal(0, stranger.Stats.Races);
    }

    // ----- crews -----

    [Fact]
    public void Before_the_racers_are_read_a_crews_leader_is_its_mods_racer_when_that_one_exists()
    {
        var crew = new Crew { Id = "addon:night_owls", ModId = "addon", Boss = "gino" };
        var modsOwn = Racer("addon:gino");
        var games = Racer("gino");

        // Both are in play: only the mod's own is him
        var both = new HashSet<string>(["addon:gino", "gino"], StringComparer.OrdinalIgnoreCase);
        Assert.True(CrewBook.IsLeader(crew, modsOwn, both.Contains));
        Assert.False(CrewBook.IsLeader(crew, games, both.Contains));

        // The mod has none of that id: the game's racer is
        var gamesOnly = new HashSet<string>(["gino"], StringComparer.OrdinalIgnoreCase);
        Assert.True(CrewBook.IsLeader(crew, games, gamesOnly.Contains));

        // Written in full, whatever is in play
        crew.Boss = "addon:gino";
        Assert.False(CrewBook.IsLeader(crew, games, gamesOnly.Contains));
        Assert.True(CrewBook.IsLeader(crew, modsOwn, both.Contains));
    }

    private static Opponent Racer(string id) => new("Gino", 30, Gender.Male, 95, 50) { DefinitionId = id };

    // ----- small rules -----

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(2.5, 2500)]
    [InlineData(double.PositiveInfinity, 365L * 24 * 3600 * 1000)]
    public void Rule_seconds_become_milliseconds_none_for_nonsense_and_at_most_a_year(double seconds, long expected) =>
        Assert.Equal(expected, AiBackoff.Milliseconds(seconds));
}
