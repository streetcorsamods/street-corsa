using System.Diagnostics;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Settings
{
    /// <summary>
    /// Which setting a run plays, worked out at start-up before any content is read (<see cref="ModSet.PlayedSetting"/>),
    /// and the restart that changes it: the content is read once, so another setting is another run.
    /// </summary>
    public static class SettingStart
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Startup);

        /// <summary>The argument a restart starts the game with: the earlier run's process id follows it</summary>
        public const string AfterSwitch = "--after";

        // How long a restarted game waits for the run before it to close (it puts the AC install back first)
        private static readonly TimeSpan WaitForEarlier = TimeSpan.FromSeconds(60);

        /// <summary>
        /// The setting to play: the one the player picked when it is installed, the only one when there is one, none
        /// for the bare core; <paramref name="ask"/> when several are installed and none of them was picked
        /// </summary>
        public static ModInfo? Decide(IReadOnlyList<ModInfo> settings, string? picked, out bool ask)
        {
            ask = false;
            var known = settings.FirstOrDefault(m => m.Id.Equals(picked, StringComparison.OrdinalIgnoreCase));
            if (known != null || settings.Count <= 1) return known ?? settings.FirstOrDefault();

            ask = true;
            return null;
        }

        /// <summary>
        /// Starts the game again, waiting for this run to close (<see cref="AwaitEarlier"/>); the caller shuts this one
        /// down. False when it could not be started.
        /// </summary>
        public static bool Restart()
        {
            try
            {
                var program = Environment.ProcessPath;
                if (string.IsNullOrEmpty(program)) return false;

                var start = new ProcessStartInfo(program) { UseShellExecute = false };
                start.ArgumentList.Add(AfterSwitch);
                start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using var process = Process.Start(start);
                return process != null;
            }
            catch (Exception ex)
            {
                Logger.Warning("The game could not be started again: {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// A restarted game (<see cref="AfterSwitch"/>) waits for the run before it to close, before it takes the one
        /// copy of the game. Never throws.
        /// </summary>
        public static void AwaitEarlier(IReadOnlyList<string> args)
        {
            var at = args.ToList().FindIndex(a => string.Equals(a, AfterSwitch, StringComparison.OrdinalIgnoreCase));
            if (at < 0 || at + 1 >= args.Count || !int.TryParse(args[at + 1], out var id)) return;

            try
            {
                using var earlier = Process.GetProcessById(id);
                earlier.WaitForExit(WaitForEarlier);
            }
            catch (Exception)
            {
                // Gone already, or not ours to wait on: the single-instance check says the rest
            }
        }
    }
}
