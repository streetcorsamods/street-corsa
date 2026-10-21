using System.Text.Json.Serialization;

namespace StreetCorsa.Models.Diners
{
    /// <summary>
    /// A diner the racers go to, as a mod's <c>diners/*.json</c> lists it (<c>{ "diners": [ ... ] }</c>): a level of the
    /// scene. Each diner takes the cars from a horsepower up (<see cref="PowerFrom"/>) to the next diner's floor: a
    /// racer is a regular of the diner their car's power puts them in, and the regulars of a diner above the player's
    /// car will not race them. The first diner (from 0) is where everybody starts out.
    /// </summary>
    public sealed class Diner
    {
        /// <summary>The diner's id, namespaced by its mod once loaded (the base game's without)</summary>
        [JsonIgnore]
        public string Id { get; set; } = string.Empty;

        /// <summary>The mod the diner came from</summary>
        [JsonIgnore]
        public string ModId { get; set; } = string.Empty;

        /// <summary>The text key of its name; left out, <c>diner.name.&lt;id&gt;</c>, else its street's title</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>The text key of a line about the place for the map's card; left out, <c>diner.about.&lt;id&gt;</c>, else nothing</summary>
        [JsonPropertyName("about")]
        public string? About { get; set; }

        /// <summary>The text key of the part of town it is in, for the map's card; left out, <c>diner.region.&lt;id&gt;</c>, else nothing</summary>
        [JsonPropertyName("region")]
        public string? Region { get; set; }

        /// <summary>Where the diner is on the dealers' map, fractions of the whole picture as the dealers' are; left out, it is marked along the map's foot</summary>
        [JsonPropertyName("mapX")]
        public double? MapX { get; set; }

        [JsonPropertyName("mapY")]
        public double? MapY { get; set; }

        /// <summary>The diner is marked on the map: both fractions given, and on the picture</summary>
        [JsonIgnore]
        public bool IsOnMap => MapX is >= 0 and <= 1 && MapY is >= 0 and <= 1;

        /// <summary>
        /// The street the diner stands on: the id of a street with a diner.json (<see cref="Services.Diner.DinerScenes"/>). One
        /// with no such street, or none named, is seen on the first diner's street.
        /// </summary>
        [JsonPropertyName("street")]
        public string? Street { get; set; }

        /// <summary>The diner's class: the cars from this many horsepower (the base game's unit) are its regulars'</summary>
        [JsonPropertyName("powerFrom")]
        public double PowerFrom { get; set; }

        /// <summary>How many times the house limits the cash wagers go for here: the top diner plays for more</summary>
        [JsonPropertyName("stakes")]
        public decimal Stakes { get; set; } = 1m;

        /// <summary>The place among diners of one floor: the diners are ordered by <see cref="PowerFrom"/>, then by this; the first of a floor is where a car of that power belongs, its racers shared among them</summary>
        [JsonPropertyName("order")]
        public int Order { get; set; }

        /// <summary>The level: 0 for the beginners' diner, counting up. Set by the book from the order of the floors.</summary>
        [JsonIgnore]
        public int Level { get; set; }

        /// <summary>What the diner wears on its street (<c>"look"</c>, read by the book); null: the street's own look groups and lights</summary>
        [JsonIgnore]
        public DinerLook? Look { get; set; }

        /// <summary>The first diner, where everybody starts out: no car is below it</summary>
        [JsonIgnore]
        public bool IsFirst => Level == 0;
    }
}
