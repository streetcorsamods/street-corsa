using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Models.Career.Filters
{
    /// <summary>
    /// Interface for car entry filters used in events and race requirements.
    /// Filters can match against car definitions (static catalog data) and
    /// optionally car instances (player-owned cars with condition/mods).
    /// </summary>
    public interface ICarFilter
    {
        /// <summary>
        /// Unique type identifier for serialization
        /// </summary>
        string FilterType { get; }

        /// <summary>
        /// Human-readable description of what this filter requires
        /// </summary>
        string DisplayDescription { get; }

        /// <summary>
        /// Check if a car matches this filter's criteria
        /// </summary>
        /// <param name="car">The car definition from the catalog</param>
        /// <param name="instance">Optional car instance for condition/value checks</param>
        /// <returns>True if the car matches the filter criteria</returns>
        bool Matches(CarDefinition car, Car? instance = null);

        /// <summary>
        /// Check a car with a way to value it: <paramref name="valueOf"/> is what the car is worth
        /// (<c>CarValuation</c> through the market). Filters that don't look at worth ignore it.
        /// </summary>
        bool Matches(CarDefinition car, Car? instance, Func<Car, decimal>? valueOf) => Matches(car, instance);

        /// <summary>
        /// Check a car on offer as the market shows it, with or without the car itself (a lot's car, a rival's ad, a
        /// model a lot means to stock): <paramref name="price"/> is what it is worth and <paramref name="powerHp"/>,
        /// when known, its power. Filters that look at neither ignore them.
        /// </summary>
        bool MatchesOffer(CarDefinition car, Car? instance, decimal price, double? powerHp) => Matches(car, instance);
    }
}
