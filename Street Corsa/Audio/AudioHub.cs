using System.IO;
using System.Windows.Threading;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Services.Settings;
using static StreetCorsa.Audio.FmodStudio;

namespace StreetCorsa.Audio;

/// <summary>
/// Everything the game plays goes through here: one FMOD system for the app, Assetto Corsa's own (see
/// <see cref="FmodStudio"/>), with a group for each kind of sound under a master volume. The engines are Studio events
/// out of AC's banks (<see cref="EngineAudio"/> plays them, at <see cref="Gain"/> of <see cref="AudioBus.Engine"/>);
/// everything else plays into its bus's channel group.
///
/// A pump on the UI thread keeps FMOD going (its update, once every <see cref="PumpInterval"/>) and moves the fade:
/// out while a race has the sound device, where the mixer then rests, and, if the player wants it, while another
/// window has the focus. The system starts on a worker at start-up, or on the first engine that wants it.
/// </summary>
public sealed partial class AudioHub
{
    private static readonly Lazy<AudioHub> SharedInstance = new(() => new AudioHub());

    public static AudioHub Shared => SharedInstance.Value;

    /// <summary>The system, released at exit if it was ever made (<see cref="FmodLifetime"/>); false when it was left to the end of the process</summary>
    internal static bool ShutdownShared(DateTime deadline) => !SharedInstance.IsValueCreated || SharedInstance.Value.Shutdown(deadline);

    private const int MaxChannels = 64;

    private static readonly TimeSpan PumpInterval = TimeSpan.FromMilliseconds(20);

    // A start that failed from a folder is tried again from it after this long: a device that was busy or missing a
    // moment ago may be back, and a whole session of silence is too high a price for one bad moment
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    // The buses that are channel groups of their own; the engines are Studio events and get their gain per instance
    private static readonly AudioBus[] GroupBuses = [AudioBus.Effects, AudioBus.Interface, AudioBus.Ambience, AudioBus.Radio];

    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Audio");

    // Making, resting, waking and releasing the system hold it. The pump only ever tries it: the UI thread never
    // waits on a system that is still being made
    private readonly object _lock = new();

    private volatile IntPtr _system;
    private IntPtr _core;
    private readonly IntPtr[] _groups = new IntPtr[Enum.GetValues<AudioBus>().Length];
    private bool _mixerSuspended;
    private bool _resumeFailureTold;
    private volatile bool _shutDown;

    // Workers inside FMOD without the lock (a song read in, its tags, a cue): counted in and out, and the release of the
    // system waits for them to be out (TryEnterFmod)
    private int _inFmod;

    // The AC folder FMOD could not be started from, and when: not tried again from there for a while, but a new
    // folder in the settings is at once
    private string? _failedFolder;
    private DateTime _failedAt;

    // Replaced whole, never changed in place: the pump and the engines read it without a lock
    private volatile AudioSettings _settings = new();
    private volatile bool _racing;
    private volatile bool _focused = true;

    // Only the pump moves them: the game's own sounds, and the radio's (which plays on in a race when the player wants it)
    private volatile float _fade = 1f;
    private volatile float _radioFade = 1f;

    // The radio is playing: in a race the mixer stays awake for it
    private volatile bool _radioActive;
    private readonly float[] _applied = Enumerable.Repeat(-1f, Enum.GetValues<AudioBus>().Length).ToArray();

    private DispatcherTimer? _pump;
    private DateTime _lastTick;

    private AudioHub() { }

    /// <summary>
    /// Takes the volumes, starts the pump on the calling (UI) thread and the system on a worker. A game without FMOD
    /// (no AC folder yet) is a silent game, never a failed start.
    /// </summary>
    public void Start(AudioSettings settings)
    {
        Apply(settings);

        if (_pump == null)
        {
            _lastTick = DateTime.UtcNow;
            _pump = new DispatcherTimer(PumpInterval, DispatcherPriority.Normal, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
            _pump.Start();
        }

        // The system, then the cues and their short sounds read in, all off the UI thread
        var reading = Interlocked.Increment(ref _cueReading);
        _ = Task.Run(() =>
        {
            if (TryGetStudio() != IntPtr.Zero) LoadCues(ReadCuesOf(ModSet.Installed), reading);
        });
    }

    /// <summary>
    /// The cues of another content (the main screen's core, a game's setting): read now, so their problems are told with
    /// the content's, and their short sounds decoded off the UI thread. A later read overtakes one still decoding.
    /// </summary>
    public void ReadCues(ModSet mods)
    {
        var cues = ReadCuesOf(mods);
        var reading = Interlocked.Increment(ref _cueReading);
        _ = Task.Run(() =>
        {
            if (TryGetStudio() != IntPtr.Zero) LoadCues(cues, reading);
        });
    }

    /// <summary>New volumes, heard at the next tick of the pump</summary>
    public void Apply(AudioSettings? settings) => _settings = settings?.Clone() ?? new AudioSettings();

    /// <summary>The window has the focus, or has lost it</summary>
    public void SetFocused(bool focused) => _focused = focused;

    /// <summary>
    /// A race (or AC's showroom) starts or has ended: the game's own sounds fade out, and the mixer rests until it is
    /// over, as AC has the sound device to itself; unless the player puts the radio on in the race, which then plays
    /// over AC (and the mixer stays awake for it). Any thread.
    /// </summary>
    public void SetRacing(bool racing)
    {
        if (_racing == racing) return;
        _racing = racing;
        _logger.Information(racing ? "Assetto Corsa runs: the game's sound fades out" : "Assetto Corsa is done: the game's sound is back");
    }

    /// <summary>The radio plays (or has stopped): in a race, the mixer is kept awake for it. Any thread.</summary>
    public void SetRadioActive(bool active) => _radioActive = active;

    /// <summary>What <paramref name="bus"/> plays at now, the fade in it</summary>
    public float Gain(AudioBus bus) => AudioMix.Gain(_settings, bus, bus == AudioBus.Radio ? _radioFade : _fade);

    /// <summary>
    /// The group sounds of <paramref name="bus"/> play into; zero while there is no system, for the engines, and for a
    /// bus the enum does not have (a cue's "7" cast through): nothing is ever indexed by a bus that was not checked
    /// </summary>
    internal IntPtr Group(AudioBus bus) => _system == IntPtr.Zero || !Enum.IsDefined(bus) ? IntPtr.Zero : _groups[(int)bus];

    /// <summary>The exit has begun: nothing is to touch FMOD's handles any more, the system frees them all</summary>
    internal bool IsShutDown => _shutDown;

    /// <summary>
    /// A way into FMOD for a call made without the hub's lock (on a worker, or any thread): false when there is no
    /// system or the exit has begun. Each true is paired with <see cref="LeaveFmod"/>; the release of the system waits
    /// for every one of them to be out.
    /// </summary>
    internal bool TryEnterFmod()
    {
        Interlocked.Increment(ref _inFmod);
        if (_system != IntPtr.Zero && !_shutDown) return true;
        Interlocked.Decrement(ref _inFmod);
        return false;
    }

    internal void LeaveFmod() => Interlocked.Decrement(ref _inFmod);

    /// <summary>
    /// Plays a sound file on <paramref name="bus"/>, streamed from disk, once or round and round. Null when there is no
    /// system (yet), or FMOD cannot read the file (logged). UI thread: the file is opened without FMOD's accurate time,
    /// which reads a whole MP3 through first (a loop never seeks, and its length is not asked).
    /// </summary>
    /// <param name="paused">Left paused, for the caller to set it up (a fade in) before it is heard</param>
    public PlayingSound? Play(AudioBus bus, string path, bool loop, bool paused = false)
    {
        // Counted in, as a worker is: the fatal path's release of the system waits for this call to be out
        if (!TryEnterFmod()) return null;
        try
        {
            return PlayInside(bus, path, loop, paused);
        }
        finally
        {
            LeaveFmod();
        }
    }

    private PlayingSound? PlayInside(AudioBus bus, string path, bool loop, bool paused)
    {
        var core = _core;
        if (core == IntPtr.Zero) return null;

        // No group to play into (a bus that is no bus, the engines'): the sound goes unheard, never a throw into the pump
        var group = Group(bus);
        if (group == IntPtr.Zero)
        {
            _logger.Warning("No bus {Bus} to play {File} on", bus, Path.GetFileName(path));
            return null;
        }

        if (!Warn(FMOD_System_CreateSound(core, Utf8(path), ModeLoopNormal | Mode2D | ModeCreateStream, IntPtr.Zero, out var sound), "open " + Path.GetFileName(path)))
            return null;

        // Paused until the loop count is set: a short sound could otherwise be over before it is told to loop
        if (!Warn(FMOD_System_PlaySound(core, sound, group, 1, out var channel), "play " + Path.GetFileName(path)))
        {
            Warn(FMOD_Sound_Release(sound), "release sound");
            return null;
        }

        var playing = new PlayingSound(sound, channel, path);
        playing.SetLoop(loop);
        if (!paused) playing.Resume();
        return playing;
    }

    /// <summary>
    /// The Studio system, made on the first call; zero when FMOD cannot run here (no AC folder, no device). Blocks for
    /// as long as the system takes to start: never on the UI thread.
    /// </summary>
    internal IntPtr TryGetStudio()
    {
        if (_system != IntPtr.Zero) return _system;

        lock (_lock)
        {
            return EnsureSystem() ? _system : IntPtr.Zero;
        }
    }

    /// <summary>Lets FMOD do its work: the pump's, and whoever waits on FMOD in between (a bank being loaded or let go of)</summary>
    public void Update()
    {
        var system = _system;
        if (system != IntPtr.Zero) Warn(FMOD_Studio_System_Update(system), "update");
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastTick;
        _lastTick = now;

        var settings = _settings;
        var racing = _racing;
        _fade = AudioMix.StepFade(_fade, AudioMix.FadeTarget(racing, _focused, settings.MuteInBackground), elapsed);
        _radioFade = AudioMix.StepFade(_radioFade, AudioMix.RadioFadeTarget(racing, _focused, settings.MuteInBackground), elapsed);
        var radioActive = _radioActive;

        if (_system == IntPtr.Zero || _shutDown) return;

        // The system may be starting on the worker, or the exit going on: the next tick will do
        if (!Monitor.TryEnter(_lock)) return;
        try
        {
            if (_system == IntPtr.Zero || _shutDown) return;

            // Awake before the fade in is heard (or the radio comes on in a race); asleep once the fade out is over
            if (!racing || radioActive) ResumeMixer();

            foreach (var bus in GroupBuses)
            {
                var gain = AudioMix.Gain(settings, bus, bus == AudioBus.Radio ? _radioFade : _fade);
                if (Math.Abs(gain - _applied[(int)bus]) < 1e-4f) continue;
                if (Warn(FMOD_ChannelGroup_SetVolume(_groups[(int)bus], gain), "set bus volume")) _applied[(int)bus] = gain;
            }

            TickAmRadio(elapsed);
            if (_mixerSuspended) return;
            TickLoops();
            Update();
            if (racing && _fade <= 0f && !radioActive) SuspendMixer();
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    private bool EnsureSystem()
    {
        if (_system != IntPtr.Zero) return true;
        if (_shutDown) return false;

        // A folder that failed is not tried again for a while; the player putting the right one in the settings is
        var folder = AppSettings.Instance.AssettoCorsaPath;
        if (string.Equals(_failedFolder, folder, StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow - _failedAt < RetryAfter) return false;

        var system = IntPtr.Zero;
        try
        {
            FmodStudio.Load(folder);
            Check(FMOD_Studio_System_Create(out system, HeaderVersion), "create");
            Check(FMOD_Studio_System_Initialize(system, MaxChannels, StudioInitNormal, InitNormal, IntPtr.Zero), "initialise");

            // Every car bank uses them, and a bank without its plugins does not load
            FmodPlugins.Register(system);

            var common = Path.Combine(folder, "content", "sfx", "common.bank");
            Check(FMOD_Studio_System_LoadBankFile(system, Utf8(common), LoadBankNormal, out _), "load common.bank");

            Check(FMOD_Studio_System_GetLowLevelSystem(system, out var core), "core system");
            foreach (var bus in GroupBuses)
            {
                Check(FMOD_System_CreateChannelGroup(core, Utf8(bus.ToString()), out var group), "create the " + bus + " group");
                _groups[(int)bus] = group;
                _applied[(int)bus] = -1f;
            }

            _core = core;
            _mixerSuspended = false;
            _resumeFailureTold = false;
            _failedFolder = null;
            _system = system;
            _logger.Information("FMOD started from {Folder}", folder);
            return true;
        }
        catch (Exception ex)
        {
            // Half a system is still threads and memory: it goes (its groups with it), and the next try starts clean
            if (system != IntPtr.Zero) FMOD_Studio_System_Release(system);
            Array.Clear(_groups);

            // No sound is no reason to fail a screen: the game just stays silent
            _failedFolder = folder;
            _failedAt = DateTime.UtcNow;
            _logger.Error(ex, "FMOD could not be started from {Folder}: the game is silent for now (tried again in {Minutes} min)", folder, RetryAfter.TotalMinutes);
            return false;
        }
    }

    // A suspend that fails is logged by Warn and leaves the flag down: the mixer just runs on
    private void SuspendMixer()
    {
        if (_core == IntPtr.Zero || _mixerSuspended) return;
        if (TryMixerSuspend(_core) is { } result && Warn(result, "suspend mixer")) _mixerSuspended = true;
    }

    /// <summary>
    /// Wakes the mixer. The flag drops only once it is awake, so a failed wake is tried again by the next tick. A
    /// mixer left asleep is a mute game, so the first failure is also said plainly, to be found in the log.
    /// </summary>
    private void ResumeMixer()
    {
        if (_core == IntPtr.Zero || !_mixerSuspended) return;

        var result = TryMixerResume(_core);
        if (result is { } code && Warn(code, "resume mixer"))
        {
            _mixerSuspended = false;
            return;
        }

        if (_resumeFailureTold) return;
        _resumeFailureTold = true;
        _logger.Information("The FMOD mixer could not be woken ({Reason}): the game stays silent until it is",
            result is { } failed ? $"error {failed}" : "the DLL has no FMOD_System_MixerResume");
    }

    /// <summary>
    /// The exit is on its way: nothing starts from now on, and a rested mixer is woken, as it paces the Studio thread
    /// the release of the engine banks waits on. False when the system is still being made and cannot be woken.
    /// </summary>
    internal bool BeginShutdown(DateTime deadline)
    {
        _shutDown = true;
        if (!TryEnterUntil(deadline)) return false;
        try
        {
            ResumeMixer();
            return true;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>
    /// Releases the system for good, its groups and every bank still in it. Never throws. A start still inside FMOD
    /// by <paramref name="deadline"/> keeps it: a system left to the end of the process is harmless, one freed while a
    /// worker is inside it crashes the exit.
    /// </summary>
    private bool Shutdown(DateTime deadline)
    {
        // Set before the workers are counted: one that comes in after sees it and goes (TryEnterFmod)
        _shutDown = true;
        Interlocked.MemoryBarrier();
        if (!SpinWait.SpinUntil(() => Volatile.Read(ref _inFmod) == 0, Remaining(deadline)))
        {
            _logger.Warning("A worker was still inside FMOD at exit: the system is left to the end of the process rather than freed under it");
            return false;
        }

        if (!TryEnterUntil(deadline))
        {
            _logger.Warning("FMOD was still starting at exit: it is left to the end of the process rather than freed under it");
            return false;
        }

        try
        {
            if (_pump is { } pump) pump.Dispatcher.BeginInvoke(pump.Stop);

            var system = _system;
            _system = IntPtr.Zero;
            _core = IntPtr.Zero;
            if (system == IntPtr.Zero) return true;

            // Their memory goes with the system; the handles must not be used after it
            ForgetSounds();

            foreach (var bus in GroupBuses)
            {
                if (_groups[(int)bus] != IntPtr.Zero) Warn(FMOD_ChannelGroup_Release(_groups[(int)bus]), "release the " + bus + " group");
            }
            Array.Clear(_groups);

            // Releasing the system unloads every bank still in it (common.bank) and stops its threads
            Warn(FMOD_Studio_System_Release(system), "release");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "FMOD did not shut down cleanly");
            return true;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    private bool TryEnterUntil(DateTime deadline) => Monitor.TryEnter(_lock, Remaining(deadline));

    private static TimeSpan Remaining(DateTime deadline)
    {
        var wait = deadline - DateTime.UtcNow;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }
}
