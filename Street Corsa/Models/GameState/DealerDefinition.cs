namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// Where a car sits on a dealer's lot: metres from the lot's origin, and which way it points.
    /// Authored per showroom, because the floor each one gives you is a different shape.
    /// </summary>
    public class LotBay
    {
        /// <summary>Across the lot, in metres</summary>
        public float X { get; set; }

        /// <summary>Along the lot, in metres</summary>
        public float Z { get; set; }

        /// <summary>Which way the car faces, in degrees. 0 points down +Z</summary>
        public float Heading { get; set; }
    }

    /// <summary>
    /// What a car dealer's and a part shop's definitions have in common: what the save's roster of them is kept up to
    /// (<c>DealerChurn.Merge</c>)
    /// </summary>
    public interface ILocationDefinition<out TLocation> where TLocation : DealerLocation
    {
        string Id { get; }
        string Name { get; }
        string Region { get; }

        /// <summary>The business as a save that has never had it starts it</summary>
        TLocation ToLocation();
    }

    /// <summary>
    /// A dealer's salesman as the mod gives him (<c>"salesman"</c> on a dealer): who he is, how he talks and deals (a
    /// temperament of <c>ai/people.json</c>), and his portrait, a picture next to the dealer files
    /// </summary>
    public class DealerSalesman
    {
        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }

        /// <summary>"man" or "woman"; left out, a man when the name is given, else drawn with the name</summary>
        public string? Sex { get; set; }

        /// <summary>A temperament's key in <c>ai/people.json</c>; empty, one is drawn</summary>
        public string Temperament { get; set; } = string.Empty;

        /// <summary>His portrait, a path under the mod's dealers folder ("salesmen/downtown_motors.png")</summary>
        public string? Portrait { get; set; }

        /// <summary>The portrait's file, found when the dealers are read; null when there is none</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? PortraitFile { get; set; }

        public Gender? Gender => string.IsNullOrWhiteSpace(Sex)
            ? null
            : string.Equals(Sex, "woman", StringComparison.OrdinalIgnoreCase) ? global::StreetCorsa.Models.GameState.Gender.Female : global::StreetCorsa.Models.GameState.Gender.Male;
    }

    /// <summary>
    /// Everything about a dealer that never changes: where it is on the map, what it looks like inside,
    /// and the kind of cars it keeps.
    ///
    /// Kept apart from <see cref="DealerLocation"/> on purpose. The save holds the dealer's Id and no more,
    /// so adding a field here does not have to reach into everybody's saved game: the definitions are the
    /// truth and are merged over the save on load (<c>DealerCatalog</c>).
    /// </summary>
    public class DealerDefinition : ILocationDefinition<DealerLocation>
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;

        /// <summary>A line for the map card: what sort of place this is</summary>
        public string Blurb { get; set; } = string.Empty;

        /// <summary>Where the pin sits on the map image, 0..1 from the left</summary>
        public float MapX { get; set; }

        /// <summary>Where the pin sits on the map image, 0..1 from the top</summary>
        public float MapY { get; set; }

        /// <summary>Folder name under content/showroom used as the lot's 3D scene</summary>
        public string ShowroomId { get; set; } = "industrial";

        /// <summary>
        /// The share of the price range this dealer stocks, cheapest to dearest, 0..1.
        /// The cheap lot takes the bottom of the market, the smart showroom the top.
        /// </summary>
        public float PriceBandLow { get; set; }
        public float PriceBandHigh { get; set; } = 1f;

        /// <summary>How rough the cars are here: the middle of the condition range, 0..1</summary>
        public float ConditionCenter { get; set; } = 0.6f;

        /// <summary>
        /// The kind of car the lot deals in, a car filter as an event's entry requirements are written (an import lot:
        /// <c>{ "type": "origin", "origin": "European" }</c>); null, any. Its price band is of the cars of that kind.
        /// </summary>
        [System.Text.Json.Serialization.JsonConverter(typeof(Career.Filters.CarFilterJsonConverter))]
        public Career.Filters.ICarFilter? Cars { get; set; }

        /// <summary>Hours of game time it takes to get here from the garage and back</summary>
        public float TravelHours { get; set; } = 1f;

        /// <summary>
        /// How many cars the lot carries. Keep it at or under the capacity of its showroom, or the overflow
        /// can only be read about and never looked at.
        /// </summary>
        public int StockLow { get; set; } = 8;
        public int StockHigh { get; set; } = 12;

        /// <summary>
        /// In business when a game starts. False: it waits to open some day, when the market has room for another lot
        /// (<c>DealerLifeService</c>). A save made before the dealer existed gets it the same way.
        /// </summary>
        public bool StartsOpen { get; set; } = true;

        /// <summary>
        /// Who sells the cars here, when the mod says (the base game's are painted ahead by tools/SalesmanPortraits); none,
        /// and the game makes one up the first time the player talks to him
        /// </summary>
        public DealerSalesman? Salesman { get; set; }

        /// <summary>The save only ever needs the name and the region, and how the business is going</summary>
        public DealerLocation ToLocation() => new()
        {
            Id = Id,
            Name = Name,
            Region = Region,
            Status = StartsOpen ? DealerStatus.Open : DealerStatus.Waiting
        };
    }
}
