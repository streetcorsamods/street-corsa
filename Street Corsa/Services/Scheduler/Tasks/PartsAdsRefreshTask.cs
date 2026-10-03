using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.Scheduler.Tasks
{
    /// <summary>
    /// Turns over the used parts ads daily (the paper's and the mail order's), and brings what the player ordered by mail:
    /// the deliveries come whatever the game has, the ads only while the paper or the mail order shows them
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
            var systems = Rules.RuleBook.Current.Systems;
            return systems.Paper || systems.MailOrder ? _shopService.RefreshAdsAsync(gameState, currentDate) : Task.CompletedTask;
        }
    }
}
