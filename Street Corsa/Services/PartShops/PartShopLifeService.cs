using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.News;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.PartShops
{
    /// <summary>The part shops' business, day by day: see <see cref="PartShopLifeService"/></summary>
    public interface IPartShopLifeService
    {
        Task ReviewDayAsync(GameState gameState, DateTime date);
    }

    /// <summary>
    /// Part shops come and go as the car dealers do (<see cref="DealerLifeService"/>, on the same rules,
    /// <see cref="DealerRules"/> and <see cref="DealerChurn"/>). Every morning:
    /// <list type="bullet">
    /// <item>the save's shops are brought up to the mods' (<see cref="PartShopRoster.Sync"/>);</item>
    /// <item>each shop's trade fades (what it sells to the player and the rivals adds to it as it sells,
    /// <see cref="PartShopRules.TradePerSale"/>, and the town's custom a little each day), and the town's other customers
    /// buy a little off the shelves (<see cref="PartShopRules.TownBuysShare"/>);</item>
    /// <item>a shop in its closing-down sale marks everything down (the same cuts as a car lot's) and the racers and
    /// everybody else clear out a share of what is left (<see cref="PartShopRules.SaleClearOutShare"/>); the morning after
    /// its last day it shuts, and what nobody bought goes with it;</item>
    /// <item>about every two months, when no shop's sale is under way, a shop goes under or a new one opens, toward about
    /// 60% of the shops open; a new one is of a kind that has no shop open, when there is one waiting;</item>
    /// <item>the open shops whose delivery is due are restocked (<see cref="IPartShopService.RestockAsync"/>).</item>
    /// </list>
    /// A game whose market does not refresh (<see cref="GameRules.MarketRefreshEnabled"/>) keeps its shops open or shut
    /// as they are, and their shelves as first stocked; a sale under way still runs out. The paper and the diner hear of every change.
    /// </summary>
    public class PartShopLifeService(IPartShopService shops, Random? random = null) : IPartShopLifeService
    {
        private readonly IPartShopService _shops = shops;
        private readonly Random _random = random ?? new();
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("PartShops");

        private IPartShopCatalog Catalog => _shops.Catalog;

        public async Task ReviewDayAsync(GameState gameState, DateTime date)
        {
            PartShopRoster.Sync(gameState, Catalog);
            if (gameState.PartShops is not { Count: > 0 } shops) return;

            var talk = new List<string>();
            var news = new List<NewsArticle>();

            foreach (var shop in shops.Where(s => s.IsTrading)) FadeTrade(shop, date);
            RunSales(shops, date, talk, news);

            var refresh = gameState.Rules.MarketRefreshEnabled;
            if (refresh)
            {
                foreach (var shop in shops.Where(s => s.Status == DealerStatus.Open)) SellOff(shop, PartShopRules.TownBuysShare);
            }

            if (refresh && !shops.Any(s => s.Status == DealerStatus.ClosingDown) && DealerRules.ChangeToday(_random.NextDouble()))
            {
                Change(shops, date, talk, news);
            }

            // A game whose market stands still still gets its shops' first stock, and keeps it
            await _shops.RestockAsync(gameState, date, _random, firstOnly: !refresh);

            if (news.Count > 0) NewsWriter.Add(gameState, date, news);
            if (talk.Count > 0) OpponentLifeService.AddTalk(gameState, date, talk);
        }

        /// <summary>
        /// What is left of a shop's trade since the last morning, and the town's custom of the days between: the player's
        /// and the rivals' purchases count as they are made (<see cref="PartShopRules.TradePerSale"/>), and fade
        /// </summary>
        private static void FadeTrade(PartShopLocation shop, DateTime date)
        {
            if (shop.TradeCountedTo is { } counted && counted.Date >= date.Date) return;

            var days = shop.TradeCountedTo is { } from ? (date.Date - from.Date).TotalDays : 0;
            shop.Trade = Unit.NonNegative(shop.Trade) * DealerRules.TradeFade(days) + PartShopRules.TownTradePerDay * days;
            shop.TradeCountedTo = date.Date;
        }

        /// <summary>
        /// Customers the player never meets buy each part on the shelves and in the yard with the odds of
        /// <paramref name="share"/>: the town a little every day (<see cref="PartShopRules.TownBuysShare"/>), so scarce
        /// parts sell out, and the bargain hunters a good deal more in a closing-down sale (<see cref="PartShopRules.SaleClearOutShare"/>)
        /// </summary>
        private void SellOff(PartShopLocation shop, double share)
        {
            foreach (var shelf in shop.Shelves)
            {
                var sold = 0;
                for (var i = 0; i < shelf.Count; i++)
                {
                    if (_random.NextDouble() < share) sold++;
                }

                shelf.Count -= sold;
            }

            shop.Shelves.RemoveAll(s => s.Count <= 0);
            shop.Yard.RemoveAll(_ => _random.NextDouble() < share);
        }

        // ----- closing-down sales -----

        private void RunSales(List<PartShopLocation> shops, DateTime date, List<string> talk, List<NewsArticle> news)
        {
            foreach (var shop in shops.Where(s => s.Status == DealerStatus.ClosingDown))
            {
                var last = (shop.ClosesOn ??= date.Date).Date;
                if (date.Date > last)
                {
                    Close(shop, date, talk, news);
                    continue;
                }

                SellOff(shop, PartShopRules.SaleClearOutShare);

                // The day the prices drop again is a day the street hears about. A shop's sale prices are worked out as
                // they are asked for (DealerRules.DiscountOn), so they always drop on this day: no car's price to check,
                // as the car lots do.
                var daysLeft = DealerRules.DaysLeft(shop, date);
                if (daysLeft == DealerRules.FinalDays - 1)
                {
                    talk.Add(Say("street.partshop_last_days", shop, DealerRules.DiscountFor(daysLeft), last));
                }
            }
        }

        private void StartSale(PartShopLocation shop, DateTime date, List<string> talk, List<NewsArticle> news)
        {
            var last = DealerChurn.StartSale(shop, date);

            var values = Values(shop, DealerRules.SaleDiscount, last);
            news.Add(new NewsArticle
            {
                Date = date,
                Weight = 20,
                Headline = GameText.Installed.Pick("news.partshop_closing.headline", _random, values),
                Body = GameText.Installed.Pick("news.partshop_closing.body", _random, values)
            });
            talk.Add(GameText.Installed.Pick("street.partshop_closing", _random, values));

            _logger.Information("{Shop} is going out of business: closing-down sale until {Last:d} (trade {Trade:0.0})", shop.Name, last, shop.Trade);
        }

        /// <summary>The doors shut: what nobody bought goes with the stock, and the owners with their regulars</summary>
        private void Close(PartShopLocation shop, DateTime date, List<string> talk, List<NewsArticle> news)
        {
            var left = shop.Shelves.Sum(s => s.Count) + shop.Yard.Count;
            DealerChurn.Close(shop, date);
            shop.Shelves = [];
            shop.Yard = [];
            shop.RestockedOn = null;
            shop.PlayerSpent = 0;

            var values = Values(shop, 0, date);
            news.Add(new NewsArticle
            {
                Date = date,
                Weight = 8,
                Headline = GameText.Installed.Pick("news.partshop_closed.headline", _random, values),
                Body = GameText.Installed.Pick("news.partshop_closed.body", _random, values)
            });
            talk.Add(GameText.Installed.Pick("street.partshop_closed", _random, values));

            _logger.Information("{Shop} has closed; {Count} parts went with it", shop.Name, left);
        }

        // ----- a shop closes or opens -----

        private void Change(List<PartShopLocation> shops, DateTime date, List<string> talk, List<NewsArticle> news)
        {
            var (change, mayClose, mayOpen) = DealerChurn.Decide(shops, date, _random);
            switch (change)
            {
                case DealerRules.Change.Close:
                    // By trade alone: a shop's shelves turn over, so nothing on them stands unsold the way a car on a lot does
                    StartSale(DealerChurn.PickWeighted(mayClose, s => DealerRules.CloseWeight(s.Trade, 0), _random), date, talk, news);
                    break;
                case DealerRules.Change.Open:
                    Open(PickToOpen(mayOpen, shops.Where(s => s.Status == DealerStatus.Open).ToList()), date, talk, news);
                    break;
                default:
                    _logger.Information("A part shop would have closed or opened today, but none can: {Open} of {Total} open",
                        shops.Count(s => s.Status == DealerStatus.Open), shops.Count);
                    break;
            }
        }

        /// <summary>A kind of shop the town has none of open comes first: somebody sees the gap</summary>
        private PartShopLocation PickToOpen(List<PartShopLocation> candidates, List<PartShopLocation> open)
        {
            var openKinds = open.Select(s => Catalog.Get(s.Id)?.KindOf).ToHashSet();
            var missing = candidates.Where(s => !openKinds.Contains(Catalog.Get(s.Id)?.KindOf)).ToList();
            var pool = missing.Count > 0 ? missing : candidates;
            return pool[_random.Next(pool.Count)];
        }

        /// <summary>New owners: nobody is a regular yet, and the shelves are stocked by the next delivery</summary>
        private void Open(PartShopLocation shop, DateTime date, List<string> talk, List<NewsArticle> news)
        {
            var reopened = DealerChurn.Open(shop, date);
            shop.RestockedOn = null;
            shop.PlayerSpent = 0;

            var values = Values(shop, 0, date);
            var story = reopened ? "partshop_reopened" : "partshop_opened";
            news.Add(new NewsArticle
            {
                Date = date,
                Weight = 15,
                Headline = GameText.Installed.Pick($"news.{story}.headline", _random, values),
                Body = GameText.Installed.Pick($"news.{story}.body", _random, values)
            });
            talk.Add(GameText.Installed.Pick($"street.{story}", _random, values));

            _logger.Information("{Shop} has {Opened} in {Region}", shop.Name, reopened ? "opened again" : "opened", shop.Region);
        }

        // ----- words -----

        private string Say(string key, PartShopLocation shop, decimal discount, DateTime day) =>
            GameText.Installed.Pick(key, _random, Values(shop, discount, day));

        /// <summary>{shop}, {region}, {blurb}, {discount} (a whole percent), {day} (a date: {day:dddd})</summary>
        private TextValues Values(PartShopLocation shop, decimal discount, DateTime day) => new()
        {
            ["shop"] = shop.Name,
            ["region"] = shop.Region,
            ["blurb"] = Catalog.Get(shop.Id)?.Blurb ?? string.Empty,
            ["discount"] = (int)Math.Round(discount * 100),
            ["day"] = day
        };
    }
}
