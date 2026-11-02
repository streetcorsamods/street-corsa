using System.Reflection;
using StreetCorsa.Audio;
using StreetCorsa.Mods;

namespace StreetCorsa.Tests;

public class SoundCuesTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_mod_replaces_a_base_cue_of_the_same_name_and_adds_its_own()
    {
        _temp.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/sfx/ui/click.ogg", "x");
        _temp.File($"{ModIds.BaseId}/sfx/sfx.json", """
            { "cues": {
                "ui.click": { "bus": "interface", "volume": 0.5, "pitch": 0.04, "files": ["ui/click.ogg"] },
                "ambience.garage": { "bus": "Ambience", "loop": true, "files": ["ui/click.ogg"] } } }
            """);
        _temp.File("louder/mod.json", """{ "id": "louder" }""");
        _temp.File("louder/sfx/my_click.ogg", "x");
        _temp.File("louder/sfx/cues.json", """
            { "cues": {
                "ui.click": { "bus": "interface", "volume": 3, "files": ["my_click.ogg"] },
                "ui.horn": { "bus": "effects", "files": ["my_click.ogg"], "cooldown": 1.5 } } }
            """);

        var cues = SoundCues.Load(ModSet.Load(_temp.Path));

        Assert.Empty(cues.Problems);
        var click = cues.Get("UI.Click")!;
        Assert.EndsWith("my_click.ogg", click.Files.Single());
        Assert.Equal(2f, click.Volume); // held to twice as loud
        Assert.Equal(0f, click.Pitch);
        Assert.Equal(AudioBus.Ambience, cues.Get("ambience.garage")!.Bus);
        Assert.True(cues.Get("ambience.garage")!.Loop);
        Assert.Equal(1.5f, cues.Get("ui.horn")!.Cooldown);
    }

    [Fact]
    public void A_cue_without_a_bus_or_a_sound_is_left_out_and_said_why()
    {
        _temp.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/sfx/there.ogg", "x");
        _temp.File($"{ModIds.BaseId}/sfx/sfx.json", """
            { "cues": {
                "no.bus": { "bus": "kitchen", "files": ["there.ogg"] },
                "no.file": { "bus": "effects", "files": ["gone.ogg"] },
                "half": { "bus": "effects", "files": ["gone.ogg", "there.ogg"] } } }
            """);
        _temp.File($"{ModIds.BaseId}/sfx/broken.json", "{ not json");

        var cues = SoundCues.Load(ModSet.Load(_temp.Path));

        Assert.Null(cues.Get("no.bus"));
        Assert.Null(cues.Get("no.file"));
        Assert.Single(cues.Get("half")!.Files);
        Assert.Contains(cues.Problems, p => p.Contains("no.bus") && p.Contains("kitchen"));
        Assert.Contains(cues.Problems, p => p.Contains("gone.ogg"));
        Assert.Contains(cues.Problems, p => p.Contains("broken.json"));
    }

    [Fact]
    public void The_next_take_is_never_the_last_one()
    {
        var random = new Random(7);
        var last = -1;
        var seen = new HashSet<int>();
        for (var i = 0; i < 400; i++)
        {
            var next = SoundCues.NextFile(4, last, random);
            Assert.InRange(next, 0, 3);
            Assert.NotEqual(last, next);
            seen.Add(next);
            last = next;
        }

        Assert.Equal(4, seen.Count);
        Assert.Equal(0, SoundCues.NextFile(1, 0, random));
    }

    [Fact]
    public void The_pitch_wanders_only_as_far_as_the_cue_lets_it()
    {
        var random = new Random(3);
        for (var i = 0; i < 200; i++) Assert.InRange(SoundCues.Pitch(0.05f, random), 0.95f, 1.05f);
        Assert.Equal(1f, SoundCues.Pitch(0f, random));
    }

    [Theory]
    [InlineData("transmission", Sfx.Impact)]
    [InlineData("Flywheel", Sfx.Impact)]
    [InlineData("rim", Sfx.Impact)]
    [InlineData("rear_axle", Sfx.Impact)]
    [InlineData("cylinder_head", Sfx.Ratchet)]
    [InlineData("water_pump", Sfx.Ratchet)]
    [InlineData("drum", Sfx.Ratchet)]
    [InlineData(null, Sfx.Ratchet)]
    public void Heavy_drivetrain_bolts_and_lug_nuts_take_the_air_gun(string? kind, string cue) =>
        Assert.Equal(cue, Sfx.ToolFor(TestCars.Catalog.Value.WorkOrder.ToolOf(kind)));
}

/// <summary>The base game's own sound cues: every one the game plays is there, and plays something</summary>
public class SfxContentTests
{
    private static SoundCues Base()
    {
        var cues = new SoundCues();
        cues.Read(Path.Combine(RepoPaths.BaseContent, ContentTypes.Sfx, "sfx.json"), "sfx.json");
        return cues;
    }

    [Fact]
    public void Every_cue_reads_and_names_files_that_are_there()
    {
        var cues = Base();
        Assert.Empty(cues.Problems);
        Assert.NotEmpty(cues.All);
    }

    [Fact]
    public void Every_cue_the_cores_own_features_play_is_in_the_core_and_a_places_are_a_settings()
    {
        var cues = Base();
        var named = typeof(Sfx).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        // The menus, the garage's work, its lift and its engine hoist are the core's; the station's bell and pump come
        // with the setting that has a station
        Assert.Contains(named, n => n.StartsWith("station.", StringComparison.Ordinal));
        foreach (var name in named)
        {
            if (name.StartsWith("station.", StringComparison.Ordinal))
                Assert.True(cues.Get(name) == null, $"{name} is in the core's sfx.json, and is a place's (its setting's)");
            else
                Assert.True(cues.Get(name) is { } cue && cue.Loop == SoundCues.PlaysAsLoop(name),
                    $"{name} is not a {(SoundCues.PlaysAsLoop(name) ? "looping" : "one-shot")} cue in the core's sfx.json");
        }

        // The garage's air and the radio's sounds are the core's too
        foreach (var name in new[] { "ambience.garage", "ambience.warehouse", "radio.static", "radio.tune", "radio.knob" })
            Assert.True(cues.Get(name) is { } cue && cue.Loop == SoundCues.PlaysAsLoop(name), $"{name} is not in the core's sfx.json as it plays");
    }

    [Fact]
    public void A_cue_the_game_loops_given_as_a_one_shot_is_told_and_looped()
    {
        using var temp = new TempDir();
        temp.Bytes("sfx/pump.ogg", [1, 2, 3]);
        temp.Bytes("sfx/air.ogg", [1, 2, 3]);
        temp.File("sfx/sfx.json", """
            { "cues": {
                "station.pump": { "bus": "effects", "files": [ "pump.ogg" ] },
                "ambience.somewhere": { "bus": "ambience", "files": [ "air.ogg" ] },
                "station.bell": { "bus": "effects", "files": [ "pump.ogg" ] }
            } }
            """);
        var cues = new SoundCues();
        cues.Read(temp.Combine("sfx", "sfx.json"), "sfx.json");

        Assert.Equal(2, cues.Problems.Count);
        Assert.True(cues.Get("station.pump")!.Loop);
        Assert.True(cues.Get("ambience.somewhere")!.Loop);
        Assert.False(cues.Get("station.bell")!.Loop);
    }

    [Fact]
    public void Every_sound_file_is_credited()
    {
        var folder = Path.Combine(RepoPaths.BaseContent, ContentTypes.Sfx);
        var credited = SoundFiles.ParseCsv(File.ReadAllText(Path.Combine(folder, "credits.csv"))).Skip(1).Select(r => r[0]).ToHashSet();
        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => SoundFiles.Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'));

        foreach (var file in files) Assert.True(credited.Contains(file), $"{file} has no line in credits.csv");
    }

    [Fact]
    public void The_settings_name_the_theme_and_no_CC0_sound()
    {
        var named = SoundFiles.NamedCredits(ModSet.BaseOnly(RepoPaths.BaseContent));

        // Lame Genie's cover is credited with the band's permission and the tune's game; the rest is CC0 or the game's own
        var theme = Assert.Single(named);
        Assert.StartsWith("Street Rod II intro (cover), Lame Genie VGM (", theme);
        Assert.Contains("streetrodonline.com", theme);
    }

    [Fact]
    public void The_main_screens_badge_names_the_band_that_made_the_theme_and_links_their_site()
    {
        var mods = ModSet.BaseOnly(RepoPaths.BaseContent);

        var maker = StartMusic.Maker(mods, SoundCues.Load(mods));

        Assert.NotNull(maker);
        Assert.Equal("Lame Genie VGM", maker.Author);
        Assert.Equal(new Uri("https://www.lamegenie.com/"), maker.Link);
    }

    [Fact]
    public void A_mods_credit_links_only_to_the_web_never_to_a_file_or_a_program()
    {
        using var temp = new TempDir();
        temp.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", """{ "id": "streetcorsa" }""");
        temp.File($"{ModIds.BaseId}/sfx/music/song.ogg", "x");
        temp.File($"{ModIds.BaseId}/sfx/music/other.ogg", "x");
        temp.File($"{ModIds.BaseId}/sfx/music/third.ogg", "x");
        temp.File($"{ModIds.BaseId}/sfx/credits.csv", """
            file,title,author,license,source
            music/song.ogg,Song,Some Band,with permission,file:///C:/Windows/System32/calc.exe
            music/other.ogg,Other,Other Band,with permission,C:\Windows\notepad.exe
            music/third.ogg,Third,,CC0 1.0,https://example.com/
            """);
        var sfx = temp.Combine(ModIds.BaseId, "sfx");

        var song = SoundFiles.MakerOf(Path.Combine(sfx, "music", "song.ogg"), sfx);
        Assert.Equal("Some Band", song?.Author);
        Assert.Null(song?.Link);
        Assert.Null(SoundFiles.MakerOf(Path.Combine(sfx, "music", "other.ogg"), sfx)?.Link);
        Assert.Null(SoundFiles.MakerOf(Path.Combine(sfx, "music", "third.ogg"), sfx)); // nobody named
        Assert.Null(SoundFiles.MakerOf(Path.Combine(sfx, "music", "missing.ogg"), sfx));
    }

    [Fact]
    public void The_music_is_credited_only_from_its_mods_sfx_folder_and_a_bad_cell_names_nobody()
    {
        using var temp = new TempDir();
        temp.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", """{ "id": "streetcorsa" }""");
        temp.File($"{ModIds.BaseId}/sfx/music.json", """{ "cues": { "music.intro": { "bus": "radio", "loop": true, "files": ["music/song.ogg"] } } }""");
        temp.File($"{ModIds.BaseId}/sfx/music/song.ogg", "x");
        // Outside sfx: never read
        temp.File($"{ModIds.BaseId}/credits.csv", """
            file,title,author,license,source
            sfx/music/song.ogg,Song,Wrong Band,with permission,https://example.com/
            """);
        var mods = ModSet.BaseOnly(temp.Combine(ModIds.BaseId));

        Assert.Null(StartMusic.Maker(mods, SoundCues.Load(mods)));

        // A sibling folder whose name starts the same is not in the folder searched up to
        var sfx = temp.Combine(ModIds.BaseId, "sfx");
        temp.File($"{ModIds.BaseId}/sfx2/music/credits.csv", """
            file,title,author,license,source
            song.ogg,Song,Wrong Band,with permission,
            """);
        Assert.Null(SoundFiles.MakerOf(temp.Combine(ModIds.BaseId, "sfx2", "music", "song.ogg"), sfx));

        // A cell that is no path is passed over, not thrown
        temp.File($"{ModIds.BaseId}/sfx/credits.csv", "file,title,author,license,source\nmusic/so\0ng.ogg,Bad,Bad Band,,\nmusic/song.ogg,Song,Some Band,with permission,https://example.com/\n");
        Assert.Equal("Some Band", StartMusic.Maker(mods, SoundCues.Load(mods))?.Author);
    }

    [Fact]
    public void A_mods_sounds_by_name_are_credited_and_its_CC0_ones_not()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(temp.Combine("streetcorsa"));
        temp.File("streetcorsa/sfx/credits.csv", """
            file,title,author,license,source
            ui/a.ogg,Click,Someone,CC0 1.0,
            music/b.mp3,"Song, live",The Band,CC BY 4.0,https://example.com
            ui/c.ogg,Own,Street Corsa,the game's own,
            """);

        var named = SoundFiles.NamedCredits(ModSet.BaseOnly(temp.Combine("streetcorsa")));

        Assert.Equal(["Song, live, The Band (CC BY 4.0)"], named);
    }

    private static string SourceOf(Type type) =>
        Directory.EnumerateFiles(Path.Combine(RepoPaths.GameProject, "Screens"), type.Name + ".cs", SearchOption.AllDirectories).Single();
}
