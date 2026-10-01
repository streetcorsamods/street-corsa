using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace StreetCorsa.Services.Settings
{
    /// <summary>
    /// Where Assetto Corsa is installed, for a player whose settings name no folder: Steam's own record of the game
    /// (its uninstall entry), else each of Steam's library folders. Never throws: nothing found is null.
    /// </summary>
    public static partial class AcInstallFinder
    {
        /// <summary>Assetto Corsa's Steam app id</summary>
        private const string SteamAppId = "244210";

        [GeneratedRegex("\"path\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase)]
        private static partial Regex LibraryPath();

        /// <summary>The first install found, or null</summary>
        public static string? Find() => Candidates().FirstOrDefault(IsInstall);

        /// <summary>A folder that holds Assetto Corsa: its cars and tracks</summary>
        public static bool IsInstall(string? folder)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(folder)
                       && Directory.Exists(Path.Combine(folder, "content", "cars"))
                       && Directory.Exists(Path.Combine(folder, "content", "tracks"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }

        /// <summary>Where to look, most likely first</summary>
        public static IEnumerable<string> Candidates()
        {
            if (Read(Registry.LocalMachine, $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {SteamAppId}", "InstallLocation") is { } own)
                yield return own;

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
