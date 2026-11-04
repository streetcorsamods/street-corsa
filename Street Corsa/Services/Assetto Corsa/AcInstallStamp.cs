using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace StreetCorsa.Services;

/// <summary>
/// A cheap fingerprint of what the import reads of an AC install: the car and track folders by name, and the write times
/// (and sizes) of what it reads in them. A few file stats a car, not a read of every ui_car.json: a game started on an
/// install that has not changed since the last look skips the full scan. What it does not see (a skin's ui_skin.json
/// edited in place) is read at the next start-up, which always scans in full.
/// </summary>
public static class AcInstallStamp
{
    /// <summary>The install at <paramref name="acRoot"/> as it stands now; null when it cannot be told (a scan is due then)</summary>
    public static string? Of(string? acRoot)
    {
        if (string.IsNullOrWhiteSpace(acRoot)) return null;

        try
        {
            var text = new StringBuilder(acRoot.TrimEnd('\\', '/').ToUpperInvariant()).Append('\n');
            var cars = Path.Combine(acRoot, "content", "cars");
            var tracks = Path.Combine(acRoot, "content", "tracks");
            if (!Directory.Exists(cars) || !Directory.Exists(tracks)) return null;

            Folders(text, cars, "ui\\ui_car.json", "skins", "data.acd", "data");
            Folders(text, tracks, "ui");

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // Each folder under parent by name with its write time, and those of the entries named in it (a missing one written as such)
    private static void Folders(StringBuilder text, string parent, params string[] entries)
    {
        text.Append(Directory.GetLastWriteTimeUtc(parent).Ticks).Append('\n');
        foreach (var folder in Directory.EnumerateDirectories(parent).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            text.Append(Path.GetFileName(folder)).Append('|').Append(Directory.GetLastWriteTimeUtc(folder).Ticks);
            foreach (var entry in entries)
            {
                var path = Path.Combine(folder, entry);
                text.Append('|');
                if (File.Exists(path)) text.Append(File.GetLastWriteTimeUtc(path).Ticks).Append(':').Append(new FileInfo(path).Length);
                else if (Directory.Exists(path)) text.Append(Directory.GetLastWriteTimeUtc(path).Ticks);
                else text.Append('-');
            }
            text.Append('\n');
        }
    }
}
