using System.IO;
using StreetCorsa.Logging;
using StreetCorsa.Services.Configuration;

namespace StreetCorsa.Services.Race
{
    /// <summary>
    /// The mark sc_race leaves once it has loaded whole for a race: a file named by the race's context id in
    /// <c>Documents\Assetto Corsa\out\sc_race_started</c> (the last lines of apps\new-modes\sc_race\mode.lua).
    ///
    /// A race that comes back without a result is forfeited: the player walked away from it. But a mode that failed to
    /// load never ran the race at all (a CSP without what it calls made every race on a clean AC a forfeit,
    /// 2026-11-01), and that is no walking away. With no mark the launcher voids the race instead.
    ///
    /// Apart from the result inbox (<c>out\sc_race</c>), so the inbox's reader never meets it. A mod's format with a mode
    /// of its own writes no mark, and its races are settled as before.
    /// </summary>
    public sealed class ModeStartMark(string folder)
    {
        /// <summary>Documents\Assetto Corsa\out\sc_race_started, where the mode writes it</summary>
        public static string DefaultFolder => Path.Combine(AcDocuments.Out, "sc_race_started");

        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("RaceMode");

        public string Folder => folder;

        /// <summary>A mark older than this belongs to a race long settled (or a save that is gone): taken away</summary>
        internal static readonly TimeSpan LeftoverAge = TimeSpan.FromDays(30);

        private string PathOf(Guid contextId) => Path.Combine(folder, contextId.ToString("D"));

        /// <summary>
        /// Whether the mode marked the race; null when that cannot be told (the folder is there but cannot be read): the
        /// race is then settled as before. No folder at all is no mark (the mode makes it with its first mark).
        /// </summary>
        public bool? Seen(Guid contextId)
        {
            try
            {
                if (!Directory.Exists(folder)) return false;
                // Listed, not File.Exists, which says false for a folder it may not read too
                return Directory.EnumerateFiles(folder, contextId.ToString("D")).Any();
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                _logger.Warning(ex, "Could not look for race {ContextId}'s start mark in {Folder}", contextId, folder);
                return null;
            }
        }

        /// <summary>The marks older than <see cref="LeftoverAge"/> taken away. Never throws.</summary>
        public void Prune()
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                var before = DateTime.UtcNow - LeftoverAge;
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
                {
                    if (file.LastWriteTimeUtc >= before) continue;
                    try { file.Delete(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                _logger.Warning(ex, "Could not take old start marks away from {Folder}", folder);
            }
        }

        /// <summary>Takes the race's mark away (before the race, so none is left from before; after it is settled). Never throws.</summary>
        public void Clear(Guid contextId)
        {
            try
            {
                var path = PathOf(contextId);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.Warning(ex, "Could not take race {ContextId}'s start mark away from {Folder}", contextId, folder);
            }
        }
    }
}
