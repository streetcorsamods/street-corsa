using System.IO;
using System.Windows.Media.Imaging;
using StreetCorsa.Logging;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// A picture off the disk shown small (a racer's portrait, a track's preview), decoded at a small width and frozen
    /// once, then shared for the session, as <see cref="Garage.GarageScreenViewModel.CalendarImage"/> does. The files
    /// are often 1024 px or more for a card a hundred pixels wide: decoded at full size, every visit to the screen
    /// would decode them again at many times the memory. Safe to call off the UI thread: the picture comes frozen.
    /// </summary>
    public static class Thumbnails
    {
        /// <summary>
        /// The pictures once decoded, by file, width and when the file was last written (a picture edited on disk is
        /// read again). A file that could not be read is not kept: it is tried again the next time it is shown, as a
        /// portrait locked for a moment by a scanner or still being written must not stay blank all session.
        /// </summary>
        private static readonly Dictionary<(string Path, int Width, DateTime Written), BitmapImage> Cache = new();

        /// <summary>The files already warned about, so a picture that never reads is logged once, not on every visit</summary>
        private static readonly HashSet<string> Warned = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The picture at <paramref name="path"/>, decoded <paramref name="decodeWidth"/> pixels wide (about twice the
        /// width it is shown at, so it stays sharp on a high-DPI screen); null without a path or when it cannot be read
        /// </summary>
        public static BitmapImage? Load(string? path, int decodeWidth)
        {
            if (string.IsNullOrEmpty(path)) return null;

            try
            {
                var key = (path, decodeWidth, File.GetLastWriteTimeUtc(path));
                lock (Cache)
                {
                    if (Cache.TryGetValue(key, out var cached)) return cached;
                }

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                image.DecodePixelWidth = decodeWidth;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                lock (Cache) Cache[key] = image;
                return image;
            }
            catch (Exception ex)
            {
                // The logger only now: a test that touches this class first must not need the logging set up
                bool first;
                lock (Warned) first = Warned.Add(path);
                if (first)
                    AppLoggerFactory.CreateLogger(LogCategory.UI).Warning("The picture {Path} could not be read: {Error}", path, ex.Message);
                return null;
            }
        }
    }
}
