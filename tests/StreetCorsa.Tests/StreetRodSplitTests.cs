using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// The street rods are a setting of their own (step 42, the split): the core holds what every game needs and none of
/// their content, a setting's ids go bare as the core's do (one setting plays at a time), an add-on finds a setting's
/// entry by its bare id, and a setting can take the core's parts off the shelves
/// </summary>
public class StreetRodSplitTests : IDisposable
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
    public void The_core_holds_none_of_the_street_rods_content_and_they_are_the_setting_played()
    {
        var core = ModSet.Load(RepoPaths.Content, coreOnly: true);
        var rods = RepoPaths.StreetRods;

        foreach (var type in new[] { ContentTypes.Dealers, ContentTypes.PartShops, ContentTypes.Racers, ContentTypes.Events, ContentTypes.Milestones,
                     ContentTypes.Victories, ContentTypes.Streets, ContentTypes.StreetKit, ContentTypes.Diners, ContentTypes.Stations, ContentTypes.Booths,
                     ContentTypes.Warehouses, ContentTypes.ShopScenes, ContentTypes.Props, ContentTypes.Cars })
        {
            Assert.Empty(core.Folders(type));
            Assert.NotEmpty(rods.Folders(type));
        }

        Assert.Empty(PartsCatalog.Load(core.ContentOf(ContentTypes.Parts).ToList(), core).Parts);
        Assert.Equal(RepoPaths.Setting, rods.Setting?.Id);
        Assert.Equal(RepoPaths.Setting, rods.Game.Id);
        Assert.True(rods.Get(RepoPaths.Setting)!.IsSetting);
        Assert.Empty(rods.Problems);
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

    [Fact]
    public void The_street_rods_say_their_own_names_and_attendant_over_the_cores()
    {
        var core = GameText.Load(ModSet.Load(RepoPaths.Content, coreOnly: true));
        var rods = GameText.Load(RepoPaths.StreetRods);

        Assert.DoesNotContain("Honest Abe Fenton", core.Lines("names.salesmen.men"));
        Assert.Contains("Honest Abe Fenton", rods.Lines("names.salesmen.men"));
        Assert.DoesNotContain(rods.Lines("names.salesmen.men"), core.Lines("names.salesmen.men").Contains);
        Assert.Contains("Afternoon! Fill 'er up?", rods.Lines("station.greeting"));
        Assert.DoesNotContain("Afternoon! Fill 'er up?", core.Lines("station.greeting"));
        Assert.Equal("The Diner", core.Lines("diner.title")[0]);
        Assert.Equal("The Main Street Diner", rods.Lines("diner.title")[0]);
        Assert.Equal("The Starlite", rods.Lines("diner.name.starlite")[0]);
        Assert.Empty(rods.Problems);
    }
}
