using System.Diagnostics;
using System.Globalization;
using StreetCorsa.Logging;

namespace StreetCorsa.Services.Settings
{
    /// <summary>What a run does once it is up, when a restart into another setting asked for it</summary>
    public enum StartKind
    {
        /// <summary>The main menu, after the intro</summary>
        Menu,

        /// <summary>The New Game card, the player's name filled in</summary>
        NewGame,

        /// <summary>A save opened</summary>
        LoadGame
    }

    /// <summary>What a run starts on: <see cref="StartKind"/>, and the name or save it goes with</summary>
    public sealed record StartAfter(StartKind Kind, string Value)
    {
        public static readonly StartAfter Menu = new(StartKind.Menu, string.Empty);
    }

    /// <summary>
    /// A career's setting is played in a run of its own: the content is read once, at start-up, in the setting
    /// settings.json remembers (the last one played). Another setting is another run: the game starts again in it, on
    /// what the player was doing (a new game, a save), and this run closes.
    /// </summary>
    public static class SettingStart
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Startup);

        /// <summary>The argument a restart starts the game with: the earlier run's process id follows it</summary>
        public const string AfterSwitch = "--after";

        /// <summary>What the restarted run starts on: <c>--then new-game &lt;name&gt;</c>, <c>--then load &lt;save&gt;</c></summary>
        public const string ThenSwitch = "--then";

        private const string NewGameWord = "new-game";
        private const string LoadWord = "load";

        // How long a restarted game waits for the run before it to close (it puts the AC install back first)
        private static readonly TimeSpan WaitForEarlier = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Starts the game again on <paramref name="then"/>, waiting for this run to close (<see cref="AwaitEarlier"/>);
        /// the caller shuts this one down. False when it could not be started.
        /// </summary>
        public static bool Restart(StartAfter then)
        {
            try
            {
                var program = Environment.ProcessPath;
                if (string.IsNullOrEmpty(program)) return false;

                var start = new ProcessStartInfo(program) { UseShellExecute = false };
                foreach (var argument in Arguments(Environment.ProcessId, then)) start.ArgumentList.Add(argument);
                using var process = Process.Start(start);
                return process != null;
            }
            catch (Exception ex)
            {
                Logger.Warning("The game could not be started again: {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>The arguments a restart is started with</summary>
        public static IReadOnlyList<string> Arguments(int earlier, StartAfter then)
        {
            var arguments = new List<string> { AfterSwitch, earlier.ToString(CultureInfo.InvariantCulture) };
            if (then.Kind == StartKind.Menu) return arguments;

            arguments.Add(ThenSwitch);
            arguments.Add(then.Kind == StartKind.NewGame ? NewGameWord : LoadWord);
            arguments.Add(then.Value);
            return arguments;
        }

        /// <summary>What the arguments say to start on; the menu when they say nothing that reads</summary>
        public static StartAfter Then(IReadOnlyList<string> args)
        {
            var at = IndexOf(args, ThenSwitch);
            if (at < 0 || at + 1 >= args.Count) return StartAfter.Menu;

            var value = at + 2 < args.Count ? args[at + 2] : string.Empty;
            if (string.Equals(args[at + 1], NewGameWord, StringComparison.OrdinalIgnoreCase)) return new StartAfter(StartKind.NewGame, value);
            if (string.Equals(args[at + 1], LoadWord, StringComparison.OrdinalIgnoreCase) && value.Length > 0) return new StartAfter(StartKind.LoadGame, value);
            return StartAfter.Menu;
        }

        /// <summary>
        /// A restarted game (<see cref="AfterSwitch"/>) waits for the run before it to close, before it takes the one
        /// copy of the game. Never throws.
        /// </summary>
        public static void AwaitEarlier(IReadOnlyList<string> args)
        {
            var at = IndexOf(args, AfterSwitch);
            if (at < 0 || at + 1 >= args.Count || !int.TryParse(args[at + 1], out var id)) return;

            try
            {
                // Looked at now and then, never waited on: a wait on the app's thread (WaitForExit) pumps its messages,
                // and the start-up the Application constructor has queued would run before the app is made
                using var earlier = Process.GetProcessById(id);
                var waited = Stopwatch.StartNew();
                while (!earlier.HasExited && waited.Elapsed < WaitForEarlier) Thread.Sleep(100);
            }
            catch (Exception)
            {
                // Gone already, or not ours to wait on: the single-instance check says the rest
            }
        }

        private static int IndexOf(IReadOnlyList<string> args, string name) =>
            args.ToList().FindIndex(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }
}
