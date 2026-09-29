using StreetCorsa.Services.Settings;

namespace StreetCorsa.Audio;

/// <summary>The kinds of sound, each with its own slider; all of them under the master one</summary>
public enum AudioBus
{
    Engine,
    Effects,
    Interface,
    Ambience,
    Radio
}

/// <summary>
/// The arithmetic of the mix, apart from FMOD so it can be tested: what a slider means as a gain, and how the fade
/// (to the background, into a race) moves.
/// </summary>
public static class AudioMix
{
    /// <summary>The range a slider covers: its bottom notch above silence is this far down</summary>
    public const double RangeDb = 30.0;

    /// <summary>How long a fade from full to silence takes, or back</summary>
    public static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(0.35);

    /// <summary>
    /// A slider (0..1) as an amplitude. The ear hears in decibels: a slider straight onto the amplitude does all its
    /// work in the last tenth of its travel, so it runs over <see cref="RangeDb"/> instead, and 0 is silence.
    /// </summary>
    public static float Amplitude(double slider)
    {
        if (double.IsNaN(slider) || slider <= 0.0) return 0f;
        if (slider >= 1.0) return 1f;
        return (float)Math.Pow(10.0, (slider - 1.0) * RangeDb / 20.0);
    }

    /// <summary>What <paramref name="bus"/> plays at: its slider under the master one, and the fade over both</summary>
    public static float Gain(AudioSettings settings, AudioBus bus, float fade) =>
        Amplitude(settings.Master) * Amplitude(settings.For(bus)) * Math.Clamp(fade, 0f, 1f);

    /// <summary>The fade one step of <paramref name="elapsed"/> on from <paramref name="fade"/> towards <paramref name="target"/>, at a steady rate</summary>
    public static float StepFade(float fade, float target, TimeSpan elapsed)
    {
        var step = (float)(elapsed.TotalSeconds / FadeTime.TotalSeconds);
        if (step <= 0f) return fade;
        return fade < target ? Math.Min(target, fade + step) : Math.Max(target, fade - step);
    }

    /// <summary>
    /// Where the fade is headed: silent in a race (Assetto Corsa has the sound device then) and, when the player asks
    /// for it, while another window has the focus
    /// </summary>
    public static float FadeTarget(bool racing, bool focused, bool muteInBackground) =>
        racing || (!focused && muteInBackground) ? 0f : 1f;

    /// <summary>
    /// Where the radio's fade is headed: as the rest behind another window, but heard in a race, where AC has the focus
    /// and the player may put it on (it is off unless they do)
    /// </summary>
    public static float RadioFadeTarget(bool racing, bool focused, bool muteInBackground) =>
        !racing && !focused && muteInBackground ? 0f : 1f;
}
