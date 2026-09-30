using System.Windows;

namespace StreetCorsa.Helpers;

/// <summary>
/// The room a dialog has inside its card on this window, inherited by the dialog's view (the main window sets it). A
/// big dialog that sizes its own lists binds its MaxWidth and MaxHeight to it and fits a small or high-DPI screen
/// whole; the card scrolls only a dialog that grows with its content. Unbounded outside the dialog host.
/// </summary>
public static class DialogRoom
{
    public static readonly DependencyProperty WidthProperty = DependencyProperty.RegisterAttached(
        "Width", typeof(double), typeof(DialogRoom),
        new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty HeightProperty = DependencyProperty.RegisterAttached(
        "Height", typeof(double), typeof(DialogRoom),
        new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.Inherits));

    public static double GetWidth(DependencyObject element) => (double)element.GetValue(WidthProperty);
    public static void SetWidth(DependencyObject element, double value) => element.SetValue(WidthProperty, value);

    public static double GetHeight(DependencyObject element) => (double)element.GetValue(HeightProperty);
    public static void SetHeight(DependencyObject element, double value) => element.SetValue(HeightProperty, value);
}
