using StreetCorsa.Models.GameState;
using StreetCorsa.Services.News;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Dealers
{
    /// <summary>
    /// How a business in town comes and goes, the same for the car dealers (<see cref="DealerLifeService"/>) and the
    /// part shops (<c>PartShopLifeService</c>): the roster kept up to the mods' files, which may close and which may
    /// open, the one picked, what closing down, shutting and opening do to it, and how the paper and the street hear of
    /// it. What each sells, and the words and weights of its stories, are the services' own. No state, so the rules can
    /// be checked on their own.
    /// </summary>
    public static class DealerChurn
    {
        /// <summary>
        /// The save's businesses brought up to the definitions: one defined since the save began joins it (open or
        /// waiting, as its definition says), one no longer defined goes, and the rest keep how their business is going
        /// under the name the definition gives them. Null when nothing is defined: a mod folder that did not read must
        /// not wipe the save's, so the caller leaves it as it is.
        /// </summary>
        public static List<TLocation>? Merge<TLocation, TDefinition>(IEnumerable<TLocation?>? saved, IReadOnlyList<TDefinition> defined)
            where TLocation : DealerLocation
            where TDefinition : ILocationDefinition<TLocation>
        {
            if (defined.Count == 0) return null;

            var known = (saved ?? [])
                .OfType<TLocation>()
                .GroupBy(l => l.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var roster = new List<TLocation>(defined.Count);
            foreach (var definition in defined)
            {
                if (known.TryGetValue(definition.Id, out var location))
                {
                    location.Id = definition.Id;
                    location.Name = definition.Name;
                    location.Region = definition.Region;
                    roster.Add(location);
                }
                else
                {
                    roster.Add(definition.ToLocation());
                }
            }

            return roster;
        }

        /// <summary>The open ones past their first months (<see cref="DealerRules.NewDealerGraceDays"/>): one of these may go under</summary>
        public static List<T> MayClose<T>(IEnumerable<T> all, DateTime date) where T : DealerLocation =>
            all.Where(l => l.Status == DealerStatus.Open
                           && (l.OpenedOn is not { } opened || (date.Date - opened.Date).TotalDays >= DealerRules.NewDealerGraceDays))
                .ToList();

        /// <summary>Those that have never opened; once there are none, premises that have stood empty long enough</summary>
        public static List<T> MayOpen<T>(IReadOnlyCollection<T> all, DateTime date) where T : DealerLocation
        {
            var waiting = all.Where(l => l.Status == DealerStatus.Waiting).ToList();
            if (waiting.Count > 0) return waiting;

            return all.Where(l => l.Status == DealerStatus.Closed
                                  && (l.ClosedOn is not { } closed || (date.Date - closed.Date).TotalDays >= DealerRules.ReopenAfterDays)).ToList();
        }

        /// <summary>
        /// Today's change, if one is due (see <see cref="DealerRules.Direction"/>), with the candidates each way. Rolls
        /// the dice once.
        /// </summary>
        public static (DealerRules.Change Change, List<T> MayClose, List<T> MayOpen) Decide<T>(IReadOnlyCollection<T> all, DateTime date, Random random)
            where T : DealerLocation
        {
            var open = all.Count(l => l.Status == DealerStatus.Open);
            var mayClose = MayClose(all, date);
            var mayOpen = MayOpen(all, date);
            return (DealerRules.Direction(open, all.Count, mayOpen.Count > 0, mayClose.Count > 0, random.NextDouble()), mayClose, mayOpen);
        }

        /// <summary>One of <paramref name="candidates"/>, the likelier the heavier <paramref name="weight"/> says it is. Rolls once.</summary>
        public static T PickWeighted<T>(IReadOnlyList<T> candidates, Func<T, double> weight, Random random)
        {
            var weights = candidates.Select(weight).ToList();
            var roll = random.NextDouble() * weights.Sum();
            for (var i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return candidates[i];
            }

            return candidates[^1];
        }

        /// <summary>The closing-down sale begins today; returns its last day</summary>
        public static DateTime StartSale(DealerLocation location, DateTime date)
        {
            var last = date.Date.AddDays(DealerRules.SaleDays - 1);
            location.Status = DealerStatus.ClosingDown;
            location.ClosesOn = last;
            return last;
        }

        /// <summary>The doors shut for good; its trade goes with it</summary>
        public static void Close(DealerLocation location, DateTime date)
        {
            location.Status = DealerStatus.Closed;
            location.ClosedOn = date.Date;
            location.ClosesOn = null;
            location.Trade = 0;
            location.TradeCountedTo = null;
        }

        /// <summary>It opens today, with a new business's custom (<see cref="DealerRules.OpeningTrade"/>); true when the premises had been closed</summary>
        public static bool Open(DealerLocation location, DateTime date)
        {
            var reopened = location.Status == DealerStatus.Closed;
            location.Status = DealerStatus.Open;
            location.OpenedOn = date.Date;
            location.ClosedOn = null;
            location.ClosesOn = null;
            location.Trade = DealerRules.OpeningTrade;
            location.TradeCountedTo = date.Date;
            return reopened;
        }

        // ----- what the town hears -----

        /// <summary>
        /// A change the paper and the street hear of: an article of <paramref name="weight"/> from the
        /// <c>news.{story}.headline</c>/<c>.body</c> pools, and a line from <c>street.{story}</c>, each with <paramref name="values"/>
        /// </summary>
        public static void Announce(string story, int weight, TextValues values, DateTime date, Random random,
            List<string> talk, List<NewsArticle> news)
        {
            news.Add(new NewsArticle
            {
                Date = date,
                Weight = weight,
                Headline = GameText.Installed.Pick($"news.{story}.headline", random, values),
                Body = GameText.Installed.Pick($"news.{story}.body", random, values)
            });
            talk.Add(GameText.Installed.Pick($"street.{story}", random, values));
        }

        /// <summary>
        /// The day's articles into the paper and lines into the diner's talk. The services call it in a <c>finally</c>:
        /// what happened before a later step of the day failed did happen, and the town hears of it all the same.
        /// </summary>
        public static void Tell(GameState gameState, DateTime date, List<string> talk, List<NewsArticle> news)
        {
            if (news.Count > 0) NewsWriter.Add(gameState, date, news);
            if (talk.Count > 0) OpponentLifeService.AddTalk(gameState, date, talk);
        }
    }
}
