using StreetCorsa.Logging;

namespace StreetCorsa.Services.Ai
{
    /// <summary>
    /// Work the AI does that nobody waits on (a portrait, a memory, a summary, a line written ahead): started and let go,
    /// but kept track of until it ends, so the tests can wait for it (<see cref="WhenIdle"/>), and what it throws is a line
    /// in the log rather than an unobserved task.
    /// </summary>
    public sealed class AiBackground
    {
        private readonly object _lock = new();
        private readonly HashSet<Task> _running = [];
        private readonly IAppLogger _logger;
        private readonly string _what;

        /// <param name="logger">Where a failure is told</param>
        /// <param name="what">What the work is, for the log ("AI background work")</param>
        public AiBackground(IAppLogger logger, string what = "AI background work")
        {
            _logger = logger;
            _what = what;
        }

        /// <summary>Everything started so far, done</summary>
        public Task WhenIdle()
        {
            lock (_lock) return Task.WhenAll(_running.ToList());
        }

        /// <summary><paramref name="work"/> on the thread pool; <paramref name="what"/> names it in the log when it fails (else the constructor's)</summary>
        public void Run(Func<Task> work, string? what = null) => Track(Guarded(() => Task.Run(work), what));

        /// <summary>
        /// <paramref name="work"/> started here, on the caller's thread: its awaits come back to the caller's context (a
        /// talk's summary, kept by the screen's thread)
        /// </summary>
        public void Start(Func<Task> work, string? what = null) => Track(Guarded(work, what));

        private async Task Guarded(Func<Task> work, string? what)
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "{What} failed", what ?? _what);
            }
        }

        private void Track(Task task)
        {
            if (task.IsCompleted) return;
            lock (_lock) _running.Add(task);
            task.ContinueWith(t => { lock (_lock) _running.Remove(t); }, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// A rest after a provider could not be reached: nothing is asked of it for a while, and the player is told once (the
    /// first failure starts the rest, the ones during it do not)
    /// </summary>
    public sealed class AiBackoff
    {
        private readonly object _lock = new();
        // Environment.TickCount64 (ms) the rest ends: a clock that never steps when the system time is set
        private long _until = long.MinValue;

        /// <summary>The provider is left alone now</summary>
        public bool IsResting
        {
            get
            {
                lock (_lock) return Environment.TickCount64 < _until;
            }
        }

        /// <summary>Rests for <paramref name="seconds"/>: false when it rests already (nothing to tell)</summary>
        public bool Rest(double seconds)
        {
            lock (_lock)
            {
                var now = Environment.TickCount64;
                if (now < _until) return false;
                _until = now + Milliseconds(seconds);
                return true;
            }
        }

        /// <summary>It answered (a Test, new settings): asked again at once</summary>
        public void Clear()
        {
            lock (_lock) _until = long.MinValue;
        }

        /// <summary>Seconds from the rules as milliseconds: none for a negative or NaN figure, at most a year</summary>
        internal static long Milliseconds(double seconds) =>
            double.IsFinite(seconds) || seconds > 0 ? (long)(Math.Clamp(seconds, 0, 365 * 24 * 3600.0) * 1000) : 0;
    }
}
