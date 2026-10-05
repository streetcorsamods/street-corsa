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
        Insulted,

        /// <summary>The player takes the price as it stands ("you got a deal"): shaken on, nothing changes</summary>
        Shake,

        /// <summary>A buyer pushed too far: they want no more of it, and their offer is gone</summary>
        Walk
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
        /// <see cref="TalkRules.OfferHighest"/> of it); the last such. A number with one of <paramref name="unitWords"/>
        /// after it ("5k miles") is no offer, nor is a year ("the 1957") unless an offer word comes right before it
        /// ("give you 1950").
        /// </summary>
        public static decimal? OfferIn(string text, decimal asking, IReadOnlyList<string> offerWords, TalkRules? rules = null,
            IReadOnlyList<string>? unitWords = null)
        {
            rules ??= RuleBook.Current.Ai.Talk;
            if (string.IsNullOrWhiteSpace(text) || asking <= 0) return null;

            var words = offerWords.Where(word => word.Length > 0).ToList();
            var worded = words.Any(word => Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase));
            decimal? offer = null;
            foreach (Match match in Amount().Matches(text))
            {
                if (Value(match) is not { } value) continue;
                if (IsMeasure(text, match, unitWords)) continue;
                var marked = match.Groups["cur"].Success || match.Groups["k"].Success;
                if (!marked && !worded) continue;
                if (!marked && YearShaped().IsMatch(match.Groups["num"].Value) && !OfferWordBefore(text, match, words)) continue;
                if (value < asking * (decimal)rules.OfferLowest || value > asking * (decimal)rules.OfferHighest) continue;
                offer = value;
            }

            return offer;
        }

        /// <summary>A number with a unit after it ("5k miles", "283 cubes"): a measure, no sum of money</summary>
        private static bool IsMeasure(string text, Match match, IReadOnlyList<string>? unitWords)
        {
            if (unitWords is not { Count: > 0 }) return false;
            var after = text[(match.Index + match.Length)..];
            return unitWords.Any(word => word.Length > 0 && Regex.IsMatch(after, $@"^\s*{Regex.Escape(word)}\b", RegexOptions.IgnoreCase));
        }

        /// <summary>An offer word right before the number ("give you 1950", "how about 1950")</summary>
        private static bool OfferWordBefore(string text, Match match, IReadOnlyList<string> words)
        {
            var before = text[..match.Index];
            return words.Any(word => Regex.IsMatch(before, $@"\b{Regex.Escape(word)}(\s+(you|me))?\s*$", RegexOptions.IgnoreCase));
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
        /// The player agrees to the price as it stands: a line that is one of <paramref name="alone"/> ("deal", "sold") by
        /// itself, or that holds one of <paramref name="phrases"/> ("got a deal", "I'll take it"), in any case. A question
        /// ("Deal?") agrees to nothing, nor does a line with one of <paramref name="negations"/> in it ("I never agreed").
        /// </summary>
        public static bool AgreesIn(string text, IReadOnlyList<string> phrases, IReadOnlyList<string> alone, IReadOnlyList<string>? negations = null)
        {
            var trimmed = text.Trim().ToLowerInvariant();
            if (trimmed.EndsWith('?')) return false;
            var line = Regex.Replace(trimmed, @"[.!,]+$", string.Empty).Trim();
            if (line.Length == 0) return false;
            if (negations != null && negations.Any(word => word.Length > 0
                                                           && Regex.IsMatch(line, $@"(^|\W){Regex.Escape(word.ToLowerInvariant())}(\W|$)")))
                return false;
            if (alone.Any(word => line == word.ToLowerInvariant())) return true;
            return phrases.Any(phrase => phrase.Length > 0
                                         && Regex.IsMatch(line, $@"(^|\W){Regex.Escape(phrase.ToLowerInvariant())}(\W|$)"));
        }

        /// <summary>One of <paramref name="words"/> (or phrases) in <paramref name="text"/> as a whole word, in any case</summary>
        public static bool HasWord(string text, IReadOnlyList<string> words)
        {
            var line = text.ToLowerInvariant();
            return words.Any(word => word.Length > 0 && Regex.IsMatch(line, $@"(^|\W){Regex.Escape(word.ToLowerInvariant())}(\W|$)"));
        }

        /// <summary>
        /// The sums of money <paramref name="text"/> names: with a currency sign ("$1,250"), "k"/"grand"/"thousand" on
        /// them ("1.2 grand"), or "bucks"/"dollars" after them ("900 bucks"); a number with one of
        /// <paramref name="unitWords"/> after it ("5k miles"), or a bare year, is no sum
        /// </summary>
        public static IReadOnlyList<decimal> SumsIn(string text, IReadOnlyList<string>? unitWords = null)
        {
            var sums = new List<decimal>();
            foreach (Match match in Amount().Matches(text))
            {
                if (IsMeasure(text, match, unitWords)) continue;
                var marked = match.Groups["cur"].Success || match.Groups["k"].Success
                             || Regex.IsMatch(text[(match.Index + match.Length)..], @"^\s*(bucks|dollars)\b", RegexOptions.IgnoreCase);
                if (marked && Value(match) is { } value) sums.Add(value);
            }

            return sums;
        }

        /// <summary>
        /// The answer names a price under <paramref name="price"/> that nothing the person was told gives (the player's
        /// own offer, a cheaper car on the lot, what was first asked): the AI came down by itself. A sum under
        /// <paramref name="least"/> is small talk (what gas costs), no price of the thing
        /// </summary>
        public static bool NamesLowerPrice(string answer, decimal price, IEnumerable<decimal> known, IReadOnlyList<string>? unitWords = null, decimal least = 0)
        {
            var allowed = known.ToHashSet();
            return SumsIn(answer, unitWords).Any(sum => sum < price && sum >= least && !allowed.Contains(sum));
        }

        /// <summary>
        /// The answer names a sum, higher or lower, that nothing the person was told gives (a car's price made up); a sum
        /// under <paramref name="least"/> is small talk, no car's price
        /// </summary>
        public static bool NamesUnknownSum(string answer, IEnumerable<decimal> known, IReadOnlyList<string>? unitWords = null, decimal least = 0)
        {
            var allowed = known.ToHashSet();
            return SumsIn(answer, unitWords).Any(sum => sum >= least && !allowed.Contains(sum));
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
        /// The least anybody ever takes for a thing first offered at <paramref name="firstAsked"/>, whose seller comes
        /// down at most <paramref name="mostOff"/> (<see cref="DrawMostOff"/>), however often and whoever is asked
        /// </summary>
        public static decimal HardFloor(decimal firstAsked, double mostOff, TalkRules rules) =>
            firstAsked * (1m - (decimal)Math.Clamp(mostOff, 0, Math.Clamp(rules.MostDiscount, 0, 1)));

        /// <summary>
        /// How far a seller will come down on a thing, drawn once: nothing at all for <see cref="TalkRules.FirmShare"/> of
        /// them, else anything up to <see cref="TalkRules.MostDiscount"/>, so the bottom is never the same twice
        /// </summary>
        public static double DrawMostOff(TalkRules rules, Random random) =>
            random.NextDouble() < rules.FirmShare ? 0 : random.NextDouble() * Math.Clamp(rules.MostDiscount, 0, 1);

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

            // Halfway on the seller's round figures, never under the floor; when that rounds up to the price itself (a
            // step from the floor) there is still room: plain halfway, in whole dollars
            var halfway = (offer + current) / 2m;
            var counter = Math.Max(round(halfway), round(floor));
            if (counter >= current) counter = Math.Ceiling(Math.Max(halfway, floor));
            return counter >= current
                ? new HaggleOutcome(HaggleAnswer.Firm, offer, current)
                : new HaggleOutcome(HaggleAnswer.Counter, offer, counter);
        }

        // ----- the other way round: a buyer of the player's car -----

        /// <summary>
        /// How far a buyer will come up on the player's car, drawn once: nothing at all for <see cref="TalkRules.FirmShare"/>
        /// of them, else anything up to <see cref="TalkRules.MostRaise"/>
        /// </summary>
        public static double DrawMostRaise(TalkRules rules, Random random) =>
            random.NextDouble() < rules.FirmShare ? 0 : random.NextDouble() * Math.Clamp(rules.MostRaise, 0, 1);

        /// <summary>
        /// The most a buyer who first offered <paramref name="firstOffered"/> ever pays: up by their own limit
        /// <paramref name="mostOver"/> (<see cref="DrawMostRaise"/>) times their temperament's give, never by more than
        /// <see cref="TalkRules.MostRaise"/>, never over what the ad asks, nor over what they have (<paramref name="money"/>,
        /// a rival's; null for anybody else). Never under what they offered.
        /// </summary>
        public static decimal Ceiling(decimal firstOffered, double mostOver, Temperament? temperament, decimal asking, decimal? money, TalkRules rules)
        {
            var raise = Math.Clamp(mostOver * (temperament?.Give ?? 1), 0, Math.Clamp(rules.MostRaise, 0, 1));
            var ceiling = Math.Min(firstOffered * (1m + (decimal)raise), Math.Max(asking, firstOffered));
            if (money is { } has) ceiling = Math.Min(ceiling, has);
            return Math.Max(ceiling, firstOffered);
        }

        /// <summary>
        /// The buyer's answer to the player asking <paramref name="ask"/> for their car with the offer at
        /// <paramref name="current"/> and the most they pay <paramref name="ceiling"/>, after <paramref name="counters"/>
        /// answers already: no more than they offered asked and they keep to their offer; up to the ceiling and they pay
        /// it; far over it, or pushed past <see cref="TalkRules.MaxCounters"/>, they walk away; else they come up halfway,
        /// on round figures down (<paramref name="round"/>), never over the ceiling, or stand where they are.
        /// </summary>
        public static HaggleOutcome DecideAsk(decimal ask, decimal current, decimal ceiling, int counters, TalkRules rules, Func<decimal, decimal> round)
        {
            if (ask <= current) return new HaggleOutcome(HaggleAnswer.Accept, ask, current);
            if (ask <= ceiling) return new HaggleOutcome(HaggleAnswer.Accept, ask, ask);
            if (ask > ceiling * (1m + (decimal)Math.Clamp(rules.InsultMargin, 0, 1)) || counters >= rules.MaxCounters)
                return new HaggleOutcome(HaggleAnswer.Walk, ask, current);

            // Halfway on the buyer's round figures, never over the ceiling; when that rounds down to the offer itself (a
            // step from it) there is still room: plain halfway, in whole dollars
            var halfway = (ask + current) / 2m;
            var counter = Math.Min(round(halfway), ceiling);
            if (counter <= current) counter = Math.Floor(Math.Min(halfway, ceiling));
            return counter <= current
                ? new HaggleOutcome(HaggleAnswer.Firm, ask, current)
                : new HaggleOutcome(HaggleAnswer.Counter, ask, counter);
        }

        /// <summary>
        /// The answer names an offer over <paramref name="price"/> that nothing the buyer was told gives (what the ad asks,
        /// the player's own figure): the AI went up by itself
        /// </summary>
        public static bool NamesHigherPrice(string answer, decimal price, IEnumerable<decimal> known, IReadOnlyList<string>? unitWords = null)
        {
            var allowed = known.ToHashSet();
            return SumsIn(answer, unitWords).Any(sum => sum > price && !allowed.Contains(sum));
        }

        /// <summary>A price down to the last <paramref name="step"/> (25: $1,437 is $1,425)</summary>
        public static decimal RoundDown(decimal price, int step) =>
            step <= 1 ? Math.Floor(price) : Math.Floor(price / step) * step;

        /// <summary>A price up to the next <paramref name="step"/> (25: $1,437 is $1,450)</summary>
        public static decimal RoundUp(decimal price, int step) =>
            step <= 1 ? Math.Ceiling(price) : Math.Ceiling(price / step) * step;

        [GeneratedRegex(@"(?<cur>[$€£¥])?\s?(?<num>\d{1,3}(?:[,.]\d{3})+|\d+(?:[.,]\d{1,2})?)\s?(?<k>k\b|grand\b|thousand\b)?", RegexOptions.IgnoreCase)]
        private static partial Regex Amount();

        [GeneratedRegex(@"^\d{1,3}(?:[,.]\d{3})+$")]
        private static partial Regex ThousandsGrouped();

        [GeneratedRegex(@"^(19|20)\d{2}$")]
        private static partial Regex YearShaped();
    }
}
