using System.IO;
using StreetCorsa.Helpers;

namespace StreetCorsa.Services.Configuration
{
    /// <summary>
    /// The one way an original is kept aside for a race and put back after it, for the cars' data
    /// (<see cref="CarDataOverlay"/>) and the cfg INI files (<see cref="AcConfigBackup"/>). Both sides are written
    /// whole or not at all (<see cref="SafeFile"/>), so a crash or a power cut leaves either the old file or the new
    /// one, and a kept copy that is gone is treated the same in both: it can never go back, so the caller logs it as
    /// lost and drops it rather than trying again forever.
    /// </summary>
    public static class KeptFile
    {
        /// <summary>
        /// Keeps a copy of <paramref name="original"/> at <paramref name="kept"/>. The copy never carries the
        /// original's read-only flag: it is ours to delete once it has gone back.
        /// </summary>
        public static void Keep(string original, string kept) =>
            SafeFile.WriteAllBytes(kept, File.ReadAllBytes(original));

        /// <summary>
        /// Puts the kept copy back over <paramref name="target"/>, with the read-only flag set again when the original
        /// had it. False, with nothing written, when the kept copy is gone (deleted outside the game): the caller logs
        /// it as lost. Throws when the copy is there but cannot go back (locked), so it is tried again later.
        /// </summary>
        public static bool PutBack(string kept, string target, bool readOnly)
        {
            if (!File.Exists(kept)) return false;

            // SafeFile takes a read-only mark off the target for the rename, and puts it back if the write fails
            SafeFile.WriteAllBytes(target, File.ReadAllBytes(kept));
            if (readOnly) File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
            return true;
        }

        /// <summary>
        /// Writes a file of the AC install whole, in one rename: a new file in its place, never the old one changed
        /// in place. A file hard-linked to another name (a mod manager, a second install) is thereby unlinked rather
        /// than changed under both names, and what goes back after the race is the only copy that was changed.
        /// </summary>
        public static void Replace(string target, byte[] content) => SafeFile.WriteAllBytes(target, content);
    }
}
