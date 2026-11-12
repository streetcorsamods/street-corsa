using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using StreetCorsa.Styles;

namespace StreetCorsa.Converters
{
    /// <summary>
    /// A colour a mod gives as text ("#RRGGBB", a crew's) as a brush: one frozen brush per colour, shared by every row.
    /// None, one that does not read, or one too dark to read on the dark cards (<see cref="MinLightness"/>) is the
    /// <see cref="Fallback"/>, set where the converter is declared, so the view keeps its colours and the view model
    /// gives only the mod's text.
    /// Usage: <c>&lt;converters:ColourToBrushConverter x:Key="..." Fallback="..."/&gt;</c>
    /// </summary>
    public sealed class ColourToBrushConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, SolidColorBrush?> BrushByColour = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>For no colour, one that does not read, or one too dark; unset, the property's own value</summary>
        public Brush? Fallback { get; set; }

        /// <summary>The least relative luminance (0 black to 1 white) a colour needs to be shown as it is</summary>
        public double MinLightness { get; set; } = 0.08;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            BrushOf(value as string, MinLightness) ?? (object?)Fallback ?? System.Windows.DependencyProperty.UnsetValue;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        /// <summary>The brush of <paramref name="colour"/>; null for none, one that does not read, or one darker than <paramref name="minLightness"/></summary>
        public static Brush? BrushOf(string? colour, double minLightness = 0)
        {
            if (string.IsNullOrWhiteSpace(colour)) return null;
            var brush = BrushByColour.GetOrAdd(colour.Trim(), Parse);
            return brush != null && Lightness(brush.Color) >= minLightness ? brush : null;
        }

        /// <summary>The colour's relative luminance, as WCAG works it out</summary>
        public static double Lightness(Color colour)
        {
            static double Channel(byte c)
            {
                var s = c / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(colour.R) + 0.7152 * Channel(colour.G) + 0.0722 * Channel(colour.B);
        }

        private static SolidColorBrush? Parse(string colour)
        {
            if (Colours.TryParse(colour) is not { } color) return null;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
