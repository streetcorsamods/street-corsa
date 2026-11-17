using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>The showroom layout's strip of cards: paged by its arrows and the wheel, the picked card kept in sight</summary>
    public partial class GarageScreenView
    {
        /// <summary>How much of the strip's width an arrow scrolls it</summary>
        private const double StripPage = 0.8;

        private ScrollViewer? _stripScroller;

        private ScrollViewer? StripScroller => _stripScroller ??= FindScroller(StripList);

        private void OnStripLeft(object sender, RoutedEventArgs e) => ScrollStrip(-1);

        private void OnStripRight(object sender, RoutedEventArgs e) => ScrollStrip(1);

        private void ScrollStrip(int direction)
        {
            if (StripScroller is not { } scroller) return;
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + direction * scroller.ViewportWidth * StripPage);
        }

        /// <summary>The wheel runs the strip along, as it has no rows to go down</summary>
        private void OnStripWheel(object sender, MouseWheelEventArgs e)
        {
            if (StripScroller is not { } scroller) return;
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        /// <summary>The card picked (by a click, or the car in the garage as the screen opens) is scrolled into sight</summary>
        private void OnStripPicked(object sender, SelectionChangedEventArgs e)
        {
            if (StripList.SelectedItem is { } card) StripList.ScrollIntoView(card);
        }

        private static ScrollViewer? FindScroller(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer scroller) return scroller;
                if (FindScroller(child) is { } found) return found;
            }

            return null;
        }
    }
}
