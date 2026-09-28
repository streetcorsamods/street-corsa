using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Market;

namespace StreetCorsa.Services.Dealers
{
    /// <summary>
    /// The numbers of the dealers' business (see <see cref="DealerLifeService"/>): how many lots are open, how often
    /// one closes or opens, which one goes under, and what its closing-down sale takes off. No state, so the rules can
    /// be checked on their own.
    /// </summary>
    public static class DealerRules
    {
        /// <summary>The share of all the dealers there are that is open at a time: 12 of the base game's 20</summary>
        public const double OpenShare = 0.6;

        /// <summary>Never fewer open than this share, whatever chance says</summary>
        public const double MinOpenShare = 0.4;

        /// <summary>A lot closes or opens about this often: five or six times a year, so most last the whole game</summary>
        public const double DaysPerChange = 60;

        /// <summary>The closing-down sale: announced in the paper, and the doors shut the morning after its last day</summary>
        public const int SaleDays = 7;

        /// <summary>What the sale takes off every car, and off it again in the last <see cref="FinalDays"/> days</summary>
        public const decimal SaleDiscount = 0.20m;
        public const decimal FinalDiscount = 0.35m;
        public const int FinalDays = 3;

        /// <summary>A lot that opened during the game gets this long to find its feet before it can go under</summary>
        public const int NewDealerGraceDays = 120;

        /// <summary>Premises stand empty at least this long before somebody opens there again (under the old name)</summary>
        public const int ReopenAfterDays = 180;

        /// <summary>How fast a dealer's sales are forgotten: after this many days a sale counts half</summary>
        public const double TradeHalfLifeDays = 30;

        /// <summary>A car on the lot this long without a buyer is one that is not selling</summary>
        public const int StaleDays = 10;

        /// <summary>What a new lot starts with in <see cref="DealerLocation.Trade"/>: the custom of its first weeks</summary>
        public const double OpeningTrade = 1;

        public static int TargetOpen(int total) => total <= 0 ? 0 : Math.Max(1, (int)Math.Round(total * OpenShare));

        public static int MinOpen(int total) => total <= 0 ? 0 : Math.Max(1, (int)Math.Round(total * MinOpenShare));

        /// <summary>What is left of a dealer's trade after <paramref name="days"/> days</summary>
        public static double TradeFade(double days) => days <= 0 ? 1 : Math.Pow(0.5, days / TradeHalfLifeDays);

        /// <summary>Whether today is one on which a lot closes or opens: <paramref name="roll"/> is 0 to 1</summary>
        public static bool ChangeToday(double roll) => roll < 1 / DaysPerChange;

        public enum Change { None, Close, Open }

        /// <summary>
        /// Which way the market changes, with <paramref name="open"/> lots of <paramref name="total"/> open: toward the
        /// target, and a coin (<paramref name="roll"/>, 0 to 1) when it is there. Never away from the target, never under
        /// the minimum, and never an opening when no premises are free (<paramref name="canOpen"/>) or a closing when no
        /// lot may close (<paramref name="canClose"/>).
        /// </summary>
        public static Change Direction(int open, int total, bool canOpen, bool canClose, double roll)
        {
            var target = TargetOpen(total);
            var mayClose = canClose && open > MinOpen(total);

            if (open > target) return mayClose ? Change.Close : Change.None;
            if (open < target) return canOpen ? Change.Open : Change.None;

            // At the target either way is one step off it: the coin's, else the other
            if (roll < 0.5) return mayClose ? Change.Close : canOpen ? Change.Open : Change.None;
            return canOpen ? Change.Open : mayClose ? Change.Close : Change.None;
        }

        /// <summary>
        /// How likely this lot is, against the others, to be the one that goes under: one selling nothing with cars
        /// standing unsold (<paramref name="staleShare"/> of the lot, 0 to 1) far more than one that sells, but never
        /// certainly, and never not at all
        /// </summary>
        public static double CloseWeight(double trade, double staleShare)
        {
            return (1 + 2 * Unit.Clamp01(staleShare, 0)) / (0.5 + Unit.NonNegative(trade));
        }

        /// <summary>The sale's cut on a day <paramref name="daysLeft"/> before the doors shut (0 on the last day)</summary>
        public static decimal DiscountFor(int daysLeft) => daysLeft < FinalDays ? FinalDiscount : SaleDiscount;

        /// <summary>
        /// The days before a closing-down lot shuts, as of <paramref name="today"/>: 0 on its last day, and for a lot
        /// that is not closing down
        /// </summary>
        public static int DaysLeft(DealerLocation dealer, DateTime today) =>
            dealer.Status == DealerStatus.ClosingDown && dealer.ClosesOn is { } last ? Math.Max(0, (last.Date - today.Date).Days) : 0;

        /// <summary>What a lot's closing-down sale takes off on <paramref name="today"/>; nothing off a lot that is not closing down</summary>
        public static decimal DiscountOn(DealerLocation dealer, DateTime today) =>
            dealer.Status == DealerStatus.ClosingDown ? DiscountFor(DaysLeft(dealer, today)) : 0m;

        /// <summary>A car marked at <paramref name="price"/> in the sale</summary>
        public static decimal SalePrice(decimal price, decimal discount) =>
            Math.Max(100m, CarValuation.RoundToHundred(price * (1 - discount)));
    }
}
