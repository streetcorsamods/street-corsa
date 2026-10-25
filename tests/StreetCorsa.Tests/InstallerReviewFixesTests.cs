using StreetCorsa.Configuration;
using StreetCorsa.Mods;
using StreetCorsa.Services;
using StreetCorsa.Services.Garage;
using StreetCorsa.Services.Settings;

namespace StreetCorsa.Tests;

/// <summary>The installer review's fixes: the AC folder shared with the installer, the uninstall's modes, the fallback garage's props</summary>
[Collection(GlobalsCollection.Name)]
public sealed class InstallerReviewFixesTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _acBefore = AppSettings.Instance.AssettoCorsaPath;

    public void Dispose()
    {
        AppSettings.Instance.AssettoCorsaPath = _acBefore;
        _temp.Dispose();
    }

    private string Ac(string name)
    {
        _temp.File(Path.Combine(name, "content", "cars", "keep.txt"), "");
        _temp.File(Path.Combine(name, "content", "tracks", "keep.txt"), "");
        return _temp.Combine(name);
    }

    [Fact]
    public void A_folder_the_installer_sets_anew_wins_over_the_settings_and_only_once()
    {
        var old = Ac("old_ac");
        var moved = Ac("moved_ac");
        var settings = _temp.Combine("settings.json");
        File.WriteAllText(settings, Newtonsoft.Json.JsonConvert.SerializeObject(new GameSettings { AssettoCorsaPath = old }));

        // The installer run again with another folder picked: the game moves to it, and keeps it
        var service = new GameSettingsService(settings, null, () => null, () => moved);
        Assert.Equal(moved, service.Current.AssettoCorsaPath);
        Assert.Equal(moved, AppSettings.Instance.AssettoCorsaPath);
        Assert.Equal(moved, new GameSettingsService(settings, null, () => null).Current.AssettoCorsaPath);

        // The player sets the old one in the game's Settings: the installer's, seen before, no longer wins
        service.Current.AssettoCorsaPath = old;
        Assert.True(service.Save());
        Assert.Equal(old, new GameSettingsService(settings, null, () => null, () => moved).Current.AssettoCorsaPath);
    }

    [Fact]
    public void An_installer_folder_with_no_ac_in_it_leaves_the_settings_folder()
    {
        var own = Ac("own_ac");
        var settings = _temp.Combine("settings.json");
        File.WriteAllText(settings, Newtonsoft.Json.JsonConvert.SerializeObject(new GameSettings { AssettoCorsaPath = own }));

        var service = new GameSettingsService(settings, null, () => null, () => _temp.Combine("gone"));

        Assert.Equal(own, service.Current.AssettoCorsaPath);
    }

    [Fact]
    public void Uninstalling_takes_out_a_marked_mode_whose_mod_is_gone()
    {
        var ac = Ac("ac");
        var modes = Path.Combine(ac, "extension", "lua", "new-modes");
        _temp.File(Path.Combine("ac", "extension", "lua", "new-modes", "oldmod_derby", "mode.lua"), "-- a removed mod's format");
        _temp.File(Path.Combine("ac", "extension", "lua", "new-modes", "oldmod_derby", ScRaceMode.Marker), "");
        _temp.File(Path.Combine("ac", "extension", "lua", "new-modes", "drift-challenge", "mode.lua"), "-- CSP's own");

        var removed = new ScRaceMode(_temp.Combine("no_source")).Uninstall(ac, []);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(Path.Combine(modes, "oldmod_derby")));
        Assert.True(Directory.Exists(Path.Combine(modes, "drift-challenge")));
    }
}
