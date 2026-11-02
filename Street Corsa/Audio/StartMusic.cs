using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using StreetCorsa.Audio.Radio;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

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

    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Music");

    private static bool _begun;
    private static bool _over;
    private static Dispatcher? _dispatcher;

    /// <summary>
    /// Who made the music while it plays (the main screen's badge): null before the cues are read, once it is over, or
    /// when nobody is named. UI thread.
    /// </summary>
    public static SoundFiles.Maker? PlayingMaker { get; private set; }

    /// <summary><see cref="PlayingMaker"/> changed: the cues came in, or the music ended. On the UI thread.</summary>
    public static event EventHandler? PlayingMakerChanged;

    /// <summary>
    /// Who made the music, as the credits.csv beside its file says: the maker of the cue's first file, looked for in
    /// the sfx folder of the mod it is in; null when the game has no such cue or no line names its maker
    /// </summary>
    public static SoundFiles.Maker? Maker(ModSet mods, SoundCues cues)
    {
        if (cues.Get(Cue)?.Files is not [var file, ..]) return null;

        var full = Path.GetFullPath(file);
        var sfx = mods.Folders(ContentTypes.Sfx)
            .Select(f => Path.GetFullPath(f.Dir).TrimEnd(Path.DirectorySeparatorChar))
            .LastOrDefault(dir => full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        return sfx == null ? null : SoundFiles.MakerOf(full, sfx);
    }

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

        _dispatcher = Dispatcher.CurrentDispatcher;
        RadioPlayer.Shared.PropertyChanged += OnRadioChanged;
        AudioHub.Shared.CuesChanged += OnCuesChanged;
        AudioHub.Shared.SetMusic(Cue);

        // Read already (a session that started the hub before): the maker is known now
        if (AudioHub.Shared.Cues.Get(Cue) != null) SetMaker(MakerOrNull(AudioHub.Shared.Cues));
    }

    /// <summary>The music fades out, for the rest of the session. UI thread.</summary>
    public static void End()
    {
        if (_over) return;
        _over = true;
        RadioPlayer.Shared.PropertyChanged -= OnRadioChanged;
        AudioHub.Shared.CuesChanged -= OnCuesChanged;
        AudioHub.Shared.SetMusic(null);
        SetMaker(null);
    }

    private static void OnRadioChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RadioPlayer.IsOn) && RadioPlayer.Shared.IsOn) End();
    }

    /// <summary>On the hub's worker: the credits are read there, and the maker handed to the UI thread</summary>
    private static void OnCuesChanged(SoundCues cues)
    {
        var maker = MakerOrNull(cues);
        _dispatcher?.BeginInvoke(() =>
        {
            if (!_over) SetMaker(maker);
        });
    }

    /// <summary>The maker; null when the credits cannot be read (logged): a credits line never stops the game</summary>
    private static SoundFiles.Maker? MakerOrNull(SoundCues cues)
    {
        try
        {
            return Maker(ModSet.Installed, cues);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or ArgumentException or NotSupportedException)
        {
            Logger.Warning(ex, "Could not find who made the music; the main screen names nobody");
            return null;
        }
    }

    private static void SetMaker(SoundFiles.Maker? maker)
    {
        if (Equals(PlayingMaker, maker)) return;
        PlayingMaker = maker;
        PlayingMakerChanged?.Invoke(null, EventArgs.Empty);
    }
}
