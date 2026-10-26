using System.IO;
using LiteDB;
using StreetCorsa.Logging;

namespace StreetCorsa.Services.Catalog
{
    /// <summary>
    /// The one door to catalog.db: one instance, opened by the first call and kept open until the game closes, as LiteDB
    /// would have it and as <see cref="Storage.SaveDatabase"/> keeps a save. Calls take turns under a lock (re-entrant: a
    /// call may make another inside it on the same database), each one only for its own reads and writes; no call opens
    /// or checkpoints the file, so the UI thread never waits behind a worker's open and close.
    ///
    /// The catalog lives in %AppData%\StreetCorsa\Catalog, apart from the saves: the Load screen lists every
    /// database in the saves folder, and a player called "Catalog" would otherwise have overwritten it. A
    /// catalog.db left in the saves folder by an older version is moved over once.
    ///
    /// Everything in the catalog is read from the install again at start-up, except profiles edited by hand. So
    /// a catalog.db that is corrupt is put aside as catalog.db.bad and a new one made, rather than every call
    /// failing for good. Only a file LiteDB says is broken goes: one written by LiteDB 4 is upgraded in place, and a
    /// file that is busy, locked or refused for any other reason goes to the caller untouched.
    /// </summary>
    internal static class CatalogDatabase
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Catalog);

        private static readonly object Gate = new();

        // The open catalog and its full path; null until the first call, and again after a close
        private static LiteDatabase? _database;
        private static string? _openPath;

        // How many calls are inside Use right now (on the thread holding the lock): a call inside another may not
        // switch the file
        private static int _depth;

        private static bool _migrated;

        private static string AppDataFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreetCorsa");

        /// <summary>%AppData%\StreetCorsa\Catalog\catalog.db, the path every repository opens</summary>
        public static string DefaultPath => Path.Combine(AppDataFolder, "Catalog", "catalog.db");

        /// <summary>Where catalog.db lived before: among the saves</summary>
        private static string LegacyPath => Path.Combine(AppDataFolder, "Saves", "catalog.db");

        /// <summary>
        /// Runs <paramref name="work"/> on the catalog at <paramref name="path"/>, opening it the first time. Another path
        /// than the open one (a test's) closes that one first; inside another call that is refused, since the outer call
        /// is still using it.
        /// </summary>
        public static T Use<T>(string path, Func<LiteDatabase, T> work)
        {
            lock (Gate)
            {
                var database = OpenShared(path);
                _depth++;
                try
                {
                    return work(database);
                }
                finally
                {
                    _depth--;
                }
            }
        }

        /// <inheritdoc cref="Use{T}"/>
        public static void Use(string path, Action<LiteDatabase> work) => Use(path, db => { work(db); return true; });

        /// <summary>
        /// Closes the catalog (the game's way out, a test done with its file) once no call has it, waiting at most
        /// <paramref name="wait"/> for one that has. False when one still had it; a later call opens it again.
        /// </summary>
        public static bool TryClose(TimeSpan wait)
        {
            if (!Monitor.TryEnter(Gate, wait)) return false;
            try
            {
                if (_depth > 0) return false;
                CloseOpen();
                return true;
            }
            finally
            {
                Monitor.Exit(Gate);
            }
        }

        /// <summary>The open catalog when it is the one at <paramref name="path"/>; else that one, opened (or made). Under the lock.</summary>
        private static LiteDatabase OpenShared(string path)
        {
            var full = Path.GetFullPath(path);
            if (_database != null && string.Equals(_openPath, full, StringComparison.OrdinalIgnoreCase)) return _database;

            if (_database != null)
            {
                if (_depth > 0)
                    throw new InvalidOperationException($"catalog.db at {_openPath} is in use by the call around this one; {full} cannot be opened inside it");
                CloseOpen();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            MigrateOnce(full);
            _database = OpenOrRecreate(full);
            _openPath = full;
            return _database;
        }

        /// <summary>Disposes the open catalog, if there is one; under the lock</summary>
        private static void CloseOpen()
        {
            var open = _database;
            _database = null;
            _openPath = null;
            open?.Dispose();
        }

        /// <summary>
        /// Moves a catalog.db from the saves folder (with LiteDB's side files) to the catalog folder, the first
        /// time the catalog is opened. Runs under the lock before the catalog is open, so nothing has either file open.
        /// </summary>
        private static void MigrateOnce(string path)
        {
            if (_migrated) return;

            // Only the default catalog has an old home to come from. Another path (a test's) says nothing about it,
            // and must not stop the default one from being moved when it is opened later.
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(DefaultPath), StringComparison.OrdinalIgnoreCase)) return;

            // Handled once per run, whatever the outcome: a move that failed has left the old catalog whole where it
            // was, and trying again on every open would only log the same failure
            _migrated = true;
            Migrate(LegacyPath, path);
        }

        /// <summary>The move of <see cref="MigrateOnce"/>, from <paramref name="legacyPath"/> to <paramref name="path"/></summary>
        internal static void Migrate(string legacyPath, string path)
        {
            if (!File.Exists(legacyPath)) return;

            if (File.Exists(path))
            {
                try
                {
                    // Both there: the new one is the one in use; the old one only confuses the Load screen
                    File.Move(legacyPath, legacyPath + ".old", overwrite: true);
                    Logger.Warning("An old catalog.db was left among the saves; renamed it to {Path}", legacyPath + ".old");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logger.Warning("Could not rename the old catalog.db among the saves ({Error}); it stays at {Path}", ex.Message, legacyPath);
                }

                return;
            }

            // The side files first, the data file last. catalog-log.db holds the writes not yet folded into the
            // data file: moved after it, a failure would leave them behind, lost to a catalog that went ahead
            // without them. Moved first, a failure puts them back and the old catalog stays whole where it was.
            var legacyFolder = Path.GetDirectoryName(legacyPath)!;
            var folder = Path.GetDirectoryName(path)!;
            var moved = new List<(string From, string To)>();
            try
            {
                foreach (var side in new[] { "-log", "-tmp" })
                {
                    var from = Path.Combine(legacyFolder, "catalog" + side + ".db");
                    var to = Path.Combine(folder, "catalog" + side + ".db");
                    if (!File.Exists(from)) continue;

                    File.Move(from, to, overwrite: true);
                    moved.Add((from, to));
                }

                File.Move(legacyPath, path);
                Logger.Information("Moved catalog.db from the saves folder to {Path}", path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var restored = true;
                foreach (var (from, to) in Enumerable.Reverse(moved))
                {
                    try
                    {
                        File.Move(to, from, overwrite: true);
                    }
                    catch (Exception back) when (back is IOException or UnauthorizedAccessException)
                    {
                        restored = false;
                        Logger.Error(back, "Could not put {File} back among the saves; it is at {Path}", Path.GetFileName(from), to);
                    }
                }

                // The import rebuilds the catalog from the install; only hand-edited profiles stay behind, in the old file
                if (restored)
                    Logger.Warning("Could not move catalog.db from the saves folder ({Error}); it stays at {Old}, and a new " +
                        "catalog is started at {Path}", ex.Message, legacyPath, path);
                else
                    Logger.Error(ex, "Could not move catalog.db from the saves folder, and its side files are split between " +
                        "{Old} and {Folder}; a new catalog is started", legacyFolder, folder);
            }
        }

        private static LiteDatabase OpenOrRecreate(string path)
        {
            try
            {
                return new LiteDatabase(new ConnectionString { Filename = path });
            }
            catch (LiteException ex) when (ex.ErrorCode == LiteException.INVALID_DATABASE)
            {
                // "Not a LiteDB 5 file": either written by LiteDB 4, which LiteDB upgrades in place (keeping a backup
                // of the old file next to it), or not a database at all. Only once the upgrade fails is it broken.
                try
                {
                    var upgraded = new LiteDatabase(new ConnectionString { Filename = path, Upgrade = true });
                    Logger.Warning("catalog.db was written by an older LiteDB; upgraded it in place");
                    return upgraded;
                }
                catch (Exception upgrade) when (IsCorrupt(upgrade))
                {
                    return Recreate(path, upgrade);
                }
            }
            catch (Exception ex) when (IsCorrupt(ex))
            {
                return Recreate(path, ex);
            }
        }

        /// <summary>
        /// The errors that say the file itself is broken. Not "in use" (an IOException, or LiteDB's lock timeout
        /// and already-open), not a password (the catalog has none; a file that wants one is somebody else's to
        /// look at), not a database shut down under us: those go to the caller and the file stays.
        /// </summary>
        private static bool IsCorrupt(Exception ex) => ex switch
        {
            LiteException lite => lite.ErrorCode is LiteException.INVALID_DATABASE
                or LiteException.INVALID_DATAFILE_STATE
                or LiteException.INVALID_FREE_SPACE_PAGE,
            // Thrown while decoding the pages of the file; opening reads nothing else
            InvalidCastException or InvalidDataException or FormatException or EndOfStreamException => true,
            _ => false
        };

        private static LiteDatabase Recreate(string path, Exception ex)
        {
            var bad = path + ".bad";
            Logger.Warning(ex, "catalog.db is corrupt ({Type} {Code}); moving it to {Bad} and starting a new one. " +
                "Car profiles edited by hand are in the old file", ex.GetType().Name,
                ex is LiteException lite ? lite.ErrorCode : 0, bad);

            File.Move(path, bad, overwrite: true);
            foreach (var side in new[] { "-log", "-tmp" })
            {
                var file = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + side + ".db");
                if (File.Exists(file)) File.Move(file, file + ".bad", overwrite: true);
            }

            return new LiteDatabase(new ConnectionString { Filename = path });
        }
    }
}
