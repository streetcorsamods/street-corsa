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

    [GeneratedRegex(@"GameText\.Say\(\s*""([^""]+)""")]
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

    [Fact]
    public void Every_line_the_race_mode_says_comes_from_the_base_game()
    {
        var text = GameText.Load(ModSet.Load(RepoPaths.Content));
        var handed = text.Keys.Where(k => k.StartsWith("race.", StringComparison.OrdinalIgnoreCase)).Select(RaceText.IniKey).ToHashSet();
        var lua = File.ReadAllText(Path.Combine(RepoPaths.Root, "apps", "new-modes", "sc_race", "mode.lua"));

        var said = LuaKey().Matches(lua).Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.NotEmpty(said);
        Assert.Empty(said.Where(k => !handed.Contains(k)));
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
    public void A_language_no_mod_has_is_told_and_the_games_own_words_are_said()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/text/ui.json", """{ "lines": { "hello": "Hello" } }""");

        var text = GameText.Load(ModSet.Load(_temp.Path), "fr");

        Assert.Equal("Hello", text.Get("hello"));
        Assert.Null(text.Translation);
        Assert.Contains(text.Problems, p => p.Contains("fr"));
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
