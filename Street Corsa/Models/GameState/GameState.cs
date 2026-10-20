using LiteDB;

namespace StreetCorsa.Models.GameState
{
    public class GameState
    {
        /// <summary>The hour each day starts, by the rules of the game in play (<c>day.startHour</c>)</summary>
        public static int DayStartHour => StreetCorsa.Rules.RuleBook.Current.Day.FirstHour;

        /// <summary>The hour the day is over, by the rules of the game in play (<c>day.endHour</c>)</summary>
        public static int DayEndHour => StreetCorsa.Rules.RuleBook.Current.Day.LastHour;

        /// <summary>The career's first morning, by the rules of the game in play (<c>start.date</c>, <c>day.startHour</c>)</summary>
        public static DateTime GetStartingDateTime() => StartOf(StreetCorsa.Rules.RuleBook.Current);

        /// <summary>The first morning of a career played by <paramref name="book"/></summary>
        public static DateTime StartOf(StreetCorsa.Rules.RuleBook book) => book.Start.FirstMorning(book.Day);

        [BsonId]
        public int Id { get; set; } = 1; // Single record per save file

        // The game world's time, from the rules' first morning (GetStartingDateTime)
        public DateTime Date { get; set; }

        // Player
        public Player Player { get; set; }

        // AI Racers (categorized like GameMaker)
        public RacerCollection Racers { get; set; }

        // Markets
        public NewspaperAds NewspaperAds { get; set; }

        // Used Car Market System
        public List<UsedCarListing> UsedCarMarket { get; set; }
        public List<DealerLocation> DealerLocations { get; set; }

        /// <summary>The part shops, each with how its business is going and what is on its shelves (<c>PartShopLifeService</c>)</summary>
        public List<PartShopLocation> PartShops { get; set; } = [];

        // Scheduled Tasks
        public List<ScheduledTaskState> ScheduledTasks { get; set; }

        /// <summary>What the racers have been up to, newest last: the diner's "word on the street"</summary>
        public List<StreetTalkItem> StreetTalk { get; set; }

        /// <summary>What the paper has written about the races, newest last (<see cref="Services.News.NewsWriter"/>)</summary>
        public List<NewsArticle> News { get; set; }

        /// <summary>
        /// What the player's AI wrote that has no other place in the save (a dealer's word on a car, an ad's blurb), by key;
        /// the oldest go past <see cref="StreetCorsa.Rules.AiRules.MaxKept"/>
        /// </summary>
        public List<AiWords> AiWords { get; set; } = [];

        /// <summary>The people the player has talked to: who they are, what they remember, the prices talked over</summary>
        public List<Person> People { get; set; } = [];

        /// <summary>The races between rivals, newest last, the oldest dropped past the rules' most (<c>ai.talk.rivalRacesKept</c>)</summary>
        public List<RivalRaceRecord> RivalRaces { get; set; } = [];

        // Career Progression
        public CareerState Career { get; set; }

        /// <summary>The difficulty the career was started on; fixed for the life of the save</summary>
        public GameRules Rules { get; set; }

        /// <summary>
        /// The rules the career was started on (<see cref="StreetCorsa.Rules.RuleBook"/>), every figure written out: a mod
        /// updated or taken out later does not change them. Null in a save from before they were kept: it plays on the
        /// mods' rules, and keeps them from its next save.
        /// </summary>
        public string? RuleBook { get; set; }

        /// <summary>
        /// The race the player went off to and has not come back from yet: set just before AC starts and saved
        /// with the game, cleared once its result (or the lack of one) has been dealt with. A result file left
        /// over from a crash is only ever applied to the save that holds its race.
        /// </summary>
        public Models.Race.RaceContext? PendingRace { get; set; }

        /// <summary>
        /// The mods the game was last saved with (stamped on every save): loading it without one of them is warned of
        /// (<c>ModsInSave</c>)
        /// </summary>
        public List<Mods.ModRef> Mods { get; set; } = [];

        // Metadata
        public int CatalogVersion { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastPlayedDate { get; set; }

        // Save file name (not persisted in DB, set at runtime)
        [BsonIgnore]
        public string SaveName { get; set; } = string.Empty;

        /// <summary>
        /// The rivals met on the street on <see cref="MetOnTheStreetOn"/>: one waved off does not come round again the
        /// same night. Not saved, and gone with a game loaded again: that game's night has met nobody yet.
        /// </summary>
        [BsonIgnore]
        public HashSet<Guid> MetOnTheStreet { get; } = [];

        [BsonIgnore]
        public DateTime MetOnTheStreetOn { get; set; }

        public GameState()
        {
            var gameStart = GetStartingDateTime();
            var now = DateTime.Now;

            Date = gameStart;

            Player = new Player("Player");
            Racers = new RacerCollection();
            NewspaperAds = new NewspaperAds();

            UsedCarMarket = [];
            DealerLocations = [];
            ScheduledTasks = [];
            StreetTalk = [];
            News = [];
            Career = CareerState.CreateNew();
            Rules = new GameRules();

            CatalogVersion = 1;
            // Real-world timestamps for save file metadata
            CreatedDate = now;
            LastPlayedDate = now;
        }

        /// <param name="openFromStart">The victory paths open from the start (<see cref="CareerState.CreateNew"/>)</param>
        /// <param name="book">The rules the career is played by; the mods' (<see cref="StreetCorsa.Rules.RuleBook.Installed"/>) when not given</param>
        public static GameState CreateNew(string playerName, GameRules? rules = null, IEnumerable<string>? openFromStart = null,
            StreetCorsa.Rules.RuleBook? book = null)
        {
            book ??= StreetCorsa.Rules.RuleBook.Installed;
            rules ??= new GameRules();
            // The rules' starting money at the difficulty's share of it (Easy starts with more, Hard with less)
            var player = new Player(playerName) { Money = GameRules.Scale(book.Start.Money, rules.StartMoneyMultiplier) };
            var state = new GameState
            {
                Player = player,
                Date = StartOf(book),
                Rules = rules,
                RuleBook = book.ToJson(),
                Career = CareerState.CreateNew(openFromStart)
            };

            return state;
        }

        public void UpdateTime(DateTime newDate)
        {
            Date = newDate;
            LastPlayedDate = DateTime.Now;
        }
    }
}
