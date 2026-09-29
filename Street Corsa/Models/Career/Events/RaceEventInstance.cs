using StreetCorsa.Models.GameState;

namespace StreetCorsa.Models.Career.Events
{
    /// <summary>
    /// Represents an active instance of a race event: the state the save keeps of it (<see cref="RaceEventState"/>),
    /// with the track and the opponent its definition gives it. Only the state is stored; these two are filled in
    /// from the definition each time.
    /// </summary>
    public class RaceEventInstance : RaceEventState
    {
        public RaceEventInstance()
        {
        }

        /// <summary>The instance of a stored state, with what its definition gives it</summary>
        public RaceEventInstance(RaceEventState state, RaceEventDefinition? definition)
        {
            CopyFrom(state);
            TrackId = definition?.TrackId;
            OpponentName = definition?.SpecificOpponent;
        }

        /// <summary>
        /// Track used for this event instance
        /// </summary>
        public string? TrackId { get; set; }

        /// <summary>
        /// Opponent assigned to this event instance
        /// </summary>
        public string? OpponentName { get; set; }
    }
}
