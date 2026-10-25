using StreetCorsa.Models.AC;
using StreetCorsa.Mods;
using StreetCorsa.Rules;

namespace StreetCorsa.Tests;

/// <summary>
/// The second setting, Touge Nights (step 42 item 4), and what the core gained for it: a setting's own tracks
/// (game.json's <c>tracks</c>), its formats of its own (the drift module), its rules over the core's
/// </summary>
public class TougeNightsTests
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

    [Fact]
    public void Touge_nights_plays_its_own_rules_formats_cars_and_tracks()
    {
        var mods = ModSet.Load(RepoPaths.Content, only: ["touge_nights"], setting: "touge_nights");
        var book = RuleBook.Of(mods);
        var game = GameFile.Load(mods);
        var formats = Services.Race.RaceFormats.Load(mods, Path.Combine(RepoPaths.Root, "apps", "new-modes", "sc_race"));
        var career = Services.Career.CareerContent.Load(mods);

        Assert.Equal("touge_nights", mods.Setting?.Id);
        Assert.Equal(new DateOnly(2002, 4, 1), book.Start.Date);
        Assert.Equal(5, book.Economy.PartsPriceFactor);
        Assert.False(book.Systems.Police);
        Assert.Contains("touge", game.Tracks.Tags);
        Assert.True(game.Cars.OnlyIds);
        Assert.Contains("ks_toyota_ae86", game.Cars.Ids);
        Assert.Empty(formats.Problems);
        var drift = formats.Get("drift")!;
        Assert.True(drift.ByScore);
        // A drift battle is one run down a pass; the street races the setting's touge, never a drag
        Assert.False(drift.NeedsLoop());
        Assert.Equal("touge", formats.Street(Models.Race.RaceFormatFamilies.Road)!.Id);
        Assert.Equal(0, book.Street.DragShareAtNight);
        Assert.DoesNotContain("drift", game.Tracks.Tags);
        // The game puts the setting's module into the race mode under a name of its own
        Assert.Equal("format_touge_nights-drift", Services.Race.RaceFormats.ModuleOf(drift));
        Assert.Contains(formats.ModuleFiles, m => m.Name == "format_touge_nights-drift.lua");
        Assert.All(career.Events.Values, e => Assert.NotNull(formats.Get(e.Format)));
        Assert.Contains(career.Events.Values, e => e.Format == "drift");
    }

    [Fact]
    public void The_summit_car_park_is_its_meeting_spot_and_its_street()
    {
        var mods = ModSet.Load(RepoPaths.Content, only: ["touge_nights"], setting: "touge_nights");
        var diners = Services.Diner.DinerScenes.LoadAll(mods);
        var streets = Services.Street.StreetScenes.LoadAll(mods);
        var book = Services.Diner.DinerBook.Load(mods, diners);

        var summit = Assert.Single(diners);
        Assert.Equal("summit", summit.Id);
        Assert.True(summit.HasModel);
        Assert.True(File.Exists(summit.GroundFile));
        Assert.Equal(16, summit.Bays.Count);
        // The racers back into the bays along the rail, facing the player's car: the first bay nearest it
        Assert.All(summit.Bays, b => Assert.Equal(0f, b.Heading));
        Assert.True(Math.Abs(summit.Bays[0].X - summit.Arrive[^1].X) <= 3f);
        Assert.True(summit.LightingFor(Services.Street.StreetLight.Night).Lamps.Count >= 7);
        Assert.Equal("summit", Assert.Single(streets).Id);
        Assert.Equal("summit", Assert.Single(book.All).Id);
        Assert.False(RuleBook.Of(mods).Systems.Police);
        Assert.True(RuleBook.Of(mods).Systems.Diner);
    }
}
