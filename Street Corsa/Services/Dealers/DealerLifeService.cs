using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.News;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Dealers
{
    /// <summary>The dealers' business, day by day: see <see cref="DealerLifeService"/></summary>
    public interface IDealerLifeService
    {
        void ReviewDay(GameState gameState, DateTime date);
    }

    /// <summary>
    /// Dealers come and go. Every morning:
    /// <list type="bullet">
    /// <item>the save's dealers are brought up to the mods' (<see cref="DealerRoster.Sync"/>);</item>
    /// <item>each lot's sales since yesterday count toward its <see cref="DealerLocation.Trade"/>, which fades as the weeks go;</item>
    /// <item>a lot in its closing-down sale marks its cars down (<see cref="DealerRules.DiscountFor"/>), and the
    /// morning after its last day shuts for good: what nobody bought goes with it;</item>
    /// <item>about every two months (<see cref="DealerRules.DaysPerChange"/>), when no sale is under way, a lot goes
    /// under or a new one opens, toward about 60% of the dealers open. The one that goes under is picked by how it is
    /// doing (<see cref="DealerRules.CloseWeight"/>); a new one is a dealer that has never opened, else premises empty
    /// for half a year.</item>
    /// </list>
    /// A game whose market does not refresh (<see cref="GameRules.MarketRefreshEnabled"/>) keeps its dealers as they
    /// are, but a sale under way still runs out. The paper and the diner hear of every change. The lots are stocked by
    /// the market's refresh, later the same morning; the rivals shop the sales (<see cref="OpponentLifeService"/>).
    /// </summary>
    public class DealerLifeService(IDealerCatalog catalog, Random? random = null) : IDealerLifeService
    {
        private readonly IDealerCatalog _catalog = catalog;
        private readonly Random _random = random ?? new();
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Dealers");

        public void ReviewDay(GameState gameState, DateTime date)
        {
            DealerRoster.Sync(gameState, _catalog);
            if (gameState.DealerLocations is not { Count: > 0 } dealers) return;

            var talk = new List<string>();
            var news = new List<NewsArticle>();

            CountTrade(gameState, dealers, date);
            RunSales(gameState, dealers, date, talk, news);

            if (gameState.Rules.MarketRefreshEnabled && !dealers.Any(d => d.Status == DealerStatus.ClosingDown)
                && DealerRules.ChangeToday(_random.NextDouble()))
            {
                Change(gameState, dealers, date, talk, news);
            }

            if (news.Count > 0) NewsWriter.Add(gameState, date, news);
            if (talk.Count > 0) OpponentLifeService.AddTalk(gameState, date, talk);
        }

        // ----- trade -----

        /// <summary>
        /// The cars sold off each lot since the last count, on top of what is left of the trade before. A count covers
        /// from the last one up to, not including, this morning: the rivals and the player buy after the review, at the
        /// very time it counted to, so those sales are tomorrow's.
        /// </summary>
        private static void CountTrade(GameState gameState, List<DealerLocation> dealers, DateTime date)
        {
            var listings = gameState.UsedCarMarket ?? [];
            foreach (var dealer in dealers.Where(d => d.IsTrading))
            {
                var from = dealer.TradeCountedTo;
                if (from is { } counted && counted >= date) continue;

                var sold = listings.Count(l => l.IsSold && l.DealerLocation == dealer.Id && l.SoldDate is { } when
                                               && (from == null || when >= from) && when < date);
                var days = from == null ? 0 : (date.Date - from.Value.Date).TotalDays;
                dealer.Trade = Unit.NonNegative(dealer.Trade) * DealerRules.TradeFade(days) + sold;
                dealer.TradeCountedTo = date;
            }
        }

        /// <summary>The share of a lot's cars that have stood there a while without a buyer; none for a lot sold out</summary>
        private static double StaleShare(GameState gameState, DealerLocation dealer, DateTime date)
        {
            var onTheLot = (gameState.UsedCarMarket ?? []).Where(l => !l.IsSold && l.DealerLocation == dealer.Id).ToList();
            if (onTheLot.Count == 0) return 0;
            return onTheLot.Count(l => (date.Date - l.ListedDate.Date).TotalDays >= DealerRules.StaleDays) / (double)onTheLot.Count;
        }

        // ----- closing-down sales -----

        private void RunSales(GameState gameState, List<DealerLocation> dealers, DateTime date, List<string> talk, List<NewsArticle> news)
        {
            foreach (var dealer in dealers.Where(d => d.Status == DealerStatus.ClosingDown))
            {
                // A save that lost the sale's last day (edited by hand) ends it today, and keeps that
                var last = (dealer.ClosesOn ??= date.Date).Date;
                if (date.Date > last)
                {
                    Close(gameState, dealer, date, talk, news);
                    continue;
                }

                var daysLeft = DealerRules.DaysLeft(dealer, date);
                var discount = DealerRules.DiscountFor(daysLeft);
                var deeper = MarkDown(gameState, dealer, discount);

                // The day the prices drop again is a day the street hears about
                if (deeper && daysLeft == DealerRules.FinalDays - 1)
                {
                    talk.Add(Say("street.dealer_last_days", dealer, discount, last));
                }
            }
        }

        /// <summary>Every car still on the lot at the sale's price; whether any came down today</summary>
        private static bool MarkDown(GameState gameState, DealerLocation dealer, decimal discount)
        {
            var cut = false;
            foreach (var listing in (gameState.UsedCarMarket ?? []).Where(l => !l.IsSold && l.DealerLocation == dealer.Id))
            {
                var was = listing.PriceBeforeSale ??= listing.Price;
                var price = DealerRules.SalePrice(was, discount);
                if (price >= listing.Price) continue;

                listing.Price = price;
                cut = true;
            }

            return cut;
        }

        private void StartSale(GameState gameState, DealerLocation dealer, DateTime date, List<string> talk, List<NewsArticle> news)
        {
            var last = date.Date.AddDays(DealerRules.SaleDays - 1);
            dealer.Status = DealerStatus.ClosingDown;
            dealer.ClosesOn = last;
            MarkDown(gameState, dealer, DealerRules.DiscountFor(DealerRules.SaleDays - 1));

            var values = Values(dealer, DealerRules.SaleDiscount, last);
            news.Add(new NewsArticle
            {
                Date = date,
                Weight = 25,
                Headline = GameText.Installed.Pick("news.dealer_closing.headline", _random, values),
                Body = GameText.Installed.Pick("news.dealer_closing.body", _random, values)
            });
            talk.Add(GameText.Installed.Pick("street.dealer_closing", _random, values));

            _logger.Information("{Dealer} is going out of business: closing-down sale until {Last:d} (trade {Trade:0.0})",
                dealer.Name, last, dealer.Trade);
        }

        /// <summary>The doors shut: the cars nobody bought go to the wholesalers with the rest of the stock</summary>
        private void Close(GameState gameState, DealerLocation dealer, DateTime date, List<string> talk, List<NewsArticle> news)
        {
            var left = gameState.UsedCarMarket?.RemoveAll(l => !l.IsSold && l.DealerLocation == dealer.Id) ?? 0;

            dealer.Status = DealerStatus.Closed;
            dealer.ClosedOn = date.Date;
            dealer.ClosesOn = null;
            dealer.Trade = 0;
            dealer.TradeCountedTo = null;

            var values = Values(dealer, 0, date);
            news.Add(new NewsArticle
            {
                Date = date,
                Weight = 10,
                Headline = GameText.Installed.Pick("news.dealer_closed.headline", _random, values),
                Body = GameText.Installed.Pick("news.dealer_closed.body", _random, values)
            });
            talk.Add(GameText.Installed.Pick("street.dealer_closed", _random, values));

            _logger.Information("{Dealer} has closed; {Count} unsold cars went with it", dealer.Name, left);
        }

        // ----- a lot closes or opens -----

        private void Change(GameState gameState, List<DealerLocation> dealers, DateTime date, List<string> talk, List<NewsArticle> news)
        {
            var open = dealers.Where(d => d.Status == DealerStatus.Open).ToList();
            var mayClose = open.Where(d => d.OpenedOn is not { } opened || (date.Date - opened.Date).TotalDays >= DealerRules.NewDealerGraceDays).ToList();
            var mayOpen = MayOpen(dealers, date);

            var change = DealerRules.Direction(open.Count, dealers.Count, mayOpen.Count > 0, mayClose.Count > 0, _random.NextDouble());
            switch (change)
            {
                case DealerRules.Change.Close:
                    StartSale(gameState, PickToClose(gameState, mayClose, date), date, talk, news);
                    break;
                case DealerRules.Change.Open:
                    Open(mayOpen, date, talk, news);
                    break;
                default:
                    _logger.Information("A dealer would have closed or opened today, but none can: {Open} of {Total} open", open.Count, dealers.Count);
                    break;
            }
        }

        /// <summary>Dealers that have never opened; once there are none, premises that have stood empty long enough</summary>
        private static List<DealerLocation> MayOpen(List<DealerLocation> dealers, DateTime date)
        {
            var waiting = dealers.Where(d => d.Status == DealerStatus.Waiting).ToList();
            if (waiting.Count > 0) return waiting;

            return dealers.Where(d => d.Status == DealerStatus.Closed
                                      && (d.ClosedOn is not { } closed || (date.Date - closed.Date).TotalDays >= DealerRules.ReopenAfterDays)).ToList();
        }

        private DealerLocation PickToClose(GameState gameState, List<DealerLocation> candidates, DateTime date)
        {
            var weights = candidates.Select(d => DealerRules.CloseWeight(d.Trade, StaleShare(gameState, d, date))).ToList();
            var roll = _random.NextDouble() * weights.Sum();
            for (var i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return candidates[i];
            }

            return candidates[^1];
        }

        private void Open(List<DealerLocation> candidates, DateTime date, List<string> talk, List<NewsArticle> news)
        {
            var dealer = candidates[_random.Next(candidates.Count)];
            var reopened = dealer.Status == DealerStatus.Closed;

            dealer.Status = DealerStatus.Open;
            dealer.OpenedOn = date.Date;
            dealer.ClosedOn = null;
            dealer.ClosesOn = null;
            dealer.Trade = DealerRules.OpeningTrade;
            dealer.TradeCountedTo = date;

            var values = Values(dealer, 0, date);
            var story = reopened ? "dealer_reopened" : "dealer_opened";
            news.Add(new NewsArticle
            {
                Date = date,
                Weight = 20,
                Headline = GameText.Installed.Pick($"news.{story}.headline", _random, values),
                Body = GameText.Installed.Pick($"news.{story}.body", _random, values)
            });
            talk.Add(GameText.Installed.Pick($"street.{story}", _random, values));

            _logger.Information("{Dealer} has {Opened} in {Region}", dealer.Name, reopened ? "opened again" : "opened", dealer.Region);
        }

        // ----- words -----

        private string Say(string key, DealerLocation dealer, decimal discount, DateTime day) =>
            GameText.Installed.Pick(key, _random, Values(dealer, discount, day));

        /// <summary>{dealer}, {region}, {blurb}, {discount} (a whole percent), {day} (a date: {day:dddd})</summary>
        private TextValues Values(DealerLocation dealer, decimal discount, DateTime day) => new()
        {
            ["dealer"] = dealer.Name,
            ["region"] = dealer.Region,
            ["blurb"] = _catalog.Get(dealer.Id)?.Blurb ?? string.Empty,
            ["discount"] = (int)Math.Round(discount * 100),
            ["day"] = day
        };
    }
}
