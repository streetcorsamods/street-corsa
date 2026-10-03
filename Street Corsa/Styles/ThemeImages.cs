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
/// that does not read is told once and the next one down is used. Every mod picture is looked at once at start-up
/// (<see cref="Use"/>), so what is wrong is told with the rest of the mods' problems.
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

    // Mod files that did not read, passed over from then on
    private static readonly ConcurrentDictionary<string, bool> Broken = new(StringComparer.OrdinalIgnoreCase);

    // Small pictures shown again and again (the tiles' icons), decoded once at their size: by name and size
    private static readonly ConcurrentDictionary<(string Name, int Size), BitmapImage?> Icons = new();

    private static ModSet? _mods;

    /// <summary>
    /// The mods the pictures are looked for in (<see cref="Theme.Apply"/>); the installed ones when not told. Each name a
    /// mod gives a picture for is found here and then, so a picture that does not read is told at start-up.
    /// </summary>
    public static void Use(ModSet mods)
    {
        _mods = mods;
        Found.Clear();
        Broken.Clear();
        Icons.Clear();

        foreach (var name in NamesInMods(mods)) ModFile(name);
    }

    /// <summary>The names the mods have pictures for (<c>icons/quit</c>), each once</summary>
    private static HashSet<string> NamesInMods(ModSet mods)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods.Mods)
        {
            if (mod.Dir(Theme.FolderName) is not { } dir) continue;
            var images = Path.Combine(dir, FolderName);
            if (!Directory.Exists(images)) continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(images, "*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(file);
                    if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) continue;
                    var relative = Path.GetRelativePath(images, file);
                    names.Add(relative[..^extension.Length].Replace(Path.DirectorySeparatorChar, '/'));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read: its pictures are looked for by name as they are needed
                Logger.Warning(ex, "Theme: could not look through {Folder}", images);
            }
        }

        return names;
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
                if (!File.Exists(file) || Broken.ContainsKey(file)) continue;
                if (Reads(file, out var problem)) return file;

                Broken[file] = true;

                var line = $"{mods[i].Id}/{Theme.FolderName}/{FolderName}/{name}{extension}: {problem}; the picture under it is used";
                Logger.Warning("Theme: {Problem}", line);
                ModProblems.Add("Theme", line);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the file is a picture WPF reads: its header only, the picture is decoded when it is shown. Decoding all of
    /// it here would cost about 60 ms a background for little: WIC draws a cut-off or garbled PNG or JPEG as far as it
    /// goes, and what it does refuse fails here already.
    /// </summary>
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
            if (!uri.IsFile) return null;

            // Its header read, the rest did not (or it changed on disk since): passed over, and the picture under it
            // looked for again. Each time round one more file is out, so this ends at the game's own.
            Broken[uri.LocalPath] = true;
            Found.TryRemove(name, out _);
            Logger.Warning(ex, "Theme: the picture {File} could not be decoded; the one under it is used", uri.LocalPath);
            ModProblems.Add("Theme", $"{uri.LocalPath}: {ex.Message}; the picture under it is used");
            return Load(name, decodeWidth, decodeHeight);
        }
    }

    /// <summary>
    /// A small picture shown again and again (a tile's icon), <paramref name="size"/> pixels wide: decoded the first time
    /// and kept, frozen, for every screen after. Not for the large pictures, which are let go of with their screen.
    /// </summary>
    public static BitmapImage? Icon(string name, int size) => Icons.GetOrAdd((name, size), key => Load(key.Name, key.Size));

    /// <summary>A picture only a mod gives (the intro's logo, the window's icon): null when no mod has one</summary>
    public static BitmapImage? LoadModImage(string name, int decodeWidth = 0) =>
        ModFile(name) == null ? null : Load(name, decodeWidth);

    /// <summary>
    /// The window's icon from a mod with every size in the file kept, so Windows picks the one it draws (title bar,
    /// taskbar); null when no mod has one or it does not read
    /// </summary>
    public static BitmapFrame? LoadModIcon()
    {
        if (ModFile(WindowIcon) is not { } file) return null;
        try
        {
            var frame = BitmapFrame.Create(new Uri(file, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            Logger.Warning(ex, "Theme: the window icon {File} could not be read; the game's is kept", file);
            return null;
        }
    }

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
