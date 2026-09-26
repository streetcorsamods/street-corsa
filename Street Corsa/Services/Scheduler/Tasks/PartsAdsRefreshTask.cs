using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.Scheduler.Tasks
{
    /// <summary>
    /// Turns over the used parts ads daily
    /// </summary>
    public class PartsAdsRefreshTask : IScheduledTask
    {
        private readonly IPartsShopService _shopService;

        public string TaskId => "parts_ads_refresh";
        public int IntervalDays => 1;

        public PartsAdsRefreshTask(IPartsShopService shopService)
        {
            _shopService = shopService;
        }

        public Task ExecuteAsync(GameState gameState, DateTime currentDate) => _shopService.RefreshAdsAsync(gameState, currentDate);
    }
}
