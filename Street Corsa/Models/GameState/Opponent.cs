namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// Represents an AI opponent racer with personality and driving traits.
    /// This is the engine-agnostic layer that defines WHO the racer is.
    /// AC-specific AI parameters are derived at runtime by the adapter layer.
    /// </summary>
    public class Opponent : Racer
    {
        /// <summary>
        /// Unique identifier for this opponent
        /// </summary>
        public Guid OpponentId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The id of the definition this opponent was made from ("drv_001" in opponent_definitions.json); empty
        /// for one that was generated. <see cref="OpponentId"/> is made from it, so it is stable across loads.
        /// </summary>
        public string DefinitionId { get; set; } = string.Empty;

        /// <summary>
        /// A boss, at the top of a victory path: only races for pink slips, only once
        /// the player has opened his path, and never in the races nobody watches
        /// </summary>
        public bool IsBoss { get; set; }

        /// <summary>The id of the victory path that ends at this boss; empty: he is out from the start</summary>
        public string BossPath { get; set; } = string.Empty;

        /// <summary>
        /// The full id of the crew they ride with (<see cref="Services.Opponents.CrewBook"/>), from the racers' data (<c>"crew"</c>,
        /// or the crew whose <c>boss</c> names them); empty, they ride alone. Kept in the save, and put on them again from their
        /// definition as the game is reviewed (<c>OpponentInitializationService.EnsureNewcomers</c>), so a mod can move them.
        /// </summary>
        public string Crew { get; set; } = string.Empty;

        /// <summary>A boss's own figures from the racers' data, for when he joins a game; not saved</summary>
        [LiteDB.BsonIgnore]
        public BossRecord? BossRecord { get; set; }

        /// <summary>
        /// The kind of car the racer drives, from the racers' data (<c>"cars"</c>, a car filter as an event's entry
        /// requirements are written); null, any. He gets and buys one of that kind when there is one to be had. Not
        /// saved: put on him from his definition as the game is reviewed (<c>OpponentInitializationService.EnsureNewcomers</c>).
        /// </summary>
        [LiteDB.BsonIgnore]
        public Career.Filters.ICarFilter? Drives { get; set; }

        /// <summary>
        /// Opponent nickname (e.g., "Flathead", "Redline")
        /// </summary>
        public string Nickname { get; set; } = string.Empty;

        /// <summary>
        /// Age of the opponent (affects aggression tendencies)
        /// </summary>
        public int Age { get; set; }

        /// <summary>
        /// Gender of the opponent (used as tendency in aggression variation)
        /// </summary>
        public Gender Gender { get; set; }

        /// <summary>
        /// Driving skill level (90-100)
        /// Maps to AC AI Strength
        /// </summary>
        public int Skill { get; set; }

        /// <summary>
        /// Risk appetite and racing aggression (0-100)
        /// Maps to AC AI Aggression
        /// </summary>
        public int Aggression { get; set; }

        /// <summary>
        /// Optional portrait reference for UI display
        /// </summary>
        public string? PortraitPath { get; set; }

        /// <summary>
        /// Neighborhood or location flavor text
        /// </summary>
        public string? Location { get; set; }

        /// <summary>
        /// Optional background lore or personality description
        /// </summary>
        public string? Biography { get; set; }

        /// <summary>They lost a pink slip to the player and want a rematch; null when they don't (see <see cref="Models.GameState.Grudge"/>)</summary>
        public Grudge? Grudge { get; set; }

        /// <summary>How often they went broke and scraped money together; one time too many and they give up the scene</summary>
        public int TimesBroke { get; set; }

        /// <summary>Broke right now: a spell of being broke counts once in <see cref="TimesBroke"/>, however many days it lasts</summary>
        public bool IsBroke { get; set; }

        /// <summary>Game date they started sitting out (<see cref="RacerStatus.Retired"/>); null while racing or not on the street yet</summary>
        public DateTime? SittingOutSince { get; set; }

        /// <summary>Game date they left the scene (<see cref="RacerStatus.Departed"/>); null while they are around</summary>
        public DateTime? LeftDate { get; set; }

        /// <summary>
        /// Constructor for creating a new opponent
        /// </summary>
        public Opponent(string name, int age, Gender gender, int skill, int aggression)
            : base(RacerType.AI, name)
        {
            Age = age;
            Gender = gender;
            Skill = ClampSkill(skill);
            Aggression = ClampAggression(aggression);
        }

        /// <summary>
        /// Parameterless constructor for LiteDB
        /// </summary>
        public Opponent() : base(RacerType.AI, "Unknown Opponent")
        {
            Age = 25;
            Gender = Gender.Male;
            Skill = 90;
            Aggression = 50;
        }

        /// <summary>
        /// Adjust skill by a delta amount (event-driven evolution)
        /// </summary>
        public void AdjustSkill(int delta)
        {
            Skill = ClampSkill(Skill + delta);
        }

        /// <summary>
        /// Adjust aggression by a delta amount (event-driven evolution)
        /// </summary>
        public void AdjustAggression(int delta)
        {
            Aggression = ClampAggression(Aggression + delta);
        }

        /// <summary>Lowest skill an opponent drives with. 90 on purpose: below that AC's AI is too slow to be a race</summary>
        public const int MinSkill = 90;

        /// <summary>Highest skill: AC's AI_LEVEL tops out at 100</summary>
        public const int MaxSkill = 100;

        /// <summary>
        /// Clamp skill to valid range (90-100)
        /// </summary>
        private static int ClampSkill(int value) => Math.Clamp(value, MinSkill, MaxSkill);

        /// <summary>
        /// Clamp aggression to valid range (0-100)
        /// </summary>
        private static int ClampAggression(int value) => Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// Gender enum (used for tendency in personality generation)
    /// </summary>
    public enum Gender
    {
        Male,
        Female,
        Other
    }

    /// <summary>
    /// A boss's own figures as the racers' data has them (<c>"boss": { ... }</c>); what one leaves out is the rules'
    /// (<c>rivals.boss</c>)
    /// </summary>
    public sealed class BossRecord
    {
        /// <summary>The victory path that ends at him</summary>
        public string? Path { get; set; }

        public decimal? Money { get; set; }

        public int? CarChoices { get; set; }

        public int? Races { get; set; }

        public int? Wins { get; set; }

        public int? PinkSlipsWon { get; set; }

        /// <summary>These figures over <paramref name="rules"/></summary>
        public StreetCorsa.Rules.BossRules Over(StreetCorsa.Rules.BossRules rules) => new()
        {
            Money = Money ?? rules.Money,
            CarChoices = CarChoices ?? rules.CarChoices,
            Races = Races ?? rules.Races,
            Wins = Wins ?? rules.Wins,
            PinkSlipsWon = PinkSlipsWon ?? rules.PinkSlipsWon
        };
    }
}
