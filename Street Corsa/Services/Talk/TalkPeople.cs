using StreetCorsa.Models.GameState;
using StreetCorsa.Rules;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Talk
{
    /// <summary>
    /// The people the player talks to (<see cref="GameState.People"/>): found by who they are to the game, made the first
    /// time from a seed of it (the same ad has the same seller, whatever the day), and forgetting in time. Rivals are
    /// their own <see cref="Opponent"/>; their person holds only what a racer lacks (the temperament, the memories).
    /// </summary>
    public static class TalkPeople
    {
        public static string RivalKey(Opponent rival) => $"rival:{rival.OpponentId}";

        public static string DealerKey(string dealerId) => $"dealer:{dealerId}";

        public static string CarKey(UsedCarListing listing) => $"car:{listing.Id}";

        public static string RivalCarKey(RivalCarAd ad) => $"rivalcar:{ad.AdId}";

        public static string PartKey(PartAd ad) => $"part:{ad.AdId}";

        /// <summary>The kind's word in the temperaments' <c>for</c></summary>
        public static string KindWord(PersonKind kind) => kind switch
        {
            PersonKind.Salesman => "salesman",
            PersonKind.Rival => "rival",
            _ => "seller"
        };

        /// <summary>A rival as a person to talk to: made with a temperament that fits their aggression the first time</summary>
        public static Person Rival(GameState game, Opponent rival, AiPrompts? prompts = null) =>
            Find(game, RivalKey(rival)) ?? Add(game, new Person
            {
                Key = RivalKey(rival),
                Kind = PersonKind.Rival,
                Name = rival.Name,
                Age = rival.Age,
                Gender = rival.Gender,
                Temperament = Draw(prompts ?? AiPrompts.Installed, PersonKind.Rival, rival.Aggression, Seeded(RivalKey(rival)))
            });

        /// <summary>
        /// A dealer's salesman, one per dealer: who the dealer's mod data says (<paramref name="given"/>; it is the truth, a
        /// salesman met before it gave one becomes him), else drawn from the salesmen's names
        /// </summary>
        public static Person Salesman(GameState game, string dealerId, DealerSalesman? given = null, AiPrompts? prompts = null, TalkRules? rules = null)
        {
            var key = DealerKey(dealerId);
            if (Find(game, key) is { } found)
            {
                if (given != null) As(found, given);
                return found;
            }

            rules ??= RuleBook.Current.Ai.Talk;
            var random = Seeded(key);
            var gender = random.NextDouble() < rules.WomenSalesmen ? Gender.Female : Gender.Male;
            var person = Add(game, new Person
            {
                Key = key,
                Kind = PersonKind.Salesman,
                Name = NameFor("names.salesmen", gender, random),
                Age = AgeBetween(rules.SalesmanMinAge, rules.SalesmanMaxAge, random),
                Gender = gender,
                Temperament = Draw(prompts ?? AiPrompts.Installed, PersonKind.Salesman, 50, random)
            });
            if (given != null) As(person, given);
            return person;
        }

        /// <summary>The salesman as the dealer's data gives him: what it says goes, what it leaves out stays</summary>
        private static void As(Person person, DealerSalesman given)
        {
            if (!string.IsNullOrWhiteSpace(given.Name)) person.Name = given.Name;
            if (given.Age > 0) person.Age = given.Age;
            person.Gender = given.Gender;
            if (!string.IsNullOrWhiteSpace(given.Temperament)) person.Temperament = given.Temperament;
        }

        /// <summary>
        /// A private seller (a car or a part in the paper) by the ad's key: <paramref name="name"/> when the ad gives one
        /// (a part's seller), else drawn
        /// </summary>
        public static Person Seller(GameState game, string key, string? name = null, AiPrompts? prompts = null, TalkRules? rules = null)
        {
            if (Find(game, key) is { } found) return found;

            rules ??= RuleBook.Current.Ai.Talk;
            var random = Seeded(key);
            var gender = random.NextDouble() < rules.WomenSellers ? Gender.Female : Gender.Male;
            return Add(game, new Person
            {
                Key = key,
                Kind = PersonKind.PrivateSeller,
                Name = string.IsNullOrWhiteSpace(name) ? NameFor("names.people", gender, random) : name,
                Age = AgeBetween(rules.SellerMinAge, rules.SellerMaxAge, random),
                Gender = gender,
                Temperament = Draw(prompts ?? AiPrompts.Installed, PersonKind.PrivateSeller, 50, random)
            });
        }

        private static int AgeBetween(int least, int most, Random random) => least + random.Next(Math.Max(1, most - least + 1));

        public static Person? Find(GameState game, string key) => game.People.FirstOrDefault(p => p.Key == key);

        private static Person Add(GameState game, Person person)
        {
            person.Met = game.Date;
            game.People.Add(person);
            return person;
        }

        /// <summary>A name of the pool "&lt;pool&gt;.men" or "&lt;pool&gt;.women"; the other when a mod has only one</summary>
        private static string NameFor(string pool, Gender gender, Random random)
        {
            var text = GameText.Installed;
            var name = text.Pick($"{pool}.{(gender == Gender.Female ? "women" : "men")}", random);
            if (name.Length == 0) name = text.Pick($"{pool}.{(gender == Gender.Female ? "men" : "women")}", random);
            return name.Length == 0 ? GameText.Say("chat.somebody") : name;
        }

        /// <summary>A temperament for the kind, drawn by weight among those that fit; empty when the mods have none</summary>
        public static string Draw(AiPrompts prompts, PersonKind kind, int aggression, Random random)
        {
            var word = KindWord(kind);
            var fitting = prompts.Temperaments.Where(t => t.Weight > 0 && t.Fits(word, aggression)).ToList();
            if (fitting.Count == 0) fitting = prompts.Temperaments.Where(t => t.Weight > 0 && t.Fits(word, 50)).ToList();
            if (fitting.Count == 0) return string.Empty;

            var roll = random.NextDouble() * fitting.Sum(t => t.Weight);
            foreach (var temperament in fitting)
            {
                roll -= temperament.Weight;
                if (roll < 0) return temperament.Id;
            }

            return fitting[^1].Id;
        }

        /// <summary>The same draws for the same key every run (string.GetHashCode is not)</summary>
        public static Random Seeded(string key)
        {
            var hash = 17u;
            foreach (var c in key) hash = hash * 31u + c;
            return new Random((int)(hash & 0x7FFFFFFF));
        }

        // ----- memories -----

        /// <summary>Days the kind remembers a talk</summary>
        public static int ForgetDays(PersonKind kind, TalkRules rules) => kind switch
        {
            PersonKind.Salesman => rules.ForgetDaysSalesman,
            PersonKind.Rival => rules.ForgetDaysRival,
            _ => rules.ForgetDaysSeller
        };

        /// <summary>What they still remember on <paramref name="date"/>, the newest <see cref="TalkRules.MemoriesGiven"/>, oldest first</summary>
        public static IReadOnlyList<PersonMemory> Remembered(Person person, DateTime date, TalkRules rules)
        {
            var days = ForgetDays(person.Kind, rules);
            return person.Memories
                .Where(m => (date.Date - m.Date.Date).TotalDays <= days)
                .TakeLast(Math.Max(0, rules.MemoriesGiven))
                .ToList();
        }

        /// <summary>A line of a talk kept with the person; what is forgotten by now goes</summary>
        public static void Remember(Person person, DateTime date, string text, string language, TalkRules rules)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            person.Memories.Add(new PersonMemory { Date = date, Text = text.Trim(), Language = language });
            Forget(person, date, rules);
        }

        /// <summary>The memories older than the kind remembers go, and at most twice what is told is kept</summary>
        public static void Forget(Person person, DateTime date, TalkRules rules)
        {
            var days = ForgetDays(person.Kind, rules);
            person.Memories.RemoveAll(m => (date.Date - m.Date.Date).TotalDays > days);
            var most = Math.Max(1, rules.MemoriesGiven * 2);
            if (person.Memories.Count > most) person.Memories.RemoveRange(0, person.Memories.Count - most);
        }

        /// <summary>
        /// The people nobody needs any more go: a private seller with nothing remembered, met longer ago than they
        /// remember. Rivals and salesmen stay (their temperament is who they are).
        /// </summary>
        public static void ForgetAll(GameState game, TalkRules rules)
        {
            foreach (var person in game.People) Forget(person, game.Date, rules);
            game.People.RemoveAll(p => p.Kind == PersonKind.PrivateSeller && p.Memories.Count == 0
                                       && (game.Date.Date - p.Met.Date).TotalDays > rules.ForgetDaysSeller);
        }

        /// <summary>The deal over <paramref name="thing"/>, begun at <paramref name="asked"/> the first time it is talked about</summary>
        public static PersonDeal Deal(Person person, string thing, decimal asked)
        {
            if (person.DealFor(thing) is { } deal) return deal;
            deal = new PersonDeal { Thing = thing, Asked = asked };
            person.Deals.Add(deal);
            return deal;
        }

        /// <summary>What was said about a thing kept with its deal, the newest few, so it is not said again</summary>
        public static void Said(PersonDeal deal, string text, TalkRules rules)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            deal.Said.Add(text.Trim());
            var most = Math.Max(1, rules.SaidKept);
            if (deal.Said.Count > most) deal.Said.RemoveRange(0, deal.Said.Count - most);
        }

        // ----- the rivals' races -----

        /// <summary>Races between rivals kept, newest last, the oldest dropped past <see cref="TalkRules.RivalRacesKept"/></summary>
        public static void RecordRivalRaces(GameState game, IEnumerable<RivalRaceRecord> races, TalkRules? rules = null)
        {
            rules ??= RuleBook.Current.Ai.Talk;
            game.RivalRaces ??= [];
            game.RivalRaces.AddRange(races);
            var most = Math.Max(0, rules.RivalRacesKept);
            if (game.RivalRaces.Count > most) game.RivalRaces.RemoveRange(0, game.RivalRaces.Count - most);
        }
    }
}
