using StreetCorsa.Models.GameState;
using StreetCorsa.Rules;

namespace StreetCorsa.Services.Scheduler.Tasks
{
    /// <summary>
    /// In a game with no gas station (<see cref="SystemRules.Station"/> off), what it would have done is done overnight:
    /// the player's cars are filled up, their oil topped up and their bodies washed, free. With a station it does nothing.
    /// </summary>
    public class StationStandInTask : IScheduledTask
    {
        public string TaskId => "station_stand_in";
        public int IntervalDays => 1;

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            if (RuleBook.Current.Systems.Station) return Task.CompletedTask;

            foreach (var car in gameState.Player.Cars ?? [])
            {
                car.FuelLitres = null;
                car.OilLevel = null;
                car.BodyDirt = 0;
            }

            return Task.CompletedTask;
        }
    }
}
