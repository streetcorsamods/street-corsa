using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    ///
    /// And the uninstall's one deferred action, <see cref="RestoreAc"/>: the game's own clean-up run as a process, its
    /// exit code read, so a race's changes left in a running Assetto Corsa are told to the player instead of kept.
    /// </summary>
    public static class CustomActions
    {
        private const string SteamAppId = "244210";

        /// <summary>Where the AC folder picked here is kept for the game (Package.wxs, AcFolderChoice; the game's AcInstallFinder)</summary>
        private const string InstallerKey = @"Software\Street Corsa";
        private const string InstallerValue = "AssettoCorsaPath";

        /// <summary>The game's AssettoCorsaPath in its settings (%AppData%\StreetCorsa\settings.json), or null (InstallerChecks.SettingsFolderIn)</summary>
        private static string? GameSettingsFolder()
        {
            try
            {
                var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreetCorsa", "settings.json");
                if (!File.Exists(settings)) return null;

                return InstallerChecks.SettingsFolderIn(File.ReadAllText(settings));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Fills ACFOLDER when the command line left it empty: the folder the game runs on (its settings), else the one
        /// picked in this wizard last time, else Steam's AC; the first that holds AC, so a folder on a drive gone since is
        /// never set (ACFOLDER is a directory, which Windows Installer resolves)
        /// </summary>
        [CustomAction]
        public static ActionResult FindAcFolder(Session session)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(session["ACFOLDER"])) return ActionResult.Success;

                var known = new[] { GameSettingsFolder(), Read(RegistryHive.CurrentUser, RegistryView.Default, InstallerKey, InstallerValue) };
                var found = known.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!.Trim()).Concat(Candidates()).FirstOrDefault(IsInstall);
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
        /// before a race), and AC_CHOSEN is the folder the game is told about (none when it is no AC, or when the player
        /// put it off with AC_LATER, for the game's Settings)
        /// </summary>
        [CustomAction]
        public static ActionResult CheckAcFolder(Session session)
        {
            session["AC_VALID"] = "";
            session["AC_CHOSEN"] = "";
            session["CSP_FOUND"] = "";
            session["CSP_OLD"] = "";
            try
            {
                if (session["AC_LATER"] == "1")
                {
                    session.Log("Street Corsa: the Assetto Corsa folder is left for the game's Settings");
                    return ActionResult.Success;
                }

                var folder = (session["ACFOLDER"] ?? "").Trim().Trim('"');
                var valid = folder.Length > 0 && IsInstall(folder);
                session["AC_VALID"] = valid ? "1" : "";
                session["AC_CHOSEN"] = valid ? folder.TrimEnd('\\') : "";
                session["CSP_FOUND"] = valid && Directory.Exists(Path.Combine(folder, "extension")) ? "1" : "";
                // An older CSP than the game's race modes need (the game refuses those races too: CspVersion)
                var csp = session["CSP_FOUND"] == "1" ? CspOf(folder) : null;
                session["CSP_OLD"] = csp != null && csp.Value.Build < InstallerChecks.CspMinimumBuild ? csp.Value.Name : "";
                session.Log($"Street Corsa: {folder} is {(valid ? "" : "not ")}an Assetto Corsa install, CSP {(session["CSP_FOUND"] == "1" ? "found" : "missing")}"
                            + (csp != null ? $" ({csp.Value.Name}, build {csp.Value.Build})" : ""));
            }
            catch (Exception ex)
            {
                session.Log("Street Corsa: checking the Assetto Corsa folder failed: " + ex);
                session["AC_VALID"] = "";
                session["AC_CHOSEN"] = "";
                session["CSP_FOUND"] = "";
                session["CSP_OLD"] = "";
            }

            return ActionResult.Success;
        }

        /// <summary>OLLAMA_FOUND is 1 when Ollama is installed (its own installer's folder, or on the PATH)</summary>
        [CustomAction]
        public static ActionResult FindOllama(Session session)
        {
            try
            {
                var found = InstallerChecks.OllamaFound(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetEnvironmentVariable("PATH"), p => SafeExists(p));
                session["OLLAMA_FOUND"] = found ? "1" : "";
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

        // ----- the uninstall -----

        /// <summary>The game's own clean-up switch (Street Corsa/Services/Assetto Corsa/AcUninstall.cs)</summary>
        private const string UninstallSwitch = "--uninstall";

        /// <summary>
        /// The game's wait for AC is half a minute; a clean-up that takes longer than this has hung, and is given up on
        /// rather than hold the wizard for good
        /// </summary>
        private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Deferred, before the files go (Package.wxs): runs <c>"Street Corsa.exe" --uninstall</c>, which puts back what a
        /// race changed in Assetto Corsa and takes the game's CSP modes out. Its CustomActionData names the exe (Exe=...).
        /// AC still running with a race's changes in it (exit code <see cref="InstallerChecks.ExitAcRunning"/>) is put to
        /// the player: Retry runs the clean-up again once AC is closed, Ignore goes on without it (the originals stay in
        /// %AppData%\StreetCorsa\AcRestore, a car's data.acd and data folder renamed in its own folder, as the README says), Abort stops the uninstall with the game in place. A
        /// clean-up that ran past its time is put to the player the same way, since it may have stopped half-way through
        /// putting AC back. A quiet uninstall (no UI to ask) goes on without it, with the line in the log.
        /// </summary>
        [CustomAction]
        public static ActionResult RestoreAc(Session session)
        {
            string exe;
            try
            {
                exe = session.CustomActionData["Exe"];
            }
            catch (Exception ex)
            {
                session.Log("Street Corsa: the clean-up was not told where the game is: " + ex);
                return ActionResult.Success;
            }

            while (true)
            {
                var outcome = InstallerChecks.CleanUpOf(RunUninstall(session, exe));
                if (outcome == CleanUp.Done) return ActionResult.Success;

                var answer = Ask(session, outcome);
                session.Log(outcome == CleanUp.AcRunning
                    ? $"Street Corsa: Assetto Corsa is still running with a race's changes in it; the player chose {answer}"
                    : $"Street Corsa: the clean-up ran past its time and was stopped; the player chose {answer}");
                switch (answer)
                {
                    case MessageResult.Retry:
                        continue;
                    case MessageResult.Abort:
                    case MessageResult.Cancel:
                        return ActionResult.UserExit;
                    default:
                        // Ignore, or no UI to ask: the uninstall goes on, the originals stay in AcRestore
                        return ActionResult.Success;
                }
            }
        }

        /// <summary>
        /// The game's exit code; 0 when it could not be run at all (logged), which is nothing a retry would mend; null when
        /// it did not end in <see cref="RestoreTimeout"/> and was stopped
        /// </summary>
        private static int? RunUninstall(Session session, string exe)
        {
            try
            {
                if (!File.Exists(exe))
                {
                    session.Log($"Street Corsa: no {exe} to run the clean-up with");
                    return 0;
                }

                var start = new ProcessStartInfo(exe, UninstallSwitch)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
                };
                using var process = Process.Start(start);
                if (process == null)
                {
                    session.Log("Street Corsa: the clean-up did not start");
                    return 0;
                }

                if (!process.WaitForExit((int)RestoreTimeout.TotalMilliseconds))
                {
                    session.Log($"Street Corsa: the clean-up did not end in {RestoreTimeout.TotalMinutes} minutes: given up on");
                    try { process.Kill(); } catch (Exception) { }
                    return null;
                }

                session.Log($"Street Corsa: the clean-up ended with exit code {process.ExitCode}");
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                session.Log("Street Corsa: the clean-up could not be run: " + ex);
                return 0;
            }
        }

        /// <summary>The installer's own error box, Abort / Retry / Ignore; <see cref="MessageResult.None"/> when there is no UI</summary>
        private static MessageResult Ask(Session session, CleanUp outcome)
        {
            try
            {
                using var record = new Record(0);
                record.FormatString = outcome == CleanUp.AcRunning
                    ? "Assetto Corsa or Street Corsa is still running, with a race's changes in it. Close it and retry, so Street Corsa can put Assetto Corsa back the way it was. " +
                      "Ignore leaves the changes in place (the originals stay in %AppData%\\StreetCorsa\\AcRestore, and a car's data.acd and data folder renamed *.streetcorsa in its own folder); Abort keeps the game installed."
                    : $"Street Corsa's clean-up of Assetto Corsa did not end in {RestoreTimeout.TotalMinutes} minutes and was stopped, so Assetto Corsa may not be all the way back. " +
                      "Retry runs the clean-up again. Ignore goes on with the uninstall (the originals stay in %AppData%\\StreetCorsa\\AcRestore, and a car's data.acd and data folder renamed *.streetcorsa in its own folder); Abort keeps the game installed.";
                return session.Message(InstallMessage.User | (InstallMessage)MessageButtons.AbortRetryIgnore | (InstallMessage)MessageIcon.Warning, record);
            }
            catch (Exception ex)
            {
                session.Log("Street Corsa: could not ask about the running Assetto Corsa: " + ex);
                return MessageResult.None;
            }
        }

        private static bool IsInstall(string folder) => InstallerChecks.IsInstall(folder, p => SafeExists(p, directory: true));

        /// <summary>The CSP in an AC folder, from its manifest; null when it cannot be read</summary>
        private static (string Name, int Build)? CspOf(string folder)
        {
            try
            {
                var manifest = Path.Combine(folder, "extension", "config", "data_manifest.ini");
                return File.Exists(manifest) ? InstallerChecks.CspOf(File.ReadAllText(manifest)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

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
                // A registry value is anybody's text: one with a character no path may hold would throw out of
                // Path.Combine on this .NET Framework, and out of the whole search with it, so each candidate is put
                // together on its own (as SafeExists looks on its own) and only the ones that form are offered
                if (InstallerChecks.Under(steam, "steamapps", "common", "assettocorsa") is { } own) yield return own;

                string vdf;
                try
                {
                    vdf = File.ReadAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"));
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var folder in InstallerChecks.LibraryFoldersIn(vdf))
                {
                    if (InstallerChecks.Under(folder, "steamapps", "common", "assettocorsa") is { } library) yield return library;
                }
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
