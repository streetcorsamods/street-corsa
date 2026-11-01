using StreetCorsa.Logging;

namespace StreetCorsa.Services.Ai
{
    /// <summary>
    /// The shows playing: a car on the move in a scene (a drive into the garage, up to the pump, the flow's scripts), drawn
    /// on the graphics card a local AI works on. One of these is shared by everything of the AI's that works that card in
    /// the background (the text service's requests, ComfyUI's pictures, the model loaded again after a race): each waits
    /// for the last show to end (<see cref="OverAsync"/>) before it starts. Shows are counted: each start wants its stop
    /// (<see cref="Set"/>). A show still on after <see cref="MostWait"/> is a stop that never came: it is taken as over,
    /// with a warning, so the AI's background work is never held for good. Never throws, never waits.
    /// </summary>
    public sealed class AiShows
    {
        /// <summary>
        /// The longest a show is waited for: the longest a scene's script may run (<see cref="Scripting.CarScript.LongestMostSeconds"/>)
        /// and a minute more
        /// </summary>
        public static readonly TimeSpan MostWait = TimeSpan.FromSeconds(Scripting.CarScript.LongestMostSeconds + 60);

        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Ai");

        private readonly object _lock = new();
        private readonly TimeSpan _mostWait;
        private int _shows;

        // Done when no show plays
        private TaskCompletionSource _over = Done();

        /// <param name="mostWait">The longest a show is waited for; <see cref="MostWait"/> when not given</param>
        public AiShows(TimeSpan? mostWait = null)
        {
            _mostWait = mostWait ?? MostWait;
        }

        private static TaskCompletionSource Done()
        {
            var over = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            over.SetResult();
            return over;
        }

        /// <summary>A show plays somewhere</summary>
        public bool Showing => Volatile.Read(ref _shows) > 0;

        /// <summary>A show starts (<paramref name="showing"/>) or ends; a stop with no show on changes nothing</summary>
        public void Set(bool showing)
        {
            TaskCompletionSource? over = null;
            lock (_lock)
            {
                if (showing)
                {
                    if (_shows++ == 0) _over = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                else if (_shows > 0 && --_shows == 0)
                {
                    over = _over;
                }
            }

            over?.TrySetResult();
        }

        /// <summary>
        /// The last show is over (true), or the wait was called off (false). A show on for longer than the most is taken as
        /// over (true): its stop was lost, and a warning says so.
        /// </summary>
        public async Task<bool> OverAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource over;
            lock (_lock) over = _over;
            try
            {
                await over.Task.WaitAsync(_mostWait, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                var forgotten = 0;
                lock (_lock)
                {
                    // Only the shows this wait saw start: a new one since is waited for afresh
                    if (ReferenceEquals(over, _over) && _shows > 0)
                    {
                        forgotten = _shows;
                        _shows = 0;
                    }
                }

                if (forgotten > 0)
                    Logger.Warning("A show has not ended in {Seconds:0} s ({Count} on): taken as over, so the AI's background work goes on", _mostWait.TotalSeconds,
                        forgotten);
                over.TrySetResult();
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
