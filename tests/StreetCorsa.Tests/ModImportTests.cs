using System.IO.Compression;
using StreetCorsa.Mods;
using StreetCorsa.Screens.MainMenu;
using StreetCorsa.Services;

namespace StreetCorsa.Tests;

/// <summary>Install mods: zips read for their mods, laid into the Content folder's groups, replacing safely</summary>
public class ModImportTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Content => _temp.Combine("Content");

    /// <summary>A zip of these entries (path -> text), in the temp folder</summary>
    private string Zip(string name, params (string Path, string Text)[] entries)
    {
        var path = _temp.Combine("zips", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, text) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(text);
        }

        return path;
    }

    private void Installed(string folder, string manifest) => _temp.File($"Content/{folder}/mod.json", manifest);

    [Fact]
    public void A_packed_zip_goes_into_its_group_with_every_file()
    {
        Installed("streetcorsa", "{}");
        var zip = Zip("StreetCorsa-mysetting-0.1.0.zip",
            ("settings/mysetting/mod.json", """{ "id": "mysetting", "type": "setting", "version": "0.1.0" }"""),
            ("settings/mysetting/text/words.json", "{}"),
            ("engines/myengine/mod.json", """{ "id": "myengine", "settings": ["mysetting"] }"""));

        var plan = ModImport.Plan([zip], Content);
        Assert.Empty(plan.Refused);
        Assert.False(plan.ReplacesAny);
        var outcome = ModImport.Install(plan, Content);

        Assert.Equal(2, outcome.Installed.Count);
        Assert.Empty(outcome.Failed);
        Assert.True(File.Exists(Path.Combine(Content, "settings", "mysetting", "text", "words.json")));
        Assert.True(File.Exists(Path.Combine(Content, "engines", "myengine", "mod.json")));
        Assert.False(Directory.Exists(Path.Combine(Content, "_installing")));
        Assert.Contains(ModSet.Load(Content).Settings, s => s.Id == "mysetting");
    }

    [Fact]
    public void A_mod_without_a_group_goes_by_its_type_and_a_wrapper_folder_is_seen_through()
    {
        var bare = Zip("bare.zip", ("myaddon/mod.json", """{ "id": "myaddon" }"""));
        var root = Zip("root.zip", ("mod.json", """{ "id": "rootsetting", "type": "setting" }"""), ("readme.txt", "hi"));
        var wrapped = Zip("wrapped.zip", ("Download-1/settings/wrapped/mod.json", """{ "id": "wrapped", "type": "setting" }"""));

        var plan = ModImport.Plan([bare, root, wrapped], Content);

        Assert.Empty(plan.Refused);
        Assert.Equal(
            [Path.Combine(Content, "addons", "myaddon"), Path.Combine(Content, "settings", "rootsetting"), Path.Combine(Content, "settings", "wrapped")],
            plan.Mods.Select(m => m.Target));
        ModImport.Install(plan, Content);
        Assert.True(File.Exists(Path.Combine(Content, "settings", "rootsetting", "readme.txt")));
    }

    [Fact]
    public void Zips_that_hold_no_proper_mod_are_refused_and_nothing_is_written()
    {
        var notZip = _temp.File("zips/not.zip", "plain text");
        var noMod = Zip("nomod.zip", ("readme.txt", "hi"));
        var broken = Zip("broken.zip", ("broken/mod.json", "{ not json"));
        var core = Zip("core.zip", ("streetcorsa/mod.json", "{}"));
        var badId = Zip("badid.zip", ("x/mod.json", """{ "id": "Bad Id" }"""));
        var newer = Zip("newer.zip", ("future/mod.json", """{ "id": "future", "gameVersion": "99.0.0" }"""));
        var escape = Zip("escape.zip", ("evil/mod.json", """{ "id": "evil" }"""), ("evil/../../outside.txt", "x"));

        var plan = ModImport.Plan([notZip, noMod, broken, core, badId, newer, escape], Content);

        Assert.Empty(plan.Mods);
        Assert.Equal(
            [ImportRefusal.NotAZip, ImportRefusal.NoMod, ImportRefusal.Unreadable, ImportRefusal.Core, ImportRefusal.BadId, ImportRefusal.NeedsNewerGame, ImportRefusal.UnsafePath],
            plan.Refused.Select(r => r.Why));
        Assert.Equal("99.0.0", plan.Refused.Single(r => r.Why == ImportRefusal.NeedsNewerGame).Detail);
        ModImport.Install(plan, Content);
        Assert.False(File.Exists(_temp.Combine("outside.txt")));
        Assert.False(File.Exists(Path.Combine(Content, "outside.txt")));
    }

    [Theory]
    [InlineData("text/words.json", true)]
    [InlineData("a/b/", true)]
    [InlineData("../x", false)]
    [InlineData("a/../../x", false)]
    [InlineData("C:/Windows/x", false)]
    [InlineData("a/./x", false)]
    public void Only_paths_that_stay_in_the_mods_folder_are_safe(string path, bool safe) =>
        Assert.Equal(safe, ModImport.IsSafe(path));

    [Fact]
    public void An_installed_mod_of_the_same_id_is_replaced_wherever_it_is()
    {
        Installed("streetcorsa", "{}");
        Installed("addons/myengine", """{ "id": "myengine", "version": "0.2.0" }""");
        _temp.File("Content/addons/myengine/old.txt", "old");
        var zip = Zip("myengine.zip", ("engines/myengine/mod.json", """{ "id": "myengine", "version": "0.1.0" }"""));

        var plan = ModImport.Plan([zip], Content);
        var mod = Assert.Single(plan.Mods);
        Assert.True(plan.ReplacesAny);
        Assert.Equal([Path.Combine(Content, "addons", "myengine")], mod.Replaces);
        Assert.Equal("0.2.0", mod.ReplacesVersion);
        Assert.True(mod.IsOlder);

        var outcome = ModImport.Install(plan, Content);

        Assert.Single(outcome.Installed);
        Assert.False(Directory.Exists(Path.Combine(Content, "addons", "myengine")));
        Assert.True(File.Exists(Path.Combine(Content, "engines", "myengine", "mod.json")));
        Assert.False(Directory.Exists(Path.Combine(Content, "_replaced")));
        Assert.Equal(["myengine"], ModSet.Load(Content).Mods.Where(m => !m.IsBase).Select(m => m.Id));
    }

    [Fact]
    public void A_failed_move_puts_the_replaced_mod_back()
    {
        Installed("settings/mysetting", """{ "id": "mysetting", "type": "setting" }""");
        var zip = Zip("mysetting.zip", ("settings/mysetting/mod.json", """{ "id": "mysetting", "type": "setting" }"""));
        var plan = ModImport.Plan([zip], Content);
        // Where it goes is under a file: the new folder cannot go in once the old one is set aside
        _temp.File("Content/blocker", "a file where a folder must go");
        var blocked = plan with { Mods = [plan.Mods[0] with { Target = Path.Combine(Content, "blocker", "mysetting") }] };

        var outcome = ModImport.Install(blocked, Content);

        Assert.Empty(outcome.Installed);
        Assert.Single(outcome.Failed);
        Assert.True(File.Exists(Path.Combine(Content, "settings", "mysetting", "mod.json")));
    }

    [Fact]
    public void Two_picks_of_one_mod_install_it_once_and_what_it_requires_is_told()
    {
        var first = Zip("a.zip", ("addons/one/mod.json", """{ "id": "one", "requires": { "two": "", "streetcorsa": "" } }"""));
        var second = Zip("b.zip", ("addons/one/mod.json", """{ "id": "one" }"""));

        var plan = ModImport.Plan([first, second], Content);

        var mod = Assert.Single(plan.Mods);
        Assert.Equal(["two"], mod.Missing);
        Assert.Equal(ImportRefusal.Twice, Assert.Single(plan.Refused).Why);
    }

    [Fact]
    public void The_installed_folders_list_mods_only()
    {
        Installed("streetcorsa", "{}");
        Installed("settings/a", """{ "id": "aa", "version": "1.0.0" }""");
        Installed("settings/_off", """{ "id": "off" }""");
        _temp.File("Content/stray/readme.txt", "no mod");

        var found = ModSet.InstalledFolders(Content);

        Assert.Equal(["aa", "streetcorsa"], found.Select(f => f.Id).Order());
        Assert.Equal("1.0.0", found.Single(f => f.Id == "aa").Version);
    }

    [Fact]
    public void The_main_screen_warns_of_a_missing_or_old_csp_only()
    {
        var ac = _temp.Combine("ac");
        Directory.CreateDirectory(Path.Combine(ac, "content", "cars"));
        Directory.CreateDirectory(Path.Combine(ac, "content", "tracks"));
        Assert.Contains(CspVersion.MinimumName, MainMenuScreenViewModel.CspProblemOf(ac));

        _temp.File("ac/extension/config/data_manifest.ini", "[VERSION]\nSHADERS_PATCH=0.1.80-preview218\nSHADERS_PATCH_BUILD=2363\n");
        Assert.Contains("0.1.80-preview218", MainMenuScreenViewModel.CspProblemOf(ac));

        _temp.File("ac/extension/config/data_manifest.ini", $"[VERSION]\nSHADERS_PATCH={CspVersion.MinimumName}\nSHADERS_PATCH_BUILD={CspVersion.MinimumBuild}\n");
        Assert.Null(MainMenuScreenViewModel.CspProblemOf(ac));

        Assert.Null(MainMenuScreenViewModel.CspProblemOf(_temp.Combine("no_ac")));
        Assert.Null(MainMenuScreenViewModel.CspProblemOf(null));
    }
}
