using StreetCorsa.Models.Career.Filters;
using StreetCorsa.Models.Race;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Models.Career.Events
{
    /// <summary>
    /// Defines a race event/invitation that can appear in the game.
    /// Events have entry requirements and rewards.
    /// </summary>
    public class RaceEventDefinition
    {
        /// <summary>
        /// Unique identifier for this event type
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the event
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description/flavor text for the event
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The event's race format id (<see cref="Services.Race.RaceFormats"/>): the mod's own format, else the
        /// game's. Resolved when the events are read.
        /// </summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>
        /// The race type as events were written before formats ("raceType": "DragRace"): read into <see cref="Format"/>
        /// when that is not given
        /// </summary>
        public string? RaceType { get; set; }

        /// <summary>The laps the race is over; null: the format's</summary>
        public int? Laps { get; set; }

        /// <summary>
        /// Car filter for entry requirements (null = any car allowed)
        /// </summary>
        public ICarFilter? EntryRequirements { get; set; }

        /// <summary>
        /// Minimum reputation required to enter
        /// </summary>
        public int MinReputation { get; set; }

        /// <summary>
        /// Milestone IDs that must be completed to unlock this event
        /// </summary>
        public List<string> RequiredMilestones { get; set; } = [];

        /// <summary>
        /// Reward for winning the event
        /// </summary>
        public EventReward Reward { get; set; } = new();

        /// <summary>
        /// How this event is scheduled
        /// </summary>
        public EventSchedule Schedule { get; set; } = EventSchedule.Weekly;

        /// <summary>
        /// Track ID for this event (null = random appropriate track)
        /// </summary>
        public string? TrackId { get; set; }

        /// <summary>
        /// Optional opponent name for special event matchups
        /// </summary>
        public string? SpecificOpponent { get; set; }

        /// <summary>
        /// Whether this is a pink slip event
        /// </summary>
        public bool IsPinkSlip { get; set; }

        /// <summary>
        /// A bracket race (a drag race only): dial-ins, a staggered start, a breakout loses (<see cref="Services.Race.BracketRules"/>)
        /// </summary>
        public bool IsBracket { get; set; }

        /// <summary>Run as a bracket race: <see cref="IsBracket"/> on a format that can be one (a drag race)</summary>
        public bool IsBracketRace => IsBracket && Services.Race.RaceFormats.Current.Get(Format)?.Bracket == true;

        /// <summary>
        /// Special opponents for this event (if set, uses these instead of pool)
        /// </summary>
        public List<EventOpponent>? SpecialOpponents { get; set; }

        /// <summary>
        /// If true, only use special opponents (no pool mixing)
        /// </summary>
        public bool ExclusiveOpponents { get; set; }

        /// <summary>
        /// A field: more than one rival on the grid (<see cref="EventField"/>), on a format that runs one
        /// (<see cref="RaceFormat.Rivals"/>). Null: one rival, as an event always had. A field races for cash only: no
        /// pink slip.
        /// </summary>
        public EventField? Field { get; set; }

        /// <summary>
        /// What each car pays to start, the player's included: the pot is everyone's entry and <see cref="EventReward.Cash"/>,
        /// shared out by <see cref="EventReward.Places"/>. Zero: free to enter.
        /// </summary>
        public decimal Entry { get; set; }

        /// <summary>
        /// How likely the event is to turn up when its schedule lets it, 0 to 1 (a rare big one: 0.1); null, the rules'
        /// chance (<c>events.eventChance</c>)
        /// </summary>
        public double? Chance { get; set; }

        /// <summary>A field of more than one rival</summary>
        public bool IsField => Field is { Max: > 1 };

        /// <summary>
        /// Get a human-readable entry requirements description
        /// </summary>
        public string GetEntryRequirementsDescription()
        {
            var requirements = new List<string>();

            if (EntryRequirements != null)
                requirements.Add(EntryRequirements.DisplayDescription);

            if (MinReputation > 0)
                requirements.Add(GameText.Say("event.needs.reputation", ("reputation", MinReputation)));

            if (RequiredMilestones.Count > 0)
                requirements.Add(GameText.Say("event.needs.milestones", ("milestones", string.Join(", ", RequiredMilestones))));

            return requirements.Count > 0
                ? string.Join(", ", requirements)
                : GameText.Say("event.needs.none");
        }
    }

    /// <summary>
    /// An event's field: how many rivals, who they are, and how the grid is laid out
    /// </summary>
    public class EventField
    {
        /// <summary>The rivals, when the count is fixed: sets <see cref="Min"/> and <see cref="Max"/> both</summary>
        public int? Count { get; set; }

        /// <summary>The fewest rivals the field runs with</summary>
        public int Min { get; set; } = 1;

        /// <summary>The most; each invitation rolls a number between the two</summary>
        public int Max { get; set; } = 1;

        /// <summary>
        /// Who races: "pool" (the scene's racers, those whose cars suit the event; the default), the racer ids named in
        /// <see cref="Racers"/> ("racers"), or the members of the crew <see cref="Crew"/> ("crew"). The event's
        /// <see cref="RaceEventDefinition.SpecialOpponents"/> join either way. A field from the pool is filled out with
        /// one-off entrants; a field of named racers or of a crew is those who are there, unless <see cref="Fill"/> says so.
        /// </summary>
        public string From { get; set; } = EventFieldFrom.Pool;

        /// <summary>
        /// With a field of named racers or of a crew: the grid is filled out to the count with one-off entrants, as a field
        /// from the pool always is; without it the field is those of them who are there, however few
        /// </summary>
        public bool Fill { get; set; }

        /// <summary>With <see cref="From"/> "racers": the racers of the scene who make the field, by id</summary>
        public List<string> Racers { get; set; } = [];

        /// <summary>With <see cref="From"/> "crew": the crew whose members make the field (its full id once the events are read)</summary>
        public string? Crew { get; set; }

        /// <summary>
        /// With a field from a crew: the crew's leader races too, the others his wingmen; without it the leader sits the night out,
        /// as a boss does every event. A boss comes only once his path is open (until then the night is his wingmen's), and
        /// a boss in a field is beaten only when he finishes behind the player.
        /// </summary>
        public bool Boss { get; set; }

        /// <summary>The grid: "random" (the default), "reputation" (the best known at the front), "player_last" or "player_first"</summary>
        public string Grid { get; set; } = EventFieldGrids.Random;

        /// <summary>A staggered start: each car goes this many seconds after the one ahead of it (a train down a pass); 0, all at once</summary>
        public double Stagger { get; set; }
    }

    /// <summary>Where an event's field comes from (<see cref="EventField.From"/>)</summary>
    public static class EventFieldFrom
    {
        public const string Pool = "pool";
        public const string Racers = "racers";
        public const string Crew = "crew";
    }

    /// <summary>How an event's grid is laid out (<see cref="EventField.Grid"/>)</summary>
    public static class EventFieldGrids
    {
        public const string Random = "random";
        public const string Reputation = "reputation";
        public const string PlayerLast = "player_last";
        public const string PlayerFirst = "player_first";
    }

    /// <summary>
    /// How often an event can appear
    /// </summary>
    public enum EventSchedule
    {
        /// <summary>
        /// Appears once and can only be completed once
        /// </summary>
        OneTime,

        /// <summary>
        /// Can appear once per day
        /// </summary>
        Daily,

        /// <summary>
        /// Can appear once per week
        /// </summary>
        Weekly,

        /// <summary>
        /// Always available
        /// </summary>
        Permanent
    }

    /// <summary>
    /// Special opponent for events only - not tracked in game state
    /// </summary>
    public class EventOpponent
    {
        /// <summary>
        /// Display name of the opponent
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Nickname/title for the opponent
        /// </summary>
        public string? Nickname { get; set; }

        /// <summary>
        /// AC AI skill level (90-100; Opponent clamps to it)
        /// </summary>
        public int Skill { get; set; } = 90;

        /// <summary>
        /// AC AI aggression level (0-100)
        /// </summary>
        public int Aggression { get; set; } = 50;

        /// <summary>
        /// Car definition ID for the opponent's car
        /// </summary>
        public string CarDefinitionId { get; set; } = string.Empty;

        /// <summary>
        /// Car skin ID (optional)
        /// </summary>
        public string? CarSkin { get; set; }

        /// <summary>
        /// Path to opponent portrait (optional)
        /// </summary>
        public string? PortraitPath { get; set; }
    }
}
