using System.Windows;
using System.Windows.Media;

namespace StreetCorsa.Helpers;

/// <summary>What the game's inputs (Inputs.xaml) show beyond their value, kept off Tag so Tag stays free for data</summary>
public static class GlassInput
{
    /// <summary>The hint an empty text box shows</summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached(
        "Hint", typeof(string), typeof(GlassInput), new PropertyMetadata(null));

    public static string? GetHint(DependencyObject element) => (string?)element.GetValue(HintProperty);
    public static void SetHint(DependencyObject element, string? value) => element.SetValue(HintProperty, value);

    /// <summary>A tick box's or radio button's mark, drawn in a 16 px box: a tick, or a dot</summary>
    public static readonly DependencyProperty MarkProperty = DependencyProperty.RegisterAttached(
        "Mark", typeof(Geometry), typeof(GlassInput), new PropertyMetadata(null));

    public static Geometry? GetMark(DependencyObject element) => (Geometry?)element.GetValue(MarkProperty);
    public static void SetMark(DependencyObject element, Geometry? value) => element.SetValue(MarkProperty, value);
}
