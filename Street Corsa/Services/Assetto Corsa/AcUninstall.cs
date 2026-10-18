using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Services.Configuration;

namespace StreetCorsa.Services
{
    /// <summary>
    /// <c>"Street Corsa.exe" --uninstall</c>, which the installer runs before it takes the game away: whatever a race left
    /// changed goes back (as at every start), and the game's CSP modes leave the AC install. No window; the saves, the
    /// settings and the log stay in %AppData%\StreetCorsa. Never throws: an uninstall goes ahead whatever this managed.
    /// There is no next start to leave anything for, so each part is tried on its own, whatever became of the others.
    ///
    /// What it tells the installer is its exit code (<see cref="ExitDone"/>, <see cref="ExitAcRunning"/>, <see cref="ExitFailed"/>):
    /// the installer's RestoreAc action (installer/Actions/CustomActions.cs) reads it, and on <see cref="ExitAcRunning"/>
    /// asks the player to close Assetto Corsa and retry, instead of going on with a race's changes left in it.
    /// </summary>
    public static class AcUninstall
    {
        public const string Switch = "--uninstall";

        /// <summary>Everything that could be done was: the install is as before the game, or there was nothing to put back</summary>
        public const int ExitDone = 0;

        /// <summary>The clean-up itself failed (logged); nothing a retry would mend, the uninstall goes on</summary>
        public const int ExitFailed = 1;

        /// <summary>
        /// Assetto Corsa was still running after the wait, with a race's changes in it: nothing was put back and the modes
        /// stay with it. The installer asks the player to close it and retry.
        /// </summary>
        public const int ExitAcRunning = 3;

        /// <summary>
        /// How long a running AC is waited for before the installer is told: the player is in front of the uninstall's
        /// progress bar, and the installer's retry dialog carries the longer wait from there
        /// </summary>
        private static readonly TimeSpan AcExitWait = TimeSpan.FromSeconds(30);

        private static readonly TimeSpan AcExitPoll = TimeSpan.FromSeconds(1);

        public static bool Asked(IEnumerable<string> args) => args.Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// What the uninstall does once AC has or has not closed: whether a race's changes are put back, whether the modes
        /// leave the install, and the exit code. Apart from the processes and the files, so it can be tested.
        /// </summary>
        /// <param name="RestoreRace">The car data and cfg files a race changed go back (only with no AC running to read them)</param>
        /// <param name="RemoveModes">The game's CSP modes leave the AC install</param>
        public sealed record Plan(bool RestoreRace, bool RemoveModes, int ExitCode);

        /// <param name="acClosed">No AC process runs (or none did), after the wait</param>
        /// <param name="acInstalled">The folder the settings name holds AC: there are modes to take out</param>
        /// <param name="hasLeftovers">Whether a race's changes wait to be put back; asked only when AC still runs</param>
        public static Plan Decide(bool acClosed, bool acInstalled, Func<bool> hasLeftovers)
        {
            // AC closed: everything goes back, and the modes go when there is an install to take them out of
            if (acClosed) return new Plan(RestoreRace: true, RemoveModes: acInstalled, ExitDone);

            // A race left waiting to go back runs in its mode: that stays with it, and the installer is told to ask for
            // a retry. Nothing waiting, the modes go even under a running AC (AC reads a mode only when a race starts)
            return hasLeftovers()
                ? new Plan(RestoreRace: false, RemoveModes: false, ExitAcRunning)
                : new Plan(RestoreRace: false, RemoveModes: acInstalled, ExitDone);
        }

        /// <summary>The clean-up; its exit code is the process's</summary>
        public static int Run()
        {
            // A log that cannot be opened is no reason to leave the install changed: the clean-up goes on without one
            try
            {
                AppLoggerFactory.Initialize();
            }
            catch (Exception)
            {
                AppLoggerFactory.InitializeSilent();
            }

            var logger = AppLoggerFactory.CreateLogger(LogCategory.Startup);
            try
            {
                logger.Information("Uninstall: putting the Assetto Corsa install back");

                // The settings name the AC folder the game ran on; read without writing them back: nothing of an uninstall
                // is worth a settings.json written half way through it
                var acRoot = Settings.GameSettingsService.ReadInstallPath();
                var carData = new CarDataOverlay();
                var iniService = new IniModificationService();

                // AC still running reads the data and the modes: it is waited for, as start-up does, but not for ever
                var acClosed = WaitForAcExit(logger);
                var plan = Decide(acClosed, AppSettings.IsInstall(acRoot), () => HasLeftovers(carData, iniService, logger));

                if (plan.RestoreRace)
                {
                    // The car manifests name their folders in full and the cfg files are in Documents: neither needs the folder
                    var restored = AssettoCorsaLauncher.RestoreInstall(carData, iniService, logger);
                    logger.Information("Uninstall: {Restored} car(s) and file(s) put back", restored);
                }

                if (plan.ExitCode == ExitAcRunning)
                {
                    logger.Warning("Assetto Corsa is still running with a race's changes in it: they and the modes stay, the installer is told to retry. The originals are in {Restore}",
                        AppSettings.AcRestorePath);
                }

                if (!AppSettings.IsInstall(acRoot)) logger.Information("No Assetto Corsa at {ACPath}: no modes to take out", acRoot);

                if (plan.RemoveModes)
                {
                    var modes = new ScRaceMode().Uninstall(acRoot, FormatModeIds(logger));
                    logger.Information("Uninstall: {Modes} mode folder(s) removed", modes);
                }

                return plan.ExitCode;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Uninstall: could not put the Assetto Corsa install back");
                return ExitFailed;
            }
            finally
            {
                AppLoggerFactory.Shutdown();
            }
        }

        /// <summary>True once no AC process runs (or none did); false when one still did after <see cref="AcExitWait"/></summary>
        private static bool WaitForAcExit(IAppLogger logger)
        {
            if (!AcProcesses.AnyRunning()) return true;

            logger.Warning("Assetto Corsa is running: waiting up to {Wait} for it to close", AcExitWait);
            var until = DateTime.UtcNow + AcExitWait;
            while (DateTime.UtcNow < until)
            {
                Thread.Sleep(AcExitPoll);
                if (!AcProcesses.AnyRunning()) return true;
            }

            return false;
        }

        private static bool HasLeftovers(CarDataOverlay carData, IniModificationService iniService, IAppLogger logger)
        {
            try
            {
                return carData.Applied.Count > 0 || iniService.HasPendingRestore;
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Could not look for what a race left changed");
                return true;
            }
        }

        /// <summary>The mods' format modes the game knows of; none when the formats cannot be read (sc_race and sc_free go anyway)</summary>
        private static IReadOnlyList<string> FormatModeIds(IAppLogger logger)
        {
            try
            {
                return Race.RaceFormats.Installed.ModeIds.ToList();
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Could not read the race formats: only the modes the game marked as its own are taken out");
                return [];
            }
        }
    }
}
