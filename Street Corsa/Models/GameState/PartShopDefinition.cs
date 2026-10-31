namespace StreetCorsa.Models.GameState
{
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

        /// <summary>
        /// The kind of shop, as the rules' <c>partShops.kinds</c> name them (the core's "speed", "wheels", "junkyard" and
        /// "machine", and a mod's own); any other is a "specialist": new parts of its makes, the factory ones and the hot
        /// ones. The kind says what the shop does (<see cref="KindRules"/>).
        /// </summary>
        public string Kind { get; set; } = Rules.PartShopRules.SpecialistKind;

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

        /// <summary>
        /// Who is behind the counter, as a dealer's salesman is given (the portrait under the mod's partshops folder);
        /// left out, somebody is drawn
        /// </summary>
        public DealerSalesman? Clerk { get; set; }

        /// <summary>Which of the shop scene's looks the shop wears (<c>shops/shop.json</c>); empty: its kind's</summary>
        public string Look { get; set; } = string.Empty;

        /// <summary>The kind by its name, as the rules know it; a name they have not (a number, two names) is a specialist</summary>
        public string KindId =>
            Kind?.Trim().ToLowerInvariant() is { Length: > 0 } name && Rules.RuleBook.Current.PartShops.Kinds.ContainsKey(name)
                ? name
                : Rules.PartShopRules.SpecialistKind;

        /// <summary>What the shop's kind does and its figures (<c>partShops.kinds</c> in the rules)</summary>
        public Rules.PartShopKindRules KindRules => Rules.RuleBook.Current.PartShops.For(KindId);

        public PartShopLocation ToLocation() => new()
        {
            Id = Id,
            Name = Name,
            Region = Region,
            Status = StartsOpen ? DealerStatus.Open : DealerStatus.Waiting
        };
    }
}
