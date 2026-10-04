namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// Represents a car available for purchase in the used car market.
    /// This is market state, stored in GameState, not Catalog.
    /// </summary>
    public class UsedCarListing : IAdWords
    {
        /// <summary>
        /// Unique identifier for this listing
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// References CarDefinition.Id from catalog
        /// </summary>
        public string CarDefinitionId { get; set; } = string.Empty;

        /// <summary>
        /// Price for this specific listing (varies from BasePrice based on condition)
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>
        /// Mileage in kilometers
        /// </summary>
        public int Mileage { get; set; }

        /// <summary>
        /// Condition (0.0 = broken, 1.0 = perfect)
        /// Affects price and performance
        /// </summary>
        public float Condition { get; set; }

        /// <summary>
        /// Which skin/livery this instance uses
        /// References AC skin folder name
        /// </summary>
        public string SkinId { get; set; } = string.Empty;

        /// <summary>
        /// When this listing was created
        /// </summary>
        public DateTime ListedDate { get; set; }

        /// <summary>
        /// Which dealer location has this car
        /// </summary>
        public string DealerLocation { get; set; } = string.Empty;

        /// <summary>
        /// Whether this listing has been sold
        /// </summary>
        public bool IsSold { get; set; }

        /// <summary>
        /// When this listing was sold (if sold)
        /// </summary>
        public DateTime? SoldDate { get; set; }

        /// <summary>
        /// The parts this very car comes with, as on <see cref="Car.Parts"/>: what is for sale is what gets bought.
        /// Empty on listings from before cars had parts; those get a factory engine when bought.
        /// </summary>
        public List<PartInstance> Parts { get; set; } = [];

        /// <summary>What the seller says about the engine, e.g. "GM 327, 275 hp"; null when there is nothing to say</summary>
        public string? EngineSummary { get; set; }

        /// <summary>Horsepower of the engine on the dyno; null when nobody has put it on one (older listings)</summary>
        public double? PowerHp { get; set; }

        /// <summary>True when the engine is not as it left the factory</summary>
        public bool IsModified { get; set; }

        /// <summary>
        /// True when <see cref="Parts"/> already holds the car's wheels, brakes, springs and shocks (a car that was
        /// somebody's, relisted); the buyer's car must not be given a second factory set then
        /// </summary>
        public bool HasRunningGearAssigned { get; set; }

        /// <summary>
        /// The body's damage per zone, as on <see cref="Car.BodyDamageKmh"/>: a relisted car is sold with its dents.
        /// Null on a car a dealer had from new stock, and on listings from before it was kept: a straight body.
        /// </summary>
        public double[]? BodyDamageKmh { get; set; }

        /// <summary>How used the car looks, as on <see cref="Car"/> (<see cref="Car.Rust"/> and the rest): rolled when it is listed, by who sells it</summary>
        public double Rust { get; set; }

        public double PaintWear { get; set; }

        public double GlassCracks { get; set; }

        public double BodyDirt { get; set; }

        /// <inheritdoc cref="Car.LookSeed"/>
        public int LookSeed { get; set; }

        /// <summary>
        /// The car's history, as on <see cref="Car.History"/>: a relisted car keeps it, and new stock comes with the
        /// owners it had before. Empty on listings from before it was kept.
        /// </summary>
        public CarHistory History { get; set; } = new();

        /// <summary>
        /// The car's own id (<see cref="Car.InstanceId"/>) when somebody had it and it was relisted: whoever buys it gets
        /// that very car back, and a rival who wanted it knows it (<see cref="Grudge.CarInstanceId"/>). Null on new stock
        /// and on listings from before it was kept.
        /// </summary>
        public Guid? CarInstanceId { get; set; }

        /// <summary>
        /// What the car was marked at before its dealer's closing-down sale cut it (<see cref="DealerStatus.ClosingDown"/>);
        /// null when it is not on sale. <see cref="Price"/> is what it sells for.
        /// </summary>
        public decimal? PriceBeforeSale { get; set; }

        /// <summary>
        /// The ad's few words about the car ("Blown with a 6-71. Scary fast."), written once when the car is listed
        /// (<see cref="Services.Market.AdBlurbs"/>); null on listings from before ads had them, written when first shown
        /// </summary>
        public string? Description { get; set; }

        /// <summary>The player's AI rewrote <see cref="Description"/>: it is not offered the ad again</summary>
        public bool AiPolished { get; set; }

        /// <summary>
        /// The price first asked, kept the first time anybody haggles over it: the hard floor is worked out from it
        /// (<c>ai.talk.mostDiscount</c>), so asking again and again, of whoever, never brings it lower. Null until then.
        /// </summary>
        public decimal? FirstAsked { get; set; }

        /// <summary>
        /// The most this seller will come down from <see cref="FirstAsked"/> (0.1: ten percent), drawn once with it: 0 for
        /// one who will not budge, never more than <c>ai.talk.mostDiscount</c>. Kept, so asking again never draws again.
        /// </summary>
        public double? MostOff { get; set; }
    }

    /// <summary>Where a dealer is in its life: open, holding its closing-down sale, gone, or not open yet</summary>
    public enum DealerStatus
    {
        /// <summary>Selling and restocking. First, so the dealers of a save from before this was kept are open.</summary>
        Open = 0,

        /// <summary>The closing-down sale: selling off at a discount, restocking nothing, until <see cref="DealerLocation.ClosesOn"/></summary>
        ClosingDown,

        /// <summary>Gone out of business; the premises may open again, a long while later</summary>
        Closed,

        /// <summary>Not in business yet: opens some day (<see cref="DealerDefinition.StartsOpen"/> false)</summary>
        Waiting
    }

    /// <summary>
    /// A dealer as the save knows it: its name, and how its business is going (see <c>DealerLifeService</c>).
    /// Everything that never changes is the dealer's definition's (<see cref="DealerDefinition"/>).
    /// </summary>
    public class DealerLocation
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string? Specialization { get; set; } // "Imports", "Classics", "Performance", etc.

        public DealerStatus Status { get; set; }

        /// <summary>When it opened during this game; null for a dealer that was there from the start</summary>
        public DateTime? OpenedOn { get; set; }

        /// <summary>The last day of its closing-down sale</summary>
        public DateTime? ClosesOn { get; set; }

        /// <summary>When it went out of business</summary>
        public DateTime? ClosedOn { get; set; }

        /// <summary>
        /// How much it has been selling lately: one for every car sold off the lot, fading day by day
        /// (<c>DealerRules.TradeFade</c>). A lot that sells nothing is the one that goes under.
        /// </summary>
        public double Trade { get; set; }

        /// <summary>Up to when <see cref="Trade"/> has counted the sales</summary>
        public DateTime? TradeCountedTo { get; set; }

        /// <summary>Selling cars: open, or in its closing-down sale</summary>
        [LiteDB.BsonIgnore]
        public bool IsTrading => Status is DealerStatus.Open or DealerStatus.ClosingDown;
    }
}
