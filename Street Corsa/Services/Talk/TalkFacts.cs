using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Market;
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
        public const int News = 12;
        public const int Town = 14;
        public const int OtherCars = 20;
        public const int Market = 30;
        public const int Races = 40;
        public const int PlayersCar = 42;
        public const int OwnCar = 45;
        public const int Memories = 50;
        public const int Lot = 60;
        public const int Said = 70;
        public const int Build = 80;
        public const int Events = 85;
        public const int Earlier = 90;
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

        /// <summary>Today, and the player as the town sees them: their record, their name on the street, what they drive</summary>
        public static IEnumerable<string> Today(GameState game, Func<string, string> carName)
        {
            yield return GameText.Say("chat.fact.today", ("date", game.Date));
            yield return GameText.Say("chat.fact.player", ("player", game.Player.Name), ("wins", game.Player.Stats.Wins),
                ("losses", game.Player.Stats.Losses), ("reputation", game.Player.Stats.Reputation), ("standing", game.Player.Stats.GetReputationTier()));
            if (CarDriven(game) is { } car) yield return GameText.Say("chat.fact.player_car", ("player", game.Player.Name), ("car", carName(car.DefinitionId)));
        }

        /// <summary>The car the player drives: the one picked in the garage, else their first</summary>
        public static Car? CarDriven(GameState game) =>
            game.Player.Cars.FirstOrDefault(c => c.InstanceId == game.Player.SelectedCarInstanceId) ?? game.Player.Cars.FirstOrDefault();

        /// <summary>What is said around town (the diner's street talk), the newest <paramref name="most"/> first, in the AI's words when it wrote them</summary>
        public static IEnumerable<string> Town(GameState game, int most)
        {
            var said = (game.StreetTalk ?? []).OrderByDescending(t => t.Date).Take(Math.Max(0, most)).ToList();
            if (said.Count == 0) yield break;
            yield return GameText.Say("chat.fact.town");
            foreach (var item in said)
                yield return GameText.Say("chat.fact.town_line", ("when", When(item.Date, game.Date)), ("text", AiWriter.Said(item)));
        }

        /// <summary>What was in the paper lately, the newest <paramref name="most"/> first, in the AI's words when it wrote them</summary>
        public static IEnumerable<string> News(GameState game, int most)
        {
            var news = (game.News ?? []).OrderByDescending(n => n.Date).Take(Math.Max(0, most)).ToList();
            if (news.Count == 0) yield break;
            yield return GameText.Say("chat.fact.news");
            foreach (var article in news)
            {
                var ai = article.AiBody is { Length: > 0 } && article.AiLanguage == AiWriter.Language;
                yield return GameText.Say("chat.fact.news_line", ("when", When(article.Date, game.Date)),
                    ("headline", ai ? article.AiHeadline ?? article.Headline : article.Headline), ("text", ai ? article.AiBody : article.Body));
            }
        }

        /// <summary>
        /// Other cars for sale in town now (the paper's and the dealers'), the most like <paramref name="car"/> first (the
        /// same model, then the nearest in price), at most <paramref name="most"/>: what a seller or buyer knows of the market
        /// </summary>
        public static IEnumerable<string> Market(UsedCarListing car, IReadOnlyList<UsedCarListing> market, Func<string, string> carName, int most)
        {
            var others = market
                .Where(l => !l.IsSold && l.Id != car.Id && (car.CarInstanceId == null || l.CarInstanceId != car.CarInstanceId))
                .OrderBy(l => string.Equals(l.CarDefinitionId, car.CarDefinitionId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(l => Math.Abs(l.Price - car.Price))
                .Take(Math.Max(0, most))
                .ToList();
            if (others.Count == 0) yield break;
            yield return GameText.Say("chat.fact.market");
            foreach (var other in others)
                yield return GameText.Say("chat.fact.market_car", ("car", carName(other.CarDefinitionId)), ("price", new Helpers.Money(other.Price)),
                    ("condition", ConditionDisplay.Of(other.Condition).Label), ("mileage", Helpers.Measure.Distance(other.Mileage).ToString()),
                    ("where", GameText.Say(string.IsNullOrEmpty(other.DealerLocation) ? "chat.fact.market_paper" : "chat.fact.market_dealer")));
        }

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

        /// <summary>
        /// What is on a car, part by part, as the game knows it: a line a kind of part (the order of the mods'
        /// <c>part_groups.json</c>, the engine's first), the same part on every corner said once with how many, with its
        /// condition, and, for the engine's parts when the factory's are known, whether it came from the factory or not.
        /// Nothing without the catalog.
        /// </summary>
        /// <param name="factory">The ids of the parts of the car's factory engine; null when not known</param>
        /// <param name="mostPerKind">Different parts of a kind told one by one; more, and they are one line (but for those put on since)</param>
        public static IEnumerable<string> Build(IReadOnlyList<PartInstance> roots, PartsCatalog? catalog, IReadOnlySet<string>? factory, int mostPerKind = 3)
        {
            if (catalog == null || roots.Count == 0) yield break;

            var engine = roots.Where(p => p.ParentSlot == PartInstance.CarEngineSlot).SelectMany(p => p.SelfAndDescendants()).ToHashSet();
            var groups = PartGroups.Current.All.Select(g => g.Id).ToList();
            var parts = roots
                .SelectMany(p => p.SelfAndDescendants())
                .Select(p => (Part: p, Definition: catalog.Get(p.DefinitionId)))
                .Where(p => p.Definition != null)
                .GroupBy(p => p.Definition!.Id, StringComparer.OrdinalIgnoreCase)
                .Select(same => (Group: PartKinds.GroupOf(same.First().Definition!), same.First().Definition, Parts: same.Select(p => p.Part).ToList()))
                .OrderBy(p => groups.IndexOf(p.Group) is var at and >= 0 ? at : int.MaxValue)
                .ToList();
            if (parts.Count == 0) yield break;

            // From the factory (true), put on since (false), or not known (null: the factory unknown, or not the engine's)
            bool? Stock(PartDefinition definition, List<PartInstance> same) =>
                factory == null || !same.Any(engine.Contains) ? null : factory.Contains(definition.Id);

            yield return GameText.Say("chat.fact.build");
            foreach (var kind in parts.GroupBy(p => p.Group))
            {
                var name = PartKinds.GroupName(kind.Key);
                // A kind of many parts (the engine's small accessories) is one line, but for any put on since
                var many = kind.Count() > Math.Max(1, mostPerKind);
                var rest = new List<(PartDefinition Definition, List<PartInstance> Parts, bool? Stock)>();
                foreach (var (_, definition, same) in kind)
                {
                    var stock = Stock(definition!, same);
                    if (many && stock != false)
                    {
                        rest.Add((definition!, same, stock));
                        continue;
                    }

                    var origin = stock switch { true => GameText.Say("chat.fact.build_stock"), false => GameText.Say("chat.fact.build_aftermarket"), _ => string.Empty };
                    yield return GameText.Say("chat.fact.build_part", ("group", name), ("part", AdBlurbs.NameOf(definition!)),
                        ("count", same.Count > 1 ? GameText.Say("chat.fact.build_count", ("count", same.Count)) : string.Empty),
                        ("condition", ConditionDisplay.Of(same.Min(p => p.Wear)).Sentence), ("origin", origin));
                }

                if (rest.Count > 0)
                    yield return GameText.Say("chat.fact.build_many", ("group", name), ("count", rest.Sum(r => r.Parts.Count)),
                        ("condition", ConditionDisplay.Of(rest.SelectMany(r => r.Parts).Min(p => p.Wear)).Sentence),
                        ("origin", rest.All(r => r.Stock == true) ? GameText.Say("chat.fact.build_stock_all") : string.Empty));
            }
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
                    ("price", new Helpers.Money(car.Listing.Price)), ("condition", car.ConditionLabel),
                    ("engine", car.EngineDisplay.Length > 0 ? car.EngineDisplay : GameText.Say("chat.fact.lot_engine_unknown")),
                    ("mileage", car.MileageDisplay));
        }

        /// <summary>Who a rival is: name, record, car, the King, a grudge, broke, their story</summary>
        public static IEnumerable<string> Rival(Opponent rival, Func<string, string> carName)
        {
            yield return GameText.Say("chat.fact.rival", ("name", rival.Name), ("nickname", rival.Nickname), ("age", rival.Age),
                ("wins", rival.Stats.Wins), ("losses", rival.Stats.Losses), ("reputation", rival.Stats.Reputation),
                ("standing", rival.Stats.GetReputationTier()));
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

        /// <summary>What happened between the person and the player, the newest first; nothing when nothing did</summary>
        public static IEnumerable<string> Events(IReadOnlyList<PersonEvent> events, DateTime today, Func<string, string> carName)
        {
            if (events.Count == 0) yield break;

            yield return GameText.Say("chat.fact.events");
            foreach (var happened in events.Reverse())
                yield return GameText.Say($"chat.fact.event.{happened.Kind}", ("when", When(happened.Date, today)), ("what", happened.CarId is { } car ? carName(car) : happened.What),
                    ("amount", new Helpers.Money(happened.Amount ?? 0)));
        }

        /// <summary>How the person feels about the player (-2..+2), in words; nothing when they feel nothing either way</summary>
        public static IEnumerable<string> Feeling(int feeling)
        {
            if (feeling == 0) yield break;
            yield return GameText.Say(feeling switch
            {
                <= -2 => "chat.fact.feeling.sore",
                -1 => "chat.fact.feeling.cool",
                1 => "chat.fact.feeling.warm",
                _ => "chat.fact.feeling.friendly"
            });
        }

        /// <summary>What was said earlier in this talk, summed up, when the oldest turns are no longer sent</summary>
        public static IEnumerable<string> Earlier(string? summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) yield break;
            yield return summary.Trim();
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
