using System.Text;
using StreetCorsa.Audio;
using StreetCorsa.Audio.Radio;

namespace StreetCorsa.Tests;

public class RadioQueueTests
{
    private static readonly string[] Ten = Enumerable.Range(1, 10).Select(i => $"song{i}.mp3").ToArray();

    [Fact]
    public void In_order_it_plays_from_the_song_picked_to_the_end_and_stops_there()
    {
        var queue = new RadioQueue { Repeat = RadioRepeat.Off };
        queue.Load(Ten, "song8.mp3", shuffle: false);

        Assert.Equal("song8.mp3", queue.Current);
        Assert.Equal("song9.mp3", queue.Next(playedOut: true));
        Assert.Equal("song10.mp3", queue.Next(playedOut: true));
        Assert.Null(queue.Next(playedOut: true));
        Assert.Null(queue.Current);
    }

    [Fact]
    public void A_skip_at_the_end_goes_round_even_when_the_list_does_not_repeat()
    {
        var queue = new RadioQueue { Repeat = RadioRepeat.Off };
        queue.Load(Ten, "song10.mp3", shuffle: false);
        Assert.Equal("song1.mp3", queue.Next(playedOut: false));
    }

    [Fact]
    public void Repeat_one_plays_the_song_again_but_a_skip_moves_on()
    {
        var queue = new RadioQueue { Repeat = RadioRepeat.One };
        queue.Load(Ten, "song3.mp3", shuffle: false);

        Assert.Equal("song3.mp3", queue.Next(playedOut: true));
        Assert.Equal("song3.mp3", queue.Upcoming);
        Assert.Equal("song4.mp3", queue.Next(playedOut: false));
    }

    [Fact]
    public void Repeat_all_goes_round_and_back_from_the_first_to_the_last()
    {
        var queue = new RadioQueue { Repeat = RadioRepeat.All };
        queue.Load(Ten, null, shuffle: false);

        Assert.Equal("song1.mp3", queue.Current);
        Assert.Equal("song10.mp3", queue.Previous());
        Assert.Equal("song1.mp3", queue.Next(playedOut: true));
        Assert.Equal("song2.mp3", queue.Upcoming);
    }

    [Fact]
    public void Shuffled_every_song_plays_once_a_round_and_a_round_never_starts_with_the_last_ones_end()
    {
        var queue = new RadioQueue(new Random(11)) { Repeat = RadioRepeat.All };
        queue.Load(Ten, null, shuffle: true);

        string? previous = null;
        for (var round = 0; round < 30; round++)
        {
            var played = new List<string> { queue.Current! };
            for (var i = 1; i < Ten.Length; i++) played.Add(queue.Next(playedOut: true)!);

            Assert.Equal(Ten.Order(), played.Order());
            if (previous != null) Assert.NotEqual(previous, played[0]);
            previous = played[^1];
            queue.Next(playedOut: true);
            Assert.NotEqual(previous, queue.Current);
            previous = null;
        }
    }

    [Fact]
    public void Shuffled_a_song_picked_plays_first()
    {
        var queue = new RadioQueue(new Random(5));
        queue.Load(Ten, "song6.mp3", shuffle: true);
        Assert.Equal("song6.mp3", queue.Current);
    }

    [Fact]
    public void Shuffled_an_artists_songs_do_not_follow_each_other_where_the_list_allows()
    {
        // Half the list by one artist: they can always be kept apart
        var artists = Ten.ToDictionary(s => s, s => int.Parse(s[4..^4]) % 2 == 0 ? "Dick Dale" : "Other " + s);
        for (var seed = 0; seed < 50; seed++)
        {
            var queue = new RadioQueue(new Random(seed), s => artists[s]);
            queue.Load(Ten, null, shuffle: true);

            var order = new List<string> { queue.Current! };
            for (var i = 1; i < Ten.Length; i++) order.Add(queue.Next(playedOut: false)!);

            for (var i = 1; i < order.Count; i++)
                Assert.False(artists[order[i]] == "Dick Dale" && artists[order[i - 1]] == "Dick Dale", $"seed {seed}: {string.Join(", ", order)}");
        }
    }

    [Fact]
    public void Shuffle_turned_on_or_off_keeps_the_song_playing()
    {
        var queue = new RadioQueue(new Random(2));
        queue.Load(Ten, "song4.mp3", shuffle: false);
        queue.Next(playedOut: false);

        queue.SetShuffle(true);
        Assert.Equal("song5.mp3", queue.Current);
        queue.SetShuffle(false);
        Assert.Equal("song5.mp3", queue.Current);
        Assert.Equal("song6.mp3", queue.Next(playedOut: false));
    }

    [Fact]
    public void An_empty_list_plays_nothing()
    {
        var queue = new RadioQueue();
        queue.Load([], null, shuffle: true);
        Assert.Null(queue.Current);
        Assert.Null(queue.Next(playedOut: true));
        Assert.Null(queue.Previous());
        Assert.Null(queue.Upcoming);
    }
}

public class RadioLibraryTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(@"C:\m\03 - Surf City.mp3", "Surf City")]
    [InlineData(@"C:\m\01. Wipe_Out.ogg", "Wipe Out")]
    [InlineData(@"C:\m\1964 Hits.mp3", "1964 Hits")]
    [InlineData(@"C:\m\Misirlou.flac", "Misirlou")]
    [InlineData(@"C:\m\12.mp3", "12")]
    public void A_song_without_tags_is_named_after_its_file(string path, string title) => Assert.Equal(title, RadioLibrary.TitleFromName(path));

    [Theory]
    [InlineData(@"D:\Oldies\BEN E. KING - Stand By Me.mp3", "Stand By Me", "BEN E. KING")]
    [InlineData(@"D:\Oldies\The Chantays - Pipeline - 1963.flac", "Pipeline - 1963", "The Chantays")]
    [InlineData(@"C:\m\03 - Surf City.mp3", "Surf City", null)]
    [InlineData(@"C:\m\1964 - Hits.mp3", "1964 - Hits", null)]
    [InlineData(@"C:\m\Misirlou.flac", "Misirlou", null)]
    public void An_untagged_songs_name_gives_its_artist_when_it_has_one(string path, string title, string? artist) =>
        Assert.Equal((title, artist), RadioLibrary.FromName(path));

    [Fact]
    public void A_scan_finds_the_songs_in_every_folder_reads_tags_once_and_counts_what_it_cannot_play()
    {
        _temp.File("a/surf/misirlou.mp3", "x");
        _temp.File("a/surf/cover.jpg", "x");
        _temp.File("a/pipeline.flac", "x");
        _temp.File("a/itunes.m4a", "x");
        _temp.File("b/wipe out.ogg", "x");
        var cache = Path.Combine(_temp.Path, "cache.json");
        var reads = 0;
        SongInfo? Read(string path)
        {
            reads++;
            return path.EndsWith("misirlou.mp3") ? new SongInfo("Misirlou", "Dick Dale", "Surfers' Choice", 130000) : null;
        }

        var folders = new[] { Path.Combine(_temp.Path, "a"), Path.Combine(_temp.Path, "b"), Path.Combine(_temp.Path, "gone") };
        var first = RadioLibrary.Scan(folders, cache, Read, TestContext.Current.CancellationToken);

        Assert.Equal(3, first.Songs.Count);
        Assert.Equal(1, first.NotPlayable);
        Assert.Equal([Path.Combine(_temp.Path, "gone")], first.MissingFolders);
        var misirlou = first.Songs.Single(s => s.Title == "Misirlou");
        Assert.Equal("Dick Dale", misirlou.Artist);
        Assert.Equal("Misirlou — Dick Dale", misirlou.Display);
        Assert.Equal("2:10", misirlou.LengthText);
        Assert.Contains(first.Songs, s => s.Title == "wipe out" && s.Artist == null);
        Assert.Equal(3, reads);

        // Nothing changed: all out of the cache
        var second = RadioLibrary.Scan(folders, cache, Read, TestContext.Current.CancellationToken);
        Assert.Equal(3, reads);
        Assert.Equal(first.Songs, second.Songs);

        // A file written anew is read again
        File.WriteAllText(Path.Combine(_temp.Path, "b", "wipe out.ogg"), "longer now");
        RadioLibrary.Scan(folders, cache, Read, TestContext.Current.CancellationToken);
        Assert.Equal(4, reads);
    }

    [Fact]
    public void A_folder_named_twice_lists_its_songs_once()
    {
        _temp.File("m/a.mp3", "x");
        var folder = Path.Combine(_temp.Path, "m");
        var result = RadioLibrary.Scan([folder, folder], Path.Combine(_temp.Path, "c.json"), _ => null, TestContext.Current.CancellationToken);
        Assert.Single(result.Songs);
    }

    [Fact]
    public void The_radio_starts_with_the_default_folder_and_keeps_what_it_is_told()
    {
        var path = Path.Combine(_temp.Path, RadioState.FileName);
        var fresh = RadioState.Load(path);
        Assert.Equal([RadioState.DefaultFolder], fresh.Folders);
        Assert.True(fresh.Shuffle);
        Assert.Equal(RadioRepeat.All, fresh.Repeat);

        fresh.Folders = [@"D:\Music", @"d:\music", @"E:\Surf"];
        fresh.Playlists = [new RadioPlaylist { Name = "Cruising", Songs = [@"D:\Music\a.mp3"] }, new RadioPlaylist { Name = " " }];
        fresh.Repeat = RadioRepeat.One;
        fresh.Song = @"D:\Music\a.mp3";
        fresh.PositionMs = 61_000;
        fresh.Save(path);

        var again = RadioState.Load(path);
        Assert.Equal([@"D:\Music", @"E:\Surf"], again.Folders);
        Assert.Equal("Cruising", again.Playlists.Single().Name);
        Assert.Equal(RadioRepeat.One, again.Repeat);
        Assert.Equal(61_000u, again.PositionMs);
    }
}

public class TagDecodingTests
{
    [Fact]
    public void Tags_are_read_however_they_are_written()
    {
        Assert.Equal("Misirlou", TagDecoding.Decode(FmodStudio.TagString, Encoding.Latin1.GetBytes("Misirlou\0\0")));
        Assert.Equal("Café Surf", TagDecoding.Decode(FmodStudio.TagStringUtf8, Encoding.UTF8.GetBytes("Café Surf")));
        Assert.Equal("Wipe Out", TagDecoding.Decode(FmodStudio.TagStringUtf16, [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("Wipe Out\0")]));
        Assert.Equal("Wipe Out", TagDecoding.Decode(FmodStudio.TagStringUtf16, [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("Wipe Out")]));
        Assert.Equal("Pipeline", TagDecoding.Decode(FmodStudio.TagStringUtf16BigEndian, Encoding.BigEndianUnicode.GetBytes("Pipeline")));
        Assert.Equal("The Chantays", TagDecoding.Decode(FmodStudio.TagString, Encoding.Latin1.GetBytes("The Chantays\0junk")));
        Assert.Null(TagDecoding.Decode(FmodStudio.TagString, Encoding.Latin1.GetBytes("  \0")));
        Assert.Null(TagDecoding.Decode(0, [1, 2, 3]));
    }
}
