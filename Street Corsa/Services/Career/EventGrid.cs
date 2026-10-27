using StreetCorsa.Models.Career.Events;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// How an event's field lines up (<see cref="EventField.Grid"/>): the rivals in their order on AC's grid and the
    /// player's place among them, and the field cut to the cars the track has pit boxes for
    /// </summary>
    public static class EventGrid
    {
        /// <summary>
        /// The rivals in grid order and the player's place (1 at the front). Random by <paramref name="seed"/> (one
        /// invitation lines up the same way every time), by reputation (the best known at the front, the player by
        /// theirs), or the player last or first with the rivals in the order they came.
        /// </summary>
        public static (List<EventOpponentResult> Rivals, int PlayerPosition) Order(string? grid, int playerReputation,
            IReadOnlyList<EventOpponentResult> rivals, int seed)
        {
            switch (grid)
            {
                case EventFieldGrids.PlayerFirst:
                    return (rivals.ToList(), 1);
                case EventFieldGrids.PlayerLast:
                    return (rivals.ToList(), rivals.Count + 1);
                case EventFieldGrids.Reputation:
                {
                    var ordered = rivals.OrderByDescending(r => r.Reputation).ToList();
                    var ahead = ordered.Count(r => r.Reputation > playerReputation);
                    return (ordered, ahead + 1);
                }
                default:
                {
                    var random = new Random(seed);
                    var shuffled = rivals.OrderBy(_ => random.Next()).ToList();
                    return (shuffled, random.Next(shuffled.Count + 1) + 1);
                }
            }
        }

        /// <summary>
        /// The rivals the track has room for: a pit box for every car, the player's too. A track that says nothing of its
        /// pit boxes takes them all. One rival always races.
        /// </summary>
        public static List<EventOpponentResult> Fit(IReadOnlyList<EventOpponentResult> rivals, string? pitboxes)
        {
            if (!int.TryParse(pitboxes, out var boxes) || boxes <= 0) return rivals.ToList();
            return rivals.Take(Math.Max(1, boxes - 1)).ToList();
        }
    }
}
