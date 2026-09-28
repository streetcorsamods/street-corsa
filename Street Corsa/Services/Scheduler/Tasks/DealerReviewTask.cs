using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Dealers;

namespace StreetCorsa.Services.Scheduler.Tasks
{
    /// <summary>
    /// The dealers' morning, daily and first: a closing-down sale marks its cars down or shuts, and now and then a lot
    /// goes under or a new one opens (<see cref="DealerLifeService"/>). Before the rivals' review, so they shop today's
    /// sale prices, and long before the market's refresh, which stocks a lot that opened and leaves a closing one be.
    /// </summary>
    public class DealerReviewTask(IDealerLifeService lifeService) : IScheduledTask
    {
        private readonly IDealerLifeService _lifeService = lifeService;

        public string TaskId => "dealer_review";
        public int IntervalDays => 1;

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            _lifeService.ReviewDay(gameState, currentDate);
            return Task.CompletedTask;
        }
    }
}
