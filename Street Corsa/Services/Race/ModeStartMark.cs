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

        private string PathOf(Guid contextId) => Path.Combine(folder, contextId.ToString("D"));

        /// <summary>Whether the mode marked the race; null when that cannot be told (the race is then settled as before)</summary>
        public bool? Seen(Guid contextId)
        {
            try
            {
                return File.Exists(PathOf(contextId));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.Warning(ex, "Could not look for race {ContextId}'s start mark in {Folder}", contextId, folder);
                return null;
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
