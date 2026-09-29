using System.Globalization;
using System.Windows.Data;

namespace StreetCorsa.Converters
{
    /// <summary>
    /// Converts a percentage (0-100) and a container width to a pixel width.
    /// Used for progress bars.
    /// </summary>
    public class PercentageToWidthConverter : IMultiValueConverter
    {
        /// <summary>What the bar's border takes off the container's width</summary>
        private const double BorderMargin = 4;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // First value is the percentage (a float or a double), second the container's width
            if (values.Length < 2 || values[1] is not double containerWidth)
                return 0.0;

            double? percentage = values[0] switch
            {
                float f => f,
                double d => d,
                _ => null
            };
            if (percentage is not { } share || !double.IsFinite(share) || !double.IsFinite(containerWidth))
                return 0.0;

            // Held to the bar: under 0 or over 100 would draw it outside its border
            return Math.Clamp(share, 0, 100) / 100.0 * Math.Max(0, containerWidth - BorderMargin);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
