using System.IO;
using StreetCorsa.Mods;
using static StreetCorsa.Audio.FmodStudio;

namespace StreetCorsa.Audio;

/// <summary>
/// The cues (<see cref="SoundCues"/>): one-shots out of memory, each file decoded once and kept for the session; the
/// place's air, a loop that crossfades into the next place's as the player goes there; and the music the game starts
/// to, a loop of its own.
/// </summary>
public sealed partial class AudioHub
{
    // How long a place's air takes to come in, and the last one to go; the music the same, slower
    private const float AmbienceFadeSeconds = 1.5f;
    private const float MusicFadeSeconds = 2.5f;

    private volatile SoundCues _cues = SoundCues.Empty;
    private volatile bool _cuesLoaded;

    // The decoded one-shots by file, what each cue played last and when; under it, from any thread
    private readonly object _cueLock = new();
    private readonly Dictionary<string, IntPtr> _samples = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int File, DateTime At)> _lastPlayed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unknownTold = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();
    private int _mixRate = 48000;

    /// <summary>Samples a second of the mixer's clock, what fades are timed in</summary>
    internal int MixRate => _mixRate;

    // The place's air, and the music: the cue each is asked for, the one playing, the ones still fading out
    private readonly LoopSlot _ambience = new("the place is quiet", AmbienceFadeSeconds);
    private readonly LoopSlot _music = new("no music plays", MusicFadeSeconds);

    /// <summary>The cues the mods give, once read (at start-up, after the system)</summary>
    public SoundCues Cues => _cues;

    /// <summary>
    /// Reads the cues and decodes every one-shot's files into memory, so the first click is as quick as the next. On the
    /// worker, at start-up.
    /// </summary>
    private void LoadCues()
    {
        SoundCues cues;
        try
        {
            cues = SoundCues.Load(ModSet.Installed);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "The sound cues could not be read: the game plays none");
            _cuesLoaded = true;
            return;
        }

        foreach (var problem in cues.Problems) _logger.Warning("Sound cues: {Problem}", problem);
        ModProblems.Add("Sound cues", cues.Problems);

        var core = _core;
        if (core != IntPtr.Zero && FMOD_System_GetSoftwareFormat(core, out var rate, out _, out _) == ResultOk && rate > 0) _mixRate = rate;

        _cues = cues;
        var files = cues.All.Where(c => !c.Loop).SelectMany(c => c.Files).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var file in files)
        {
            if (_shutDown) return;
            lock (_cueLock) Sample(file);
        }

        _cuesLoaded = true;
        _logger.Information("{Cues} sound cues, {Files} short sounds read in", cues.All.Count, files.Count);
    }

    /// <summary>The file decoded into memory, once; zero when FMOD cannot read it (logged once). Under the cue lock.</summary>
    private IntPtr Sample(string file)
    {
        if (_samples.TryGetValue(file, out var known)) return known;

        var core = _core;
        var sound = IntPtr.Zero;
        if (core == IntPtr.Zero || !Warn(FMOD_System_CreateSound(core, Utf8(file), ModeLoopOff | Mode2D | ModeCreateSample, IntPtr.Zero, out sound), "read " + Path.GetFileName(file)))
            sound = IntPtr.Zero;

        _samples[file] = sound;
        return sound;
    }

    /// <summary>
    /// Plays a cue once, a take of it the last play was not, at a slightly different pitch each time; after
    /// <paramref name="delaySeconds"/> of the mixer's own clock when given. Nothing while the game is silent (a race,
    /// no system yet) or while the cue's cooldown runs. Any thread; never throws.
    /// </summary>
    public void PlayCue(string name, float delaySeconds = 0f)
    {
        try
        {
            var core = _core;
            if (_system == IntPtr.Zero || core == IntPtr.Zero || _shutDown || _racing || !_cuesLoaded) return;

            if (_cues.Get(name) is not { Loop: false } cue)
            {
                lock (_cueLock)
                {
                    if (_unknownTold.Add(name)) _logger.Debug("No sound cue {Cue}: the moment goes unheard", name);
                }
                return;
            }

            IntPtr sound;
            float pitch;
            lock (_cueLock)
            {
                var now = DateTime.UtcNow;
                var last = _lastPlayed.TryGetValue(name, out var played) ? played : (File: -1, At: DateTime.MinValue);
                if (cue.Cooldown > 0f && (now - last.At).TotalSeconds < cue.Cooldown) return;

                var file = SoundCues.NextFile(cue.Files.Count, last.File, _random);
                _lastPlayed[name] = (file, now);
                sound = Sample(cue.Files[file]);
                pitch = SoundCues.Pitch(cue.Pitch, _random);
            }

            if (sound == IntPtr.Zero) return;
            if (!Warn(FMOD_System_PlaySound(core, sound, _groups[(int)cue.Bus], 1, out var channel), "play " + name)) return;

            // Set up while paused: released at its end by FMOD itself, nobody keeps the channel
            Warn(FMOD_Channel_SetVolume(channel, cue.Volume), "set cue volume");
            if (pitch != 1f) Warn(FMOD_Channel_SetPitch(channel, pitch), "set cue pitch");
            if (delaySeconds > 0f && Warn(FMOD_Channel_GetDSPClock(channel, out _, out var clock), "read the mixer's clock"))
                Warn(FMOD_Channel_SetDelay(channel, clock + (ulong)(delaySeconds * _mixRate), 0, 0), "delay cue");
            Warn(FMOD_Channel_SetPaused(channel, 0), "start cue");
        }
        catch (Exception ex)
        {
            _logger.Warning("The {Cue} sound could not be played: {Error}", name, ex.Message);
        }
    }

    /// <summary>
    /// The air of the place the player is in, a looping cue; null for none. Crossfades from the last one at the pump's
    /// next tick, and waits for the system and the cues when they are not there yet. Any thread.
    /// </summary>
    public void SetAmbience(string? cue) => _ambience.Wanted = cue;

    /// <summary>
    /// The music, a looping cue (<see cref="Audio.StartMusic"/>); null for none. Fades in or out at the pump's next tick,
    /// once the system and the cues are there. Any thread.
    /// </summary>
    public void SetMusic(string? cue) => _music.Wanted = cue;

    /// <summary>The pump's part: the loops brought in line with what is asked for, and faded-out ones let go of</summary>
    private void TickLoops()
    {
        _ambience.Tick(this);
        _music.Tick(this);
    }

    /// <summary>At the release of the system, which frees them: their handles are not to be used again</summary>
    private void ForgetSounds()
    {
        lock (_cueLock) _samples.Clear();
        _ambience.Forget();
        _music.Forget();
    }

    /// <summary>
    /// A looping cue that crossfades into the next one asked for: the place's air, the music. What is asked for may be
    /// set from any thread; the rest only the pump touches.
    /// </summary>
    private sealed class LoopSlot(string whenMissing, float fadeSeconds)
    {
        private volatile string? _wanted;
        private string? _name;
        private PlayingSound? _playing;
        private readonly List<PlayingSound> _fadingOut = [];

        public string? Wanted
        {
            set => _wanted = value;
        }

        public void Tick(AudioHub hub)
        {
            _fadingOut.RemoveAll(sound => !sound.IsPlaying);
            if (!hub._cuesLoaded) return;

            var wanted = _wanted;
            if (string.Equals(wanted, _name, StringComparison.OrdinalIgnoreCase)) return;

            if (_playing is { } leaving)
            {
                // From wherever its fade-in got to: one cut short does not jump to full first
                leaving.FadeFromHere(0f, fadeSeconds, hub._mixRate, stopAtEnd: true);
                _fadingOut.Add(leaving);
            }

            _playing = null;
            _name = wanted;
            if (wanted == null) return;

            if (hub._cues.Get(wanted) is not { Loop: true } cue)
            {
                hub._logger.Debug("No looping sound cue {Cue}: " + whenMissing, wanted);
                return;
            }

            // The cues' Random is shared with PlayCue on any thread
            string file;
            lock (hub._cueLock) file = cue.Files[hub._random.Next(cue.Files.Count)];
            var playing = hub.Play(cue.Bus, file, loop: true, paused: true);
            if (playing == null) return;

            playing.SetVolume(cue.Volume);
            playing.Fade(0f, 1f, fadeSeconds, hub._mixRate, stopAtEnd: false);
            playing.Resume();
            _playing = playing;
        }

        public void Forget()
        {
            _playing = null;
            _fadingOut.Clear();
        }
    }
}
