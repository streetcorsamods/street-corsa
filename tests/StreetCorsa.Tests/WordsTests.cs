using System.Text.RegularExpressions;
using StreetCorsa.Mods;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// The words on the screens as data (step E3): every key the code, the XAML and the race mode say is in the base
/// game's text, a translation says its own words over the game's and the game's where it has none, the race mode's
/// words reach it through race.ini whatever they hold
/// </summary>
public partial class WordsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private void Mod(string id, string manifest) => _temp.File($"{id}/{ModSet.ManifestFileName}", manifest);

    [GeneratedRegex(@"\{t:Text\s+(?:Key=)?([\w.\-]+)\s*\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"\b(?:GameText|PartWords)\.Say\(\s*""([^""]+)""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"\bsay\('([A-Z0-9_]+)'")]
    private static partial Regex LuaKey();

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(RepoPaths.GameProject, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Every_key_the_screens_and_the_code_say_has_words_in_the_base_game()
    {
        var text = GameText.Load(ModSet.Load(RepoPaths.Content));
        var missing = new List<string>();
        foreach (var file in SourceFiles("*.xaml"))
        foreach (Match match in XamlKey().Matches(File.ReadAllText(file)))
            if (!text.Has(match.Groups[1].Value)) missing.Add($"{Path.GetFileName(file)}: {match.Groups[1].Value}");

        foreach (var file in SourceFiles("*.cs"))
        foreach (Match match in CodeKey().Matches(File.ReadAllText(file)))
            if (!text.Has(match.Groups[1].Value)) missing.Add($"{Path.GetFileName(file)}: {match.Groups[1].Value}");

        Assert.Empty(missing);
    }

    // Any literal that reads like a key: "work.bare", or $"loadgame.played.{unit}.one" with its holes
    [GeneratedRegex(@"\$?""([a-z][a-z0-9_]*(?:\.(?:[a-z0-9_]+|\{[^{}""]+\}))+)""")]
    private static partial Regex KeyLike();

    private static readonly string[] FileExtensions =
        ["json", "ini", "db", "kn5", "ksanim", "png", "jpg", "dds", "wav", "ogg", "txt", "lua", "dll", "exe", "cfg", "acd", "log", "xaml", "zip", "bak", "tmp", "csv"];

    // Names in the same spaces that are no words: sound cues, a rule's path, the car scripts' triggers; and the keys of
    // older translations the code still reads when one has them (station.quarts.*, now station.oil_short.qt.*)
    private static readonly HashSet<string> NotWords =
    [
        "common.bank", "engine.hoist", "engine.land", "engine.turn", "station.bell", "station.pump", "station.pump.start", "station.pump.stop", "radio.knob", "radio.tune", "radio.static",
        "rivals.money", "station.quarts.{count}", "station.quarts.many",
        "garage.arrive", "garage.leave", "station.arrive", "station.at_pump", "station.leave", "street.rival_arrive", "street.rival_leave",
        "diner.arrive", "diner.parked", "diner.leave", "booth.arrive", "booth.standing", "booth.leave", "booth.spray", "work.bench"
    ];

    [Fact]
    public void Every_key_the_code_builds_has_words_in_the_base_game()
    {
        // The keys built in code (a ternary, a switch, a key the code adds .one or .other to, a key with a hole in it)
        // escape the test above. A literal in one of the game's key spaces ("work.", "market.") must be a key, or
        // start keys (the code adds the rest), or with holes read as at least one key
        var text = GameText.Load(ModSet.Load(RepoPaths.Content));
        var keys = text.Keys.ToList();
        var spaces = keys.Where(k => k.Contains('.')).Select(k => k[..k.IndexOf('.')]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var file in SourceFiles("*.cs").Where(f => !EnglishOnPurpose.Any(f.Contains)))
        foreach (Match match in KeyLike().Matches(File.ReadAllText(file)))
        {
            var key = match.Groups[1].Value;
            if (!spaces.Contains(key[..key.IndexOf('.')]) || FileExtensions.Contains(key[(key.LastIndexOf('.') + 1)..])) continue;
            if (!NotWords.Contains(key) && !IsKeyOrStartsKeys(key, text, keys)) missing.Add($"{Path.GetFileName(file)}: {key}");
        }

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing.Distinct()));
    }

    private static bool IsKeyOrStartsKeys(string key, GameText text, List<string> keys)
    {
        if (!key.Contains('{')) return text.Has(key) || keys.Any(k => k.StartsWith(key + ".", StringComparison.OrdinalIgnoreCase));

        var pattern = "^" + Regex.Replace(Regex.Escape(key), @"\\\{[^}]*}", "[^.]+") + @"(\..+)?$";
        return keys.Any(k => Regex.IsMatch(k, pattern, RegexOptions.IgnoreCase));
    }

    // Tag is a GameButton's hover label, Message a loader's line
    [GeneratedRegex(@"\b(Text|Content|Header|ToolTip|Title|Tag|Value|Message)=""([^""{]*[A-Za-z]{2,}[^""]*)""")]
    private static partial Regex XamlLiteral();

    // The developer's screens stay in English (the owner, 2026-09-30)
    private static readonly string[] EnglishOnPurpose =
    [
        $"{Path.DirectorySeparatorChar}CarCatalogEditor{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}SoundBrowser{Path.DirectorySeparatorChar}"
    ];

    // The window's title is game.json's, set as it opens: only the window's Title= is no word of the screens
    private static bool IsWindowTitle(string file, Match match) =>
        match.Groups[1].Value == "Title" && Path.GetFileName(file) == "MainWindow.xaml";

    [Fact]
    public void No_screen_writes_its_words_in_the_xaml()
    {
        var literal = new List<string>();
        foreach (var file in SourceFiles("*.xaml").Where(f => !EnglishOnPurpose.Any(f.Contains)))
        foreach (var (line, number) in File.ReadLines(file).Select((l, i) => (l, i + 1)))
        {
            if (line.TrimStart().StartsWith("d:", StringComparison.Ordinal) || line.Contains(" d:")) continue;
            // A setter's value may be a colour or a size; only a setter of words counts
            var wordsSetter = line.Contains("<Setter") && Regex.IsMatch(line, @"Property=""(?:Text|Content|Header|ToolTip|Title)""");
            foreach (Match match in XamlLiteral().Matches(line))
            {
                if (match.Groups[1].Value == "Value" && !wordsSetter) continue;
                if (IsWindowTitle(file, match)) continue;
                // A quote mark written &quot; is no word
                if (Regex.IsMatch(System.Net.WebUtility.HtmlDecode(match.Groups[2].Value), "[A-Za-z]{2,}"))
                    literal.Add($"{Path.GetFileName(file)}:{number}: {match.Groups[2].Value}");
            }
        }

        Assert.True(literal.Count == 0, string.Join(Environment.NewLine, literal));
    }

    [Fact]
    public void Every_line_the_race_mode_says_comes_from_the_base_game()
    {
        var text = GameText.Load(ModSet.Load(RepoPaths.Content));
        var handed = RaceText.IniLines(text).Skip(1).Select(l => l[..l.IndexOf('=')]).ToHashSet();
        // The mode and its format modules (format_*.lua)
        var lua = string.Join("\n", Directory.GetFiles(Path.Combine(RepoPaths.Root, "apps", "new-modes", "sc_race"), "*.lua").Select(File.ReadAllText));

        var said = LuaKey().Matches(lua).Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.NotEmpty(said);
        Assert.DoesNotContain(said, k => !handed.Contains(k));
    }

    [Fact]
    public void A_translation_says_its_words_and_the_games_own_where_it_has_none()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/text/ui.json", """{ "lines": { "language.name": "English", "hello": "Hello", "bye": "Bye {name}" } }""");
        Mod("spanish", """{ "id": "spanish" }""");
        _temp.File("spanish/text/es/ui.json", """{ "lines": { "language.name": "Español", "hello": "Hola" } }""");

        var mods = ModSet.Load(_temp.Path);
        var english = GameText.Load(mods);
        var spanish = GameText.Load(mods, "es");

        Assert.Equal("Hello", english.Get("hello"));
        Assert.Equal("Hola", spanish.Get("hello"));
        Assert.Equal("Bye Ana", spanish.Get("bye", new TextValues { ["name"] = "Ana" }));
        Assert.Equal("es", spanish.Translation);
        Assert.Equal([("en", "English"), ("es", "Español")], GameText.Languages(mods));
    }

    [Fact]
    public void What_a_mod_removes_or_rewrites_stays_so_in_a_translation()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/text/ui.json", """{ "lines": { "hello": "Hello", "bye": "Bye", "title": "Garage" } }""");
        _temp.File($"{ModIds.BaseId}/text/es/ui.json", """{ "lines": { "hello": "Hola", "bye": "Adiós", "title": "Taller" } }""");
        Mod("tweak", """{ "id": "tweak", "removes": { "text": ["bye"] } }""");
        _temp.File("tweak/text/ui.json", """{ "replace": { "hello": "Howdy", "title": "Shop" } }""");
        _temp.File("tweak/text/es/ui.json", """{ "replace": { "title": "Tienda" } }""");

        var spanish = GameText.Load(ModSet.Load(_temp.Path), "es");

        Assert.Equal("es", spanish.Translation);
        Assert.Empty(spanish.Lines("bye"));
        // Rewritten with no words of its own in the language: the mod's
        Assert.Equal("Howdy", spanish.Get("hello"));
        Assert.Equal("Tienda", spanish.Get("title"));
        Assert.Empty(spanish.Problems);
    }

    [Fact]
    public void The_games_own_language_is_its_game_json_s()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/game.json", """{ "language": "es" }""");
        _temp.File($"{ModIds.BaseId}/text/ui.json", """{ "lines": { "language.name": "Español", "hello": "Hola" } }""");
        _temp.File($"{ModIds.BaseId}/text/en/ui.json", """{ "lines": { "language.name": "English", "hello": "Hello" } }""");

        var mods = ModSet.Load(_temp.Path);

        Assert.Equal([("es", "Español"), ("en", "English")], GameText.Languages(mods));
        Assert.Null(GameText.Load(mods, "es").Translation);
        Assert.Equal("Hello", GameText.Load(mods, "en").Get("hello"));
    }

    [Fact]
    public void A_language_no_mod_has_is_told_and_the_games_own_words_are_said()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/text/ui.json", """{ "lines": { "hello": "Hello" } }""");

        var text = GameText.Load(ModSet.Load(_temp.Path), "fr");

        Assert.Equal("Hello", text.Get("hello"));
        Assert.Null(text.Translation);
        Assert.Contains(text.Problems, p => p.Contains("fr"));
        Assert.True(text.LanguageMissing);
    }

    [Fact]
    public void The_race_modes_words_go_through_the_ini_as_they_are_written()
    {
        var line = " Señor, you're out; $5,000 = {driver}\n\"gone\" ";

        var encoded = RaceText.Encode(line);

        Assert.Matches("^[A-Za-z0-9 .!?_%-]*$", encoded);
        Assert.StartsWith("%20", encoded);
        Assert.EndsWith("%20", encoded);
        Assert.Equal(line, Decode(encoded));
        Assert.Equal("RESULT_WIN", RaceText.IniKey("race.result.win"));
    }

    /// <summary>What the mode does with a value: every %XX back to its byte, the bytes read as UTF-8</summary>
    private static string Decode(string value)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%')
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.Add((byte)value[i]);
            }
        }

        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }
}
