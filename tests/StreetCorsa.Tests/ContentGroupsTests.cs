using System.Numerics;
using StreetCorsa.Mods;
using StreetCorsa.Services.Garage;

namespace StreetCorsa.Tests;

/// <summary>
/// The Content folder kept tidy (2026-10-26): mods in group folders one level down (<c>engines</c>, <c>settings</c>),
/// an add-on that plays in some settings only (<c>"settings"</c>), and a setting placing a core prop in its own garage
/// without a copy of its model
/// </summary>
public class ContentGroupsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private void Mod(string folder, string manifest) => _temp.File($"{folder}/{ModSet.ManifestFileName}", manifest);

    private string Showroom(string garage) => Path.Combine(_temp.Path, "showrooms", garage, garage + ".kn5");

    [Fact]
    public void Mods_in_a_group_folder_load_as_if_they_were_at_the_top()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("settings/rods", """{ "id": "rods", "type": "setting" }""");
        Mod("engines/big_v8", """{ "id": "big_v8" }""");
        Mod("loose_radio", """{ "id": "loose_radio" }""");

        var mods = ModSet.Load(_temp.Path);

        Assert.Equal("rods", mods.Setting?.Id);
        Assert.NotNull(mods.Get("big_v8"));
        Assert.NotNull(mods.Get("loose_radio"));
        Assert.Equal(Path.Combine(_temp.Path, "engines", "big_v8"), mods.Get("big_v8")!.Folder);
        Assert.Empty(mods.Problems);
    }

    [Fact]
    public void A_group_switched_off_takes_its_mods_with_it_and_a_mod_in_one_is_switched_off_alone()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("_engines/big_v8", """{ "id": "big_v8" }""");
        Mod("addons/radio", """{ "id": "radio" }""");
        Mod("addons/_jukebox", """{ "id": "jukebox" }""");

        var mods = ModSet.Load(_temp.Path);

        Assert.Null(mods.Get("big_v8"));
        Assert.Null(mods.Get("jukebox"));
        Assert.NotNull(mods.Get("radio"));
    }

    [Fact]
    public void A_folder_with_no_mod_json_and_no_mod_in_it_is_still_told_it_is_no_mod()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File("forgot_manifest/parts/stock/battery.json", "{}");

        var mods = ModSet.Load(_temp.Path);

        Assert.Contains(mods.Problems, p => p.StartsWith("forgot_manifest: no mod.json", StringComparison.Ordinal));
    }

    [Fact]
    public void The_core_in_a_group_is_no_core()
    {
        Mod("settings/streetcorsa", """{ "name": "Not the core" }""");

        var mods = ModSet.Load(_temp.Path);

        Assert.Contains(mods.Problems, p => p.Contains("the id \"streetcorsa\" is the core's"));
        Assert.Contains(mods.Problems, p => p.Contains("is missing"));
    }

    [Fact]
    public void An_add_on_for_some_settings_rests_while_another_or_none_plays()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("settings/rods", """{ "id": "rods", "type": "setting", "order": 1 }""");
        Mod("settings/touge", """{ "id": "touge", "type": "setting", "order": 2 }""");
        Mod("engines/big_v8", """{ "id": "big_v8", "settings": ["Rods"] }""");
        Mod("engines/v8_heads", """{ "id": "v8_heads", "requires": { "big_v8": "" } }""");
        Mod("addons/radio", """{ "id": "radio" }""");

        var rods = ModSet.Load(_temp.Path, setting: "rods");
        var touge = ModSet.Load(_temp.Path, setting: "touge");
        var everything = ModSet.Load(_temp.Path, everything: true);

        Assert.NotNull(rods.Get("big_v8"));
        Assert.NotNull(rods.Get("v8_heads"));
        Assert.Null(touge.Get("big_v8"));
        Assert.Null(touge.Get("v8_heads"));
        Assert.Contains(touge.Resting, m => m.Id == "big_v8");
        Assert.NotNull(touge.Get("radio"));
        Assert.NotNull(everything.Get("big_v8"));
        // Resting is no problem: the pack comes in with its setting
        Assert.Empty(touge.Problems);
    }

    [Fact]
    public void A_settings_own_settings_list_is_not_read()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("rods", """{ "id": "rods", "type": "setting", "settings": ["touge"] }""");

        var mods = ModSet.Load(_temp.Path, setting: "rods");

        Assert.Equal("rods", mods.Setting?.Id);
        Assert.Empty(mods.Get("rods")!.ForSettings);
        Assert.Contains(mods.Problems, p => p.StartsWith("rods: \"settings\" is for an add-on", StringComparison.Ordinal));
    }

    [Fact]
    public void A_setting_places_the_cores_stand_in_its_garage_and_keeps_its_model()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/props/engine_stand/engine_stand.kn5", "x");
        _temp.File($"{ModIds.BaseId}/props/engine_stand/prop.json", """
            { "name": "Engine stand", "model": "engine_stand.kn5", "credit": "by someone, CC BY 4.0",
              "places": { "garage": { "position": [3, 0, 0.3] } } }
            """);
        Mod("rods", """{ "id": "rods", "type": "setting" }""");
        _temp.File("rods/props/engine_stand/prop.json", """{ "places": { "workshop": { "position": [5.4, 0, 0.6] } } }""");
        var mods = ModSet.Load(_temp.Path, setting: "rods");

        var stand = GarageProps.Find(mods, "engine_stand", Showroom("workshop"), builtIn: true)!;

        Assert.Equal(Path.Combine(_temp.Path, ModIds.BaseId, "props", "engine_stand", "engine_stand.kn5"), stand.Model);
        Assert.Equal("Engine stand", stand.Name);
        Assert.Equal("by someone, CC BY 4.0", stand.Credit);
        Assert.Equal(new Vector3(5.4f, 0, 0.6f), stand.Position);
        // The setting's places are the ones: the core's garage has none from it
        Assert.Null(GarageProps.Find(mods, "engine_stand", Showroom("garage"), builtIn: true));
    }

    [Fact]
    public void A_dressing_prop_with_no_model_of_its_own_keeps_the_cores_and_one_with_its_own_has_that()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/props/engine_hoist/engine_hoist.kn5", "x");
        _temp.File($"{ModIds.BaseId}/props/engine_hoist/prop.json", """{ "name": "Engine hoist", "places": { } }""");
        Mod("rods", """{ "id": "rods", "type": "setting" }""");
        _temp.File("rods/props/engine_hoist/prop.json", """{ "places": { "workshop": { "position": [5.6, 0, 4.6], "heading": 90 } } }""");
        var problems = new List<string>();

        var hoist = Assert.Single(GarageProps.FindAll(ModSet.Load(_temp.Path, setting: "rods"), Showroom("workshop"), [], problems));

        Assert.EndsWith(Path.Combine(ModIds.BaseId, "props", "engine_hoist", "engine_hoist.kn5"), hoist.Model);
        Assert.Equal("Engine hoist", hoist.Name);
        Assert.True(problems.Count == 0, string.Join(" | ", problems));

        _temp.File("rods/props/engine_hoist/engine_hoist.kn5", "y");
        hoist = Assert.Single(GarageProps.FindAll(ModSet.Load(_temp.Path, setting: "rods"), Showroom("workshop"), []));
        Assert.EndsWith(Path.Combine("rods", "props", "engine_hoist", "engine_hoist.kn5"), hoist.Model);
    }
}
