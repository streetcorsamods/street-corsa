using System.Globalization;
using System.Windows.Data;
using StreetCorsa.Helpers;

namespace StreetCorsa.Converters
{
    /// <summary>A sum of money as the player reads it, in the game's currency (<see cref="Money"/>): "$1,234"</summary>
    public class MoneyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                return value == null ? string.Empty : Money.Text(System.Convert.ToDecimal(value, CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                return string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }
}
