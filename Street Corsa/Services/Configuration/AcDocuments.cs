using System.IO;

namespace StreetCorsa.Services.Configuration
{
    /// <summary>
    /// AC's folder in the player's Documents (Documents\Assetto Corsa): its cfg files, and the out folder the race mode
    /// writes its results to. The shell's Documents folder, so it follows one moved to OneDrive, as AC and CSP do.
    /// </summary>
    public static class AcDocuments
    {
        /// <summary>Documents\Assetto Corsa</summary>
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Assetto Corsa");

        /// <summary>Documents\Assetto Corsa\cfg</summary>
        public static string Cfg => Path.Combine(Root, "cfg");

        /// <summary>Documents\Assetto Corsa\out</summary>
        public static string Out => Path.Combine(Root, "out");
    }
}
