using System.Diagnostics;
using StreetCorsa.Audio;
using StreetCorsa.Audio.Radio;
using StreetCorsa.Looks;
using StreetCorsa.Mods;

namespace StreetCorsa.Tests;

/// <summary>The 2026-10-05 audit's fixes in the sound (the radio, its race keys, the cues) and the paint</summary>
public class Audit1005CTests : IDisposable
{
    private const int KeyDown = 0x0100;
    private const int KeyUp = 0x0101;
    private const int TestKey = 0xFF;

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ----- RaceKeys: only while AC's window is in front -----

    [Fact]
    public void The_race_keys_act_only_while_ACs_window_is_in_front()
    {
        const uint ac = 4242, chat = 77;
        var inFront = ac;
        var posted = new List<Action>();
        var keys = new RaceKeys(posted.Add)
        {
            Map = key => key == TestKey ? () => { } : null,
            ProcessInFront = () => inFront,
            ProcessName = process => process switch { ac => "acs", chat => "Discord", _ => "Street Corsa" }
        };

        Assert.True(keys.Heard(TestKey, KeyDown));
        keys.Heard(TestKey, KeyUp);

        // Typing into another program, or into the game's own window: not the radio's key
        inFront = chat;
        Assert.False(keys.Heard(TestKey, KeyDown));
        keys.Heard(TestKey, KeyUp);
        inFront = (uint)Environment.ProcessId;
        Assert.False(keys.Heard(TestKey, KeyDown));
        keys.Heard(TestKey, KeyUp);

        // Nobody's window, and a key that is not the radio's
        inFront = 0;
        Assert.False(keys.Heard(TestKey, KeyDown));
        keys.Heard(TestKey, KeyUp);
        inFront = ac;
        Assert.False(keys.Heard(0x41, KeyDown));

        Assert.Single(posted);
    }

    [Fact]
    public void A_race_key_held_down_acts_once()
    {
        var posted = new List<Action>();
        var keys = new RaceKeys(posted.Add)
        {
            Map = key => key == TestKey ? () => { } : null,
            ProcessInFront = () => 1,
            ProcessName = _ => "ACS"
        };

        Assert.True(keys.Heard(TestKey, KeyDown));
        Assert.False(keys.Heard(TestKey, KeyDown));
        Assert.False(keys.Heard(TestKey, KeyDown));
        keys.Heard(TestKey, KeyUp);
        Assert.True(keys.Heard(TestKey, KeyDown));
        Assert.Equal(2, posted.Count);
    }

    // ----- RadioLibrary: a song nobody could ask about is not kept untagged; a scan called off keeps what it read -----

    [Fact]
    public void A_song_read_without_FMOD_is_listed_but_asked_about_again_next_time()
    {
        _temp.File("m/Dick Dale - Misirlou.mp3", "x");
        var folders = new[] { _temp.Combine("m") };
        var cache = _temp.Combine("cache.json");

        var first = RadioLibrary.Scan(folders, cache, _ => new RadioLibrary.SongRead(null, Asked: false), Ct);
        var named = Assert.Single(first.Songs);
        Assert.Equal("Misirlou", named.Title);

        var reads = 0;
        var second = RadioLibrary.Scan(folders, cache, _ =>
        {
            reads++;
            return new RadioLibrary.SongRead(new SongInfo("Misirlou", "Dick Dale", "Surfers' Choice", 130000), Asked: true);
        }, Ct);
        Assert.Equal(1, reads);
        Assert.Equal(130000u, Assert.Single(second.Songs).LengthMs);
    }

    [Fact]
    public void A_scan_called_off_keeps_the_tags_it_read_for_the_next()
    {
        for (var i = 0; i < 4; i++) _temp.File($"m/song{i}.mp3", "x");
        var folders = new[] { _temp.Combine("m") };
        var cache = _temp.Combine("cache.json");

        using var cancel = new CancellationTokenSource();
        var firstReads = 0;
        Assert.Throws<OperationCanceledException>(() => RadioLibrary.Scan(folders, cache, path =>
        {
            if (++firstReads == 2) cancel.Cancel();
            return new SongInfo(Path.GetFileNameWithoutExtension(path), "Surfaris", null, 1000);
        }, cancel.Token));
        Assert.Equal(2, firstReads);

        var reads = 0;
        var result = RadioLibrary.Scan(folders, cache, path =>
        {
            reads++;
            return new SongInfo(Path.GetFileNameWithoutExtension(path), "Surfaris", null, 1000);
        }, Ct);
        Assert.Equal(2, reads);
        Assert.Equal(4, result.Songs.Count);
        Assert.All(result.Songs, s => Assert.Equal("Surfaris", s.Artist));
    }

    // ----- RadioQueue: the artists kept apart in a big library, quickly -----

    [Fact]
    public void A_big_shuffled_library_keeps_its_artists_apart_quickly()
    {
        var songs = Enumerable.Range(0, 10_000).Select(i => $"song{i}.mp3").ToArray();
        var artists = songs.Select((s, i) => (s, i)).ToDictionary(p => p.s, p => $"artist{p.i % 40}");
        var queue = new RadioQueue(new Random(3), s => artists[s]);

        var watch = Stopwatch.StartNew();
        queue.Load(songs, null, shuffle: true);
        watch.Stop();

        var order = new List<string> { queue.Current! };
        while (order.Count < songs.Length) order.Add(queue.Next(playedOut: true)!);
        Assert.Equal(songs.Length, order.Distinct().Count());
        for (var i = 1; i < order.Count; i++) Assert.NotEqual(artists[order[i - 1]], artists[order[i]]);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    // ----- SoundCues: files kept inside the mod, a one-shot's size held -----

    [Fact]
    public void A_cue_file_outside_its_mod_or_too_big_for_a_one_shot_is_a_problem_not_a_sound()
    {
        _temp.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/sfx/ok.ogg", "x");
        _temp.File($"{ModIds.BaseId}/music/theme.ogg", "x");
        _temp.File("outside.ogg", "x");
        _temp.Bytes($"{ModIds.BaseId}/sfx/huge.wav", new byte[SoundCues.LargestOneShot + 1]);
        _temp.File($"{ModIds.BaseId}/sfx/sfx.json", """
            { "cues": {
                "ui.ok": { "bus": "interface", "files": ["ok.ogg", "../../outside.ogg", "\\\\host\\share\\x.ogg", "C:\\Windows\\Media\\ding.wav"] },
                "ui.theme": { "bus": "interface", "files": ["../music/theme.ogg"] },
                "ui.huge": { "bus": "effects", "files": ["huge.wav"] },
                "air.huge": { "bus": "ambience", "loop": true, "files": ["huge.wav"] } } }
            """);

        var cues = SoundCues.Load(ModSet.Load(_temp.Path));

        Assert.EndsWith("ok.ogg", Assert.Single(cues.Get("ui.ok")!.Files));
        Assert.EndsWith("theme.ogg", Assert.Single(cues.Get("ui.theme")!.Files));
        Assert.Null(cues.Get("ui.huge"));
        Assert.NotNull(cues.Get("air.huge"));
        Assert.Equal(3, cues.Problems.Count(p => p.Contains("not inside its mod")));
        Assert.Contains(cues.Problems, p => p.Contains("too big"));
    }

    // ----- CarPaint: a skin's textures changed are looked at again -----

    [Fact]
    public void The_paint_stamp_changes_when_the_skins_textures_do()
    {
        var car = _temp.Combine("car");
        var texture = _temp.File("car/skins/red/body.dds", "first");
        var before = CarPaint.SkinStamp(car, "red");

        File.WriteAllText(texture, "repainted by the player");
        var after = CarPaint.SkinStamp(car, "red");
        Assert.NotEqual(before, after);

        File.SetLastWriteTimeUtc(texture, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.NotEqual(after, CarPaint.SkinStamp(car, "red"));
        Assert.Equal("", CarPaint.SkinStamp(_temp.Combine("no-skins"), "red"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
