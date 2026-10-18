using StreetCorsa.Models.Catalog;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>
    /// Service for generating and managing car gameplay profiles
    /// </summary>
    public interface ICarProfileService
    {
        /// <summary>
        /// Generates a default profile for a car definition
        /// Uses power/weight ratio and other specs to calculate price/precedence
        /// </summary>
        CarProfile GenerateDefaultProfile(CarDefinition carDefinition);

        /// <summary>
        /// Calculates base price from car specifications
        /// </summary>
        decimal CalculateBasePrice(CarDefinition carDefinition);

        /// <summary>
        /// Calculates dealer precedence (spawn probability)
        /// </summary>
        float CalculatePrecedence(CarDefinition carDefinition);

        /// <summary>
        /// Ensures all car definitions have profiles, creating missing ones (priced by the body alone: the parts are
        /// not up yet at start-up)
        /// </summary>
        Task EnsureProfilesExistAsync();

        /// <summary>
        /// Prices every active car again at its parts' prices (its factory engine and running gear; the body alone
        /// without parts), once the parts are up. Never throws: a failure is logged and the prices stay as they were.
        /// </summary>
        Task RepriceAsync();
    }
}
