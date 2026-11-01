using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Styles;

/// <summary>
/// The game's look, made at start-up before the first window and again whenever another content plays (the main
/// screen's core, a game's setting; <see cref="Apply"/>): the colour, size and typeface keys (Colors.xaml,
/// Typography.xaml, Fonts.xaml) with the mods' theme over them (<see cref="ThemeFile"/>; the core's own theme.json
/// only lists those keys for modders, it is not read), then the brushes and styles made from them. The style
/// dictionaries take their colours as they load, so the mods' go in first; every view finds them in the application's
/// resources. What is on show keeps the look it was made in; screens made after a switch take the new one.
/// </summary>
public static class Theme
{
    /// <summary>A mod's theme folder: its theme/*.json, theme/fonts, theme/images</summary>
    public const string FolderName = ContentTypes.Theme;

    /// <summary>A mod's own typefaces, in its theme folder</summary>
    public const string FontsFolder = "fonts";

    // What a theme may change, then what is made of it, in the order they need one another
    private const string Colors = "Colors";
    private const string Sizes = "Typography";
    private const string Fonts = "Fonts";
    private static readonly string[] Styles = ["Brushes", "Icons", "Buttons", "Inputs", "Radio"];

    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Theme");

    // What the last Apply put into the application's resources: the next one (another content in play) takes it out
    private static readonly List<ResourceDictionary> Applied = [];

    /// <summary>
    /// The look, into <paramref name="resources"/> (the application's), ahead of whatever is there, in place of the
    /// look applied before (the main screen's core, a game's setting). What is on show keeps the look it was made in;
    /// screens made from here on take this one.
    /// </summary>
    public static void Apply(ResourceDictionary resources, ModSet mods)
    {
        foreach (var dictionary in Applied) resources.MergedDictionaries.Remove(dictionary);
        Applied.Clear();

        ThemeImages.Use(mods);

        var colors = Dictionary(Colors);
        var sizes = Dictionary(Sizes);
        var fonts = Dictionary(Fonts);

        var problems = new List<string>();
        var theme = ThemeFile.Read(mods, KeysOf(colors, sizes, fonts), problems);
        foreach (var (key, color) in theme.Colors) colors[key] = color;
        foreach (var (key, size) in theme.Sizes) sizes[key] = size;
        foreach (var (key, font) in theme.Fonts) fonts[key] = font;

        foreach (var problem in problems) Logger.Warning("Theme: {Problem}", problem);
        ModProblems.Add("Theme", problems);
        var changed = theme.Colors.Count + theme.Sizes.Count + theme.Fonts.Count;
        if (changed > 0) Logger.Information("Theme: {Count} keys from the mods", changed);

        // One at a time, each in the application's resources before the next loads: Brushes.xaml finds the colours
        // there, Buttons.xaml the brushes
        var at = 0;
        foreach (var dictionary in new[] { colors, sizes, fonts }.Concat(Styles.Select(Dictionary)))
        {
            resources.MergedDictionaries.Insert(at++, dictionary);
            Applied.Add(dictionary);
        }
    }

    /// <summary>The keys of each kind in the game's own dictionaries: what a theme may set</summary>
    public static ThemeKeys KeysOf(ResourceDictionary colors, ResourceDictionary sizes, ResourceDictionary fonts) => new(
        colors.Keys.OfType<string>().Where(k => colors[k] is Color),
        sizes.Keys.OfType<string>().Where(k => sizes[k] is double),
        fonts.Keys.OfType<string>().Where(k => fonts[k] is FontFamily),
        fonts.Keys.OfType<string>().Where(k => fonts[k] is FontFamily).ToDictionary(k => k, k => ((FontFamily)fonts[k]).Source));

    private static ResourceDictionary Dictionary(string name) =>
        new() { Source = new Uri($"pack://application:,,,/Styles/{name}.xaml", UriKind.Absolute) };

    /// <summary>
    /// A colour of the theme for code that makes its own brushes, at <paramref name="alpha"/> when given (a view that
    /// sets how see-through it is itself); black when there is no application to ask (tests)
    /// </summary>
    public static Color Color(string key, byte? alpha = null)
    {
        var color = System.Windows.Application.Current?.TryFindResource(key) is Color found ? found : System.Windows.Media.Colors.Black;
        return alpha is { } a ? System.Windows.Media.Color.FromArgb(a, color.R, color.G, color.B) : color;
    }
}
