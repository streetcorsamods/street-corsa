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

    [Fact]
    public void The_car_each_lot_prints_is_not_the_one_the_paper_puts_first()
    {
        // Picked by the same numbers as the shuffle, every lot's car went to the front of the paper
        List<UsedCarListing> forSale =
        [
            .. Enumerable.Range(0, 40).Select(i => Private($"p{i}")),
            .. Enumerable.Range(0, 40).SelectMany(d => Enumerable.Range(0, 10).Select(i => OnLot($"lot{d}-{i}", $"dealer{d}")))
        ];

        var printed = ClassifiedOrder.Shuffled(PaperAds.Listings(forSale, perLot: 1), l => l.Id).ToList();
        var dealersUpFront = printed.Take(printed.Count / 2).Count(l => !l.PaperOnly);

        Assert.Equal(80, printed.Count);
        Assert.InRange(dealersUpFront, 10, 30);
    }

    [Fact]
    public void A_car_bought_from_a_private_seller_says_whom_it_was_bought_from()
    {
        var listing = new UsedCarListing { CarDefinitionId = "car_a", PaperOnly = true, ListedDate = new DateTime(1969, 4, 20), History = new CarHistory { EarlierOwners = 2 } };
        var owners = listing.History.OwnerCount;

        var car = CarPurchaseService.CarFrom(listing, new DateTime(1969, 5, 1), "Player", privateSeller: "Frank");

        Assert.Equal(["Frank", "Player"], car.History.Owners.Select(o => o.Name));
        Assert.Equal(CarAcquisition.PrivateSale, car.History.Owners[^1].How);
        Assert.Equal(owners + 1, car.History.OwnerCount);
        Assert.Equal(CarAcquisition.Dealer, CarPurchaseService.CarFrom(OnLot("lot", "downtown"), new DateTime(1969, 5, 1), "Player").History.Owners[^1].How);
    }

    [Fact]
    public async Task On_a_day_with_fewer_private_ads_the_oldest_went_to_somebody_else()
    {
        var catalog = new MemoryCatalog();
        var profiles = new MemoryProfiles();
        catalog.UpsertCar(new CarDefinition { Id = "car_a", Name = "A", Brand = "Ford" });
        profiles.UpsertProfile(new CarProfile { CarDefinitionId = "car_a", BasePrice = 10000m });
        var market = new UsedCarMarketService(catalog, profiles);
        var rules = Rules.RuleBook.Current.Market;

        var today = new DateTime(1971, 6, 30, 8, 0, 0);
        var ads = Enumerable.Range(0, rules.PrivateAdsHigh + 10)
            .Select(i => new UsedCarListing { Id = $"p{i:00}", CarDefinitionId = "car_a", PaperOnly = true, ListedDate = today.AddDays(-1 - i % 10) })
            .ToList();

        var kept = (await market.RefreshMarketAsync(ads, [], today, 1.0)).Where(l => l.IsPrivate).ToList();

        Assert.InRange(kept.Count, rules.PrivateAdsLow, rules.PrivateAdsHigh);
        var newestGone = ads.Except(kept).Max(l => l.ListedDate);
        Assert.All(kept, l => Assert.True(l.ListedDate >= newestGone));
    }

    [Fact]
    public void A_save_without_its_people_still_names_the_paper_s_sellers()
    {
        var game = GameState.CreateNew("P");
        game.People = null!;

        Assert.False(string.IsNullOrEmpty(TalkPeople.SellerName(game, TalkPeople.CarKey(Private("ad-7")))));
    }

    [Fact]
    public void A_dealer_s_car_out_back_is_told_as_in_the_paper_not_on_the_lot()
    {
        var car = new UsedCarListing { Id = "mine", CarDefinitionId = "car_a", Price = 1000m };
        List<UsedCarListing> market = [car, OutBack("back", "downtown"), OnLot("lot", "downtown")];
        foreach (var l in market) l.CarDefinitionId = "car_a";

        var facts = TalkFacts.Market(car, market, _ => "A", 5).ToList();

        Assert.Equal(1, facts.Count(f => f.Contains(GameText.Say("chat.fact.market_paper"))));
        Assert.Equal(1, facts.Count(f => f.Contains(GameText.Say("chat.fact.market_dealer"))));
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
        Assert.Equal(GameText.Say("streets.no_car"), StreetReady.WhyNot(game, Listed, null)?.Why);

        var (withCar, _) = Selected(new Car("car_a") { FuelLitres = 40, FuelTankLitres = 60 });
        Assert.Null(StreetReady.WhyNot(withCar, Listed, null));
        Assert.Equal(GameText.Say("streets.not_installed"), StreetReady.WhyNot(withCar, _ => false, null)?.Why);
    }

    [Fact]
    public void A_car_impounded_dry_or_wrecked_stays_home()
    {
        var (game, car) = Selected(new Car("car_a") { FuelLitres = 40, FuelTankLitres = 60 });

        car.ImpoundedUntil = game.Date.AddDays(3);
        Assert.Equal(GameText.Say("racehub.practice.impounded"), StreetReady.WhyNot(game, Listed, null)?.Why);
        car.ImpoundedUntil = null;

        car.FuelLitres = 0;
        Assert.Equal(GameText.Say("streets.wont_run", ("problems", PartWords.Say("condition.problem.tank_empty"))), StreetReady.WhyNot(game, Listed, null)?.Why);
        car.FuelLitres = 40;

        car.BodyDamageKmh = [CarCondition.TotaledKmh, 0, 0, 0];
        Assert.Contains(PartWords.Say("condition.problem.totaled"), StreetReady.WhyNot(game, Listed, null)?.Why);
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
        Assert.Contains(GameText.Say("enginespecs.incomplete"), (await StreetReady.WhyNotAsync(game, Listed, dead))?.Why);

        // No engine at all: said without the dyno
        car.Parts.Remove(car.Engine!);
        Assert.Contains(GameText.Say("station.ui.no_engine"), (await StreetReady.WhyNotAsync(game, Listed, runs))?.Why);
    }

    private sealed class Parts(PartsCatalog catalog, Func<Car, EngineReport?> evaluate) : NullCarPartsService
    {
        public override bool IsAvailable => true;
        public override PartsCatalog Catalog => catalog;
        public override EngineReport? Evaluate(Car car) => evaluate(car);
    }
}
