using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Models.Career.Filters
{
    /// <summary>
    /// Filter that matches cars by value/price range.
    /// Requires a car instance. The car is valued by the valuation the filter service passes in (what the
    /// car is worth now, the same figure the market uses); without one, by what was paid for it.
    /// Example: "Budget builds under $5000", "High-roller specials"
    /// </summary>
    public class ValueFilter : ICarFilter
    {
        public string FilterType => "Value";

        /// <summary>
        /// Minimum value (inclusive). Null means no minimum.
        /// </summary>
        public decimal? MinValue { get; set; }

        /// <summary>
        /// Maximum value (inclusive). Null means no maximum.
        /// </summary>
        public decimal? MaxValue { get; set; }

        public string DisplayDescription
        {
            get
            {
                if (MinValue.HasValue && MaxValue.HasValue)
                    return GameText.Say("event.cars.value", ("min", MinValue), ("max", MaxValue));
                if (MinValue.HasValue)
                    return GameText.Say("event.cars.value.min", ("min", MinValue));
                if (MaxValue.HasValue)
                    return GameText.Say("event.cars.value.max", ("max", MaxValue));
                return GameText.Say("event.cars.any_value");
            }
        }

        public ValueFilter() { }

        public ValueFilter(decimal? minValue, decimal? maxValue)
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public bool Matches(CarDefinition car, Car? instance = null) => Matches(car, instance, null);

        public bool Matches(CarDefinition car, Car? instance, Func<Car, decimal>? valueOf)
        {
            // Value filter requires an instance to value
            if (instance == null)
                return false;

            var value = valueOf?.Invoke(instance) ?? instance.PurchasePrice;

            if (MinValue.HasValue && value < MinValue.Value)
                return false;

            if (MaxValue.HasValue && value > MaxValue.Value)
                return false;

            return true;
        }
    }
}
