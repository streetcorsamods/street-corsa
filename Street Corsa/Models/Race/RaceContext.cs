namespace StreetCorsa.Models.Race
{
    /// <summary>
    /// Race context containing pre-race setup information
    /// Used to correlate race results with game state updates
    /// Stored in LaunchIntent.Metadata and passed to ingestion service
    /// </summary>
    public class RaceContext
    {
        /// <summary>
        /// Unique identifier for this race context
        /// </summary>
        public Guid ContextId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Player's racer name
        /// </summary>
        public string PlayerName { get; set; } = string.Empty;

        /// <summary>
        /// Opponent racer name
        /// </summary>
        public string OpponentName { get; set; } = string.Empty;

        /// <summary>
        /// Player's car instance ID
        /// </summary>
        public Guid PlayerCarInstanceId { get; set; }

        /// <summary>
        /// Opponent's car instance ID
        /// </summary>
        public Guid OpponentCarInstanceId { get; set; }

        /// <summary>
        /// The opponent's car model. The one way to name an event-only opponent's car, which is nobody's in the game
        /// (<see cref="OpponentCarInstanceId"/> is empty then). Empty on contexts from before it was kept.
        /// </summary>
        public string OpponentCarDefinitionId { get; set; } = string.Empty;

        /// <summary>
        /// Cash wager amount (0 if no wager)
        /// </summary>
        public decimal CashWager { get; set; }

        /// <summary>
        /// Whether this is a pink slip race (loser gives up their car)
        /// </summary>
        public bool IsPinkSlip { get; set; }

        /// <summary>
        /// Track identifier
        /// </summary>
        public string TrackId { get; set; } = string.Empty;

        /// <summary>
        /// Track configuration/layout (e.g., "drag1000", "gp")
        /// </summary>
        public string? TrackConfig { get; set; }

        /// <summary>
        /// The race's format id (<see cref="Services.Race.RaceFormats"/>). Kept under the name the race type had, whose
        /// values ("DragRace") are the core formats' aliases.
        /// </summary>
        [LiteDB.BsonField("RaceType")]
        public string Format { get; set; } = string.Empty;

        /// <summary>The laps the race is over (an event's own); null: the format's</summary>
        public int? Laps { get; set; }

        /// <summary>
        /// The format's family when the race was set up (<see cref="RaceFormatFamilies"/>): what the career counts the
        /// race as, even when the format's mod is gone by the time the result comes in. Null on contexts from before.
        /// </summary>
        public string? Family { get; set; }

        /// <summary>A drag race, by <see cref="Family"/>; a context from before goes by its format</summary>
        [LiteDB.BsonIgnore]
        public bool IsDrag => Family != null ? Family == RaceFormatFamilies.Drag : Services.Race.RaceFormats.Current.IsDrag(Format);

        /// <summary>
        /// When this context was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// When Assetto Corsa was started for this race; null while it is being prepared, and for a race whose
        /// launch never got that far. Only a race that ran can be forfeited for bringing back no result: a pending
        /// race without it (the app died before AC started) is simply released.
        /// </summary>
        public DateTime? LaunchedAt { get; set; }

        /// <summary>
        /// Event ID if this is an event race (null for regular races)
        /// </summary>
        public string? EventId { get; set; }

        /// <summary>
        /// The instance of the event this race is for (<c>RaceEventState.InstanceId</c>). Daily and weekly
        /// events share their definition id, so this is what tells them apart when the race completes one;
        /// null for regular races and for contexts saved before instances had ids.
        /// </summary>
        public Guid? EventInstanceId { get; set; }

        /// <summary>
        /// True if opponent is event-only (don't track their stats)
        /// </summary>
        public bool IsEventOnlyOpponent { get; set; }

        /// <summary>
        /// Test-and-tune: the player alone on the strip, pass after pass, nothing at stake. There is no opponent:
        /// its names and ids are empty.
        /// </summary>
        public bool IsTestAndTune { get; set; }

        /// <summary>The player's dial-in in a bracket race, in seconds over the quarter; null in any other race</summary>
        public double? PlayerDialIn { get; set; }

        /// <summary>The opponent's dial-in in a bracket race; null in any other race</summary>
        public double? OpponentDialIn { get; set; }

        /// <summary>A bracket race: both racers dialled in (<see cref="Services.Race.BracketRules"/>)</summary>
        [LiteDB.BsonIgnore]
        public bool IsBracket => PlayerDialIn != null && OpponentDialIn != null;

        /// <summary>
        /// A field: the rivals after the opponent (cars 2 on in race.ini), in their order there. Empty for a race of
        /// two. The opponent stays the race's first rival (the one challenged, an event's boss): who the race is named
        /// after. A field races for cash only, never a pink slip or a wager between two.
        /// </summary>
        public List<FieldEntrant> Field { get; set; } = [];

        /// <summary>More than one rival on the grid</summary>
        [LiteDB.BsonIgnore]
        public bool IsField => Field.Count > 0;

        /// <summary>How many rivals the race has: none on a test-and-tune, else the opponent and the field</summary>
        [LiteDB.BsonIgnore]
        public int RivalCount => IsTestAndTune ? 0 : 1 + Field.Count;

        /// <summary>Every rival in the race, the opponent first, in their order in race.ini (car 1 on)</summary>
        public IEnumerable<FieldEntrant> Rivals()
        {
            if (IsTestAndTune) yield break;
            yield return new FieldEntrant
            {
                Name = OpponentName,
                CarInstanceId = OpponentCarInstanceId,
                CarDefinitionId = OpponentCarDefinitionId,
                IsEventOnly = IsEventOnlyOpponent
            };
            foreach (var entrant in Field) yield return entrant;
        }

        /// <summary>
        /// This race as it stands between the player and one of its rivals: the race of two the career settles for
        /// each rival of a field. No stakes go with it (a field races for cash only).
        /// </summary>
        public RaceContext Against(FieldEntrant rival)
        {
            var duel = (RaceContext)MemberwiseClone();
            duel.OpponentName = rival.Name;
            duel.OpponentCarInstanceId = rival.CarInstanceId;
            duel.OpponentCarDefinitionId = rival.CarDefinitionId;
            duel.IsEventOnlyOpponent = rival.IsEventOnly;
            duel.Field = [];
            duel.CashWager = 0;
            duel.IsPinkSlip = false;
            return duel;
        }
    }

    /// <summary>One rival of a field (<see cref="RaceContext.Field"/>): who, and in which car</summary>
    public sealed class FieldEntrant
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Their own car, when they have one on the books; empty for an event's one-off entrant</summary>
        public Guid CarInstanceId { get; set; }

        /// <summary>The car's model, the one way to name a one-off entrant's car</summary>
        public string CarDefinitionId { get; set; } = string.Empty;

        /// <summary>An event's one-off entrant, not one of the scene's racers: no stats are kept for them</summary>
        public bool IsEventOnly { get; set; }
    }
}
