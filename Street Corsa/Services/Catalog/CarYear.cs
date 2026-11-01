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
        public static int? Of(string carId, string? name, string? brand, int? uiYear, CarBook? book = null) =>
            (book ?? CarBook.Current).Car(carId)?.Year
            ?? (uiYear is >= CarBook.MinYear and <= CarBook.MaxYear ? uiYear : null)
            ?? FromName(name, brand) ?? FromName(carId, brand);

        /// <summary>
        /// A year written whole in a name, as a word of its own (not "2000GT"), 1920 to 2039 (an Alfa Romeo 1900 is no
        /// 1900 car), the last one when there are more. A number right after the make is the model's, not a year
        /// ("BMW 2002 Turbo", "Triumph 2000", "datsun_2000_roadster"); one first in the name is a year only before the
        /// make ("1969 Chevrolet Camaro", not "2002 Turbo").
        /// </summary>
        /// <param name="brand">The car's make as its ui_car.json writes it; none, and a number first is taken as a year</param>
        public static int? FromName(string? name, string? brand = null)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var words = Word().Matches(name).Select(m => m.Value).ToList();
            var makeWords = string.IsNullOrEmpty(brand)
                ? new HashSet<string>()
                : Word().Matches(brand).Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var at = words.Count - 1; at >= 0; at--)
            {
                if (!YearWord().IsMatch(words[at])) continue;
                var isYear = at == 0
                    ? makeWords.Count == 0 || (words.Count > 1 && makeWords.Contains(words[1]))
                    : !makeWords.Contains(words[at - 1]);
                if (isYear) return int.Parse(words[at], System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }

        // A word: letters and digits of any script, so a year next to any of them is no word of its own
        [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
        private static partial Regex Word();

        [GeneratedRegex("^(19[2-9][0-9]|20[0-3][0-9])$")]
        private static partial Regex YearWord();
    }
}
