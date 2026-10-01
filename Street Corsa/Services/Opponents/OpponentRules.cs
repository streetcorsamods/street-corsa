using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Opponents
{
    /// <summary>
    /// The numbers of a rival's life, from the GameMaker version's daily review (scr_review_racers): how many racers
    /// are out on the street, which car a racer drives, which one he buys, when he is broke. No state, so the rules
    /// can be checked on their own; <see cref="OpponentLifeService"/> applies them.
    /// </summary>
    public static class OpponentRules
    {
        /// <summary>Racers on the street when a career starts</summary>
        public static int BaseActiveRacers => Rules.RuleBook.Current.Rivals.BaseActiveRacers;

        /// <summary>More racers turn up every week</summary>
        public static int ActiveRacersPerWeek => Rules.RuleBook.Current.Rivals.ActiveRacersPerWeek;

        /// <summary>What counts as broke when there is no car for sale to measure against</summary>
        public static decimal BankruptFloor => Rules.RuleBook.Current.Rivals.BankruptFloor;

        /// <summary>
        /// What a broke racer scrapes together (a loan, a pawned watch, a second job), as a share of the cheapest car
        /// for sale: a few of those and they can buy one
        /// </summary>
        public static double MinCashShare => Rules.RuleBook.Current.Rivals.MinCashShare;
        public static double MaxCashShare => Rules.RuleBook.Current.Rivals.MaxCashShare;

        /// <summary>The same when nothing is for sale (the GameMaker version's $50–500)</summary>
        public static int MinCashInjection => Rules.RuleBook.Current.Rivals.MinCashInjection;
        public static int MaxCashInjection => Rules.RuleBook.Current.Rivals.MaxCashInjection;

        /// <summary>A racer keeps a car to race and one spare; the rest go to a dealer</summary>
        public static int MaxCars => Rules.RuleBook.Current.Rivals.MaxCars;

        /// <summary>
        /// Money a racer keeps back from tuning, for the bets and the repairs: this share of what their car is worth,
        /// so it follows the prices
        /// </summary>
        public static decimal TuningReserveShare => Rules.RuleBook.Current.Rivals.TuningReserveShare;

        /// <summary>Share of the money over the reserve a racer spends on parts in a day</summary>
        public static decimal TuningShare => Rules.RuleBook.Current.Rivals.TuningShare;

        /// <summary>The chance a racer on the street sells up and leaves town on a given day: one in five hundred, so most stay well over a year</summary>
        public static double LeaveChance => Rules.RuleBook.Current.Rivals.LeaveChance;

        /// <summary>Going broke this many times, they give up instead of scraping money together once more</summary>
        public static int MaxTimesBroke => Rules.RuleBook.Current.Rivals.MaxTimesBroke;

        /// <summary>A racer sitting out this long starts thinking of giving up</summary>
        public static int LaidUpDays => Rules.RuleBook.Current.Rivals.LaidUpDays;

        /// <summary>The chance, each day after <see cref="LaidUpDays"/>, that a racer sitting out gives up</summary>
        public static double LaidUpLeaveChance => Rules.RuleBook.Current.Rivals.LaidUpLeaveChance;

        /// <summary>
        /// When nobody new is left to come out, a racer who left this long ago can come back to town, with fresh money
        /// </summary>
        public static int ComeBackAfterDays => Rules.RuleBook.Current.Rivals.ComeBackAfterDays;

        /// <summary>The chance a racer with money to spare goes looking for a better car on a given day</summary>
        public static double TradeUpChance => Rules.RuleBook.Current.Rivals.TradeUpChance;

        /// <summary>A better car is one with this much more power: less is not worth the trouble of a sale</summary>
        public static double TradeUpGain => Rules.RuleBook.Current.Rivals.TradeUpGain;

        /// <summary>
        /// The chance a racer with money goes round a dealer's closing-down sale on a given day: far more than they look
        /// for a better car (<see cref="TradeUpChance"/>), because a bargain does not wait
        /// </summary>
        public static double BargainChance => Rules.RuleBook.Current.Rivals.BargainChance;

        /// <summary>At most this many cars go off the sale lots to the rivals in a day: the player still gets a look</summary>
        public static int MaxBargainsPerDay => Rules.RuleBook.Current.Rivals.MaxBargainsPerDay;

        /// <summary>At a sale price a smaller step up is worth taking than <see cref="TradeUpGain"/></summary>
        public static double BargainTradeUpGain => Rules.RuleBook.Current.Rivals.BargainTradeUpGain;

        /// <summary>What a flipped car has to make in the paper over what it cost: less is not worth the bother</summary>
        public static decimal FlipMargin => Rules.RuleBook.Current.Rivals.FlipMargin;

        /// <summary>
        /// Whether a car the sale marks at <paramref name="price"/> is worth buying to put straight back in the paper:
        /// the least the racer would ask for it there (<paramref name="leastAsked"/>, see <see cref="AskingPrice"/>, from
        /// what the car is worth, not what the lot marked it at) beats the price by <see cref="FlipMargin"/>. The sale's
        /// first cut is not deep enough for that; its last days are.
        /// </summary>
        public static bool WorthFlipping(decimal price, decimal leastAsked) =>
            price > 0 && leastAsked > 0 && price * FlipMargin <= leastAsked;

        /// <summary>A car bought with a reserve left over, as for a trade-up: the same share of its price a tuner keeps back</summary>
        public static bool Affordable(decimal price, decimal money) => price > 0 && money - price >= price * TuningReserveShare;

        /// <summary>What a rival asks for a car in the paper, as a share of the lot price: under what a lot asks, over what a dealer pays</summary>
        public static double MinAskShare => Rules.RuleBook.Current.Rivals.MinAskShare;
        public static double MaxAskShare => Rules.RuleBook.Current.Rivals.MaxAskShare;

        /// <summary>After this many days an ad nobody answered comes down in price, once, by <see cref="AskReduction"/></summary>
        public static int AskReducedAfterDays => Rules.RuleBook.Current.Rivals.AskReducedAfterDays;
        public static decimal AskReduction => Rules.RuleBook.Current.Rivals.AskReduction;

        /// <summary>
        /// Whether a racer gives up the scene today: gone broke once too often, laid up for too long, or, rarely, just
        /// moving on. <paramref name="roll"/> is 0 to 1.
        /// </summary>
        public static bool Leaves(int timesBroke, int? daysSittingOut, double roll)
        {
            if (timesBroke >= MaxTimesBroke) return true;
            if (daysSittingOut >= LaidUpDays && roll < LaidUpLeaveChance) return true;
            return roll < LeaveChance;
        }

        /// <summary>
        /// Whether a racer driving a car of <paramref name="currentHp"/> would buy one of <paramref name="hp"/> for
        /// <paramref name="price"/>: clearly stronger, and paid for with a reserve left over (the same share of the new
        /// car's price a tuner keeps back of theirs)
        /// </summary>
        public static bool WorthTradingUp(double currentHp, double hp, decimal price, decimal money) =>
            price > 0 && Unit.NonNegative(hp) >= Math.Max(1, Unit.NonNegative(currentHp)) * TradeUpGain && money - price >= price * TuningReserveShare;

        /// <summary>
        /// What a rival asks in the paper for a car worth <paramref name="lotPrice"/> on a lot, <paramref name="roll"/>
        /// 0 to 1 picking where in the range; never under what a dealer would pay (<paramref name="dealerPays"/>)
        /// </summary>
        public static decimal AskingPrice(decimal lotPrice, decimal dealerPays, double roll)
        {
            roll = Unit.Clamp01(roll, ifNotFinite: 0);
            var share = MinAskShare + roll * (MaxAskShare - MinAskShare);
            return Math.Max(Math.Max(0m, dealerPays), CarValuation.RoundToHundred(Math.Max(0m, lotPrice) * (decimal)share));
        }

        /// <summary>
        /// Where a skill sits in the opponents' range, 0 at <see cref="Opponent.MinSkill"/> to 1 at
        /// <see cref="Opponent.MaxSkill"/>. Skill starts at 90 on purpose, so a formula written for 0-100 would put
        /// every racer near its top: the rules weigh skill through this instead.
        /// </summary>
        public static double SkillShare(double skill) =>
            Unit.Clamp01((skill - Opponent.MinSkill) / (double)(Opponent.MaxSkill - Opponent.MinSkill), ifNotFinite: 0);

        public static int WeeksElapsed(DateTime date) =>
            Math.Max(0, (date.Date - GameState.GetStartingDateTime().Date).Days / 7);

        /// <summary>How many racers should be out on the street by <paramref name="date"/>, of <paramref name="total"/> there are</summary>
        public static int MinActive(DateTime date, int total) =>
            Math.Min(total, BaseActiveRacers + ActiveRacersPerWeek * WeeksElapsed(date));

        /// <summary>
        /// How much a racer wants to drive this car of theirs: one that can race before any that can't, then power,
        /// its shape, and its worth as the tie-breaker. (The GameMaker version gave a running car 1000 points, less
        /// than 100 hp is worth: a racer would pick a stronger wreck over the spare that runs.)
        /// </summary>
        /// <param name="condition">0 to 1</param>
        public static double CarScore(double hp, double condition, bool canRace, decimal value) =>
            Unit.NonNegative(hp) * 10 + Unit.Clamp01(condition, ifNotFinite: 0) * 100 * 5 + (canRace ? RacingCarBonus : 0) + (double)value * 0.1;

        /// <summary>More than any power, shape and worth add up to</summary>
        private const double RacingCarBonus = 1_000_000;

        /// <summary>
        /// How much a racer with <paramref name="budget"/> wants to buy this car: power, shape, a car that runs, power
        /// for the money, and a car that leaves money over for repairs and parts (under 70% of the budget)
        /// </summary>
        public static double PurchaseScore(double hp, double condition, bool runs, decimal price, decimal budget)
        {
            var score = Unit.NonNegative(hp) * 2 + Unit.Clamp01(condition, ifNotFinite: 0) * 100 * 3 + (runs ? 500 : 0);
            if (price > 0) score += Unit.NonNegative(hp) / (double)price * 1000;

            if (budget > 0)
            {
                var left = 1 - (double)(price / budget);
                if (left > 0.3) score += left * 200;
            }

            return score;
        }

        /// <summary>Broke: no car that can race, and less money than the cheapest car for sale</summary>
        public static bool IsBankrupt(bool hasRacingCar, decimal money, decimal? cheapestCar) =>
            !hasRacingCar && money < (cheapestCar ?? BankruptFloor);

        /// <summary>What a racer with <paramref name="money"/> and a car worth <paramref name="carValue"/> spends on parts today at most</summary>
        public static decimal TuningBudget(decimal money, decimal carValue) =>
            Math.Max(0m, (money - Math.Max(0m, carValue) * TuningReserveShare) * TuningShare);

        /// <summary>What a broke racer scrapes together: a share of the cheapest car for sale, <paramref name="roll"/> 0 to 1 picking where in the range</summary>
        public static decimal CashInjection(decimal? cheapestCar, double roll)
        {
            roll = Unit.Clamp01(roll, ifNotFinite: 0);
            if (cheapestCar is not > 0) return Math.Round(MinCashInjection + (decimal)roll * (MaxCashInjection - MinCashInjection));

            var share = MinCashShare + roll * (MaxCashShare - MinCashShare);
            return Math.Max(1m, Math.Round(cheapestCar.Value * (decimal)share));
        }

        /// <summary>"his", "her", "their"</summary>
        public static string Possessive(Racer racer) => racer is Opponent { Gender: var gender }
            ? GameText.Say(gender switch { Gender.Male => "rivals.possessive.male", Gender.Female => "rivals.possessive.female", _ => "rivals.possessive.other" })
            : GameText.Say("rivals.possessive.other");
    }
}
