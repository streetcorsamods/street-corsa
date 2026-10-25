using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.CarLot;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.News;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Simulation;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>The fixes of the 2026-09-29 audit in the game's services (slice B)</summary>
public class AuditFixesBTests
{
    public AuditFixesBTests() => TestLogging.SilenceLogging();

    // ----- a stripped car is not worth a whole one: a car is worth its body and the parts on it -----

    /// <summary>A real Mopar 340 and real running gear on every corner, all of it on</summary>
    private static Car Whole()
    {
        var car = TestCars.WithRealEngine(TestCars.Catalog.Value);
        car.HasRunningGearAssigned = true;
        for (var corner = 0; corner < RunningGear.Corners; corner++)
        {
            var rim = new PartInstance("rims/test/mag_15x5") { ParentSlot = RunningGear.WheelSlot(corner) };
            rim.Children.Add(new PartInstance("tyres/test/slick_15x11") { ParentSlot = RunningGear.TyreSlotOnRim });
            car.Parts.Add(rim);
            car.Parts.Add(new PartInstance("brakes/stock/11_2_inch_4_piston_disc_brake_no_1") { ParentSlot = RunningGear.BrakeSlot(corner) });
            car.Parts.Add(new PartInstance("suspension/stock/409_lb_in_coil_spring_8_5_in") { ParentSlot = RunningGear.SpringSlot(corner) });
            car.Parts.Add(new PartInstance("suspension/stock/firm_shock_absorber_20_0_lb_s_in_no_2") { ParentSlot = RunningGear.ShockSlot(corner) });
        }

        return car;
    }

    /// <summary>A model of $10,000 new: $3,000 its body, $6,000 its factory engine, $1,000 its running gear</summary>
    private static CarProfile Model() => new()
    {
        CarDefinitionId = "car_a", BasePrice = 10000m, BodyPrice = 3000m, EnginePrice = 6000m, RunningGearPrice = 1000m
    };

    private static decimal Worth(PartInstance part) => (decimal)PartPricing.WorthOfAssembly(TestCars.Catalog.Value, part);

    [Fact]
    public void A_car_is_worth_its_body_and_every_part_on_it()
    {
        var catalog = TestCars.Catalog.Value;
        var whole = Whole();
        Assert.Equal(CarValuation.RoundToHundred(3000m + whole.Parts.Sum(Worth)), CarValuation.ValueOf(whole, Model(), catalog));

        // A car from an older save was never given its parts: it is worth the factory's, in its shape
        Assert.Equal(10000m, CarValuation.ValueOf(new Car("car_a"), Model(), catalog));
    }

    [Fact]
    public void A_listing_is_priced_with_the_factory_parts_until_its_engine_takes_their_place()
    {
        var catalog = TestCars.Catalog.Value;
        Assert.Equal(10000m, CarValuation.ListingValue(Model(), 1, 1m, 1m));
        Assert.Equal(6500m, CarValuation.ListingValue(Model(), 0.5, 1m, 1m));

        // The engine it turns out to have, against the factory one in the car's shape
        var engine = Whole().Engine!;
        Assert.Equal(Worth(engine) - 6000m, CarValuation.EngineWorthOver(catalog, engine, Model(), 1));
        Assert.Equal(Worth(engine) - 3900m, CarValuation.EngineWorthOver(catalog, engine, Model(), 0.5));
    }

    [Fact]
    public void A_car_with_its_engine_on_the_stand_is_worth_its_engine_less()
    {
        var catalog = TestCars.Catalog.Value;
        var whole = Whole();
        var stripped = Whole();
        stripped.Parts.RemoveAll(p => p.ParentSlot == PartInstance.CarEngineSlot);

        // The car's own figures still say the engine is fine: it is the parts on the car that count
        Assert.Equal(whole.EngineHealth, stripped.EngineHealth);
        Assert.Equal(CarValuation.RoundToHundred(3000m + stripped.Parts.Sum(Worth)), CarValuation.ValueOf(stripped, Model(), catalog));
        Assert.True(CarValuation.ValueOf(stripped, Model(), catalog) < CarValuation.ValueOf(whole, Model(), catalog) - Worth(whole.Engine!) / 2);
    }

    [Fact]
    public void A_car_on_blocks_loses_its_wheels_worth_and_never_goes_below_nothing()
    {
        var catalog = TestCars.Catalog.Value;
        var onBlocks = Whole();
        onBlocks.Parts.RemoveAll(p => RunningGear.IsWheelSlot(p.ParentSlot));
        Assert.True(CarValuation.ValueOf(onBlocks, Model(), catalog) < CarValuation.ValueOf(Whole(), Model(), catalog));

        // A bare shell is its body
        var shell = Whole();
        shell.Parts.Clear();
        Assert.Equal(3000m, CarValuation.ValueOf(shell, Model(), catalog));
        Assert.True(CarValuation.ValueOf(shell, Model(), catalog) < CarValuation.ValueOf(onBlocks, Model(), catalog));
    }

    [Fact]
    public void The_dealer_quotes_a_stripped_car_below_a_whole_one()
    {
        var profiles = new MemoryProfiles();
        profiles.Profiles["car_a"] = Model();
        var market = new UsedCarMarketService(new MemoryCatalog(), profiles, new CatalogParts());
        var sale = new CarSaleService(market, null!, null!);
        var state = GameState.CreateNew("P");

        var stripped = Whole();
        stripped.Parts.RemoveAll(p => p.ParentSlot == PartInstance.CarEngineSlot);

        Assert.True(sale.QuoteDealer(state, stripped).Amount < sale.QuoteDealer(state, Whole()).Amount);
    }

    private static MemoryProfiles Profiles(decimal basePrice)
    {
        var profiles = new MemoryProfiles();
        profiles.Profiles["car_a"] = new CarProfile { CarDefinitionId = "car_a", BasePrice = basePrice };
        return profiles;
    }

    // ----- skill on the 90-100 scale -----

    [Fact]
    public void Skill_is_weighed_from_the_floor_of_ninety_to_a_hundred()
    {
        Assert.Equal(0, OpponentRules.SkillShare(Opponent.MinSkill));
        Assert.Equal(0.5, OpponentRules.SkillShare(95));
        Assert.Equal(1, OpponentRules.SkillShare(Opponent.MaxSkill));
        Assert.Equal(0, OpponentRules.SkillShare(50));
        Assert.Equal(0, OpponentRules.SkillShare(double.NaN));
    }

    // ----- the rival of a race not settled yet -----

    private static Opponent Racer(GameState state, string name, RacerStatus status, params Car[] cars)
    {
        var racer = new Opponent(name, 30, Gender.Female, 92, 50) { Money = 5000m, Status = status };
        racer.Cars.AddRange(cars);
        state.Racers.AddRacer(racer);
        return racer;
    }

    [Fact]
    public void The_rival_of_a_race_not_settled_yet_is_not_raced_by_the_others()
    {
        var state = GameState.CreateNew("P");
        Racer(state, "Pat", RacerStatus.ReadyToRace, new Car("car_a") { PowerHp = 200 });
        Racer(state, "Ann", RacerStatus.ReadyToRace, new Car("car_b") { PowerHp = 200 });
        state.PendingRace = new RaceContext { OpponentName = "Pat" };

        var simulator = new RaceSimulatorService(new FakeMarket(1000m));
        for (var day = 0; day < 20; day++)
            Assert.Equal(0, simulator.SimulateDay(state, state.Date.AddDays(day)).TotalRaces);
    }

    [Fact]
    public async Task The_rival_of_a_race_not_settled_yet_keeps_their_cars_and_nobody_buys_their_spare()
    {
        var state = GameState.CreateNew("P");
        var driven = new Car("car_a") { PowerHp = 200 };
        var spare = new Car("car_b") { PowerHp = 150 };
        var third = new Car("car_c") { PowerHp = 100 };
        var pat = Racer(state, "Pat", RacerStatus.ReadyToRace, driven, spare, third);
        RivalCarAds.Post(state, pat, spare, 500m, state.Date);
        var ann = Racer(state, "Ann", RacerStatus.Retired);
        state.PendingRace = new RaceContext { OpponentName = "Pat", OpponentCarInstanceId = driven.InstanceId };

        var life = new OpponentLifeService(new NoParts(), new FakeMarket(1000m), new MemoryCatalog(), random: new FixedRandom(0.99));
        await life.ReviewDayAsync(state, state.Date);

        Assert.Equal([driven, spare, third], pat.Cars);
        Assert.DoesNotContain(spare, ann.Cars);
        Assert.NotEqual(RacerStatus.Departed, pat.Status);
    }

    [Fact]
    public void The_rival_of_a_race_not_settled_yet_does_not_answer_the_players_ad()
    {
        var state = GameState.CreateNew("P");
        Racer(state, "Pat", RacerStatus.ReadyToRace);
        state.PendingRace = new RaceContext { OpponentName = "Pat" };
        var forSale = new Car("car_a") { PowerHp = 300 };

        // Pat is the only rival with the money and room, and a roll of 0 always brings a rival buyer
        Assert.Null(CarSaleService.RivalBuyer(state, forSale, 1000m, new FixedRandom(0)));

        state.PendingRace = null;
        Assert.Equal("Pat", CarSaleService.RivalBuyer(state, forSale, 1000m, new FixedRandom(0))?.Name);
    }

    [Fact]
    public void An_ad_found_earlier_stands_only_while_the_seller_still_has_the_car_spare()
    {
        var state = GameState.CreateNew("P");
        var driven = new Car("car_a");
        var spare = new Car("car_b");
        var pat = Racer(state, "Pat", RacerStatus.ReadyToRace, driven, spare);
        RivalCarAds.Post(state, pat, spare, 500m, state.Date);
        var offer = Assert.Single(RivalCarAds.Live(state));

        Assert.True(RivalCarAds.StillStands(state, offer));

        // Now the car they drive
        pat.Cars.Reverse();
        Assert.False(RivalCarAds.StillStands(state, offer));

        pat.Cars.Reverse();
        state.NewspaperAds.RivalCars.Clear();
        Assert.False(RivalCarAds.StillStands(state, offer));
    }

    // ----- the day's words, whatever fails after them -----

    [Fact]
    public void A_change_is_announced_in_the_paper_and_the_street_from_its_story()
    {
        var talk = new List<string>();
        var news = new List<NewsArticle>();
        var date = GameState.GetStartingDateTime();
        var values = new TextValues { ["dealer"] = "Joe's", ["region"] = "Downtown", ["blurb"] = "", ["discount"] = 0, ["day"] = date };

        DealerChurn.Announce("dealer_opened", 20, values, date, new Random(1), talk, news);

        var article = Assert.Single(news);
        Assert.Equal(20, article.Weight);
        Assert.Equal(date, article.Date);
        Assert.NotEmpty(article.Headline);
        Assert.NotEmpty(Assert.Single(talk));

        var state = GameState.CreateNew("P");
        DealerChurn.Tell(state, date, talk, news);
        Assert.Contains(state.News, a => a.Headline == article.Headline);
        Assert.Contains(state.StreetTalk, t => t.Text == talk[0]);
    }

    // ----- a dealer whose mod is gone takes its cars with it -----

    [Fact]
    public void A_dealer_no_longer_defined_takes_the_cars_on_its_lot_with_it_and_keeps_its_sales()
    {
        var state = GameState.CreateNew("P");
        state.DealerLocations = [new DealerLocation { Id = "open_0" }, new DealerLocation { Id = "gone" }];
        var kept = new UsedCarListing { CarDefinitionId = "car_a", DealerLocation = "open_0" };
        var stranded = new UsedCarListing { CarDefinitionId = "car_a", DealerLocation = "gone", PriceBeforeSale = 9000m };
        var sold = new UsedCarListing { CarDefinitionId = "car_a", DealerLocation = "gone", IsSold = true, SoldDate = state.Date };
        state.UsedCarMarket.AddRange([kept, stranded, sold]);

        DealerRoster.Sync(state, FakeDealers.Named(open: 2, waiting: 0));

        Assert.Contains(kept, state.UsedCarMarket);
        Assert.Contains(sold, state.UsedCarMarket);
        Assert.DoesNotContain(stranded, state.UsedCarMarket);
    }

    // ----- a mod's scene files -----

    [Fact]
    public void A_street_with_a_null_light_or_lamp_loads_without_them()
    {
        using var dir = new TempDir();
        dir.File("s/street_day.kn5", "");
        dir.File("s/street.json", """
            {
              "id": "s", "player": { "x": 0, "z": 0, "heading": 0 }, "laneOffset": 3, "approachFrom": 50, "leaveTo": 50,
              "times": [ "day", "7", "1", null ],
              "lights": { "night": null, "7": { "brightness": 1 }, "day": { "brightness": 2, "lamps": [ null, { "x": 1, "y": 2, "z": 3, "brightness": 4, "range": 5 } ] } }
            }
            """);

        var street = Assert.IsType<StreetScene>(StreetScenes.Load(dir.Combine("s")));

        Assert.Equal([StreetLight.Day], street.Times);
        Assert.Equal([StreetLight.Day], street.Lights.Keys);
        Assert.Equal(4f, Assert.Single(street.Lights[StreetLight.Day].Lamps).Brightness);
        Assert.Same(street.Lights[StreetLight.Day], street.LightingFor(StreetLight.Night));
    }

    [Fact]
    public void A_light_of_the_day_is_named_never_numbered()
    {
        Assert.True(SceneLighting.TryParse("Dusk", out var dusk));
        Assert.Equal(StreetLight.Dusk, dusk);
        Assert.False(SceneLighting.TryParse("1", out _));
        Assert.False(SceneLighting.TryParse("7", out _));
        Assert.False(SceneLighting.TryParse(null, out _));
    }

    [Fact]
    public void A_station_whose_model_is_outside_its_folder_is_shown_without_it_and_a_null_light_is_skipped()
    {
        using var dir = new TempDir();
        dir.File("outside.kn5", "");
        dir.File("st/station.json", """
            { "id": "st", "model": "..\\outside.kn5", "arrive": [[0, 10], [0, 0]], "leave": [[0, 0], [1, 1]], "lights": { "day": null } }
            """);

        var station = Assert.IsType<StationScene>(StationScenes.Load(dir.Combine("st")));

        Assert.False(station.HasModel);
        Assert.Equal(string.Empty, station.Kn5);
        Assert.Empty(station.Lights);
        Assert.Same(SceneLighting.Default, station.LightingFor(StreetLight.Day));
    }

    [Fact]
    public void A_warehouse_whose_model_is_outside_its_folder_is_left_out_and_a_picture_path_is_dropped()
    {
        using var dir = new TempDir();
        dir.File("outside.kn5", "");
        dir.File("away/warehouse.json", """{ "model": "../outside.kn5", "bays": [ { "x": 0, "z": 0, "heading": 0 } ] }""");
        Assert.Null(WarehouseScenes.Load(dir.Combine("away")));

        dir.File("wh/warehouse.kn5", "");
        dir.File("wh/warehouse.json", """
            {
              "bays": [ { "x": 0, "z": 0, "heading": 0 } ],
              "lamps": [ { "x": 0, "y": 5, "z": 0, "brightness": 2, "range": 10 } ],
              "lights": { "night": { "brightness": 0.2, "lampScale": 3 } },
              "skylight": { "2": 0.5, "day": 1 },
              "looks": [ { "surfaces": { "wh_wall": "..\\..\\secret.dds", "wh_floor": "C:\\floor.jpg", "wh_door": "door.jpg" } } ],
              "digits": { "tens": "t", "units": "u", "picture": "../digit_{0}.jpg" }
            }
            """);

        var scene = Assert.IsType<WarehouseScene>(WarehouseScenes.Load(dir.Combine("wh")));

        Assert.Equal(new Dictionary<string, string> { ["wh_door"] = "door.jpg" }, Assert.Single(scene.Looks).Surfaces);
        Assert.Null(scene.DigitPicture);
        Assert.Equal([StreetLight.Day], scene.Skylight.Keys);
        var lamp = Assert.Single(scene.Lights[StreetLight.Night].Lamps);
        Assert.Equal(6f, lamp.Brightness);
        Assert.Equal(0f, scene.Lights[StreetLight.Night].CubemapAmbient);
    }

    [Fact]
    public void A_ground_file_that_asks_for_more_heights_than_it_holds_is_flat()
    {
        using var dir = new TempDir();
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("SCG1"u8);
            writer.Write(0.5f);
            writer.Write(10000);
            writer.Write(10000);
            writer.Write(-10f);
            writer.Write(-10f);
        }

        var file = dir.Bytes("ground.bin", bytes.ToArray());
        Assert.Same(StationGround.Flat, StationGround.Load(file));
    }

    // ----- the catalog -----

    [Fact]
    public void A_mod_car_of_absurd_figures_is_priced_not_thrown_out()
    {
        var service = new CarProfileService(new MemoryCatalog(), new MemoryProfiles());
        var car = new CarDefinition
        {
            Id = "rocket", Name = "Rocket", Brand = "Plymouth", ContentHash = "hash",
            Specs = new CarSpecsData { Bhp = "9999999999999999999999bhp", Weight = "0.000001kg" }
        };

        // A weight no car has is no weight: the reference body
        Assert.Equal(2500m, service.CalculateBasePrice(car));
    }

    [Fact]
    public void Every_reply_a_rival_gives_is_in_the_base_game_text()
    {
        foreach (var key in new[] { "replies.always_accept", "replies.car_laid_up", "replies.car_unknown",
                     "replies.boss.pink_slips", "replies.boss.no_pink_slip", "replies.rematch" })
        {
            Assert.True(GameText.Installed.Has(key), key);
        }

        Assert.Equal("Pat: You're on! Let's race!", GameText.Installed.Pick("replies.always_accept", new Random(1), new TextValues { ["name"] = "Pat" }));
    }
}
