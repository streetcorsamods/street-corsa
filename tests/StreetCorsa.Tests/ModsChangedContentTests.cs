using StreetCorsa.Mods;
using StreetCorsa.Services.Content;

namespace StreetCorsa.Tests;

/// <summary>
/// A setting's content kept for the session is played again only while the same mods would play: engine packs
/// installed after a first game (the Mod Manager) must be in the next one
/// </summary>
public sealed class ModsChangedContentTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Root => _temp.Combine("Content");

    private ModSet Played() => ModSet.Load(Root, setting: "rods", exactSetting: true);

    [Fact]
    public void A_mod_installed_switched_off_or_updated_since_makes_the_content_read_anew()
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa", "version": "0.1.0" }""");
        _temp.File("Content/settings/rods/mod.json", """{ "id": "rods", "type": "setting", "version": "1.0" }""");
        var before = Played();
        Assert.True(ContentInPlay.SameMods(before, Played()));

        _temp.File("Content/engines/v8/mod.json", """{ "id": "v8", "version": "1.0" }""");
        var withEngine = Played();
        Assert.Contains(withEngine.Mods, m => m.Id == "v8");
        Assert.False(ContentInPlay.SameMods(before, withEngine));

        _temp.File("Content/engines/v8/mod.json", """{ "id": "v8", "version": "1.1" }""");
        Assert.False(ContentInPlay.SameMods(withEngine, Played()));

        Directory.Move(_temp.Combine("Content", "engines", "v8"), _temp.Combine("Content", "engines", "_v8"));
        Assert.True(ContentInPlay.SameMods(before, Played()));
    }
}
