using StreetCorsa.Mods;
using StreetCorsa.Services.Ai;

namespace StreetCorsa.Tests;

/// <summary>
/// A made-up setting's AI for the tests of the talk: the core has the plumbing, the Settings test and a plain persona; a
/// setting brings its voice (<c>ai/prompts.json</c> and <c>ai/ollama.json</c>, in Fixtures\test_ai), who the people are
/// (<c>ai/people.json</c>, their temperaments) and what everybody knows (<c>ai/lore.json</c>)
/// </summary>
internal static class TestAi
{
    public const string SettingId = "talk_setting";

    public const string People = """
        { "temperaments": {
            "chatty": { "words": "Friendly and talkative, but honest.", "give": 1.0, "for": [ "seller", "buyer" ], "weight": 2 },
            "gruff": { "words": "Gruff and short with words, hates being lowballed.", "give": 0.5, "for": [ "seller", "buyer" ], "weight": 1 },
            "smooth": { "words": "A smooth salesman who never stops selling.", "give": 0.8, "for": [ "salesman" ] },
            "helpful": { "words": "Knows the stock and likes to help.", "give": 0, "for": [ "clerk" ] },
            "calm": { "words": "Cool-headed, lets the car do the talking.", "for": [ "rival" ], "maxAggression": 50 },
            "hothead": { "words": "A hothead who takes every word as a dare.", "for": [ "rival" ], "minAggression": 50 }
        } }
        """;

    public const string Lore = """
        { "lore": {
            "town": { "lines": [ "The main road runs from the river to the hills.", "Friday nights everybody is out.", "The gas is cheap at the corner.",
                                 "The old mill closed last spring.", "There is a race track an hour away.", "The bridge is being fixed." ] },
            "cars": { "lines": [ "A sleeper looks slow and isn't.", "Everybody wants more power.", "Good tyres win races." ] }
        } }
        """;

    /// <summary>The core of the source tree and the made-up setting in <paramref name="temp"/>, the setting played</summary>
    public static ModSet Mods(TempDir temp)
    {
        temp.File($"{SettingId}/{ModSet.ManifestFileName}", $$"""{ "id": "{{SettingId}}", "type": "setting" }""");
        temp.File($"{SettingId}/ai/people.json", People);
        temp.File($"{SettingId}/ai/lore.json", Lore);
        foreach (var file in Directory.GetFiles(Fixtures))
            temp.File($"{SettingId}/ai/{Path.GetFileName(file)}", File.ReadAllText(file));
        return ModSet.Load(temp.Path, alsoFrom: [RepoPaths.BaseContent], setting: SettingId);
    }

    /// <summary>The made-up setting's prompts and Ollama model</summary>
    private static string Fixtures => Path.Combine(RepoPaths.Root, "tests", "StreetCorsa.Tests", "Fixtures", "test_ai");

    /// <summary>The prompts of the core with the made-up setting's, read once</summary>
    public static AiPrompts Prompts => LoadedPrompts.Value;

    private static readonly Lazy<AiPrompts> LoadedPrompts = new(() =>
    {
        using var temp = new TempDir();
        return AiPrompts.Load(Mods(temp));
    });
}
