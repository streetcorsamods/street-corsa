using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using StreetCorsa.Logging;

namespace StreetCorsa.Audio.Radio
{
    /// <summary>
    /// The radio's keys while Assetto Corsa has the keyboard: a low-level keyboard hook that only listens (every key goes
    /// on to AC as before), set while AC runs and taken off after. R switches the radio on and off, the media keys play,
    /// pause and change the song. Only while another program is in front: in the game's own window its keys do this.
    /// </summary>
    public sealed class RaceKeys
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;

        // Virtual keys
        private const int VkR = 0x52;
        private const int VkShift = 0x10;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkMediaNext = 0xB0;
        private const int VkMediaPrevious = 0xB1;
        private const int VkMediaStop = 0xB2;
        private const int VkMediaPlayPause = 0xB3;

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInfo
        {
            public int VkCode;
            public int ScanCode;
            public int Flags;
            public int Time;
            public IntPtr ExtraInfo;
        }

        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr info);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hook, HookProc proc, IntPtr module, uint thread);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr info);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? module);

        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("Radio");
        private readonly Dispatcher _dispatcher;
        private readonly HookProc _proc;
        private readonly HashSet<int> _held = [];
        private IntPtr _hook;

        public RaceKeys(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;

            // Kept in a field: a delegate handed to Windows and collected is a crash at the next key
            _proc = OnKey;
        }

        public bool IsListening => _hook != IntPtr.Zero;

        /// <summary>What a key does; the radio's keys unless a test puts in a key of its own (one nobody presses)</summary>
        internal Func<int, Action?> Map { get; set; } = Action;

        /// <summary>Starts listening; on the UI thread, whose message loop the hook's calls come through</summary>
        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                _logger.Warning("The radio's keys cannot be heard in the race (error {Code})", new Win32Exception(Marshal.GetLastWin32Error()).Message);
            else
                _logger.Information("Radio keys listened for while AC runs: R on and off, the media keys");
        }

        public void Stop()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _held.Clear();
        }

        /// <summary>
        /// Windows asks this of every key, and waits for it: it only notes the key and answers at once, the radio acting
        /// on it a moment later on the UI thread. A key held down counts once.
        /// </summary>
        private IntPtr OnKey(int code, IntPtr message, IntPtr info)
        {
            if (code >= 0)
            {
                try
                {
                    var key = Marshal.PtrToStructure<KeyboardInfo>(info).VkCode;
                    var kind = (int)message;
                    if (kind is WmKeyUp or WmSysKeyUp) _held.Remove(key);
                    else if (kind is WmKeyDown or WmSysKeyDown && _held.Add(key) && Map(key) is { } action && !OursInFront())
                        _dispatcher.BeginInvoke(action);
                }
                catch (Exception ex)
                {
                    // Nothing may escape into Windows' call
                    _logger.Debug("A key in the race could not be read: {Error}", ex.Message);
                }
            }

            return CallNextHookEx(_hook, code, message, info);
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

        /// <summary>The game's own window is in front: its own key handlers have the key</summary>
        private static bool OursInFront()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var process);
            return process == (uint)Environment.ProcessId;
        }
    }
}
