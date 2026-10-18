using System.IO;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.TrackPicker;
using StreetCorsa.Models.AC;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Settings;

namespace StreetCorsa.Tests;

/// <summary>The free run and the test-and-tune from the racing hub: the tracks they offer, the search, the pick remembered</summary>
[Collection(GlobalsCollection.Name)]
public class PracticeRunsTests
{
    private static TrackInfo Track(string id, string name, string run = "clockwise", string length = "", params (string Folder, string Name, string Length)[] layouts) => new()
    {
        TrackId = id,
        Name = name,
        Run = run,
        Length = length,
        Configurations = layouts.Select(l => new TrackConfiguration { FolderName = l.Folder, Name = l.Name, Length = l.Length }).ToList()
    };

    private static List<TrackInfo> Install() =>
    [
        Track("monza", "Monza", "clockwise", "5793m", ("gp", "Grand Prix", ""), ("junior", "Junior", "2400m")),
        Track("drag_a", "Alpha Dragway", "dragstrip", "", ("drag200", "Eighth", "201m"), ("drag400", "Quarter", "402m")),
        Track("brands", "Brands Hatch", "clockwise", "3900m"),
        Track("drag_b", "Bravo Strip", "dragstrip", "402m")
    ];

    [Fact]
    public void Every_layout_is_a_track_of_its_own_by_name_and_a_track_with_none_is_one()
    {
        var all = PracticeTracks.All(Install());

        Assert.Equal(
            ["Alpha Dragway - Eighth", "Alpha Dragway - Quarter", "Brands Hatch", "Bravo Strip", "Monza - Grand Prix", "Monza - Junior"],
            all.Select(t => t.DisplayName));
        Assert.Equal("monza/junior", all.Single(t => t.LayoutName == "Junior").Key);
        Assert.Equal("brands", all.Single(t => t.Name == "Brands Hatch").Key);
        Assert.Null(all.Single(t => t.Name == "Brands Hatch").Configuration);
    }

    [Fact]
    public void A_test_and_tune_offers_the_drag_strips_only()
    {
        var strips = PracticeTracks.Strips(Install());

        Assert.Equal(["drag_a/drag200", "drag_a/drag400", "drag_b"], strips.Select(s => s.Key));
        Assert.All(strips, s => Assert.True(s.IsStrip));
    }

    [Fact]
    public void A_layout_with_no_length_of_its_own_says_the_tracks()
    {
        var monza = PracticeTracks.All(Install()).Where(t => t.TrackId == "monza").ToList();

        Assert.Equal(5793, monza.Single(t => t.Configuration == "gp").LengthMetres);
        Assert.Equal(2400, monza.Single(t => t.Configuration == "junior").LengthMetres);
    }

    [Theory]
    [InlineData("8", null, 8)]
    [InlineData("8", "2", 2)]
    [InlineData("", "", null)]
    [InlineData("many", null, null)]
    public void The_pits_are_the_layouts_else_the_tracks(string track, string? layout, int? expected)
    {
        var info = new TrackInfo { TrackId = "t", Name = "T", Pitboxes = track };
        var practice = new PracticeTrack(info, layout == null ? null : new TrackConfiguration { FolderName = "l", Name = "L", Pitboxes = layout });

        Assert.Equal(expected, practice.Pitboxes);
    }

    [Fact]
    public void A_free_run_goes_where_it_went_last_time_else_to_the_first_track_that_is_no_strip()
    {
        var all = PracticeTracks.All(Install());

        Assert.Equal("monza/junior", PracticeTracks.FreeRunDefault(all, "MONZA/junior")!.Key);
        Assert.Equal("brands", PracticeTracks.FreeRunDefault(all, "gone_track")!.Key);
        Assert.Equal("brands", PracticeTracks.FreeRunDefault(all, null)!.Key);
        Assert.Null(PracticeTracks.FreeRunDefault([], "monza"));
    }

    [Fact]
    public void A_test_and_tune_goes_to_the_strip_of_last_time_else_to_the_one_a_race_would_pick()
    {
        var strips = PracticeTracks.Strips(Install());

        Assert.Equal("drag_a/drag200", PracticeTracks.StripDefault(strips, "drag_a/drag200")!.Key);

        // The quarter, the longest, the first by folder: the strip RaceSetupBuilder.PickStrip picks
        var picked = RaceSetupBuilder.PickStrip(Install())!.Value;
        var fallback = PracticeTracks.StripDefault(strips, "monza")!;
        Assert.Equal((picked.TrackId, picked.TrackConfig), (fallback.TrackId, fallback.Configuration));
        Assert.Null(PracticeTracks.StripDefault([], null));
    }

    [Theory]
    [InlineData("monza", 2)]
    [InlineData("JUNIOR", 1)]
    [InlineData("italy", 2)]
    [InlineData("milan", 2)]
    [InlineData("historic", 1)]
    [InlineData("monza junior", 1)]
    [InlineData("monza brands", 0)]
    [InlineData("  ", 6)]
    public void The_search_finds_by_name_layout_city_country_and_tags(string query, int found)
    {
        var tracks = Install();
        tracks[0].City = "Milan";
        tracks[0].Country = "Italy";
        tracks[2].Tags = ["historic", "uk"];

        Assert.Equal(found, PracticeTracks.Filter(PracticeTracks.All(tracks), query).Count);
    }

    [Fact]
    public void The_one_picked_last_leads_the_list_until_a_search_is_typed()
    {
        var all = PracticeTracks.All(Install());
        var last = all.Single(t => t.Key == "monza/gp");

        Assert.Same(last, PracticeTracks.Filter(all, "", last)[0]);
        Assert.Equal(all.Count, PracticeTracks.Filter(all, "", last).Count);
        Assert.Equal("Monza - Grand Prix", PracticeTracks.Filter(all, "a", last).Select(t => t.DisplayName).ElementAt(4));
    }

    [Fact]
    public void A_layouts_preview_comes_before_the_tracks_and_a_preview_before_an_outline()
    {
        var folder = Directory.CreateTempSubdirectory("sc_practice_");
        try
        {
            var ui = Directory.CreateDirectory(Path.Combine(folder.FullName, "ui"));
            var layoutUi = Directory.CreateDirectory(Path.Combine(ui.FullName, "gp"));
            File.WriteAllText(Path.Combine(ui.FullName, "outline.png"), "");
            var info = new TrackInfo { TrackId = "t", Name = "T", FolderPath = folder.FullName };
            var withLayout = new PracticeTrack(info, new TrackConfiguration { FolderName = "gp", Name = "GP" });
            var plain = new PracticeTrack(info, null);

            Assert.Equal(Path.Combine(ui.FullName, "outline.png"), plain.FindPreview());

            File.WriteAllText(Path.Combine(ui.FullName, "preview.png"), "");
            Assert.Equal(Path.Combine(ui.FullName, "preview.png"), withLayout.FindPreview());

            File.WriteAllText(Path.Combine(layoutUi.FullName, "preview.png"), "");
            Assert.Equal(Path.Combine(layoutUi.FullName, "preview.png"), withLayout.FindPreview());
            Assert.Null(new PracticeTrack(new TrackInfo { TrackId = "x" }, null).FindPreview());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static TrackPickerDialogViewModel Picker(DialogService dialogs, List<PracticeTrack> tracks, PracticeTrack? last,
        List<string> picked, List<string> backs)
    {
        var picker = new TrackPickerDialogViewModel(dialogs, "title", "note", tracks, last, t => picked.Add(t.Key), () => backs.Add("back"));
        dialogs.ShowDialog(picker);
        return picker;
    }

    [Fact]
    public void A_click_picks_a_card_and_a_click_on_it_again_goes_there_with_the_picker_closed()
    {
        var dialogs = new DialogService();
        var tracks = PracticeTracks.All(Install());
        var picked = new List<string>();
        var picker = Picker(dialogs, tracks, tracks[2], picked, []);

        Assert.Same(tracks[2], picker.Cards[0].Track);
        Assert.True(picker.Cards[0].IsSelected);
        Assert.True(picker.Cards[0].IsLast);

        var other = picker.Cards[3];
        picker.PickCommand.Execute(other);
        Assert.True(other.IsSelected);
        Assert.False(picker.Cards[0].IsSelected);
        Assert.Empty(picked);

        picker.PickCommand.Execute(other);
        Assert.Equal([other.Track.Key], picked);
        Assert.Null(dialogs.CurrentDialog);

        // Answered once: a second click does not send the car twice
        picker.GoCommand.Execute(null);
        Assert.Single(picked);
    }

    [Fact]
    public void A_search_that_hides_the_pick_picks_the_first_card_it_finds()
    {
        var dialogs = new DialogService();
        var tracks = PracticeTracks.All(Install());
        var picked = new List<string>();
        var picker = Picker(dialogs, tracks, tracks.Single(t => t.Key == "brands"), picked, []);

        picker.SearchText = "monza";

        Assert.Equal(2, picker.Cards.Count);
        Assert.True(picker.Cards[0].IsSelected);
        picker.GoCommand.Execute(null);
        Assert.Equal(["monza/gp"], picked);
    }

    [Fact]
    public void Back_closes_the_picker_and_goes_back_with_nothing_picked()
    {
        var dialogs = new DialogService();
        var picked = new List<string>();
        var backs = new List<string>();
        var picker = Picker(dialogs, PracticeTracks.All(Install()), null, picked, backs);

        Assert.False(picker.GoCommand.CanExecute(null));
        picker.BackCommand.Execute(null);

        Assert.Single(backs);
        Assert.Empty(picked);
        Assert.Null(dialogs.CurrentDialog);
    }

    [Fact]
    public void Settings_from_before_the_test_and_tune_strip_load_with_none_and_keep_one_set()
    {
        var folder = Directory.CreateTempSubdirectory("sc_practice_settings_");
        try
        {
            var path = Path.Combine(folder.FullName, "settings.json");
            File.WriteAllText(path, "{\"FreeRunTrack\": \"monza/gp\"}");
            var service = new GameSettingsService(path, null, findInstall: () => null);
            service.Load();
            Assert.Equal("monza/gp", service.Current.FreeRunTrack);
            Assert.Equal(string.Empty, service.Current.TestAndTuneStrip);

            service.Current.TestAndTuneStrip = "drag_a/drag400";
            Assert.True(service.Save());
            var again = new GameSettingsService(path, null, findInstall: () => null);
            again.Load();
            Assert.Equal("drag_a/drag400", again.Current.TestAndTuneStrip);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
