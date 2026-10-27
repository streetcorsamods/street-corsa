using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// Service for selecting opponents for race events
    /// </summary>
    public interface IEventOpponentService
    {
        /// <summary>
        /// Get an opponent for an event.
        /// Returns pool opponent if available, otherwise special opponent. A pink-slip event only takes a
        /// racer of the pool; null when none can race it. An event with a field (<see cref="RaceEventDefinition.Field"/>)
        /// gets its rivals after this one in <see cref="EventOpponentResult.Field"/>.
        /// </summary>
        EventOpponentResult? GetOpponentForEvent(
            RaceEventDefinition eventDef,
            GameState gameState);
    }

    /// <summary>
    /// Result of opponent selection for an event
    /// </summary>
    public class EventOpponentResult
    {
        /// <summary>
        /// Whether this is a pool opponent (tracked in game state) or event-only
        /// </summary>
        public bool IsPoolOpponent { get; set; }

        /// <summary>
        /// The pool opponent (if IsPoolOpponent is true)
        /// </summary>
        public Opponent? PoolOpponent { get; set; }

        /// <summary>
        /// The pool opponent's car (if IsPoolOpponent is true)
        /// </summary>
        public Car? PoolOpponentCar { get; set; }

        /// <summary>
        /// The special opponent (if IsPoolOpponent is false)
        /// </summary>
        public EventOpponent? SpecialOpponent { get; set; }

        // Common properties for easy access
        public string OpponentName { get; set; } = string.Empty;
        public string? OpponentNickname { get; set; }
        public string CarDefinitionId { get; set; } = string.Empty;
        public string CarSkin { get; set; } = "default";
        public int Skill { get; set; }
        public int Aggression { get; set; }

        /// <summary>The racer's reputation, for a grid laid out by it; an event's one-off entrant has none</summary>
        public int Reputation { get; set; }

        /// <summary>
        /// A field's rivals after this one, each picked the same way, nobody twice; empty for a race of two. Only the
        /// first of a field carries them.
        /// </summary>
        public List<EventOpponentResult> Field { get; set; } = [];

        /// <summary>This rival and the field after them</summary>
        public IEnumerable<EventOpponentResult> All() => Field.Prepend(this);
    }
}
