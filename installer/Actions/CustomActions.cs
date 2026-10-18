using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace StreetCorsa.Installer
{
    /// <summary>
    /// What the wizard asks of the machine (installer/Package.wxs): where Assetto Corsa is, whether it is one, whether
    /// Custom Shaders Patch and Ollama are there; and the links the last page opens. Immediate actions, run as the
    /// player: they read and set properties, and never fail the install (a search that finds nothing leaves the box empty).
    /// The search is the game's own (Street Corsa/Services/Settings/AcInstallFinder.cs), which this cannot link: the
    /// game is .NET 10, these run in Windows Installer's .NET Framework.
    /// </summary>
    public static class CustomActions
    {
        private const string SteamAppId = "244210";

        /// <summary>Fills ACFOLDER when it is empty (no folder picked before, none on the command line): Steam's AC</summary>
        [CustomAction]
        public static ActionResult FindAcFolder(Session session)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(session["ACFOLDER"])) return ActionResult.Success;

                var found = Candidates().FirstOrDefault(IsInstall);
                session.Log($"Street Corsa: Assetto Corsa {(found != null ? "found at " + found : "not found")}");
                if (found != null) session["ACFOLDER"] = WithSlash(found);
            }
            catch (Exception ex)
            {
                session.Log("Street Corsa: the search for Assetto Corsa failed: " + ex);
            }

            return ActionResult.Success;
        }

        /// <summary>
        /// The AC page's Next, and the install itself: AC_VALID is 1 when ACFOLDER holds AC (content\cars and
        /// content\tracks), CSP_FOUND is 1 when it has Custom Shaders Patch (its extension folder, what the game checks
        /// before a race), and AC_CHOSEN is the folder the game is told about (none when it is no AC)
        /// </summary>
        [CustomAction]
        public static ActionResult CheckAcFolder(Session session)
        {
            try
            {
                var folder = (session["ACFOLDER"] ?? "").Trim().Trim('"');
                var valid = folder.Length > 0 && IsInstall(folder);
                session["AC_VALID"] = valid ? "1" : "";
                session["AC_CHOSEN"] = valid ? folder.TrimEnd('\\') : "";
                session["CSP_FOUND"] = valid && Directory.Exists(Path.Combine(folder, "extension")) ? "1" : "";
                session.Log($"Street Corsa: {folder} is {(valid ? "" : "not ")}an Assetto Corsa install, CSP {(session["CSP_FOUND"] == "1" ? "found" : "missing")}");
            }
            catch (Exception ex)
            {
                session.Log("Street Corsa: checking the Assetto Corsa folder failed: " + ex);
                session["AC_VALID"] = "";
            }

            return ActionResult.Success;
        }

        /// <summary>OLLAMA_FOUND is 1 when Ollama is installed (its own installer's folder, or on the PATH)</summary>
        [CustomAction]
        public static ActionResult FindOllama(Session session)
        {
            try
            {
                var own = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe");
                var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')
                    .Where(p => p.Trim().Length > 0)
                    .Any(p => SafeExists(Path.Combine(p.Trim().Trim('"'), "ollama.exe")));
                session["OLLAMA_FOUND"] = File.Exists(own) || onPath ? "1" : "";
            }
            catch (Exception ex)
            {
                session.Log("Street Corsa: looking for Ollama failed: " + ex);
            }

            return ActionResult.Success;
        }

        /// <summary>The last page's tick: Ollama's download page in the player's browser</summary>
        [CustomAction]
        public static ActionResult OpenOllamaPage(Session session) => Open(session, "https://ollama.com/download", null);

        /// <summary>The last page's tick: the game, just installed</summary>
        [CustomAction]
        public static ActionResult LaunchGame(Session session)
        {
            var folder = session["INSTALLFOLDER"] ?? "";
            return Open(session, Path.Combine(folder, "Street Corsa.exe"), folder);
        }

        private static ActionResult Open(Session session, string target, string? workingDirectory)
        {
            try
            {
                var start = new ProcessStartInfo(target) { UseShellExecute = true };
                if (workingDirectory != null) start.WorkingDirectory = workingDirectory;
                Process.Start(start);
            }
            catch (Exception ex)
            {
                session.Log($"Street Corsa: could not open {target}: {ex}");
            }

            return ActionResult.Success;
        }

        private static bool IsInstall(string folder) =>
            SafeExists(Path.Combine(folder, "content", "cars"), directory: true) && SafeExists(Path.Combine(folder, "content", "tracks"), directory: true);

        private static bool SafeExists(string path, bool directory = false)
        {
            try
            {
                return directory ? Directory.Exists(path) : File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string WithSlash(string folder) => folder.EndsWith("\\") ? folder : folder + "\\";

        /// <summary>Steam's record of AC (its uninstall entry), else each of Steam's library folders</summary>
        private static IEnumerable<string> Candidates()
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                if (Read(RegistryHive.LocalMachine, view, $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {SteamAppId}", "InstallLocation") is { } own)
                    yield return own;
            }

            var steams = new[]
            {
                Read(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath"),
                Read(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath"),
                Read(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Valve\Steam", "InstallPath")
            };
            foreach (var steam in steams.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Replace('/', '\\')))
            {
                yield return Path.Combine(steam, "steamapps", "common", "assettocorsa");

                string vdf;
                try
                {
                    vdf = File.ReadAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"));
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (Match m in Regex.Matches(vdf, "\"path\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase))
                    yield return Path.Combine(m.Groups["path"].Value.Replace(@"\\", @"\"), "steamapps", "common", "assettocorsa");
            }
        }

        private static string? Read(RegistryHive hive, RegistryView view, string key, string name)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var opened = root.OpenSubKey(key);
                return opened?.GetValue(name) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
