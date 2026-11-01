using StreetCorsa.Mods;

namespace StreetCorsa.Tests;

/// <summary>The Mod Manager: mods listed with what the game makes of them, switched off and on, deleted</summary>
public class ModManagerTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Content => _temp.Combine("Content");

    private void Mod(string folder, string manifest) => _temp.File($"Content/{folder}/mod.json", manifest);

    [Fact]
    public void Every_mod_is_listed_but_the_core_settings_first_switched_off_ones_too()
    {
        Mod("streetcorsa", "{}");
        Mod("settings/rod", """{ "id": "rod", "type": "setting", "name": "Street Rod", "version": "0.1.0", "author": "Pablo" }""");
        Mod("engines/v8", """{ "id": "v8", "settings": ["rod"] }""");
        Mod("engines/_six", """{ "id": "six" }""");
        Mod("loose", """{ "id": "loose" }""");
        Mod("_parked/old", """{ "id": "old" }""");
        Mod("_installing/x", """{ "id": "x" }""");

        var mods = ModManager.List(Content);

        Assert.Equal(["rod", "loose", "six", "v8"], mods.Select(m => m.Id));
        var rod = mods[0];
        Assert.True(rod.IsSetting && rod.On && rod.Loaded);
        Assert.Equal("settings", rod.Group);
        Assert.Equal("Street Rod", rod.Info!.Name);
        Assert.False(mods[2].On);
        Assert.False(mods[2].Loaded);
        Assert.Equal("", mods[1].Group);
    }

    [Fact]
    public void What_the_game_says_of_a_mod_is_on_its_row()
    {
        Mod("streetcorsa", "{}");
        Mod("addons/future", """{ "id": "future", "gameVersion": "99.0.0" }""");
        Mod("addons/broken", "{ not json");

        var mods = ModManager.List(Content);

        var future = mods.Single(m => m.Id == "future");
        Assert.False(future.Loaded);
        Assert.Contains(future.Problems, p => p.Contains("99.0.0"));
        var broken = mods.Single(m => m.FolderName == "broken");
        Assert.Null(broken.Info);
        Assert.NotEmpty(broken.Problems);
    }

    [Fact]
    public void A_mod_is_switched_off_and_on_by_its_folders_name_and_the_game_follows()
    {
        Mod("streetcorsa", "{}");
        Mod("addons/radio", """{ "id": "radio" }""");

        var radio = ModManager.List(Content).Single();
        var off = ModManager.Switch(radio, on: false);
        Assert.Equal(Path.Combine(Content, "addons", "_radio"), off);
        Assert.DoesNotContain(ModSet.Load(Content).Mods, m => m.Id == "radio");

        var on = ModManager.Switch(ModManager.List(Content).Single(), on: true);
        Assert.Equal(Path.Combine(Content, "addons", "radio"), on);
        Assert.Contains(ModSet.Load(Content).Mods, m => m.Id == "radio");
    }

    [Fact]
    public void Switching_onto_a_taken_name_is_refused_and_nothing_moves()
    {
        Mod("addons/_radio", """{ "id": "radio" }""");
        Mod("addons/radio", """{ "id": "radio2" }""");

        var parked = ModManager.List(Content).Single(m => !m.On);

        Assert.Throws<IOException>(() => ModManager.Switch(parked, on: true));
        Assert.True(Directory.Exists(Path.Combine(Content, "addons", "_radio")));
    }

    [Fact]
    public void A_deleted_mod_is_gone()
    {
        Mod("streetcorsa", "{}");
        Mod("settings/rod", """{ "id": "rod", "type": "setting" }""");
        _temp.File("Content/settings/rod/text/words.json", "{}");

        ModManager.Delete(ModManager.List(Content).Single());

        Assert.False(Directory.Exists(Path.Combine(Content, "settings", "rod")));
        Assert.Empty(ModManager.List(Content));
        Assert.True(File.Exists(Path.Combine(Content, "streetcorsa", "mod.json")));
    }
}
