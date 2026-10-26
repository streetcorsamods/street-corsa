using System.IO;
using System.Reflection;
using StreetCorsa.Configuration;
using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.Career.Filters;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Screens.UsedParts;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.CarLot;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Garage;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Police;
using StreetCorsa.Services.Scripting;
using StreetCorsa.Services.Simulation;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>The world services' fixes of the 2026-10-25 audit: buying and selling, the shops, the rivals, the scenes</summary>
public class Audit1025CTests : IDisposable
{
    private readonly TempDir _temp = new();

    public Audit1025CTests() => TestLogging.SilenceLogging();

    public void Dispose() => _temp.Dispose();

    private static CarDefinition Definition(string id = "car_a") => new() { Id = id, Name = "A", Brand = "Ford" };

    // ----- buying a car -----

    private static (GameState State, Opponent Seller, Car Spare, RivalCarAd Ad) RivalAd()
    {
        var state = GameState.CreateNew("P");
        var spare = new Car("car_a") { PowerHp = 250 };
        var seller = new Opponent("Sal", 30, Gender.Female, 92, 50) { Money = 100m, Status = RacerStatus.ReadyToRace };
        seller.Cars.AddRange([new Car("car_good") { PowerHp = 200 }, spare]);
        state.Racers.AddRacer(seller);
        var ad = RivalCarAds.Post(state, seller, spare, 700m, state.Date);
        return (state, seller, spare, ad);
    }

    [Fact]
    public async Task A_rival_whose_race_with_the_player_is_unsettled_does_not_sell_the_car_out_of_the_paper()
    {
        var (state, seller, spare, ad) = RivalAd();
        state.PendingRace = new RaceContext { OpponentName = "Sal" };
        var money = state.Player.Money;
        var service = new CarPurchaseService(new RaceResultProcessorTests.FakeRepository(), new NoParts(), new QuietTime());

        var refused = await service.PurchaseFromRivalAsync(state, ad, Definition());

        Assert.Equal(PurchaseOutcome.NoLongerAvailable, refused.Outcome);
        Assert.Contains("Sal", refused.Message);
        Assert.Equal(money, state.Player.Money);
        Assert.Contains(spare, seller.Cars);
        Assert.Empty(state.Player.Cars);
        Assert.Contains(ad, state.NewspaperAds.RivalCars);

        // Settled, it sells
        state.PendingRace = null;
        Assert.True((await service.PurchaseFromRivalAsync(state, ad, Definition())).Succeeded);
        Assert.Same(spare, Assert.Single(state.Player.Cars));
    }

    [Fact]
    public async Task A_listing_is_paid_at_the_markets_price_not_the_one_on_a_stale_copy()
    {
        var state = GameState.CreateNew("P");
        state.Player.Money = 10000m;
        var live = new UsedCarListing { Id = "l1", CarDefinitionId = "car_a", Price = 3000m, Condition = 0.8f };
        state.UsedCarMarket.Add(live);
        var copy = new UsedCarListing { Id = "l1", CarDefinitionId = "car_a", Price = 4000m, Condition = 0.8f };
        var service = new CarPurchaseService(new RaceResultProcessorTests.FakeRepository(), new NoParts(), new QuietTime());

        var result = await service.PurchaseAsync(state, copy, Definition());

        Assert.True(result.Succeeded);
        Assert.Equal(7000m, state.Player.Money);
        Assert.Equal(3000m, Assert.Single(state.Player.Cars).PurchasePrice);
        Assert.True(live.IsSold);

        // Nor is the money checked against the copy: one the player can afford at the copy's price but not the market's is not bought
        state.Player.Money = 4500m;
        var dearer = new UsedCarListing { Id = "l2", CarDefinitionId = "car_a", Price = 5000m, Condition = 0.8f };
        state.UsedCarMarket.Add(dearer);
        var cheap = new UsedCarListing { Id = "l2", CarDefinitionId = "car_a", Price = 4000m, Condition = 0.8f };
        Assert.Equal(PurchaseOutcome.NotEnoughMoney, (await service.PurchaseAsync(state, cheap, Definition())).Outcome);
        Assert.False(dearer.IsSold);
        Assert.Equal(4500m, state.Player.Money);
    }

    /// <summary>Putting the factory parts on a car fails</summary>
    private sealed class ThrowingParts : NullCarPartsService
    {
        public override Task<bool> EnsurePartsAsync(Car car) => throw new InvalidOperationException("parts are broken");
    }

    [Fact]
    public async Task A_car_whose_factory_parts_cannot_be_put_on_is_still_bought_once_and_paid_for_once()
    {
        var state = GameState.CreateNew("P");
        state.Player.Money = 10000m;
        var listing = new UsedCarListing { Id = "l1", CarDefinitionId = "car_a", Price = 4000m, Condition = 0.8f };
        state.UsedCarMarket.Add(listing);
        var service = new CarPurchaseService(new RaceResultProcessorTests.FakeRepository(), new ThrowingParts(), new QuietTime());

        var result = await service.PurchaseAsync(state, listing, Definition());

        Assert.True(result.Succeeded);
        Assert.Single(state.Player.Cars);
        Assert.Equal(6000m, state.Player.Money);
        Assert.True(listing.IsSold);
    }

    // ----- selling a car -----

    [Fact]
    public async Task A_rivals_offer_waits_while_their_race_with_the_player_is_unsettled()
    {
        var market = new FakeMarket(2000m);
        var service = new CarSaleService(market, new QuietTime(), new RaceResultProcessorTests.FakeRepository(), new Random(1));
        var state = GameState.CreateNew("P");
        var car = new Car("car_a") { InstanceId = Guid.NewGuid() };
        state.Player.Cars.Add(car);
        var rival = new Opponent("Pat", 30, Gender.Male, 92, 50) { Money = 5000m, Status = RacerStatus.ReadyToRace };
        rival.Cars.Add(new Car("car_b"));
        state.Racers.AddRacer(rival);
        var ad = new CarSaleAd
        {
            CarInstanceId = car.InstanceId, AskingPrice = 1800m, PostedDate = state.Date,
            Offer = new CarOffer { Id = Guid.NewGuid(), BuyerName = "Pat", RivalName = "Pat", Amount = 1800m, ValueAtOffer = 2000m, Expires = state.Date.AddDays(1) }
        };
        state.NewspaperAds.PlayerCars.Add(ad);
        state.PendingRace = new RaceContext { OpponentName = "Pat" };
        var money = state.Player.Money;

        var waiting = await service.AcceptOfferAsync(state, ad);

        Assert.Equal(SaleOutcome.Racing, waiting.Outcome);
        Assert.NotNull(ad.Offer);
        Assert.Contains(car, state.Player.Cars);
        Assert.DoesNotContain(car, rival.Cars);
        Assert.Equal(money, state.Player.Money);
        Assert.Equal(5000m, rival.Money);

        state.PendingRace = null;
        Assert.True((await service.AcceptOfferAsync(state, ad)).Succeeded);
        Assert.Contains(car, rival.Cars);
        Assert.Equal(3200m, rival.Money);
    }

    [Fact]
    public void A_dealer_pays_the_same_rounded_sum_for_a_car_whoever_sells_it()
    {
        var market = new FakeMarket(1234m);
        var service = new CarSaleService(market, new QuietTime(), new RaceResultProcessorTests.FakeRepository());
        var state = GameState.CreateNew("P");
        var car = new Car("car_a");

        var quote = service.QuoteDealer(state, car);

        Assert.Equal(CarSaleService.DealerPays(car, 1234m), quote.Amount);
        Assert.Equal(0m, quote.Amount % 10m);
        Assert.Equal(Math.Round(1234m * (decimal)CarSaleService.DealerShare / 10m) * 10m, quote.Amount);
    }

    [Fact]
    public void A_car_that_cannot_be_valued_is_worth_what_was_paid_for_it()
    {
        var car = new Car("car_a") { PurchasePrice = 1234m };

        Assert.Equal(CarValuation.PaidFor(car), CarValuation.WorthOrPaid(car, _ => throw new IOException("catalog.db is busy"), StreetCorsa.Logging.AppLoggerFactory.CreateLogger("Test")));
        Assert.Equal(900m, CarValuation.WorthOrPaid(car, _ => 900m, StreetCorsa.Logging.AppLoggerFactory.CreateLogger("Test")));
    }

    // ----- the price book -----

    private static PartPriceBook BookOf(Dictionary<string, double> power)
    {
        var made = typeof(PartPriceBook).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        return (PartPriceBook)made.Invoke([new Dictionary<string, double>(power, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), Array.Empty<string>()]);
    }

    [Fact]
    public void A_book_with_a_power_that_is_no_number_is_kept_for_the_session_and_only_its_numbers_on_disk()
    {
        var folders = new[] { _temp.Combine("parts") };
        Directory.CreateDirectory(folders[0]);
        var path = _temp.Combine("part_power.json");
        var book = BookOf(new() { ["good"] = 120, ["broken"] = double.NaN });
        var store = new PriceBookStore(() => folders, _ => book, path);

        Assert.Same(book, store.Book);
        Assert.True(File.Exists(path));

        IReadOnlyDictionary<string, double>? given = null;
        _ = new PriceBookStore(() => folders, kept => { given = kept; return PartPriceBook.Empty; }, path).Book;
        Assert.NotNull(given);
        Assert.Equal(120, given!["good"]);
        Assert.False(given.ContainsKey("broken"));
    }

    [Fact]
    public void A_kept_measuring_without_its_power_is_measured_again()
    {
        var folders = new[] { _temp.Combine("parts") };
        Directory.CreateDirectory(folders[0]);
        var path = _temp.Combine("part_power.json");
        File.WriteAllText(path, $$"""{ "Fingerprint": "{{PartPriceBook.Fingerprint(folders)}}", "Power": null }""");

        var asked = 0;
        IReadOnlyDictionary<string, double>? given = new Dictionary<string, double>();
        var store = new PriceBookStore(() => folders, kept => { asked++; given = kept; return PartPriceBook.Empty; }, path);

        Assert.Same(PartPriceBook.Empty, store.Book);
        Assert.Equal(1, asked);
        Assert.Null(given);
    }

    // ----- restocking the part shops -----

    private static readonly CatalogParts Parts = new();

    /// <summary>The real catalog, whose engine builds cannot be had: the kits and the wrecks fail</summary>
    private sealed class NoBuilds : NullCarPartsService
    {
        public Action? Asked { get; set; }

        public override PartsCatalog Catalog => Parts.Catalog;
        public override bool IsAvailable => true;

        public override EngineBuildIndex Builds
        {
            get
            {
                Asked?.Invoke();
                throw new InvalidOperationException("the builds are broken");
            }
        }
    }

    private static readonly PartShopDefinition Mopar = new()
    {
        Id = "hemi", Name = "Hemi Hut", Region = "Burbank", Kind = "specialist", Makes = ["mopar"], StockLow = 5, StockHigh = 5
    };

    private static readonly PartShopDefinition Yard = new()
    {
        Id = "yard", Name = "Wreckers", Region = "Docks", Kind = "junkyard", StockLow = 4, StockHigh = 4
    };

    private static (PartShopService Service, GameState State, NoBuilds Parts) FailingShops()
    {
        var parts = new NoBuilds();
        var service = new PartShopService(new FakePartShops(Mopar, Yard), parts, new NewspaperPartsService(Parts));
        var state = new GameState();
        PartShopRoster.Sync(state, service.Catalog);
        return (service, state, parts);
    }

    [Fact]
    public async Task A_restock_whose_kits_fail_leaves_the_shop_as_it_was_for_the_next_caller()
    {
        var (service, state, _) = FailingShops();
        var shop = PartShopRoster.Find(state, "hemi")!;
        var yard = PartShopRoster.Find(state, "yard")!;
        var shelves = new List<ShopShelf> { new() { DefinitionId = "old_part", Count = 2 } };
        shop.Shelves = shelves;

        await Assert.ThrowsAnyAsync<Exception>(() => service.RestockAsync(state, state.Date, new Random(3)));

        Assert.Null(shop.RestockedOn);
        Assert.Same(shelves, shop.Shelves);

        // The yard's wrecks fail the same way, before anything is awaited
        shop.Status = DealerStatus.Closed;
        await Assert.ThrowsAnyAsync<Exception>(() => service.RestockAsync(state, state.Date, new Random(3)));
        Assert.Null(yard.RestockedOn);
        Assert.Empty(yard.Yard);
    }

    [Fact]
    public async Task A_shop_that_closed_while_its_kits_were_worked_out_keeps_the_empty_shelves_its_closing_left()
    {
        var (service, state, parts) = FailingShops();
        var shop = PartShopRoster.Find(state, "hemi")!;
        PartShopRoster.Find(state, "yard")!.Status = DealerStatus.Closed;
        shop.Shelves = [new() { DefinitionId = "old_part", Count = 2 }];
        parts.Asked = () =>
        {
            shop.Status = DealerStatus.Closed;
            shop.Shelves = [];
        };

        await Assert.ThrowsAnyAsync<Exception>(() => service.RestockAsync(state, state.Date, new Random(3)));

        Assert.Empty(shop.Shelves);
    }

    // ----- one key for a line of kits -----

    [Fact]
    public void A_line_of_kits_has_one_key_in_the_shop_and_on_the_mail_order()
    {
        Assert.Equal("chevy_327|short_block", ShopKit.KeyOf("chevy_327", "short_block"));
        Assert.Equal("kit:" + ShopKit.KeyOf("chevy_327", "short_block"), PartOfferViewModel.KitKey("chevy_327", "short_block"));
    }

    // ----- the scenes -----

    private ModSet GarageMods()
    {
        _temp.File("content/streetcorsa/mod.json", """{ "id": "streetcorsa", "name": "Street Corsa" }""");
        _temp.File("content/streetcorsa/garages/shop/scene.json", """
            {
              "room": { "minX": -4, "maxX": 4, "minZ": -5, "maxZ": 5, "height": 3 },
              "marks": { "door": { "behind": 3 } }
            }
            """);
        return ModSet.CoreOf(_temp.Combine("content"));
    }

    [Fact]
    public void A_garage_id_that_is_no_plain_folder_name_reads_no_file()
    {
        var mods = GarageMods();

        Assert.NotNull(GarageRooms.Load(mods, "shop").Walls);
        Assert.Equal(new MarkSpec(Ahead: -3f), GarageScene.Load(mods, "shop").Marks["door"]);

        foreach (var id in new[] { "../garages/shop", "..\\garages\\shop", "..", "" })
        {
            Assert.Null(GarageRooms.Load(mods, id).Walls);
            Assert.Equal(new MarkSpec(Ahead: -7f), GarageScene.Load(mods, id).Marks["door"]);
        }
    }

    [Fact]
    public void A_scenes_way_keeps_to_the_scenes_scale()
    {
        var points = ScenePaths.Points([[0, 0], [1e30f, 0], [5, 5], [0, -20000], [float.NaN, 1], [-10000, 10000]]);

        Assert.Equal([new GroundPoint(0, 0), new GroundPoint(5, 5), new GroundPoint(-10000, 10000)], points);
    }

    [Fact]
    public void A_warehouses_bays_and_camera_are_held_to_the_floor()
    {
        _temp.File("wh/warehouse.kn5", "");
        _temp.File("wh/warehouse.json", """
            {
              "bays": [ { "x": 0, "z": 0, "heading": 0 }, { "x": 1e30, "z": 0, "heading": 0 }, { "x": 3, "z": -20000, "heading": 90 } ],
              "camera": { "alpha": 100, "beta": 9, "radius": 1e30, "targetHeight": -1e30 },
              "room": { "halfLength": 1e30, "halfWidth": 10, "height": 6 },
              "exit": { "x": 50000, "z": 0 }
            }
            """);

        var scene = Assert.IsType<WarehouseScene>(WarehouseScenes.Load(_temp.Combine("wh")));

        Assert.Equal(new WarehouseBay(0, 0, 0), Assert.Single(scene.Bays));
        Assert.Equal(WarehouseScenes.MostCameraRadius, scene.Camera.Radius);
        Assert.Equal(WarehouseScenes.MostCameraBeta, scene.Camera.Beta);
        Assert.Equal(-WarehouseScenes.MostCameraRadius, scene.Camera.TargetHeight);
        Assert.InRange(scene.Camera.Alpha, -MathF.PI, MathF.PI);
        Assert.Null(scene.Room);
        Assert.Null(scene.Exit);

        // Out of the floor altogether: no bay at all, and the warehouse is left out
        _temp.File("far/warehouse.kn5", "");
        _temp.File("far/warehouse.json", """{ "bays": [ { "x": 1e30, "z": 0, "heading": 0 } ] }""");
        Assert.Null(WarehouseScenes.Load(_temp.Combine("far")));
    }

    // ----- seedable dice -----

    [Fact]
    public void A_new_racer_is_the_same_racer_for_the_same_dice()
    {
        var first = new OpponentGenerationService(new Random(7)).GenerateOpponent("Gus", 25, Gender.Male);
        var second = new OpponentGenerationService(new Random(7)).GenerateOpponent("Gus", 25, Gender.Male);

        Assert.Equal((first.Skill, first.Aggression), (second.Skill, second.Aggression));
        Assert.InRange(first.Skill, Opponent.MinSkill, Opponent.MaxSkill);

        // A skill asked under the floor is raised to it
        Assert.InRange(new OpponentGenerationService(new Random(7)).GenerateOpponent("Gus", 25, Gender.Male, 10).Skill, Opponent.MinSkill, Opponent.MaxSkill);
    }

    [Fact]
    public void A_racer_grows_the_same_way_for_the_same_dice_and_within_the_skill_range()
    {
        Opponent Grown(int seed)
        {
            var racer = new Opponent("Gus", 25, Gender.Male, Opponent.MaxSkill, 50);
            var evolution = new OpponentEvolutionService(new Random(seed));
            for (var i = 0; i < 20; i++) evolution.ApplyWinEvolution(racer, isDominantWin: true);
            return racer;
        }

        var first = Grown(3);
        var second = Grown(3);
        Assert.Equal((first.Skill, first.Aggression), (second.Skill, second.Aggression));
        Assert.Equal(Opponent.MaxSkill, first.Skill);

        var loser = new Opponent("Lou", 25, Gender.Male, Opponent.MinSkill, 50);
        var evolution = new OpponentEvolutionService(new Random(4));
        for (var i = 0; i < 20; i++)
        {
            evolution.ApplyLossEvolution(loser, isBadlyBeaten: true);
            evolution.ApplyCrashEvolution(loser);
        }

        Assert.InRange(loser.Skill, Opponent.MinSkill, Opponent.MaxSkill);
    }

    [Fact]
    public void The_same_dice_run_the_same_day_of_rivals_races_and_both_cars_of_a_race_are_worn()
    {
        static (GameState State, Opponent A, Opponent B) Street()
        {
            var state = GameState.CreateNew("P");
            var a = new Opponent("Ann", 30, Gender.Female, 95, 50) { Money = 1000m, Status = RacerStatus.ReadyToRace };
            a.Cars.Add(new Car("car_a") { PowerHp = 220, EngineHealth = 1, TransmissionHealth = 1, BodyCondition = 1, TireCondition = 1 });
            var b = new Opponent("Bob", 30, Gender.Male, 95, 50) { Money = 1000m, Status = RacerStatus.ReadyToRace };
            b.Cars.Add(new Car("car_b") { PowerHp = 210, EngineHealth = 1, TransmissionHealth = 1, BodyCondition = 1, TireCondition = 1 });
            state.Racers.AddRacer(a);
            state.Racers.AddRacer(b);
            return (state, a, b);
        }

        // 0.05 accepts every challenge
        var (first, a1, b1) = Street();
        var day1 = new RaceSimulatorService(new FakeMarket(1000m), random: new FixedRandom(0.05)).SimulateDay(first, first.Date);
        var (second, _, _) = Street();
        var day2 = new RaceSimulatorService(new FakeMarket(1000m), random: new FixedRandom(0.05)).SimulateDay(second, second.Date);

        var race = Assert.Single(day1.Races);
        Assert.Equal((race.WinnerName, race.RaceType, race.Prize), (day2.Races[0].WinnerName, day2.Races[0].RaceType, day2.Races[0].Prize));
        Assert.True(a1.Cars.Concat(b1.Cars).All(c => c.OdometerKM > 0));
        Assert.Equal(1, a1.Stats.Races);
        Assert.Equal(1, b1.Stats.Races);
    }
}

/// <summary>The fixes that set the AC folder in use: the police cars, the event opponents</summary>
[Collection(GlobalsCollection.Name)]
public class Audit1025CGlobalsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _acBefore = AppSettings.Instance.AssettoCorsaPath;

    public Audit1025CGlobalsTests() => TestLogging.SilenceLogging();

    public void Dispose()
    {
        AppSettings.Instance.AssettoCorsaPath = _acBefore;
        _temp.Dispose();
    }

    private string Install(string name, params (string Car, bool Police)[] cars)
    {
        foreach (var (car, police) in cars)
            _temp.File(Path.Combine(name, "content", "cars", car, "skins", "black", "ui_skin.json"),
                police ? $$"""{ "skinname": "Black", "{{PoliceCars.SkinMarker}}": true }""" : """{ "skinname": "Black" }""");
        return _temp.Combine(name);
    }

    [Fact]
    public void The_police_car_is_looked_for_again_in_a_cars_folder_changed_in_the_settings()
    {
        AppSettings.Instance.AssettoCorsaPath = Install("first", ("cruiser_one", true), ("sedan", false));
        Assert.Equal("cruiser_one", PoliceCars.Installed()?.CarId);

        AppSettings.Instance.AssettoCorsaPath = Install("second", ("cruiser_two", true));
        Assert.Equal("cruiser_two", PoliceCars.Installed()?.CarId);

        AppSettings.Instance.AssettoCorsaPath = Install("none", ("sedan", false));
        Assert.Null(PoliceCars.Installed());
    }

    private static CarDefinition Car(string id, string brand) => new() { Id = id, Name = id, Brand = brand, Status = ContentStatus.Active };

    private EventOpponentService Events(MemoryCatalog catalog, int seed = 1) =>
        new(new CarFilterService(), catalog, random: new Random(seed));

    private MemoryCatalog Installed(params CarDefinition[] cars)
    {
        var catalog = new MemoryCatalog();
        foreach (var car in cars)
        {
            catalog.UpsertCar(car);
            Directory.CreateDirectory(_temp.Combine("ac", "content", "cars", car.Id));
        }

        AppSettings.Instance.AssettoCorsaPath = _temp.Combine("ac");
        return catalog;
    }

    [Fact]
    public void An_event_takes_a_rival_whose_car_meets_its_requirements()
    {
        var catalog = Installed(Car("ford_a", "Ford"), Car("chevy_a", "Chevrolet"));
        var state = GameState.CreateNew("P");
        var chevy = new Opponent("Chet", 30, Gender.Male, 92, 50) { Status = RacerStatus.ReadyToRace };
        chevy.Cars.Add(new Car("chevy_a"));
        var ford = new Opponent("Fay", 30, Gender.Female, 92, 50) { Status = RacerStatus.ReadyToRace };
        ford.Cars.Add(new Car("ford_a"));
        state.Racers.AddRacer(chevy);
        state.Racers.AddRacer(ford);

        var picked = Events(catalog).GetOpponentForEvent(new RaceEventDefinition { EntryRequirements = new BrandFilter("Ford") }, state);

        Assert.NotNull(picked);
        Assert.True(picked!.IsPoolOpponent);
        Assert.Same(ford, picked.PoolOpponent);
        Assert.Equal("ford_a", picked.CarDefinitionId);
    }

    [Fact]
    public void An_event_with_no_rival_to_take_gets_a_made_up_opponent_in_a_car_that_meets_it_and_a_pink_slip_none()
    {
        var catalog = Installed(Car("ford_a", "Ford"), Car("chevy_a", "Chevrolet"));
        var state = GameState.CreateNew("P");
        var ford = new RaceEventDefinition { EntryRequirements = new BrandFilter("Ford") };

        var first = Events(catalog, 5).GetOpponentForEvent(ford, state);
        var again = Events(catalog, 5).GetOpponentForEvent(ford, state);

        Assert.NotNull(first);
        Assert.False(first!.IsPoolOpponent);
        Assert.Equal("ford_a", first.CarDefinitionId);
        Assert.InRange(first.Skill, Opponent.MinSkill, Opponent.MaxSkill);
        Assert.Equal((first.OpponentName, first.Skill, first.Aggression), (again!.OpponentName, again.Skill, again.Aggression));

        // Nobody's garage to take the player's car into
        Assert.Null(Events(catalog).GetOpponentForEvent(new RaceEventDefinition { IsPinkSlip = true }, state));

        // A boss races at his own table only, and a car no longer installed cannot race
        var boss = new Opponent("Big", 50, Gender.Male, 99, 50) { Status = RacerStatus.ReadyToRace, IsBoss = true };
        boss.Cars.Add(new Car("ford_a"));
        var gone = new Opponent("Gil", 30, Gender.Male, 92, 50) { Status = RacerStatus.ReadyToRace };
        gone.Cars.Add(new Car("not_installed"));
        state.Racers.AddRacer(boss);
        state.Racers.AddRacer(gone);
        Assert.False(Events(catalog).GetOpponentForEvent(ford, state)!.IsPoolOpponent);
    }
}
