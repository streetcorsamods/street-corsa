using System.Windows.Media.Imaging;
using StreetCorsa.Logging;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// A picture off the disk shown small (a racer's portrait, a track's preview), decoded at a small width and frozen
    /// once, then shared for the session, as <see cref="Garage.GarageScreenViewModel.CalendarImage"/> does. The files
    /// are often 1024 px or more for a card a hundred pixels wide: decoded at full size, every visit to the screen
    /// would decode them again at many times the memory.
    /// </summary>
    public static class Thumbnails
    {
        /// <summary>The pictures once decoded, by file and width; null for a file that could not be read</summary>
        private static readonly Dictionary<(string Path, int Width), BitmapImage?> Cache = new();

        /// <summary>
        /// The picture at <paramref name="path"/>, decoded <paramref name="decodeWidth"/> pixels wide (about twice the
        /// width it is shown at, so it stays sharp on a high-DPI screen); null without a path or when it cannot be read
        /// </summary>
        public static BitmapImage? Load(string? path, int decodeWidth)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var key = (path, decodeWidth);
            if (Cache.TryGetValue(key, out var cached)) return cached;

            BitmapImage? image = null;
            try
            {
                image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                image.DecodePixelWidth = decodeWidth;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
            }
            catch (Exception ex)
            {
                // The logger only now: a test that touches this class first must not need the logging set up
                AppLoggerFactory.CreateLogger(LogCategory.UI).Warning("The picture {Path} could not be read: {Error}", path, ex.Message);
                image = null;
            }

            Cache[key] = image;
            return image;
        }
    }
}
