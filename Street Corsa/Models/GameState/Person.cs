namespace StreetCorsa.Models.GameState
{
    /// <summary>What a person the player can talk to is to the game</summary>
    public enum PersonKind
    {
        /// <summary>Somebody selling a car or a part in the paper, from home</summary>
        PrivateSeller,

        /// <summary>A dealer's salesman, on the lot</summary>
        Salesman,

        /// <summary>A racer on the street (<see cref="Opponent"/>): the racer's own record says who they are</summary>
        Rival
    }

    /// <summary>
    /// Somebody the player has talked to, or is about to (<see cref="GameState.People"/>, <c>Services.Talk.TalkPeople</c>):
    /// made once, when first needed, from a seed of who they are, so the seller of an ad is the same person every call.
    /// What they remember of past talks fades (<see cref="Memories"/>); the deals they made are kept as long as they are.
    /// </summary>
    public class Person
    {
        /// <summary>Who they are to the game: "rival:&lt;opponent id&gt;", "dealer:&lt;dealer id&gt;", "car:&lt;listing id&gt;", "part:&lt;ad id&gt;"</summary>
        public string Key { get; set; } = string.Empty;

        public PersonKind Kind { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }

        public Gender Gender { get; set; }

        /// <summary>The mods' temperament (<c>ai/people.json</c>): how they talk and how far they come down on a price</summary>
        public string Temperament { get; set; } = string.Empty;

        /// <summary>Game time they were first talked to</summary>
        public DateTime Met { get; set; }

        /// <summary>What they remember of their talks with the player, newest last; forgotten after the rules' days</summary>
        public List<PersonMemory> Memories { get; set; } = [];

        /// <summary>The prices talked over, one per thing they sell (a salesman sells many)</summary>
        public List<PersonDeal> Deals { get; set; } = [];

        public PersonDeal? DealFor(string thing) => Deals.FirstOrDefault(d => d.Thing == thing);
    }

    /// <summary>One line a person keeps of a talk with the player, in the words of the language it was written in</summary>
    public class PersonMemory
    {
        public DateTime Date { get; set; }

        public string Text { get; set; } = string.Empty;

        public string Language { get; set; } = string.Empty;
    }

    /// <summary>
    /// A price talked over: what was asked when the talking began (the floor is worked out from it, so asking again and
    /// again never brings it lower), what was agreed, and what was said about the thing, so it is not said again
    /// </summary>
    public class PersonDeal
    {
        /// <summary>"car:&lt;listing id&gt;", "rivalcar:&lt;ad id&gt;", "part:&lt;ad id&gt;"</summary>
        public string Thing { get; set; } = string.Empty;

        /// <summary>The price asked before any talk</summary>
        public decimal Asked { get; set; }

        /// <summary>The price the seller came down to, if any: the ad's price now</summary>
        public decimal? Agreed { get; set; }

        /// <summary>Offers turned down or countered: past the rules' most, the seller stands firm</summary>
        public int Counters { get; set; }

        /// <summary>Game time it was last talked over: forgotten as the person's memories are (<c>TalkPeople.Forget</c>)</summary>
        public DateTime? Date { get; set; }

        /// <summary>What the seller said about it last (a salesman's pitches), newest last, a few kept</summary>
        public List<string> Said { get; set; } = [];
    }

    /// <summary>A race between two rivals the player was not in, kept so the rivals can talk true about it</summary>
    public class RivalRaceRecord
    {
        public DateTime Date { get; set; }

        public string Winner { get; set; } = string.Empty;

        public string Loser { get; set; } = string.Empty;

        /// <summary>The loser's car (its definition id), when known</summary>
        public string? LoserCar { get; set; }

        /// <summary>"Drag" or "Road"</summary>
        public string RaceType { get; set; } = string.Empty;

        public bool PinkSlip { get; set; }

        public bool Crashed { get; set; }
    }
}
