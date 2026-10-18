using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using StreetCorsa.Logging;
using StreetCorsa.Services;

namespace StreetCorsa.Audio.Radio
{
    /// <summary>
    /// The radio's keys while Assetto Corsa has the keyboard: a low-level keyboard hook that only listens (every key goes
    /// on to AC as before), set while AC runs and taken off after. R switches the radio on and off, the media keys play,
    /// pause and change the song. Only while AC's own window is in front: in the game's window its own keys do this, and
    /// a key typed into any other program (a chat, a browser) is that program's.
    /// </summary>
    public sealed partial class RaceKeys
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const uint WmQuit = 0x0012;
        private const uint PmNoRemove = 0x0000;
        private const uint ProcessQueryLimitedInformation = 0x1000;

        // Virtual keys
        private const int VkR = 0x52;
        private const int VkShift = 0x10;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkMediaNext = 0xB0;
        private const int VkMediaPrevious = 0xB1;
        private const int VkMediaStop = 0xB2;
        private const int VkMediaPlayPause = 0xB3;

        // How long a stop waits for the hook's thread to end before leaving it to end on its own: a moment, on the UI
        // thread at the end of a race
        private static readonly TimeSpan StopWait = TimeSpan.FromMilliseconds(250);

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInfo
        {
            public int VkCode;
            public int ScanCode;
            public int Flags;
            public int Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr Window;
            public uint Id;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int X;
            public int Y;
        }

        [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        private static partial IntPtr SetWindowsHookEx(int hook, IntPtr proc, IntPtr module, uint thread);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnhookWindowsHookEx(IntPtr hook);

        [LibraryImport("user32.dll")]
        private static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr info);

        [LibraryImport("user32.dll")]
        private static partial short GetAsyncKeyState(int key);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(IntPtr window, out uint process);

        [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
        private static partial int GetMessage(out Message message, IntPtr window, uint first, uint last);

        [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint remove);

        [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);

        [LibraryImport("kernel32.dll")]
        private static partial uint GetCurrentThreadId();

        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr GetModuleHandle(string? module);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint process);

        [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool QueryFullProcessImageName(IntPtr process, uint flags, char* name, ref uint length);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(IntPtr handle);

        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Radio");

        // The hook that is set: Windows calls a static function, which hands each key to its keys. Per start, so a hook
        // still coming off at the next start (its thread a moment behind) cannot take the new one's place away
        private static volatile HookThread? _listening;

        private readonly Action<Action> _post;

        // The keys down, and the window-in-front cache: on the hook's thread, under the lock for the moment two hooks
        // overlap (one coming off as the next is set)
        private readonly HashSet<int> _held = [];
        private HookThread? _current;

        // The game's process name, and the last window in front's process and whether it was the game's: looked up again
        // only when another program comes to the front
        private string _game = AcProcesses.Race;
        private uint _lastInFront;
        private bool _lastWasGame;

        /// <param name="post">Hands what a key does to the radio's thread (the UI thread), without waiting for it</param>
        public RaceKeys(Action<Action> post)
        {
            _post = post;
        }

        public bool IsListening => _current is { Hooked: true };

        /// <summary>What a key does; the radio's keys unless a test puts in a key of its own (one nobody presses)</summary>
        internal Func<int, Action?> Map { get; set; } = Action;

        /// <summary>The process whose window is in front; Windows' answer unless a test gives its own</summary>
        internal Func<uint> ProcessInFront { get; set; } = InFront;

        /// <summary>A process's name by its id (null when it cannot be told); Windows' answer unless a test gives its own</summary>
        internal Func<uint, string?> ProcessName { get; set; } = NameOf;

        /// <summary>
        /// Starts listening while <paramref name="game"/> (a process name: AC's race) runs. The hook is set on a thread of
        /// its own with its own message loop, so a busy UI thread never holds up AC's keys; and the start does not wait
        /// for it (the race's start goes on, the hook is there a moment later). Set afresh at every start: a hook Windows
        /// took off (an answer once too slow) cannot be told from one that is still set.
        /// </summary>
        public void Start(string game = AcProcesses.Race)
        {
            Stop();
            _game = game;
            lock (_held)
            {
                _lastInFront = 0;
                _held.Clear();
            }

            var listening = new HookThread(this);
            var thread = new Thread(() => Listen(listening)) { IsBackground = true, Name = "Radio race keys" };
            _current = listening;
            thread.Start();
        }

        /// <summary>
        /// Takes the hook off: the thread is told to stop and waited for a moment, then left to finish on its own (it
        /// unhooks in its finally whenever it gets there). A thread that has not yet set its hook turns back when it does.
        /// </summary>
        public void Stop()
        {
            if (_current is not { } current) return;
            _current = null;

            var told = current.AskStop(id =>
            {
                if (PostThreadMessage(id, WmQuit, IntPtr.Zero, IntPtr.Zero)) return true;
                Logger.Debug("The radio's keys could not be told to stop (error {Code}); they go with the app", Marshal.GetLastPInvokeError());
                return false;
            });
            if (told && !current.Thread.Join(StopWait)) Logger.Debug("The radio's keys are still coming off; they finish on their own");
        }

        /// <summary>The hook's thread: sets it, and runs the message loop its calls come through until told to stop</summary>
        private void Listen(HookThread listening)
        {
            // The thread's message queue made before the thread says where it is, so the stop's message has somewhere to go
            PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
            if (!listening.Arrived(GetCurrentThreadId())) return;

            IntPtr hook;
            unsafe
            {
                hook = SetWindowsHookEx(WhKeyboardLl, (IntPtr)(delegate* unmanaged<int, IntPtr, IntPtr, IntPtr>)&OnKey, GetModuleHandle(null), 0);
            }

            if (hook == IntPtr.Zero)
            {
                Logger.Warning("The radio's keys cannot be heard in the race (error {Code})", new Win32Exception(Marshal.GetLastPInvokeError()).Message);
                return;
            }

            _listening = listening;
            listening.Hooked = true;
            Logger.Information("Radio keys listened for while AC runs: R on and off, the media keys");
            try
            {
                while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
                {
                    // Nothing comes here but the stop: the hook's calls are made inside GetMessage
                }
            }
            finally
            {
                UnhookWindowsHookEx(hook);
                listening.Hooked = false;
                if (ReferenceEquals(_listening, listening)) _listening = null;
            }
        }

        /// <summary>
        /// Windows asks this of every key, and waits for it: it only notes the key and answers at once, the radio acting
        /// on it a moment later on the UI thread. On the hook's thread.
        /// </summary>
        [UnmanagedCallersOnly]
        private static unsafe IntPtr OnKey(int code, IntPtr message, IntPtr info)
        {
            if (code >= 0 && _listening is { } listening)
            {
                try
                {
                    listening.Owner.Heard(((KeyboardInfo*)info)->VkCode, (int)message);
                }
                catch (Exception ex)
                {
                    // Nothing may escape into Windows' call
                    try
                    {
                        Logger.Debug("A key in the race could not be read: {Error}", ex.Message);
                    }
                    catch
                    {
                        // Not even the log's own trouble
                    }
                }
            }

            return CallNextHookEx(IntPtr.Zero, code, message, info);
        }

        /// <summary>
        /// A key went down or up: the radio's key acts when AC's window is in front, a key held down once. True when it
        /// acted.
        /// </summary>
        internal bool Heard(int key, int kind)
        {
            Action action;
            lock (_held)
            {
                if (kind is WmKeyUp or WmSysKeyUp)
                {
                    _held.Remove(key);
                    return false;
                }

                if (kind is not (WmKeyDown or WmSysKeyDown) || !_held.Add(key) || Map(key) is not { } mapped || !GameInFront()) return false;
                action = mapped;
            }

            _post(action);
            return true;
        }

        /// <summary>
        /// AC's window is in front: not the game's own (its own key handlers have the key), nor any other program's. Asked
        /// inside Windows' call, so the name is looked up only when another window comes to the front, and cheaply
        /// (<see cref="NameOf"/>): an answer too slow has Windows take the hook off for good.
        /// </summary>
        private bool GameInFront()
        {
            var process = ProcessInFront();
            if (process == 0) return false;
            if (process != _lastInFront)
            {
                _lastInFront = process;
                _lastWasGame = string.Equals(ProcessName(process), _game, StringComparison.OrdinalIgnoreCase);
            }

            return _lastWasGame;
        }

        /// <summary>What a key does to the radio; null for a key that is not the radio's (R with a modifier is AC's)</summary>
        private static Action? Action(int key) => key switch
        {
            VkR when !Down(VkShift) && !Down(VkControl) && !Down(VkMenu) => RadioPlayer.Shared.TogglePower,
            VkMediaPlayPause => RadioPlayer.Shared.TogglePlay,
            VkMediaNext => RadioPlayer.Shared.Next,
            VkMediaPrevious => RadioPlayer.Shared.Previous,
            VkMediaStop => RadioPlayer.Shared.Stop,
            _ => null
        };

        private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

        private static uint InFront()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var process);
            return process;
        }

        /// <summary>
        /// A process's name ("acs" for acs.exe) by its id, straight from its image path: one handle opened with the least
        /// right and one call, not a snapshot of every process on the machine (what Process.GetProcessById takes, tens of
        /// milliseconds on a loaded one). Null when it is gone, or not ours to look at: not the game.
        /// </summary>
        internal static unsafe string? NameOf(uint process)
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, process);
            if (handle == IntPtr.Zero) return null;
            try
            {
                const int most = 1024;
                var path = stackalloc char[most];
                var length = (uint)most;
                return QueryFullProcessImageName(handle, 0, path, ref length) && length > 0
                    ? Path.GetFileNameWithoutExtension(new string(path, 0, (int)length))
                    : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>
        /// One setting of the hook: its thread, and the handshake between the thread that starts and stops it (the UI
        /// thread) and the hook's own thread, which gets going on its own time. The one side writes its word and then
        /// reads the other's, so whichever comes second sees the first: a stop asked before the thread has said where it
        /// is turns the thread back at its start, one asked after is posted to it.
        /// </summary>
        internal sealed class HookThread(RaceKeys owner)
        {
            private volatile uint _threadId;
            private volatile bool _stopAsked;

            public RaceKeys Owner { get; } = owner;

            /// <summary>The thread the hook is set on; the calling thread until <see cref="Arrived"/> (a test's)</summary>
            public Thread Thread { get; private set; } = Thread.CurrentThread;

            /// <summary>The hook is set (and not yet taken off)</summary>
            public volatile bool Hooked;

            /// <summary>On the hook's thread, its message queue made: true when it may go on and set the hook</summary>
            public bool Arrived(uint threadId)
            {
                Thread = Thread.CurrentThread;
                _threadId = threadId;
                Interlocked.MemoryBarrier();
                return !_stopAsked;
            }

            /// <summary>
            /// Asks the thread to stop: <paramref name="post"/> is given its id when it has arrived (true when the post
            /// went), else nothing, the thread turning back when it arrives
            /// </summary>
            public bool AskStop(Func<uint, bool> post)
            {
                _stopAsked = true;
                Interlocked.MemoryBarrier();
                var id = _threadId;
                return id != 0 && post(id);
            }

            public bool StopAsked => _stopAsked;
        }
    }
}
