using System.Windows;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace StreetCorsa.Audio;

/// <summary>
/// The game's sound effects by name (the cues in the mods' sfx folders), and the click every button makes. A button
/// with a sound of its own names it: <c>audio:Sfx.Click="ui.lights"</c>; an empty name keeps it quiet.
/// </summary>
public static class Sfx
{
    public const string Click = "ui.click";
    public const string Lights = "ui.lights";
    public const string Purchase = "ui.purchase";

    public const string Ratchet = "part.ratchet";
    public const string Impact = "part.impact";
    public const string Clank = "part.clank";
    public const string Hoist = "engine.hoist";
    public const string EngineLands = "engine.land";
    public const string EngineTurns = "engine.turn";

    // The driveway bell as a car rolls over its hose: a step of the station's arrive script, and rung by the game when
    // no script shows the car driving in
    public const string StationBell = "station.bell";

    // The pump as the tank fills: the nozzle going in, the flow (a loop), the click as it shuts off
    public const string PumpStart = "station.pump.start";
    public const string Pump = "station.pump";
    public const string PumpStop = "station.pump.stop";

    /// <summary>
    /// The cue of the tool a part is heard to come off and go on with, as its work-order kind says
    /// (<see cref="Parts.WorkOrder.ToolOf"/>): the air gun for <c>"impact"</c>, else the socket wrench
    /// </summary>
    public static string ToolFor(string? tool) => string.Equals(tool, "impact", StringComparison.OrdinalIgnoreCase) ? Impact : Ratchet;

    /// <summary>Plays a cue, after <paramref name="delaySeconds"/> when given; nothing when there is no such cue</summary>
    public static void Play(string cue, float delaySeconds = 0f) => AudioHub.Shared.PlayCue(cue, delaySeconds);

    public static readonly DependencyProperty ClickProperty = DependencyProperty.RegisterAttached(
        "Click", typeof(string), typeof(Sfx), new PropertyMetadata(null));

    public static string? GetClick(DependencyObject element) => (string?)element.GetValue(ClickProperty);

    public static void SetClick(DependencyObject element, string? value) => element.SetValue(ClickProperty, value);

    private static bool _registered;

    /// <summary>Every button's click, app-wide: a class handler, so buttons of any style (and those without one) click</summary>
    public static void RegisterClicks()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick), handledEventsToo: true);
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        // A click bubbles through the button only once, but a class handler hears it for every ButtonBase it passes
        if (!ReferenceEquals(sender, e.OriginalSource) || sender is not DependencyObject button) return;

        var cue = GetClick(button) ?? Click;
        if (cue.Length > 0) Play(cue);
    }
}
