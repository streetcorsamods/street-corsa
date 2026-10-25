using StreetCorsa.Models.AC;
using StreetCorsa.Rules;

namespace StreetCorsa.Tests;

/// <summary>
/// A setting's own tracks (game.json's <c>tracks</c>): the installed AC tracks its tags and ids name, every one when
/// none of them is installed
/// </summary>
public class GameTracksTests
{
    private static TrackInfo Track(string id, string run = "clockwise", params string[] tags) =>
        new() { TrackId = id, Run = run, Tags = [.. tags] };

    [Fact]
    public void A_game_races_on_the_tracks_its_tags_and_ids_name()
    {
        var game = new GameTracks { Tags = ["touge", "drift"], Ids = ["ks_nordschleife"], Never = ["la_canyons"] };
        var tracks = new List<TrackInfo>
        {
            Track("mount_akina_2017", tags: ["Touge", "Downhill", "Japan"]),
            Track("monza", tags: ["circuit", "Italy"]),
            Track("ks_nordschleife", tags: ["circuit"]),
            Track("la_canyons", tags: ["drift", "touge"]),
            Track("ks_drag", run: "dragstrip"),
            new() { TrackId = "layouts", Run = "clockwise", Tags = ["circuit"], Configurations = [new TrackConfiguration { FolderName = "drift", Tags = ["drift"] }, new TrackConfiguration { FolderName = "gp", Tags = ["circuit"] }] },
            // A null tag (System.Text.Json reads one) is no tag
            Track("nulls", tags: [null!, "circuit"])
        };

        var kept = game.Of(tracks, out var noneIn);

        Assert.False(noneIn);
        Assert.Equal(["mount_akina_2017", "ks_nordschleife", "ks_drag", "layouts"], kept.Select(t => t.TrackId));
        // A track in by one layout's tags brings that layout alone; the installed track keeps all of them
        Assert.Equal(["drift"], kept.Single(t => t.TrackId == "layouts").Configurations.Select(c => c.FolderName));
        Assert.Equal(2, tracks.Single(t => t.TrackId == "layouts").Configurations.Count);
    }

    [Fact]
    public void A_game_with_none_of_its_tracks_installed_races_on_every_one()
    {
        var game = new GameTracks { Tags = ["touge"] };
        var tracks = new List<TrackInfo> { Track("monza", tags: ["circuit"]), Track("ks_drag", run: "dragstrip") };

        var kept = game.Of(tracks, out var noneIn);

        Assert.True(noneIn);
        Assert.Equal(2, kept.Count);
        Assert.True(new GameTracks().Everything);
        Assert.Equal(2, new GameTracks().Of(tracks, out _).Count);
    }
}
