using Newtonsoft.Json;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Race.Traffic;
using StreetCorsa.Services.Race.Validation;
using StreetCorsa.Services.Storage;

namespace StreetCorsa.Tests;

/// <summary>
/// A format the player may run alone for the score (<c>solo</c>), in traffic (<c>traffic</c>) in a mode of its own: the
/// format read, race.ini with the player alone and the road's traffic, the result validated and read, the run kept on
/// its road's table; and the traffic itself, sized by the lane length in reach and cast from the mods' fleets.
/// </summary>
[Collection(GlobalsCollection.Name)]
public sealed class SoloRunTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly RaceFormats _before = RaceFormats.Current;

    // The format as a mod would give it: its own mode, won on the higher score, alone or with rivals, in traffic
    private static readonly RaceFormat Drive = new()
    {
        Id = "traffic_run", ModId = "roads", Family = RaceFormatFamilies.Road, Mode = "traffic_run", Win = RaceFormatWins.Score,
        Better = "higher", Rivals = 20, Solo = true, Traffic = true, Session = "Traffic Run"
    };

    public SoloRunTests() => RaceFormats.Current = RaceFormats.From([.. _before.All, Drive]);

    public void Dispose()
    {
        RaceFormats.Current = _before;
        _temp.Dispose();
    }

    private static string Fixture(string name) => Path.Combine(RepoPaths.Root, "tests", "StreetCorsa.Tests", "Fixtures", name);

    // ---- the format ----

    private ModSet Mods(string formatsJson)
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/formats/formats.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "formats", "formats.json")));
        _temp.File("roads/mod.json", """{ "id": "roads" }""");
        _temp.File("roads/formats/formats.json", formatsJson);
        _temp.File("roads/formats/traffic_run/mode.lua", "-- the mode");
        return ModSet.Load(_temp.Path);
    }

    [Fact]
    public void A_format_alone_in_traffic_reads_and_is_the_one_run_alone()
    {
        var mods = Mods("""
            { "formats": [
              { "id": "traffic_run", "family": "road", "mode": "traffic_run", "win": "score", "better": "higher", "rivals": 20, "solo": true, "traffic": true }
            ] }
            """);

        var formats = RaceFormats.Load(mods, _temp.Combine("no_mode"));

        Assert.Empty(formats.Problems);
        var drive = formats.Get("roads:traffic_run")!;
        Assert.True(drive.Solo);
        Assert.True(drive.Traffic);
        Assert.Same(drive, formats.Solo);
    }

    [Fact]
    public void Only_a_score_is_run_alone_and_only_a_mode_of_its_own_runs_traffic()
    {
        var mods = Mods("""
            { "formats": [
              { "id": "alone_to_the_line", "family": "road", "module": "road", "solo": true },
              { "id": "traffic_in_sc_race", "family": "road", "module": "road", "win": "score", "traffic": true }
            ] }
            """);

        var formats = RaceFormats.Load(mods, _temp.Combine("no_mode"));

        Assert.False(formats.Get("roads:alone_to_the_line")!.Solo);
        Assert.False(formats.Get("roads:traffic_in_sc_race")!.Traffic);
        Assert.Contains(formats.Problems, p => p.Contains("alone_to_the_line") && p.Contains("alone"));
        Assert.Contains(formats.Problems, p => p.Contains("traffic_in_sc_race") && p.Contains("traffic"));
        Assert.Null(formats.Solo);
    }

    // ---- race.ini ----

    private string[] RaceIni(DragRaceIntent intent)
    {
        var cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(cfg);
        Assert.True(new IniModificationService(cfg, _temp.Combine("AcRestore")).ApplyIntent(intent));
        return File.ReadAllLines(Path.Combine(cfg, "race.ini"));
    }

    [Fact]
    public void A_run_alone_is_the_player_alone_in_the_formats_mode_with_the_roads_traffic()
    {
        var ini = RaceIni(new DragRaceIntent
        {
            PlayerCarId = "a", PlayerName = "P", Format = Drive.Id, Solo = true, CashWager = 500,
            Traffic = new RaceTraffic { Cars = 51, Cast = "van_a:1 saloon_b:0.5", Density = 1 }
        });

        Assert.Contains("CARS=1", ini);
        Assert.DoesNotContain("[CAR_1]", ini);
        Assert.Contains("__CM_CUSTOM_MODE=roads-traffic_run", ini);
        Assert.Contains("RACE_TYPE=ROAD", ini);
        Assert.Contains("FORMAT=traffic_run", ini);
        Assert.Contains("SCORE=HIGHER", ini);
        Assert.Contains("RIVALS=0", ini);
        Assert.Contains("TRAFFIC_DENSITY=51", ini);
        Assert.Contains("TRAFFIC_CARS=van_a:1 saloon_b:0.5", ini);
        // Nothing rides on a run alone, whatever the intent carries; the game's own plan and models only when it names them
        Assert.DoesNotContain(ini, l => l.StartsWith("STAKES="));
        Assert.DoesNotContain(ini, l => l.StartsWith("TRAFFIC_PLAN=") || l.StartsWith("TRAFFIC_MODELS="));
    }

    [Fact]
    public void The_traffic_count_is_held_to_what_the_mode_takes_and_a_path_that_cannot_go_into_race_ini_stays_out()
    {
        var ini = RaceIni(new DragRaceIntent
        {
            PlayerCarId = "a", PlayerName = "P", Format = Drive.Id, Solo = true,
            Traffic = new RaceTraffic { Cars = 99999, Plan = @"C:\plans\a,b\traffic.json", Models = @"D:\models" }
        });

        Assert.Contains("TRAFFIC_DENSITY=2500", ini);
        Assert.DoesNotContain(ini, l => l.StartsWith("TRAFFIC_PLAN="));
        Assert.Contains(@"TRAFFIC_MODELS=D:\models", ini);
        Assert.DoesNotContain(ini, l => l.StartsWith("TRAFFIC_CARS="));
    }

    [Fact]
    public void A_format_not_run_in_traffic_gets_no_traffic_keys()
    {
        var ini = RaceIni(new DragRaceIntent
        {
            PlayerCarId = "a", OpponentCarId = "b", PlayerName = "P", OpponentName = "O", Format = "circuit",
            Traffic = new RaceTraffic { Cars = 60 }
        });

        Assert.Contains("[CAR_1]", ini);
        Assert.DoesNotContain(ini, l => l.StartsWith("TRAFFIC_"));
    }

    [Fact]
    public void The_race_set_up_alone_is_a_context_with_no_rival_and_the_traffic_with_it()
    {
        var car = new Car("car_a") { InstanceId = Guid.NewGuid() };
        var traffic = new RaceTraffic { Cars = 40, Cast = "van_a:1" };
        var intent = RaceSetupBuilder.BuildIntent(new RaceEntry
        {
            PlayerName = "P", PlayerCar = car, TrackId = "test_road", Format = Drive.Id, Solo = true, Traffic = traffic,
            Field = [new RaceRival { Name = "Somebody", CarId = "car_b" }]
        }, string.Empty, []);

        var context = (RaceContext)intent.Metadata["RaceContext"];
        Assert.True(context.IsSolo);
        Assert.True(context.IsAlone);
        Assert.Equal(0, context.RivalCount);
        Assert.Empty(context.Rivals());
        Assert.Same(traffic, context.Traffic);
        Assert.True(intent.Alone);
        Assert.Empty(intent.Race.Field);
        Assert.Equal(Services.Time.GameAction.FreeRun, intent.SessionAction);
    }

    // ---- the result ----

    private async Task<RaceResultJson?> ValidateAsync(string text)
    {
        var path = _temp.File(Guid.NewGuid() + ".json", text);
        var validation = await new RaceResultValidator().ValidateFileAsync(path);
        return validation.IsValid ? validation.ParsedResult : null;
    }

    [Fact]
    public async Task A_run_alone_as_the_mode_writes_it_passes_and_reads_into_every_field()
    {
        var text = File.ReadAllText(Fixture("sc_race_solo_result.json"));
        JsonConvert.DeserializeObject<RaceResultJson>(text, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });

        var result = await ValidateAsync(text);

        Assert.NotNull(result);
        var player = Assert.Single(result!.Participants);
        Assert.Equal(996, player.Score);
        Assert.Equal(12, player.Drive!.Crashes);
        Assert.False(player.Drive.Clean);
        Assert.Equal(39.57, player.Drive.AverageKmh);
        Assert.False(player.Crash.Crashed);
        Assert.Equal(120, result.Session.Traffic!.Density);
        Assert.Equal("van_a:1 saloon_b:1 truck_c:0.5", result.Session.Traffic.Cars);
    }

    [Fact]
    public async Task One_racer_alone_is_no_result_of_a_format_that_is_not_run_alone()
    {
        var text = File.ReadAllText(Fixture("sc_race_solo_result.json"));
        RaceFormats.Current = RaceFormats.From([.. _before.All, new RaceFormat
        {
            Id = Drive.Id, ModId = Drive.ModId, Family = Drive.Family, Mode = Drive.Mode, Win = Drive.Win, Better = Drive.Better
        }]);

        Assert.Null(await ValidateAsync(text));
    }

    // ---- the run in the save ----

    private readonly RaceResultProcessorTests.FakeRepository _repository = new();

    private RaceResultProcessor Processor() =>
        new(_repository, new RaceSessionRepository(new SaveDatabase(_temp.Combine("unused-saves"))), new RaceFakes.FakeCareer(), new RaceFakes.FakeEvents());

    private static RaceResultJson Result(double score, double timeMs, int crashes = 12)
    {
        var result = JsonConvert.DeserializeObject<RaceResultJson>(File.ReadAllText(Fixture("sc_race_solo_result.json")))!;
        var player = result.Participants[0];
        player.Score = score;
        player.LapTimesMs = [timeMs];
        player.Drive!.Crashes = crashes;
        player.Drive.Clean = crashes == 0;
        result.Session.SessionId = Guid.NewGuid().ToString();
        return result;
    }

    private static (GameState State, RaceContext Context, Car Car) Alone()
    {
        var world = RaceFakes.World(wager: 0, format: Drive.Id);
        var context = world.Context;
        context.IsSolo = true;
        context.OpponentName = string.Empty;
        context.OpponentCarInstanceId = Guid.Empty;
        context.TrackId = "test_road";
        context.Traffic = new RaceTraffic { Cars = 120, Density = 1.5 };
        return (world.State, context, world.PlayerCar);
    }

    [Fact]
    public void A_run_alone_goes_on_its_roads_table_and_changes_nothing_but_the_car()
    {
        var (state, context, car) = Alone();
        var money = state.Player.Money;
        var odometer = car.OdometerKM;

        var messages = Processor().ProcessRaceResult(Result(996, 329731.28), context, state);

        var run = Assert.Single(state.Career.ScoredRuns);
        Assert.Equal(996, run.Score);
        Assert.Equal(Drive.Id, run.Format);
        Assert.Equal("test_road", run.TrackId);
        Assert.Equal(329731.28, run.TimeMs);
        Assert.Equal(3.625, run.DistanceKm);
        Assert.Equal(39.57, run.AverageKmh);
        Assert.Equal(12, run.Crashes);
        Assert.Equal(car.DefinitionId, run.CarDefinitionId);
        Assert.Equal(120, run.TrafficCars);
        Assert.Equal(1.5, run.TrafficDensity);
        Assert.Equal(context.ContextId, run.ContextId);

        Assert.Equal(odometer + 3.625, car.OdometerKM, 6);
        Assert.Equal(money, state.Player.Money);
        Assert.Equal(0, state.Player.Stats.Races);
        Assert.Null(state.PendingRace);
        Assert.Equal(nameof(WinCondition.SoloRun), _repository.Recorded.Single().WinCondition);

        var card = messages[0];
        Assert.Contains("996", card.Text);
        Assert.Contains("Crashes: 12.", card.Text);
        Assert.Contains("Your best on this road.", card.Text);
    }

    [Fact]
    public void A_lesser_run_takes_its_place_below_and_a_clean_one_says_so()
    {
        var (state, context, _) = Alone();
        Processor().ProcessRaceResult(Result(996, 329731), context, state);
        var (_, second, _) = Alone();
        second.PlayerCarInstanceId = context.PlayerCarInstanceId;
        state.PendingRace = second;

        var messages = Processor().ProcessRaceResult(Result(500, 300000, crashes: 0), second, state);

        Assert.Equal([996.0, 500.0], ScoredRun.Table(state.Career.ScoredRuns, Drive.Id, "test_road", null, higherWins: true).Select(r => r.Score));
        Assert.Contains("Number 2 of your best on this road.", messages[0].Text);
        Assert.Contains("a clean run", messages[0].Text);
    }

    [Fact]
    public void A_run_with_no_score_keeps_nothing_and_says_so()
    {
        var (state, context, _) = Alone();
        var result = Result(0, 0);
        result.Participants[0].Score = null;

        var messages = Processor().ProcessRaceResult(result, context, state);

        Assert.Empty(state.Career.ScoredRuns);
        Assert.Contains("no score", messages[0].Text);
    }

    [Fact]
    public void A_roads_table_keeps_its_best_ranked_by_score_then_time_then_the_older()
    {
        var runs = new List<ScoredRun>();
        var day = new DateTime(2026, 11, 15);
        ScoredRun Run(double score, double time, int days, string track = "a") =>
            new() { Format = "f", TrackId = track, Score = score, TimeMs = time, Date = day.AddDays(days), ContextId = Guid.NewGuid() };

        for (var i = 0; i < ScoredRun.Kept; i++) Assert.NotNull(ScoredRun.Record(runs, Run(100 + i, 1000, i), higherWins: true));
        Assert.Null(ScoredRun.Record(runs, Run(50, 1000, 20), higherWins: true));
        Assert.Equal(1, ScoredRun.Record(runs, Run(500, 1000, 21), higherWins: true));
        Assert.NotNull(ScoredRun.Record(runs, Run(100, 1000, 22, track: "b"), higherWins: true));

        var table = ScoredRun.Table(runs, "f", "a", null, higherWins: true);
        Assert.Equal(ScoredRun.Kept, table.Count);
        Assert.Equal(500, table[0].Score);
        Assert.DoesNotContain(table, r => r.Score is 50 or 100);
        Assert.Single(ScoredRun.Table(runs, "f", "b", null, higherWins: true));

        // A tie goes to the quicker, then to the older run; lower wins puts the least first
        var ties = new List<ScoredRun>();
        var older = Run(10, 2000, 0);
        var quicker = Run(10, 1000, 5);
        var newer = Run(10, 2000, 9);
        ScoredRun.Record(ties, newer, higherWins: false);
        ScoredRun.Record(ties, older, higherWins: false);
        ScoredRun.Record(ties, quicker, higherWins: false);
        Assert.Equal([quicker, older, newer], ScoredRun.Table(ties, "f", "a", null, higherWins: false));
        // The same race twice is kept once
        Assert.Null(ScoredRun.Record(ties, quicker, higherWins: false));
        Assert.Equal(3, ties.Count);
    }

    // ---- the traffic ----

    private static string Lane(params (double X, double Z)[] points) =>
        "{ \"points\": [" + string.Join(", ", points.Select(p => FormattableString.Invariant($"[{p.X}, 0, {p.Z}]"))) + "] }";

    [Fact]
    public void A_short_road_is_all_in_reach_and_a_long_one_only_its_stretch_round_the_player()
    {
        // Two lanes, one each way, 1 km long: every metre of both is within 1.5 km of anywhere on it
        var shortRoad = TrafficRoad.Measure("{ \"lanes\": [" + Lane((0, 0), (500, 0), (1000, 0)) + ", " + Lane((1000, 5), (0, 5)) + "] }")!;
        Assert.Equal(2, shortRoad.Lanes);
        Assert.Equal(2.0, shortRoad.LaneKm, 3);
        Assert.Equal(2.0, shortRoad.ReachKm, 2);

        // One lane, 30 km straight in 100 m pieces: about 3 km of it round a point in the middle, less near the ends
        var points = Enumerable.Range(0, 301).Select(i => (i * 100.0, 0.0)).ToArray();
        var longRoad = TrafficRoad.Measure("{ \"lanes\": [" + Lane(points) + "] }")!;
        Assert.Equal(30.0, longRoad.LaneKm, 3);
        Assert.InRange(longRoad.ReachKm, 2.5, 3.1);
    }

    [Fact]
    public void What_is_no_plan_measures_as_none()
    {
        Assert.Null(TrafficRoad.Measure("not json"));
        Assert.Null(TrafficRoad.Measure("{ \"lanes\": 3 }"));
        Assert.Null(TrafficRoad.Measure("{ \"lanes\": [ { \"points\": [[0, 0, 0]] }, { \"points\": [[0, 0], [\"x\", 1, 2]] } ] }"));
        // A loop closes back to its first point; a BOM and comments are read past
        var loop = TrafficRoad.Measure("\uFEFF{ // a square\n \"lanes\": [ { \"loop\": true, \"points\": [[0,0,0],[100,0,0],[100,0,100],[0,0,100]] } ] }")!;
        Assert.Equal(0.4, loop.LaneKm, 6);
    }

    [Fact]
    public void A_road_is_given_its_reach_times_the_rate_times_the_players_density_held_to_what_the_mode_takes()
    {
        var road = new TrafficRoad(2, 7.26, 7.26);

        Assert.Equal(51, TrafficSetup.Cars(road, 7, 1));
        Assert.Equal(102, TrafficSetup.Cars(road, 7, 2));
        // Past the quietest and the busiest, the density is held to them
        Assert.Equal(TrafficSetup.Cars(road, 7, TrafficSetup.LeastDensity), TrafficSetup.Cars(road, 7, 0));
        Assert.Equal(TrafficSetup.LeastCars, TrafficSetup.Cars(new TrafficRoad(1, 0.5, 0.5), 7, 1));
        Assert.Equal(TrafficSetup.MostCars, TrafficSetup.Cars(new TrafficRoad(100, 1000, 1000), 20, 2));
        Assert.Equal(TrafficSetup.LeastCars, TrafficSetup.Cars(null, 7, 1));
    }

    [Fact]
    public void A_road_is_cast_by_its_layout_then_its_track()
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("roads/mod.json", """{ "id": "roads" }""");
        _temp.File("roads/traffic/fleets.json", """
            {
              "fleets": {
                "uk": [ { "id": "van_a", "weight": 1 }, { "id": "saloon_b", "weight": 0.5 }, { "id": "bad id", "weight": 1 } ],
                "hills": [ { "id": "tractor", "weight": 2 } ]
              },
              "tracks": { "test_road": "uk", "test_road-hillclimb": "hills", "lost_road": "nowhere" }
            }
            """);
        var fleets = TrafficFleets.Load(ModSet.Load(_temp.Path));

        Assert.Equal(["van_a", "saloon_b"], fleets.For("test_road", null).Select(m => m.Id));
        Assert.Equal(["van_a", "saloon_b"], fleets.For("test_road", "loop").Select(m => m.Id));
        Assert.Equal(["tractor"], fleets.For("test_road", "hillclimb").Select(m => m.Id));
        Assert.Empty(fleets.For("other_road", null));
        Assert.Empty(fleets.For("lost_road", null));
        Assert.Contains(fleets.Problems, p => p.Contains("bad id") || p.Contains("not an id"));
        Assert.Contains(fleets.Problems, p => p.Contains("nowhere"));
        Assert.Equal("van_a:1 saloon_b:0.5", TrafficFleets.Spec(fleets.For("test_road", null)));

    }

    [Fact]
    public void A_roads_plan_is_its_layouts_else_its_tracks()
    {
        var track = _temp.Combine("tracks", "test_road");
        Assert.Null(TrafficRoad.PlanOf(track, null));

        _temp.File("tracks/test_road/data/traffic.json", "{}");
        Assert.Equal(Path.Combine(track, "data", "traffic.json"), TrafficRoad.PlanOf(track, "loop"));

        _temp.File("tracks/test_road/loop/data/traffic.json", "{}");
        Assert.Equal(Path.Combine(track, "loop", "data", "traffic.json"), TrafficRoad.PlanOf(track, "loop"));
        Assert.True(TrafficPlans.CanHave(track, "test_road", null, ModSet.Load(_temp.Path)));
    }
}
