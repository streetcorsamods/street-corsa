using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Models.Career.Filters
{
    /// <summary>
    /// Filter that matches cars by country/region of origin.
    /// Example: "American muscle", "Japanese imports", "European sports"
    /// </summary>
    public class OriginFilter : ICarFilter
    {
        public string FilterType => "Origin";

        /// <summary>
        /// Country or region to match (case-insensitive).
        /// Supports both country names ("USA", "Japan") and region aliases ("American", "Japanese").
        /// </summary>
        public string Origin { get; set; } = string.Empty;

        public string DisplayDescription => GameText.Say("event.cars.origin", ("origin", Origin));

        public OriginFilter() { }

        public OriginFilter(string origin)
        {
            Origin = origin;
        }

        /// <summary>
        /// The car's country (its ui_car.json's, else its make's, as the mods' makes say) is the origin, or in it: a
        /// country by any of its names ("American", "Great Britain"), or a region ("European")
        /// </summary>
        public bool Matches(CarDefinition car, Car? instance = null)
        {
            if (string.IsNullOrEmpty(Origin))
                return true;

            var book = CarBook.Current;
            var country = !string.IsNullOrEmpty(car.Country) ? car.Country : book.CountryOfMake(book.MakeOf(car.Id, car.Brand, car.Name));
            return country != null && book.IsIn(country, Origin);
        }
    }
}
