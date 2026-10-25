using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Rules;
using StreetCorsa.Services.Ai;
using StreetCorsa.Services.Opponents;

namespace StreetCorsa.Tests;

/// <summary>
/// The core-only review's fixes: each setting's own Ollama model, a save's rules and racers from before the King became
/// any setting's boss, portraits saved before mods
/// </summary>
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

    [Fact]
    public void A_saves_king_and_king_weight_are_read_as_the_boss_and_the_boss_weight()
    {
        var started = System.Text.Json.Nodes.JsonNode.Parse(RuleBook.Installed.ToJson())!.AsObject();
        var rivals = started["rivals"]!.AsObject();
        var boss = rivals["boss"]!.DeepClone().AsObject();
        boss["money"] = 123456;
        rivals.Remove("boss");
        rivals["king"] = boss;
        var street = started["street"]!.AsObject();
        street.Remove("bossWeight");
        street["kingWeight"] = 0.75;

        var book = RuleBook.ForSave(started.ToJsonString());

        Assert.Equal(123456m, book.Rivals.Boss.Money);
        Assert.Equal(0.75, book.Street.BossWeight);
    }

    [Fact]
    public void A_saves_one_boss_from_before_bosses_is_the_boss_of_his_path()
    {
        var game = new GameState();
        var king = new Opponent { Name = "Old boss", IsKing = true };
        game.Racers.AddRacer(king);

        Bosses.BringUpToDate(game);

        Assert.True(king.IsBoss);
        Assert.Equal("King", king.BossPath);
        Assert.False(king.IsKing);
    }

    [Fact]
    public void A_portrait_saved_before_mods_is_the_game_s_own_racers()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(temp.Combine("streetcorsa"));
        temp.File("period/mod.json", """{ "id": "period", "type": "setting" }""");
        temp.Bytes("period/racers/drv_001.png", [1]);
        var mods = ModSet.Load(temp.Path, setting: "period");

        Assert.Equal(temp.Combine("period", "racers", "drv_001.png"), Portraits.Resolve(mods, "/Assets/Opponents/drv_001.png"));
        Assert.Null(Portraits.Resolve(mods, "/Assets/Opponents/../../outside.png"));
    }
}
