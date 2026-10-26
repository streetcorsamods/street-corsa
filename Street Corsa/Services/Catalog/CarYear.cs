using System.Text.RegularExpressions;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>
    /// A car's model year: the one the mods' cars.json gives it, else its ui_car.json's, else one its name or folder
    /// name says ("Ford Mustang 2015", "camaro_1969"). Half the cars of an install give no year (Kunos' own among them),
    /// and a setting that takes cars by their years would otherwise have none of them. Worked out when asked, never
    /// stored: cars.json is the mods', and catalog.db keeps what the car's own files say.
    /// </summary>
    public static partial class CarYear
    {
        /// <summary>The model year; null when nothing tells it</summary>
        public static int? Of(string carId, string? name, int? uiYear, CarBook? book = null) =>
            (book ?? CarBook.Current).Car(carId)?.Year ?? uiYear ?? FromName(name) ?? FromName(carId);

        /// <summary>
        /// A year written whole in a name, on its own (not "2000GT"), 1920 to 2039 (an Alfa Romeo 1900 is no 1900 car);
        /// the last one when there are more
        /// </summary>
        public static int? FromName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var found = YearWord().Matches(name);
            return found.Count == 0 ? null : int.Parse(found[^1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        [GeneratedRegex(@"(?<![\p{L}\d])(19[2-9]\d|20[0-3]\d)(?![\p{L}\d])")]
        private static partial Regex YearWord();
    }
}
