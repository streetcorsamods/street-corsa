using LiteDB;

namespace StreetCorsa.Models.Catalog
{
    /// <summary>
    /// Represents gameplay properties for a car.
    /// This is the gameplay overlay on top of immutable CarDefinition.
    /// Stored in catalog.db, NOT in AC installation directories.
    /// </summary>
    public class CarProfile
    {
        /// <summary>
        /// References CarDefinition.Id (stable AC folder name)
        /// </summary>
        [BsonId]
        public string CarDefinitionId { get; set; } = string.Empty;

        /// <summary>
        /// What the car costs new, in perfect condition with its factory engine and running gear (the parts' new prices
        /// and its body's, rounded and held to the rules' least and most). Worked out by the game at every start; market
        /// listings vary from it by condition.
        /// </summary>
        public decimal BasePrice { get; set; }

        /// <summary>What the bare body costs new: by the car's weight (<see cref="StreetCorsa.Parts.Cars.CarPricing"/>)</summary>
        public decimal BodyPrice { get; set; }

        /// <summary>What the factory engine's parts cost new; 0 for a car priced without parts</summary>
        public decimal EnginePrice { get; set; }

        /// <summary>What the factory running gear's parts cost new; 0 for a car priced without parts</summary>
        public decimal RunningGearPrice { get; set; }

        /// <summary>
        /// Spawn probability (0.0 = rare, 1.0 = common)
        /// Controls how often this car appears in used car market
        /// </summary>
        public float DealerPrecedence { get; set; }

        /// <summary>
        /// How this profile was created
        /// </summary>
        public ProfileDataSource Source { get; set; } = ProfileDataSource.Generated;

        /// <summary>
        /// The CarDefinition.ContentHash a Generated profile was worked out from. When the car's ui_car.json (or
        /// its skins) change, the hash moves on and the price and precedence are worked out again; a Manual
        /// profile is left as it is. Null on profiles from before it was kept: those are worked out again once.
        /// </summary>
        public string? DefinitionHash { get; set; }

        /// <summary>
        /// When this profile was created
        /// </summary>
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// When this profile was last modified
        /// </summary>
        public DateTime LastUpdatedDate { get; set; }

        /// <summary>
        /// The engine the car leaves the factory with: id of an engine build of the parts catalog
        /// (engine_builds.json). Suggested from the car's make, name and power; null until that has happened
        /// or when no build comes close.
        /// </summary>
        public string? StockEngineBuildId { get; set; }

        /// <summary>True when somebody picked the engine by hand; suggestions leave it alone then</summary>
        public bool StockEngineIsManual { get; set; }

        /// <summary>
        /// Where the car carries its engine, set by hand in the catalog editor; null to go by the guess from the car's
        /// data (<see cref="StreetCorsa.Parts.EngineLayouts.Guess"/>)
        /// </summary>
        public StreetCorsa.Parts.EngineLayout? EngineLayout { get; set; }

        // Future enrichment properties (optional, not used initially)
        public string? PerformanceTier { get; set; }
        public bool IsStreetLegal { get; set; } = true;
    }

    /// <summary>
    /// Indicates how the profile data was created
    /// </summary>
    public enum ProfileDataSource
    {
        Generated,  // Algorithm-generated defaults
        Manual,     // User/curator edited
        Imported    // From external data pack (future)
    }
}
