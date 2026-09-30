using System.IO;
using System.Runtime.InteropServices;
using StreetCorsa.Logging;

namespace StreetCorsa.Audio;

/// <summary>
/// The handful of FMOD Studio calls it takes to play a car's engine: Assetto Corsa's own fmodstudio64.dll
/// (1.08), loaded from the AC install. The C API, not the C++ one, so plain P/Invoke will do: every signature here
/// is blittable, so the source-generated imports need no marshalling of their own. Names go in as NUL-terminated
/// UTF-8 bytes.
/// </summary>
internal static partial class FmodStudio
{
    private const string StudioDll = "fmodstudio64.dll";
    private const string CoreDll = "fmod64.dll";

    /// <summary>The header version the DLLs are built against: 1.08.12, the same in every AC install</summary>
    public const uint HeaderVersion = 0x00010812;

    public const int ResultOk = 0;

    // FMOD_STUDIO_INIT_SYNCHRONOUS_UPDATE: commands run in Update, on the caller's thread
    public const uint StudioInitSynchronousUpdate = 0x2;

    // FMOD_OUTPUTTYPE_NOSOUND_NRT: mixes one block per update, as fast as it is asked, to nowhere
    public const int OutputNoSoundNrt = 4;

    // FMOD_CHANNELCONTROL_DSP_HEAD: the end of a channel group's chain nearest the output
    public const int DspHead = -1;

    // FMOD_STUDIO_INIT_NORMAL, FMOD_INIT_NORMAL
    public const uint StudioInitNormal = 0;
    public const uint InitNormal = 0;

    // FMOD_STUDIO_LOAD_BANK_NORMAL
    public const uint LoadBankNormal = 0;

    // FMOD_STUDIO_STOP_MODE
    public const int StopAllowFadeout = 0;
    public const int StopImmediate = 1;

    // FMOD_STUDIO_LOADING_STATE
    public const int LoadingStateUnloading = 0;
    public const int LoadingStateUnloaded = 1;
    public const int LoadingStateLoading = 2;
    public const int LoadingStateLoaded = 3;
    public const int LoadingStateError = 4;

    /// <summary>
    /// How far an engine bank's parameters go when the bank does not say: the same for playing and for measuring,
    /// or the loudness would be measured over another range than the one heard
    /// </summary>
    public const float DefaultMaxRpm = 10000f;

    public const float DefaultMaxThrottle = 1f;

    /// <summary>The parameters every engine_ext event is driven by, as FMOD wants their names</summary>
    public static readonly byte[] RpmsName = Utf8("rpms");

    public static readonly byte[] ThrottleName = Utf8("throttle");

    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("EngineAudio");

    private static readonly object LoadLock = new();
    private static bool _resolverSet;
    private static string? _folder;

    // The optional exports (see "Calls 1.08 may not have"), resolved once as the DLLs load; zero when missing
    private static IntPtr _flushCommands;
    private static IntPtr _bankGetLoadingState;
    private static IntPtr _mixerSuspend;
    private static IntPtr _mixerResume;

    /// <summary>
    /// Points the imports at the AC install. The core DLL goes in first by its full path: the studio DLL links
    /// to it by name, and Windows would not look for it next to the studio DLL.
    ///
    /// Only a load that worked is kept, so a folder without FMOD can be tried again once the path is put right. Once
    /// loaded the DLLs stay for the process (every AC install ships the same 1.08): a later change of the AC folder
    /// only changes where the banks come from.
    /// </summary>
    public static void Load(string folder)
    {
        lock (LoadLock)
        {
            if (_folder != null) return;

            var core = Path.Combine(folder, CoreDll);
            var studio = Path.Combine(folder, StudioDll);
            if (!File.Exists(core) || !File.Exists(studio))
                throw new FileNotFoundException($"FMOD is not in the Assetto Corsa folder: {folder}");

            var coreHandle = NativeLibrary.Load(core);
            IntPtr handle;
            try
            {
                handle = NativeLibrary.Load(studio);
            }
            catch
            {
                // Let go of the core again: every retry would otherwise add a reference to it
                NativeLibrary.Free(coreHandle);
                throw;
            }

            if (!_resolverSet)
            {
                NativeLibrary.SetDllImportResolver(typeof(FmodStudio).Assembly, (name, _, _) =>
                    name == StudioDll ? handle : name == CoreDll ? coreHandle : IntPtr.Zero);
                _resolverSet = true;
            }

            // The calls 1.08 may not have, looked up once: zero where the DLL has no such export
            _flushCommands = ExportOrZero(handle, "FMOD_Studio_System_FlushCommands");
            _bankGetLoadingState = ExportOrZero(handle, "FMOD_Studio_Bank_GetLoadingState");
            _mixerSuspend = ExportOrZero(coreHandle, "FMOD_System_MixerSuspend");
            _mixerResume = ExportOrZero(coreHandle, "FMOD_System_MixerResume");
            _folder = folder;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ParameterDescription
    {
        public IntPtr Name;
        public int Index;
        public float Minimum;
        public float Maximum;
        public float DefaultValue;
        public int Type;
    }

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_Create(out IntPtr system, uint headerVersion);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_Initialize(IntPtr system, int maxChannels, uint studioFlags, uint flags, IntPtr extraDriverData);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_Release(IntPtr system);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_RegisterPlugin(IntPtr system, IntPtr description);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_Update(IntPtr system);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_LoadBankFile(IntPtr system, byte[] fileNameUtf8, uint flags, out IntPtr bank);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_GetEventByID(IntPtr system, ref Guid id, out IntPtr description);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_Bank_Unload(IntPtr bank);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventDescription_CreateInstance(IntPtr description, out IntPtr instance);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventDescription_LoadSampleData(IntPtr description);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventDescription_GetSampleLoadingState(IntPtr description, out int state);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventDescription_GetParameterCount(IntPtr description, out int count);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventDescription_GetParameterByIndex(IntPtr description, int index, out ParameterDescription parameter);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_Start(IntPtr instance);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_Stop(IntPtr instance, int mode);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_Release(IntPtr instance);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_SetVolume(IntPtr instance, float volume);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_SetParameterValue(IntPtr instance, byte[] nameUtf8, float value);

    /// <summary>A vector in FMOD's space: x right, y up, z ahead of the listener</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector
    {
        public float X;
        public float Y;
        public float Z;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Attributes3D
    {
        public Vector Position;
        public Vector Velocity;
        public Vector Forward;
        public Vector Up;
    }

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_Set3DAttributes(IntPtr instance, ref Attributes3D attributes);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_GetLowLevelSystem(IntPtr system, out IntPtr coreSystem);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_System_SetOutput(IntPtr system, int output);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_System_CreateDSP(IntPtr system, IntPtr description, out IntPtr dsp);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_System_GetMasterChannelGroup(IntPtr system, out IntPtr channelGroup);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_ChannelGroup_AddDSP(IntPtr channelGroup, int index, IntPtr dsp);

    /// <summary>A new group, mixed into the master group</summary>
    [LibraryImport(CoreDll)]
    public static partial int FMOD_System_CreateChannelGroup(IntPtr system, byte[] nameUtf8, out IntPtr channelGroup);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_ChannelGroup_SetVolume(IntPtr channelGroup, float volume);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_ChannelGroup_Release(IntPtr channelGroup);

    // FMOD_MODE: looping (the channel's loop count then says how often), no 3D, read from disk as it plays, and an
    // mp3's length found by reading it through rather than guessed from its first frames
    public const uint ModeLoopNormal = 0x2;
    public const uint Mode2D = 0x8;
    public const uint ModeCreateStream = 0x80;
    public const uint ModeAccurateTime = 0x4000;

    // FMOD_TIMEUNIT_MS
    public const uint TimeUnitMs = 1;

    [LibraryImport(CoreDll)]
    public static partial int FMOD_System_CreateSound(IntPtr system, byte[] nameUtf8, uint mode, IntPtr exInfo, out IntPtr sound);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_System_PlaySound(IntPtr system, IntPtr sound, IntPtr channelGroup, int paused, out IntPtr channel);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_Sound_Release(IntPtr sound);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_Sound_GetLength(IntPtr sound, out uint length, uint unit);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_Stop(IntPtr channel);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_SetPaused(IntPtr channel, int paused);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_IsPlaying(IntPtr channel, out int playing);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_GetPosition(IntPtr channel, out uint position, uint unit);

    /// <summary>-1 loops for ever, 0 plays once</summary>
    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_SetLoopCount(IntPtr channel, int loopCount);

    // FMOD_LOOP_OFF; FMOD_CREATESAMPLE: decoded whole into memory as it opens, the file closed again
    public const uint ModeLoopOff = 0x1;
    public const uint ModeCreateSample = 0x100;

    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_SetVolume(IntPtr channel, float volume);

    /// <summary>A playback speed: 1 as recorded, 1.05 a semitone's worth higher</summary>
    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_SetPitch(IntPtr channel, float pitch);

    /// <summary>The mixer's clock, in samples: the channel's own and its group's (what fade points and delays go by)</summary>
    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_GetDSPClock(IntPtr channel, out ulong dspClock, out ulong parentClock);

    /// <summary>A volume the channel is at when the group's clock reaches <paramref name="dspClock"/>; ramps between points</summary>
    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_AddFadePoint(IntPtr channel, ulong dspClock, float volume);

    /// <summary>Starts at the group's clock <paramref name="start"/> (0: now) and stops at <paramref name="end"/> (0: never)</summary>
    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_SetDelay(IntPtr channel, ulong start, ulong end, int stopChannels);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_RemoveFadePoints(IntPtr channel, ulong start, ulong end);

    // FMOD_OPENMEMORY: the sound is read out of memory (copied by FMOD), the file never opened; FMOD_OPENONLY: opened
    // to read what it is (length, tags), not decoded
    public const uint ModeOpenMemory = 0x800;
    public const uint ModeOpenOnly = 0x2000;

    /// <summary>FMOD_CREATESOUNDEXINFO of 1.08 on x64: its size first, the length of the data in memory after it</summary>
    public const int CreateSoundExInfoSize = 216;

    /// <summary>A sound out of memory: <paramref name="data"/> points at the file's bytes, <paramref name="exInfo"/> says how many</summary>
    [LibraryImport(CoreDll, EntryPoint = "FMOD_System_CreateSound")]
    public static partial int FMOD_System_CreateSoundFromMemory(IntPtr system, IntPtr data, uint mode, IntPtr exInfo, out IntPtr sound);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_Channel_SetPosition(IntPtr channel, uint position, uint unit);

    /// <summary>FMOD_TAG: its type, how its data is written (<see cref="TagString"/> and on), its name, the data</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Tag
    {
        public int Type;
        public int DataType;
        public IntPtr Name;
        public IntPtr Data;
        public uint DataLength;
        public int Updated;
    }

    // FMOD_TAGDATATYPE
    public const int TagInt = 1;
    public const int TagString = 3;
    public const int TagStringUtf16 = 4;
    public const int TagStringUtf16BigEndian = 5;
    public const int TagStringUtf8 = 6;

    [LibraryImport(CoreDll)]
    public static partial int FMOD_Sound_GetNumTags(IntPtr sound, out int tags, out int updated);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_Sound_GetTag(IntPtr sound, IntPtr name, int index, out Tag tag);

    // FMOD_DSP_TYPE of 1.08 (not 2.x's numbers): what the AM radio is made of
    public const int DspLowpass = 3;
    public const int DspHighpass = 5;
    public const int DspDistortion = 9;
    public const int DspParamEq = 12;
    public const int DspCompressor = 18;
    public const int DspChannelMix = 33;

    [LibraryImport(CoreDll)]
    public static partial int FMOD_System_CreateDSPByType(IntPtr system, int type, out IntPtr dsp);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_DSP_SetParameterFloat(IntPtr dsp, int index, float value);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_DSP_SetParameterInt(IntPtr dsp, int index, int value);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_DSP_SetBypass(IntPtr dsp, int bypass);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_DSP_SetWetDryMix(IntPtr dsp, float preWet, float postWet, float dry);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_System_GetSoftwareFormat(IntPtr system, out int sampleRate, out int speakerMode, out int rawSpeakers);

    #region Calls 1.08 may not have

    // Looked up by name rather than imported: an import that is not there only fails when it is called, and each
    // of these has a way round it

    private static IntPtr ExportOrZero(IntPtr library, string name) =>
        NativeLibrary.TryGetExport(library, name, out var function) ? function : IntPtr.Zero;

    /// <summary>Runs every command queued so far, and waits for it; null when the DLL does not have it</summary>
    public static unsafe int? TryFlushCommands(IntPtr system) =>
        _flushCommands != IntPtr.Zero ? ((delegate* unmanaged<IntPtr, int>)_flushCommands)(system) : null;

    /// <summary>Where a bank is in loading or unloading; null when the DLL cannot say</summary>
    public static unsafe int? TryGetBankLoadingState(IntPtr bank, out int state)
    {
        state = LoadingStateError;
        if (_bankGetLoadingState == IntPtr.Zero) return null;

        int value = LoadingStateError;
        var result = ((delegate* unmanaged<IntPtr, int*, int>)_bankGetLoadingState)(bank, &value);
        state = value;
        return result;
    }

    /// <summary>Stops the mixer and its output thread for as long as nothing is to be heard; null when the DLL does not have it</summary>
    public static unsafe int? TryMixerSuspend(IntPtr coreSystem) =>
        _mixerSuspend != IntPtr.Zero ? ((delegate* unmanaged<IntPtr, int>)_mixerSuspend)(coreSystem) : null;

    public static unsafe int? TryMixerResume(IntPtr coreSystem) =>
        _mixerResume != IntPtr.Zero ? ((delegate* unmanaged<IntPtr, int>)_mixerResume)(coreSystem) : null;

    #endregion

    public static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text + "\0");

    /// <summary>For setting up: a call that fails means the thing cannot be done at all</summary>
    public static void Check(int result, string what)
    {
        if (result != ResultOk) throw new FmodException(what, result);
    }

    // Failures already reported, by what failed: the first time, then at most once every few seconds with a count
    private static readonly TimeSpan WarnInterval = TimeSpan.FromSeconds(10);
    private static readonly Dictionary<string, (DateTime Last, int Suppressed)> Warned = new();

    /// <summary>
    /// For playing: a call that fails is logged, and whatever it was for goes on without it. Many of these run
    /// every frame, so the same failure is logged once and then at most every few seconds, with how many went unsaid.
    /// </summary>
    /// <returns>True when the call went through</returns>
    public static bool Warn(int result, string what)
    {
        if (result == ResultOk) return true;

        var now = DateTime.UtcNow;
        int suppressed;
        lock (Warned)
        {
            if (Warned.TryGetValue(what, out var known) && now - known.Last < WarnInterval)
            {
                Warned[what] = (known.Last, known.Suppressed + 1);
                return false;
            }

            suppressed = known.Suppressed;
            Warned[what] = (now, 0);
        }

        if (suppressed > 0) Logger.Warning("FMOD {What} failed: error {Code} ({Suppressed} more since the last report)", what, result, suppressed);
        else Logger.Warning("FMOD {What} failed: error {Code}", what, result);
        return false;
    }

    /// <summary>The highest value a parameter of an event goes to; null when the event has no such parameter</summary>
    public static float? ParameterMaximum(IntPtr description, string name)
    {
        if (FMOD_Studio_EventDescription_GetParameterCount(description, out var count) != ResultOk) return null;
        for (var i = 0; i < count; i++)
        {
            if (FMOD_Studio_EventDescription_GetParameterByIndex(description, i, out var parameter) != ResultOk) continue;
            if (string.Equals(Marshal.PtrToStringUTF8(parameter.Name), name, StringComparison.OrdinalIgnoreCase))
                return parameter.Maximum;
        }

        return null;
    }
}

public sealed class FmodException(string what, int code) : Exception($"FMOD {what} failed: error {code}")
{
    public int Code { get; } = code;
}
