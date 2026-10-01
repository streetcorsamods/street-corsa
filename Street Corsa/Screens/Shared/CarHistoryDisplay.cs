using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Text;
using StreetCorsa.Helpers;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>A car's history (<see cref="CarHistory"/>) in words, the same on every screen</summary>
    public static class CarHistoryDisplay
    {
        /// <summary>
        /// "2 owners before you · won from Johnny on a pink slip · 5 wins in 8 races · best 13.52 @ 104 mph" for a car
        /// somebody has; "3 previous owners, the last Johnny · 5 wins in 8 races" for one on a lot
        /// </summary>
        public static string Summary(CarHistory? history, bool forSale)
        {
            if (history == null) return string.Empty;

            var parts = new List<string> { Owners(history, forSale) };
            if (!forSale && HowBought(history) is { } how) parts.Add(how);
            parts.Add(Record(history));
            parts.Add(BestQuarter(history));
            return string.Join(" · ", parts.Where(p => p.Length > 0));
        }

        /// <summary>"best 13.52 @ 104 mph", the car's best quarter mile; empty when it has never run one</summary>
        public static string BestQuarter(CarHistory history)
        {
            if (history.BestQuarterSeconds is not { } et) return string.Empty;
            var time = et.ToString("0.00", Locale.Culture);
            return history.BestQuarterMph is { } mph
                ? GameText.Say("market.history.best_mph", ("time", time), ("speed", Measure.SpeedFromMph(mph)))
                : GameText.Say("market.history.best", ("time", time));
        }

        /// <summary>"5 wins in 8 races, 2 cars won on pink slips"; "Never raced" when it hasn't</summary>
        public static string Record(CarHistory history)
        {
            if (history.Races <= 0) return GameText.Say("market.history.never_raced");

            var record = GameText.Say("market.history.record",
                ("wins", Count(history.Wins, "market.history.wins")), ("races", Count(history.Races, "market.history.races")));
            return history.PinkSlipsWon > 0
                ? GameText.Say("market.history.pink_slips", ("record", record), ("cars", Count(history.PinkSlipsWon, "market.history.cars")))
                : record;
        }

        private static string Owners(CarHistory history, bool forSale)
        {
            // On a lot everybody who had it is a previous owner; in somebody's garage, all but them
            var before = forSale ? history.OwnerCount : Math.Max(0, history.OwnerCount - 1);
            var last = forSale ? history.Owners.LastOrDefault()?.Name : null;

            if (before == 0) return GameText.Say(forSale ? "market.history.no_previous_owners" : "market.history.first_owner");
            if (forSale && last != null)
                return GameText.Say("market.history.previous_owners_last", ("owners", Count(before, "market.history.previous_owners")), ("last", last));
            return forSale
                ? Count(before, "market.history.previous_owners")
                : GameText.Say("market.history.owners_before_you", ("owners", Count(before, "market.history.owners")));
        }

        /// <summary>How the one who has the car now came by it, from whom: "won from Johnny on a pink slip"</summary>
        private static string? HowBought(CarHistory history)
        {
            if (history.Owners.Count == 0) return null;

            var now = history.Owners[^1];
            var from = history.Owners.Count >= 2 ? history.Owners[^2].Name : null;
            return now.How switch
            {
                CarAcquisition.PinkSlip => from != null
                    ? GameText.Say("market.history.won_from", ("from", from))
                    : GameText.Say("market.history.won"),
                CarAcquisition.PrivateSale => from != null
                    ? GameText.Say("market.history.bought_from", ("from", from))
                    : GameText.Say("market.history.bought_privately"),
                CarAcquisition.Dealer => GameText.Say("market.history.bought_off_lot"),
                _ => null
            };
        }

        /// <summary>A count in words: the pool <paramref name="key"/>.one for one, .other for any other number</summary>
        private static string Count(int n, string key) => GameText.Say(n == 1 ? key + ".one" : key + ".other", ("count", n));
    }
}
