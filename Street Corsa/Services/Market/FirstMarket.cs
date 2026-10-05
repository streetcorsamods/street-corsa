using System.Runtime.CompilerServices;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Services.Market
{
    /// <summary>
    /// A game's first classifieds: the lots stocked and the part ads in the paper, made when the game is created (in the
    /// background, the garage opens at once), so every ad is there from the first day and the player's AI writes for it
    /// as it is made. After that the daily tasks turn them over (<see cref="Scheduler.Tasks.MarketRefreshTask"/>,
    /// <see cref="Scheduler.Tasks.PartsAdsRefreshTask"/>). A game from before, or one whose first market did not get
    /// made, gets it when the paper or the dealers' map is first opened. A market from before the paper had cars of its
    /// own (the private people's, the dealers' out back) gets those then too: the paper prints only a car or so off
    /// each lot, and would be all but empty until the day turned over, or for good with the market's turnover off.
    ///
    /// One run per game at a time: a screen opened while the first market is still being made waits for it rather than
    /// making a second. Run on the UI thread: the game state is changed there only.
    /// </summary>
    public static class FirstMarket
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Market");
        private static readonly ConditionalWeakTable<GameState, Task> Running = new();

        /// <summary>Stocks the lots and fills the part ads of a game that has none yet; the run going on, if there is one</summary>
        public static Task EnsureAsync(GameState state, IUsedCarMarketService market, INewspaperPartsService partAds)
        {
            if (Running.TryGetValue(state, out var running) && !running.IsCompleted) return running;

            var task = RunAsync(state, market, partAds);
            Running.AddOrUpdate(state, task);
            return task;
        }

        /// <summary>Waits for the first market being made for <paramref name="state"/>, if it is; never throws</summary>
        public static async Task WaitAsync(GameState state)
        {
            if (!Running.TryGetValue(state, out var running)) return;

            try
            {
                await running;
            }
            catch (Exception ex)
            {
                Logger.Warning("The first market was not made: {Error}", ex.Message);
            }
        }

        /// <summary>The rules put cars in the paper only: a market without any is one from before, not one meant so</summary>
        private static bool WantsPaperOnly()
        {
            var rules = Rules.RuleBook.Current.Market;
            return Math.Max(rules.PrivateAdsLow, rules.PrivateAdsHigh) > 0 || Math.Max(rules.BackLotLow, rules.BackLotHigh) > 0;
        }

        private static async Task RunAsync(GameState state, IUsedCarMarketService market, INewspaperPartsService partAds)
        {
            if (state.UsedCarMarket is not { Count: > 0 })
            {
                if (state.DealerLocations is not { Count: > 0 }) state.DealerLocations = market.GetDefaultDealers();

                var spawned = await market.SpawnListingsAsync(DealerRoster.Restocking(state), state.Date, state.Rules.CarPriceMultiplier);

                // The day's refresh may have stocked the lots meanwhile: what it put there stays
                if (state.UsedCarMarket is not { Count: > 0 })
                {
                    state.UsedCarMarket = spawned;
                    Logger.Information("Spawned {ListingCount} initial listings", spawned.Count);
                }
            }
            else if (!state.UsedCarMarket.Any(l => l.PaperOnly) && WantsPaperOnly())
            {
                var paperOnly = await market.SpawnPaperOnlyAsync(DealerRoster.Restocking(state), state.Date, state.Rules.CarPriceMultiplier);

                // The day's refresh may have put some in meanwhile: then those are the paper's
                if (!state.UsedCarMarket.Any(l => l.PaperOnly))
                {
                    state.UsedCarMarket.AddRange(paperOnly);
                    Logger.Information("Put {ListingCount} cars in the paper of a market from before it had its own", paperOnly.Count);
                }
            }

            if (state.NewspaperAds.Parts.Count == 0 && await Task.Run(() => partAds.IsAvailable))
                await partAds.RefreshAdsAsync(state, state.Date);
        }
    }
}
