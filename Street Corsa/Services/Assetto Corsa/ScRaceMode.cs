using System.IO;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services
{
    /// <summary>
    /// The CSP mode every race runs in (<c>apps\new-modes\sc_race</c> in the repo, shipped next to the game under
    /// <c>AcModes\sc_race</c>): it starts the race, judges crashes and false starts, writes the result and quits.
    ///
    /// Installed into the AC install's <c>extension\lua\new-modes\sc_race</c> before every race, a file written
    /// only when it differs, so the mode in the game is always the one this build was made with. It stays there
    /// after the race: it is the game's own folder, which nothing else uses, and a race without it could not start
    /// or report. The Lua app it replaced (<c>apps\lua\sr_race_manager</c>) is removed on the way, since it would
    /// run beside the mode and write a second, older result of the same race, and so is the mode's folder from before
    /// the rename to Street Corsa (<c>extension\lua\new-modes\sr_race</c>), which nothing selects any more.
    ///
    /// The mods' race formats go in with it (<see cref="Race.RaceFormats"/>): every mod's format module next to the mode's
    /// own (<c>format_&lt;mod&gt;_&lt;module&gt;.lua</c>), and a format's own mode, when the race is one, in a folder of its
    /// own (<c>new-modes\&lt;mod&gt;_&lt;mode&gt;</c>).
    ///
    /// So do the part models the race's cars show (<see cref="StreetCorsa.Parts.Cars.ShownParts"/>, in <c>parts\</c>): the mode loads
    /// them from its own folder, and a race's models leave with the next race. A format's own mode gets them too, with the
    /// script that puts them on (<c>car_parts.lua</c>), for it to run as sc_race does.
    /// </summary>
    public sealed class ScRaceMode(string sourceFolder, Func<Race.RaceFormats>? formats = null)
    {
        /// <summary>Where the build puts the mode, next to the game</summary>
        public static string DefaultSourceFolder => Path.Combine(AppContext.BaseDirectory, "AcModes", IniModificationService.RaceModeId);

        /// <summary>What the mode replaced, relative to the AC install: the Lua app, and the mode under its old id</summary>
        private static readonly string[] Retired =
        [
            Path.Combine("apps", "lua", "sr_race_manager"),
            Path.Combine("extension", "lua", "new-modes", "sr_race")
        ];

        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("RaceMode");

        public ScRaceMode() : this(DefaultSourceFolder, () => Race.RaceFormats.Current)
        {
        }

        /// <summary>The mode's folder in an AC install</summary>
        public static string InstalledFolder(string acRoot) =>
            Path.Combine(acRoot, "extension", "lua", "new-modes", IniModificationService.RaceModeId);

        /// <summary>
        /// Puts the mode into <paramref name="acRoot"/> as this build has it, with the mods' format modules, the mode
        /// of <paramref name="format"/> when it has its own, and the part <paramref name="models"/> the cars show. Null when
        /// it is there; otherwise why the race cannot run, for the player.
        /// </summary>
        public string? Install(string acRoot, Models.Race.RaceFormat? format = null, IEnumerable<StreetCorsa.Parts.Cars.ShownModel>? models = null)
        {
            if (!Directory.Exists(Path.Combine(acRoot, "extension")))
                return GameText.Say("launch.no_csp");

            if (!Directory.Exists(sourceFolder) || !File.Exists(Path.Combine(sourceFolder, "mode.lua")))
                return GameText.Say("launch.mode_missing", ("folder", sourceFolder));

            try
            {
                var target = InstalledFolder(acRoot);
                var modelFiles = (models ?? []).Select(m => (m.Name, m.Source)).ToList();
                var files = Directory.GetFiles(sourceFolder).Select(f => (Name: Path.GetFileName(f), Source: f))
                    .Concat(formats?.Invoke().ModuleFiles ?? [])
                    .Concat(modelFiles).ToList();
                var written = Mirror(files, target);
                if (written > 0)
                    _logger.Information("Race mode installed in {Target} ({Count} file(s) changed)", target, written);

                // A format with a mode of its own: its folder, whole, as the mod has it
                if (format != null && Race.RaceFormats.ModeFolder(format) is { } modeFolder)
                {
                    if (!File.Exists(Path.Combine(modeFolder, "mode.lua")))
                        return GameText.Say("launch.mode_missing", ("folder", modeFolder));

                    var modeTarget = Path.Combine(acRoot, "extension", "lua", "new-modes", Race.RaceFormats.ModeIdOf(format));
                    // With the parts and their script: the mod's own files first, so a car_parts.lua of its own wins
                    var partsScript = Path.Combine(sourceFolder, PartsScript);
                    var modeFiles = Directory.GetFiles(modeFolder, "*", SearchOption.AllDirectories)
                        .Select(f => (Name: Path.GetRelativePath(modeFolder, f), Source: f))
                        .Concat(File.Exists(partsScript) ? [(PartsScript, partsScript)] : [])
                        .Concat(modelFiles).ToList();
                    var modeWritten = Mirror(modeFiles, modeTarget);
                    if (modeWritten > 0)
                        _logger.Information("Race format {Format}'s mode installed in {Target} ({Count} file(s) changed)", format.Id, modeTarget, modeWritten);
                }

                foreach (var retired in Retired) RemoveFolder(Path.Combine(acRoot, retired), "the race mode does its work now");
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error(ex, "Could not install the race mode into {AcRoot}", acRoot);
                return GameText.Say("launch.mode_not_installed", ("problem", ex.Message));
            }
        }

        /// <summary>
        /// Puts the free run's mode (<c>sc_free</c>, built next to this one) into <paramref name="acRoot"/> with this
        /// mode's car_parts.lua and the part <paramref name="models"/> the car shows. Null when it is there; otherwise why
        /// the free run cannot go, for the player.
        /// </summary>
        public string? InstallFreeRun(string acRoot, IEnumerable<StreetCorsa.Parts.Cars.ShownModel> models)
        {
            if (!Directory.Exists(Path.Combine(acRoot, "extension")))
                return GameText.Say("launch.no_csp");

            var freeSource = Path.Combine(Path.GetDirectoryName(sourceFolder) ?? sourceFolder, IniModificationService.FreeRunModeId);
            var parts = Path.Combine(sourceFolder, PartsScript);
            if (!File.Exists(Path.Combine(freeSource, "mode.lua")) || !File.Exists(parts))
                return GameText.Say("launch.mode_missing", ("folder", freeSource));

            try
            {
                var target = Path.Combine(acRoot, "extension", "lua", "new-modes", IniModificationService.FreeRunModeId);
                var files = Directory.GetFiles(freeSource).Select(f => (Name: Path.GetFileName(f), Source: f))
                    .Append((PartsScript, parts))
                    .Concat(models.Select(m => (m.Name, m.Source))).ToList();
                var written = Mirror(files, target);
                if (written > 0)
                    _logger.Information("Free run mode installed in {Target} ({Count} file(s) changed)", target, written);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error(ex, "Could not install the free run mode into {AcRoot}", acRoot);
                return GameText.Say("launch.mode_not_installed", ("problem", ex.Message));
            }
        }

        /// <summary>
        /// Takes the game's modes out of <paramref name="acRoot"/> when the game is uninstalled: sc_race, sc_free, the
        /// <paramref name="modeIds"/> of the mods' formats and what the modes replaced. Returns how many folders went;
        /// one that cannot go is logged and left.
        /// </summary>
        public int Uninstall(string acRoot, IEnumerable<string> modeIds)
        {
            var removed = 0;
            var folders = modeIds.Append(IniModificationService.RaceModeId).Append(IniModificationService.FreeRunModeId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => Path.Combine(acRoot, "extension", "lua", "new-modes", id))
                .Concat(Retired.Select(r => Path.Combine(acRoot, r)));
            foreach (var folder in folders)
            {
                var existed = Directory.Exists(folder);
                RemoveFolder(folder, "the game is uninstalled");
                if (existed && !Directory.Exists(folder)) removed++;
            }

            return removed;
        }

        /// <summary>The race mode's script that puts the parts on, which the free run's mode runs too</summary>
        private const string PartsScript = "car_parts.lua";

        /// <summary>
        /// Makes <paramref name="target"/> hold exactly <paramref name="files"/> (by their names there, which may have
        /// folders in them), each written only when it differs: files no longer wanted go, since the mode would still
        /// load (or read) them. Returns how many changed.
        /// </summary>
        private static int Mirror(IReadOnlyList<(string Name, string Source)> files, string target)
        {
            Directory.CreateDirectory(target);
            var written = 0;
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, source) in files)
            {
                var installed = Path.GetFullPath(Path.Combine(target, name));
                if (!wanted.Add(installed)) continue;

                var bytes = File.ReadAllBytes(source);
                if (File.Exists(installed) && File.ReadAllBytes(installed).AsSpan().SequenceEqual(bytes)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
                SafeFile.WriteAllBytes(installed, bytes);
                written++;
            }

            foreach (var stale in Directory.GetFiles(target, "*", SearchOption.AllDirectories).Where(f => !wanted.Contains(Path.GetFullPath(f))))
            {
                File.Delete(stale);
                written++;
            }

            return written;
        }

        /// <summary>A clean-up, never worth a race: one that fails (a file locked or denied) is logged and tried next time</summary>
        private void RemoveFolder(string folder, string why)
        {
            try
            {
                if (!Directory.Exists(folder)) return;

                Directory.Delete(folder, recursive: true);
                _logger.Information("Removed {Folder}: {Why}", folder, why);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(ex, "Could not remove {Folder}; the race goes ahead, the next one tries again", folder);
            }
        }
    }
}
