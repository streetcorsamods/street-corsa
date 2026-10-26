using System.Text.RegularExpressions;
using StreetCorsa.Services;

namespace StreetCorsa.Tests;

/// <summary>ScRaceMode on a fake AC install and a fake build folder in a temp folder</summary>
public sealed class ScRaceModeTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _source;
    private readonly string _ac;

    public ScRaceModeTests()
    {
        _source = _temp.Combine("build", "AcModes", "sc_race");
        _ac = _temp.Combine("ac");
        _temp.File(Path.Combine("build", "AcModes", "sc_race", "mode.lua"), "-- mode");
        _temp.File(Path.Combine("build", "AcModes", "sc_race", "manifest.ini"), "[ABOUT]");
        Directory.CreateDirectory(Path.Combine(_ac, "extension"));
    }

    public void Dispose() => _temp.Dispose();

    private string Installed(string name) => Path.Combine(ScRaceMode.InstalledFolder(_ac), name);

    [Fact]
    public void The_mode_goes_into_new_modes_as_the_build_has_it()
    {
        Assert.Null(new ScRaceMode(_source).Install(_ac));

        Assert.Equal("-- mode", File.ReadAllText(Installed("mode.lua")));
        Assert.Equal("[ABOUT]", File.ReadAllText(Installed("manifest.ini")));
        Assert.EndsWith(Path.Combine("extension", "lua", "new-modes", "sc_race"), ScRaceMode.InstalledFolder(_ac));
    }

    [Fact]
    public void An_edited_or_stale_file_in_the_install_is_put_right()
    {
        Directory.CreateDirectory(ScRaceMode.InstalledFolder(_ac));
        File.WriteAllText(Installed("mode.lua"), "-- edited in the install");
        File.WriteAllText(Installed("old.lua"), "-- from an older build");

        Assert.Null(new ScRaceMode(_source).Install(_ac));

        Assert.Equal("-- mode", File.ReadAllText(Installed("mode.lua")));
        Assert.False(File.Exists(Installed("old.lua")));
    }

    [Fact]
    public void The_old_race_manager_app_is_removed_so_it_cannot_report_the_race_twice()
    {
        _temp.File(Path.Combine("ac", "apps", "lua", "sr_race_manager", "sr_race_manager.lua"), "-- 2.1.0");
        _temp.File(Path.Combine("ac", "apps", "lua", "other_app", "other.lua"), "-- someone else's");

        Assert.Null(new ScRaceMode(_source).Install(_ac));

        Assert.False(Directory.Exists(Path.Combine(_ac, "apps", "lua", "sr_race_manager")));
        Assert.True(File.Exists(Path.Combine(_ac, "apps", "lua", "other_app", "other.lua")));
    }

    [Fact]
    public void Uninstalling_takes_the_games_modes_out_and_leaves_the_others()
    {
        Assert.Null(new ScRaceMode(_source).Install(_ac));
        _temp.File(Path.Combine("ac", "extension", "lua", "new-modes", "sc_free", "mode.lua"), "-- free run");
        _temp.File(Path.Combine("ac", "extension", "lua", "new-modes", "mymod_derby", "mode.lua"), "-- a mod's format");
        _temp.File(Path.Combine("ac", "extension", "lua", "new-modes", "mymod_derby", ScRaceMode.Marker), "put here by the game");
        _temp.File(Path.Combine("ac", "extension", "lua", "new-modes", "drift-challenge", "mode.lua"), "-- CSP's own");

        var removed = new ScRaceMode(_source).Uninstall(_ac, ["sc_race", "mymod_derby"]);

        Assert.Equal(3, removed);
        var modes = Path.Combine(_ac, "extension", "lua", "new-modes");
        Assert.False(Directory.Exists(Path.Combine(modes, "sc_race")));
        Assert.False(Directory.Exists(Path.Combine(modes, "sc_free")));
        Assert.False(Directory.Exists(Path.Combine(modes, "mymod_derby")));
        Assert.True(File.Exists(Path.Combine(modes, "drift-challenge", "mode.lua")));
    }

    [Fact]
    public void The_mode_under_its_old_id_is_removed()
    {
        _temp.File(Path.Combine("ac", "extension", "lua", "new-modes", "sr_race", "mode.lua"), "-- 3.7.0");

        Assert.Null(new ScRaceMode(_source).Install(_ac));

        Assert.False(Directory.Exists(Path.Combine(_ac, "extension", "lua", "new-modes", "sr_race")));
        Assert.True(File.Exists(Path.Combine(ScRaceMode.InstalledFolder(_ac), "mode.lua")));
    }

    [Fact]
    public void Without_CSP_the_race_cannot_run()
    {
        Directory.Delete(Path.Combine(_ac, "extension"));

        var problem = new ScRaceMode(_source).Install(_ac);

        Assert.Contains("Custom Shaders Patch", problem);
        Assert.False(Directory.Exists(ScRaceMode.InstalledFolder(_ac)));
    }

    [Fact]
    public void Without_the_mode_in_the_build_the_race_cannot_run()
    {
        var problem = new ScRaceMode(_temp.Combine("nowhere")).Install(_ac);

        Assert.NotNull(problem);
        Assert.False(Directory.Exists(ScRaceMode.InstalledFolder(_ac)));
    }

    [Fact]
    public void The_manifest_version_is_the_scripts_version()
    {
        // In step by a comment only, and they drifted before: CSP shows the manifest's, the result file carries the script's
        var mode = Path.Combine(RepoPaths.Root, "apps", "new-modes", "sc_race");
        var manifest = Regex.Match(File.ReadAllText(Path.Combine(mode, "manifest.ini")), @"^\s*VERSION\s*=\s*(\S+)\s*$", RegexOptions.Multiline);
        var script = Regex.Match(File.ReadAllText(Path.Combine(mode, "mode.lua")), @"^local SCRIPT_VERSION = ""([^""]+)""", RegexOptions.Multiline);

        Assert.True(manifest.Success, "no VERSION in manifest.ini");
        Assert.True(script.Success, "no SCRIPT_VERSION in mode.lua");
        Assert.Equal(script.Groups[1].Value, manifest.Groups[1].Value);
    }
}
