using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Styles;

/// <summary>
/// The game's pictures by name (<c>backgrounds/main</c>, <c>icons/garage</c>, <c>misc/calendar/calendar01</c>): a mod's
/// <c>theme/images/&lt;name&gt;.png</c> (or .jpg, .jpeg, .bmp, .ico) stands in for the game's own, the last mod in
/// load order first; else the picture compiled into the game, <c>Assets/Images/&lt;name&gt;.png</c>. A mod's picture
/// that does not read is told once and the next one down is used.
/// </summary>
public static class ThemeImages
{
    /// <summary>The pictures' folder in a mod's theme folder</summary>
    public const string FolderName = "images";

    /// <summary>A game's own logo for the intro, over the lettered title; the base game has none</summary>
    public const string IntroLogo = "intro/logo";

    /// <summary>The window's (and the taskbar's) picture; the base game's is the exe's icon</summary>
    public const string WindowIcon = "window/icon";

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".ico"];

    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Theme");

    // The mod file found for each name (null: none), looked for once
    private static readonly ConcurrentDictionary<string, string?> Found = new(StringComparer.OrdinalIgnoreCase);

    private static ModSet? _mods;

    /// <summary>The mods the pictures are looked for in (<see cref="Theme.Apply"/>); the installed ones when not told</summary>
    public static void Use(ModSet mods)
    {
        _mods = mods;
        Found.Clear();
    }

    /// <summary>The picture compiled into the game</summary>
    public static Uri GameUri(string name) => new($"pack://application:,,,/Assets/Images/{name}.png", UriKind.Absolute);

    /// <summary>Where the picture of that name is read from: a mod's, else the game's</summary>
    public static Uri Uri(string name) => ModFile(name) is { } file ? new Uri(file, UriKind.Absolute) : GameUri(name);

    /// <summary>The file of the last mod (in load order) that has a picture of that name that reads; null for none</summary>
    public static string? ModFile(string name) => Found.GetOrAdd(name, Find);

    private static string? Find(string name)
    {
        var mods = (_mods ?? ModSet.Installed).Mods;
        for (var i = mods.Count - 1; i >= 0; i--)
        {
            if (mods[i].Dir(Theme.FolderName) is not { } dir) continue;

            foreach (var extension in Extensions)
            {
                var file = Path.Combine(dir, FolderName, name.Replace('/', Path.DirectorySeparatorChar) + extension);
                if (!File.Exists(file)) continue;
                if (Reads(file, out var problem)) return file;

                var line = $"{mods[i].Id}/{Theme.FolderName}/{FolderName}/{name}{extension}: {problem}; the picture under it is used";
                Logger.Warning("Theme: {Problem}", line);
                ModProblems.Add("Theme", line);
            }
        }

        return null;
    }

    /// <summary>Whether the file is a picture WPF reads: its header only, the picture is decoded when it is shown</summary>
    private static bool Reads(string file, out string problem)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            problem = string.Empty;
            if (decoder.Frames.Count > 0) return true;
            problem = "no picture in it";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            problem = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// The picture of that name, decoded once at the size given (0: as it is) and frozen. The game's own is there for
    /// every name the game uses; a name only a mod gives is null without one.
    /// </summary>
    public static BitmapImage? Load(string name, int decodeWidth = 0, int decodeHeight = 0)
    {
        var uri = Uri(name);
        try
        {
            return Decode(uri, decodeWidth, decodeHeight);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            if (uri.IsFile)
            {
                // Read at the header, broken further in: the game's own picture, from now on
                Found[name] = null;
                Logger.Warning(ex, "Theme: the picture {File} could not be decoded; the game's is used", uri.LocalPath);
                ModProblems.Add("Theme", $"{uri.LocalPath}: {ex.Message}; the game's picture is used");
                try
                {
                    return Decode(GameUri(name), decodeWidth, decodeHeight);
                }
                catch (Exception inner) when (inner is IOException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
                {
                    return null;
                }
            }

            return null;
        }
    }

    /// <summary>A picture only a mod gives (the intro's logo, the window's icon): null when no mod has one</summary>
    public static BitmapImage? LoadModImage(string name, int decodeWidth = 0) =>
        ModFile(name) == null ? null : Load(name, decodeWidth);

    private static BitmapImage Decode(Uri uri, int decodeWidth, int decodeHeight)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = uri;
        if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        if (decodeHeight > 0) image.DecodePixelHeight = decodeHeight;
        // Read whole at once and the file let go of: a mod's file is not held open while the game runs
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
