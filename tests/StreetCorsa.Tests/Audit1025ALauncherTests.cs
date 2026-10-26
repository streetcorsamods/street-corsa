using System.Diagnostics;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Race;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-25 audit's launcher pipeline from start to finish, on a fake install in a temp folder and fake processes:
/// the finally's restore, a game that outlives its launch, a stop before and after the start, a game that will not
/// die, a failure after the start, and a seal that comes in while the launch prepares. In the globals collection: the
/// seal is the install gate's, one for the process.
/// </summary>
[Collection(GlobalsCollection.Name)]
public sealed class Audit1025ALauncherTests : IDisposable
{
    private const string Car = "sr_launch_car";

    private readonly TempDir _temp = new();
    private readonly string _ac;
    private readonly string _keep;
    private readonly FakeIni _ini = new();
    private readonly FakeIngestion _ingestion = new();
    private readonly FakeHost _host = new();

    public Audit1025ALauncherTests()
    {
        _ac = _temp.Combine("ac");
        _keep = _temp.Combine("AcRestore");
        _temp.File(Path.Combine("ac", "acs.exe"), "not a game");
        _temp.File(Path.Combine("ac", "content", "cars", Car, "data", "engine.ini"), "[HEADER]\r\nVERSION=1\r\n");
        Directory.CreateDirectory(Path.Combine(_ac, "extension"));
        _temp.File(Path.Combine("modes", "sc_race", "mode.lua"), "-- mode");
    }

    public void Dispose()
    {
        AcInstallGate.Unseal();
        _temp.Dispose();
    }

    private string EngineIni => Path.Combine(_ac, "content", "cars", Car, "data", "engine.ini");

    private CarDataOverlay Overlay { get; set; } = null!;

    private AssettoCorsaLauncher Launcher()
    {
        Overlay = new CarDataOverlay(Path.Combine(_ac, "content", "cars"), _keep);
        return new AssettoCorsaLauncher(_ini, _ingestion, Overlay, new ScRaceMode(_temp.Combine("modes", "sc_race")), _host, () => _ac)
        {
            RelaunchGrace = TimeSpan.Zero,
            StopTimeout = TimeSpan.FromMilliseconds(200),
            ExitPoll = TimeSpan.FromMilliseconds(20),
            IngestionRetryDelay = TimeSpan.Zero,
            ShortestRace = TimeSpan.Zero
        };
    }

    private static FreeRunLaunchIntent FreeRun() => new() { CarId = Car, TrackId = "strip", PlayerName = "Player" };

    private static DragRaceLaunchIntent Race(RaceContext context)
    {
        var intent = new DragRaceLaunchIntent { Race = new DragRaceIntent { PlayerCarId = Car, OpponentCarId = Car, PlayerName = "Player", OpponentName = "Rival", TrackId = "strip" } };
        intent.Metadata["RaceContext"] = context;
        return intent;
    }

    private static RaceCarData CarData()
    {
        var build = new CarBuildResult();
        build.Files["engine.ini"] = "[HEADER]\r\nVERSION=2\r\n";
        return new RaceCarData(Car, build);
    }

    [Fact]
    public async Task A_run_that_ends_puts_the_install_back_in_the_finally_and_lets_go_of_the_lock()
    {
        var launcher = Launcher();
        var intent = FreeRun();
        intent.CarData.Add(CarData());
        _host.OnStart = process =>
        {
            // The car runs on the race's data while the game runs
            Assert.Contains("VERSION=2", File.ReadAllText(EngineIni));
            process.Exit(0);
        };
        var ended = 0;
        launcher.ExecutionEnded += (_, _) => ended++;

        var result = await launcher.LaunchRaceAsync(intent);

        Assert.True(result.Success);
        Assert.Equal(RaceOutcome.NotARace, result.Outcome);
        Assert.Equal(1, _ini.Restores);
        Assert.Contains("VERSION=1", File.ReadAllText(EngineIni));
        Assert.Empty(Overlay.Applied);
        Assert.False(launcher.IsExecutionLocked);
        Assert.False(launcher.IsLaunchInProgress);
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task A_game_that_outlives_its_launch_keeps_the_lock_until_it_closes_and_the_install_goes_back_then()
    {
        var launcher = Launcher();
        var intent = FreeRun();
        intent.CarData.Add(CarData());
        _host.OnStart = process =>
        {
            // The game the launcher started is gone, but another AC process is still on the install
            _host.Running = true;
            process.Exit(0);
        };
        var exited = new TaskCompletionSource();
        launcher.AssettoCorsaExited += (_, _) => exited.TrySetResult();

        await launcher.LaunchRaceAsync(intent);

        Assert.True(launcher.IsExecutionLocked);
        Assert.Equal(0, _ini.Restores);
        Assert.Contains("VERSION=2", File.ReadAllText(EngineIni));

        // No other launch goes in while the earlier game holds the install
        var busy = await launcher.LaunchRaceAsync(FreeRun());
        Assert.False(busy.Success);

        _host.Running = false;
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(launcher.IsExecutionLocked);
        Assert.Equal(1, _ini.Restores);
        Assert.Contains("VERSION=1", File.ReadAllText(EngineIni));
    }

    [Fact]
    public async Task A_stop_before_the_start_calls_the_launch_off_and_the_race_is_released()
    {
        var launcher = Launcher();
        var context = new RaceContext();
        _ini.OnApply = launcher.CancelRace;

        var result = await launcher.LaunchRaceAsync(Race(context));

        Assert.Equal(RaceOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, _host.Started);
        Assert.Equal([context.ContextId], _ingestion.Released);
        Assert.Equal([context.ContextId], _ingestion.Ended);
        Assert.Equal(1, _ini.Restores);
        Assert.False(launcher.IsExecutionLocked);
    }

    [Fact]
    public async Task A_stop_after_the_start_kills_the_game_and_the_race_without_a_result_is_forfeited()
    {
        var launcher = Launcher();
        var context = new RaceContext();
        _host.OnWait = _ => launcher.CancelRace();

        var result = await launcher.LaunchRaceAsync(Race(context));

        Assert.Equal(1, _host.Started);
        Assert.True(_host.Last!.Killed);
        Assert.Equal([context.ContextId], _ingestion.Launched);
        Assert.Equal([context.ContextId], _ingestion.Forfeited);
        Assert.Equal(RaceOutcome.NoResult, result.Outcome);
        Assert.Equal(1, _ini.Restores);
    }

    [Fact]
    public async Task A_game_that_will_not_die_after_a_stop_leaves_the_race_pending_and_the_restore_waits_for_it()
    {
        var launcher = Launcher();
        var context = new RaceContext();
        _host.Stubborn = true;
        _host.OnWait = _ =>
        {
            _host.Running = true;
            launcher.CancelRace();
        };

        var result = await launcher.LaunchRaceAsync(Race(context));

        Assert.Equal(RaceOutcome.ResultPending, result.Outcome);
        Assert.Empty(_ingestion.Forfeited);
        Assert.Empty(_ingestion.Released);
        Assert.Equal(0, _ini.Restores);
        Assert.True(launcher.IsExecutionLocked);

        var exited = new TaskCompletionSource();
        launcher.AssettoCorsaExited += (_, _) => exited.TrySetResult();
        _host.Running = false;
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(1, _ini.Restores);
    }

    [Fact]
    public async Task A_failure_after_the_start_leaves_the_race_pending_never_released()
    {
        var launcher = Launcher();
        var context = new RaceContext();
        _ingestion.OnLaunched = () => throw new IOException("the save is busy");
        _host.OnStart = process => process.Exit(0);

        var result = await launcher.LaunchRaceAsync(Race(context));

        Assert.False(result.Success);
        Assert.Equal(RaceOutcome.ResultPending, result.Outcome);
        Assert.Empty(_ingestion.Released);
        Assert.Equal([context.ContextId], _ingestion.Ended);
        Assert.Equal(1, _ini.Restores);
    }

    [Fact]
    public async Task A_seal_while_the_launch_prepares_puts_no_car_data_in_and_starts_nothing()
    {
        var launcher = Launcher();
        var intent = FreeRun();
        intent.CarData.Add(CarData());
        _ini.OnApply = AcInstallGate.Seal;

        var result = await launcher.LaunchRaceAsync(intent);

        Assert.False(result.Success);
        Assert.Equal(0, _host.Started);
        Assert.Contains("VERSION=1", File.ReadAllText(EngineIni));
        Assert.Empty(Overlay.Applied);
        Assert.False(Directory.Exists(Path.Combine(_keep, Car)));
    }

    [Fact]
    public async Task No_launch_starts_while_another_AC_process_runs_or_may_run()
    {
        var launcher = Launcher();

        _host.Running = true;
        var running = await launcher.LaunchRaceAsync(FreeRun());
        _host.Running = null;
        var unknown = await launcher.LaunchRaceAsync(FreeRun());

        Assert.False(running.Success);
        Assert.False(unknown.Success);
        Assert.Equal(0, _ini.Applied);
        Assert.False(launcher.IsExecutionLocked);
    }

    [Fact]
    public async Task The_gate_refuses_a_try_while_another_thread_holds_it_and_lets_its_holder_in_again()
    {
        var token = TestContext.Current.CancellationToken;
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            using (AcInstallGate.Hold())
            {
                held.Set();
                release.Wait(TimeSpan.FromSeconds(10), token);
            }
        }, token);
        held.Wait(TimeSpan.FromSeconds(10), token);

        var ran = AcInstallGate.TryRun(TimeSpan.FromMilliseconds(50), () => { });
        release.Set();
        await holder.WaitAsync(TimeSpan.FromSeconds(10), token);

        Assert.False(ran);
        using (AcInstallGate.Hold())
        {
            Assert.True(AcInstallGate.TryRun(TimeSpan.Zero, () => { }));
        }
    }

    [Fact]
    public void A_seal_stays_until_it_is_given_back()
    {
        Assert.False(AcInstallGate.IsSealed);
        AcInstallGate.Seal();
        Assert.True(AcInstallGate.IsSealed);
        AcInstallGate.Unseal();
        Assert.False(AcInstallGate.IsSealed);
    }

    // ----- fakes -----

    private sealed class FakeIni : IIniModificationService
    {
        public int Applied;
        public int Restores;
        public Action? OnApply;

        public bool ApplyIntent(ModificationIntent intent)
        {
            Applied++;
            OnApply?.Invoke();
            return true;
        }

        public int RestoreAll(ICollection<string>? problems = null)
        {
            Restores++;
            return 0;
        }

        public bool HasPendingRestore => false;

        public IniText? ReadIniFile(string fileName) => null;

        public bool FileExists(string fileName) => false;

        public string GetIniFilePath(string fileName) => fileName;
    }

    private sealed class FakeIngestion : IRaceResultIngestionService
    {
        public readonly List<Guid> Launched = [];
        public readonly List<Guid> Released = [];
        public readonly List<Guid> Ended = [];
        public readonly List<Guid> Forfeited = [];
        public Action? OnLaunched;

        public Task<IngestionResult> IngestResultsAsync(RaceContext? raceContext = null, IProgress<string>? progress = null) =>
            Task.FromResult(new IngestionResult());

        public Task<IngestionResult> ProcessOrphanedResultsAsync() => Task.FromResult(new IngestionResult());

        public Task<bool> BeginRaceAsync(RaceContext context, List<PlayerMessage> messages) => Task.FromResult(true);

        public void MarkRaceLaunched(RaceContext context)
        {
            Launched.Add(context.ContextId);
            OnLaunched?.Invoke();
        }

        public Task ReleasePendingRaceAsync(RaceContext context)
        {
            Released.Add(context.ContextId);
            return Task.CompletedTask;
        }

        public void EndRace(RaceContext context) => Ended.Add(context.ContextId);

        public Task<bool> ApplyNoResultForfeitAsync(RaceContext context, List<PlayerMessage>? messages = null)
        {
            Forfeited.Add(context.ContextId);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeHost : AcProcessHost
    {
        private readonly object _sync = new();
        private bool? _running = false;

        /// <summary>What "is any AC process running" answers: false, true, or null for "cannot tell"</summary>
        public bool? Running
        {
            get { lock (_sync) return _running; }
            set { lock (_sync) _running = value; }
        }

        public int Started;
        public FakeProcess? Last;
        public Action<FakeProcess>? OnStart;
        public Action<FakeProcess>? OnWait;

        /// <summary>A kill leaves the game running</summary>
        public bool Stubborn;

        public override bool? QueryAnyRunning() => Running;

        public override IGameProcess? Start(ProcessStartInfo startInfo)
        {
            Started++;
            Last = new FakeProcess(this);
            OnStart?.Invoke(Last);
            return Last;
        }

        public override IReadOnlyList<IGameProcess> ByName(string processName) => [];

        public override int KillAll(string processName) => 0;

        public sealed class FakeProcess(FakeHost host) : IGameProcess
        {
            private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool Killed { get; private set; }

            public int Id => 4242;

            public bool HasExited => _exit.Task.IsCompleted;

            public int ExitCode => _exit.Task.IsCompleted ? _exit.Task.Result : throw new InvalidOperationException("still running");

            public void Exit(int code) => _exit.TrySetResult(code);

            public Task WaitForExitAsync(CancellationToken cancellationToken)
            {
                host.OnWait?.Invoke(this);
                return _exit.Task.WaitAsync(cancellationToken);
            }

            public void Kill()
            {
                Killed = true;
                if (!host.Stubborn) Exit(-1);
            }

            public void Dispose()
            {
            }
        }
    }
}
