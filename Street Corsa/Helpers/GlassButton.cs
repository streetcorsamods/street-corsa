using System.Windows;

namespace StreetCorsa.Helpers;

/// <summary>The glass a button's hover fades in over it (<c>GlassButtonTemplate</c> in Buttons.xaml); each style sets its own</summary>
public static class GlassButton
{
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.RegisterAttached(
        "HoverBrush", typeof(System.Windows.Media.Brush), typeof(GlassButton), new PropertyMetadata(null));

    public static System.Windows.Media.Brush? GetHoverBrush(DependencyObject element) => (System.Windows.Media.Brush?)element.GetValue(HoverBrushProperty);
    public static void SetHoverBrush(DependencyObject element, System.Windows.Media.Brush? value) => element.SetValue(HoverBrushProperty, value);

    /// <summary>
    /// A round icon button (<c>RoundIconButtonStyle</c>) lit as a pushed-in toggle is: for a button that shows a state it
    /// does not set itself (the lift's, lit while the car is up, each click a step along)
    /// </summary>
    public static readonly DependencyProperty IsLitProperty = DependencyProperty.RegisterAttached(
        "IsLit", typeof(bool), typeof(GlassButton), new PropertyMetadata(false));

    public static bool GetIsLit(DependencyObject element) => (bool)element.GetValue(IsLitProperty);
    public static void SetIsLit(DependencyObject element, bool value) => element.SetValue(IsLitProperty, value);
}
