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
