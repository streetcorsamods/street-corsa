namespace StreetCorsa.Services.Ai
{
    /// <summary>
    /// The shows playing: a car on the move in a scene (a drive into the garage, up to the pump, the flow's scripts), drawn
    /// on the graphics card a local AI works on. One of these is shared by everything of the AI's that works that card in
    /// the background (the text service's requests, ComfyUI's pictures, the model loaded again after a race): each waits
    /// for the last show to end (<see cref="OverAsync"/>) before it starts. Shows are counted: each start wants its stop
    /// (<see cref="Set"/>). Never throws, never waits.
    /// </summary>
    public sealed class AiShows
    {
        private readonly object _lock = new();
        private int _shows;

        // Done when no show plays
        private TaskCompletionSource _over = Done();

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

        /// <summary>The last show is over (true), or the wait was called off (false)</summary>
        public async Task<bool> OverAsync(CancellationToken cancellationToken)
        {
            Task over;
            lock (_lock) over = _over.Task;
            try
            {
                await over.WaitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
