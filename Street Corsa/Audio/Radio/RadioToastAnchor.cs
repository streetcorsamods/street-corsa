using System.Windows;

namespace StreetCorsa.Audio.Radio;

/// <summary>
/// Marks a screen's radio button (<c>radio:RadioToastAnchor.IsAnchor="True"</c>): the "now playing" note comes out of it
/// while it is on screen. The one loaded last counts; weakly held, so a screen left behind is not kept for it.
/// </summary>
public static class RadioToastAnchor
{
    private static WeakReference<FrameworkElement>? _current;

    public static readonly DependencyProperty IsAnchorProperty = DependencyProperty.RegisterAttached(
        "IsAnchor", typeof(bool), typeof(RadioToastAnchor), new PropertyMetadata(false, OnIsAnchorChanged));

    public static bool GetIsAnchor(DependencyObject element) => (bool)element.GetValue(IsAnchorProperty);
    public static void SetIsAnchor(DependencyObject element, bool value) => element.SetValue(IsAnchorProperty, value);

    /// <summary>The radio button on show, if there is one</summary>
    public static FrameworkElement? Current =>
        _current?.TryGetTarget(out var element) == true && element.IsLoaded && element.IsVisible ? element : null;

    private static void OnIsAnchorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        element.Loaded -= OnLoaded;
        element.Unloaded -= OnUnloaded;
        if (!(bool)e.NewValue)
        {
            Forget(element);
            return;
        }

        element.Loaded += OnLoaded;
        element.Unloaded += OnUnloaded;
        if (element.IsLoaded) _current = new WeakReference<FrameworkElement>(element);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => _current = new WeakReference<FrameworkElement>((FrameworkElement)sender);

    private static void OnUnloaded(object sender, RoutedEventArgs e) => Forget((FrameworkElement)sender);

    private static void Forget(FrameworkElement element)
    {
        if (_current?.TryGetTarget(out var current) == true && ReferenceEquals(current, element)) _current = null;
    }
}
