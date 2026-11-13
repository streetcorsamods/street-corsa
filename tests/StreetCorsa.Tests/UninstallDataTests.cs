using StreetCorsa.Installer;

namespace StreetCorsa.Tests;

/// <summary>
/// The uninstall's clean-up of what the game made (installer/Actions/InstallerChecks.cs, run by RemoveGameData): the
/// saves and settings in %AppData%, the mods in the install folder's Content, never a folder not certainly the game's
/// </summary>
public sealed class UninstallDataTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Content_goes_only_from_a_folder_that_holds_the_core_and_never_from_a_drives_root()
    {
        var install = _temp.Combine("Street Corsa");
        Assert.Null(InstallerChecks.OwnContentFolder(install, File.Exists));

        _temp.File(@"Street Corsa\Content\streetcorsa\game.json", "{}");
        Assert.Equal(Path.Combine(install, "Content"), InstallerChecks.OwnContentFolder(install, File.Exists));
        Assert.Equal(Path.Combine(install, "Content"), InstallerChecks.OwnContentFolder(install + "\\", File.Exists));

        Assert.Null(InstallerChecks.OwnContentFolder(@"C:\", _ => true));
        Assert.Null(InstallerChecks.OwnContentFolder("", _ => true));
        Assert.Null(InstallerChecks.OwnContentFolder(null, _ => true));
    }

    [Fact]
    public void The_saves_the_mods_and_the_emptied_install_folder_go()
    {
        var data = _temp.Combine("AppData", "StreetCorsa");
        _temp.File(@"AppData\StreetCorsa\settings.json", "{}");
        _temp.File(@"AppData\StreetCorsa\Saves\one.db", "save");
        // An AcRestore with nothing waiting in it goes with the rest
        Directory.CreateDirectory(_temp.Combine("AppData", "StreetCorsa", "AcRestore", "cars"));
        var readOnly = _temp.File(@"Street Corsa\Content\settings\streetrod\mod.json", "{}");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var install = _temp.Combine("Street Corsa");

        var lines = new List<string>();
        var failed = InstallerChecks.RemoveGameData(data, Path.Combine(install, "Content"), install, lines.Add);

        Assert.Equal(0, failed);
        Assert.False(Directory.Exists(data));
        Assert.False(Directory.Exists(install));
        Assert.True(Directory.Exists(_temp.Combine("AppData")));
    }

    [Fact]
    public void A_races_originals_waiting_in_ac_restore_are_kept_and_the_rest_goes()
    {
        var data = _temp.Combine("AppData", "StreetCorsa");
        _temp.File(@"AppData\StreetCorsa\settings.json", "{}");
        var original = _temp.File(@"AppData\StreetCorsa\AcRestore\cars\some_car\data.acd", "original");

        var lines = new List<string>();
        InstallerChecks.RemoveGameData(data, null, null, lines.Add);

        Assert.True(File.Exists(original));
        Assert.False(File.Exists(Path.Combine(data, "settings.json")));
        Assert.Contains(lines, l => l.Contains(InstallerChecks.AcRestoreName));
    }

    [Fact]
    public void An_install_folder_with_something_else_left_in_it_stays()
    {
        var install = _temp.Combine("Games");
        _temp.File(@"Games\Content\streetcorsa\game.json", "{}");
        var other = _temp.File(@"Games\other game.exe", "x");

        InstallerChecks.RemoveGameData(null, Path.Combine(install, "Content"), install, _ => { });

        Assert.True(File.Exists(other));
        Assert.False(Directory.Exists(Path.Combine(install, "Content")));
    }

    [Fact]
    public void A_content_folder_shared_with_another_app_keeps_what_is_not_the_games()
    {
        var install = _temp.Combine("Games");
        _temp.File(@"Games\Content\streetcorsa\game.json", "{}");
        _temp.File(@"Games\Content\engines\v8\mod.json", "{}");
        _temp.File(@"Games\Content\_hand_mod\mod.json", "{}");
        _temp.File(@"Games\Content\pack\inside\mod.json", "{}");
        var theirs = _temp.File(@"Games\Content\OtherGame\levels\one.pak", "x");
        var loose = _temp.File(@"Games\Content\readme.txt", "x");

        var failed = InstallerChecks.RemoveGameData(null, Path.Combine(install, "Content"), install, _ => { });

        Assert.Equal(0, failed);
        Assert.True(File.Exists(theirs));
        Assert.True(File.Exists(loose));
        foreach (var gone in new[] { "streetcorsa", "engines", "_hand_mod", "pack" })
            Assert.False(Directory.Exists(Path.Combine(install, "Content", gone)), gone);
    }

    [Fact]
    public void What_the_uninstall_could_not_do_is_read_once_for_the_finish_page()
    {
        var file = InstallerChecks.OutcomeFile(_temp.Combine(), "{0A1B2C3D-0000-0000-0000-000000000000}");
        Assert.Empty(InstallerChecks.TakeOutcome(file));

        InstallerChecks.Record(file, InstallerChecks.AcLeft, _ => { });
        InstallerChecks.Record(file, InstallerChecks.DataLeft, _ => { });

        Assert.Equal(new[] { InstallerChecks.AcLeft, InstallerChecks.DataLeft }.OrderBy(s => s), InstallerChecks.TakeOutcome(file).OrderBy(s => s));
        Assert.False(File.Exists(file));
        Assert.Empty(InstallerChecks.TakeOutcome(file));
    }

    [Fact]
    public void A_junction_among_the_mods_goes_without_what_it_points_at()
    {
        var target = _temp.File(@"Elsewhere\mod\mod.json", "{}");
        var link = _temp.Combine("Street Corsa", "Content", "settings", "linked");
        Junction.Make(link, Path.GetDirectoryName(target)!);

        var failed = InstallerChecks.RemoveGameData(null, _temp.Combine("Street Corsa", "Content"), null, _ => { });

        Assert.Equal(0, failed);
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(target));
    }
}
