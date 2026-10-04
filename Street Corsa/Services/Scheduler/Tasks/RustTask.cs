using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Services.Scheduler.Tasks
{
    /// <summary>
    /// Every day the rust on every car in the game grows (<see cref="CarLooks.Age"/>): the player's, the rivals' and the
    /// cars on the lots. A body with sound paint and no rust stays as it is. Several days gone by at once (a delivery
    /// waited for, a load after a jump of the date) are each a day's rust.
    /// </summary>
    public class RustTask : IScheduledTask
    {
        public string TaskId => "rust";
        public int IntervalDays => 1;

        /// <summary>The most days aged at once: a save's first run (never run before) is one day's, not an age's</summary>
        private const int MostDays = 365;

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            var days = DaysSinceLastRun(gameState, currentDate);
            foreach (var car in gameState.Player.Cars ?? []) CarLooks.Age(car, days);
            foreach (var racer in gameState.Racers?.All ?? [])
                foreach (var car in racer.Cars ?? []) CarLooks.Age(car, days);
            foreach (var listing in gameState.UsedCarMarket ?? []) Age(listing, days);
            return Task.CompletedTask;
        }

        /// <summary>The days gone by since the task last ran (the scheduler's record of it): at least one, at most <see cref="MostDays"/></summary>
        private int DaysSinceLastRun(GameState gameState, DateTime currentDate)
        {
            var last = gameState.ScheduledTasks?.FirstOrDefault(t => t.TaskId == TaskId)?.LastExecuted;
            if (last is not { } ran || ran == DateTime.MinValue) return 1;
            return Math.Clamp((currentDate.Date - ran.Date).Days, 1, MostDays);
        }

        /// <summary>A car on a lot rusts as one in a garage does</summary>
        private static void Age(UsedCarListing listing, int days)
        {
            var car = new Car { Rust = listing.Rust, PaintWear = listing.PaintWear };
            CarLooks.Age(car, days);
            listing.Rust = car.Rust;
        }
    }
}
