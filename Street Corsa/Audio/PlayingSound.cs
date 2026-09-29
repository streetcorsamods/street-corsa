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

    internal PlayingSound(IntPtr sound, IntPtr channel, string path)
    {
        _sound = sound;
        _channel = channel;
        Path = path;
        LengthMs = FMOD_Sound_GetLength(sound, out var length, TimeUnitMs) == ResultOk ? length : 0;
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

    public void Stop()
    {
        var (sound, channel) = (_sound, _channel);
        Forget();
        if (sound == IntPtr.Zero || AudioHub.Shared.IsShutDown) return;

        // A channel that played out already answers with a stale handle: nothing to stop
        FMOD_Channel_Stop(channel);
        Warn(FMOD_Sound_Release(sound), "release sound");
    }

    /// <summary>Drops the handles without a word to FMOD (after the exit, the system frees them); false, for "not playing"</summary>
    private bool Forget()
    {
        _sound = IntPtr.Zero;
        _channel = IntPtr.Zero;
        return false;
    }
}
