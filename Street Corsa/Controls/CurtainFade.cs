using System.Windows;
using System.Windows.Media.Animation;

namespace StreetCorsa.Controls;

/// <summary>A black curtain over a screen, brought down or lifted by its opacity (the gas station, the lot's drive-off)</summary>
public static class CurtainFade
{
    // An animation taken off, or one on an element not shown, never says it is done: the wait ends this long after
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Fades <paramref name="curtain"/> to <paramref name="opacity"/> over <paramref name="duration"/>. Done when it is
    /// there, and never left waiting on an animation that will not finish (the screen gone, or the fade cut short).
    /// </summary>
    public static Task ToAsync(UIElement curtain, double opacity, TimeSpan duration, IEasingFunction? easing = null)
    {
        var done = new TaskCompletionSource();
        var animation = new DoubleAnimation(opacity, duration) { EasingFunction = easing };
        animation.Completed += (_, _) => done.TrySetResult();
        curtain.BeginAnimation(UIElement.OpacityProperty, animation);

        // Not shown (the window gone): no animation will finish
        if (curtain is FrameworkElement { IsLoaded: false }) done.TrySetResult();
        return Task.WhenAny(done.Task, Task.Delay(duration + Grace));
    }
}
