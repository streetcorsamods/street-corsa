using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Scheduler;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Storage
{
    /// <summary>
    /// A save opened with mods new since it was last played (<see cref="ModsInSave.Arrived"/>): what the game already
    /// made from the old mods is turned over at once, so what the new ones add is out there on the first day rather
    /// than weeks of deliveries later. The shops and dealers of a new mod join; every open shop gets a whole new
    /// delivery (<see cref="IPartShopService.DeliverAnewAsync"/>); the cars the market put out itself and the paper's
    /// own parts ads are taken off and made again (<see cref="UsedCarMarketService.TurnOver"/>,
    /// <see cref="NewspaperPartsService.TurnOver"/>). A game whose market stands still
    /// (<see cref="GameRules.MarketRefreshEnabled"/>) keeps its shelves and lots: only a shop just joined is stocked.
    ///
    /// The player's own cars, what they sold to a junkyard and the rivals' builds and ads are left as they are: changing
    /// those would be the game rewriting the save. Nothing here is needed to play: a step that fails is logged and
    /// leaves what it was given, and the save opens with the rest done.
    /// </summary>
    public sealed class ModsArrival(IPartShopService partShops, IDealerCatalog dealers, IGameTimeScheduler scheduler)
    {
        /// <summary>The daily tasks run at once: the car lots, the paper's and the mail order's parts ads</summary>
        public const string MarketTask = "market_refresh", PaperTask = "parts_ads_refresh";

        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Save);

        /// <summary>What turned over for the new mods, to tell the player; no mods, and nothing was touched</summary>
        public sealed record Outcome(IReadOnlyList<ModInfo> Mods, bool Shops, bool Lots, bool Paper, bool MarketStill)
        {
            public static readonly Outcome None = new([], false, false, false, false);
        }

        /// <summary>The mods new to the save and what turned over for them; never thrown</summary>
        public async Task<Outcome> FreshenAsync(GameState gameState, ModSet inPlay)
        {
            List<ModInfo> arrived;
            try
            {
                arrived = ModsInSave.Arrived(gameState, inPlay);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not tell which mods are new to the save; nothing turns over");
                return Outcome.None;
            }

            if (arrived.Count == 0) return Outcome.None;

            var still = !gameState.Rules.MarketRefreshEnabled;
            _logger.Information("New mods since the save was last played: {Mods}; {What} turn over",
                string.Join(", ", arrived.Select(m => $"{m.Id} {m.Version}")), still ? "the paper's ads" : "the shops, lots and paper");

            try
            {
                PartShopRoster.Sync(gameState, partShops.Catalog);
                DealerRoster.Sync(gameState, dealers);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "The shops and dealers of the new mods could not join; they join on a later load");
            }

            var shops = false;
            try
            {
                // A whole new delivery, what a shop carries picked again from every mod's parts; a market that stands
                // still only stocks a shop that just joined
                if (still) await partShops.RestockAsync(gameState, gameState.Date, Random.Shared, firstOnly: true);
                else shops = await partShops.DeliverAnewAsync(gameState, gameState.Date, Random.Shared) > 0;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "The shops' delivery for the new mods failed; they keep what they had");
            }

            var lots = !still && await TurnOverAsync(gameState, MarketTask,
                () => gameState.UsedCarMarket.ToList(),
                () => UsedCarMarketService.TurnOver(gameState.UsedCarMarket),
                kept => gameState.UsedCarMarket = kept);

            // The paper's ads are only made while the paper or the mail order shows them (PartsAdsRefreshTask)
            var paper = (Navigation.FlowBook.InPlay("paper") || Navigation.FlowBook.InPlay("mailOrder")) && await TurnOverAsync(gameState, PaperTask,
                () => (Parts: gameState.NewspaperAds.Parts.ToList(), Kits: gameState.NewspaperAds.Kits?.ToList()),
                () => NewspaperPartsService.TurnOver(gameState),
                kept => (gameState.NewspaperAds.Parts, gameState.NewspaperAds.Kits) = kept);

            return new Outcome(arrived, shops, lots, paper, still);
        }

        /// <summary>
        /// Takes off what <paramref name="takeOff"/> does and runs the daily task that fills it again at once; if the task
        /// fails, what was there is put back (<paramref name="putBack"/>) rather than leaving it bare till tomorrow. True
        /// when it turned over.
        /// </summary>
        private async Task<bool> TurnOverAsync<T>(GameState gameState, string task, Func<T> keep, Func<int> takeOff, Action<T> putBack)
        {
            T kept;
            try
            {
                kept = keep();
                takeOff();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not turn over for {Task}", task);
                return false;
            }

            bool ran;
            try
            {
                // The scheduler logs a task's failure and leaves it due; a task the game does without does not run
                ran = (await scheduler.RunNowAsync(gameState, gameState.Date, task)).Contains(task);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "{Task} failed for the new mods", task);
                ran = false;
            }

            if (!ran) putBack(kept);
            return ran;
        }
    }

    /// <summary>What the player is told of the mods new to a save, and what turned over for them</summary>
    public static class ModsArrivalNote
    {
        public static string? For(GameState gameState, ModsArrival.Outcome outcome)
        {
            if (outcome.Mods.Count == 0) return null;

            var fresh = new List<string>();
            if (outcome.Shops) fresh.Add(GameText.Say("loadgame.mods_new.shops"));
            if (outcome.Lots) fresh.Add(GameText.Say("loadgame.mods_new.lots"));
            if (outcome.Paper) fresh.Add(GameText.Say("loadgame.mods_new.paper"));
            if (outcome.MarketStill) fresh.Add(GameText.Say("loadgame.mods_new.still"));
            else if (!outcome.Shops || !outcome.Lots || !outcome.Paper) fresh.Add(GameText.Say("loadgame.mods_new.later"));
            return ModsInSave.ArrivedNote(gameState, outcome.Mods, string.Join(" ", fresh));
        }
    }
}
