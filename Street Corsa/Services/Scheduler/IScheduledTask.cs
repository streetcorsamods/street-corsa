using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Scheduler
{
    /// <summary>
    /// Represents a task that runs on a game time schedule
    /// </summary>
    /// <remarks>
    /// Tasks run inside GameTimeService's spend of time, which holds its (non-reentrant) lock: a task must never
    /// spend time or end the day itself (SpendTimeAsync/EndDayAsync), or it waits for itself for good.
    /// A task that fails is run again the next time, so it does all its fallible work before changing the game state.
    /// </remarks>
    public interface IScheduledTask
    {
        /// <summary>
        /// Unique identifier for this task (used for tracking execution state)
        /// </summary>
        string TaskId { get; }

        /// <summary>
        /// How often this task should run in game days
        /// </summary>
        int IntervalDays { get; }

        /// <summary>
        /// The system the task is the work of (<see cref="Rules.SystemRules"/>): in a game with it switched off the task
        /// never runs. None: it always does.
        /// </summary>
        string? System => null;

        /// <summary>
        /// Execute the scheduled task
        /// </summary>
        /// <param name="gameState">Current game state to modify</param>
        /// <param name="currentDate">Current game date</param>
        Task ExecuteAsync(GameState gameState, DateTime currentDate);
    }
}
