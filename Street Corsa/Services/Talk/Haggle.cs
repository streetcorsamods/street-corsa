using System.Globalization;
using System.Text.RegularExpressions;
using StreetCorsa.Models.GameState;
using StreetCorsa.Rules;
using StreetCorsa.Services.Ai;

namespace StreetCorsa.Services.Talk
{
    /// <summary>What a seller does with an offer</summary>
    public enum HaggleAnswer
    {
        /// <summary>Taken: the price is the offer (or what was asked, when the offer was more)</summary>
        Accept,

        /// <summary>Not that low, but lower: the price comes down to the counter</summary>
        Counter,

        /// <summary>The seller will not come down any more</summary>
        Firm,

        /// <summary>So low it is not worth an answer</summary>
        Insulted
    }

    /// <summary>An offer and what came of it: <paramref name="Price"/> is the price now (the ad's)</summary>
    public sealed record HaggleOutcome(HaggleAnswer Answer, decimal Offer, decimal Price);

    /// <summary>
    /// Haggling, decided by the game and only said by the AI: an offer found in what the player typed, the lowest the
    /// seller would take (from the price first asked, so asking again and again never brings it lower), and the
    /// answer: take it, meet halfway, stand firm, or take offence.
    /// </summary>
    public static partial class Haggle
    {
        /// <summary>
        /// The price the player names in <paramref name="text"/>, when it reads as an offer: a sum with a currency sign
        /// or "k"/"grand" on it, or any number in a line with one of <paramref name="offerWords"/> ("take", "give",
        /// "offer"), and within reason of <paramref name="asking"/> (<see cref="TalkRules.OfferLowest"/> to
        /// <see cref="TalkRules.OfferHighest"/> of it); the last such. A year ("a '57?") or a mileage is no offer.
        /// </summary>
        public static decimal? OfferIn(string text, decimal asking, IReadOnlyList<string> offerWords, TalkRules? rules = null)
        {
            rules ??= RuleBook.Current.Ai.Talk;
            if (string.IsNullOrWhiteSpace(text) || asking <= 0) return null;

            var worded = offerWords.Any(word => word.Length > 0 && Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase));
            decimal? offer = null;
            foreach (Match match in Amount().Matches(text))
            {
                if (Value(match) is not { } value) continue;
                var marked = match.Groups["cur"].Success || match.Groups["k"].Success;
                if (!marked && !worded) continue;
                if (value < asking * (decimal)rules.OfferLowest || value > asking * (decimal)rules.OfferHighest) continue;
                offer = value;
            }

            return offer;
        }

        private static decimal? Value(Match match)
        {
            var digits = match.Groups["num"].Value;
            var thousands = match.Groups["k"].Success;

            // "1,200" and "1.200" are twelve hundred; "1.2k" one and a fifth thousand
            decimal value;
            if (ThousandsGrouped().IsMatch(digits))
                value = decimal.Parse(digits.Replace(",", string.Empty).Replace(".", string.Empty), CultureInfo.InvariantCulture);
            else if (!decimal.TryParse(digits.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                return null;

            if (thousands) value *= 1000m;
            return value > 0 ? value : null;
        }

        /// <summary>
        /// The least the seller takes: what was first asked, less the most their kind comes down (<see cref="TalkRules"/>)
        /// times their temperament's give, more for a broke rival, more again for something long on sale
        /// </summary>
        public static decimal Floor(decimal asked, PersonKind kind, Temperament? temperament, int daysOnSale, bool broke, TalkRules rules)
        {
            var most = kind switch
            {
                PersonKind.Salesman => rules.DiscountSalesman,
                PersonKind.Rival => rules.DiscountRival,
                _ => rules.DiscountPrivate
            };
            var discount = most * (temperament?.Give ?? 1);
            if (broke) discount *= rules.BrokeRivalFactor;
            if (daysOnSale >= rules.LongOnSaleDays) discount += rules.LongOnSaleExtra;
            return asked * (1m - (decimal)Math.Clamp(discount, 0, Math.Clamp(rules.MostDiscount, 0, 1)));
        }

        /// <summary>
        /// The seller's answer to <paramref name="offer"/> with the price at <paramref name="current"/> and the least they
        /// take <paramref name="floor"/>, after <paramref name="counters"/> answers already; <paramref name="round"/> puts a
        /// price on the seller's round figures (never under the floor)
        /// </summary>
        public static HaggleOutcome Decide(decimal offer, decimal current, decimal floor, int counters, TalkRules rules, Func<decimal, decimal> round)
        {
            if (offer >= current) return new HaggleOutcome(HaggleAnswer.Accept, offer, current);
            if (offer >= floor) return new HaggleOutcome(HaggleAnswer.Accept, offer, offer);
            if (offer < floor * (1m - (decimal)Math.Clamp(rules.InsultMargin, 0, 1))) return new HaggleOutcome(HaggleAnswer.Insulted, offer, current);
            if (counters >= rules.MaxCounters) return new HaggleOutcome(HaggleAnswer.Firm, offer, current);

            var counter = Math.Max(round((offer + current) / 2m), floor);
            if (counter < floor) counter = floor;
            return counter >= current
                ? new HaggleOutcome(HaggleAnswer.Firm, offer, current)
                : new HaggleOutcome(HaggleAnswer.Counter, offer, counter);
        }

        /// <summary>A price up to the next <paramref name="step"/> (25: $1,437 is $1,450)</summary>
        public static decimal RoundUp(decimal price, int step) =>
            step <= 1 ? Math.Ceiling(price) : Math.Ceiling(price / step) * step;

        [GeneratedRegex(@"(?<cur>[$€£¥])?\s?(?<num>\d{1,3}(?:[,.]\d{3})+|\d+(?:[.,]\d{1,2})?)\s?(?<k>k\b|grand\b|thousand\b)?", RegexOptions.IgnoreCase)]
        private static partial Regex Amount();

        [GeneratedRegex(@"^\d{1,3}(?:[,.]\d{3})+$")]
        private static partial Regex ThousandsGrouped();
    }
}
