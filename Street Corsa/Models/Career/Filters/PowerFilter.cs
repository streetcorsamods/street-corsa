using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Models.Career.Filters
{
    /// <summary>
    /// Filter that matches cars by horsepower range.
    /// Example: "Under 200hp", "300+ HP monsters"
    /// </summary>
    public class PowerFilter : ICarFilter
    {
        public string FilterType => "Power";

        /// <summary>
        /// Minimum horsepower (inclusive). Null means no minimum.
        /// </summary>
        public int? MinHP { get; set; }

        /// <summary>
        /// Maximum horsepower (inclusive). Null means no maximum.
        /// </summary>
        public int? MaxHP { get; set; }

        public string DisplayDescription
        {
            get
            {
                if (MinHP.HasValue && MaxHP.HasValue)
                    return $"{MinHP}-{MaxHP} HP";
                if (MinHP.HasValue)
                    return $"{MinHP}+ HP";
                if (MaxHP.HasValue)
                    return $"Under {MaxHP} HP";
                return "Any power";
            }
        }

        public PowerFilter() { }

        public PowerFilter(int? minHP, int? maxHP)
        {
            MinHP = minHP;
            MaxHP = maxHP;
        }

        public bool Matches(CarDefinition car, Car? instance = null)
        {
            // The car's own dyno figure when it has one (an engine swap counts), else its spec sheet's power
            var bhp = CarPower.Of(instance, car);
            if (!bhp.HasValue)
                return false; // Cars without power data don't match power-based filters

            if (MinHP.HasValue && bhp.Value < MinHP.Value)
                return false;

            if (MaxHP.HasValue && bhp.Value > MaxHP.Value)
                return false;

            return true;
        }
    }
}
