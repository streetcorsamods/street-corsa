namespace StreetCorsa.Models.GameState
{
    /// <summary>What kind of part shop: what it deals in, new or used</summary>
    public enum PartShopKind
    {
        /// <summary>New parts for the makes it names, the factory ones and the hot ones</summary>
        Specialist,

        /// <summary>New performance parts for any make: carburettors, blowers, cams, manifolds, headers. Dear, and not much of each.</summary>
        Speed,

        /// <summary>New running gear: tyres, rims, brakes, springs and shocks</summary>
        Wheels,

        /// <summary>Used parts and whole engines out of wrecks, cheap; buys the player's old parts for more than a trade-in</summary>
        Junkyard
    }

    /// <summary>
    /// A part shop as a mod defines it (a <c>shops</c> list in its <c>partshops</c> folder): where it is on the part shops' map,
    /// what kind of shop it is and what it asks. The save keeps the shop's business and stock (<see cref="PartShopLocation"/>)
    /// and this is merged over it on load, so a field added here reaches every save. Numbers left out take the kind's
    /// own (<c>PartShopRules</c>).
    /// </summary>
    public class PartShopDefinition : ILocationDefinition<PartShopLocation>
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;

        /// <summary>A line for the map card: what sort of place this is</summary>
        public string Blurb { get; set; } = string.Empty;

        /// <summary>"specialist", "speed", "wheels" or "junkyard"</summary>
        public string Kind { get; set; } = "specialist";

        /// <summary>
        /// The makes it deals in, as the part packs name them (their <c>"make"</c>): a specialist stocks every pack of
        /// these, a mod's too; a speed shop or junkyard with makes keeps to them. Empty: any make.
        /// </summary>
        public List<string> Makes { get; set; } = [];

        /// <summary>Only these kinds of part, as the shop lists them ("Camshafts"); empty: whatever its kind of shop sells</summary>
        public List<string> Groups { get; set; } = [];

        /// <summary>Where the pin sits on the map picture (the part shops' map uses the dealers' picture), 0..1 from the left and from the top</summary>
        public float MapX { get; set; }
        public float MapY { get; set; }

        /// <summary>Hours of game time it takes to get here from the garage and back</summary>
        public float TravelHours { get; set; } = 1f;

        /// <summary>What it asks against a part's list price (a junkyard: against what the used part is worth)</summary>
        public double? PriceFactor { get; set; }

        /// <summary>What it pays for the player's parts, against what they are worth</summary>
        public double? BuyFactor { get; set; }

        /// <summary>How many different parts it carries (a junkyard: how many parts in the yard)</summary>
        public int? StockLow { get; set; }
        public int? StockHigh { get; set; }

        /// <summary>Days between deliveries (a junkyard: between wrecks coming in)</summary>
        public int? RestockDays { get; set; }

        /// <summary>In business when a game starts; false: it opens some day, as the dealers do</summary>
        public bool StartsOpen { get; set; } = true;

        /// <summary>The kind by its name; a name that is none of them (a number, or two names) is a specialist</summary>
        public PartShopKind KindOf =>
            Kind?.Trim() is { Length: > 0 } name && name.All(char.IsAsciiLetter)
            && Enum.TryParse<PartShopKind>(name, ignoreCase: true, out var kind)
                ? kind
                : PartShopKind.Specialist;

        public PartShopLocation ToLocation() => new()
        {
            Id = Id,
            Name = Name,
            Region = Region,
            Status = StartsOpen ? DealerStatus.Open : DealerStatus.Waiting
        };
    }
}
