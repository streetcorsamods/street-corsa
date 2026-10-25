using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Opponents;

namespace StreetCorsa.Tests;

/// <summary>A dice that rolls what it is told, in turn, and then <paramref name="then"/>; Next(n) is always 0</summary>
internal sealed class ScriptedRandom(double then, params double[] rolls) : Random
{
    private readonly Queue<double> _rolls = new(rolls);

    public override double NextDouble() => _rolls.Count > 0 ? _rolls.Dequeue() : then;

    public override int Next(int maxValue) => 0;

    public override int Next(int minValue, int maxValue) => minValue;
}

/// <summary>Dealers as authored, without a mod folder</summary>
internal sealed class FakeDealers(params DealerDefinition[] dealers) : IDealerCatalog
{
    public IReadOnlyList<DealerDefinition> All => dealers;

    public DealerDefinition? Get(string id) => dealers.FirstOrDefault(d => d.Id == id);

    public ShowroomSpec GetShowroom(string id) => ShowroomSpec.Unknown(id);

    public DealerMapSpec? Map => null;

    public List<DealerLocation> ToLocations() => dealers.Select(d => d.ToLocation()).ToList();

    public static FakeDealers Named(int open, int waiting) => new(
    [
        .. Enumerable.Range(0, open).Select(i => new DealerDefinition { Id = $"open_{i}", Name = $"Open {i}", Region = "Town", ConditionCenter = 0.5f + i * 0.01f }),
        .. Enumerable.Range(0, waiting).Select(i => new DealerDefinition { Id = $"new_{i}", Name = $"New {i}", Region = "Valley", StartsOpen = false })
    ]);
}

public class DealerRulesTests
{
    [Fact]
    public void About_sixty_percent_of_the_dealers_are_open_and_never_fewer_than_forty()
    {
        Assert.Equal(12, DealerRules.TargetOpen(20));
        Assert.Equal(8, DealerRules.MinOpen(20));
        Assert.Equal(1, DealerRules.TargetOpen(1));
        Assert.Equal(0, DealerRules.TargetOpen(0));
    }

    [Fact]
    public void The_market_changes_toward_its_target_and_never_under_its_minimum()
    {
        Assert.Equal(DealerRules.Change.Open, DealerRules.Direction(10, 20, true, true, 0.1));
        Assert.Equal(DealerRules.Change.Close, DealerRules.Direction(14, 20, true, true, 0.9));

        // At the target a coin decides
        Assert.Equal(DealerRules.Change.Close, DealerRules.Direction(12, 20, true, true, 0.1));
        Assert.Equal(DealerRules.Change.Open, DealerRules.Direction(12, 20, true, true, 0.9));

        // Short of lots with nowhere to open, or over with nobody that may close: nothing, never away from the target
        Assert.Equal(DealerRules.Change.None, DealerRules.Direction(10, 20, false, true, 0.1));
        Assert.Equal(DealerRules.Change.None, DealerRules.Direction(14, 20, true, false, 0.9));

        // At the target the other way does, when the coin's cannot
        Assert.Equal(DealerRules.Change.Open, DealerRules.Direction(12, 20, true, false, 0.1));
        Assert.Equal(DealerRules.Change.Close, DealerRules.Direction(12, 20, false, true, 0.9));
        Assert.Equal(DealerRules.Change.None, DealerRules.Direction(12, 20, false, false, 0.9));

        // Never under the minimum
        Assert.Equal(DealerRules.Change.None, DealerRules.Direction(1, 1, false, true, 0.1));
    }

    [Fact]
    public void A_lot_that_sells_nothing_is_likelier_to_go_under_but_never_certainly()
    {
        var idle = DealerRules.CloseWeight(0, 1);
        var busy = DealerRules.CloseWeight(5, 0);
        Assert.True(idle > busy * 10);
        Assert.True(busy > 0);
        Assert.True(DealerRules.CloseWeight(0, 1) > DealerRules.CloseWeight(0, 0));
        Assert.Equal(DealerRules.CloseWeight(0, 0), DealerRules.CloseWeight(double.NaN, double.NaN));
    }

    [Fact]
    public void The_sale_takes_a_fifth_off_and_more_in_its_last_three_days()
    {
        Assert.Equal(0.20m, DealerRules.DiscountFor(6));
        Assert.Equal(0.20m, DealerRules.DiscountFor(3));
        Assert.Equal(0.35m, DealerRules.DiscountFor(2));
        Assert.Equal(0.35m, DealerRules.DiscountFor(0));
        Assert.Equal(8000m, DealerRules.SalePrice(10000m, 0.2m));
        Assert.Equal(6500m, DealerRules.SalePrice(10000m, 0.35m));
    }

    [Fact]
    public void A_sale_counts_half_a_month_later()
    {
        Assert.Equal(1, DealerRules.TradeFade(0));
        Assert.Equal(0.5, DealerRules.TradeFade(DealerRules.TradeHalfLifeDays), 6);
    }

    [Fact]
    public void Only_a_deep_cut_is_worth_buying_to_sell_on()
    {
        // A car worth $10,000: the least a racer asks for it in the paper is $7,500
        Assert.False(OpponentRules.WorthFlipping(8000m, 7500m)); // the sale's first cut
        Assert.True(OpponentRules.WorthFlipping(6500m, 7500m)); // its last days
        Assert.False(OpponentRules.WorthFlipping(0m, 7500m));
        Assert.False(OpponentRules.WorthFlipping(6500m, 0m));
    }

    [Fact]
    public void Only_a_lot_in_its_sale_has_days_left_and_a_discount()
    {
        var today = new DateTime(1971, 6, 1, 8, 0, 0);
        var closing = new DealerLocation { Status = DealerStatus.ClosingDown, ClosesOn = today.Date.AddDays(4) };
        Assert.Equal(4, DealerRules.DaysLeft(closing, today));
        Assert.Equal(DealerRules.SaleDiscount, DealerRules.DiscountOn(closing, today));
        Assert.Equal(DealerRules.FinalDiscount, DealerRules.DiscountOn(closing, today.AddDays(2)));
        Assert.Equal(0, DealerRules.DaysLeft(closing, today.AddDays(9)));

        var open = new DealerLocation { ClosesOn = today.Date.AddDays(4) };
        Assert.Equal(0, DealerRules.DaysLeft(open, today));
        Assert.Equal(0m, DealerRules.DiscountOn(open, today));
    }
}

public class DealerRosterTests
{
    [Fact]
    public void A_new_game_has_the_dealers_that_start_open_open_and_the_rest_waiting()
    {
        var state = GameState.CreateNew("P");
        DealerRoster.Sync(state, FakeDealers.Named(open: 2, waiting: 1));

        Assert.Equal(["open_0", "open_1", "new_0"], state.DealerLocations.Select(d => d.Id));
        Assert.Equal([DealerStatus.Open, DealerStatus.Open, DealerStatus.Waiting], state.DealerLocations.Select(d => d.Status));
        Assert.Equal(["open_0", "open_1"], DealerRoster.Trading(state).Select(d => d.Id));
    }

    [Fact]
    public void A_save_keeps_how_its_dealers_are_doing_gets_new_ones_and_loses_those_no_longer_defined()
    {
        var state = GameState.CreateNew("P");
        state.DealerLocations =
        [
            new DealerLocation { Id = "open_0", Name = "Old Name", Status = DealerStatus.Closed, ClosedOn = state.Date },
            new DealerLocation { Id = "gone", Name = "Gone" }
        ];

        DealerRoster.Sync(state, FakeDealers.Named(open: 2, waiting: 1));

        var kept = state.DealerLocations[0];
        Assert.Equal(DealerStatus.Closed, kept.Status);
        Assert.Equal("Open 0", kept.Name);
        Assert.Equal(DealerStatus.Open, DealerRoster.Find(state, "open_1")!.Status);
        Assert.Equal(DealerStatus.Waiting, DealerRoster.Find(state, "new_0")!.Status);
        Assert.Null(DealerRoster.Find(state, "gone"));
    }

    [Fact]
    public void A_save_from_before_dealers_came_and_went_reads_its_dealers_as_open()
    {
        Assert.Equal(DealerStatus.Open, new DealerLocation().Status);
    }

    [Fact]
    public void Cars_are_traded_in_at_an_open_lot_never_at_one_closing_down_or_closed()
    {
        var catalog = new FakeDealers(
            new DealerDefinition { Id = "rough", ConditionCenter = 0.3f },
            new DealerDefinition { Id = "shut", ConditionCenter = 0.1f },
            new DealerDefinition { Id = "fine", ConditionCenter = 0.8f });
        var market = new UsedCarMarketService(null!, null!, null, catalog);
        List<DealerLocation> dealers =
        [
            new() { Id = "rough", Status = DealerStatus.ClosingDown },
            new() { Id = "shut", Status = DealerStatus.Closed },
            new() { Id = "fine" }
        ];

        Assert.Equal("fine", market.TradeInLocation(dealers));
    }

    [Fact]
    public void A_lot_of_one_kind_of_car_takes_only_its_kind_in_trade()
    {
        var cars = new MemoryCatalog();
        cars.UpsertCar(new CarDefinition { Id = "chevy", Name = "Chevy", Brand = "Chevrolet" });
        cars.UpsertCar(new CarDefinition { Id = "fiat", Name = "Fiat", Brand = "Fiat" });
        var catalog = new FakeDealers(
            new DealerDefinition { Id = "imports", ConditionCenter = 0.2f, Cars = new StreetCorsa.Models.Career.Filters.BrandFilter("Fiat") },
            new DealerDefinition { Id = "rough", ConditionCenter = 0.4f });
        var market = new UsedCarMarketService(cars, new MemoryProfiles(), null, catalog);
        List<DealerLocation> dealers = [new() { Id = "imports" }, new() { Id = "rough" }];

        Assert.Equal("rough", market.TradeInLocation(dealers, new Car("chevy")));
        Assert.Equal("imports", market.TradeInLocation(dealers, new Car("fiat")));
    }
}

public class DealerLifeTests
{
    private readonly GameState _state;

    public DealerLifeTests()
    {
        TestLogging.SilenceLogging();
        _state = GameState.CreateNew("P");
    }

    private UsedCarListing OnTheLot(string dealer, decimal price, bool sold = false, int listedDaysAgo = 0)
    {
        var listing = new UsedCarListing
        {
            CarDefinitionId = "car_x", Price = price, DealerLocation = dealer, ListedDate = _state.Date.AddDays(-listedDaysAgo),
            IsSold = sold, SoldDate = sold ? _state.Date.AddHours(-1) : null
        };
        _state.UsedCarMarket.Add(listing);
        return listing;
    }

    [Fact]
    public void A_closing_down_sale_marks_the_lot_down_then_down_again_then_shuts()
    {
        var catalog = FakeDealers.Named(open: 3, waiting: 0);
        DealerRoster.Sync(_state, catalog);
        var closing = _state.DealerLocations[0];
        closing.Status = DealerStatus.ClosingDown;
        closing.ClosesOn = _state.Date.Date.AddDays(DealerRules.SaleDays - 1);
        var car = OnTheLot(closing.Id, 10000m);
        var gone = OnTheLot(closing.Id, 4000m, sold: true);
        var elsewhere = OnTheLot("open_1", 10000m);
        var life = new DealerLifeService(catalog, new FixedRandom(0.99));
        var start = _state.Date;

        life.ReviewDay(_state, start);
        Assert.Equal(8000m, car.Price);
        Assert.Equal(10000m, car.PriceBeforeSale);
        Assert.Equal(10000m, elsewhere.Price);

        life.ReviewDay(_state, start.AddDays(DealerRules.SaleDays - DealerRules.FinalDays));
        Assert.Equal(6500m, car.Price);
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("Last days at Open 0"));

        life.ReviewDay(_state, start.AddDays(DealerRules.SaleDays));
        Assert.Equal(DealerStatus.Closed, closing.Status);
        Assert.DoesNotContain(car, _state.UsedCarMarket);
        Assert.Contains(gone, _state.UsedCarMarket);
        Assert.Contains(elsewhere, _state.UsedCarMarket);
        Assert.Contains(_state.News, a => a.Headline.Contains("Open 0"));
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("Open 0 has shut for good"));
    }

    [Fact]
    public void A_market_short_of_lots_opens_a_dealer_that_was_waiting()
    {
        var catalog = FakeDealers.Named(open: 2, waiting: 3);
        var life = new DealerLifeService(catalog, new ScriptedRandom(0.99, 0.0));

        life.ReviewDay(_state, _state.Date);

        var opened = DealerRoster.Find(_state, "new_0")!;
        Assert.Equal(DealerStatus.Open, opened.Status);
        Assert.Equal(_state.Date.Date, opened.OpenedOn);
        Assert.Contains(_state.News, a => a.Body.Contains("New 0"));
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("New 0"));
        Assert.Contains(opened, DealerRoster.Restocking(_state));
    }

    [Fact]
    public void A_market_with_lots_to_spare_puts_one_into_a_closing_down_sale()
    {
        var catalog = FakeDealers.Named(open: 5, waiting: 0);
        DealerRoster.Sync(_state, catalog);
        var car = OnTheLot("open_0", 10000m);
        var life = new DealerLifeService(catalog, new ScriptedRandom(0.99, 0.0, 0.5, 0.0));

        life.ReviewDay(_state, _state.Date);

        var closing = Assert.Single(DealerRoster.ClosingDown(_state));
        Assert.Equal("open_0", closing.Id);
        Assert.Equal(_state.Date.Date.AddDays(DealerRules.SaleDays - 1), closing.ClosesOn);
        Assert.Equal(8000m, car.Price);
        Assert.DoesNotContain(closing, DealerRoster.Restocking(_state));
        Assert.Contains(closing, DealerRoster.Trading(_state));
        Assert.Contains(_state.News, a => a.Body.Contains("Open 0") && a.Body.Contains("20%"));

        // One sale at a time: however the dice fall, nobody else closes while it runs
        new DealerLifeService(catalog, new FixedRandom(0.0)).ReviewDay(_state, _state.Date.AddDays(1));
        Assert.Single(DealerRoster.ClosingDown(_state));
    }

    [Fact]
    public void A_lot_that_opened_lately_is_given_time_before_it_can_close()
    {
        var catalog = FakeDealers.Named(open: 5, waiting: 0);
        DealerRoster.Sync(_state, catalog);
        foreach (var dealer in _state.DealerLocations) dealer.OpenedOn = _state.Date.AddDays(-10);

        new DealerLifeService(catalog, new ScriptedRandom(0.99, 0.0)).ReviewDay(_state, _state.Date);

        Assert.Empty(DealerRoster.ClosingDown(_state));
    }

    [Fact]
    public void A_market_that_does_not_refresh_keeps_its_dealers_but_a_sale_still_runs_out()
    {
        _state.Rules.MarketRefreshEnabled = false;
        var catalog = FakeDealers.Named(open: 2, waiting: 3);
        DealerRoster.Sync(_state, catalog);
        var closing = _state.DealerLocations[0];
        closing.Status = DealerStatus.ClosingDown;
        closing.ClosesOn = _state.Date.Date.AddDays(-1);

        new DealerLifeService(catalog, new FixedRandom(0.0)).ReviewDay(_state, _state.Date);

        Assert.Equal(DealerStatus.Closed, closing.Status);
        Assert.All(_state.DealerLocations.Where(d => d.Id.StartsWith("new_")), d => Assert.Equal(DealerStatus.Waiting, d.Status));
    }

    [Fact]
    public void The_cars_a_lot_sells_count_toward_its_trade_and_fade_as_the_weeks_go()
    {
        var catalog = FakeDealers.Named(open: 2, waiting: 0);
        DealerRoster.Sync(_state, catalog);
        var dealer = _state.DealerLocations[0];
        dealer.Trade = 4;
        dealer.TradeCountedTo = _state.Date.AddDays(-DealerRules.TradeHalfLifeDays);
        OnTheLot(dealer.Id, 1000m, sold: true);
        OnTheLot(dealer.Id, 1000m, sold: true);
        OnTheLot(dealer.Id, 1000m);

        new DealerLifeService(catalog, new FixedRandom(0.99)).ReviewDay(_state, _state.Date);

        Assert.Equal(4, dealer.Trade, 6); // half the 4, and the two sold
        Assert.Equal(_state.Date, dealer.TradeCountedTo);

        // The same sales are not counted twice
        new DealerLifeService(catalog, new FixedRandom(0.99)).ReviewDay(_state, _state.Date.AddDays(1));
        Assert.True(dealer.Trade < 4);
    }

    [Fact]
    public void A_car_bought_the_morning_of_a_review_counts_the_next_day()
    {
        var catalog = FakeDealers.Named(open: 2, waiting: 0);
        var life = new DealerLifeService(catalog, new FixedRandom(0.99));
        var morning = _state.Date;
        life.ReviewDay(_state, morning);
        var dealer = _state.DealerLocations[0];
        Assert.Equal(0, dealer.Trade);

        // The rivals and the player buy after the review, at the very time it counted to
        var sold = OnTheLot(dealer.Id, 1000m, sold: true);
        sold.SoldDate = morning;

        life.ReviewDay(_state, morning.AddDays(1));
        Assert.Equal(1, dealer.Trade, 1);

        life.ReviewDay(_state, morning.AddDays(2));
        Assert.True(dealer.Trade < 1);
    }

    [Fact]
    public void A_lot_sold_out_is_not_the_one_picked_to_close()
    {
        var catalog = FakeDealers.Named(open: 5, waiting: 0);
        DealerRoster.Sync(_state, catalog);
        foreach (var dealer in _state.DealerLocations) dealer.TradeCountedTo = _state.Date;

        // Every lot but the first has a car that has stood there a while; the first sold its last one
        foreach (var dealer in _state.DealerLocations.Skip(1)) OnTheLot(dealer.Id, 1000m, listedDaysAgo: DealerRules.StaleDays);

        // ChangeToday, Direction's coin, then the pick: a tenth of the way through the weights. Were the empty lot
        // weighted as the stale ones, that would land on it; weighted as fresh stock, a third of theirs, it passes it by.
        new DealerLifeService(catalog, new ScriptedRandom(0.0, 0.0, 0.0, 0.1)).ReviewDay(_state, _state.Date);

        var closing = Assert.Single(DealerRoster.ClosingDown(_state));
        Assert.Equal("open_1", closing.Id);
    }

    [Fact]
    public void A_sale_whose_last_day_was_lost_ends_today_and_the_dealers_carry_on()
    {
        var catalog = FakeDealers.Named(open: 3, waiting: 0);
        DealerRoster.Sync(_state, catalog);
        var closing = _state.DealerLocations[0];
        closing.Status = DealerStatus.ClosingDown;
        closing.ClosesOn = null;
        var life = new DealerLifeService(catalog, new FixedRandom(0.99));

        life.ReviewDay(_state, _state.Date);
        Assert.Equal(_state.Date.Date, closing.ClosesOn);

        life.ReviewDay(_state, _state.Date.AddDays(1));
        Assert.Equal(DealerStatus.Closed, closing.Status);
    }
}

public class ClosingSaleRivalTests
{
    // Every car is worth $10,000: the least a racer asks for one in the paper is $7,500
    private readonly FakeMarket _market = new(10000m);
    private readonly GameState _state = GameState.CreateNew("P");

    public ClosingSaleRivalTests()
    {
        TestLogging.SilenceLogging();
        _state.DealerLocations =
        [
            new DealerLocation { Id = "closing", Name = "Sunset Motors", Status = DealerStatus.ClosingDown, ClosesOn = _state.Date.AddDays(2) },
            new DealerLocation { Id = "open", Name = "Downtown Motors" }
        ];
    }

    // 0.3: under the sale's pull (BargainChance), over every other chance a racer takes in a day
    private OpponentLifeService Life() => new(new NoParts(), _market, new NoCars(), random: new FixedRandom(0.3));

    private Opponent Racer(string name, decimal money, double hp = 200)
    {
        var racer = new Opponent(name, 30, Gender.Female, 92, 50) { Money = money, Status = RacerStatus.ReadyToRace };
        racer.Cars.Add(new Car("car_own") { PowerHp = hp, PurchaseDate = _state.Date.AddDays(-30) });
        _state.Racers.AddRacer(racer);
        return racer;
    }

    private UsedCarListing OnSale(decimal was, decimal price, double hp, string dealer = "closing")
    {
        var listing = new UsedCarListing
        {
            CarDefinitionId = "car_sale", Price = price, PriceBeforeSale = dealer == "closing" ? was : null, PowerHp = hp,
            Condition = 0.8f, DealerLocation = dealer, ListedDate = _state.Date
        };
        _state.UsedCarMarket.Add(listing);
        return listing;
    }

    [Fact]
    public async Task A_racer_buys_a_stronger_car_cheap_at_the_sale_and_puts_the_old_one_in_the_paper()
    {
        var racer = Racer("Ann", 20000m);
        var old = racer.Cars[0];
        var bargain = OnSale(10000m, 8000m, 300);

        await Life().ReviewDayAsync(_state, _state.Date);

        Assert.True(bargain.IsSold);
        Assert.Equal(300, racer.Cars[0].PowerHp);
        Assert.NotNull(RivalCarAds.AdFor(_state, old));
        Assert.Contains(_state.StreetTalk, t => t.Text.Contains("Sunset Motors") && t.Text.Contains("closing-down sale"));
    }

    [Fact]
    public async Task A_racer_buys_a_deep_cut_to_sell_it_straight_on()
    {
        var racer = Racer("Bo", 10000m);
        var own = racer.Cars[0];
        var flip = OnSale(10000m, 6500m, 100);

        await Life().ReviewDayAsync(_state, _state.Date);

        Assert.True(flip.IsSold);
        Assert.Same(own, racer.Cars[0]);
        var bought = Assert.Single(racer.Cars, c => c.DefinitionId == "car_sale");
        Assert.NotNull(RivalCarAds.AdFor(_state, bought));
    }

    [Fact]
    public async Task A_car_bought_to_sell_on_goes_in_the_paper_even_when_it_is_better_than_their_own()
    {
        var racer = Racer("Di", 10000m, hp: 100);
        racer.Cars[0].EngineHealth = 0.2;
        var own = racer.Cars[0];
        var flip = OnSale(10000m, 6500m, 105);

        await Life().ReviewDayAsync(_state, _state.Date);

        Assert.True(flip.IsSold);
        Assert.Same(own, racer.Cars[0]);
        Assert.NotNull(RivalCarAds.AdFor(_state, racer.Cars[1]));
    }

    [Fact]
    public async Task A_racer_with_no_room_does_not_buy_to_sell_on()
    {
        var racer = Racer("Ed", 10000m);
        racer.Cars.Add(new Car("car_spare") { PowerHp = 50, PurchaseDate = _state.Date.AddDays(-30) });
        var flip = OnSale(10000m, 6500m, 100);

        await Life().ReviewDayAsync(_state, _state.Date);

        Assert.False(flip.IsSold);
    }

    [Fact]
    public async Task A_lot_that_marked_a_car_up_does_not_make_it_worth_flipping()
    {
        Racer("Fay", 10000m);
        // Marked at $12,000 over its $10,000 worth: at 35% off it is $7,800, over what it would fetch in the paper
        var flip = OnSale(12000m, 7800m, 100);

        await Life().ReviewDayAsync(_state, _state.Date);

        Assert.False(flip.IsSold);
    }

    [Fact]
    public async Task Nobody_flips_a_car_off_an_open_lot_or_at_the_sales_first_cut()
    {
        Racer("Cy", 10000m);
        var firstCut = OnSale(10000m, 8000m, 100);
        var openLot = OnSale(10000m, 6500m, 100, dealer: "open");

        await Life().ReviewDayAsync(_state, _state.Date);

        Assert.False(firstCut.IsSold);
        Assert.False(openLot.IsSold);
    }

    [Fact]
    public async Task Only_a_few_cars_go_to_the_rivals_in_a_day()
    {
        for (var i = 0; i < 5; i++) Racer($"R{i}", 20000m);
        var sale = Enumerable.Range(0, 5).Select(_ => OnSale(10000m, 8000m, 300)).ToList();

        await Life().ReviewDayAsync(_state, _state.Date);

        Assert.Equal(OpponentRules.MaxBargainsPerDay, sale.Count(l => l.IsSold));
    }
}

public class DealerMapTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_mods_map_is_the_one_shown_and_a_map_without_its_picture_is_left_out()
    {
        _temp.File("streetcorsa/mod.json", "{}");
        _temp.File("mymod/mod.json", """{ "id": "mymod", "name": "My Mod" }""");
        _temp.File("streetcorsa/dealers/dealers.json", """{ "dealers": [ { "id": "downtown", "name": "Downtown" } ] }""");
        _temp.File("streetcorsa/dealers/map.json", """{ "maps": [ { "id": "city", "image": "city.jpg", "anchorX": 0.1 } ] }""");
        _temp.Bytes("streetcorsa/dealers/city.jpg", [1, 2, 3]);
        _temp.File("mymod/dealers/map.json", """{ "maps": [ { "id": "valley", "image": "valley.png", "anchorWidth": 0.5 } ] }""");
        _temp.Bytes("mymod/dealers/valley.png", [1, 2, 3]);
        _temp.File("mymod/dealers/broken.json", """{ "maps": [ { "id": "nowhere", "image": "../../streetcorsa/dealers/city.jpg" } ] }""");

        var dealers = new DealerCatalog(ModSet.Load(_temp.Path));

        Assert.Equal("mymod:valley", dealers.Map!.Id);
        Assert.Equal(_temp.Combine("mymod", "dealers", "valley.png"), dealers.Map.ImagePath);
        Assert.Equal(0.5, dealers.Map.Anchor.Width, 6);
        Assert.Equal(["downtown"], dealers.All.Select(d => d.Id)); // a map is no dealer
    }

    [Fact]
    public void A_lots_kind_of_car_that_does_not_read_is_told_and_the_lot_deals_in_any()
    {
        _temp.File("streetcorsa/mod.json", "{}");
        _temp.File("streetcorsa/dealers/dealers.json", """
            { "dealers": [
              { "id": "imports", "name": "Imports", "cars": { "type": "origin", "origin": "European" } },
              { "id": "worded", "name": "Worded", "cars": "European" },
              { "id": "unknown", "name": "Unknown", "cars": { "type": "country" } } ] }
            """);

        var dealers = new DealerCatalog(ModSet.Load(_temp.Path));

        Assert.Equal(["imports", "worded", "unknown"], dealers.All.Select(d => d.Id));
        Assert.Equal("European", Assert.IsType<StreetCorsa.Models.Career.Filters.OriginFilter>(dealers.Get("imports")!.Cars).Origin);
        Assert.Null(dealers.Get("worded")!.Cars);
        Assert.Null(dealers.Get("unknown")!.Cars);
        Assert.Contains(ModProblems.All, p => p.Contains("dealer worded: \"cars\" is no car filter"));
        Assert.Contains(ModProblems.All, p => p.Contains("dealer unknown: \"cars\" is no car filter"));
    }
}
