using StreetCorsa.Models.GameState;
using StreetCorsa.Services.PartShops;

namespace StreetCorsa.Services.Scheduler.Tasks
{
    /// <summary>
    /// The part shops' morning, daily, after the dealers' and before the rivals': a sale marks down and clears out or
    /// shuts, now and then a shop goes under or opens, and the deliveries come in (<see cref="PartShopLifeService"/>).
    /// The rivals tune their engines from what is on the shelves this morning.
    /// </summary>
    public class PartShopReviewTask(IPartShopLifeService lifeService) : IScheduledTask
    {
        private readonly IPartShopLifeService _lifeService = lifeService;

        public string TaskId => "partshop_review";
        public int IntervalDays => 1;
        public string System => "partShops";

        public Task ExecuteAsync(GameState gameState, DateTime currentDate) => _lifeService.ReviewDayAsync(gameState, currentDate);
    }
}
