using System.IO;
using StreetCorsa.Configuration;
using StreetCorsa.Services.Race;

namespace StreetCorsa.Services.Storage
{
    /// <summary>
    /// Moves what the game wrote under its old name (Street Rod AC) to the new one, at start-up and before the logger
    /// opens its file under the new folder: %AppData%\StreetRodAC (settings, saves, the catalog, the originals a race
    /// changed in AC) and the race mode's outbox and quarantine under Documents\Assetto Corsa\out.
    ///
    /// Entry by entry, never over something already there: a start that was cut short (or a folder locked by a scan)
    /// finishes the next time, and an entry that is in both places is left in the old folder and reported.
    /// </summary>
    public static class LegacyRename
    {
        /// <summary>%AppData%\StreetRodAC, the game's folder before the rename</summary>
        public static string LegacyAppDataPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreetRodAC");

        /// <summary>Moves every old folder the game knows of; returns what happened, for the log once it is open</summary>
        public static IReadOnlyList<string> Run() =>
        [
            .. Move(LegacyAppDataPath, AppSettings.AppDataPath),
            .. Move(RaceResultIngestionService.LegacyInboxPath, RaceResultIngestionService.DefaultInboxPath),
            .. Move(RaceResultIngestionService.LegacyQuarantinePath, RaceResultIngestionService.DefaultQuarantinePath)
        ];

        /// <summary>
        /// Moves <paramref name="from"/> to <paramref name="to"/>: the whole folder when <paramref name="to"/> is not
        /// there yet, otherwise each entry that <paramref name="to"/> does not have. The old folder goes once it is empty.
        /// </summary>
        public static IReadOnlyList<string> Move(string from, string to)
        {
            var notes = new List<string>();
            if (!Directory.Exists(from)) return notes;

            try
            {
                if (!Directory.Exists(to))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(to))!);
                    Directory.Move(from, to);
                    notes.Add($"Moved {from} to {to}");
                    return notes;
                }

                foreach (var entry in Directory.EnumerateFileSystemEntries(from).ToList())
                {
                    var target = Path.Combine(to, Path.GetFileName(entry));
                    if (File.Exists(target) || Directory.Exists(target))
                    {
                        notes.Add($"Left {entry} where it is: {target} is there already");
                        continue;
                    }

                    if (Directory.Exists(entry)) Directory.Move(entry, target);
                    else File.Move(entry, target);
                    notes.Add($"Moved {entry} to {target}");
                }

                if (!Directory.EnumerateFileSystemEntries(from).Any()) Directory.Delete(from);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add($"Could not move everything from {from} to {to} (tried again at the next start): {ex.Message}");
            }

            return notes;
        }
    }
}
