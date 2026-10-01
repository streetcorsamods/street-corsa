using System.ComponentModel;
using StreetCorsa.Audio.Radio;

namespace StreetCorsa.Audio;

/// <summary>
/// The music the game starts to (the cue <c>music.intro</c>, a loop): from the intro on, over the main screen, until
/// the player is in a game, puts the radio on or opens the sound library (to hear sounds on their own), whichever
/// comes first. Once a session: back at the main screen from a game, the radio is what plays (or nothing, if it is
/// off). A game with no such cue starts quiet.
/// </summary>
public static class StartMusic
{
    public const string Cue = "music.intro";

    private static bool _begun;
    private static bool _over;

    /// <summary>The music on, the first time only. UI thread.</summary>
    public static void Begin()
    {
        if (_begun || _over) return;
        _begun = true;

        // The radio wins: the player putting it on ends the music
        if (RadioPlayer.Shared.IsOn)
        {
            _over = true;
            return;
        }

        RadioPlayer.Shared.PropertyChanged += OnRadioChanged;
        AudioHub.Shared.SetMusic(Cue);
    }

    /// <summary>The music fades out, for the rest of the session. UI thread.</summary>
    public static void End()
    {
        if (_over) return;
        _over = true;
        RadioPlayer.Shared.PropertyChanged -= OnRadioChanged;
        AudioHub.Shared.SetMusic(null);
    }

    private static void OnRadioChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RadioPlayer.IsOn) && RadioPlayer.Shared.IsOn) End();
    }
}
