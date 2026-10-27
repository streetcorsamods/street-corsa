using System.Text.Json.Serialization;

namespace StreetCorsa.Models.Career
{
    /// <summary>
    /// A crew: racers who ride together, as a mod's <c>crews/*.json</c> lists them (<c>{ "crews": [ ... ] }</c>). Its
    /// members are the racers whose <c>"crew"</c> names it, and its leader (<see cref="Boss"/>, a racer id) belongs to it
    /// by being named. An event's field may be the crew (<see cref="Events.EventField.From"/> "crew"), its members as
    /// regulars of the crew's home diner, and a crew has lines of its own as a racer has (<see cref="Services.Opponents.RacerWords"/>).
    /// </summary>
    public sealed class Crew
    {
        /// <summary>The crew's id, namespaced by its mod once loaded (the core's and a setting's without)</summary>
        [JsonIgnore]
        public string Id { get; set; } = string.Empty;

        /// <summary>The mod the crew came from</summary>
        [JsonIgnore]
        public string ModId { get; set; } = string.Empty;

        /// <summary>What they call themselves, as the player reads it</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>A line about them: for the AI that speaks for its members, and the player</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Their colour, "#RRGGBB" (or "#AARRGGBB"): the jackets, worn in the diner's list; null, no colour of their own</summary>
        public string? Colour { get; set; }

        /// <summary>
        /// The diner the crew hangs out at (a diner id, the mod's own first): its members are regulars there whatever they drive,
        /// the leader too; null, each member sits where their car puts them
        /// </summary>
        public string? Home { get; set; }

        /// <summary>
        /// The leader's racer id, as the mod wrote it (the mod's own racer first, then the game's); null, a crew with no leader.
        /// The leader is a member without naming the crew. In a field from the crew he races only when the event asks for him
        /// (<see cref="Events.EventField.Boss"/>).
        /// </summary>
        public string? Boss { get; set; }

        /// <summary>The crew has a leader</summary>
        [JsonIgnore]
        public bool HasLeader => !string.IsNullOrEmpty(Boss);
    }
}
