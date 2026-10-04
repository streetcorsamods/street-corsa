using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Services.Scheduler.Tasks
{
    /// <summary>
    /// Every day the rust on every car in the game grows (<see cref="CarLooks.Age"/>): the player's, the rivals' and the
    /// cars on the lots. A body with sound paint and no rust stays as it is.
    /// </summary>
    public class RustTask : IScheduledTask
    {
        public string TaskId => "rust";
        public int IntervalDays => 1;

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            foreach (var car in gameState.Player.Cars ?? []) CarLooks.Age(car);
            foreach (var racer in gameState.Racers?.All ?? [])
                foreach (var car in racer.Cars ?? []) CarLooks.Age(car);
            foreach (var listing in gameState.UsedCarMarket ?? []) Age(listing);
            return Task.CompletedTask;
        }

        /// <summary>A car on a lot rusts as one in a garage does</summary>
        private static void Age(UsedCarListing listing)
        {
            var car = new Car { Rust = listing.Rust, PaintWear = listing.PaintWear };
            CarLooks.Age(car);
            listing.Rust = car.Rust;
        }
    }
}
