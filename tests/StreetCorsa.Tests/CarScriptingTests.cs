using StreetCorsa.Mods;
using StreetCorsa.Services.Scripting;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>
/// The cars' moves as scripts (step E7): the base game's scripts and feels, how a mod's are read and take their place,
/// conditions, the scenes' marks, the drives worked out up front, and a script played on a stage: in order, letting
/// the game go on, skipped, cancelled, failing to its end, never longer than it may take.
/// </summary>
public class CarScriptingTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static readonly ScriptBook BaseBook = ScriptBook.Load(ModSet.Load(RepoPaths.Content), []);

    #region The base game's scripts

    [Fact]
    public void The_base_games_scripts_and_feels_read_without_a_problem()
    {
        var problems = new List<string>();
        var book = ScriptBook.Load(ModSet.Load(RepoPaths.Content), problems);

        Assert.Empty(problems);
        Assert.Equal(["road", "lane", "launch", "pull_away", "roll_in"], book.Feels.Keys);
        foreach (var trigger in new[]
                 {
                     ScriptTriggers.LotTakeOut, ScriptTriggers.GarageArrive, ScriptTriggers.StationArrive, ScriptTriggers.StationLeave,
                     ScriptTriggers.StreetRivalArrive, ScriptTriggers.StreetRivalLeave
                 })
        {
            Assert.NotNull(book.For(trigger, ScriptFacts.None, null));
        }

        // Nothing moves by itself where the base game has no show
        Assert.Null(book.For(ScriptTriggers.GarageLeave, ScriptFacts.None, null));
        Assert.Null(book.For(ScriptTriggers.StationAtPump, ScriptFacts.None, null));
    }

    [Fact]
    public void The_base_feels_are_the_tuned_numbers_of_the_movers_they_replaced()
    {
        var road = BaseBook.Feels["road"];
        Assert.Equal((DriveKind.Road, 13.5f, 2.2f, 3.2f, 1.5f, 3f, 0.18), (road.Kind, road.CruiseSpeed, road.Acceleration, road.Brake, road.CornerAccel, road.ClutchSpeed, road.CruiseThrottle));
        Assert.Equal(DriveFeel.Road with { Id = "road" }, road with { Ratios = DriveFeel.Road.Ratios });

        var lane = BaseBook.Feels["lane"];
        Assert.Equal((12f, 4.2f, 3.5f, 0.28, 2), (lane.CruiseSpeed, lane.Brake, lane.ClutchSpeed, lane.CruiseThrottle, lane.Gear));

        var pull = BaseBook.Feels["pull_away"];
        Assert.Equal((DriveKind.Pull, 1.8f, 5f, 0.6f, 0.6f, 4.2f, 1.2f), (pull.Kind, pull.Acceleration, pull.TopSpeed, pull.ClutchSeconds, pull.Lean, pull.TurningRadius, pull.StraightOut));

        var rollIn = BaseBook.Feels["roll_in"];
        Assert.Equal((DriveKind.RollIn, 5f, 156.0), (rollIn.Kind, rollIn.CruiseSpeed, rollIn.RpmPerMetrePerSecond));
        Assert.Equal((DriveKind.Launch, 5.2f, 38f), (BaseBook.Feels["launch"].Kind, BaseBook.Feels["launch"].Acceleration, BaseBook.Feels["launch"].TopSpeed));
    }

    #endregion

    #region Mods' scripts

    private ModSet ModWith(string scripts)
    {
        _temp.File($"{ModIds.BaseId}/scripts/feels.json", File.ReadAllText(Path.Combine(RepoPaths.Content, ModIds.BaseId, "scripts", "feels.json")));
        _temp.File($"{ModIds.BaseId}/scripts/scripts.json", File.ReadAllText(Path.Combine(RepoPaths.Content, ModIds.BaseId, "scripts", "scripts.json")));
        _temp.File("mymod/mod.json", """{ "id": "mymod" }""");
        _temp.File("mymod/scripts/mine.json", scripts);
        return ModSet.Load(_temp.Path);
    }

    [Fact]
    public void A_mods_script_that_replaces_the_base_games_takes_its_place()
    {
        var problems = new List<string>();
        var book = ScriptBook.Load(ModWith("""
            { "replaces": "lot_take_out", "trigger": "lot.take_out", "steps": [ { "do": "lights", "head": true }, { "do": "release" } ] }
            """), problems);

        Assert.Empty(problems);
        var script = book.For(ScriptTriggers.LotTakeOut, ScriptFacts.None, null)!;
        Assert.Equal("lot_take_out", script.Id);
        Assert.Equal(2, script.Steps.Count);
        Assert.Single(book.Scripts, s => s.Trigger == ScriptTriggers.LotTakeOut);
    }

    [Theory]
    [InlineData("""{ "do": "lights", "head": true, "colour": "red" }""", "\"colour\" is not one of")]
    [InlineData("""{ "do": "jump" }""", "\"jump\" is no step")]
    [InlineData("""{ "do": "drive", "path": "arrive", "feel": "drift" }""", "no mod has the feel \"drift\"")]
    [InlineData("""{ "do": "drive", "toward": "exit", "feel": "road" }""", "\"toward\" needs a feel of kind pull")]
    [InlineData("""{ "do": "drive", "path": "arrive", "feel": "pull_away" }""", "goes \"toward\" a mark")]
    [InlineData("""{ "do": "drive", "feel": "road" }""", "needs one of \"path\", \"to\", \"toward\", \"distance\"")]
    [InlineData("""{ "do": "wait" }""", "says neither \"seconds\" nor \"until\"")]
    [InlineData("""{ "do": "fade", "to": 2 }""", "is not between 0 and 1")]
    [InlineData("""{ "if": { "light": "noon" } }""", "light \"noon\" is none of")]
    public void A_broken_replacement_is_left_out_said_and_the_base_games_script_stands(string step, string said)
    {
        var problems = new List<string>();
        var book = ScriptBook.Load(ModWith($$"""
            { "replaces": "station_arrive", "trigger": "station.arrive", "steps": [ {{step}} ] }
            """), problems);

        var problem = Assert.Single(problems);
        Assert.Contains(said, problem);
        Assert.Contains("mymod/scripts/mine.json", problem);
        Assert.Equal(3, book.For(ScriptTriggers.StationArrive, ScriptFacts.None, null)!.Steps.Count);
    }

    [Fact]
    public void A_mods_feel_takes_its_kinds_numbers_for_those_it_leaves_out_and_is_named_in_its_namespace()
    {
        var problems = new List<string>();
        var book = ScriptBook.Load(ModWith("""
            {
              "feels": [ { "id": "creep", "kind": "road", "cruiseSpeed": 2 } ],
              "scripts": [ { "id": "creep_in", "trigger": "station.arrive", "steps": [ { "do": "drive", "path": "arrive", "feel": "creep" } ] } ]
            }
            """), problems);

        Assert.Empty(problems);
        var creep = book.Feels["mymod:creep"];
        Assert.Equal((2f, 2.2f), (creep.CruiseSpeed, creep.Acceleration));

        var script = book.For(ScriptTriggers.StationArrive, ScriptFacts.None, null)!;
        Assert.Equal("mymod:creep_in", script.Id);
        Assert.Same(creep, Assert.IsType<DriveStep>(script.Steps[0]).Feel);
    }

    [Fact]
    public void A_script_with_a_when_plays_only_when_it_holds_and_the_base_games_otherwise()
    {
        var book = ScriptBook.Load(ModWith("""
            { "id": "night_arrive", "trigger": "station.arrive", "when": { "light": "night" }, "steps": [ { "do": "end" } ] }
            """), []);

        Assert.Equal("mymod:night_arrive", book.For(ScriptTriggers.StationArrive, new ScriptFacts(Light: StreetLight.Night), null)!.Id);
        Assert.Equal("station_arrive", book.For(ScriptTriggers.StationArrive, new ScriptFacts(Light: StreetLight.Day), null)!.Id);
        Assert.Equal("station_arrive", book.For(ScriptTriggers.StationArrive, ScriptFacts.None, null)!.Id);
    }

    #endregion

    #region Conditions

    [Theory]
    [InlineData(22, 20, 6, true)]
    [InlineData(3, 20, 6, true)]
    [InlineData(6, 20, 6, false)]
    [InlineData(12, 8, 18, true)]
    [InlineData(18, 8, 18, false)]
    public void Hours_run_from_the_first_up_to_the_second_round_midnight_when_need_be(int hour, int from, int to, bool holds)
    {
        var condition = new ScriptCondition { Hours = (from, to) };
        Assert.Equal(holds, condition.Holds(new ScriptFacts(new DateTime(1962, 5, 1, hour, 30, 0)), null));
        Assert.False(condition.Holds(ScriptFacts.None, null), "a screen without a clock holds no hours");
    }

    [Fact]
    public void Every_key_of_a_condition_must_hold_and_not_and_any_turn_and_widen_it()
    {
        var engine = new FakeEngine { Runs = true };
        var night = new ScriptFacts(Light: StreetLight.Night, Towed: true);

        Assert.True(new ScriptCondition { Engine = EngineIs.Runs, Towed = true }.Holds(night, engine));
        Assert.False(new ScriptCondition { Engine = EngineIs.Running, Towed = true }.Holds(night, engine));
        Assert.True(new ScriptCondition { Engine = EngineIs.Off }.Holds(night, engine));
        Assert.True(new ScriptCondition { Engine = EngineIs.Dead }.Holds(night, null));
        Assert.True(new ScriptCondition { Not = new ScriptCondition { Light = new HashSet<StreetLight> { StreetLight.Day } } }.Holds(night, engine));
        Assert.True(new ScriptCondition
        {
            Any = [new ScriptCondition { Towed = false }, new ScriptCondition { Light = new HashSet<StreetLight> { StreetLight.Dusk, StreetLight.Night } }]
        }.Holds(night, engine));
        Assert.False(new ScriptCondition { Any = [new ScriptCondition { Towed = false }] }.Holds(night, engine));
    }

    #endregion

    #region Marks

    [Fact]
    public void A_scenes_marks_are_points_or_measured_from_the_cars_home_and_a_bad_one_is_said()
    {
        var file = _temp.File("street/street.json", """
            {
              "marks": { "kerb": [ 3, -4.5 ], "door": { "behind": 7, "left": 1 }, "bad": "here", "worse": { "up": 2 } },
              "paths": { "loop": [ [0, 0], [5, 5], [0, 10] ], "broken": [ [0, 0], [1] ] }
            }
            """);
        var problems = new List<string>();
        var marks = SceneMarks.ReadFile(file, problems);

        Assert.Equal(new MarkSpec(X: 3, Z: -4.5f), marks.Marks["kerb"]);
        Assert.Equal(new MarkSpec(Ahead: -7, Left: 1), marks.Marks["door"]);
        Assert.Equal(3, marks.Paths["loop"].Count);
        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("\"bad\""));
        Assert.Contains(problems, p => p.Contains("\"broken\""));
    }

    [Fact]
    public void The_base_scenes_name_the_marks_and_paths_their_scripts_drive_by()
    {
        var mods = ModSet.Load(RepoPaths.Content);
        var station = StationScenes.LoadAll(mods).Single(s => s.Id == "tomwixon").ScriptMarks;
        Assert.True(station.Paths.ContainsKey("arrive") && station.Paths.ContainsKey("leave") && station.Marks.ContainsKey("pump"));

        var street = StreetScenes.LoadAll(mods).First();
        var marks = street.ScriptMarks;
        Assert.True(marks.Paths.ContainsKey("arrive") && marks.Paths.ContainsKey("leave"));
        Assert.True(marks.Marks.ContainsKey("alongside") && marks.Marks.ContainsKey("gone"));

        // The lane runs along the parked car, a lane over: the rival stops level with the player
        var alongside = street.Lane(street.RivalStop);
        var player = new GroundPose(street.Player.X, street.Player.Z, street.Player.Heading * MathF.PI / 180f);
        var level = player.Offset(street.RivalStop, street.LaneOffset);
        Assert.Equal(level.X, alongside.X, 3);
        Assert.Equal(level.Z, alongside.Z, 3);

        Assert.Equal(new MarkSpec(Ahead: -7f), GarageScene.Load(mods, "garage").Marks["door"]);
        Assert.Equal(new MarkSpec(Ahead: -7f), GarageScene.Load(mods, "no_such_garage").Marks["door"]);
    }

    #endregion

    #region Drives

    [Fact]
    public void A_car_reversing_faces_away_from_its_way_and_steers_the_other_way()
    {
        var path = DrivePath.Through([new GroundPoint(0, 0), new GroundPoint(0, -6), new GroundPoint(4, -10)]);
        var forward = DrivePlan.Road(path, DriveFeel.Road, 800, rolling: false, stop: true);
        var back = DrivePlan.Road(path, DriveFeel.Road, 800, rolling: false, stop: true, reverse: true);

        // The way heads down -z: forwards the car faces it, in reverse its nose points back up +z
        Assert.InRange(MathF.Abs(forward.Start.Heading), MathF.PI - 0.1f, MathF.PI);
        Assert.InRange(back.Start.Heading, -0.1f, 0.1f);
        Assert.Equal(0f, GroundPose.Turn(forward.At(3f).Heading + MathF.PI, back.At(3f).Heading), 3);
        Assert.Equal(-forward.At(3f).Steer, back.At(3f).Steer, 4);
        Assert.Equal(forward.End.X, back.End.X, 3);
    }

    [Fact]
    public void A_car_pulling_away_turns_for_its_mark_and_drives_on_straight_past_it()
    {
        var plan = DrivePlan.Pull(new GroundPose(0, 0, 0), new GroundPoint(6, 6), BaseBook.Feels["pull_away"], 800);

        Assert.Equal(0f, plan.At(0.5f).Steer, 3);
        Assert.True(plan.At(2.5f).Heading > 0.3f, "it has turned towards the mark, to the left");

        // Past the mark it goes straight on: the heading holds
        var late = plan.At(plan.Duration - 2f).Heading;
        Assert.Equal(late, plan.End.Heading, 2);
        Assert.InRange(plan.End.Speed, 4.9f, 5.01f);
    }

    [Fact]
    public void A_car_rolling_in_stops_on_its_mark_its_brake_lights_on_until_it_has()
    {
        var path = DrivePath.Through([new GroundPoint(0, -7), new GroundPoint(0, 0)]);
        var plan = DrivePlan.RollIn(path, BaseBook.Feels["roll_in"], 800);

        Assert.Equal(4.2f, plan.Duration, 3);
        Assert.True(plan.Start.Braking);
        Assert.Equal(5f, plan.Start.Speed, 3);
        Assert.True(plan.End.Done);
        Assert.False(plan.End.Braking);
        Assert.Null(plan.End.Rpm);
        Assert.Equal(0f, plan.End.Z, 4);
    }

    #endregion

    #region Playing a script

    private static CarScript Script(float most = 30f, params ScriptStep[] steps) => new("test", "test.trigger", ScriptCondition.Always, most, steps);

    private static ScriptRun Play(CarScript script, FakeStage stage, FakeClock clock, List<string>? problems = null)
    {
        // The runner's awaits go on in line here, as on the UI thread: no context to post them to
        SynchronizationContext.SetSynchronizationContext(null);
        return ScriptRun.Play(script, stage, ScriptFacts.None, clock, problems == null ? null : problems.Add);
    }

    /// <summary>How a run that is over ended (it is over at once on a hand-moved clock)</summary>
    private static ScriptEnd EndOf(ScriptRun run)
    {
        Assert.True(run.IsOver);
#pragma warning disable xUnit1031 // over already: nothing to wait for
        return run.Ended.Result;
#pragma warning restore xUnit1031
    }

    private static DriveStep DriveTo(string to, bool wait = true, string? from = null) =>
        new(null, null, from == null ? null : new MarkSpec(from), new MarkSpec(to), null, null, false, DriveFeel.Road, false, false, wait);

    [Fact]
    public void A_script_plays_its_steps_in_order_and_lets_the_game_go_on_where_it_says()
    {
        var clock = new FakeClock();
        var stage = new FakeStage(clock);
        stage.Marks["exit"] = new MarkSpec(X: 0, Z: 30);
        var run = Play(Script(30f,
            new LightsStep(null, true, null),
            new WaitStep(null, WaitFor.Time, 1f, 0f),
            DriveTo("exit"),
            new ReleaseStep(),
            new WaitStep(null, WaitFor.Time, 2f, 0f),
            new LightsStep(null, false, null)), stage, clock);

        Assert.Equal(["head on"], stage.Car.Log);
        clock.Advance(1.01);
        Assert.Equal(["head on", "drive"], stage.Car.Log);
        Assert.False(run.Released.IsCompleted);

        clock.Advance(stage.Car.LastPlan!.Duration + 0.1);
        Assert.True(run.Released.IsCompleted);
        Assert.False(run.IsOver);
        Assert.True(run.Drove);
        Assert.Equal(30f, stage.Car.Pose.Z, 3);

        clock.Advance(2.1);
        Assert.Equal(ScriptEnd.Finished, EndOf(run));
        Assert.Equal("head off", stage.Car.Log[^1]);
    }

    [Fact]
    public void A_skipped_script_does_the_rest_at_once_the_car_where_it_would_end()
    {
        var clock = new FakeClock();
        var stage = new FakeStage(clock);
        stage.Marks["exit"] = new MarkSpec(X: 0, Z: 50);
        var run = Play(Script(30f,
            DriveTo("exit"),
            new WaitStep(null, WaitFor.Time, 10f, 0f),
            new FadeStep(null, 1f, 1f, FadeEase.In, false),
            new LightsStep(null, false, null)), stage, clock);

        clock.Advance(0.5);
        run.Skip();

        Assert.Equal(ScriptEnd.Skipped, EndOf(run));
        Assert.Equal(50f, stage.Car.Pose.Z, 3);
        Assert.Contains("jump", stage.Car.Log);
        Assert.Equal((1f, TimeSpan.Zero), stage.Fades[^1]);
        Assert.Equal("head off", stage.Car.Log[^1]);
    }

    [Fact]
    public void A_drive_that_does_not_wait_goes_on_behind_the_next_steps_and_a_skip_jumps_it_too()
    {
        var clock = new FakeClock();
        var stage = new FakeStage(clock);
        stage.Marks["exit"] = new MarkSpec(X: 0, Z: 80);
        var run = Play(Script(30f,
            DriveTo("exit", wait: false),
            new WaitStep(null, WaitFor.DriveEnd, 30f, 1.5f),
            new ReleaseStep(),
            new WaitStep(null, WaitFor.DriveEnd, 30f, 0f)), stage, clock);

        var plan = stage.Car.LastPlan!;
        clock.Advance(plan.Duration - 2);
        Assert.False(run.Released.IsCompleted);
        clock.Advance(0.6);
        Assert.True(run.Released.IsCompleted, "let go a moment before the drive ends");

        run.Skip();
        Assert.Equal(80f, stage.Car.Pose.Z, 3);
        Assert.True(run.IsOver);
    }

    [Fact]
    public void A_cancelled_script_does_nothing_more_but_lets_whoever_waits_go()
    {
        var clock = new FakeClock();
        var stage = new FakeStage(clock);
        var run = Play(Script(30f,
            new WaitStep(null, WaitFor.Time, 5f, 0f),
            new LightsStep(null, true, null)), stage, clock);

        run.Cancel();
        clock.Advance(10);

        Assert.Equal(ScriptEnd.Cancelled, EndOf(run));
        Assert.True(run.Released.IsCompleted);
        Assert.Empty(stage.Car.Log);
    }

    [Fact]
    public void A_step_that_cannot_be_done_is_said_and_the_rest_done_at_once()
    {
        var clock = new FakeClock();
        var stage = new FakeStage(clock);
        var problems = new List<string>();
        var run = Play(Script(30f,
            DriveTo("nowhere"),
            new WaitStep(null, WaitFor.Time, 5f, 0f),
            new LightsStep(null, true, null)), stage, clock, problems);

        Assert.Equal(ScriptEnd.Failed, EndOf(run));
        Assert.Contains("the scene has no mark \"nowhere\"", Assert.Single(problems));
        Assert.Equal(["head on"], stage.Car.Log);
    }

    [Fact]
    public void A_script_that_takes_longer_than_it_may_is_skipped_to_its_end()
    {
        var clock = new FakeClock();
        var stage = new FakeStage(clock);
        var run = Play(Script(3f,
            new WaitStep(null, WaitFor.Time, 100f, 0f),
            new LightsStep(null, true, null)), stage, clock);

        clock.Advance(3.1);

        Assert.Equal(ScriptEnd.Skipped, EndOf(run));
        Assert.Equal(["head on"], stage.Car.Log);
    }

    [Fact]
    public void An_engine_that_will_not_start_is_waited_for_no_longer_than_the_step_says_and_a_branch_ends_the_show()
    {
        var clock = new FakeClock();
        var stage = new FakeStage(clock) { Car = { Engine = new FakeEngine { Runs = true, StartsAfterCranks = int.MaxValue } } };
        var run = Play(Script(30f,
            new EngineStep(null, true, true, null, 0.5f, true, 4f),
            new IfStep(null, new ScriptCondition { Engine = EngineIs.Off }, [new EndStep()], []),
            new LightsStep(null, true, null)), stage, clock);

        clock.Advance(3.9);
        Assert.False(run.IsOver);
        clock.Advance(0.2);

        Assert.Equal(ScriptEnd.Finished, EndOf(run));
        Assert.Empty(stage.Car.Log);
        Assert.Equal(1, ((FakeEngine)stage.Car.Engine!).Cranks);
    }

    [Fact]
    public void A_drive_from_a_mark_measured_from_home_starts_there_and_stops_at_home()
    {
        var clock = new FakeClock();
        var stage = new FakeStage(clock);
        stage.Car.Home = stage.Car.Pose = new GroundPose(2, 3, MathF.PI / 2);
        stage.Marks["door"] = new MarkSpec(Ahead: -7f);
        Play(Script(30f, DriveTo(MarkSpec.Home, from: "door")), stage, clock);

        var plan = stage.Car.LastPlan!;
        Assert.Equal(-5f, plan.Start.X, 3);
        Assert.Equal(3f, plan.Start.Z, 3);
        Assert.Equal(MathF.PI / 2, plan.Start.Heading, 3);
        Assert.Equal(2f, plan.End.X, 3);
    }

    #endregion

    #region Fakes

    private sealed class FakeClock : IScriptClock
    {
        private readonly List<(TimeSpan Due, TaskCompletionSource Done)> _waits = [];

        public TimeSpan Now { get; private set; }

        public Task Delay(TimeSpan span, CancellationToken token)
        {
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            var done = new TaskCompletionSource();
            token.Register(() => done.TrySetCanceled(token));
            _waits.Add((Now + span, done));
            return done.Task;
        }

        /// <summary>Time goes on: every wait due by then ends, in the order they are due</summary>
        public void Advance(double seconds)
        {
            var end = Now + TimeSpan.FromSeconds(seconds);
            while (true)
            {
                _waits.RemoveAll(w => w.Done.Task.IsCompleted);
                if (_waits.Count == 0) break;
                var next = _waits.MinBy(w => w.Due);
                if (next.Due > end) break;
                if (next.Due > Now) Now = next.Due;
                _waits.Remove(next);
                next.Done.TrySetResult();
            }

            Now = end;
        }
    }

    private sealed class FakeEngine : IScriptEngine
    {
        public bool Runs { get; init; }

        // How many turns of the key it takes: 0 runs at once
        public int StartsAfterCranks { get; init; }

        public int Cranks { get; private set; }

        public bool IsRunning { get; private set; }

        public (double Idle, double Limiter) Revs => (800, 6000);

        public void Start(bool crank)
        {
            if (IsRunning) return;
            if (crank && ++Cranks <= StartsAfterCranks) return;
            IsRunning = Runs;
        }

        public void Stop() => IsRunning = false;

        public void Pedal(double? depth)
        {
        }

        public void Volume(float scale)
        {
        }
    }

    private sealed class FakeCar(FakeClock clock) : IScriptCar
    {
        private TaskCompletionSource? _driving;
        private TimeSpan _started;

        public List<string> Log { get; } = [];

        public DrivePlan? LastPlan { get; private set; }

        public GroundPose Home { get; set; }

        public GroundPose Pose { get; set; }

        public IScriptEngine? Engine { get; set; }

        public bool IsSettled => _driving == null;

        public float DriveLeft => _driving == null || LastPlan == null ? 0f : Math.Max(0f, LastPlan.Duration - (float)(clock.Now - _started).TotalSeconds);

        public void Lights(bool? head, bool? brake)
        {
            if (head is { } on) Log.Add(on ? "head on" : "head off");
            if (brake is { } braking) Log.Add(braking ? "brake on" : "brake off");
        }

        public Task Drive(DrivePlan plan)
        {
            Log.Add("drive");
            LastPlan = plan;
            Pose = plan.Start.Pose;
            _started = clock.Now;
            var driving = _driving = new TaskCompletionSource();
            clock.Delay(TimeSpan.FromSeconds(plan.Duration), CancellationToken.None).ContinueWith(_ =>
            {
                if (_driving != driving) return;
                Pose = plan.End.Pose;
                _driving = null;
                driving.TrySetResult();
            }, TaskContinuationOptions.ExecuteSynchronously);
            return driving.Task;
        }

        public void Jump(DrivePlan plan)
        {
            Log.Add("jump");
            LastPlan = plan;
            Pose = plan.End.Pose;
            var driving = _driving;
            _driving = null;
            driving?.TrySetResult();
        }

        public void Place(GroundPose pose) => Pose = pose;
    }

    private sealed class FakeStage(FakeClock clock) : IScriptStage
    {
        public FakeCar Car { get; } = new(clock);

        public Dictionary<string, MarkSpec> Marks { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(float Opacity, TimeSpan Over)> Fades { get; } = [];

        IScriptCar? IScriptStage.Car(string? role) => role == null ? Car : null;

        public MarkSpec? Mark(string name) => Marks.TryGetValue(name, out var mark) ? mark : null;

        public IReadOnlyList<GroundPoint>? Path(string name) => null;

        public Task Fade(float opacity, TimeSpan over, FadeEase ease)
        {
            Fades.Add((opacity, over));
            return over <= TimeSpan.Zero ? Task.CompletedTask : clock.Delay(over, CancellationToken.None);
        }

        public void Camera(IScriptCar of, CameraMode mode)
        {
        }
    }

    #endregion
}
