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

    /// <summary>The game's logo on the intro; the lettered title stands in only when none reads</summary>
    public const string IntroLogo = "intro/logo";

    /// <summary>The garage the intro plays in, lit and the car's headlights on</summary>
    public const string IntroBackground = "backgrounds/init";

    /// <summary>The intro's garage with its lights on and the car's headlights off</summary>
    public const string IntroLights = "intro/lights";

    /// <summary>The most rows of lights the intro switches on, one picture each</summary>
    public const int IntroLightRowsMost = 9;

    /// <summary>
    /// What the intro's nth row of lights lights (from 1): white, the part in its alpha, laid over
    /// <see cref="IntroLights"/>; any size, stretched to it
    /// </summary>
    public static string IntroLightRow(int n) => $"intro/light_{n}";

    /// <summary>The window's (and the taskbar's) picture; the core's is the exe's icon</summary>
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

    /// <summary>
    /// Who gives the picture of that name: the folder of the mod's pictures, <see cref="string.Empty"/> for the game's
    /// own, null when there is none. Pictures made to go together (the intro's light-up) are only used together.
    /// </summary>
    public static string? Giver(string name)
    {
        if (ModFile(name) is { } file) return file[..^(name.Length + Path.GetExtension(file).Length)];
        return GameHas(name) ? string.Empty : null;
    }

    /// <summary>Whether the game has a picture of that name compiled in; looked up once, never by opening it</summary>
    public static bool GameHas(string name) =>
        GameNames.Value is { } names ? names.Contains(name) : GameHasByOpening(name);

    private const string GameImagesFolder = "assets/images/";

    // The names of the pictures compiled in, from the game's resources; null when they cannot be listed (no WPF app)
    private static readonly Lazy<HashSet<string>?> GameNames = new(ListGameNames);

    private static HashSet<string>? ListGameNames()
    {
        try
        {
            var assembly = System.Windows.Application.ResourceAssembly;
            if (assembly == null) return null;
            using var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + ".g.resources");
            if (stream == null) return null;

            // WPF keeps them lower case, escaped as in a URI: assets/images/intro/light_1.png
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = new System.Resources.ResourceReader(stream);
            foreach (System.Collections.DictionaryEntry entry in reader)
            {
                if (entry.Key is not string key || !key.StartsWith(GameImagesFolder, StringComparison.Ordinal)
                    || !key.EndsWith(".png", StringComparison.Ordinal)) continue;
                names.Add(System.Uri.UnescapeDataString(key[GameImagesFolder.Length..^4]));
            }
            return names;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or BadImageFormatException
                                       or ArgumentException or System.Resources.MissingManifestResourceException)
        {
            Logger.Warning(ex, "Theme: could not list the game's own pictures; each is looked for as it is asked for");
            return null;
        }
    }

    private static bool GameHasByOpening(string name)
    {
        try
        {
            using var stream = System.Windows.Application.GetResourceStream(GameUri(name))?.Stream;
            return stream != null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UriFormatException)
        {
            return false;
        }
    }

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
