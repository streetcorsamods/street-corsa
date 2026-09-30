using System.Windows;

namespace StreetCorsa.Helpers;

/// <summary>The glass a button's hover fades in over it (<c>GlassButtonTemplate</c> in Buttons.xaml); each style sets its own</summary>
public static class GlassButton
{
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.RegisterAttached(
        "HoverBrush", typeof(System.Windows.Media.Brush), typeof(GlassButton), new PropertyMetadata(null));

    public static System.Windows.Media.Brush? GetHoverBrush(DependencyObject element) => (System.Windows.Media.Brush?)element.GetValue(HoverBrushProperty);
    public static void SetHoverBrush(DependencyObject element, System.Windows.Media.Brush? value) => element.SetValue(HoverBrushProperty, value);
}
