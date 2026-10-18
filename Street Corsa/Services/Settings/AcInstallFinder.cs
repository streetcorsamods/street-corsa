using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using StreetCorsa.Configuration;

namespace StreetCorsa.Services.Settings
{
    /// <summary>
    /// Where Assetto Corsa is installed, for a player whose settings name no folder: the one picked in the installer,
    /// else Steam's own record of the game (its uninstall entry), else each of Steam's library folders. Never throws: nothing found is null.
    /// </summary>
    public static partial class AcInstallFinder
    {
        /// <summary>Assetto Corsa's Steam app id</summary>
        private const string SteamAppId = "244210";

        /// <summary>Where the installer keeps the AC folder the player picked in its wizard (installer/Package.wxs)</summary>
        public const string InstallerKey = @"Software\Street Corsa";
        public const string InstallerValue = "AssettoCorsaPath";

        [GeneratedRegex("\"path\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase)]
        private static partial Regex LibraryPath();

        /// <summary>The first install found (<see cref="AppSettings.IsInstall"/>), or null</summary>
        public static string? Find() => Candidates().FirstOrDefault(AppSettings.IsInstall);

        /// <summary>The AC folder the installer was last told about (<see cref="InstallerKey"/>), or null</summary>
        public static string? InstallerChoice() => Read(Registry.CurrentUser, InstallerKey, InstallerValue);

        /// <summary>Where to look, most likely first</summary>
        public static IEnumerable<string> Candidates()
        {
            if (InstallerChoice() is { } chosen)
                yield return chosen;
            // Steam is a 32-bit program: its uninstall entries are in the 32-bit view, which this x64 process reads under WOW6432Node
            foreach (var uninstall in new[] { @"SOFTWARE\WOW6432Node\Microsoft", @"SOFTWARE\Microsoft" })
            {
                if (Read(Registry.LocalMachine, $@"{uninstall}\Windows\CurrentVersion\Uninstall\Steam App {SteamAppId}", "InstallLocation") is { } own)
                    yield return own;
            }

            var steams = new[]
            {
                Read(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                Read(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                Read(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath")
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var steam in steams.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                foreach (var library in Libraries(steam!))
                {
                    var folder = Path.Combine(library, "steamapps", "common", "assettocorsa");
                    if (seen.Add(folder)) yield return folder;
                }
            }
        }

        /// <summary>Steam's own folder and the library folders its libraryfolders.vdf lists</summary>
        public static IEnumerable<string> Libraries(string steam)
        {
            var folders = new List<string> { steam.Replace('/', '\\') };
            try
            {
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf)) folders.AddRange(LibrariesIn(File.ReadAllText(vdf)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Steam's own folder is still worth a look
            }

            return folders;
        }

        /// <summary>The <c>"path"</c> entries of a libraryfolders.vdf, its escaped backslashes undone</summary>
        public static IEnumerable<string> LibrariesIn(string vdf) =>
            LibraryPath().Matches(vdf).Select(m => m.Groups["path"].Value.Replace(@"\\", @"\"));

        private static string? Read(RegistryKey root, string key, string name)
        {
            try
            {
                using var opened = root.OpenSubKey(key);
                return opened?.GetValue(name) as string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return null;
            }
        }
    }
}
