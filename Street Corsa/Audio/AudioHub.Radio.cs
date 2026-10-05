using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using StreetCorsa.Audio.Radio;
using static StreetCorsa.Audio.FmodStudio;

namespace StreetCorsa.Audio;

/// <summary>What a song file says of itself; null fields where its tags say nothing</summary>
public sealed record SongInfo(string? Title, string? Artist, string? Album, uint LengthMs);

/// <summary>
/// A song read into memory and opened as a stream on it: FMOD streams out of the caller's memory, so the bytes live
/// here until the sound is released (<see cref="PlayingSound.Stop"/> lets go of both). The file itself is closed
/// before anything plays, and the player may delete or move it while it does.
/// </summary>
public sealed class SongSound
{
    internal IntPtr Sound;
    internal IntPtr Memory;

    internal SongSound(IntPtr sound, IntPtr memory, string path)
    {
        Sound = sound;
        Memory = memory;
        Path = path;
    }

    public string Path { get; }

    /// <summary>A song opened but never played (skipped past, the radio switched off): let go of here</summary>
    public void Release()
    {
        var (sound, memory) = (Sound, Memory);
        Sound = Memory = IntPtr.Zero;

        // After the exit the system frees the sound; its memory goes with the process
        var hub = AudioHub.Shared;
        if (!hub.TryEnterFmod()) return;
        try
        {
            if (sound != IntPtr.Zero) Warn(FMOD_Sound_Release(sound), "release song");
            if (memory != IntPtr.Zero) Marshal.FreeHGlobal(memory);
        }
        finally
        {
            hub.LeaveFmod();
        }
    }
}

/// <summary>The radio's side of the hub: songs out of memory, their tags, and the AM sound of a car radio</summary>
public sealed partial class AudioHub
{
    // How the car radio sounds, input to output: summed to mono, a small speaker's band (the lows and highs cut, a
    // bump in the middle), a little grit, squeezed. Checked with FMOD's wav writer: a left-only noise comes out on both
    // sides alike, 1-2 kHz strongest, 20 dB down under 150 Hz and 25-55 dB down over 5 kHz.
    private IntPtr[] _amChain = [];

    // How far in the car radio's sound is, 0 (the music as it is) to 1, and where it is headed: it comes in and goes
    // out over AmFadeSeconds, the pump stepping it (TickAmRadio)
    private float _amMix;
    private float _amTarget;

    /// <summary>How long the car radio's sound takes to come in, or to go</summary>
    public const float AmFadeSeconds = 1.5f;

    // The biggest song read into memory: about 50 minutes of a CD's WAV, a few hours of MP3 or FLAC. A bigger file (a long
    // DJ mix as WAV) is passed over, said in the log, rather than costing its size in memory
    private const long LargestSong = 512L * 1024 * 1024;

    /// <summary>
    /// A song file read into memory and opened on it; null when there is no system, the file cannot be read, is bigger
    /// than <see cref="LargestSong"/>, or FMOD cannot play it (logged). On a worker: a long FLAC is tens of MB.
    /// </summary>
    public SongSound? OpenSong(string path)
    {
        if (!TryEnterFmod()) return null;
        try
        {
            return OpenSongInside(path);
        }
        finally
        {
            LeaveFmod();
        }
    }

    private SongSound? OpenSongInside(string path)
    {
        var core = _core;
        if (core == IntPtr.Zero) return null;

        // Read whole, straight into the memory FMOD plays it from, and closed at once; shared, so a file another program
        // has open can still be played
        var memory = IntPtr.Zero;
        int length;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > LargestSong)
            {
                _logger.Warning("The song {File} is too big to play ({Mb} MB; at most {Most} MB)", path, stream.Length / (1024 * 1024), LargestSong / (1024 * 1024));
                return null;
            }

            length = (int)stream.Length;
            if (length == 0) return null;
            memory = Marshal.AllocHGlobal(length);
            unsafe
            {
                stream.ReadExactly(new Span<byte>((void*)memory, length));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            if (memory != IntPtr.Zero) Marshal.FreeHGlobal(memory);
            _logger.Warning("The song {File} could not be read: {Error}", path, ex.Message);
            return null;
        }

        var exInfo = Marshal.AllocHGlobal(CreateSoundExInfoSize);
        try
        {
            unsafe
            {
                new Span<byte>((void*)exInfo, CreateSoundExInfoSize).Clear();
            }
            Marshal.WriteInt32(exInfo, 0, CreateSoundExInfoSize);
            Marshal.WriteInt32(exInfo, 4, length);

            var result = FMOD_System_CreateSoundFromMemory(core, memory, ModeOpenMemory | ModeCreateStream | Mode2D | ModeLoopNormal | ModeAccurateTime, exInfo, out var sound);
            if (result != ResultOk)
            {
                _logger.Warning("The song {File} cannot be played (FMOD error {Code}: a format it does not know?)", path, result);
                Marshal.FreeHGlobal(memory);
                return null;
            }

            return new SongSound(sound, memory, path);
        }
        catch
        {
            Marshal.FreeHGlobal(memory);
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(exInfo);
        }
    }

    /// <summary>
    /// Plays an opened song on the radio's bus, from <paramref name="positionMs"/>, coming up over
    /// <paramref name="fadeInSeconds"/> when given (the radio switched on); null when it cannot
    /// </summary>
    public PlayingSound? PlaySong(SongSound song, uint positionMs = 0, float fadeInSeconds = 0f)
    {
        var core = _core;
        if (_system == IntPtr.Zero || core == IntPtr.Zero || _shutDown || song.Sound == IntPtr.Zero) return null;

        if (!Warn(FMOD_System_PlaySound(core, song.Sound, _groups[(int)AudioBus.Radio], 1, out var channel), "play song")) return null;

        var playing = new PlayingSound(song, channel);
        playing.SetLoop(false);
        if (positionMs > 0) Warn(FMOD_Channel_SetPosition(channel, positionMs, TimeUnitMs), "seek song");
        if (fadeInSeconds > 0f) playing.Fade(0f, 1f, fadeInSeconds, _mixRate, stopAtEnd: false);
        playing.Resume();
        return playing;
    }

    /// <summary>
    /// A song file's title, artist, album and length, as its tags have them (ID3v1/v2, Vorbis comments, FLAC); no
    /// tags when FMOD cannot open it, and not asked when there is no FMOD to ask (none yet, or the exit under way).
    /// Opens the file for a moment only. On a worker.
    /// </summary>
    public RadioLibrary.SongRead ReadSongInfo(string path)
    {
        if (!TryEnterFmod()) return new RadioLibrary.SongRead(null, Asked: false);
        try
        {
            var core = _core;
            return core == IntPtr.Zero
                ? new RadioLibrary.SongRead(null, Asked: false)
                : new RadioLibrary.SongRead(ReadSongInfo(core, path), Asked: true);
        }
        finally
        {
            LeaveFmod();
        }
    }

    private static SongInfo? ReadSongInfo(IntPtr core, string path)
    {
        if (FMOD_System_CreateSound(core, Utf8(path), ModeOpenOnly | ModeCreateStream | Mode2D, IntPtr.Zero, out var sound) != ResultOk) return null;

        try
        {
            var length = FMOD_Sound_GetLength(sound, out var ms, TimeUnitMs) == ResultOk ? ms : 0;
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (FMOD_Sound_GetNumTags(sound, out var count, out _) == ResultOk)
            {
                for (var i = 0; i < count; i++)
                {
                    if (FMOD_Sound_GetTag(sound, IntPtr.Zero, i, out var tag) != ResultOk) continue;
                    var name = Marshal.PtrToStringAnsi(tag.Name);
                    if (name == null || tags.ContainsKey(name)) continue;
                    if (TagText(tag) is { Length: > 0 } text) tags[name] = text;
                }
            }

            return new SongInfo(
                First(tags, "TITLE", "TIT2", "TT2"),
                First(tags, "ARTIST", "TPE1", "TP1", "ALBUMARTIST", "TPE2"),
                First(tags, "ALBUM", "TALB", "TAL"),
                length);
        }
        finally
        {
            FMOD_Sound_Release(sound);
        }
    }

    /// <summary>
    /// The longest of what the tags say under these names: an MP3 often has both an ID3v1 tag, which cuts a title at 30
    /// characters, and an ID3v2 one with all of it
    /// </summary>
    private static string? First(Dictionary<string, string> tags, params string[] names) =>
        names.Select(n => tags.GetValueOrDefault(n)).Where(v => v != null).OrderByDescending(v => v!.Length).FirstOrDefault();

    /// <summary>A tag's text however it is written; null for data that is not text</summary>
    private static string? TagText(Tag tag)
    {
        if (tag.Data == IntPtr.Zero || tag.DataLength == 0 || tag.DataLength > 64 * 1024) return null;

        var bytes = new byte[tag.DataLength];
        Marshal.Copy(tag.Data, bytes, 0, bytes.Length);
        return TagDecoding.Decode(tag.DataType, bytes);
    }

    /// <summary>
    /// The car radio's sound on the radio's bus, or the music as it is: it comes in (or goes) over
    /// <see cref="AmFadeSeconds"/>, each effect mixed in by as much. The chain is made the first time it is wanted.
    /// UI thread.
    /// </summary>
    public void SetAmRadio(bool on)
    {
        var core = _core;
        if (_system == IntPtr.Zero || core == IntPtr.Zero || _shutDown) return;

        if (on && _amChain.Length == 0)
        {
            _amChain = BuildAmChain(core, _groups[(int)AudioBus.Radio]);
            ApplyAmMix();
        }

        _amTarget = on ? 1f : 0f;
    }

    /// <summary>One step of the car radio's fade, from the pump (under the hub's lock)</summary>
    private void TickAmRadio(TimeSpan elapsed)
    {
        if (_amChain.Length == 0 || _amMix == _amTarget) return;

        var step = (float)(elapsed.TotalSeconds / AmFadeSeconds);
        _amMix = _amMix < _amTarget ? Math.Min(_amTarget, _amMix + step) : Math.Max(_amTarget, _amMix - step);
        ApplyAmMix();
    }

    /// <summary>
    /// Each effect of the chain as far in as the fade is: its own output mixed with what it was given. All the way out,
    /// they are bypassed, and cost nothing.
    /// </summary>
    private void ApplyAmMix()
    {
        var mix = _amMix;
        foreach (var dsp in _amChain)
        {
            Warn(FMOD_DSP_SetBypass(dsp, mix <= 0f ? 1 : 0), "switch the AM sound");
            if (mix > 0f) Warn(FMOD_DSP_SetWetDryMix(dsp, 1f, mix, 1f - mix), "mix the AM sound");
        }
    }

    // FMOD_CHANNELCONTROL_DSP_TAIL: the input end of a group's chain; FMOD_DSP_CHANNELMIX_OUTPUT_ALLMONO
    private const int DspTail = -2;
    private const int ChannelMixAllMono = 1;

    private IntPtr[] BuildAmChain(IntPtr core, IntPtr group)
    {
        // Added at the input end one after another, so the last added is the first the music meets: the fader
        // stays after all of it, and the grit does not change with the volume
        var chain = new List<IntPtr>();
        try
        {
            Add(DspCompressor, d =>
            {
                Param(d, 0, -24f); // threshold, dB
                Param(d, 1, 4f); // ratio
                Param(d, 2, 5f); // attack, ms
                Param(d, 3, 150f); // release, ms
                Param(d, 4, 8f); // make-up gain, dB: what the narrow band takes away
            });
            Add(DspDistortion, d => Param(d, 0, 0.12f));
            Add(DspParamEq, d =>
            {
                Param(d, 0, 1500f); // centre, Hz
                Param(d, 1, 1.5f); // octaves
                Param(d, 2, 4f); // gain, dB
            });
            Add(DspLowpass, d =>
            {
                Param(d, 0, 3600f);
                Param(d, 1, 1.1f);
            });
            Add(DspHighpass, d =>
            {
                Param(d, 0, 320f);
                Param(d, 1, 1.1f);
            });
            Add(DspChannelMix, d =>
            {
                Warn(FMOD_DSP_SetParameterInt(d, 0, ChannelMixAllMono), "mono radio");
                Param(d, 1, -6f); // left and right summed: each at half
                Param(d, 2, -6f);
            });
        }
        catch (FmodException ex)
        {
            // Without all of it the radio plays as it is
            _logger.Warning("The car radio's sound could not be made: {Error}", ex.Message);
        }

        return chain.ToArray();

        void Add(int type, Action<IntPtr> setUp)
        {
            Check(FMOD_System_CreateDSPByType(core, type, out var dsp), "create the radio's DSP " + type);
            setUp(dsp);
            Check(FMOD_ChannelGroup_AddDSP(group, DspTail, dsp), "add the radio's DSP " + type);
            chain.Add(dsp);
        }

        static void Param(IntPtr dsp, int index, float value) => Warn(FMOD_DSP_SetParameterFloat(dsp, index, value), "radio DSP setting");
    }
}

/// <summary>Tag text as FMOD hands it over: Latin-1, UTF-8 or UTF-16 either way, with or without a byte-order mark</summary>
public static class TagDecoding
{
    public static string? Decode(int dataType, byte[] bytes)
    {
        string text;
        switch (dataType)
        {
            case TagString:
                text = Encoding.Latin1.GetString(bytes);
                break;
            case TagStringUtf8:
                text = Encoding.UTF8.GetString(bytes);
                break;
            case TagStringUtf16:
                text = bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF
                    ? Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2)
                    : Encoding.Unicode.GetString(bytes);
                break;
            case TagStringUtf16BigEndian:
                text = Encoding.BigEndianUnicode.GetString(bytes);
                break;
            default:
                return null;
        }

        text = text.Trim('﻿', '\0', ' ', '\t', '\r', '\n');
        var nul = text.IndexOf('\0');
        if (nul >= 0) text = text[..nul].Trim();
        return text.Length > 0 ? text : null;
    }
}
