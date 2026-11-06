using System.Globalization;
using StreetCorsa.Converters;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.RaceHub;
using StreetCorsa.Dialogs.Tune;
using StreetCorsa.Helpers;
using StreetCorsa.Models.AC;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;
using StreetCorsa.Screens.Diner;
using StreetCorsa.Screens.MainMenu;
using StreetCorsa.Screens.ModManager;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Career;

namespace StreetCorsa.Tests;

/// <summary>
/// Tech audit 2026-11-05, fix worker F (the screens): the Tune screen's Done and figures, the racing hub's field cut to
/// the pit boxes, the Mod Manager's list off the UI thread, the diner's track cards, a crew's colour as a brush
/// </summary>
[Collection(GlobalsCollection.Name)]
public sealed class Audit1105FTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly CultureInfo _culture = Locale.Culture;
    private readonly UnitSystem _units = Locale.Units;
    private readonly Func<string, string, string> _unitWords = Locale.UnitWords;

    public Audit1105FTests() => Locale.UnitWords = (unit, number) => $"{number} {unit}";

    public void Dispose()
    {
        Locale.Culture = _culture;
        Locale.Units = _units;
        Locale.UnitWords = _unitWords;
        _temp.Dispose();
    }

    // ---- the Tune screen ----

    private PartsCatalog Catalog()
    {
        _temp.Parts("parts/f1105/test", """
            [
                { "id": "f1105/test/shock", "specs": { "damping": 2000 } }
              ]
            """);
        return PartsCatalog.Load(_temp.Combine("parts"));
    }

    private static Car TestCar() => new()
    {
        DefinitionId = "test_car",
        Parts = [new PartInstance("f1105/test/shock") { ParentSlot = 200 }],
        HasPartsAssigned = true
    };

    private static TuneRowViewModel PressureRow(TuneDialogViewModel tune) =>
        tune.Tabs.SelectMany(t => t.Rows).Single(r => r.Row.Item == CarSetup.PressureFront);

    [Fact]
    public async Task Done_charges_the_tune_once_and_closes_even_when_the_clock_throws()
    {
        var catalog = Catalog();
        var car = TestCar();
        var rows = CarTune.Rows(car, catalog, CarSetup.For(car.Parts, car.Setup, catalog, new TuningRules().Ranges()));
        var dialogs = new DialogService();
        var performed = 0;
        var release = new TaskCompletionSource();
        var tune = new TuneDialogViewModel(dialogs, car, "Test car", catalog, new TuningRules(), rows, async (_, _) =>
        {
            performed++;
            await release.Task;
            throw new InvalidOperationException("the clock broke");
        });
        dialogs.ShowDialog(tune);

        var pressure = PressureRow(tune);
        pressure.Value = pressure.Maximum;
        Assert.Equal(1, tune.Changed);

        // Done pressed twice while the first is still writing: charged once
        tune.ApplyCommand.Execute(null);
        tune.ApplyCommand.Execute(null);
        Assert.Equal(1, performed);

        release.SetResult();
        for (var i = 0; i < 500 && dialogs.IsDialogOpen; i++) await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.False(dialogs.IsDialogOpen);
        Assert.Equal(1, performed);
    }

    private static TuneRow CarRow(string item) => new()
    {
        Key = item,
        Tab = TuneTab.Suspension,
        Item = item,
        Range = new SetupRange(-100, 100, 0.1)
    };

    [Fact]
    public void The_tunes_figures_read_in_imperial_and_metric_and_in_the_players_culture()
    {
        Locale.Culture = CultureInfo.GetCultureInfo("en-US");
        Locale.Units = UnitSystem.Imperial;
        Assert.Equal("-1.00 in", TuneWords.Figure(CarRow(CarSetup.RideHeightFront), -25.4));
        Assert.Equal("+0.010 in", TuneWords.Figure(CarRow(CarSetup.ToeFront), 0.254));
        Assert.Equal("+7.3 psi", TuneWords.Figure(CarRow(CarSetup.PressureFront), 0.5));
        Assert.Equal("+0.5 deg", TuneWords.Figure(CarRow(CarSetup.CamberFront), 0.5));

        Locale.Units = UnitSystem.Metric;
        Assert.Equal("-25 mm", TuneWords.Figure(CarRow(CarSetup.RideHeightFront), -25.4));
        Assert.Equal("+0.5 mm", TuneWords.Figure(CarRow(CarSetup.ToeFront), 0.5));
        Assert.Equal("+0.50 bar", TuneWords.Figure(CarRow(CarSetup.PressureFront), 0.5));

        // A comma culture writes every figure of the screen with its comma, not only some
        Locale.Culture = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("+0,50 bar", TuneWords.Figure(CarRow(CarSetup.PressureFront), 0.5));
        Assert.Equal("+0,5 deg", TuneWords.Figure(CarRow(CarSetup.CamberFront), 0.5));
        Locale.Units = UnitSystem.Imperial;
        Assert.Equal("-1,00 in", TuneWords.Figure(CarRow(CarSetup.RideHeightFront), -25.4));

        // A part's spec is the figure itself, to its step's places
        var damping = new TuneRow
        {
            Key = "x:damping",
            Tab = TuneTab.Suspension,
            Item = "damping",
            Part = new PartInstance("f1105/test/shock"),
            Range = new SetupRange(1000, 4000, 250)
        };
        Assert.Equal("2.500", TuneWords.Figure(damping, 2500));
        Locale.Culture = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("2,500", TuneWords.Figure(damping, 2500));
    }

    // ---- the racing hub's field ----

    private static EventOpponentResult Rival(string name, string car) => new() { OpponentName = name, CarDefinitionId = car };

    [Fact]
    public void A_field_is_cut_to_the_layouts_pit_boxes_and_rivals_in_unknown_cars_are_left_out()
    {
        var opponent = Rival("A", "c1");
        opponent.Field = [Rival("B", "c2"), Rival("C", "gone"), Rival("D", "c4"), Rival("E", "c5")];
        var tracks = new List<TrackInfo>
        {
            new()
            {
                TrackId = "strip",
                Pitboxes = "8",
                Configurations = [new TrackConfiguration { FolderName = "short", Pitboxes = "3" }]
            }
        };
        bool Known(string car) => car != "gone";

        // Three pit boxes: the player's and two rivals'
        var cut = RaceHubDialogViewModel.FieldThatFits(opponent, ("strip", "short"), tracks, Known);
        Assert.Equal(["A", "B"], cut.Select(r => r.OpponentName));

        // The track's own boxes for a layout that gives none, and no cut for a track the content does not list
        Assert.Equal(["A", "B", "D", "E"], RaceHubDialogViewModel.FieldThatFits(opponent, ("strip", null), tracks, Known).Select(r => r.OpponentName));
        Assert.Equal(4, RaceHubDialogViewModel.FieldThatFits(opponent, ("elsewhere", null), tracks, Known).Count);

        // Every car gone: nobody fits (the hub then races the one rival on their own)
        Assert.Empty(RaceHubDialogViewModel.FieldThatFits(opponent, ("strip", "short"), tracks, _ => false));
    }

    // ---- the Mod Manager ----

    private (ModManagerScreenViewModel Card, DialogService Dialogs, ModInstallFlow Install) ModCard(Func<string, IReadOnlyList<InstalledMod>>? list = null)
    {
        _temp.File("Content/streetcorsa/mod.json", "{}");
        _temp.File("Content/addons/radio/mod.json", """{ "id": "radio", "name": "Hit Parade" }""");
        var dialogs = new DialogService();
        var root = _temp.Combine("Content");
        var install = new ModInstallFlow(dialogs, _ => { }, () => null, root);
        return (new ModManagerScreenViewModel(dialogs, install, () => { }, root, list), dialogs, install);
    }

    [Fact]
    public async Task A_list_that_reads_again_takes_the_failed_reads_banner_away()
    {
        var fail = true;
        var (card, _, _) = ModCard(root => fail ? throw new IOException("locked") : ModManager.List(root));

        await card.RefreshAsync();
        Assert.Contains("locked", card.ErrorMessage);
        Assert.False(card.HasMods);

        fail = false;
        await card.RefreshAsync();
        Assert.Equal(string.Empty, card.ErrorMessage);
        Assert.Equal(["Hit Parade"], card.Mods.Select(m => m.Name));
    }

    [Fact]
    public async Task A_mod_deleted_from_the_card_goes_off_the_ui_thread_and_the_list_is_read_again()
    {
        var (card, dialogs, install) = ModCard();
        await card.RefreshAsync();
        var radio = Assert.Single(card.Mods);

        card.DeleteCommand.Execute(radio);
        var question = Assert.IsType<ConfirmationDialogViewModel>(dialogs.CurrentDialog);
        question.YesCommand.Execute(null);
        for (var i = 0; i < 500 && (card.Mods.Count > 0 || install.IsBusy); i++) await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.Empty(card.Mods);
        Assert.False(install.IsBusy);
        Assert.False(Directory.Exists(_temp.Combine("Content", "addons", "radio")));
        Assert.Equal(string.Empty, card.ErrorMessage);
    }

    [Fact]
    public async Task Work_on_the_content_folder_holds_installs_off_and_lets_go_when_it_throws()
    {
        var (_, _, install) = ModCard();
        var seen = new List<bool>();
        void Seen(bool working) => seen.Add(working);
        ModInstallFlow.WorkingChanged += Seen;
        try
        {
            await Assert.ThrowsAsync<IOException>(() => install.WorkAsync(() =>
            {
                Assert.True(install.IsBusy);
                throw new IOException("in use");
            }));
        }
        finally
        {
            ModInstallFlow.WorkingChanged -= Seen;
        }

        Assert.False(install.IsBusy);
        Assert.Equal([true, false], seen);
    }

    [Fact]
    public async Task A_file_dialog_that_fails_is_told_not_lost()
    {
        var dialogs = new DialogService();
        var install = new ModInstallFlow(dialogs, _ => { }, () => throw new InvalidOperationException("shell gone"), _temp.Combine("Content"));

        await install.RunAsync();

        Assert.True(dialogs.IsDialogOpen);
        Assert.False(install.IsBusy);
    }

    // ---- the diner's track cards ----

    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Fact]
    public async Task A_diner_track_card_reads_its_preview_through_the_shared_thumbnails()
    {
        _temp.Bytes("tracks/strip/ui/preview.png", Convert.FromBase64String(OnePixelPng));
        var info = new TrackInfo { TrackId = "strip", FolderPath = _temp.Combine("tracks", "strip"), Tags = ["drag"] };
        var card = new TrackCardViewModel(new PracticeTrack(info, null), Models.Race.RaceFormatFamilies.Drag);
        var told = new TaskCompletionSource();
        card.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrackCardViewModel.PreviewImage)) told.TrySetResult();
        };

        Assert.Null(card.PreviewImage);
        await told.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.NotNull(card.PreviewImage);
        Assert.True(card.IsDrag);
        Assert.Equal("strip", card.TrackId);
        Assert.Null(card.ConfigurationId);
    }

    [Fact]
    public async Task A_preview_whose_lookup_throws_is_none()
    {
        Assert.Null(await Thumbnails.LoadAsync(() => throw new IOException("gone"), 320, "a test track"));
        Assert.Null(await Thumbnails.LoadAsync(() => null, 320, "a test track"));
    }

    // ---- a crew's colour ----

    [Fact]
    public void A_crews_colour_too_dark_for_the_card_or_none_is_the_views_own()
    {
        var fallback = System.Windows.Media.Brushes.Gray;
        var converter = new ColourToBrushConverter { Fallback = fallback };

        var red = Assert.IsType<System.Windows.Media.SolidColorBrush>(converter.Convert("#B22222", typeof(System.Windows.Media.Brush), null!, CultureInfo.InvariantCulture));
        Assert.Equal(System.Windows.Media.Color.FromRgb(0xB2, 0x22, 0x22), red.Color);
        Assert.True(red.IsFrozen);
        Assert.Same(red, converter.Convert("#b22222", typeof(System.Windows.Media.Brush), null!, CultureInfo.InvariantCulture));

        Assert.Same(fallback, converter.Convert("#101010", typeof(System.Windows.Media.Brush), null!, CultureInfo.InvariantCulture));
        Assert.Same(fallback, converter.Convert("plaid", typeof(System.Windows.Media.Brush), null!, CultureInfo.InvariantCulture));
        Assert.Same(fallback, converter.Convert(null!, typeof(System.Windows.Media.Brush), null!, CultureInfo.InvariantCulture));
    }
}
