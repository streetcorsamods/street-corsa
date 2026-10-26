using System.Diagnostics;
using System.Numerics;
using AcTools.Kn5File;
using StreetCorsa.Audio;
using StreetCorsa.Audio.Radio;
using StreetCorsa.Looks;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Rules;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-18 audit's fixes in the sound (a cue's bus, the radio's folders and files, its race keys) and the looks
/// (the kept shell, the texel reader), and the condition rule the parts' "undamaged" line became
/// </summary>
public sealed class Audit1018BTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ----- SoundCues: a bus is one of the buses, never a number or the engines' -----

    [Fact]
    public void A_cue_on_a_bus_that_is_a_number_or_the_engines_is_refused_and_a_null_cue_is_skipped()
    {
        _temp.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/sfx/click.ogg", "x");
        _temp.File($"{ModIds.BaseId}/sfx/sfx.json", """
            { "cues": {
                "seven": { "bus": "7", "files": ["click.ogg"] },
                "minus": { "bus": "-1", "files": ["click.ogg"] },
                "engine": { "bus": "engine", "files": ["click.ogg"] },
                "capital": { "bus": "Engine", "files": ["click.ogg"] },
                "nothing": null,
                "good": { "bus": "Interface", "files": ["click.ogg"] } } }
            """);

        var cues = SoundCues.Load(ModSet.Load(_temp.Path));

        foreach (var name in new[] { "seven", "minus", "engine", "capital", "nothing" })
        {
            Assert.Null(cues.Get(name));
            Assert.Contains(cues.Problems, p => p.Contains(name));
        }
        Assert.Equal(AudioBus.Interface, cues.Get("good")!.Bus);
        Assert.Equal(5, cues.Problems.Count); // the five refused, nothing else wrong

        Assert.Null(SoundCues.BusOf("7"));
        Assert.Null(SoundCues.BusOf("-1"));
        Assert.Null(SoundCues.BusOf("engine"));
        Assert.Null(SoundCues.BusOf(null));
        Assert.Equal(AudioBus.Ambience, SoundCues.BusOf("ambience"));
        Assert.DoesNotContain(AudioBus.Engine, SoundCues.CueBuses);
    }

    // ----- ShellFile: a kept shell whose panels or indices point outside what it holds is no file -----

    private static BodyShell Doctored(BodyShell shell, IReadOnlyList<BodyShell.Panel>? panels = null, IReadOnlyList<BodyShell.Mesh>? body = null) => new()
    {
        Min = shell.Min, Max = shell.Max, FrontSign = shell.FrontSign, LeftSign = shell.LeftSign, Hubs = shell.Hubs, PictureSize = shell.PictureSize,
        Panels = panels ?? shell.Panels, BodyMeshes = body ?? shell.BodyMeshes, ScreenMeshes = shell.ScreenMeshes, ScreenMin = shell.ScreenMin,
        ScreenMax = shell.ScreenMax, Ambient = shell.Ambient, Diffuse = shell.Diffuse, Paint = shell.Paint
    };

    [Fact]
    public void A_kept_shell_with_a_panel_outside_its_picture_or_an_index_outside_its_mesh_is_no_file()
    {
        var shell = BodyShell.Of(TestCars.BoxCar(), 512)!;
        var path = _temp.Combine("box.shell");
        var panel = shell.Panels[0];

        // As it is, it reads
        ShellFile.Write(path, shell);
        Assert.True(ShellFile.TryRead(path, out _));

        // A side that is no side; a panel starting before the picture; one reaching past it
        foreach (var bad in new[]
                 {
                     panel with { Side = (BodyShell.Side)9 },
                     panel with { X = -1 },
                     panel with { Y = -3 },
                     panel with { X = shell.PictureSize - panel.Width + 1 },
                     panel with { Height = shell.PictureSize - panel.Y + 1 },
                     panel with { Width = -1 }
                 })
        {
            ShellFile.Write(path, Doctored(shell, panels: [bad, .. shell.Panels.Skip(1)]));
            Assert.False(ShellFile.TryRead(path, out var none));
            Assert.Null(none);
        }

        // An index one past the mesh's corners
        var mesh = shell.BodyMeshes[0];
        var indices = (ushort[])mesh.Indices.Clone();
        indices[^1] = (ushort)mesh.Vertices.Length;
        ShellFile.Write(path, Doctored(shell, body: [mesh with { Indices = indices }, .. shell.BodyMeshes.Skip(1)]));
        Assert.False(ShellFile.TryRead(path, out _));

        // The same shell, sound, still reads: the checks refuse only what is wrong
        ShellFile.Write(path, Doctored(shell));
        Assert.True(ShellFile.TryRead(path, out var back));
        Assert.Equal(shell.Panels, back!.Panels);
    }

    [Fact]
    public void An_animation_edited_in_place_changes_the_shells_stamp()
    {
        var car = _temp.Combine("car");
        var animation = _temp.File(Path.Combine("car", "animations", "car_door_L.ksanim"), "aaaa");
        var before = BodyShell.MoversStamp(car);

        File.SetLastWriteTimeUtc(animation, File.GetLastWriteTimeUtc(animation).AddMinutes(1));

        Assert.NotEqual(before, BodyShell.MoversStamp(car));
        Assert.Equal("", BodyShell.MoversStamp(_temp.Combine("no_car")));
    }

    // ----- TexelReader: a strip of a texture never comes out a side of nought -----

    [Theory]
    [InlineData(1, 4096, 256, 1, 256)]
    [InlineData(8192, 64, 256, 256, 2)]
    [InlineData(1024, 1024, 256, 256, 256)]
    [InlineData(300, 100, null, 300, 100)]
    [InlineData(100, 50, 256, 256, 128)]
    public void A_texture_drawn_at_most_a_side_keeps_its_shape_and_at_least_a_texel(int width, int height, int? most, int w, int h) =>
        Assert.Equal((w, h), TexelReader.SizeOf(width, height, most));

    // ----- Radio: the time written one way, a drive's root refused, the list cut short, the files read as before -----

    [Fact]
    public void The_radio_writes_a_time_one_way()
    {
        Assert.Equal("0:00", RadioTime.Format(0));
        Assert.Equal("2:10", RadioTime.Format(130_000));
        Assert.Equal("1:02:30", RadioTime.Format(3_750_000));
        Assert.Equal("", new RadioSong("a", "A", null, null, 0).LengthText);
        Assert.Equal("2:10", new RadioSong("a", "A", null, null, 130_000).LengthText);
    }

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"D:\", true)]
    [InlineData(@"C:\Music", false)]
    [InlineData(@"C:\Music\", false)]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server\share\", true)]
    [InlineData(@"\\server\share\music", false)]
    public void A_whole_drive_or_share_is_no_music_folder(string folder, bool root) => Assert.Equal(root, RadioLibrary.IsRoot(folder));

    [Fact]
    public void A_scan_stops_at_the_most_songs_and_says_so()
    {
        for (var i = 0; i < 6; i++) _temp.File($"m/song{i}.mp3", "x");
        var folders = new[] { _temp.Combine("m") };

        var capped = RadioLibrary.Scan(folders, _temp.Combine("c.json"), _ => new RadioLibrary.SongRead(null, true), Ct, mostSongs: 4);
        Assert.True(capped.Capped);
        Assert.Equal(4, capped.Songs.Count);

        var whole = RadioLibrary.Scan(folders, _temp.Combine("c.json"), _ => new RadioLibrary.SongRead(null, true), Ct);
        Assert.False(whole.Capped);
        Assert.Equal(6, whole.Songs.Count);
    }

    [Fact]
    public void The_radios_settings_written_by_the_older_game_read_and_write_back_the_same()
    {
        // radio.json as Newtonsoft wrote it: the names, the repeat as a word, a path with its backslashes
        var old = string.Join(Environment.NewLine,
            "{",
            "  \"folders\": [",
            "    \"D:\\\\Music\",",
            "    \"E:\\\\Surf Café\"",
            "  ],",
            "  \"playlists\": [",
            "    {",
            "      \"name\": \"Cruising\",",
            "      \"songs\": [",
            "        \"D:\\\\Music\\\\a.mp3\"",
            "      ]",
            "    }",
            "  ],",
            "  \"shuffle\": false,",
            "  \"repeat\": \"One\",",
            "  \"on\": true,",
            "  \"list\": \"Cruising\",",
            "  \"song\": \"D:\\\\Music\\\\a.mp3\",",
            "  \"position_ms\": 61000",
            "}");
        var path = _temp.File(RadioState.FileName, old);

        var state = RadioState.Load(path);
        Assert.Equal([@"D:\Music", @"E:\Surf Café"], state.Folders);
        Assert.Equal(@"D:\Music\a.mp3", state.Playlists.Single().Songs.Single());
        Assert.False(state.Shuffle);
        Assert.Equal(RadioRepeat.One, state.Repeat);
        Assert.True(state.WasOn);
        Assert.Equal("Cruising", state.List);
        Assert.Equal(61_000u, state.PositionMs);

        state.Save(path);
        Assert.Equal(old, File.ReadAllText(path));

        // Names whatever their case, and a word that is no repeat is a bad file (as it was)
        Assert.Equal(RadioRepeat.Off, RadioState.Load(_temp.File("r2.json", """{ "Repeat": "off", "Folders": ["D:\\M"] }""")).Repeat);
        Assert.ThrowsAny<Exception>(() => RadioState.Load(_temp.File("r3.json", """{ "repeat": "sideways" }""")));
    }

    [Fact]
    public void The_tags_cache_written_by_the_older_game_is_read_as_it_is()
    {
        var song = _temp.File(Path.Combine("m", "Misirlou.mp3"), "xxxxx");
        var info = new FileInfo(song);
        // radio_songs.json as Newtonsoft wrote it, one line, the path a key
        var old = "{\"" + song.Replace("\\", "\\\\") + "\":{\"v\":2,\"size\":" + info.Length + ",\"written\":" + info.LastWriteTimeUtc.Ticks
                  + ",\"title\":\"Misirlou\",\"artist\":\"Dick Dale\",\"album\":null,\"length_ms\":130000}}";
        var cache = _temp.File("radio_songs.json", old);

        var reads = 0;
        var result = RadioLibrary.Scan([_temp.Combine("m")], cache, _ =>
        {
            reads++;
            return new RadioLibrary.SongRead(null, true);
        }, Ct);

        Assert.Equal(0, reads);
        var found = Assert.Single(result.Songs);
        Assert.Equal("Misirlou", found.Title);
        Assert.Equal("Dick Dale", found.Artist);
        Assert.Equal(130_000u, found.LengthMs);
        Assert.Equal(old, File.ReadAllText(cache));
    }

    // ----- RaceKeys: the stop and the hook's thread meet whichever comes first; a process named without a snapshot -----

    [Fact]
    public void A_stop_before_the_hooks_thread_has_arrived_turns_it_back_and_one_after_is_posted_to_it()
    {
        var keys = new RaceKeys(_ => { });

        // Stopped before the thread said where it is: nothing to post to, the thread turns back on arrival
        var early = new RaceKeys.HookThread(keys);
        var posted = new List<uint>();
        Assert.False(early.AskStop(id => { posted.Add(id); return true; }));
        Assert.Empty(posted);
        Assert.False(early.Arrived(42));
        Assert.True(early.StopAsked);

        // Arrived first: the stop goes to its thread
        var late = new RaceKeys.HookThread(keys);
        Assert.True(late.Arrived(42));
        Assert.True(late.AskStop(id => { posted.Add(id); return true; }));
        Assert.Equal([42u], posted);

        // A post that fails is said so, not taken for a stop
        var failing = new RaceKeys.HookThread(keys);
        Assert.True(failing.Arrived(7));
        Assert.False(failing.AskStop(_ => false));

        // Nothing started: nothing to stop
        keys.Stop();
        Assert.False(keys.IsListening);
    }

    [Fact]
    public void A_process_is_named_by_its_image_without_a_snapshot_of_every_process()
    {
        using var own = Process.GetCurrentProcess();
        Assert.Equal(own.ProcessName, RaceKeys.NameOf((uint)own.Id), ignoreCase: true);

        // The idle process (0) is nobody's to open: not the game
        Assert.Null(RaceKeys.NameOf(0));
    }

    // ----- Rules: what counts as undamaged is the condition rules' figure -----

    [Fact]
    public void What_counts_as_undamaged_is_a_condition_rule_with_the_parts_figure_as_its_default()
    {
        var rules = new ConditionRules();
        Assert.Equal(PartInstance.Undamaged, rules.UndamagedTear);

        var problems = new List<string>();
        rules.UndamagedTear = 1.5;
        rules.Check("condition", problems);
        Assert.Contains(problems, p => p.Contains("undamagedTear", StringComparison.OrdinalIgnoreCase));
    }
}
