using System.ComponentModel;
using System.Diagnostics;
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

        // Virtual keys
        private const int VkR = 0x52;
        private const int VkShift = 0x10;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkMediaNext = 0xB0;
        private const int VkMediaPrevious = 0xB1;
        private const int VkMediaStop = 0xB2;
        private const int VkMediaPlayPause = 0xB3;

        // How long a start waits for the hook's thread to set it, and a stop for it to end
        private static readonly TimeSpan ThreadWait = TimeSpan.FromSeconds(2);

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

        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Radio");

        // The keys whose hook is set: Windows calls a static function, which hands each key to them
        private static volatile RaceKeys? _listening;

        private readonly Action<Action> _post;
        private readonly HashSet<int> _held = [];
        private volatile bool _hooked;
        private Thread? _thread;
        private uint _threadId;

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

        public bool IsListening => _hooked;

        /// <summary>What a key does; the radio's keys unless a test puts in a key of its own (one nobody presses)</summary>
        internal Func<int, Action?> Map { get; set; } = Action;

        /// <summary>The process whose window is in front; Windows' answer unless a test gives its own</summary>
        internal Func<uint> ProcessInFront { get; set; } = InFront;

        /// <summary>A process's name by its id (null when it cannot be told); Windows' answer unless a test gives its own</summary>
        internal Func<uint, string?> ProcessName { get; set; } = NameOf;

        /// <summary>
        /// Starts listening while <paramref name="game"/> (a process name: AC's race) runs. The hook is set on a thread of
        /// its own with its own message loop, so a busy UI thread never holds up AC's keys. Set afresh at every start: a
        /// hook Windows took off (an answer once too slow) cannot be told from one that is still set.
        /// </summary>
        public void Start(string game = AcProcesses.Race)
        {
            Stop();
            _game = game;
            _lastInFront = 0;

            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() => Listen(ready)) { IsBackground = true, Name = "Radio race keys" };
            _thread = thread;
            thread.Start();
            if (!ready.Wait(ThreadWait)) Logger.Warning("The radio's keys took too long to be set for the race");
        }

        public void Stop()
        {
            if (_thread is not { } thread) return;
            _thread = null;
            if (!PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero))
                Logger.Debug("The radio's keys could not be told to stop (error {Code})", Marshal.GetLastPInvokeError());
            else if (!thread.Join(ThreadWait))
                Logger.Warning("The radio's keys did not stop in time: they go with the app");
        }

        /// <summary>The hook's thread: sets it, and runs the message loop its calls come through until told to stop</summary>
        private void Listen(ManualResetEventSlim ready)
        {
            IntPtr hook;
            try
            {
                _threadId = GetCurrentThreadId();

                // The thread's message queue made before the start goes on, so the stop's message has somewhere to go
                PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
                _held.Clear();
                _listening = this;
                unsafe
                {
                    hook = SetWindowsHookEx(WhKeyboardLl, (IntPtr)(delegate* unmanaged<int, IntPtr, IntPtr, IntPtr>)&OnKey, GetModuleHandle(null), 0);
                }

                if (hook == IntPtr.Zero)
                {
                    _listening = null;
                    Logger.Warning("The radio's keys cannot be heard in the race (error {Code})", new Win32Exception(Marshal.GetLastPInvokeError()).Message);
                    return;
                }

                _hooked = true;
                Logger.Information("Radio keys listened for while AC runs: R on and off, the media keys");
            }
            finally
            {
                ready.Set();
            }

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
                _hooked = false;
                if (ReferenceEquals(_listening, this)) _listening = null;
            }
        }

        /// <summary>
        /// Windows asks this of every key, and waits for it: it only notes the key and answers at once, the radio acting
        /// on it a moment later on the UI thread. On the hook's thread.
        /// </summary>
        [UnmanagedCallersOnly]
        private static unsafe IntPtr OnKey(int code, IntPtr message, IntPtr info)
        {
            if (code >= 0 && _listening is { } keys)
            {
                try
                {
                    keys.Heard(((KeyboardInfo*)info)->VkCode, (int)message);
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
            if (kind is WmKeyUp or WmSysKeyUp)
            {
                _held.Remove(key);
                return false;
            }

            if (kind is not (WmKeyDown or WmSysKeyDown) || !_held.Add(key) || Map(key) is not { } action || !GameInFront()) return false;
            _post(action);
            return true;
        }

        /// <summary>AC's window is in front: not the game's own (its own key handlers have the key), nor any other program's</summary>
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

        private static string? NameOf(uint process)
        {
            try
            {
                using var found = Process.GetProcessById((int)process);
                return found.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Gone already, or not ours to look at: not the game
                return null;
            }
        }
    }
}
