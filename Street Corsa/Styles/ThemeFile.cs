using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using StreetCorsa.Mods;

namespace StreetCorsa.Styles;

/// <summary>
/// The keys a theme may set, by kind: the names of the colours (Colors.xaml), the sizes (Typography.xaml) and the
/// typefaces (Fonts.xaml). Anything else in a theme.json is a mistake the player is told of.
/// </summary>
public sealed class ThemeKeys(
    IEnumerable<string> colors,
    IEnumerable<string> sizes,
    IEnumerable<string> fonts,
    IReadOnlyDictionary<string, string>? gameFonts = null)
{
    /// <summary>
    /// The game's own typeface for each font key: a theme naming that one (a copy of the base game's theme.json) is
    /// not told of when the PC lacks it, since the game's own stands anyway
    /// </summary>
    public IReadOnlyDictionary<string, string> GameFonts { get; } = gameFonts ?? new Dictionary<string, string>();

    // Matched whatever their case; the set gives back the key as the game writes it
    private readonly HashSet<string> _colors = new(colors, StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sizes = new(sizes, StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fonts = new(fonts, StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Colors => _colors;
    public IReadOnlySet<string> Sizes => _sizes;
    public IReadOnlySet<string> Fonts => _fonts;

    /// <summary>The keys of a theme's section ("colors", "sizes", "fonts", any case); null for a section there is not</summary>
    internal HashSet<string>? Section(string section) => section.ToLowerInvariant() switch
    {
        ThemeFile.ColorsSection => _colors,
        ThemeFile.SizesSection => _sizes,
        ThemeFile.FontsSection => _fonts,
        _ => null
    };
}

/// <summary>
/// The look the mods give: every <c>theme/*.json</c> of each mod, in load order, so a later mod's key goes over an
/// earlier one's. A file names only what it changes:
/// <code>
/// {
///   "colors": { "AccentPrimary": "#2E7D32", "BgDark": "#101418" },
///   "sizes":  { "FontSizeBody": 15 },
///   "fonts":  { "DisplayFont": "Bebas Neue" }
/// }
/// </code>
/// A colour is written as WPF reads one (<c>#RGB</c>, <c>#RRGGBB</c>, <c>#AARRGGBB</c>, a colour's name); a size is a
/// number of pixels; a typeface is a family name, found first among the fonts in the mod's own <c>theme/fonts</c>, then
/// among the fonts installed on the PC. A key the game does not have, or a value that does not read, is told and left
/// out: the game's own look stands for it. Keys are matched whatever their case; names starting with <c>_</c> are notes.
/// The base game's own theme folder is not read: its theme.json is the list of keys for modders, written from the
/// dictionaries the game already has.
/// </summary>
public sealed class ThemeFile
{
    public const string ColorsSection = "colors";
    public const string SizesSection = "sizes";
    public const string FontsSection = "fonts";

    /// <summary>The largest size a theme may set: past it a number is a mistake, not a size</summary>
    public const double MaxSize = 2000;

    public Dictionary<string, Color> Colors { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Sizes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, FontFamily> Fonts { get; } = new(StringComparer.Ordinal);

    /// <summary>Every mod's theme files but the base game's, over one another in load order</summary>
    /// <param name="installedFonts">Whether a family of that name is installed on the PC; all of Windows' when not given</param>
    public static ThemeFile Read(ModSet mods, ThemeKeys keys, List<string> problems, Func<string, bool>? installedFonts = null)
    {
        var theme = new ThemeFile();
        foreach (var (mod, file) in mods.JsonFiles(Theme.FolderName))
        {
            if (mod.IsBase) continue;
            var fontsFolder = mod.Dir(Theme.FolderName) is { } dir ? Path.Combine(dir, Theme.FontsFolder) : null;
            theme.ReadFile(file, $"{mod.Id}/{Theme.FolderName}/{Path.GetFileName(file)}", fontsFolder, keys, problems, installedFonts);
        }

        return theme;
    }

    /// <summary>One theme file over what is read so far; a file that does not read changes nothing</summary>
    public void ReadFile(string file, string source, string? fontsFolder, ThemeKeys keys, List<string> problems, Func<string, bool>? installedFonts = null)
    {
        if (ModMerge.ReadFile(file, source, problems) is { } read) ReadOne(read, source, keys, fontsFolder, installedFonts ?? IsInstalled, problems);
    }

    private void ReadOne(JsonObject file, string source, ThemeKeys keys, string? fontsFolder, Func<string, bool> installedFonts, List<string> problems)
    {
        foreach (var (section, node) in file)
        {
            if (section.StartsWith('_')) continue;

            var known = keys.Section(section);
            if (known == null)
            {
                problems.Add($"{source}: \"{section}\" is no part of a theme (colors, sizes, fonts); left out");
                continue;
            }

            if (node is not JsonObject values)
            {
                problems.Add($"{source}: \"{section}\" is not an object of keys; left out");
                continue;
            }

            foreach (var (name, value) in values)
            {
                if (name.StartsWith('_')) continue;

                if (!known.TryGetValue(name, out var key))
                {
                    problems.Add($"{source}: {section}.{name} is not a key the game has; left out");
                    continue;
                }

                switch (section.ToLowerInvariant())
                {
                    case ColorsSection when ParseColor(value) is { } color:
                        Colors[key] = color;
                        break;
                    case ColorsSection:
                        problems.Add($"{source}: colors.{key} \"{value}\" is not a colour (#RRGGBB, #AARRGGBB); the game's is kept");
                        break;
                    case SizesSection when ParseSize(value) is { } size:
                        Sizes[key] = size;
                        break;
                    case SizesSection:
                        problems.Add($"{source}: sizes.{key} {value?.ToJsonString()} is not a size (a number above 0, up to {MaxSize}); the game's is kept");
                        break;
                    default:
                        if (FindFont(value, fontsFolder, installedFonts) is { } font)
                        {
                            Fonts[key] = font;
                        }
                        else if (value is JsonValue face && face.TryGetValue<string>(out var faceName)
                                 && keys.GameFonts.TryGetValue(key, out var own) && own.Equals(faceName.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            // The game's own face, not on this PC: what the game shows anyway, so nothing to tell
                            Fonts.Remove(key);
                        }
                        else problems.Add($"{source}: fonts.{key} {value?.ToJsonString()} is in neither the mod's {Theme.FolderName}/{Theme.FontsFolder} nor installed; the game's is kept");
                        break;
                }
            }
        }
    }

    /// <summary>The colour a theme writes, null when it reads as none</summary>
    public static Color? ParseColor(JsonNode? value)
    {
        if (value is not JsonValue json || !json.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();

        // A colour with a colour profile names a file to open (even on a network share): no colour a theme needs
        if (text.StartsWith("ContextColor", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            return System.Windows.Media.ColorConverter.ConvertFromString(text) is Color color ? color : null;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or InvalidOperationException or ArgumentException
                                       or OverflowException or IndexOutOfRangeException)
        {
            // "sc#1" and its like throw InvalidOperationException from WPF's tokenizer
            return null;
        }
    }

    /// <summary>The size a theme writes, null when it is no number of pixels</summary>
    public static double? ParseSize(JsonNode? value)
    {
        if (value is not JsonValue json || !json.TryGetValue<double>(out var size)) return null;
        return double.IsFinite(size) && size > 0 && size <= MaxSize ? size : null;
    }

    /// <summary>
    /// The family of that name among the fonts in <paramref name="fontsFolder"/> (a mod's own), else the installed one;
    /// null when it is in neither
    /// </summary>
    public static FontFamily? FindFont(JsonNode? value, string? fontsFolder, Func<string, bool> installedFonts)
    {
        if (value is not JsonValue json || !json.TryGetValue<string>(out var name) || string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();

        if (fontsFolder != null && Directory.Exists(fontsFolder))
        {
            try
            {
                // WPF's own reading of the folder: the families in it, each found by its names in any language
                var folder = fontsFolder.EndsWith(Path.DirectorySeparatorChar) ? fontsFolder : fontsFolder + Path.DirectorySeparatorChar;
                foreach (var family in System.Windows.Media.Fonts.GetFontFamilies(folder))
                {
                    if (family.FamilyNames.Values.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))) return family;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
            {
                // A folder that cannot be read has no fonts in it: the installed ones are looked at
            }
        }

        return installedFonts(name) ? new FontFamily(name) : null;
    }

    private static bool IsInstalled(string name) =>
        System.Windows.Media.Fonts.SystemFontFamilies.Any(f => f.FamilyNames.Values.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)));
}
