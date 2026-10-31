using StreetCorsa.Dialogs;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;

namespace StreetCorsa.Tests;

/// <summary>
/// The Tune screen review's fixes: a dialog a closing one brings back stays in front, a part's spec stepped from its own
/// figure, the stock of a spec the part does not give and of an item a part holds away from nothing
/// </summary>
public sealed class TuneReviewFixesTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A dialog that brings another up as it closes, as the Tune screen brings the racing hub back</summary>
    private sealed class BringsBack(DialogService dialogs, IDialog back) : IDialog
    {
        public string? DuplicateKey => null;
        public void OnOpened() { }
        public void OnClosed() => dialogs.ShowDialog(back, jumpQueue: true);
    }

    [Fact]
    public void A_dialog_a_closing_one_brings_back_stays_in_front_of_the_waiting_ones()
    {
        var dialogs = new DialogService();
        var hub = new FakeDialog();
        dialogs.ShowDialog(new BringsBack(dialogs, hub));
        var waiting = new FakeDialog();
        dialogs.ShowDialog(waiting);

        dialogs.CloseDialog();

        Assert.Same(hub, dialogs.CurrentDialog);
        dialogs.CloseDialog();
        Assert.Same(waiting, dialogs.CurrentDialog);
    }

    private PartsCatalog Catalog()
    {
        _temp.Parts("parts/fix/test", """
            [
                { "id": "fix/test/shock", "specs": { "damping": 3750, "adjust": { "damping": { "min": 1000, "max": 5000, "step": 100 } } } },
                { "id": "fix/test/coils", "specs": { "adjust": { "drop": { "min": 0, "max": 50, "step": 5 } } } },
                { "id": "fix/test/blocks", "specs": { "adjust": { "ride_height_rear": { "min": -50, "max": -25, "step": 5 } } } }
              ]
            """);
        return PartsCatalog.Load(_temp.Combine("parts"));
    }

    private static List<TuneRow> Rows(Car car, PartsCatalog catalog) =>
        CarTune.Rows(car, catalog, CarSetup.For(car.Parts, car.Setup, catalog, new TuningRules().Ranges()));

    private static Car CarWith(string id) =>
        new() { DefinitionId = "test_car", Parts = [new PartInstance(id) { ParentSlot = 200 }], HasPartsAssigned = true };

    [Fact]
    public void A_spec_off_the_steps_goes_back_to_its_own_figure_and_steps_from_it()
    {
        var car = CarWith("fix/test/shock");
        var row = Assert.Single(Rows(car, Catalog()), r => r.Item == "damping");

        Assert.Equal(3750, row.Snap(row.Stock));
        Assert.Equal(3850, row.Snap(3830));
        Assert.Equal(0, CarTune.Apply(car, [(row, row.Stock)]));
        Assert.Empty(car.Parts[0].Tuning);
    }

    [Fact]
    public void A_spec_the_part_does_not_give_stands_where_the_code_reads_it()
    {
        var row = Assert.Single(Rows(CarWith("fix/test/coils"), Catalog()), r => r.Item == "drop");

        Assert.Equal(0, row.Stock);
        Assert.Equal(0, row.Value);
    }

    [Fact]
    public void An_item_a_part_holds_away_from_nothing_is_stock_at_the_nearest_it_allows()
    {
        var car = CarWith("fix/test/blocks");
        var row = Assert.Single(Rows(car, Catalog()), r => r.Item == CarSetup.RideHeightRear);

        Assert.Equal(-25, row.Stock);
        Assert.Equal(row.Stock, row.Value);
        Assert.False(SetupLook.Of(car, Catalog(), new TuningRules().Ranges()).Changed > 0);
    }
}
