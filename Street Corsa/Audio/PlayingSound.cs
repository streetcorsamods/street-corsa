using static StreetCorsa.Audio.FmodStudio;

namespace StreetCorsa.Audio;

/// <summary>
/// A sound file playing on a bus (<see cref="AudioHub.Play"/>): streamed from disk as it plays, so the file stays open
/// until it is stopped. Driven from the UI thread. Once it has played out, or is stopped, it lets go of the file.
/// </summary>
public sealed class PlayingSound
{
    private IntPtr _sound;
    private IntPtr _channel;

    // A song out of memory: its memory goes when it does
    private SongSound? _song;

    internal PlayingSound(IntPtr sound, IntPtr channel, string path)
    {
        _sound = sound;
        _channel = channel;
        Path = path;
        LengthMs = FMOD_Sound_GetLength(sound, out var length, TimeUnitMs) == ResultOk ? length : 0;
    }

    internal PlayingSound(SongSound song, IntPtr channel) : this(song.Sound, channel, song.Path)
    {
        _song = song;
    }

    public string Path { get; }

    /// <summary>How long it plays once through; 0 when FMOD cannot say</summary>
    public uint LengthMs { get; }

    /// <summary>Still sounding. A sound that has played out is let go of here, the first time this is asked</summary>
    public bool IsPlaying
    {
        get
        {
            if (_sound == IntPtr.Zero) return false;
            if (AudioHub.Shared.IsShutDown) return Forget();

            // A channel that has played out is a stale handle to FMOD: an error, not a "no"
            if (FMOD_Channel_IsPlaying(_channel, out var playing) == ResultOk && playing != 0) return true;

            Stop();
            return false;
        }
    }

    /// <summary>Where it is, within one time through</summary>
    public uint PositionMs =>
        _sound != IntPtr.Zero && !AudioHub.Shared.IsShutDown && FMOD_Channel_GetPosition(_channel, out var position, TimeUnitMs) == ResultOk ? position : 0;

    /// <summary>Round and round, or once through to its end from where it is</summary>
    public void SetLoop(bool loop)
    {
        if (_sound != IntPtr.Zero && !AudioHub.Shared.IsShutDown) Warn(FMOD_Channel_SetLoopCount(_channel, loop ? -1 : 0), "set loop count");
    }

    /// <summary>Heard from now: a sound played paused (<see cref="AudioHub.Play"/>) once it is set up</summary>
    public void Resume()
    {
        if (_sound != IntPtr.Zero && !AudioHub.Shared.IsShutDown) Warn(FMOD_Channel_SetPaused(_channel, 0), "unpause");
    }

    public void SetPaused(bool paused)
    {
        if (_sound != IntPtr.Zero && !AudioHub.Shared.IsShutDown) Warn(FMOD_Channel_SetPaused(_channel, paused ? 1 : 0), "pause");
    }

    public void SetVolume(float volume)
    {
        if (_sound != IntPtr.Zero && !AudioHub.Shared.IsShutDown) Warn(FMOD_Channel_SetVolume(_channel, volume), "set volume");
    }

    /// <summary>
    /// From <paramref name="from"/> to <paramref name="to"/> of its volume over <paramref name="seconds"/> of the mixer's
    /// clock (<paramref name="rate"/> samples a second), sample-accurate; stopped at the end when asked, and then let go
    /// of the next time <see cref="IsPlaying"/> is asked
    /// </summary>
    internal void Fade(float from, float to, float seconds, int rate, bool stopAtEnd)
    {
        if (_sound == IntPtr.Zero || AudioHub.Shared.IsShutDown) return;
        if (!Warn(FMOD_Channel_GetDSPClock(_channel, out _, out var now), "read the mixer's clock")) return;

        var end = now + (ulong)Math.Max(1, seconds * rate);
        FMOD_Channel_RemoveFadePoints(_channel, 0, ulong.MaxValue);
        Warn(FMOD_Channel_AddFadePoint(_channel, now, from), "fade point");
        Warn(FMOD_Channel_AddFadePoint(_channel, end, to), "fade point");
        if (stopAtEnd) Warn(FMOD_Channel_SetDelay(_channel, 0, end, 1), "stop at the end of the fade");
    }

    public void Stop()
    {
        var (sound, channel, song) = (_sound, _channel, _song);
        Forget();
        if (sound == IntPtr.Zero || AudioHub.Shared.IsShutDown) return;

        // A channel that played out already answers with a stale handle: nothing to stop
        FMOD_Channel_Stop(channel);
        if (song != null) song.Release();
        else Warn(FMOD_Sound_Release(sound), "release sound");
    }

    /// <summary>Drops the handles without a word to FMOD (after the exit, the system frees them); false, for "not playing"</summary>
    private bool Forget()
    {
        _sound = IntPtr.Zero;
        _channel = IntPtr.Zero;
        _song = null;
        return false;
    }
}
