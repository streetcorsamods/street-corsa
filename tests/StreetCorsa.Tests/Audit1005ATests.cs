using System.Numerics;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Race;
using StreetCorsa.Services.Race.Validation;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-05 audit's fixes to the economy, the race and race.ini: the paper's buyers (haggling, a car just bought,
/// a car stripped after the call), used parts never under a shop's buying price, scores and lap times that cannot be
/// shown, text formats a mod got wrong, the sun of a sunset the rules put out of reach, names from a car's files that
/// would break race.ini, the read-only mark left to SafeFile and the can of gas at the pump's price
/// </summary>
public sealed class Audit1005ATests : IDisposable
{
    private readonly TempDir _temp = new();

    public Audit1005ATests() => TestLogging.SilenceLogging();

    public void Dispose() => _temp.Dispose();

    // ----- The paper's buyers -----

    private static (CarSaleService Service, FakeMarket Market, GameState State, Car Car, CarSaleAd Ad) PaperSale(decimal offer, decimal valueAtOffer)
    {
        var market = new FakeMarket(valueAtOffer);
        var service = new CarSaleService(market, new QuietTime(), new RaceResultProcessorTests.FakeRepository());
        var state = GameState.CreateNew("P");
        var car = new Car("car_a") { InstanceId = Guid.NewGuid() };
        state.Player.Cars.Add(car);
        var ad = new CarSaleAd
        {
            CarInstanceId = car.InstanceId, AskingPrice = offer, PostedDate = state.Date,
            Offer = new CarOffer { Id = Guid.NewGuid(), BuyerName = "a kid from the high school", Amount = offer, ValueAtOffer = valueAtOffer, Expires = state.Date.AddDays(1) }
        };
        state.NewspaperAds.PlayerCars.Add(ad);
        return (service, market, state, car, ad);
    }

    [Fact]
    public async Task A_car_stripped_after_the_call_gets_an_offer_cut_to_match_and_sells_at_it()
    {
        var (service, market, state, car, ad) = PaperSale(1800m, 2000m);
        ad.Offer!.FirstAmount = 1800m;
        market.Value = 1200m; // the engine came out since

        var lowered = await service.AcceptOfferAsync(state, ad);

        Assert.Equal(SaleOutcome.NoOffer, lowered.Outcome);
        Assert.Contains(car, state.Player.Cars);
        Assert.Equal(1080m, ad.Offer!.Amount);
        Assert.Equal(1080m, ad.Offer.FirstAmount);
        Assert.Equal(1200m, ad.Offer.ValueAtOffer);

        var money = state.Player.Money;
        Assert.True((await service.AcceptOfferAsync(state, ad)).Succeeded);
        Assert.Equal(money + 1080m, state.Player.Money);
        Assert.DoesNotContain(car, state.Player.Cars);
    }

    [Fact]
    public async Task A_buyer_walks_away_from_a_shell_and_an_offer_on_an_untouched_car_stands()
    {
        var (service, market, state, car, ad) = PaperSale(1800m, 2000m);
        market.Value = 50m;

        Assert.Equal(SaleOutcome.NoOffer, (await service.AcceptOfferAsync(state, ad)).Outcome);
        Assert.Null(ad.Offer);
        Assert.Contains(car, state.Player.Cars);

        // Worth more since (a part put on), or an offer from before the worth was kept: paid as offered
        var (moreService, moreMarket, moreState, _, moreAd) = PaperSale(1800m, 2000m);
        moreMarket.Value = 2500m;
        var money = moreState.Player.Money;
        Assert.True((await moreService.AcceptOfferAsync(moreState, moreAd)).Succeeded);
        Assert.Equal(money + 1800m, moreState.Player.Money);
    }

    [Fact]
    public void A_buyer_who_calls_keeps_what_the_car_was_worth()
    {
        var market = new FakeMarket(2000m);
        var service = new CarSaleService(market, new QuietTime(), new RaceResultProcessorTests.FakeRepository());
        var state = GameState.CreateNew("P");
        var car = new Car("car_a") { InstanceId = Guid.NewGuid() };
        state.Player.Cars.Add(car);
        var ad = new CarSaleAd { CarInstanceId = car.InstanceId, AskingPrice = 1500m, PostedDate = state.Date };
        state.NewspaperAds.PlayerCars.Add(ad);

        var random = new Random(1);
        for (var day = 1; day <= 60 && ad.Offer == null; day++) service.ReviewAds(state, state.Date.AddDays(day % CarSaleService.AdDays), random);

        Assert.Equal(2000m, ad.Offer!.ValueAtOffer);
    }

    [Theory]
    [InlineData(1800, 2000, 3, 0.3)]
    [InlineData(1800, 2000, 13.9, 0.3)]
    [InlineData(1800, 2000, 14, 0.6)]
    [InlineData(2000, 2000, 1, 0.24)]
    public void A_car_just_bought_draws_half_the_callers(int asking, int value, double daysOwned, double chance) =>
        Assert.Equal(chance, CarSaleService.OfferChance(asking, value, daysOwned), 6);

    [Fact]
    public void The_days_owned_count_from_the_last_change_of_hands()
    {
        var car = new Car("car_a") { PurchaseDate = new DateTime(1970, 1, 1) };
        Assert.Equal(new DateTime(1970, 1, 1), CarSaleService.OwnedSince(car));

        car.History.ChangeHands("Earl", new DateTime(1970, 2, 1), CarAcquisition.PrivateSale);
        car.History.ChangeHands("P", new DateTime(1970, 3, 1), CarAcquisition.PinkSlip);
        Assert.Equal(new DateTime(1970, 3, 1), CarSaleService.OwnedSince(car));
    }

    [Fact]
    public void Every_ask_is_haggled_never_over_it_nor_under_four_fifths_of_the_worth()
    {
        const decimal worth = 2000m;
        var random = new Random(5);
        for (var asking = 100m; asking <= 6000m; asking += 50m)
        {
            for (var i = 0; i < 20; i++)
            {
                var offer = CarSaleService.OfferAmount(asking, worth, random.NextDouble());
                Assert.InRange(offer, Math.Min(asking, worth * 0.8m), asking);
                Assert.True(offer >= Math.Min(asking, Math.Max(worth * 0.8m, asking * 0.85m - 10m)), $"{offer} for {asking}");
            }
        }

        // A roll that is no number is the hardest haggle: 85% of 1,800, held at four fifths of the worth
        Assert.Equal(1600m, CarSaleService.OfferAmount(1800m, worth, double.NaN));
    }

    [Fact]
    public void A_flip_through_the_paper_leaves_a_thin_margin_at_most()
    {
        // Bought from a private ad at its average (privatePriceShare of the worth) and put straight back in the paper at
        // the worth: what the average buyer offers is little over what was paid, and it takes twice as long to come
        var worth = 2000m;
        var paid = worth * RuleBook.Current.Market.PrivatePriceShare;
        var offers = Enumerable.Range(0, 101).Select(i => CarSaleService.OfferAmount(worth, worth, i / 100.0)).ToList();

        Assert.True(offers.Average() - paid <= worth * 0.1m, $"{offers.Average() - paid} on a ${worth} car");
        Assert.Equal(CarSaleService.OfferChance(worth, worth) / 2, CarSaleService.OfferChance(worth, worth, 0), 6);
    }

    [Theory]
    [InlineData(1800, 2000, 1200, 1080)]
    [InlineData(1800, 2000, 2000, 1800)]
    [InlineData(1800, 2000, 3000, 1800)]
    [InlineData(1800, 0, 100, 1800)]
    [InlineData(1800, 2000, -5, 0)]
    public void An_offer_goes_down_with_the_cars_worth_and_never_up(int amount, int then, int now, int lowered) =>
        Assert.Equal((decimal)lowered, CarSaleService.LoweredOffer(amount, then, now));

    [Fact]
    public void The_paper_buyers_figures_out_of_reach_are_the_base_games_told()
    {
        var merged = ModMerge.Parse(RuleBook.Installed.ToJson())!;
        var market = (JsonObject)merged["market"]!;
        market["paperHaggleShare"] = 1.5;
        market["paperFloorShare"] = -0.2;
        market["paperFreshChanceFactor"] = 2;
        market["paperFreshDays"] = -3;
        var economy = (JsonObject)merged["economy"]!;
        economy["rivalPartMinAskShare"] = 1.2;
        economy["rivalPartMaxAskShare"] = 0.9;
        var problems = new List<string>();

        var book = RuleBook.Read(merged, problems);

        var fresh = new MarketRules();
        Assert.Equal(fresh.PaperHaggleShare, book.Market.PaperHaggleShare);
        Assert.Equal(fresh.PaperFloorShare, book.Market.PaperFloorShare);
        Assert.Equal(fresh.PaperFreshChanceFactor, book.Market.PaperFreshChanceFactor);
        Assert.Equal(fresh.PaperFreshDays, book.Market.PaperFreshDays);
        Assert.Equal((0.9, 1.2), (book.Economy.RivalPartMinAskShare, book.Economy.RivalPartMaxAskShare));
        Assert.Equal(5, problems.Count);
    }

    // ----- Used parts: never under what a shop pays, at every difficulty -----

    [Theory]
    [InlineData(Difficulty.Easy)]
    [InlineData(Difficulty.Normal)]
    [InlineData(Difficulty.Hard)]
    public async Task No_used_part_in_the_paper_asks_less_than_any_shop_pays_for_it(Difficulty difficulty)
    {
        // A setting's shops (the core has none): one of each kind that sells
        using var temp = new TempDir();
        temp.File("streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        temp.File("streetcorsa/partshops/shops.json", """
            { "shops": [
              { "id": "makers", "name": "Makers", "kind": "specialist", "makes": [ "gm", "ford", "mopar" ], "mapX": 0.2, "mapY": 0.2, "travelHours": 1 },
              { "id": "speed", "name": "Speed", "kind": "speed", "mapX": 0.4, "mapY": 0.2, "travelHours": 1 },
              { "id": "tyres", "name": "Tyres", "kind": "wheels", "mapX": 0.6, "mapY": 0.2, "travelHours": 1 },
              { "id": "wreckers", "name": "Wreckers", "kind": "junkyard", "mapX": 0.8, "mapY": 0.2, "travelHours": 1 }
            ] }
            """);
        var shops = new PartShopCatalog(ModSet.Load(temp.Path));
        var parts = new CatalogParts();
        var paper = new NewspaperPartsService(parts, shops);
        var shopService = new PartShopService(shops, parts, paper);
        var state = GameState.CreateNew("P");
        state.Rules = GameRules.For(difficulty);
        PartShopRoster.Sync(state, shops);
        var seller = new Opponent("Sal", 30, Gender.Male, 92, 50);
        state.Racers.AddRacer(seller);

        for (var i = 0; i < 4; i++) await paper.RefreshAdsAsync(state, state.Date);
        foreach (var definition in parts.Catalog.Parts.Values.Where(p => p.IsRealPart).Take(400))
        {
            // The cheapest a rival ever asks (roll 0), in every shape
            foreach (var wear in new[] { 0.0, 0.5, 1.0 })
                RivalPartAds.Post(state, parts.Catalog, seller, new PartInstance(definition.Id) { Wear = wear }, state.Date, 0, shops.All);
        }

        var locations = shops.All.Select(s => PartShopRoster.Find(state, s.Id)!).ToList();
        Assert.Contains(locations, l => shops.Get(l.Id)!.KindRules.Yard);
        Assert.True(state.NewspaperAds.Parts.Count > 100);
        foreach (var ad in state.NewspaperAds.Parts)
        {
            var paid = locations.Select(l => shopService.BuyingPrice(l, ad.Part)).Append(paper.TradeInPrice(ad.Part)).Max();
            Assert.True(ad.AskingPrice > paid, $"{ad.Part.DefinitionId} asked ${ad.AskingPrice}, a shop pays ${paid} ({difficulty})");
        }
    }

    [Fact]
    public void The_most_a_shop_pays_counts_a_shops_own_figure()
    {
        var generous = new PartShopDefinition { Id = "gen", Name = "Generous", Kind = "speed", BuyFactor = 0.7 };
        Assert.Equal(0.42, Services.PartShops.PartShopRules.MostPaidFactor(), 6);
        Assert.Equal(0.7, Services.PartShops.PartShopRules.MostPaidFactor([generous]), 6);
    }

    // ----- Scores and lap times -----

    private async Task<ValidationResult> ValidateWith(Action<JObject> change)
    {
        var json = RaceResultValidatorTests.Fixture("1.1", "P", "O");
        change((JObject)json["participants"]![0]!);
        return await new RaceResultValidator().ValidateFileAsync(_temp.File(Guid.NewGuid() + ".json", json.ToString()));
    }

    [Fact]
    public async Task A_score_or_a_lap_time_no_card_could_show_is_refused_before_anything_is_applied()
    {
        Assert.True((await ValidateWith(p => { p["score"] = 61000.5; p["lap_times_ms"] = new JArray(61000.5, 62000); })).IsValid);
        Assert.False((await ValidateWith(p => p["score"] = 1e300)).IsValid);
        Assert.False((await ValidateWith(p => p["score"] = "NaN")).IsValid);
        Assert.False((await ValidateWith(p => p["lap_times_ms"] = new JArray(61000.5, 1e20))).IsValid);
        Assert.False((await ValidateWith(p => p["lap_times_ms"] = new JArray(-1))).IsValid);
    }

    [Theory]
    [InlineData(double.NaN, "0:00.000")]
    [InlineData(double.PositiveInfinity, "0:00.000")]
    [InlineData(-5, "0:00.000")]
    [InlineData(1e300, "16666666:40.000")]
    public void A_lap_time_shows_whatever_it_is(double ms, string shown) =>
        Assert.Equal(shown, RaceResultProcessor.LapTime(ms).Replace(Locale.Culture.NumberFormat.NumberDecimalSeparator, "."));

    // ----- Text a mod got wrong -----

    [Fact]
    public void A_format_a_value_cannot_take_leaves_its_placeholder_and_the_rest_is_said()
    {
        var values = new Dictionary<string, object?> { ["amount"] = 12.5, ["name"] = "Earl" };

        Assert.Equal("{amount:D} for Earl", GameText.Fill("{amount:D} for {name}", values));
        Assert.Equal("{when:%} by Earl", GameText.Fill("{when:%} by {name}", new Dictionary<string, object?> { ["when"] = DateTime.Now, ["name"] = "Earl" }));
    }

    [Fact]
    public void A_race_text_key_that_would_break_race_ini_is_left_out()
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("Content/streetcorsa/text/race.json", """
            {
              "lines": {
                "race.result.win": "You won",
                "race.bad\r\n[RACE]\r\nCARS": "9",
                "race.semi;colon": "x",
                "race.equals=sign": "y"
              }
            }
            """);
        var text = GameText.Load(ModSet.Load(_temp.Combine("Content")));

        Assert.Equal(4, text.Keys.Count());
        var lines = RaceText.IniLines(text).ToList();

        Assert.Contains("RESULT_WIN=You won", lines);
        Assert.Equal("[" + RaceText.Section + "]", Assert.Single(lines, l => l.StartsWith('[')));
        Assert.DoesNotContain(lines, l => l.Contains(';') || l.StartsWith("SEMI", StringComparison.Ordinal) || l.StartsWith("EQUALS", StringComparison.Ordinal));
        Assert.All(lines, l => Assert.DoesNotContain('\n', l));
    }

    // ----- The sun -----

    [Theory]
    [InlineData(13.0)]
    [InlineData(24.5)]
    [InlineData(23.99)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_sunset_out_of_reach_still_puts_the_sun_somewhere(double sunset)
    {
        foreach (var hour in new[] { 12.0, 13.5, 18, 23.9 })
        {
            var acHour = SeasonalSun.AcHour(hour, sunset, 18.3);
            Assert.True(double.IsFinite(acHour), $"{hour} with the sunset at {sunset}");
            Assert.InRange(SeasonalSun.SunAngleAt(acHour), SeasonalSun.MinAngle, SeasonalSun.MaxAngle);
        }

        Assert.True(double.IsFinite(SeasonalSun.AcHour(20, 18.3, sunset)));
        Assert.Equal(IniModificationService.NoonSunAngle, SeasonalSun.SunAngleAt(double.NaN));
    }

    // ----- race.ini: names from a car's files and the rules -----

    [Fact]
    public void A_texture_or_node_name_with_a_line_break_is_left_out_of_race_ini()
    {
        var carb = new ShownModelAt(new ShownModel("carb.kn5", "carb"), Matrix4x4.Identity);
        var shown = ShownSet.Of(null, [carb], new ModelFrame("floor\r\n[RACE]\r\nCARS=9", Matrix4x4.Identity))!
            .WithPaintFiles([
                new ShownPaint(new ShownModel("paint_1.dds", "a"), "body.dds\r\n[CAR_0]\r\nSKIN=evil"),
                new ShownPaint(new ShownModel("paint_2.dds", "b"), "body_2.dds")
            ]);
        var leftOut = new List<string>();

        var keys = RaceStartState.ShownKeys(0, shown, leftOut).ToList();

        Assert.DoesNotContain(keys, k => k.Key == "CAR_0_MODEL_NODE");
        Assert.Contains(("CAR_0_PAINT_1", "paint_2.dds"), keys);
        Assert.Contains(("CAR_0_PAINT_1_TEXTURE", "body_2.dds"), keys);
        Assert.DoesNotContain(keys, k => k.Key.StartsWith("CAR_0_PAINT_2", StringComparison.Ordinal));
        Assert.Contains(("CAR_0_PART_1", "carb.kn5"), keys);
        Assert.All(keys, k => Assert.True(RaceStartState.IsIniValue(k.Value), k.Key));
        Assert.Equal(2, leftOut.Count);
    }

    [Theory]
    [InlineData(null, "4_mid_clear")]
    [InlineData("  ", "4_mid_clear")]
    [InlineData(" 3_clear ", "3_clear")]
    [InlineData("3_clear\r\n[RACE]", "4_mid_clear")]
    [InlineData("rain;", "4_mid_clear")]
    public void The_rules_weather_goes_into_race_ini_only_as_a_plain_name(string? weather, string written) =>
        Assert.Equal(written, IniModificationService.WeatherName(weather));

    // ----- The read-only mark: SafeFile's -----

    [Fact]
    public void A_kept_copy_goes_back_over_a_read_only_file_and_a_read_only_file_is_deleted()
    {
        var target = _temp.File("car/data/engine.ini", "race");
        var kept = _temp.File("keep/engine.ini", "original");
        File.SetAttributes(target, FileAttributes.ReadOnly);

        Assert.True(KeptFile.PutBack(kept, target, readOnly: true));
        Assert.Equal("original", File.ReadAllText(target));
        Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.ReadOnly));

        Assert.True(SafeFile.Delete(target));
        Assert.False(File.Exists(target));
        Assert.False(SafeFile.Delete(target));
    }

    // ----- The can of gas -----

    [Fact]
    public void The_can_of_gas_costs_what_the_pump_charges()
    {
        var dear = new StationPrices(1m, 0.65m, 3m, 25m);
        var car = new Car("car_a") { FuelLitres = 0.1, FuelTankLitres = 60 };

        var can = Assert.Single(RepairShop.Jobs(car, null, gas: dear), j => j.Id == RepairShop.CanOfGas);

        Assert.Equal(GasStation.GasPrice(dear, RepairShop.CanOfGasLitres, 1.0), can.Cost);
        Assert.True(can.Cost > Assert.Single(RepairShop.Jobs(car, null), j => j.Id == RepairShop.CanOfGas).Cost);
    }
}
