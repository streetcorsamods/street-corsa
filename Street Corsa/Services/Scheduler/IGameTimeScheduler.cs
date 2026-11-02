using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Scheduler
{
    /// <summary>
    /// Scheduler that executes tasks based on game time progression
    /// </summary>
    public interface IGameTimeScheduler
    {
        /// <summary>
        /// Register a task to be executed on schedule
        /// </summary>
        void RegisterTask(IScheduledTask task);

        /// <summary>
        /// Called when game time advances. Executes any due tasks.
        /// </summary>
        /// <param name="gameState">Current game state</param>
        /// <param name="previousDate">Date before advancement</param>
        /// <param name="newDate">Date after advancement</param>
        /// <returns>The ids of the due tasks that failed; they are tried again on the next day</returns>
        Task<IReadOnlyList<string>> OnTimeAdvancedAsync(GameState gameState, DateTime previousDate, DateTime newDate);

        /// <summary>
        /// Runs the tasks of these ids now, due or not (a save that has new mods since it was last played: the lots and
        /// the paper turned over at once). A task whose system the game does without is left out, as on a new day.
        /// </summary>
        /// <returns>The ids of the tasks that failed</returns>
        Task<IReadOnlyList<string>> RunNowAsync(GameState gameState, DateTime date, params string[] taskIds);
    }
}
