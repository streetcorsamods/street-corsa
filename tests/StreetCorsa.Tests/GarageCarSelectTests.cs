using StreetCorsa.Controls;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Mods;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;
using StreetCorsa.Screens.Garage;
using StreetCorsa.Services.Career;

namespace StreetCorsa.Tests;

/// <summary>
/// The garage laid out as a showroom to pick a car in (game.json's garageLayout car_select): the layout read, the cards'
/// names and badges, the sheet's figures and bars, a top speed read off ui_car.json, and the car stood above the strip
/// of cards
/// </summary>
[Collection(GlobalsCollection.Name)]
public class GarageCarSelectTests : IDisposable
{
    private const float FovY = 32f * MathF.PI / 180f;
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private ModSet WithGame(string json)
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("Content/mymod/mod.json", """{ "id": "mymod" }""");
        _temp.File("Content/mymod/game.json", json);
        return ModSet.Load(_temp.Combine("Content"));
    }

    [Fact]
    public void The_core_garage_is_the_workshop()
    {
        Assert.Equal(GameFile.WorkshopLayout, new GameFile().GarageLayout);
        Assert.False(new GameFile().IsCarSelect);
    }

    [Fact]
    public void A_setting_lays_the_garage_out_as_a_showroom()
    {
        var file = GameFile.Load(WithGame("""{ "garageLayout": "Car_Select" }"""));
        Assert.True(file.IsCarSelect);
    }

    [Fact]
    public void A_layout_there_is_none_of_is_told_and_the_workshop_kept()
    {
        var file = GameFile.Load(WithGame("""{ "garageLayout": "turntable" }"""));
        Assert.Equal(GameFile.WorkshopLayout, file.GarageLayout);
        Assert.Contains(ModProblems.All, p => p.Contains("garageLayout") && p.Contains("turntable"));
    }

    [Theory]
    [InlineData("250km/h", 250.0)]
    [InlineData("250 km/h", 250.0)]
    [InlineData("250+ km/h", 250.0)]
    [InlineData("155 mph", 155 * 1.609344)]
    [InlineData("155+ mph", 155 * 1.609344)]
    [InlineData("~180mph", 180 * 1.609344)]
    [InlineData("220", 220.0)]
    public void A_top_speed_is_read_in_kmh(string text, double expected) =>
        Assert.Equal(expected, AcSpecs.ParseSpeed(text)!.Value, 6);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--")]
    [InlineData("n/a")]
    public void No_top_speed_is_none(string? text) => Assert.Null(AcSpecs.ParseSpeed(text));

    [Theory]
    [InlineData("Mazda", "Mazda MX5 ND", "MX5 ND")]
    [InlineData("Ford", "Mustang Boss 302", "Mustang Boss 302")]
    [InlineData("Alfa Romeo", "alfa romeo GTA", "GTA")]
    [InlineData("Ford", "Ford", "Ford")]
    [InlineData("Fordson", "Ford Anglia", "Ford Anglia")]
    public void A_card_names_the_car_without_its_make(string brand, string name, string expected) =>
        Assert.Equal(expected, CarSelectCard.NameOf(new CarDefinition { Id = "x", Brand = brand, Name = name }));

    [Fact]
    public void A_card_with_no_name_goes_by_its_folder() =>
        Assert.Equal("ks_car", CarSelectCard.NameOf(new CarDefinition { Id = "ks_car", Brand = "Make", Name = " " }));

    private static CarDefinition Car(string id, string? bhp, string? weight, string? top) =>
        new() { Id = id, Brand = "Make", Name = id, Specs = new CarSpecsData { Bhp = bhp, Weight = weight, TopSpeed = top } };

    [Fact]
    public void The_bars_are_measured_against_the_highest_of_the_cards()
    {
        var slow = Car("slow", "100 bhp", "1000 kg", "160 km/h");
        var fast = Car("fast", "400 bhp", "1600 kg", "320 km/h");
        var scale = CarSheet.Scale.Of([slow, fast]);

        Assert.Equal(400, scale.Power, 6);
        Assert.Equal(1600, scale.Weight, 6);
        Assert.Equal(250, scale.Ratio, 6);
        Assert.Equal(320, scale.Speed, 6);

        var card = CarSelectCard.Shown(new ShowroomCar(slow, 1, true, false, 0), 0);
        card.Scale = scale;
        var sheet = new CarSheet(card, null);
        Assert.Equal([0.25, 0.625, 0.4, 0.5], sheet.Rows.Select(r => Math.Round(r.Fraction, 3)));
    }

    [Fact]
    public void A_figure_the_car_does_not_give_has_no_bar()
    {
        var card = CarSelectCard.Shown(new ShowroomCar(Car("bare", null, "1200 kg", null), 1, true, false, 0), 0);
        card.Scale = CarSheet.Scale.Of([card.Definition, Car("other", "200 bhp", "1000 kg", "200 km/h")]);
        var sheet = new CarSheet(card, null);

        Assert.Equal(0, sheet.Rows[0].Fraction);
        Assert.Equal(0, sheet.Rows[2].Fraction);
        Assert.Equal(0, sheet.Rows[3].Fraction);
        Assert.True(sheet.Rows[1].Fraction > 0);
    }

    [Fact]
    public void The_best_run_is_told_when_there_is_one()
    {
        var card = CarSelectCard.Shown(new ShowroomCar(Car("c", "100 bhp", "1000 kg", null), 1, true, false, 0), 0);
        Assert.NotEqual(new CarSheet(card, null).Best, new CarSheet(card, 12480).Best);
        Assert.Contains("12", new CarSheet(card, 12480).Best);
    }

    [Fact]
    public void A_showroom_card_is_open_shut_or_gone()
    {
        var def = Car("c", null, null, null);
        var open = CarSelectCard.Shown(new ShowroomCar(def, 1, true, false, 0), 0);
        var shut = CarSelectCard.Shown(new ShowroomCar(def, 2, false, false, 30000), 1000);
        var gone = CarSelectCard.Shown(new ShowroomCar(def, 1, true, false, 0, Taken: true), 0);

        Assert.True(open.CanTake);
        Assert.False(open.IsShut);
        Assert.False(open.HasBadge);
        Assert.Equal(1, open.Opacity);

        Assert.False(shut.CanTake);
        Assert.True(shut.IsShut);
        Assert.Contains("30", shut.Badge);
        Assert.True(shut.Opacity < 1);

        Assert.False(gone.CanTake);
        Assert.True(gone.IsShut);
        Assert.True(gone.HasBadge);
    }

    [Fact]
    public void The_players_card_says_so_and_which_is_in_the_garage()
    {
        var own = CarSelectCard.Own(new CarDisplayViewModel { CarDefinition = Car("mine", null, null, null) }, 1);
        Assert.True(own.IsOwned);
        Assert.False(own.CanTake);
        var yours = own.Badge;

        own.IsInGarage = true;
        Assert.NotEqual(yours, own.Badge);
    }

    [Fact]
    public void Nothing_over_the_bottom_leaves_the_car_where_it_was()
    {
        Assert.Equal(0f, PreviewFraming.BottomDrop(0, 1080, 7f, FovY));
        Assert.Equal(1f, PreviewFraming.HeightLeft(0, 1080));
    }

    [Fact]
    public void A_strip_over_the_bottom_raises_the_car_into_what_it_leaves()
    {
        var drop = PreviewFraming.BottomDrop(210, 1080, 7f, FovY);
        // Half the strip, as a share of the half-height, at 7 m through the lens
        Assert.Equal((float)(105.0 / 540 * 7 * Math.Tan(FovY / 2)), drop, 5);
        Assert.Equal((1080f - 210) / 1080, PreviewFraming.HeightLeft(210, 1080), 5);
        // Further back, further down
        Assert.True(PreviewFraming.BottomDrop(210, 1080, 9f, FovY) > drop);
    }

    [Fact]
    public void However_much_is_covered_the_car_keeps_half_the_picture()
    {
        Assert.Equal(0.5f, PreviewFraming.HeightLeft(5000, 1080));
        Assert.Equal(PreviewFraming.BottomDrop(540, 1080, 7f, FovY), PreviewFraming.BottomDrop(5000, 1080, 7f, FovY), 5);
        Assert.Equal(0f, PreviewFraming.BottomDrop(200, 0, 7f, FovY));
        Assert.Equal(1f, PreviewFraming.HeightLeft(-50, 1080));
    }
}
