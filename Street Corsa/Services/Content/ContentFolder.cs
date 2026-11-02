using System.Diagnostics;
using System.IO;
using StreetCorsa.Logging;

namespace StreetCorsa.Services.Content
{
    /// <summary>The Content folder (or another folder of mods) opened for the player in Explorer: New Game, the Mod Manager</summary>
    public static class ContentFolder
    {
        /// <summary>Makes <paramref name="folder"/> when it is not there and opens it; logged, never throws</summary>
        public static void Open(string folder, IAppLogger logger)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                logger.Warning(ex, "The Content folder {Folder} could not be opened", folder);
            }
        }
    }
}
