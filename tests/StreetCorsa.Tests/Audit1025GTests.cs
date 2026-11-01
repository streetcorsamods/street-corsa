using StreetCorsa.Installer;
using StreetCorsa.Tools;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-25 audit's fixes, worker G: the installer's checks (the same file its custom actions run), the bench's
/// output guard, the OBJ reader's lines and manifests, a street kit's piece names
/// </summary>
public sealed class Audit1025GTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ----- the installer -----

    [Fact]
    public void The_uninstalls_clean_up_is_done_on_any_exit_code_but_acs_and_a_run_past_its_time_is_put_to_the_player()
    {
        Assert.Equal(CleanUp.Done, InstallerChecks.CleanUpOf(0));
        Assert.Equal(CleanUp.Done, InstallerChecks.CleanUpOf(1));
        Assert.Equal(CleanUp.Done, InstallerChecks.CleanUpOf(-1));
        Assert.Equal(CleanUp.AcRunning, InstallerChecks.CleanUpOf(InstallerChecks.ExitAcRunning));
        Assert.Equal(CleanUp.TimedOut, InstallerChecks.CleanUpOf(null));

        // The game's code and the installer's are one number
        Assert.Equal(Services.AcUninstall.ExitAcRunning, InstallerChecks.ExitAcRunning);
    }

    [Fact]
    public void The_games_ac_folder_is_read_out_of_its_settings_with_the_json_escapes_undone()
    {
        Assert.Equal(@"C:\GAMES\Assetto Corsa", InstallerChecks.SettingsFolderIn("{\"Audio\":{},\"AssettoCorsaPath\":\"C:\\\\GAMES\\\\Assetto Corsa\"}"));
        Assert.Equal("D:\\Jeux\\Caf\u00e9 \"AC\"", InstallerChecks.SettingsFolderIn("{\"AssettoCorsaPath\": \"D:\\\\Jeux\\\\Caf\\u00e9 \\\"AC\\\"\"}"));
        Assert.Null(InstallerChecks.SettingsFolderIn("{\"Audio\":{\"Volume\":1}}"));
        Assert.Equal("a\nb\tc/", InstallerChecks.JsonUnescape("a\\nb\\tc\\/"));
    }

    [Fact]
    public void Steams_library_folders_come_out_of_its_vdf_with_single_backslashes()
    {
        const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"PATH\"\t\t\"E:\\\\SteamLibrary\"\n\t}\n}\n";

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"E:\SteamLibrary"], InstallerChecks.LibraryFoldersIn(vdf).ToList());
        Assert.Empty(InstallerChecks.LibraryFoldersIn("not a vdf"));
    }

    [Fact]
    public void Ollama_is_found_in_its_own_folder_even_with_a_path_entry_no_path_can_be_and_on_the_path()
    {
        var own = Path.Combine(_temp.Path, "Programs", "Ollama", "ollama.exe");
        var onPath = Path.Combine(_temp.Path, "bin", "ollama.exe");
        bool Exists(string path) => path == own || path == onPath;

        // The own install is looked at before any PATH entry, so one the installer's .NET Framework cannot make a path
        // of (it throws on '<', '|' or '"' in the middle; .NET 10 makes a path of anything) never hides it
        Assert.True(InstallerChecks.OllamaFound(_temp.Path, "C:\\a<b;C:\\Windows", p => p == own ? true : throw new ArgumentException("asked first")));
        Assert.True(InstallerChecks.OllamaFound(null, $"C:\\Windows; \"{Path.GetDirectoryName(onPath)}\" ;", Exists));
        Assert.False(InstallerChecks.OllamaFound(null, ";;C:\\Windows", Exists));
        Assert.False(InstallerChecks.OllamaFound("", null, Exists));
    }

    [Fact]
    public void An_ac_install_has_both_content_folders()
    {
        Directory.CreateDirectory(_temp.Combine("ac", "content", "cars"));
        Assert.False(InstallerChecks.IsInstall(_temp.Combine("ac"), Directory.Exists));
        Directory.CreateDirectory(_temp.Combine("ac", "content", "tracks"));
        Assert.True(InstallerChecks.IsInstall(_temp.Combine("ac"), Directory.Exists));
    }

    // ----- EngineBench's output -----

    [Fact]
    public void The_bench_never_writes_into_the_car_it_read_nor_into_an_ac_cars_folder()
    {
        var car = _temp.Combine("cars_unpacked", "chevy");

        Assert.NotNull(BenchOutput.Refusal(car, car));
        Assert.NotNull(BenchOutput.Refusal(car + Path.DirectorySeparatorChar, car));
        Assert.NotNull(BenchOutput.Refusal(Path.Combine(car, "data"), car));
        Assert.NotNull(BenchOutput.Refusal(_temp.Combine("AC", "content", "cars", "other", "data"), car));
        Assert.NotNull(BenchOutput.Refusal(_temp.Combine("AC", "Content", "Cars"), car));

        Assert.Null(BenchOutput.Refusal(_temp.Combine("out"), car));
        Assert.Null(BenchOutput.Refusal(_temp.Combine("cars_unpacked", "chevy_out"), car));
        Assert.Null(BenchOutput.Refusal(_temp.Combine("content", "tracks"), car));
    }

    [Fact]
    public void The_bench_writes_each_file_whole_into_a_folder_it_makes()
    {
        var output = _temp.Combine("out", "new");
        BenchOutput.Write(output, [("engine.ini", "[HEADER]\nVERSION=1\n"), ("car.ini", "caf\u00e9")], System.Text.Encoding.Latin1);

        Assert.Equal("[HEADER]\nVERSION=1\n", File.ReadAllText(Path.Combine(output, "engine.ini")));
        Assert.Equal([0x63, 0x61, 0x66, 0xE9], File.ReadAllBytes(Path.Combine(output, "car.ini")));
        Assert.Equal(2, Directory.GetFiles(output).Length);
    }

    // ----- the OBJ reader -----

    [Fact]
    public void Obj_values_may_be_set_apart_by_tabs()
    {
        var obj = _temp.File("tabs.obj", "v\t0 0 0\nv\t1\t0\t0\nv 0\t1 0\nvt\t0 0\nvn 0\t0 1\nusemtl\tpaint\nf\t1/1/1 2/1/1\t3/1/1\n");

        var (vertices, indices) = Assert.Single(ObjModel.ReadObj(obj, 1f, 0f, mirror: false, recentre: false)).Value;
        Assert.Equal(3, vertices.Count);
        Assert.Equal([0, 1, 2], indices);
        Assert.Equal(1f, vertices[1].Position.X);
    }

    [Theory]
    [InlineData("v 0 0\n", "line 1", "too few")]
    [InlineData("v 0 0 0\nvt 0\n", "line 2", "texture coordinate")]
    [InlineData("vn 0 x 1\n", "line 1", "'x' is no normal value")]
    [InlineData("v 0 1e999 0\n", "line 1", "'1e999' is no vertex value")]
    public void A_short_or_bad_obj_line_is_refused_with_its_line(string text, string line, string what)
    {
        var ex = Assert.Throws<InvalidDataException>(() => ObjModel.ReadObj(_temp.File("bad.obj", text), 1f, 0f, mirror: false));
        Assert.Contains(line, ex.Message);
        Assert.Contains(what, ex.Message);
    }

    [Fact]
    public void An_obj_with_no_vertices_is_told_so_when_it_would_be_recentred()
    {
        var obj = _temp.File("empty.obj", "# nothing\no thing\n");

        var ex = Assert.Throws<InvalidDataException>(() => ObjModel.ReadObj(obj, 1f, 0f, mirror: false));
        Assert.Contains("no vertices", ex.Message);
        Assert.Contains(obj, ex.Message);
        Assert.Empty(ObjModel.ReadObj(obj, 1f, 0f, mirror: false, recentre: false));
    }

    [Fact]
    public void A_manifest_alias_to_a_material_it_lacks_or_without_its_lists_names_what_is_wrong()
    {
        var kn5 = AcTools.Kn5File.Kn5.CreateEmpty();
        var missing = _temp.File("missing.json", "{\"materials\": [], \"aliases\": {\"paint\": \"car_paint\", \"glass\": null}}");
        var ex = Assert.Throws<InvalidDataException>(() => ObjModel.AddMaterials(kn5, _temp.Path, missing));
        Assert.Contains("paint", ex.Message);
        Assert.Contains("car_paint", ex.Message);
        Assert.Contains(missing, ex.Message);

        var noAliases = _temp.File("no_aliases.json", "{\"materials\": []}");
        Assert.Contains("aliases", Assert.Throws<InvalidDataException>(() => ObjModel.AddMaterials(kn5, _temp.Path, noAliases)).Message);

        // A name to none (glass the prepare script left out) is no alias, as before
        Assert.Empty(ObjModel.AddMaterials(kn5, _temp.Path, _temp.File("none.json", "{\"materials\": [], \"aliases\": {\"glass\": null}}")));
    }

    // ----- a street kit's pieces -----

    private string Prepared(string pieceName, string aliases)
    {
        _temp.File(Path.Combine("prepared", "kit.obj"), $"o {pieceName}\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl paint\nf 1 2 3\n");
        _temp.File(Path.Combine("prepared", "materials.json"), "{\"materials\": [], \"aliases\": " + aliases + "}");
        return _temp.Combine("prepared");
    }

    [Theory]
    [InlineData(@"PIECE_..\evil")]
    [InlineData("PIECE_../evil")]
    [InlineData("PIECE_..")]
    [InlineData(@"PIECE_C:\evil")]
    public void A_piece_whose_name_is_no_plain_name_stops_the_kit_before_anything_is_written(string name)
    {
        var prepared = Prepared(name, "{\"paint\": \"paint\"}");
        var output = _temp.Combine("kit");

        var ex = Assert.Throws<InvalidDataException>(() => PreparedKit.Build(prepared, "kit", output, "lawn"));
        Assert.Contains("no plain name", ex.Message);
        Assert.False(Directory.Exists(output));
        Assert.False(Directory.Exists(_temp.Combine("evil")));
        Assert.Equal(["kit.obj", "materials.json"], Directory.GetFiles(prepared).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void A_piece_that_fails_leaves_no_scratch_manifest_behind()
    {
        // Its material's alias names a material the manifest lacks: the piece fails in the middle of the build
        var prepared = Prepared("PIECE_house", "{\"paint\": \"car_paint\"}");

        Assert.Throws<InvalidDataException>(() => PreparedKit.Build(prepared, "kit", _temp.Combine("kit"), "lawn"));
        Assert.Equal(["kit.obj", "materials.json"], Directory.GetFiles(prepared).Select(Path.GetFileName).Order());
    }

    [Theory]
    [InlineData("house_a", true)]
    [InlineData("porch 2", true)]
    [InlineData("", false)]
    [InlineData(" house", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData(@"a\b", false)]
    [InlineData("a/b", false)]
    [InlineData("C:", false)]
    [InlineData("a|b", false)]
    public void A_piece_id_is_one_plain_file_name(string id, bool plain) => Assert.Equal(plain, PreparedKit.IsPlainName(id));
}
