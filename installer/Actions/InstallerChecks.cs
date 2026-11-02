using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace StreetCorsa.Installer
{
    /// <summary>What the clean-up's run came to (<see cref="InstallerChecks.CleanUpOf"/>)</summary>
    internal enum CleanUp
    {
        /// <summary>Done, or it could not run or failed on its own: nothing a retry would mend, the uninstall goes on</summary>
        Done,

        /// <summary>Assetto Corsa was still running with a race's changes in it, so nothing could be put back</summary>
        AcRunning,

        /// <summary>It ran past its time and was stopped, perhaps in the middle of putting AC back</summary>
        TimedOut
    }

    /// <summary>
    /// The custom actions' reading of text and paths, kept apart from Windows Installer (no Session, no registry), so the
    /// tests compile this same file into the game's .NET (tests/StreetCorsa.Tests links it). Only what both .NET
    /// Framework 4.7.2 and .NET 10 have is used here.
    /// </summary>
    internal static class InstallerChecks
    {
        /// <summary>
        /// The exit code the game gives when Assetto Corsa was still running with a race's changes in it after its wait,
        /// so nothing could be put back (AcUninstall.ExitAcRunning). Any other non-zero code is a failure of the clean-up
        /// itself, logged and gone past: a game that cannot run is no reason to keep it installed.
        /// </summary>
        public const int ExitAcRunning = 3;

        /// <summary>The clean-up's outcome from its exit code; null is a run that did not end in its time</summary>
        public static CleanUp CleanUpOf(int? exitCode) =>
            exitCode == null ? CleanUp.TimedOut : exitCode == ExitAcRunning ? CleanUp.AcRunning : CleanUp.Done;

        /// <summary>
        /// The game's AssettoCorsaPath in the text of its settings.json, or null. The file is one flat JSON object
        /// (GameSettings, written by Newtonsoft): AssettoCorsaPath is a string at its top level, and its only nested
        /// objects (Audio, Ai) have no key of that name, so the first match in the text is the game's folder.
        /// </summary>
        public static string? SettingsFolderIn(string settingsText)
        {
            var m = Regex.Match(settingsText, @"""AssettoCorsaPath""\s*:\s*""(?<path>(?:[^""\\]|\\.)*)""");
            return m.Success ? JsonUnescape(m.Groups["path"].Value) : null;
        }

        /// <summary>A JSON string's escapes undone (a Windows path has \\ for each backslash)</summary>
        public static string JsonUnescape(string text) =>
            Regex.Replace(text, @"\\(u[0-9a-fA-F]{4}|.)", m =>
            {
                var escape = m.Groups[1].Value;
                switch (escape[0])
                {
                    case 'u': return ((char)Convert.ToInt32(escape.Substring(1), 16)).ToString();
                    case 'n': return "\n";
                    case 't': return "\t";
                    case 'r': return "\r";
                    case 'b': return "\b";
                    case 'f': return "\f";
                    default: return escape;
                }
            });

        /// <summary>The library folders in the text of Steam's libraryfolders.vdf, its doubled backslashes made single</summary>
        public static IEnumerable<string> LibraryFoldersIn(string vdf) =>
            Regex.Matches(vdf, "\"path\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(m => m.Groups["path"].Value.Replace(@"\\", @"\"));

        /// <summary>The path under a folder, or null when the folder's text cannot be a path</summary>
        public static string? Under(string folder, params string[] parts)
        {
            try
            {
                return Path.Combine(new[] { folder }.Concat(parts).ToArray());
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>A folder that holds AC: content\cars and content\tracks, each looked for by the given test</summary>
        public static bool IsInstall(string folder, Func<string, bool> directoryExists) =>
            Under(folder, "content", "cars") is { } cars && directoryExists(cars) &&
            Under(folder, "content", "tracks") is { } tracks && directoryExists(tracks);

        /// <summary>
        /// Whether Ollama is installed: its own installer's folder under %LocalAppData% first, then each PATH entry. A PATH
        /// entry is anybody's text, and one with a character no path may hold would throw out of Path.Combine on .NET
        /// Framework, so each is put together on its own and one that does not form is passed over.
        /// </summary>
        public static bool OllamaFound(string? localAppData, string? pathVariable, Func<string, bool> fileExists)
        {
            if (!string.IsNullOrWhiteSpace(localAppData) && Under(localAppData!, "Programs", "Ollama", "ollama.exe") is { } own && fileExists(own))
                return true;

            return (pathVariable ?? "").Split(';')
                .Select(p => p.Trim().Trim('"'))
                .Where(p => p.Length > 0)
                .Any(p => Under(p, "ollama.exe") is { } exe && fileExists(exe));
        }

        // ----- the uninstall's clean-up of what the game made -----

        /// <summary>
        /// The game's folder in %AppData% (AppSettings.AppDataPath): settings, saves, logs, AcRestore. Its AcRestore holds
        /// a race's originals when the uninstall went on with AC running (CustomActions.RestoreAc): kept then.
        /// </summary>
        public const string DataFolderName = "StreetCorsa";

        public const string AcRestoreName = "AcRestore";

        /// <summary>
        /// The install folder's Content, for the uninstall to take with it: only when it is the game's (the core's
        /// Content\streetcorsa\game.json still there, which the immediate plan sees before any file goes) and the
        /// install folder is no drive's root. Null otherwise: nothing of a folder not certainly the game's is deleted.
        /// </summary>
        public static string? OwnContentFolder(string? installFolder, Func<string, bool> fileExists)
        {
            if (string.IsNullOrWhiteSpace(installFolder)) return null;
            var folder = installFolder!.Trim().Trim('"');
            try
            {
                var full = Path.GetFullPath(folder).TrimEnd('\\');
                if (Path.GetPathRoot(full)?.TrimEnd('\\') == full) return null;
                return Under(full, "Content", "streetcorsa", "game.json") is { } core && fileExists(core) ? Path.Combine(full, "Content") : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Deletes what the uninstall plan named: the data folder (all of it, but its AcRestore when that still holds
        /// files), the Content folder, and then the install folder when nothing is left in it. Every entry on its own, so
        /// one locked file leaves only itself; a link (a junction, a symbolic link) is taken out, never what it points at.
        /// Returns how many entries could not be deleted, each told to the log.
        /// </summary>
        public static int RemoveGameData(string? dataFolder, string? contentFolder, string? installFolder, Action<string> log)
        {
            var failed = 0;
            if (!string.IsNullOrWhiteSpace(dataFolder) && Directory.Exists(dataFolder))
            {
                var restore = Path.Combine(dataFolder!, AcRestoreName);
                if (HasFiles(restore))
                {
                    log($"{restore} still holds a race's originals for Assetto Corsa: kept");
                    foreach (var entry in Entries(dataFolder!).Where(e => !string.Equals(e.TrimEnd('\\'), restore, StringComparison.OrdinalIgnoreCase)))
                        failed += DeleteTree(entry, log);
                }
                else
                {
                    failed += DeleteTree(dataFolder!, log);
                }
            }

            if (!string.IsNullOrWhiteSpace(contentFolder)) failed += DeleteTree(contentFolder!, log);

            if (!string.IsNullOrWhiteSpace(installFolder))
            {
                try
                {
                    if (Directory.Exists(installFolder) && !Directory.EnumerateFileSystemEntries(installFolder).Any()) Directory.Delete(installFolder);
                }
                catch (Exception ex)
                {
                    log($"could not delete {installFolder}: {ex.Message}");
                }
            }

            return failed;
        }

        private static bool HasFiles(string folder)
        {
            try
            {
                return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();
            }
            catch (Exception)
            {
                // Unreadable: kept, as when it is known to hold the originals
                return true;
            }
        }

        private static IEnumerable<string> Entries(string folder)
        {
            try
            {
                return Directory.GetFileSystemEntries(folder);
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>A file, or a folder with all it holds; a link is deleted as itself. The entries that could not go, counted.</summary>
        private static int DeleteTree(string path, Action<string> log)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);

                if ((attributes & FileAttributes.Directory) == 0)
                {
                    File.Delete(path);
                    return 0;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    // The link only: a non-recursive delete takes out the junction or symbolic link, not its target's files
                    Directory.Delete(path);
                    return 0;
                }

                var failed = Entries(path).Sum(e => DeleteTree(e, log));
                if (failed == 0) Directory.Delete(path);
                return failed;
            }
            catch (FileNotFoundException)
            {
                return 0;
            }
            catch (DirectoryNotFoundException)
            {
                return 0;
            }
            catch (Exception ex)
            {
                log($"could not delete {path}: {ex.Message}");
                return 1;
            }
        }

        /// <summary>The oldest CSP build the game's races run on (the game's CspVersion.MinimumBuild; a test keeps the two equal)</summary>
        public const int CspMinimumBuild = 3465;

        /// <summary>
        /// CSP's version and build from its extension\config\data_manifest.ini text ([VERSION] SHADERS_PATCH and
        /// SHADERS_PATCH_BUILD); null when it names no build. The name falls back to "build N".
        /// </summary>
        public static (string Name, int Build)? CspOf(string? manifest)
        {
            string? section = null, name = null;
            int? build = null;
            foreach (var raw in (manifest ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == ';') continue;
                if (line[0] == '[')
                {
                    section = line.Trim('[', ']').Trim();
                    continue;
                }

                if (!string.Equals(section, "VERSION", StringComparison.OrdinalIgnoreCase)) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                // A trailing comment (';' or '//') is no part of the value, as the game's IniText reads it
                var value = line.Substring(eq + 1);
                var semicolon = value.IndexOf(';');
                var slashes = value.IndexOf("//", StringComparison.Ordinal);
                var comment = semicolon < 0 ? slashes : slashes < 0 ? semicolon : Math.Min(semicolon, slashes);
                if (comment >= 0) value = value.Substring(0, comment);
                value = value.Trim();
                if (key == "SHADERS_PATCH") name = value;
                else if (key == "SHADERS_PATCH_BUILD" && int.TryParse(value, out var b)) build = b;
            }

            if (build == null) return null;
            return (string.IsNullOrEmpty(name) ? "build " + build : name!, build.Value);
        }
    }
}
