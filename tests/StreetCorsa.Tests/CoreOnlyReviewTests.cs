using StreetCorsa.Mods;
using StreetCorsa.Services.Ai;

namespace StreetCorsa.Tests;

/// <summary>The core-only review's fixes: each setting's own Ollama model, no talk without the prompts to talk by</summary>
[Collection(GlobalsCollection.Name)]
public sealed class CoreOnlyReviewTests
{
    [Fact]
    public void Each_setting_has_an_ollama_model_of_its_own_unless_it_names_one()
    {
        var core = AiPrompts.Load(ModSet.CoreOf(RepoPaths.Content)).Ollama!;
        Assert.Equal("streetcorsa", core.Name);

        // The made-up setting gives its voice, not a name: its model is called after it, its voice in it
        var setting = TestAi.Prompts.Ollama!;
        Assert.Equal($"streetcorsa-{TestAi.SettingId}", setting.Name);
        Assert.NotEmpty(setting.SystemText);

        using var temp = new TempDir();
        temp.File("named/mod.json", """{ "id": "named", "type": "setting" }""");
        temp.File("named/ai/ollama.json", """{ "name": "wangan", "system": "You speak for Tokyo." }""");
        var named = AiPrompts.Load(ModSet.Load(temp.Path, alsoFrom: [RepoPaths.BaseContent], setting: "named")).Ollama!;
        Assert.Equal("wangan", named.Name);
    }

    [Fact]
    public void Nobody_is_offered_a_talk_when_the_mods_have_no_prompts_to_talk_by()
    {
        var ai = new FakeAiText((_, _) => Task.FromResult<string?>("Hello."));
        var core = AiPrompts.Load(ModSet.CoreOf(RepoPaths.Content));

        // The core has the plumbing but no chat prompt: talking is a setting's
        Assert.False(new Services.Talk.TalkDesk(ai, prompts: () => core).IsAvailable);
        Assert.True(new Services.Talk.TalkDesk(ai, prompts: () => TestAi.Prompts).IsAvailable);
    }
}
