using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Screens.Newspaper;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Talk;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// Ideas batch 9: the paper's car ads mostly private people's and in no order, and no going out on the streets in a car
/// that cannot
/// </summary>
public class PaperAdsTests
{
    public PaperAdsTests() => TestLogging.SilenceLogging();

    // ----- what the paper prints -----

    private static UsedCarListing OnLot(string id, string dealer) => new() { Id = id, DealerLocation = dealer };

    private static UsedCarListing OutBack(string id, string dealer) => new() { Id = id, DealerLocation = dealer, PaperOnly = true };

    private static UsedCarListing Private(string id) => new() { Id = id, PaperOnly = true };

    [Fact]
    public void The_paper_prints_every_private_and_back_lot_car_and_a_few_off_each_lot()
    {
        List<UsedCarListing> forSale =
        [
            Private("p1"), Private("p2"), Private("p3"), OutBack("b1", "eastside"),
            .. Enumerable.Range(0, 10).Select(i => OnLot($"d{i}", "downtown")),
            .. Enumerable.Range(0, 10).Select(i => OnLot($"e{i}", "eastside"))
        ];

        var printed = PaperAds.Listings(forSale, perLot: 2).ToList();

        Assert.Equal(8, printed.Count);
        Assert.Subset(printed.ToHashSet(), forSale.Where(l => l.PaperOnly).ToHashSet());
        Assert.Equal(2, printed.Count(l => l.DealerLocation == "downtown"));
        Assert.Equal(2, printed.Count(l => !l.PaperOnly && l.DealerLocation == "eastside"));

        // The same cars off the lots whatever order the market has them in
        var again = PaperAds.Listings(Enumerable.Reverse(forSale), perLot: 2).Select(l => l.Id).Order();
        Assert.Equal(printed.Select(l => l.Id).Order(), again);
        Assert.Equal(4, PaperAds.Listings(forSale, perLot: 0).Count());
    }

    [Fact]
    public void A_car_out_back_or_a_private_one_is_on_no_lot()
    {
        var market = new UsedCarMarketService(new MemoryCatalog(), new MemoryProfiles());
        var listings = new List<UsedCarListing> { OnLot("lot", "downtown"), OutBack("back", "downtown"), Private("own") };

        Assert.Equal(["lot"], market.GetListingsByDealer(listings, "downtown").Select(l => l.Id));
        Assert.True(listings[2].IsPrivate);
        Assert.False(listings[1].IsPrivate);
        Assert.False(listings[1].IsOnLotOf("downtown"));
        Assert.Equal(3, market.GetAvailableListings(listings).Count);
    }

    [Fact]
    public void The_ads_run_in_no_order_but_the_same_one_every_read()
    {
        var ids = Enumerable.Range(0, 26).Select(i => $"{(char)('a' + i)}-ad").ToList();

        var first = ClassifiedOrder.Shuffled(ids, id => id).ToList();
        var again = ClassifiedOrder.Shuffled(Enumerable.Reverse(ids), id => id).ToList();

        Assert.Equal(first, again);
        Assert.Equal(ids.Order(), first.Order());
        Assert.NotEqual(ids, first);
    }

    [Fact]
    public void A_private_seller_has_the_name_in_the_paper_they_answer_the_phone_with()
    {
        var game = GameState.CreateNew("P");
        var key = TalkPeople.CarKey(Private("ad-42"));

        var printed = TalkPeople.SellerName(game, key);
        Assert.Empty(game.People);

        Assert.Equal(printed, TalkPeople.Seller(game, key).Name);
        Assert.Equal(printed, TalkPeople.SellerName(game, key));
    }

    // ----- going out on the streets -----

    private static (GameState Game, Car Car) Selected(Car car)
    {
        var game = GameState.CreateNew("P");
        game.Player.Cars.Add(car);
        game.Player.SelectedCarInstanceId = car.InstanceId;
        return (game, car);
    }

    private static bool Listed(string _) => true;

    [Fact]
    public void Nobody_goes_out_on_the_streets_with_no_car_or_one_the_catalog_lacks()
    {
        var game = GameState.CreateNew("P");
        Assert.Equal(GameText.Say("streets.no_car"), StreetReady.WhyNot(game, Listed, null));

        var (withCar, _) = Selected(new Car("car_a") { FuelLitres = 40, FuelTankLitres = 60 });
        Assert.Null(StreetReady.WhyNot(withCar, Listed, null));
        Assert.Equal(GameText.Say("streets.no_car"), StreetReady.WhyNot(withCar, _ => false, null));
    }

    [Fact]
    public void A_car_impounded_dry_or_wrecked_stays_home()
    {
        var (game, car) = Selected(new Car("car_a") { FuelLitres = 40, FuelTankLitres = 60 });

        car.ImpoundedUntil = game.Date.AddDays(3);
        Assert.Equal(GameText.Say("racehub.practice.impounded"), StreetReady.WhyNot(game, Listed, null));
        car.ImpoundedUntil = null;

        car.FuelLitres = 0;
        Assert.Equal(GameText.Say("streets.wont_run", ("problems", PartWords.Say("condition.problem.tank_empty"))), StreetReady.WhyNot(game, Listed, null));
        car.FuelLitres = 40;

        car.BodyDamageKmh = [CarCondition.TotaledKmh, 0, 0, 0];
        Assert.Contains(PartWords.Say("condition.problem.totaled"), StreetReady.WhyNot(game, Listed, null));
    }

    [Fact]
    public async Task A_car_whose_engine_will_not_start_stays_home()
    {
        var catalog = TestCars.Catalog.Value;
        var car = TestCars.WithRealEngine(catalog);
        car.FuelLitres = 40;
        car.FuelTankLitres = 60;
        var (game, _) = Selected(car);

        var runs = new Parts(catalog, c => EngineFactory.Evaluate(catalog, c.Engine!));
        Assert.Null(await StreetReady.WhyNotAsync(game, Listed, runs));

        var dead = new Parts(catalog, _ => null);
        Assert.Contains(GameText.Say("enginespecs.incomplete"), await StreetReady.WhyNotAsync(game, Listed, dead));

        // No engine at all: said without the dyno
        car.Parts.Remove(car.Engine!);
        Assert.Contains(GameText.Say("station.ui.no_engine"), await StreetReady.WhyNotAsync(game, Listed, runs));
    }

    private sealed class Parts(PartsCatalog catalog, Func<Car, EngineReport?> evaluate) : NullCarPartsService
    {
        public override bool IsAvailable => true;
        public override PartsCatalog Catalog => catalog;
        public override EngineReport? Evaluate(Car car) => evaluate(car);
    }
}
