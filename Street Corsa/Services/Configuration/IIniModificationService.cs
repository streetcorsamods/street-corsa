using StreetCorsa.Services.Configuration.Models;

namespace StreetCorsa.Services.Configuration
{
    /// <summary>
    /// Central service for all Assetto Corsa INI file modifications.
    /// This is the ONLY interface that should touch INI files.
    ///
    /// Follows the Read -> Intent -> Apply model:
    /// 1. Read: Parse INI preserving everything
    /// 2. Intent: Declare what to change (high-level)
    /// 3. Apply: Translate intent to minimal file changes
    /// </summary>
    public interface IIniModificationService
    {
        /// <summary>
        /// Apply a modification intent to the appropriate INI file.
        /// This is the primary method for all INI modifications.
        /// </summary>
        /// <param name="intent">High-level modification intent</param>
        /// <returns>True if successful, false otherwise</returns>
        bool ApplyIntent(ModificationIntent intent);

        /// <summary>
        /// Puts back every cfg file an intent changed, as it was before the first change: call it once AC has
        /// exited (the launcher's finally, start-up, exit, a crash). Idempotent; a file that cannot go back stays
        /// kept for the next call and is logged, the others go back anyway.
        /// </summary>
        /// <param name="problems">Gets a line for the player about each file that stayed changed</param>
        /// <returns>How many files went back</returns>
        int RestoreAll(ICollection<string>? problems = null);

        /// <summary>Whether a changed cfg file is waiting to go back</summary>
        bool HasPendingRestore { get; }

        /// <summary>
        /// Read an INI file (for inspection, not modification)
        /// </summary>
        /// <param name="fileName">File name (relative to cfg directory)</param>
        /// <returns>The file's text or null if not found</returns>
        StreetCorsa.Parts.Export.IniText? ReadIniFile(string fileName);

        /// <summary>
        /// Check if a specific INI file exists
        /// </summary>
        /// <param name="fileName">File name (relative to cfg directory)</param>
        bool FileExists(string fileName);

        /// <summary>
        /// The full path of an INI file in the cfg directory. Only a file name is taken: a path (rooted, or with
        /// folders or "..") throws, so nothing outside the cfg folder is ever read or written through here.
        /// </summary>
        /// <param name="fileName">File name</param>
        string GetIniFilePath(string fileName);
    }
}
