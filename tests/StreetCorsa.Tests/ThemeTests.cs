using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Xml.Linq;
using StreetCorsa.Audio;
using StreetCorsa.Mods;
using StreetCorsa.Styles;

namespace StreetCorsa.Tests;

/// <summary>
/// The look as data (step E5): the colour, size and typeface keys live in their three dictionaries and nowhere else,
/// a mod's theme.json goes over them key by key, its fonts and pictures stand in for the game's, and the base game's
/// theme.json lists every key at the game's own value
/// </summary>
[Collection(ThemeCollection.Name)]
public partial class ThemeTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose()
    {
        ThemeImages.Use(ModSet.Load(_temp.Path));
        _temp.Dispose();
    }

    private static string Styles => Path.Combine(RepoPaths.GameProject, "Styles");

    private static string BaseTheme => Path.Combine(RepoPaths.BaseContent, Theme.FolderName, "theme.json");

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The keys of a dictionary of the game, in the order written, with their values as written</summary>
    private static List<(string Key, string Kind, string Value)> Keys(string dictionary) =>
        XDocument.Load(Path.Combine(Styles, dictionary + ".xaml")).Root!.Elements()
            .Where(e => e.Attribute(X + "Key") != null)
            .Select(e => (e.Attribute(X + "Key")!.Value, e.Name.LocalName, e.Value.Trim()))
            .ToList();

    private static ThemeKeys GameKeys() => new(
        Keys("Colors").Where(k => k.Kind == "Color").Select(k => k.Key),
        Keys("Typography").Where(k => k.Kind == "Double").Select(k => k.Key),
        Keys("Fonts").Where(k => k.Kind == "FontFamily").Select(k => k.Key),
        Keys("Fonts").Where(k => k.Kind == "FontFamily").ToDictionary(k => k.Key, k => k.Value));

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(RepoPaths.GameProject, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private void Mod(string id, int order = 0) =>
        _temp.File($"{id}/{ModSet.ManifestFileName}", $$"""{ "id": "{{id}}", "order": {{order}} }""");

    /// <summary>
    /// The base game's theme.json is the dictionaries' keys written out, so a modder reads every key there is and the
    /// game looks as it did. Set SC_WRITE_BASE_THEME=1 to write it again after a key is added or changed.
    /// </summary>
    [Fact]
    public void The_base_games_theme_lists_every_key_at_the_games_own_value()
    {
        var sections = new (string Name, string Dictionary, string Kind)[]
        {
            (ThemeFile.ColorsSection, "Colors", "Color"),
            (ThemeFile.SizesSection, "Typography", "Double"),
            (ThemeFile.FontsSection, "Fonts", "FontFamily")
        };

        var expected = new JsonObject
        {
            ["_comment"] = new JsonArray(
                "The base game's look: every key a theme may set, at the game's own value (written from Styles/Colors.xaml,",
                "Typography.xaml and Fonts.xaml). A mod changes any of them with a .json file of its own in its theme folder,",
                "naming only what it changes; its fonts go in theme/fonts, its pictures in theme/images (see docs/modding.md,",
                "\"theme\"). Colours are #AARRGGBB or #RRGGBB, sizes are pixels, fonts are family names.")
        };
        foreach (var (name, dictionary, kind) in sections)
        {
            var values = new JsonObject();
            foreach (var key in Keys(dictionary).Where(k => k.Kind == kind))
                values[key.Key] = kind == "Double" ? JsonValue.Create(double.Parse(key.Value, System.Globalization.CultureInfo.InvariantCulture)) : JsonValue.Create(key.Value);
            expected[name] = values;
        }

        if (Environment.GetEnvironmentVariable("SC_WRITE_BASE_THEME") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BaseTheme)!);
            File.WriteAllText(BaseTheme, expected.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }) + "\n");
        }

        var written = JsonNode.Parse(File.ReadAllText(BaseTheme))!.AsObject();
        foreach (var (name, _, _) in sections)
            Assert.Equal(expected[name]!.ToJsonString(), written[name]?.ToJsonString());

        // And it reads back as it is (a mod copying it gets no problem): no key the game lacks, no value it cannot take,
        // on a PC with none of the game's own faces installed too
        var problems = new List<string>();
        var theme = new ThemeFile();
        theme.ReadFile(BaseTheme, "theme.json", null, GameKeys(), problems, _ => false);
        Assert.Empty(problems);
        Assert.Equal(GameKeys().Colors.Count, theme.Colors.Count);

        // The game itself does not read it: the dictionaries already are what it says
        Assert.Empty(ThemeFile.Read(ModSet.BaseOnly(RepoPaths.BaseContent), GameKeys(), problems).Colors);
    }

    [Fact]
    public void A_key_of_a_kind_lives_only_in_its_dictionary()
    {
        var strays = new List<string>();
        foreach (var file in SourceFiles("*.xaml"))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            if (name != "Colors.xaml" && text.Contains("<Color x:Key=")) strays.Add($"{name}: a colour key");
            if (name != "Fonts.xaml" && text.Contains("<FontFamily x:Key=")) strays.Add($"{name}: a typeface key");
            if (name != "Typography.xaml" && Regex.IsMatch(text, @"<sys:Double x:Key=""FontSize")) strays.Add($"{name}: a font size key");
        }

        Assert.Empty(strays);
    }

    [GeneratedRegex(@"=""#[0-9A-Fa-f]{3,8}""|(?:Color|Foreground|Background|Fill|Stroke|BorderBrush|CaretBrush|Brush|Value)=""(?:Black|White|Red|Green|Blue|Gray|Grey|Yellow|Orange|Gold|Silver|DarkGray|LightGray|LightGreen|IndianRed)""")]
    private static partial Regex XamlColour();

    [GeneratedRegex(@"FontFamily=""[^{""]|Property=""FontFamily"" Value=""[^{""]")]
    private static partial Regex XamlTypeface();

    // The colours code makes of its own: the 3D scenes' lights (data from the scenes), a converter whose brushes the
    // view always sets, the paint booth's paints (the colours a car is painted)
    private static readonly string[] OwnColoursOnPurpose =
    [
        $"{Path.DirectorySeparatorChar}Controls{Path.DirectorySeparatorChar}Street{Path.DirectorySeparatorChar}",
        "OnboardViewport3D.cs",
        "DifficultyToBrushConverter.cs",
        "PaintBoothScreenViewModel.cs"
    ];

    [GeneratedRegex(@"Color\.FromRgb|Color\.FromArgb\(\s*\w+\s*,\s*0x|Brushes\.(?!Transparent\b)[A-Z]\w+|(?<!theme\.)Colors\.(?!Black\b|Transparent\b)[A-Z]\w+")]
    private static partial Regex CodeColour();

    [Fact]
    public void The_views_name_no_colour_typeface_or_picture_of_their_own()
    {
        var strays = new List<string>();
        foreach (var file in SourceFiles("*.xaml").Where(f => !f.EndsWith("Colors.xaml") && !f.EndsWith("Fonts.xaml")))
        {
            var lines = File.ReadAllLines(file);
            // The drawn icons are white only as a mask: the button paints their shape in its own colour
            var masks = file.EndsWith("Icons.xaml");
            for (var i = 0; i < lines.Length; i++)
            {
                if (masks && lines[i].Contains("<GeometryDrawing Brush=\"White\"")) continue;
                if (XamlColour().IsMatch(lines[i]) || XamlTypeface().IsMatch(lines[i]) || lines[i].Contains("pack://"))
                    strays.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        foreach (var file in SourceFiles("*.cs").Where(f => !OwnColoursOnPurpose.Any(f.Contains)))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (CodeColour().IsMatch(lines[i])) strays.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                if (lines[i].Contains("pack://application:,,,/Assets/") && !file.EndsWith("ThemeImages.cs"))
                    strays.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.Empty(strays);
    }

    [GeneratedRegex(@"\{theme:Image\s+(?:Name=)?([\w/]+)\s*\}")]
    private static partial Regex XamlImage();

    [Fact]
    public void Every_picture_the_game_names_is_in_it()
    {
        var names = SourceFiles("*.xaml").SelectMany(f => XamlImage().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).ToList();
        names.AddRange(Enumerable.Range(1, 12).Select(n => $"misc/calendar/calendar{n:D2}"));

        Assert.True(names.Count > 20, "the pictures are named through {theme:Image}");
        var missing = names.Where(n => !File.Exists(Path.Combine(RepoPaths.GameProject, "Assets", "Images", n.Replace('/', Path.DirectorySeparatorChar) + ".png"))).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void A_later_mods_theme_goes_over_an_earlier_ones_key_by_key()
    {
        Mod("streetcorsa");
        _temp.File("streetcorsa/theme/theme.json", """{ "colors": { "AccentPrimary": "#FFFFFF", "Gold": "#FFFFFF" } }""");
        Mod("dark", 5);
        _temp.File("dark/theme/theme.json", """{ "colors": { "AccentPrimary": "#D32F2F", "BgDark": "#1B1B1B" }, "sizes": { "FontSizeBody": 14 } }""");
        Mod("green", 10);
        _temp.File("green/theme/theme.json", """
            {
              // keys in any case, notes skipped
              "_comment": "a green game",
              "colors": { "accentprimary": "#2E7D32", "_note": "x" },
              "Sizes": { "FontSizeBody": 15.5 }
            }
            """);

        var problems = new List<string>();
        var theme = ThemeFile.Read(ModSet.Load(_temp.Path), GameKeys(), problems, _ => true);

        Assert.Empty(problems);
        Assert.Equal(Color.FromRgb(0x2E, 0x7D, 0x32), theme.Colors["AccentPrimary"]);
        Assert.Equal(Color.FromRgb(0x1B, 0x1B, 0x1B), theme.Colors["BgDark"]);
        Assert.Equal(15.5, theme.Sizes["FontSizeBody"]);
        Assert.False(theme.Colors.ContainsKey("Gold"));
    }

    [Fact]
    public void What_a_theme_gets_wrong_is_told_and_the_games_own_stands()
    {
        Mod("streetcorsa");
        Mod("broken", 10);
        _temp.File("broken/theme/a.json", """
            {
              "colors": { "Nope": "#FFF", "Danger": "not a colour", "Gold": 5, "Amber": "#80FFC107" },
              "sizes": { "FontSizeBody": -3, "FontSizeHero": "big", "FontSizeTitle": 99999 },
              "fonts": { "BodyFont": "No Such Face", "DialFont": 3 },
              "pictures": {}
            }
            """);
        _temp.File("broken/theme/b.json", "{ not json");
        _temp.File("broken/theme/c.json", """{ "colors": { "Gold": "#FFF", "GOLD": "#000" } }""");
        // What WPF's colour reader throws other than a FormatException, and a colour profile it would open a file for
        _temp.File("broken/theme/d.json", """{ "colors": { "Danger": "sc#1", "Gold": "sc#1,,2,3", "Success": "ContextColor x.icc 1,0,0,0" } }""");

        var problems = new List<string>();
        var theme = ThemeFile.Read(ModSet.Load(_temp.Path), GameKeys(), problems, _ => false);

        Assert.Equal(["Amber"], theme.Colors.Keys);
        Assert.Equal(Color.FromArgb(0x80, 0xFF, 0xC1, 0x07), theme.Colors["Amber"]);
        Assert.Empty(theme.Sizes);
        Assert.Empty(theme.Fonts);
        foreach (var said in new[] { "colors.Nope", "colors.Danger", "colors.Gold", "sizes.FontSizeBody", "sizes.FontSizeHero", "sizes.FontSizeTitle", "fonts.BodyFont", "fonts.DialFont", "\"pictures\"", "b.json", "c.json", "colors.Success" })
            Assert.Contains(problems, p => p.Contains(said));
    }

    [Fact]
    public void A_typeface_is_found_in_the_mods_fonts_before_the_pcs()
    {
        var comic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "comic.ttf");
        Assert.SkipUnless(File.Exists(comic), "Comic Sans is not on this PC");

        Mod("streetcorsa");
        Mod("comic", 10);
        _temp.Bytes($"comic/theme/{Theme.FontsFolder}/comic.ttf", File.ReadAllBytes(comic));
        _temp.File("comic/theme/theme.json", """{ "fonts": { "DisplayFont": "Comic Sans MS", "BodyFont": "Installed Face" } }""");

        var problems = new List<string>();
        // Nothing is installed but "Installed Face": the mod's own font is found in its folder
        var theme = ThemeFile.Read(ModSet.Load(_temp.Path), GameKeys(), problems, name => name == "Installed Face");

        Assert.Empty(problems);
        Assert.Contains("comic", theme.Fonts["DisplayFont"].BaseUri?.LocalPath ?? theme.Fonts["DisplayFont"].Source, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Installed Face", theme.Fonts["BodyFont"].Source);
    }

    [Fact]
    public void A_mods_font_named_as_the_games_own_face_is_used_from_the_mod()
    {
        var comic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "comic.ttf");
        Assert.SkipUnless(File.Exists(comic), "Comic Sans is not on this PC");

        // The game's own face is "Comic Sans MS" here, and the PC has none of it: the mod's file is what is used
        var keys = new ThemeKeys([], [], ["DisplayFont"], new Dictionary<string, string> { ["DisplayFont"] = "Comic Sans MS" });
        Mod("streetcorsa");
        Mod("comic", 10);
        _temp.Bytes($"comic/theme/{Theme.FontsFolder}/comic.ttf", File.ReadAllBytes(comic));
        _temp.File("comic/theme/theme.json", """{ "fonts": { "displayfont": "Comic Sans MS" } }""");

        var problems = new List<string>();
        var theme = ThemeFile.Read(ModSet.Load(_temp.Path), keys, problems, _ => false);

        Assert.Empty(problems);
        Assert.Contains("comic", theme.Fonts["DisplayFont"].BaseUri?.LocalPath ?? theme.Fonts["DisplayFont"].Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_mods_picture_that_does_not_read_is_told_at_start_up_not_when_first_shown()
    {
        Mod("streetcorsa");
        Mod("broken", 10);
        _temp.File("broken/theme/images/misc/calendar/calendar03.png", "not a picture");

        ThemeImages.Use(ModSet.Load(_temp.Path));

        Assert.Contains(ModProblems.All, p => p.Contains("broken/theme/images/misc/calendar/calendar03.png"));
    }

    [Fact]
    public void A_mods_picture_that_fails_to_decode_gives_way_to_the_one_under_it_not_the_games()
    {
        var png = File.ReadAllBytes(Path.Combine(RepoPaths.GameProject, "Assets", "Images", "Icons", "trophy.png"));
        Mod("streetcorsa");
        Mod("first", 10);
        var first = Path.GetFullPath(_temp.Bytes("first/theme/images/icons/quit.png", png));
        Mod("second", 20);
        var second = Path.GetFullPath(_temp.Bytes("second/theme/images/icons/quit.png", png));

        ThemeImages.Use(ModSet.Load(_temp.Path));
        Assert.Equal(second, ThemeImages.ModFile("icons/quit"));

        // Broken on disk after it was found: the first mod's is what is shown, from then on
        File.WriteAllText(second, "not a picture any more");
        var image = ThemeImages.Load("icons/quit");

        Assert.Equal(new Uri(first), image?.UriSource);
        Assert.Equal(first, ThemeImages.ModFile("icons/quit"));
    }

    [Fact]
    public void A_mods_picture_stands_in_for_the_games_the_last_mod_first_and_one_that_does_not_read_is_passed_over()
    {
        var png = File.ReadAllBytes(Path.Combine(RepoPaths.GameProject, "Assets", "Images", "Icons", "trophy.png"));
        Mod("streetcorsa");
        Mod("first", 10);
        _temp.Bytes("first/theme/images/icons/quit.png", png);
        _temp.Bytes("first/theme/images/backgrounds/main.jpg", png);
        Mod("second", 20);
        _temp.File("second/theme/images/icons/quit.png", "not a picture");
        _temp.Bytes("second/theme/images/intro/logo.png", png);

        ThemeImages.Use(ModSet.Load(_temp.Path));

        Assert.Equal(_temp.Combine("first", "theme", "images", "icons", "quit.png"), ThemeImages.ModFile("icons/quit"));
        Assert.Equal(_temp.Combine("first", "theme", "images", "backgrounds", "main.jpg"), ThemeImages.ModFile("backgrounds/main"));
        Assert.Equal(_temp.Combine("second", "theme", "images", "intro", "logo.png"), ThemeImages.ModFile(ThemeImages.IntroLogo));
        Assert.Null(ThemeImages.ModFile("icons/garage"));
        Assert.Null(ThemeImages.ModFile(ThemeImages.WindowIcon));
        Assert.Contains(ModProblems.All, p => p.Contains("second/theme/images/icons/quit.png"));
    }

    [Fact]
    public void An_optional_cue_without_its_file_is_silent_and_no_problem()
    {
        Mod("streetcorsa");
        _temp.File("streetcorsa/sfx/music.json", """
            { "cues": { "music.intro": { "bus": "radio", "loop": true, "optional": true, "files": ["music/intro.mp3"] } } }
            """);

        var cues = SoundCues.Load(ModSet.Load(_temp.Path));

        Assert.Empty(cues.Problems);
        Assert.Null(cues.Get(StartMusic.Cue));

        _temp.File("streetcorsa/sfx/music/intro.mp3", "x");
        cues = SoundCues.Load(ModSet.Load(_temp.Path));
        Assert.Equal(AudioBus.Radio, cues.Get(StartMusic.Cue)!.Bus);
        Assert.True(cues.Get(StartMusic.Cue)!.Loop);
    }

    [Fact]
    public void The_base_game_names_the_start_music()
    {
        var cues = new SoundCues();
        cues.Read(Path.Combine(RepoPaths.BaseContent, ContentTypes.Sfx, "music.json"), "music.json");
        Assert.Empty(cues.Problems);
    }
}

/// <summary>The theme's pictures are looked up through one static resolver: its tests run one at a time</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThemeCollection
{
    public const string Name = "Theme";
}
