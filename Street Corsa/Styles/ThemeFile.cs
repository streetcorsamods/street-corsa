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
    /// <summary>The game's own typeface for each font key: a theme naming that one changes nothing, so needs no check</summary>
    public IReadOnlyDictionary<string, string> GameFonts { get; } = gameFonts ?? new Dictionary<string, string>();

    public IReadOnlySet<string> Colors { get; } = new HashSet<string>(colors, StringComparer.Ordinal);
    public IReadOnlySet<string> Sizes { get; } = new HashSet<string>(sizes, StringComparer.Ordinal);
    public IReadOnlySet<string> Fonts { get; } = new HashSet<string>(fonts, StringComparer.Ordinal);
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
/// </summary>
public sealed class ThemeFile
{
    public const string ColorsSection = "colors";
    public const string SizesSection = "sizes";
    public const string FontsSection = "fonts";

    /// <summary>The largest size a theme may set: past it a number is a mistake, not a size</summary>
    public const double MaxSize = 2000;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public Dictionary<string, Color> Colors { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Sizes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, FontFamily> Fonts { get; } = new(StringComparer.Ordinal);

    /// <summary>Every mod's theme files, over one another in load order</summary>
    /// <param name="installedFonts">Whether a family of that name is installed on the PC; all of Windows' when not given</param>
    public static ThemeFile Read(ModSet mods, ThemeKeys keys, List<string> problems, Func<string, bool>? installedFonts = null)
    {
        var theme = new ThemeFile();
        installedFonts ??= IsInstalled;
        foreach (var (mod, file) in mods.JsonFiles(Theme.FolderName))
        {
            var source = $"{mod.Id}/{Theme.FolderName}/{Path.GetFileName(file)}";
            JsonObject? read;
            try
            {
                read = JsonNode.Parse(File.ReadAllText(file), ModMerge.NodeOptions, DocumentOptions) as JsonObject;
                // A name twice in an object (whatever its case) only throws once the object is looked into: all of
                // it is, before any of it is used, so a file like that changes nothing
                LookInto(read);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                problems.Add($"{source}: {ex.Message}; left out");
                continue;
            }

            if (read == null)
            {
                problems.Add($"{source}: not an object ({{ \"colors\": {{ ... }} }}); left out");
                continue;
            }

            var fontsFolder = mod.Dir(Theme.FolderName) is { } dir ? Path.Combine(dir, Theme.FontsFolder) : null;
            theme.ReadOne(read, source, keys, fontsFolder, installedFonts, problems);
        }

        return theme;
    }

    private static void LookInto(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var (_, value) in obj) LookInto(value);
        }
    }

    private void ReadOne(JsonObject file, string source, ThemeKeys keys, string? fontsFolder, Func<string, bool> installedFonts, List<string> problems)
    {
        foreach (var (section, node) in file)
        {
            if (section.StartsWith('_')) continue;

            var known = section.ToLowerInvariant() switch
            {
                ColorsSection => keys.Colors,
                SizesSection => keys.Sizes,
                FontsSection => keys.Fonts,
                _ => null
            };
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

                var key = known.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (key == null)
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
                        // The game's own face (the base game's theme.json lists them all): nothing changes, nothing to find
                        if (value is JsonValue face && face.TryGetValue<string>(out var faceName)
                            && keys.GameFonts.TryGetValue(key, out var own) && own.Equals(faceName.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            Fonts.Remove(key);
                            break;
                        }

                        if (FindFont(value, fontsFolder, installedFonts) is { } font) Fonts[key] = font;
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
        try
        {
            return System.Windows.Media.ColorConverter.ConvertFromString(text.Trim()) is Color color ? color : null;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
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
