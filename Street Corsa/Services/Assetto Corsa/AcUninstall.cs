using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Services.Configuration;

namespace StreetCorsa.Services
{
    /// <summary>
    /// <c>"Street Corsa.exe" --uninstall</c>, which the installer runs before it takes the game away: whatever a race left
    /// changed goes back (as at every start), and the game's CSP modes leave the AC install. No window; the saves, the
    /// settings and the log stay in %AppData%\StreetCorsa. Never throws: an uninstall goes ahead whatever this managed.
    /// </summary>
    public static class AcUninstall
    {
        public const string Switch = "--uninstall";

        public static bool Asked(IEnumerable<string> args) => args.Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));

        public static void Run()
        {
            IAppLogger? logger = null;
            try
            {
                AppLoggerFactory.Initialize();
                logger = AppLoggerFactory.CreateLogger(LogCategory.Startup);
                logger.Information("Uninstall: putting the Assetto Corsa install back");

                // The settings name the AC folder the game ran on (AppSettings.Instance takes it from them)
                _ = new Settings.GameSettingsService();
                var acRoot = AppSettings.Instance.AssettoCorsaPath;
                if (!AppSettings.IsInstall(acRoot))
                {
                    logger.Information("No Assetto Corsa at {ACPath}: nothing to put back", acRoot);
                    return;
                }

                // AC still running reads the data and the modes: they stay, and its cars go back at the next start
                if (AcProcesses.AnyRunning())
                {
                    logger.Warning("Assetto Corsa is running: the install is left as it is");
                    return;
                }

                var restored = AssettoCorsaLauncher.RestoreInstall(new CarDataOverlay(), new IniModificationService(), logger);
                var modes = new ScRaceMode().Uninstall(acRoot, Race.RaceFormats.Installed.ModeIds);
                logger.Information("Uninstall: {Restored} car(s) and file(s) put back, {Modes} mode folder(s) removed", restored, modes);
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Uninstall: could not put the Assetto Corsa install back");
            }
            finally
            {
                AppLoggerFactory.Shutdown();
            }
        }
    }
}
