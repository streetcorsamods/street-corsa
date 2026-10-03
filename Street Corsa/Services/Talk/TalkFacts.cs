using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Talk
{
    /// <summary>
    /// What a person knows, written out for the AI with every turn (the text pools <c>talk.fact.*</c>): the game's facts,
    /// never the AI's. The thing for sale and its price now, the lot's cars, who a rival is, the races they had with the
    /// player and with each other, what they remember of past talks, what they said about the thing before, and the
    /// answer to an offer just made. One fact a line.
    /// </summary>
    /// <summary>
    /// One part of the fact sheet: its heading (<c>chat.section.{Key}</c>), its lines, the most needed first, and how much
    /// it is needed when the sheet is too long (<see cref="TalkFacts.Sheet"/>): the lowest goes first, line by line from
    /// the end; its first <paramref name="HeadLines"/> lines (a line that heads a list) only go with the whole section
    /// </summary>
    public sealed record FactSection(string Key, IReadOnlyList<string> Lines, int Priority, int HeadLines = 0)
    {
        /// <summary>Never left out: who the person is, the thing talked about, the deal</summary>
        public const int Always = int.MaxValue;

        // Kept before the others, the lowest first out
        public const int RivalRaces = 10;
        public const int OtherCars = 20;
        public const int Races = 40;
        public const int Memories = 50;
        public const int Lot = 60;
        public const int Said = 70;
    }

    public static class TalkFacts
    {
        /// <summary>"today", "yesterday", "5 days ago": how people say when</summary>
        public static string When(DateTime date, DateTime today)
        {
            var days = (int)(today.Date - date.Date).TotalDays;
            return days switch
            {
                <= 0 => GameText.Say("chat.when.today"),
                1 => GameText.Say("chat.when.yesterday"),
                _ => GameText.Say("chat.when.days", ("days", days))
            };
        }

        public static IEnumerable<string> Today(GameState game) =>
            [GameText.Say("chat.fact.today", ("date", game.Date)),
             GameText.Say("chat.fact.player", ("player", game.Player.Name), ("wins", game.Player.Stats.Wins),
                 ("losses", game.Player.Stats.Losses), ("reputation", game.Player.Stats.Reputation))];

        /// <summary>A car for sale: what it is, the engine, its shape and mileage, its past, the ad's words, how long on sale</summary>
        public static IEnumerable<string> Car(UsedCarListingViewModel car, decimal price, DateTime today, DateTime onSaleSince)
        {
            yield return GameText.Say("chat.fact.car", ("car", car.DisplayName), ("year", car.YearDisplay), ("price", new Helpers.Money(price)));
            foreach (var line in CarDetails(car, today, onSaleSince)) yield return line;
        }

        /// <summary>
        /// The player's own car in the paper, as the buyer who answered the ad knows it: what the ad asks, and the car as
        /// it is (the same truths a seller tells)
        /// </summary>
        public static IEnumerable<string> PlayersCar(UsedCarListingViewModel car, decimal asking, DateTime today, DateTime onSaleSince)
        {
            yield return GameText.Say("chat.fact.buyer_car", ("car", car.DisplayName), ("year", car.YearDisplay), ("price", new Helpers.Money(asking)));
            foreach (var line in CarDetails(car, today, onSaleSince)) yield return line;
        }

        private static IEnumerable<string> CarDetails(UsedCarListingViewModel car, DateTime today, DateTime onSaleSince)
        {
            var listing = car.Listing;
            if (car.EngineDisplay.Length > 0) yield return GameText.Say("chat.fact.car_engine", ("engine", car.EngineDisplay));
            if (listing.PowerHp is { } hp and > 0) yield return GameText.Say("chat.fact.car_power", ("power", (int)Math.Round(hp)));
            yield return GameText.Say(listing.IsModified ? "chat.fact.car_modified" : "chat.fact.car_original");
            yield return GameText.Say("chat.fact.car_condition", ("condition", car.ConditionDisplay), ("label", car.ConditionLabel),
                ("mileage", car.MileageDisplay));
            if (car.HistoryDisplay.Length > 0) yield return GameText.Say("chat.fact.car_history", ("history", car.HistoryDisplay));
            if (!string.IsNullOrWhiteSpace(listing.Description)) yield return GameText.Say("chat.fact.ad", ("ad", listing.Description));
            yield return GameText.Say("chat.fact.on_sale", ("days", Math.Max(0, (int)(today.Date - onSaleSince.Date).TotalDays)));
        }

        /// <summary>A part for sale: its name and shelf, its shape, the ad's words, how long on sale</summary>
        public static IEnumerable<string> Part(PartAd ad, string name, PartsCatalog? catalog, DateTime today)
        {
            var definition = catalog?.Get(ad.Part.DefinitionId);
            var group = definition == null ? string.Empty : PartKinds.GroupName(PartKinds.GroupOf(definition));
            yield return GameText.Say("chat.fact.part", ("part", name), ("group", group), ("condition", PartTrees.Describe(ad.Part)),
                ("price", new Helpers.Money(ad.AskingPrice)));
            if (!string.IsNullOrWhiteSpace(ad.Description)) yield return GameText.Say("chat.fact.ad", ("ad", ad.Description));
            yield return GameText.Say("chat.fact.on_sale", ("days", Math.Max(0, (int)(today.Date - ad.PostedDate.Date).TotalDays)));
        }

        /// <summary>A salesman's lot: the cars on it, the dearest first, at most <paramref name="most"/></summary>
        public static IEnumerable<string> Lot(IReadOnlyList<UsedCarListingViewModel> lot, int most)
        {
            if (lot.Count == 0)
            {
                yield return GameText.Say("chat.fact.lot_empty");
                yield break;
            }

            yield return GameText.Say("chat.fact.lot", ("count", lot.Count));
            foreach (var car in lot.OrderByDescending(c => c.Listing.Price).Take(most))
                yield return GameText.Say("chat.fact.lot_car", ("year", car.YearDisplay), ("car", car.DisplayName),
                    ("price", new Helpers.Money(car.Listing.Price)), ("condition", car.ConditionLabel));
        }

        /// <summary>Who a rival is: name, record, car, the King, a grudge, broke, their story</summary>
        public static IEnumerable<string> Rival(Opponent rival, Func<string, string> carName)
        {
            yield return GameText.Say("chat.fact.rival", ("name", rival.Name), ("nickname", rival.Nickname), ("age", rival.Age),
                ("wins", rival.Stats.Wins), ("losses", rival.Stats.Losses), ("reputation", rival.Stats.Reputation));
            if (rival.Cars.FirstOrDefault() is { } car) yield return GameText.Say("chat.fact.rival_car", ("car", carName(car.DefinitionId)));
            if (rival.IsKing) yield return GameText.Say("chat.fact.rival_king");
            if (rival.Grudge is { } grudge) yield return GameText.Say("chat.fact.rival_grudge", ("car", carName(grudge.CarDefinitionId)));
            if (rival.IsBroke) yield return GameText.Say("chat.fact.rival_broke");
            if (!string.IsNullOrWhiteSpace(rival.Biography)) yield return rival.Biography!;
        }

        /// <summary>
        /// The races the person had with the player, the newest <paramref name="most"/> (the newest first), by name (as the race record keeps
        /// it), with how many there were in all; that there were none, when none
        /// </summary>
        public static IEnumerable<string> RacesWithPlayer(string name, IReadOnlyList<ProcessedRaceSession> sessions, DateTime today, int most)
        {
            var races = sessions
                .Where(s => s.GameDate != null && string.Equals(s.OpponentName, name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.GameDate)
                .ToList();
            if (races.Count == 0)
            {
                yield return GameText.Say("chat.fact.no_races");
                yield break;
            }

            yield return GameText.Say("chat.fact.races", ("count", races.Count), ("won", races.Count(r => !r.PlayerWon)), ("lost", races.Count(r => r.PlayerWon)));
            foreach (var race in races.TakeLast(Math.Max(0, most)).Reverse())
            {
                yield return GameText.Say(race.PlayerWon ? "chat.fact.race_lost" : "chat.fact.race_won",
                    ("when", When(race.GameDate!.Value, today)), ("kind", KindOf(race.RaceType)),
                    ("pink", race.WasPinkSlip ? GameText.Say("chat.fact.pink") : string.Empty));
            }
        }

        /// <summary>A rival's races with other rivals, the newest <paramref name="most"/>, the newest first</summary>
        public static IEnumerable<string> RivalRaces(string name, IReadOnlyList<RivalRaceRecord> records, DateTime today, Func<string, string> carName, int most)
        {
            foreach (var race in records.Where(r => string.Equals(r.Winner, name, StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(r.Loser, name, StringComparison.OrdinalIgnoreCase)).TakeLast(Math.Max(0, most)).Reverse())
            {
                var won = string.Equals(race.Winner, name, StringComparison.OrdinalIgnoreCase);
                yield return GameText.Say(won ? "chat.fact.rival_race_won" : "chat.fact.rival_race_lost",
                    ("when", When(race.Date, today)), ("other", won ? race.Loser : race.Winner), ("kind", KindOf(race.RaceType)),
                    ("car", race.LoserCar == null ? string.Empty : carName(race.LoserCar)),
                    ("pink", race.PinkSlip ? GameText.Say("chat.fact.pink") : string.Empty),
                    ("wreck", race.Crashed ? GameText.Say("chat.fact.wreck") : string.Empty));
            }
        }

        /// <summary>What the person remembers of past talks, the newest first; that they remember none, when none</summary>
        public static IEnumerable<string> Memories(IReadOnlyList<PersonMemory> memories, DateTime today)
        {
            if (memories.Count == 0)
            {
                yield return GameText.Say("chat.fact.no_memories");
                yield break;
            }

            yield return GameText.Say("chat.fact.memories");
            foreach (var memory in memories.Reverse())
                yield return GameText.Say("chat.fact.memory", ("when", When(memory.Date, today)), ("text", memory.Text));
        }

        /// <summary>What the person said about the thing before: not to be said again</summary>
        public static IEnumerable<string> Said(PersonDeal? deal)
        {
            if (deal is not { Said.Count: > 0 }) yield break;
            yield return GameText.Say("chat.fact.said");
            foreach (var line in deal.Said) yield return GameText.Say("chat.fact.said_line", ("text", line));
        }

        /// <summary>The price as it stands, and the answer to the offer just made, if one was</summary>
        public static IEnumerable<string> Deal(decimal price, decimal asked, HaggleOutcome? outcome)
        {
            yield return GameText.Say(price < asked ? "chat.fact.price_down" : "chat.fact.price",
                ("price", new Helpers.Money(price)), ("asked", new Helpers.Money(asked)));
            if (outcome == null) yield break;

            var key = outcome.Answer switch
            {
                // More than was asked: the asking price is all they take
                HaggleAnswer.Accept when outcome.Offer > outcome.Price => "chat.deal.accept_asked",
                HaggleAnswer.Accept => "chat.deal.accept",
                HaggleAnswer.Counter => "chat.deal.counter",
                HaggleAnswer.Firm => "chat.deal.firm",
                HaggleAnswer.Shake => "chat.deal.shake",
                _ => "chat.deal.insulted"
            };
            yield return GameText.Say(key, ("offer", new Helpers.Money(outcome.Offer)), ("price", new Helpers.Money(outcome.Price)));
        }

        /// <summary>
        /// The buyer's offer as it stands (raised from what they first offered, or gone), and their answer to what the
        /// player just asked, if they asked
        /// </summary>
        public static IEnumerable<string> Offer(decimal offer, decimal first, bool gone, HaggleOutcome? outcome)
        {
            if (gone && outcome?.Answer != HaggleAnswer.Walk)
            {
                yield return GameText.Say("chat.fact.offer_gone");
                yield break;
            }

            yield return GameText.Say(offer > first ? "chat.fact.offer_up" : "chat.fact.offer",
                ("price", new Helpers.Money(offer)), ("asked", new Helpers.Money(first)));
            if (outcome == null) yield break;

            var key = outcome.Answer switch
            {
                // Less than they offered: they keep to their offer all the same
                HaggleAnswer.Accept when outcome.Offer < outcome.Price => "chat.deal.buyer.accept_offered",
                HaggleAnswer.Accept => "chat.deal.buyer.accept",
                HaggleAnswer.Counter => "chat.deal.buyer.counter",
                HaggleAnswer.Firm => "chat.deal.buyer.firm",
                HaggleAnswer.Shake => "chat.deal.buyer.shake",
                _ => "chat.deal.buyer.walk"
            };
            yield return GameText.Say(key, ("offer", new Helpers.Money(outcome.Offer)), ("price", new Helpers.Money(outcome.Price)));
        }

        /// <summary>"drag race" or "road race", in the player's words</summary>
        public static string KindOf(string? raceType) =>
            GameText.Say(raceType?.Contains("drag", StringComparison.OrdinalIgnoreCase) == true ? "chat.fact.kind_drag" : "chat.fact.kind_road");

        /// <summary>
        /// The sections as the sheet the AI is given, each under its heading (<c>chat.section</c>), in the order given:
        /// cut to <paramref name="maxTokens"/> by leaving out the lines of the least needed section first, from its end,
        /// and the whole section once only its head is left
        /// </summary>
        public static string Sheet(IEnumerable<FactSection> sections, int maxTokens)
        {
            var kept = sections
                .Select(s => (s.Priority, s.HeadLines,
                    Heading: GameText.Say("chat.section", ("heading", GameText.Say($"chat.section.{s.Key}"))),
                    Lines: s.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList()))
                .Where(s => s.Lines.Count > 0)
                .ToList();

            string Write() => string.Join("\n\n", kept.Select(s => s.Heading + "\n" + string.Join("\n", s.Lines)));

            var sheet = Write();
            while (AiTokens.Estimate(sheet) > maxTokens)
            {
                var lowest = kept.Select(s => s.Priority).DefaultIfEmpty(FactSection.Always).Min();
                if (lowest == FactSection.Always) break;
                var least = kept.FindIndex(s => s.Priority == lowest);
                // From its end, the most needed line first in it; the head only with the whole section
                if (kept[least].Lines.Count > Math.Max(1, kept[least].HeadLines)) kept[least].Lines.RemoveAt(kept[least].Lines.Count - 1);
                else kept.RemoveAt(least);
                sheet = Write();
            }

            return sheet;
        }

        public static TalkRules Rules => RuleBook.Current.Ai.Talk;
    }
}
