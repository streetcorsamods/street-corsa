using StreetCorsa.Logging;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Talk
{
    /// <summary>
    /// What racers say, in the player's AI's words when it has them ready: the game's own line (<see cref="StaticTalkService"/>)
    /// is always made first and is what the AI tells again in the racer's voice. A line written ahead for the moment (the
    /// racer, what was said, the track, the cars) is said at once; with none, the AI gets <see cref="Rules.AiRules.TalkWaitSeconds"/>
    /// to write one, then the game's own line is said and the AI's is kept for the next time that moment comes.
    ///
    /// The screens show whatever a call returns, last come last shown: a call still waiting when a newer one starts gives
    /// the newer one's line, so a slow answer never puts an old line over a new one.
    /// </summary>
    public sealed class AiTalkService : ITalkService
    {
        public const string TalkUse = "talk";

        /// <summary>Lines kept written ahead at most; the oldest go</summary>
        private const int MaxReady = 200;

        private readonly ITalkService _own;
        private readonly IAiText _ai;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Ai");

        private readonly object _lock = new();
        private readonly Dictionary<string, Queue<string>> _ready = new(StringComparer.Ordinal);
        private readonly LinkedList<string> _readyOrder = new();
        private readonly HashSet<string> _writing = new(StringComparer.Ordinal);
        private Call? _current;

        private sealed class Call
        {
            public TaskCompletionSource<string> Said { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public CancellationTokenSource Superseded { get; } = new();

            /// <summary>A newer line is asked: this call stops waiting (one already said and let go is left as it is)</summary>
            public void Supersede()
            {
                try
                {
                    Superseded.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Said and done already
                }
            }
        }

        private readonly Func<StreetCorsa.Rules.AiRules> _rules;

        /// <param name="own">The game's own lines</param>
        /// <param name="ai">The player's AI</param>
        /// <param name="rules">The figures it works by; the game in play's when not given</param>
        public AiTalkService(ITalkService own, IAiText ai, Func<StreetCorsa.Rules.AiRules>? rules = null)
        {
            _own = own;
            _ai = ai;
            _rules = rules ?? (() => StreetCorsa.Rules.RuleBook.Current.Ai);
            _background = new AiBackground(_logger, "Writing a line ahead");
        }

        private readonly AiBackground _background;

        /// <summary>The lines being written ahead, done (for the tests)</summary>
        public Task WhenIdle() => _background.WhenIdle();

        private StreetCorsa.Rules.AiRules Rules => _rules();

        public bool IsAvailable => true;

        public async Task<string> GetMessageAsync(TalkContext context)
        {
            var call = new Call();
            var previous = Interlocked.Exchange(ref _current, call);
            previous?.Supersede();

            var own = string.Empty;
            try
            {
                own = await _own.GetMessageAsync(context).ConfigureAwait(true);
                var line = await SayAsync(context, own, call.Superseded.Token).ConfigureAwait(true);
                call.Said.TrySetResult(line);
                return line;
            }
            catch (OperationCanceledException) when (call.Superseded.IsCancellationRequested)
            {
                // A newer line is on its way: this call says the same, so the screen ends on the newest whichever lands last
                var newest = Volatile.Read(ref _current)!;
                var line = ReferenceEquals(newest, call) ? own : await newest.Said.Task.ConfigureAwait(true);
                call.Said.TrySetResult(line);
                return line;
            }
            catch (Exception ex)
            {
                _logger.Debug("Talk fell back to the game's own line: {Error}", ex.Message);
                call.Said.TrySetResult(own);
                return own;
            }
            finally
            {
                // One a line said: a newer call that supersedes it finds it let go
                call.Superseded.Dispose();
            }
        }

        /// <summary>
        /// A line for this moment written ahead, in the background (the diner prepares each racer's greeting as it lists
        /// them); nothing when the AI is off or one is ready already
        /// </summary>
        public void Prepare(TalkContext context)
        {
            if (!_ai.IsAvailable) return;
            _background.Start(() => PrepareAsync(context));
        }

        private async Task PrepareAsync(TalkContext context)
        {
            try
            {
                var own = await _own.GetMessageAsync(context).ConfigureAwait(false);
                if (own.Length == 0) return;
                var key = KeyOf(context);
                lock (_lock)
                {
                    if (ReadyCount(key) >= Math.Max(1, Rules.TalkLinesAhead) || !_writing.Add(key)) return;
                }

                try
                {
                    if (await _ai.WriteAsync(TalkUse, ValuesFor(context, own)).ConfigureAwait(false) is { } line) Keep(key, line);
                }
                finally
                {
                    lock (_lock) _writing.Remove(key);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("A line could not be written ahead: {Error}", ex.Message);
            }
        }

        private async Task<string> SayAsync(TalkContext context, string own, CancellationToken superseded)
        {
            if (own.Length == 0 || !_ai.IsAvailable) return own;

            var key = KeyOf(context);
            PrepareNext(context);
            if (Take(key) is { } ready)
            {
                Prepare(context);
                return ready;
            }

            // Written for the player now, ahead of the background's work; kept for next time if it comes too late. Whoever
            // claims the answer first has it: the line said now, or the one kept
            var claimed = 0;
            var writing = _ai.WriteAsync(TalkUse, ValuesFor(context, own), AiPriority.Now);
            // Kept as background work (WhenIdle): a late line's keeping is something the tests wait for
            _background.Start(() => writing.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result is { } late && Interlocked.Exchange(ref claimed, 1) == 0) Keep(key, late);
            }, TaskScheduler.Default));

            var wait = Task.Delay(TimeSpan.FromSeconds(Math.Max(0, Rules.TalkWaitSeconds)), superseded);
            var first = await Task.WhenAny(writing, wait).ConfigureAwait(true);
            superseded.ThrowIfCancellationRequested();
            if (first == writing && writing.Result is { } line)
                return Interlocked.Exchange(ref claimed, 1) == 0 ? line : Take(key) ?? own;

            return own;
        }

        /// <summary>The moments likely next after this one, written ahead: pink slips talked of after a greeting</summary>
        private void PrepareNext(TalkContext context)
        {
            if (context.Trigger == TalkTrigger.OpponentSelected && !context.IsPinkSlipBet)
                Prepare(With(context, TalkTrigger.BetTypeChanged, pinkSlip: true));
        }

        private static TalkContext With(TalkContext context, TalkTrigger trigger, bool pinkSlip) => new()
        {
            Opponent = context.Opponent,
            Player = context.Player,
            OpponentCar = context.OpponentCar,
            PlayerCar = context.PlayerCar,
            Trigger = trigger,
            IsPinkSlipBet = pinkSlip,
            SelectedTrackName = context.SelectedTrackName,
            Grudge = context.Grudge,
            GrudgeCarName = context.GrudgeCarName,
            PlayerHasGrudgeCar = context.PlayerHasGrudgeCar
        };

        /// <summary>
        /// The moment a line belongs to: who says it, what to, about which track and cars, and where the two stand (the
        /// game's own line is picked by the same)
        /// </summary>
        public static string KeyOf(TalkContext context)
        {
            var standing = context.Opponent.Stats.Reputation - context.Player.Stats.Reputation;
            return string.Join("|", context.Opponent.OpponentId, context.Trigger, context.IsPinkSlipBet, context.Grudge != null,
                context.PlayerHasGrudgeCar, context.SelectedTrackName, context.PlayerCar?.Id, context.OpponentCar?.Id, standing / 5,
                context.Opponent.Aggression / 10, AiWriter.Language);
        }

        /// <summary>What the prompt may name: the game's line, the racer, the moment</summary>
        public static TextValues ValuesFor(TalkContext context, string line)
        {
            var opponent = context.Opponent;
            var repDiff = opponent.Stats.Reputation - context.Player.Stats.Reputation;
            return new TextValues
            {
                ["line"] = line,
                ["name"] = opponent.Name,
                ["nickname"] = opponent.Nickname,
                ["age"] = opponent.Age,
                ["aggression"] = opponent.Aggression,
                ["temper"] = opponent.Aggression >= StreetCorsa.Rules.RuleBook.Current.Street.TalkAggressive ? "hothead" : "cool-headed",
                ["standing"] = repDiff > 0 ? "better known than the player" : repDiff < 0 ? "less known than the player" : "as well known as the player",
                ["boss"] = opponent.IsBoss ? "yes" : "no",
                ["moment"] = context.Trigger.ToString(),
                ["pinkSlip"] = context.IsPinkSlipBet ? "yes" : "no",
                ["grudge"] = context.Grudge != null ? "yes" : "no",
                ["track"] = context.SelectedTrackName ?? string.Empty,
                ["car"] = context.OpponentCar is { } car ? Catalog.CarNames.Of(car) : string.Empty,
                ["playerCar"] = context.PlayerCar is { } playerCar ? Catalog.CarNames.Of(playerCar) : string.Empty,
                ["biography"] = opponent.Biography ?? string.Empty
            };
        }

        // ----- the lines written ahead -----

        private int ReadyCount(string key) => _ready.TryGetValue(key, out var lines) ? lines.Count : 0;

        private void Keep(string key, string line)
        {
            lock (_lock)
            {
                if (!_ready.TryGetValue(key, out var lines)) _ready[key] = lines = new Queue<string>();
                lines.Enqueue(line);
                _readyOrder.AddLast(key);
                while (_readyOrder.Count > MaxReady)
                {
                    var oldest = _readyOrder.First!.Value;
                    _readyOrder.RemoveFirst();
                    if (_ready.TryGetValue(oldest, out var old) && old.TryDequeue(out _) && old.Count == 0) _ready.Remove(oldest);
                }
            }
        }

        private string? Take(string key)
        {
            lock (_lock)
            {
                if (!_ready.TryGetValue(key, out var lines) || !lines.TryDequeue(out var line)) return null;
                if (lines.Count == 0) _ready.Remove(key);
                var node = _readyOrder.Find(key);
                if (node != null) _readyOrder.Remove(node);
                return line;
            }
        }
    }
}
