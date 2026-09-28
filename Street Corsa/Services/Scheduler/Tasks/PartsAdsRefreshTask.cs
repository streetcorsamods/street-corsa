using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.Scheduler.Tasks
{
    /// <summary>
    /// Turns over the used parts ads daily, and brings what the player ordered by mail
    /// </summary>
    public class PartsAdsRefreshTask : IScheduledTask
    {
        private readonly INewspaperPartsService _shopService;

        public string TaskId => "parts_ads_refresh";
        public int IntervalDays => 1;

        public PartsAdsRefreshTask(INewspaperPartsService shopService)
        {
            _shopService = shopService;
        }

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            _shopService.Deliver(gameState, currentDate);
            return _shopService.RefreshAdsAsync(gameState, currentDate);
        }
    }
}
