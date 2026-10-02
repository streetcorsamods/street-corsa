using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.News
{
    /// <summary>What happened in one of the player's races, as the paper needs it</summary>
    public sealed record PlayerRaceFacts
    {
        public DateTime Date { get; init; }
        public string PlayerName { get; init; } = string.Empty;
        public string RivalName { get; init; } = string.Empty;

        /// <summary>"his", "her", "their" for the rival (<see cref="Opponents.OpponentRules.Possessive"/>)</summary>
        public string RivalPossessive { get; init; } = "their";
        public bool RivalIsKing { get; init; }
        public int PlayerReputation { get; init; }
        public int RivalReputation { get; init; }
        public string PlayerCar { get; init; } = string.Empty;
        public string RivalCar { get; init; } = string.Empty;
        public bool IsDrag { get; init; }

        /// <summary>The race had a winner</summary>
        public bool Decided { get; init; }
        public bool PlayerWon { get; init; }
        public bool PinkSlip { get; init; }
        public decimal CashWager { get; init; }

        /// <summary>The loser wrecked their car (<see cref="Race.WinCondition.PlayerCrashed"/>, <see cref="Race.WinCondition.OpponentCrashed"/>)</summary>
        public bool PlayerCrashed { get; init; }
        public bool RivalCrashed { get; init; }
        public bool PlayerBusted { get; init; }
        public bool RivalBusted { get; init; }
        public bool PlayerEscaped { get; init; }

        /// <summary>The event the race was for; null for a race at the diner</summary>
        public string? EventName { get; init; }

        /// <summary>The rival wanted this rematch (<see cref="Grudge"/>)</summary>
        public bool Rematch { get; init; }

        /// <summary>The rival wants the car back now</summary>
        public bool RivalSwearsRevenge { get; init; }

        /// <summary>The player's first win</summary>
        public bool FirstWin { get; init; }
    }

    /// <summary>
    /// The newspaper's race pages (<see cref="GameState.News"/>): a piece is written when a race is settled, and only
    /// when there is a story in it. The player's pink slips, the King, the police and wrecks make the paper; a
    /// run-of-the-mill cash race does not. From the rivals' own races only pink slips and wrecks do. The paper prints
    /// the last <see cref="DaysPrinted"/> days, newest and weightiest first.
    /// </summary>
    public static class NewsWriter
    {
        /// <summary>How far back the paper goes</summary>
        public static int DaysPrinted => Rules.RuleBook.Current.News.DaysPrinted;

        /// <summary>Pieces on the page</summary>
        public static int OnThePage => Rules.RuleBook.Current.News.OnThePage;

        /// <summary>How long a piece is kept at all</summary>
        public static int DaysKept => Rules.RuleBook.Current.News.DaysKept;

        public static int MaxKept => Rules.RuleBook.Current.News.MaxKept;

        /// <summary>A cash race worth writing about, at the least</summary>
        public static decimal BigWager => Rules.RuleBook.Current.News.BigWager;

        /// <summary>How much higher the rival's name has to be for the player's win to be an upset</summary>
        public static int UpsetReputation => Rules.RuleBook.Current.News.UpsetReputation;

        /// <summary>The piece about one of the player's races; null when there is no story in it</summary>
        public static NewsArticle? PlayerRace(PlayerRaceFacts f, Random random)
        {
            var (headline, body, weight) = Story(f, random);
            if (headline == null) return null;

            return new NewsArticle { Date = f.Date, Headline = headline, Body = body!, Weight = weight };
        }

        private static (string? Headline, string? Body, int Weight) Story(PlayerRaceFacts f, Random random)
        {
            var text = GameText.Installed;
            var p = f.PlayerName;
            var r = f.RivalName;
            var values = new TextValues
            {
                ["p"] = p,
                ["r"] = r,
                ["pPossessive"] = PossessiveOf(p),
                ["rPossessive"] = PossessiveOf(r),
                ["rivalPossessive"] = f.RivalPossessive,
                ["where"] = text.Get(f.IsDrag ? "news.where.strip" : "news.where.road"),
                ["playerCar"] = f.PlayerCar,
                ["rivalCar"] = f.RivalCar,
                ["playerCarShort"] = Short(f.PlayerCar),
                ["rivalCarShort"] = Short(f.RivalCar),
                ["eventName"] = f.EventName,
                ["wager"] = f.CashWager,
                ["winner"] = f.PlayerWon ? p : r
            };

            (string, string, int) Piece(string story, int weight, string suffix = "") =>
                (text.Pick($"news.{story}.headline", random, values), text.Pick($"news.{story}.body", random, values) + suffix, weight);

            string Line(string key) => text.Pick(key, random, values);

            // The police first: a bust is the story, whatever else happened
            if (f.PlayerBusted)
                return Piece("busted", 50, Line(f.RivalBusted ? "news.busted.rival_busted" : "news.busted.rival_escaped"));

            if (f.PlayerEscaped)
                return Piece("escaped", 50, f.RivalBusted ? Line("news.escaped.rival_busted") : "");

            if (!f.Decided) return (null, null, 0);

            if (f.RivalIsKing)
                return f.PlayerWon ? Piece("king_beaten", 100) : Piece("king_won", 80);

            var revenge = f.RivalSwearsRevenge ? Line("news.suffix.revenge") : "";
            var crash = f.PlayerCrashed ? Line("news.suffix.player_crashed")
                : f.RivalCrashed ? Line("news.suffix.rival_crashed")
                : "";

            if (f.Rematch && f.PinkSlip)
                return f.PlayerWon ? Piece("rematch_won", 70, revenge) : Piece("rematch_lost", 70);

            if (f.PinkSlip)
                return f.PlayerWon ? Piece("pink_won", 60, crash + revenge) : Piece("pink_lost", 60, crash);

            // An event won is the story even when the rival wrecked on the way
            if (f.EventName != null && f.PlayerWon)
                return Piece("event_won", 45, f.RivalCrashed ? Line("news.event_won.rival_crashed") : "");

            if (f.PlayerCrashed || f.RivalCrashed)
            {
                var who = f.PlayerCrashed ? p : r;
                var car = f.PlayerCrashed ? f.PlayerCar : f.RivalCar;
                values["who"] = who;
                values["whoPossessive"] = PossessiveOf(who);
                values["car"] = car;
                values["carShort"] = Short(car);

                // The player's own car is "the" car in a headline: the paper does not know the player as him or her
                values["whose"] = f.PlayerCrashed ? GameText.Say("paper.whose.player") : Capitalized(f.RivalPossessive);
                return Piece("wreck", 40);
            }

            if (f.PlayerWon && f.RivalReputation - f.PlayerReputation >= UpsetReputation)
                return Piece("upset", 35);

            if (f.CashWager >= BigWager)
                return Piece("big_money", 30);

            if (f.FirstWin && f.PlayerWon)
                return Piece("first_win", 25);

            return (null, null, 0);
        }

        /// <summary>The piece about a race between two rivals; null when there is no story in it</summary>
        /// <param name="loserPossessive">"his", "her", "their" for the loser (<see cref="Opponents.OpponentRules.Possessive"/>)</param>
        public static NewsArticle? RivalRace(DateTime date, string winner, string loser, string loserPossessive, string loserCar, bool isDrag,
            bool pinkSlip, bool loserCrashed, int carWins, Random random)
        {
            if (!pinkSlip && !loserCrashed) return null;

            var text = GameText.Installed;
            var values = new TextValues
            {
                ["winner"] = winner,
                ["loser"] = loser,
                ["loserPossessive"] = PossessiveOf(loser),
                ["whose"] = Capitalized(loserPossessive),
                ["loserCar"] = loserCar,
                ["loserCarShort"] = Short(loserCar),
                ["where"] = text.Get(isDrag ? "news.where.strip" : "news.where.rivals_road"),
                ["carWins"] = carWins
            };
            values["record"] = carWins >= 5 ? text.Pick("news.rivals.record", random, values) : "";

            var story = pinkSlip ? loserCrashed ? "pink_wreck" : "pink" : "wreck";
            return new NewsArticle
            {
                Date = date,
                Weight = pinkSlip ? 30 : 15,
                Headline = text.Pick($"news.rivals.{story}.headline", random, values),
                Body = text.Pick($"news.rivals.{story}.body", random, values)
            };
        }

        /// <summary>The new pieces after what was written before; old ones go</summary>
        public static void Add(GameState gameState, DateTime date, IEnumerable<NewsArticle> articles)
        {
            gameState.News ??= [];
            var added = articles.ToList();
            DatedLog.Append(gameState.News, added, a => a.Date, date, DaysKept, MaxKept);
            Added?.Invoke(gameState, added);
        }

        /// <summary>Told of every new piece once it is in the paper: the player's AI writes it again (<see cref="Ai.AiWriter.NewsAdded"/>)</summary>
        public static Action<GameState, IReadOnlyList<NewsArticle>>? Added { get; set; }

        /// <summary>What the paper prints on <paramref name="today"/>: the last few days, newest day first, the weightiest first within a day</summary>
        public static List<NewsArticle> FrontPage(IEnumerable<NewsArticle>? news, DateTime today) =>
            (news ?? [])
                .Where(a => a.Date.Date <= today.Date && (today.Date - a.Date.Date).TotalDays < DaysPrinted)
                .Select((a, i) => (Article: a, Order: i))
                .OrderByDescending(x => x.Article.Date.Date)
                .ThenByDescending(x => x.Article.Weight)
                .ThenByDescending(x => x.Order)
                .Select(x => x.Article)
                .Take(OnThePage)
                .ToList();

        private static string PossessiveOf(string name) => name.EndsWith('s') ? name + "'" : name + "'s";

        private static string Capitalized(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

        /// <summary>A car as a headline says it: the model without the year</summary>
        private static string Short(string car)
        {
            var space = car.IndexOf(' ');
            return space > 0 && int.TryParse(car[..space], out _) ? car[(space + 1)..] : car;
        }
    }
}
