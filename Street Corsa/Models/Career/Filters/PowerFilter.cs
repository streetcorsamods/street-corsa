using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Text;

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
                    return GameText.Say("event.cars.power", ("min", MinHP), ("max", MaxHP));
                if (MinHP.HasValue)
                    return GameText.Say("event.cars.power.min", ("min", MinHP));
                if (MaxHP.HasValue)
                    return GameText.Say("event.cars.power.max", ("max", MaxHP));
                return GameText.Say("event.cars.any_power");
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
            // The car's own dyno figure when it has one (an engine swap counts), else its spec sheet's power; a car
            // whose engine does not run has none, as the race simulator sees it
            return InRange(CarPower.Racing(instance, car));
        }

        /// <summary>A car on offer has the power quoted for it; with none quoted, as <see cref="Matches(CarDefinition, Car?)"/> works it out</summary>
        public bool MatchesOffer(CarDefinition car, Car? instance, decimal price, double? powerHp) =>
            InRange(powerHp ?? CarPower.Racing(instance, car));

        private bool InRange(double? bhp)
        {
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
