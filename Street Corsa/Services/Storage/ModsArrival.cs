using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Scheduler;

namespace StreetCorsa.Services.Storage
{
    /// <summary>
    /// A save opened with mods new since it was last played (<see cref="ModsInSave.Arrived"/>): what the game already
    /// made from the old mods is turned over at once, so what the new ones add is out there on the first day rather
    /// than weeks of deliveries later. The shops and dealers of a new mod join; every open shop gets a whole new
    /// delivery (shelves, kits, a junkyard's wrecks); the car lots and the paper's parts ads turn over as on a new day.
    /// The player's own cars and the rivals' builds are left as they are: changing what somebody built would be the game
    /// rewriting the save (a car with no engine of its make still gets its factory one, as on any load).
    /// </summary>
    public sealed class ModsArrival(IPartShopService partShops, IDealerCatalog dealers, IGameTimeScheduler scheduler)
    {
        /// <summary>The daily tasks run at once: the car lots, the paper's and the mail order's parts ads</summary>
        public static readonly string[] TasksRunNow = ["market_refresh", "parts_ads_refresh"];

        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Save);

        /// <summary>The mods new to the save, after turning over what they add to; none, and nothing is touched</summary>
        public async Task<IReadOnlyList<ModInfo>> FreshenAsync(GameState gameState, ModSet inPlay)
        {
            var arrived = ModsInSave.Arrived(gameState, inPlay);
            if (arrived.Count == 0) return arrived;

            _logger.Information("New mods since the save was last played: {Mods}; the shops, lots and paper turn over",
                string.Join(", ", arrived.Select(m => $"{m.Id} {m.Version}")));

            PartShopRoster.Sync(gameState, partShops.Catalog);
            DealerRoster.Sync(gameState, dealers);

            // A whole new delivery: what a shop carries is picked again from every mod's parts, the new ones among them
            var emptied = 0;
            foreach (var shop in gameState.PartShops ?? [])
            {
                if (shop.Status != DealerStatus.Open || partShops.Catalog.Get(shop.Id) is not { KindRules.Sells: true }) continue;

                shop.Shelves = [];
                shop.Kits = [];
                shop.Yard = [];
                shop.YardKits = [];
                shop.RestockedOn = null;
                emptied++;
            }

            if (emptied > 0) await partShops.RestockAsync(gameState, gameState.Date, Random.Shared, firstOnly: true);

            var failed = await scheduler.RunNowAsync(gameState, gameState.Date, TasksRunNow);
            if (failed.Count > 0) _logger.Warning("Turning over for the new mods: {Tasks} failed; they run again tomorrow", string.Join(", ", failed));

            return arrived;
        }
    }
}
