using StreetCorsa.Models.Career.Filters;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Career
{
    /// <summary>
    /// Service for evaluating car filters and finding matching cars
    /// </summary>
    public interface ICarFilterService
    {
        /// <summary>
        /// Check if a specific car matches a filter
        /// </summary>
        bool Matches(ICarFilter filter, CarDefinition car, Car? instance = null);

        /// <summary>
        /// Find all cars in a collection that match a filter
        /// </summary>
        IEnumerable<Car> FindMatchingCars(ICarFilter filter, IEnumerable<Car> cars,
            Func<string, CarDefinition?> definitionLookup);

        /// <summary>
        /// Check if any car in a collection matches the filter
        /// </summary>
        bool HasMatchingCar(ICarFilter filter, IEnumerable<Car> cars,
            Func<string, CarDefinition?> definitionLookup);
    }
}
