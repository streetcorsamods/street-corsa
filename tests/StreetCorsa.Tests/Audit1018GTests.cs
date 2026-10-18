using StreetCorsa.Configuration;
using StreetCorsa.Services;
using StreetCorsa.Services.Settings;
using StreetCorsa.Tools;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-18 audit's fixes, worker G: the uninstall's decision and exit codes, the OBJ reader's index checks
/// </summary>
public sealed class Audit1018GTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ----- the uninstall -----

    [Fact]
    public void With_ac_closed_everything_goes_back_and_the_modes_go_when_there_is_an_install()
    {
        var plan = AcUninstall.Decide(acClosed: true, acInstalled: true, hasLeftovers: () => throw new InvalidOperationException("not asked"));
        Assert.True(plan.RestoreRace);
        Assert.True(plan.RemoveModes);
        Assert.Equal(AcUninstall.ExitDone, plan.ExitCode);

        var noInstall = AcUninstall.Decide(acClosed: true, acInstalled: false, hasLeftovers: () => true);
        Assert.True(noInstall.RestoreRace);
        Assert.False(noInstall.RemoveModes);
        Assert.Equal(AcUninstall.ExitDone, noInstall.ExitCode);
    }

    [Fact]
    public void Ac_still_running_with_a_races_changes_in_it_puts_nothing_back_and_tells_the_installer()
    {
        var plan = AcUninstall.Decide(acClosed: false, acInstalled: true, hasLeftovers: () => true);
        Assert.False(plan.RestoreRace);
        Assert.False(plan.RemoveModes);
        Assert.Equal(AcUninstall.ExitAcRunning, plan.ExitCode);
        Assert.NotEqual(AcUninstall.ExitDone, AcUninstall.ExitAcRunning);
        Assert.NotEqual(AcUninstall.ExitFailed, AcUninstall.ExitAcRunning);

        // Whatever the folder holds: the race's manifests name their own folders in full
        Assert.Equal(AcUninstall.ExitAcRunning, AcUninstall.Decide(acClosed: false, acInstalled: false, hasLeftovers: () => true).ExitCode);
    }

    [Fact]
    public void Ac_still_running_with_nothing_waiting_takes_the_modes_out_and_is_done()
    {
        var plan = AcUninstall.Decide(acClosed: false, acInstalled: true, hasLeftovers: () => false);
        Assert.False(plan.RestoreRace);
        Assert.True(plan.RemoveModes);
        Assert.Equal(AcUninstall.ExitDone, plan.ExitCode);
    }

    // ----- the OBJ reader -----

    private string Obj(string faces) => _temp.File("model.obj", "v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 0 1\nvn 0 0 1\nusemtl paint\n" + faces);

    [Fact]
    public void A_face_corner_past_the_vertices_given_is_refused_with_its_line()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ObjModel.ReadObj(Obj("f 1/1/1 2/2/1 7/3/1\n"), 1f, 0f, mirror: false));
        Assert.Contains("line 9", ex.Message);
        Assert.Contains("vertex index 7", ex.Message);
        Assert.Contains("3 so far", ex.Message);
    }

    [Fact]
    public void A_texture_or_normal_index_out_of_range_names_what_it_is_and_a_zero_is_no_index()
    {
        var texture = Assert.Throws<InvalidDataException>(() => ObjModel.ReadObj(Obj("f 1/1/1 2/9/1 3/3/1\n"), 1f, 0f, mirror: false));
        Assert.Contains("texture coordinate index 9", texture.Message);

        var normal = Assert.Throws<InvalidDataException>(() => ObjModel.ReadObj(Obj("f 1/1/1 2/2/2 3/3/1\n"), 1f, 0f, mirror: false));
        Assert.Contains("normal index 2", normal.Message);

        var zero = Assert.Throws<InvalidDataException>(() => ObjModel.ReadObj(Obj("f 0/1/1 2/2/1 3/3/1\n"), 1f, 0f, mirror: false));
        Assert.Contains("line 9", zero.Message);
        Assert.Contains("no vertex index", zero.Message);
    }

    [Fact]
    public void A_negative_index_counts_back_from_the_last_vertex_given_as_obj_allows()
    {
        var groups = ObjModel.ReadObj(Obj("f -3/-3/-1 -2/-2/-1 -1/-1/-1\n"), 1f, 0f, mirror: false, recentre: false);

        var (vertices, indices) = Assert.Single(groups).Value;
        Assert.Equal([0, 1, 2], indices);
        Assert.Equal(3, vertices.Count);
        Assert.Equal(1f, vertices[1].Position.X);
        Assert.Equal(1f, vertices[2].Position.Y);
        // OBJ's V runs up, the texture's down
        Assert.Equal(0f, vertices[2].Tex.Y);
    }
}

/// <summary>The settings read for the uninstall: the AC folder worked out as at start-up, the file left as it was</summary>
[Collection(GlobalsCollection.Name)]
public sealed class Audit1018GSettingsTests : IDisposable
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
    public void A_read_only_load_takes_the_installers_folder_into_use_but_writes_nothing()
    {
        var old = Ac("old_ac");
        var moved = Ac("moved_ac");
        var settings = _temp.Combine("settings.json");
        var written = Newtonsoft.Json.JsonConvert.SerializeObject(new GameSettings { AssettoCorsaPath = old });
        File.WriteAllText(settings, written);
        var stamp = File.GetLastWriteTimeUtc(settings);

        var service = new GameSettingsService(settings, null, () => null, () => moved, writeBack: false);

        // The folder in use is the installer's, as the game would take it at its next start; the file is untouched
        Assert.Equal(moved, service.Current.AssettoCorsaPath);
        Assert.Equal(moved, AppSettings.Instance.AssettoCorsaPath);
        Assert.Equal(written, File.ReadAllText(settings));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(settings));
        Assert.Empty(Directory.GetFiles(_temp.Path, "*.tmp"));

        // The same load writing back keeps the installer's folder, as before
        Assert.Equal(moved, new GameSettingsService(settings, null, () => null, () => moved).Current.AssettoCorsaPath);
        Assert.Contains(moved.Replace(@"\", @"\\"), File.ReadAllText(settings));
    }

    [Fact]
    public void A_read_only_load_does_not_keep_a_found_steam_folder_either()
    {
        var steam = Ac("steam_ac");
        var settings = _temp.Combine("settings.json");
        File.WriteAllText(settings, "{}");

        _ = new GameSettingsService(settings, null, () => steam, () => null, writeBack: false);

        Assert.Equal("{}", File.ReadAllText(settings));
        // Found or the default: the one the uninstall works on either way, never a folder written into the file
        Assert.True(AppSettings.Instance.AssettoCorsaPath == steam || AppSettings.Instance.AssettoCorsaPath == AppSettings.DefaultAssettoCorsaPath);
    }
}
