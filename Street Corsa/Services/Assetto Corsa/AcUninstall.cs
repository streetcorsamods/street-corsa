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
    /// </summary>
    public static class AcUninstall
    {
        public const string Switch = "--uninstall";

        /// <summary>How long a running AC is waited for: the player is in front of the uninstall's progress bar</summary>
        private static readonly TimeSpan AcExitWait = TimeSpan.FromMinutes(2);

        private static readonly TimeSpan AcExitPoll = TimeSpan.FromSeconds(1);

        public static bool Asked(IEnumerable<string> args) => args.Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));

        public static void Run()
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

                // The settings name the AC folder the game ran on (AppSettings.Instance takes it from them)
                _ = new Settings.GameSettingsService();
                var acRoot = AppSettings.Instance.AssettoCorsaPath;
                var carData = new CarDataOverlay();
                var iniService = new IniModificationService();

                // AC still running reads the data and the modes: it is waited for, as start-up does, but not for ever
                var acClosed = WaitForAcExit(logger);
                if (acClosed)
                {
                    // The car manifests name their folders in full and the cfg files are in Documents: neither needs the folder
                    var restored = AssettoCorsaLauncher.RestoreInstall(carData, iniService, logger);
                    logger.Information("Uninstall: {Restored} car(s) and file(s) put back", restored);
                }

                if (!AppSettings.IsInstall(acRoot))
                {
                    logger.Information("No Assetto Corsa at {ACPath}: no modes to take out", acRoot);
                    return;
                }

                // A race left waiting to go back runs in its mode: that stays with it. Nothing waiting, the modes go even under a running AC.
                if (!acClosed && HasLeftovers(carData, iniService, logger))
                {
                    logger.Warning("Assetto Corsa is still running with a race's changes in it: they and the modes stay. The originals are in {Restore}",
                        AppSettings.AcRestorePath);
                    return;
                }

                var modes = new ScRaceMode().Uninstall(acRoot, FormatModeIds(logger));
                logger.Information("Uninstall: {Modes} mode folder(s) removed", modes);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Uninstall: could not put the Assetto Corsa install back");
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
