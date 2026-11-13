using StreetCorsa.Mods;
using StreetCorsa.Parts;

namespace StreetCorsa.Tests;

/// <summary>
/// A game's setting is a mod of its own (step 42, the split): the core holds what every game needs and none of a
/// setting's content, a setting's ids go bare as the core's do (one setting plays at a time), an add-on finds a setting's
/// entry by its bare id, and a setting can take the core's parts off the shelves
/// </summary>
public class SettingSplitTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private void Mod(string folder, string manifest) => _temp.File($"{folder}/{ModSet.ManifestFileName}", manifest);

    [Fact]
    public void A_settings_ids_go_bare_and_an_add_on_finds_its_entries_by_them()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("rods_bare", """{ "id": "rods_bare", "type": "setting" }""");
        Mod("extra_lots", """{ "id": "extra_lots", "requires": { "rods_bare": "" } }""");
        var mods = ModSet.Load(_temp.Path, setting: "rods_bare");
        var registry = new ContentRegistry<object>("dealer");

        var setting = registry.Add(mods.Get("rods_bare")!, "lot", null, new object(), "rods_bare/dealers/lots.json");
        var addon = registry.Add(mods.Get("extra_lots")!, "lot", null, new object(), "extra_lots/dealers/lots.json");

        Assert.Equal("lot", setting);
        Assert.Equal("extra_lots:lot", addon);
        Assert.True(ModIds.GoesBare("rods_bare"));
        Assert.False(ModIds.GoesBare("extra_lots"));
        Assert.Equal("lot", ModIds.Normalize("rods_bare:lot"));
        // The add-on's own entry first; written in full, the setting's
        Assert.Equal("extra_lots:lot", registry.Resolve(mods.Get("extra_lots")!, "lot"));
        Assert.Equal("lot", registry.Resolve(mods.Get("extra_lots")!, "rods_bare:lot"));
        Assert.Equal(mods.Get("rods_bare"), mods.Game);
    }

    [Fact]
    public void An_add_on_naming_an_entry_it_has_not_finds_the_settings()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("rods_named", """{ "id": "rods_named", "type": "setting" }""");
        Mod("more_racers", """{ "id": "more_racers" }""");
        var mods = ModSet.Load(_temp.Path);
        var registry = new ContentRegistry<object>("racer");
        registry.Add(mods.Get("rods_named")!, "king", null, new object(), "rods_named/racers/racers.json");

        Assert.Equal("king", registry.Resolve(mods.Get("more_racers")!, "king"));
    }

    [Fact]
    public void A_settings_own_add_on_loads_before_it_with_no_circle_and_its_ids_go_bare()
    {
        // A setting's place shipped as a mod of its own (2026-11-11): the setting requires it, it plays in that setting only
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("own_rods", """{ "id": "own_rods", "type": "setting", "requires": { "own_station": "1.0" } }""");
        Mod("own_station", """{ "id": "own_station", "version": "1.0", "settings": ["own_rods"] }""");
        // A modder's add-on for the setting loads after it, so what it sets over the place's is theirs
        Mod("own_new_station", """{ "id": "own_new_station", "settings": ["own_rods"] }""");
        var mods = ModSet.Load(_temp.Path, setting: "own_rods");
        var registry = new ContentRegistry<object>("station");

        var station = registry.Add(mods.Get("own_station")!, "crossroads", null, new object(), "own_station/stations/crossroads");
        var modders = registry.Add(mods.Get("own_new_station")!, "corner", null, new object(), "own_new_station/stations/corner");

        Assert.Empty(mods.Problems);
        Assert.Equal(["own_rods"], mods.Get("own_station")!.OwnedBy);
        Assert.Empty(mods.Get("own_new_station")!.OwnedBy);
        Assert.Equal([ModIds.BaseId, "own_station", "own_rods", "own_new_station"], mods.Mods.Select(m => m.Id));
        // The setting names its place bare, as it did when the place was its own folder; a save's ids stay as they were
        Assert.Equal("crossroads", station);
        Assert.Equal("crossroads", registry.Resolve(mods.Get("own_rods")!, "crossroads"));
        Assert.Equal("crossroads", ModIds.Normalize("own_station:crossroads"));
        Assert.Equal("own_new_station:corner", modders);
    }

    [Fact]
    public void A_settings_own_add_on_rests_with_it_and_is_needed_for_it_to_be_offered()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("rest_rods", """{ "id": "rest_rods", "type": "setting", "requires": { "rest_garage": "2.0" } }""");
        Mod("rest_garage", """{ "id": "rest_garage", "version": "1.0", "settings": ["rest_rods"] }""");
        Mod("rest_touge", """{ "id": "rest_touge", "type": "setting" }""");

        var mods = ModSet.Load(_temp.Path, setting: "rest_touge");

        // Too old for the setting: the setting is not offered, and the place rests with it in the other game
        Assert.DoesNotContain(mods.Settings, s => s.Id == "rest_rods");
        Assert.Contains(mods.Problems, p => p.Contains("rest_rods") && p.Contains("2.0"));
        Assert.Contains(mods.Resting, m => m.Id == "rest_garage");
        Assert.Equal("rest_touge", mods.Setting?.Id);
    }

    [Fact]
    public void An_add_on_a_setting_requires_without_playing_in_it_alone_keeps_its_namespace()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("ns_rods", """{ "id": "ns_rods", "type": "setting", "requires": { "ns_v8": "" } }""");
        // An engine pack for every setting, and a place for the setting it does not require: neither is the setting's own
        Mod("ns_v8", """{ "id": "ns_v8" }""");
        Mod("ns_diner", """{ "id": "ns_diner", "settings": ["ns_rods"] }""");
        var mods = ModSet.Load(_temp.Path, setting: "ns_rods");

        Assert.Empty(mods.Get("ns_v8")!.OwnedBy);
        Assert.Empty(mods.Get("ns_diner")!.OwnedBy);
        Assert.Equal("ns_v8:x", ModIds.Qualify("ns_v8", "x"));
        Assert.Equal("ns_diner:x", ModIds.Qualify("ns_diner", "x"));
        Assert.Empty(mods.Problems);
    }

    [Fact]
    public void A_setting_takes_the_cores_parts_off_the_shelves_and_a_part_no_mod_has_is_told()
    {
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        _temp.Parts($"{ModIds.BaseId}/parts/stock", """
            [ { "id": "stock/battery", "name": "Battery" }, { "id": "stock/rim", "name": "Rim" } ]
            """);
        Mod("rods_parts", """{ "id": "rods_parts", "type": "setting", "removes": { "parts": ["stock/rim", "stock/nothing"] } }""");
        _temp.Parts("rods_parts/parts/wheels", """[ { "id": "wheels/mag", "name": "Mag" } ]""");
        var mods = ModSet.Load(_temp.Path);

        var catalog = PartsCatalog.Load(mods.ContentOf(ContentTypes.Parts).ToList(), mods);

        Assert.NotNull(catalog.Get("stock/battery"));
        Assert.Null(catalog.Get("stock/rim"));
        Assert.NotNull(catalog.Get("wheels/mag"));
        Assert.Contains(catalog.Problems, p => p.Contains("stock/nothing"));
        Assert.DoesNotContain(catalog.Problems, p => p.Contains("stock/rim"));
    }

    [Fact]
    public void The_core_holds_no_setting_content_and_a_setting_is_the_game_played()
    {
        var core = ModSet.Load(RepoPaths.Content, coreOnly: true);
        Mod(ModIds.BaseId, """{ "id": "streetcorsa" }""");
        Mod("some_setting", """{ "id": "some_setting", "type": "setting" }""");
        _temp.File("some_setting/dealers/lots.json", """{ "dealers": [] }""");
        var setting = ModSet.Load(_temp.Path, setting: "some_setting");

        foreach (var type in new[] { ContentTypes.Dealers, ContentTypes.PartShops, ContentTypes.Racers, ContentTypes.Events, ContentTypes.Milestones,
                     ContentTypes.Victories, ContentTypes.Streets, ContentTypes.StreetKit, ContentTypes.Diners, ContentTypes.Stations, ContentTypes.Booths,
                     ContentTypes.Warehouses, ContentTypes.ShopScenes, ContentTypes.Cars })
        {
            Assert.Empty(core.Folders(type));
        }

        // Its props are only the ones its own work draws, for every setting to place (2026-10-26): no garage dressing
        Assert.Equal(["engine_hoist", "engine_stand", "lift"],
            core.SubFolders(ContentTypes.Props).Select(f => Path.GetFileName(f.Dir)).Order(StringComparer.Ordinal));

        Assert.Empty(PartsCatalog.Load(core.ContentOf(ContentTypes.Parts).ToList(), core).Parts);
        Assert.Single(setting.Folders(ContentTypes.Dealers));
        Assert.Equal("some_setting", setting.Setting?.Id);
        Assert.Equal("some_setting", setting.Game.Id);
        Assert.True(setting.Get("some_setting")!.IsSetting);
        Assert.Empty(setting.Problems);
    }

    [Fact]
    public void A_core_without_parts_has_no_mistake_in_its_jobs_for_the_shelves_and_the_parts_a_setting_brings()
    {
        _temp.File("core/rebuilds.json", """
            { "jobs": [ { "id": "reline", "groups": [ "brake_shoes" ] }, { "id": "nowhere", "groups": [ "no_such_shelf" ] },
                        { "id": "weld", "kinds": [ "differential" ], "becomes": true } ] }
            """);
        var problems = new List<string>();
        var book = PartRebuilds.Load([(ModIds.BaseId, true, _temp.Combine("core"))], problems);

        // A shelf of part_groups.json with no part on it is no mistake; one no file has is
        var unknown = book.UnknownNames(["differential"], PartGroups.Current.All.Select(g => g.Id));
        Assert.Equal(["nowhere"], unknown.Select(p => p.Contains("nowhere") ? "nowhere" : p));
        // With no parts at all a job that turns parts into others is waiting for them, not wrong
        book.CheckBecomes([], _ => null, problems);
        Assert.Empty(problems);
        // With parts but none of its kind (a setting with no spider gears) it is waiting for them too
        book.CheckBecomes([new PartDefinition { Id = "some/turbo" }], _ => null, problems, _ => "turbocharger");
        Assert.Empty(problems);
        // With parts of its kind and none naming it, it is never offered: told
        book.CheckBecomes([new PartDefinition { Id = "some/diff" }], _ => null, problems, _ => "differential");
        Assert.Contains(problems, p => p.Contains("weld"));
        problems.Clear();
        // Asked without the kinds, any part at all counts
        book.CheckBecomes([new PartDefinition { Id = "some/part" }], _ => null, problems);
        Assert.Contains(problems, p => p.Contains("weld"));
    }
}
