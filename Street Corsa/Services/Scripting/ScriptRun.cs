using System.Diagnostics;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Services.Station;

namespace StreetCorsa.Services.Scripting
{
    /// <summary>The time a script plays in: real time in the game, a clock moved on by hand in the tests</summary>
    public interface IScriptClock
    {
        TimeSpan Now { get; }

        /// <summary>Done after <paramref name="span"/>, or cancelled with <paramref name="token"/></summary>
        Task Delay(TimeSpan span, CancellationToken token);
    }

    /// <summary>The wall's clock: a stopwatch, so a clock put back an hour does not hold a script for that hour</summary>
    public sealed class RealScriptClock : IScriptClock
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();

        public TimeSpan Now => _watch.Elapsed;

        public Task Delay(TimeSpan span, CancellationToken token) => Task.Delay(span, token);
    }

    /// <summary>
    /// A script playing on a stage, step after step. The game never waits on the show: it goes on when the script lets
    /// it (<see cref="Released"/>: a <c>release</c> step, or the end), and whatever happens to the show the car is left
    /// where the script ends. A click skips it (<see cref="Skip"/>: every step from there done at once, a drive under
    /// way jumped to its end), and so does going past the time it may take; a step that goes wrong is a line in the
    /// mods' problems and the rest is skipped the same way. The screen going cancels it (<see cref="Cancel"/>): nothing
    /// more is done. Lives on the UI thread.
    /// </summary>
    public sealed class ScriptRun
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Scripts");

        // How often a wait for something looks again
        private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(16);

        private readonly CarScript? _script;
        private readonly IScriptStage _stage;
        private readonly ScriptFacts _facts;
        private readonly IScriptClock _clock;
        private readonly Action<string> _problem;

        // Cut when the run is skipped or cancelled: the wait under way ends at once
        private readonly CancellationTokenSource _cut = new();
        private readonly Task _cutTask;

        // Over: the time limit stops counting
        private readonly CancellationTokenSource _over = new();

        // The engines still being started behind the steps: the run lets go of its tokens once they are done too
        private readonly List<Task> _starting = [];

        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ScriptEnd> _end = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The latest drive of each car: a skip jumps the ones still under way to their ends
        private readonly Dictionary<IScriptCar, DrivePlan> _driving = new();

        private bool _fast;
        private bool _skipped;
        private bool _failed;
        private bool _cancelled;
        private bool _ended;
        private float _volume = 1f;

        private ScriptRun(CarScript? script, IScriptStage stage, ScriptFacts facts, IScriptClock clock, Action<string>? problem)
        {
            _script = script;
            _stage = stage;
            _facts = facts;
            _clock = clock;
            _problem = problem ?? (line => Report(script, line));
            _cutTask = Task.Delay(Timeout.Infinite, _cut.Token).ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);
            _ = RunAsync();
        }

        /// <summary>Plays <paramref name="script"/> on <paramref name="stage"/>; a null script is over at once (<see cref="ScriptEnd.None"/>)</summary>
        public static ScriptRun Play(CarScript? script, IScriptStage stage, ScriptFacts facts, IScriptClock? clock = null, Action<string>? problem = null) =>
            new(script, stage, facts, clock ?? new RealScriptClock(), problem);

        /// <summary>A run of no script: over at once, the game let go (<see cref="ScriptEnd.None"/>)</summary>
        public static ScriptRun Nothing() => new(null, NoStage.Instance, ScriptFacts.None, new RealScriptClock(), null);

        /// <summary>Plays the script for <paramref name="trigger"/> in <paramref name="book"/>, the one whose <c>when</c> holds for the stage's own car</summary>
        public static ScriptRun Play(ScriptBook book, string trigger, IScriptStage stage, ScriptFacts facts, IScriptClock? clock = null) =>
            Play(book.For(trigger, facts, stage.Car(null)?.Engine), stage, facts, clock);

        public CarScript? Script => _script;

        /// <summary>Done when the game may go on: the script let it (a <c>release</c> step), or is over</summary>
        public Task Released => _released.Task;

        /// <summary>Done when the script is over, saying how it went</summary>
        public Task<ScriptEnd> Ended => _end.Task;

        /// <summary>A car drove (a drive step was played, or skipped to its end)</summary>
        public bool Drove { get; private set; }

        public bool IsOver => Ended.IsCompleted;

        /// <summary>The rest of the script at once: the car where it would end</summary>
        public void Skip()
        {
            if (IsOver || _fast) return;
            _skipped = true;
            FastForward();
        }

        /// <summary>The screen is going: nothing more is done (the car is left where it is)</summary>
        public void Cancel()
        {
            if (IsOver) return;
            _cancelled = true;
            _fast = true;
            Cut();
        }

        private void FastForward()
        {
            _fast = true;
            foreach (var (car, plan) in _driving)
            {
                if (car.DriveLeft > 0f) car.Jump(plan);
            }

            Cut();
        }

        private async Task RunAsync()
        {
            if (_script == null)
            {
                _released.TrySetResult();
                _end.TrySetResult(ScriptEnd.None);
                LetGo();
                return;
            }

            _ = LimitAsync(_script);
            try
            {
                await StepsAsync(_script.Steps);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Each step catches its own: this is the run itself gone wrong
                Logger.Error(ex, "The script {Script} went wrong", _script.Id);
                _failed = true;
            }
            finally
            {
                _over.Cancel();
                _released.TrySetResult();
                _end.TrySetResult(_cancelled ? ScriptEnd.Cancelled : _failed ? ScriptEnd.Failed : _skipped ? ScriptEnd.Skipped : ScriptEnd.Finished);
            }

            // Over: nothing cuts or skips it any more (both look at IsOver first); an engine still starting may wait on the cut
            await Task.WhenAll(_starting);
            LetGo();
        }

        private void LetGo()
        {
            _letGo = true;
            _cut.Dispose();
            _over.Dispose();
        }

        // The tokens were let go: a skip that ended the run on its way (a car's jump plays the rest) has nothing left to cut
        private bool _letGo;

        private void Cut()
        {
            if (!_letGo) _cut.Cancel();
        }

        /// <summary>A script that goes on past the time it may take is skipped to its end</summary>
        private async Task LimitAsync(CarScript script)
        {
            try
            {
                await _clock.Delay(TimeSpan.FromSeconds(script.MostSeconds), _over.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (IsOver || _fast) return;
            Logger.Warning("The script {Script} took longer than its {Seconds} s: skipped to its end", script.Id, script.MostSeconds);
            Skip();
        }

        private async Task StepsAsync(IReadOnlyList<ScriptStep> steps)
        {
            foreach (var step in steps)
            {
                if (_cancelled || _ended) return;
                try
                {
                    await StepAsync(step);
                }
                catch (ScriptProblem problem)
                {
                    Fail(problem.Message);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Logger.Error(ex, "The script {Script} went wrong at {Step}", _script?.Id, step);
                    Fail($"{step.GetType().Name.Replace("Step", "").ToLowerInvariant()} went wrong ({ex.Message})");
                }
            }
        }

        /// <summary>A step that cannot be done: said, and the rest skipped to its end</summary>
        private void Fail(string problem)
        {
            _problem(problem);
            _failed = true;
            if (!_fast) FastForward();
        }

        private static void Report(CarScript? script, string problem)
        {
            var line = $"{script?.Id}: {problem}; the rest skipped";
            Logger.Warning("Script {Line}", line);
            ModProblems.Add("Scripts", line);
        }

        private async Task StepAsync(ScriptStep step)
        {
            switch (step)
            {
                case LightsStep lights:
                    CarFor(step).Lights(lights.Head, lights.Brake);
                    break;
                case EngineStep engine:
                    await EngineAsync(CarFor(step), engine);
                    break;
                case WaitStep wait:
                    await WaitAsync(wait);
                    break;
                case DriveStep drive:
                    await DriveAsync(CarFor(step), drive);
                    break;
                case PlaceStep place:
                    Place(CarFor(step), place);
                    break;
                case FadeStep fade:
                    await FadeAsync(fade);
                    break;
                case CameraStep camera:
                    _stage.Camera(CarFor(step), camera.Mode);
                    break;
                case IfStep branch:
                    await StepsAsync(branch.When.Holds(_facts, _stage.Car(branch.Car)?.Engine) ? branch.Then : branch.Else);
                    break;
                case SoundStep sound:
                    // A moment skipped past goes unheard
                    if (!_fast) _stage.Sound(sound.Cue);
                    break;
                case ReleaseStep:
                    _released.TrySetResult();
                    break;
                case EndStep:
                    _ended = true;
                    break;
            }
        }

        private IScriptCar CarFor(ScriptStep step) =>
            _stage.Car(step.Car) ?? throw new ScriptProblem(step.Car == null ? "there is no car here" : $"there is no car \"{step.Car}\" here");

        /// <summary>Waits a while, or not at all once skipped; never throws</summary>
        private async Task Pause(TimeSpan span)
        {
            if (_fast || span <= TimeSpan.Zero) return;
            try
            {
                await _clock.Delay(span, _cut.Token);
            }
            catch (OperationCanceledException)
            {
                // Skipped or cancelled: the step finishes at once
            }
        }

        /// <summary>Waits until <paramref name="done"/> holds, at most <paramref name="most"/>; at once when skipped</summary>
        private async Task Until(Func<bool> done, TimeSpan most)
        {
            var until = _clock.Now + most;
            while (!_fast && !done() && _clock.Now < until) await Pause(Poll);
        }

        private async Task EngineAsync(IScriptCar car, EngineStep step)
        {
            if (car.Engine is not { } engine) return;

            if (step.Rev is { } rev)
            {
                if (_fast) return;
                engine.Pedal(rev);
                await Pause(TimeSpan.FromSeconds(step.Seconds));
                engine.Pedal(null);
                return;
            }

            if (step.On != true)
            {
                engine.Stop();
                return;
            }

            if (_fast)
            {
                engine.Start(crank: false);
                return;
            }

            var most = TimeSpan.FromSeconds(step.MostSeconds);
            if (step.Wait)
            {
                engine.Start(step.Crank);
                await Until(() => engine.IsRunning, most);
                if (_fast && !_cancelled) engine.Start(crank: false);
                return;
            }

            // On behind the next steps: an engine still being worked out starts the moment it is there
            _starting.Add(KeepStartingAsync(engine, step.Crank, most));
        }

        private async Task KeepStartingAsync(IScriptEngine engine, bool crank, TimeSpan most)
        {
            try
            {
                var until = _clock.Now + most;
                while (!_fast && !engine.IsRunning && _clock.Now < until)
                {
                    engine.Start(crank);
                    await Pause(Poll);
                }

                if (_fast && !_cancelled) engine.Start(crank: false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Logger.Error(ex, "The script {Script} could not start an engine", _script?.Id);
            }
        }

        private async Task WaitAsync(WaitStep step)
        {
            var span = TimeSpan.FromSeconds(step.Seconds);
            switch (step.Until)
            {
                case WaitFor.Time:
                    await Pause(span);
                    break;
                case WaitFor.Settled:
                {
                    var car = CarFor(step);
                    await Until(() => car.IsSettled, span);
                    break;
                }
                case WaitFor.Running:
                {
                    var car = CarFor(step);
                    await Until(() => car.Engine?.IsRunning != false, span);
                    break;
                }
                case WaitFor.DriveEnd:
                {
                    var car = CarFor(step);
                    await Until(() => car.DriveLeft <= step.EarlySeconds, span);
                    break;
                }
            }
        }

        private async Task DriveAsync(IScriptCar car, DriveStep step)
        {
            var plan = Plan(car, step);
            Drove = true;
            if (_fast)
            {
                car.Jump(plan);
                return;
            }

            _driving[car] = plan;
            var driving = ObservedAsync(car.Drive(plan));
            if (step.Wait) await Task.WhenAny(driving, _cutTask);
        }

        /// <summary>
        /// A drive that ends in a fault says so in the log, whether the step waits for it or not ("wait": false is never
        /// awaited, and WhenAny would let the fault go unseen)
        /// </summary>
        private async Task ObservedAsync(Task drive)
        {
            try
            {
                await drive;
            }
            catch (OperationCanceledException)
            {
                // A drive called off with the run
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Logger.Error(ex, "The script {Script} could not drive a car", _script?.Id);
            }
        }

        /// <summary>The drive a step asks for, from where the car is</summary>
        private DrivePlan Plan(IScriptCar car, DriveStep step)
        {
            var (idle, limiter) = car.Engine?.Revs ?? (800, 6000);
            var feel = step.Feel;

            if (feel.Kind == DriveKind.Pull) return DrivePlan.Pull(car.Pose, step.Toward is { } toward ? Point(toward, car) : null, feel, idle);

            DrivePath path;
            if (step.Path is { } name)
            {
                path = DrivePath.Of(PathNamed(name));
            }
            else if (step.To is { } to)
            {
                var from = step.From is { } f ? Point(f, car) : car.Pose.Point;
                path = DrivePath.Through([from, Point(to, car)]);
            }
            else
            {
                // Along its own length: ahead, or backwards for a distance under 0
                path = DrivePath.Through([car.Pose.Point, car.Pose.Offset(step.Distance ?? 0f, 0f)]);
            }

            return feel.Kind switch
            {
                DriveKind.Launch => DrivePlan.Launch(path, feel, idle, limiter, step.Reverse),
                DriveKind.RollIn => DrivePlan.RollIn(path, feel, idle, step.Reverse),
                _ => DrivePlan.Road(path, feel, idle, step.Rolling, !step.KeepGoing, step.Reverse)
            };
        }

        private void Place(IScriptCar car, PlaceStep step)
        {
            if (step.Path is { } name)
            {
                var path = DrivePath.Of(PathNamed(name));
                var (x, z, heading, _) = path.At(step.PathEnd ? path.Length : 0f);
                car.Place(new GroundPose(x, z, heading));
                return;
            }

            var point = Point(step.At!, car);
            var facing = step.HeadingDegrees is { } degrees ? degrees * MathF.PI / 180f : car.Pose.Heading;
            car.Place(new GroundPose(point.X, point.Z, facing));
        }

        private async Task FadeAsync(FadeStep step)
        {
            var span = _fast ? TimeSpan.Zero : TimeSpan.FromSeconds(step.Seconds);
            var engine = step.Engine ? _stage.Car(step.Car)?.Engine : null;
            var started = _clock.Now;
            var fade = _stage.Fade(step.To, span, step.Ease);

            // The engine goes quiet as the picture goes black, and comes back up with it
            if (engine != null)
            {
                var (from, to) = (_volume, 1f - step.To);
                while (!_fast)
                {
                    var done = span <= TimeSpan.Zero ? 1.0 : Math.Min(1.0, (_clock.Now - started).TotalSeconds / span.TotalSeconds);
                    engine.Volume(_volume = (float)(from + (to - from) * done));
                    if (done >= 1.0) break;
                    await Pause(Poll);
                }

                // Cancelled: the screen has the engine back, at the level it sets
                if (_cancelled) return;
                engine.Volume(_volume = to);
            }

            await Task.WhenAny(fade, _cutTask);

            // Cut short: the curtain where it was going, at once
            if (_fast && !_cancelled && !fade.IsCompleted) await _stage.Fade(step.To, TimeSpan.Zero, step.Ease);

            // A screen without a curtain takes the time all the same: the steps after it come when they would
            await Pause(span - (_clock.Now - started));
        }

        private IReadOnlyList<GroundPoint> PathNamed(string name) =>
            _stage.Path(name) is { Count: > 0 } points ? points : throw new ScriptProblem($"the scene has no path \"{name}\"");

        /// <summary>Where a mark is on the ground: one the scene names, the car's home or where it is, a point, or measured from its home</summary>
        private GroundPoint Point(MarkSpec mark, IScriptCar car)
        {
            if (mark.Name is { } name)
            {
                if (name.Equals(MarkSpec.Home, StringComparison.OrdinalIgnoreCase)) return car.Home.Point;
                if (name.Equals(MarkSpec.Here, StringComparison.OrdinalIgnoreCase)) return car.Pose.Point;

                // A scene's mark is a point or measured from the car's home, never another name
                mark = _stage.Mark(name) is { Name: null } named ? named : throw new ScriptProblem($"the scene has no mark \"{name}\"");
            }

            return mark.IsPoint ? new GroundPoint(mark.X!.Value, mark.Z!.Value) : car.Home.Offset(mark.Ahead, mark.Left);
        }

        private sealed class ScriptProblem(string message) : Exception(message);

        /// <summary>The stage of a run with no script: never looked at</summary>
        private sealed class NoStage : IScriptStage
        {
            public static readonly NoStage Instance = new();

            public IScriptCar? Car(string? role) => null;

            public MarkSpec? Mark(string name) => null;

            public IReadOnlyList<GroundPoint>? Path(string name) => null;

            public Task Fade(float opacity, TimeSpan over, FadeEase ease) => Task.CompletedTask;

            public void Camera(IScriptCar car, CameraMode mode)
            {
            }
        }
    }
}
