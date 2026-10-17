using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;

namespace StreetCorsa.Tests;

/// <summary>Part shops as authored, without a mod folder</summary>
internal sealed class FakePartShops(params PartShopDefinition[] shops) : IPartShopCatalog
{
    public IReadOnlyList<PartShopDefinition> All => shops;

    public PartShopDefinition? Get(string? id) => shops.FirstOrDefault(s => s.Id == id);
}

public class PartShopRulesTests
{
    private static readonly CatalogParts Parts = new();

    private static PartDefinition Find(Func<PartDefinition, bool> where) => Parts.Catalog.Parts.Values.First(where);

    [Fact]
    public void A_regular_gets_five_percent_off_and_a_good_customer_ten()
    {
        Assert.Equal(0m, PartShopRules.RegularDiscountFor(PartShopRules.RegularSpent - 1));
        Assert.Equal(PartShopRules.RegularDiscount, PartShopRules.RegularDiscountFor(PartShopRules.RegularSpent));
        Assert.Equal(PartShopRules.GoodCustomerDiscount, PartShopRules.RegularDiscountFor(PartShopRules.GoodCustomerSpent));
    }

    [Fact]
    public void The_base_game_engine_packs_carry_their_make_and_the_universal_parts_none()
    {
        Assert.All(Parts.Catalog.Parts.Values.Where(p => p.LocalId.StartsWith("engines/chrysler/")), p => Assert.Equal("mopar", p.Make));
        Assert.All(Parts.Catalog.Parts.Values.Where(p => p.LocalId.StartsWith("engines/ford")), p => Assert.Equal("ford", p.Make));
        Assert.All(Parts.Catalog.Parts.Values.Where(p => p.LocalId.StartsWith("engines/gm/")), p => Assert.Equal("gm", p.Make));
        Assert.All(Parts.Catalog.Parts.Values.Where(p => p.LocalId.StartsWith("engines/generic/")), p => Assert.Null(p.Make));
    }

    [Fact]
    public void Each_kind_of_shop_deals_in_its_own_parts()
    {
        var mopar = new PartShopDefinition { Id = "m", Kind = "specialist", Makes = ["mopar"] };
        var speed = new PartShopDefinition { Id = "s", Kind = "speed" };
        var wheels = new PartShopDefinition { Id = "w", Kind = "wheels" };
        var yard = new PartShopDefinition { Id = "j", Kind = "junkyard" };

        var moparCam = Find(p => p.Make == "mopar" && PartKinds.GroupOf(p) == PartGroups.Camshafts);
        var fordBlock = Find(p => p.Make == "ford" && PartKinds.IsBlock(p));
        var tyre = Find(p => p.IsScripted && PartKinds.GroupOf(p) == PartKinds.Tyres);
        var genericCarb = Find(p => p.Make == null && PartKinds.GroupOf(p) == "carburettors");
        var noFactory = new HashSet<string>();

        Assert.True(PartShopRules.Deals(mopar, moparCam, noFactory));
        Assert.False(PartShopRules.Deals(mopar, fordBlock, noFactory));
        Assert.False(PartShopRules.Deals(mopar, tyre, noFactory));

        // Power for any make, but no block, nothing a car came with, and no running gear
        Assert.True(PartShopRules.Deals(speed, genericCarb, noFactory));
        Assert.True(PartShopRules.Deals(speed, moparCam, noFactory));
        Assert.False(PartShopRules.Deals(speed, moparCam, new HashSet<string> { moparCam.Id }));
        Assert.False(PartShopRules.Deals(speed, fordBlock, noFactory));
        Assert.False(PartShopRules.Deals(speed, tyre, noFactory));

        Assert.True(PartShopRules.Deals(wheels, tyre, noFactory));
        Assert.False(PartShopRules.Deals(wheels, moparCam, noFactory));

        Assert.True(PartShopRules.Deals(yard, fordBlock, noFactory));
        Assert.True(PartShopRules.Deals(yard, tyre, noFactory));
    }

    [Fact]
    public void A_shops_own_numbers_win_over_its_kinds()
    {
        var shop = new PartShopDefinition { Kind = "speed", PriceFactor = 1.5, StockLow = 3, StockHigh = 2, RestockDays = 0 };
        Assert.Equal(1.5, PartShopRules.PriceFactor(shop));
        Assert.Equal((3, 3), PartShopRules.Stock(shop));
        Assert.Equal(1, PartShopRules.RestockDays(shop));

        // Nonsense falls back on the kind's
        Assert.Equal(1.1, PartShopRules.PriceFactor(new PartShopDefinition { Kind = "speed", PriceFactor = double.NaN }));
        Assert.Equal(PartShopKind.Specialist, new PartShopDefinition { Kind = "boutique" }.KindOf);
        Assert.Equal(PartShopKind.Junkyard, new PartShopDefinition { Kind = " JunkYard " }.KindOf);

        // A number is no kind's name, nor is a mix of them
        Assert.Equal(PartShopKind.Specialist, new PartShopDefinition { Kind = "1" }.KindOf);
        Assert.Equal(PartShopKind.Specialist, new PartShopDefinition { Kind = "7" }.KindOf);
        Assert.Equal(PartShopKind.Specialist, new PartShopDefinition { Kind = "-1" }.KindOf);
        Assert.Equal(PartShopKind.Specialist, new PartShopDefinition { Kind = "speed, wheels" }.KindOf);
    }
}

public class BaseGamePartShopsTests
{
    private static readonly PartShopCatalog Shops = new(ModSet.Load(RepoPaths.Content));

    [Fact]
    public void Twelve_shops_seven_open_and_every_kind_open_from_the_start()
    {
        var shops = Shops.All.Where(s => ModIds.IsBase(ModIds.ModOf(s.Id))).ToList();
        Assert.Equal(12, shops.Count);
        Assert.Equal(7, shops.Count(s => s.StartsOpen));
        Assert.Equal(DealerRules.TargetOpen(12), shops.Count(s => s.StartsOpen));
        foreach (var kind in Enum.GetValues<PartShopKind>()) Assert.Contains(shops, s => s.StartsOpen && s.KindOf == kind);
        Assert.Equal(shops.Count, shops.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Every_shop_pin_keeps_clear_of_every_other_shop_pin_on_the_map()
    {
        // The name plates hang under the pins: about 0.055 of the picture wide and 0.035 tall between two pins. The
        // shops have a map of their own: the dealers' pins are not on it.
        var shops = Shops.All.Select(s => (s.Name, X: (double)s.MapX, Y: (double)s.MapY)).ToList();

        foreach (var shop in shops)
        {
            foreach (var other in shops.Where(o => o.Name != shop.Name))
            {
                var clear = Math.Abs(shop.X - other.X) >= 0.055 || Math.Abs(shop.Y - other.Y) >= 0.035;
                Assert.True(clear, $"{shop.Name} is on top of {other.Name}");
            }
        }
    }

    [Fact]
    public void Every_base_game_shop_has_something_to_sell_but_a_machine_shop_nothing()
    {
        var parts = new CatalogParts();
        var service = new PartShopService(Shops, parts, new NewspaperPartsService(parts));
        foreach (var shop in Shops.All)
        {
            if (shop.KindOf == PartShopKind.Machine) Assert.Empty(service.AssortmentOf(shop));
            else Assert.True(service.AssortmentOf(shop).Count >= 10, $"{shop.Name} deals in almost nothing");
        }
    }
}

public class PartShopServiceTests
{
    private static readonly CatalogParts Parts = new();

    private static readonly PartShopDefinition Mopar = new()
    {
        Id = "hemi", Name = "Hemi Hut", Region = "Burbank", Kind = "specialist", Makes = ["mopar"], StockLow = 5, StockHigh = 5
    };

    private static readonly PartShopDefinition Yard = new()
    {
        Id = "yard", Name = "Wreckers", Region = "Docks", Kind = "junkyard", StockLow = 4, StockHigh = 4
    };

    private readonly GameState _state = new();
    private readonly PartShopService _service;

    public PartShopServiceTests()
    {
        _service = new PartShopService(new FakePartShops(Mopar, Yard), Parts, new NewspaperPartsService(Parts));
        PartShopRoster.Sync(_state, _service.Catalog);
    }

    private PartShopLocation Shop(string id) => PartShopRoster.Find(_state, id)!;

    private async Task<(PartShopLocation Shop, PartDefinition Part)> StockedMopar()
    {
        await _service.RestockAsync(_state, _state.Date, new Random(3));
        var shop = Shop("hemi");
        return (shop, Parts.Catalog.Get(shop.Shelves[0].DefinitionId)!);
    }

    [Fact]
    public async Task A_delivery_fills_the_shelves_with_parts_of_the_shops_make()
    {
        var (shop, _) = await StockedMopar();

        Assert.Equal(5, shop.Shelves.Count);
        Assert.All(shop.Shelves, s => Assert.Equal("mopar", Parts.Catalog.Get(s.DefinitionId)!.Make));
        Assert.All(shop.Shelves, s => Assert.InRange(s.Count, 2, 5));
        Assert.Equal(_state.Date.Date, shop.RestockedOn);

        // Not due again for a week
        var before = shop.Shelves.Select(s => (s.DefinitionId, s.Count)).ToList();
        await _service.RestockAsync(_state, _state.Date.AddDays(2), new Random(4));
        Assert.Equal(before, shop.Shelves.Select(s => (s.DefinitionId, s.Count)).ToList());
    }

    [Fact]
    public async Task Buying_takes_one_off_the_shelf_and_the_last_one_is_the_last()
    {
        var (shop, part) = await StockedMopar();
        var shelf = shop.Shelves[0];
        shelf.Count = 1;
        var money = _state.Player.Money;
        var price = _service.PriceOfNew(_state, shop, part);

        Assert.True(_service.BuyNew(_state, shop, part));
        Assert.Equal(0, shelf.Count);
        Assert.Equal(money - price, _state.Player.Money);
        Assert.Contains(_state.Player.Parts, p => p.DefinitionId == part.Id);
        Assert.Equal(price, shop.PlayerSpent);

        Assert.False(_service.BuyNew(_state, shop, part));
    }

    [Fact]
    public async Task A_regular_and_a_closing_down_sale_both_take_their_share_off()
    {
        var (shop, part) = await StockedMopar();
        var marked = _service.MarkedPriceOfNew(_state, shop, part);
        Assert.Equal(marked, _service.PriceOfNew(_state, shop, part));

        shop.PlayerSpent = PartShopRules.GoodCustomerSpent;
        var asRegular = _service.PriceOfNew(_state, shop, part);
        Assert.True(asRegular < marked);

        shop.Status = DealerStatus.ClosingDown;
        shop.ClosesOn = _state.Date.Date.AddDays(DealerRules.SaleDays - 1);
        Assert.True(_service.PriceOfNew(_state, shop, part) < asRegular);
    }

    [Fact]
    public async Task A_shut_shop_sells_nothing()
    {
        var (shop, part) = await StockedMopar();
        shop.Status = DealerStatus.Closed;
        Assert.False(_service.BuyNew(_state, shop, part));
    }

    [Fact]
    public async Task A_junkyard_pays_more_than_a_trade_in_and_puts_the_part_in_its_yard()
    {
        await _service.RestockAsync(_state, _state.Date, new Random(5));
        var yard = Shop("yard");
        Assert.Equal(4, yard.Yard.Count);

        var part = new PartInstance(Parts.Catalog.Parts.Values.First(p => p.IsScripted && PartPricing.NewPrice(p) > 50).Id) { Wear = 0.8 };
        _state.Player.Parts.Add(part);
        var paid = _service.BuyingPrice(yard, part);
        Assert.True(paid > PartPricing.TradeIn(Parts.Catalog, part));

        Assert.True(_service.Sell(_state, yard, part));
        Assert.DoesNotContain(part, _state.Player.Parts);
        var item = Assert.Single(yard.Yard, i => ReferenceEquals(i.Part, part));
        Assert.True(item.AskingPrice > paid);
    }

    [Fact]
    public async Task Nothing_out_of_a_junkyard_sells_back_to_it_at_a_profit()
    {
        await _service.RestockAsync(_state, _state.Date, new Random(5));
        var yard = Shop("yard");

        _state.Player.Money = 10_000_000m;

        // Everything off at once: the last days of the sale, a good customer, and the owner come down
        yard.PlayerSpent = PartShopRules.GoodCustomerSpent;
        yard.Status = DealerStatus.ClosingDown;
        yard.ClosesOn = _state.Date.Date;
        foreach (var item in yard.Yard.ToList())
        {
            _service.Haggle(yard, item, new FixedRandom(0.0));
            var price = _service.PriceOfUsed(_state, yard, item);
            Assert.True(price > _service.BuyingPrice(yard, item.Part), $"{item.Part.DefinitionId} costs ${price}");

            var money = _state.Player.Money;
            Assert.True(_service.BuyUsed(_state, yard, item));
            Assert.True(_service.Sell(_state, yard, item.Part));
            Assert.True(_state.Player.Money < money);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(45)]
    [InlineData(1000)]
    public void A_junkyard_asks_more_than_it_paid_on_the_price_grid(int paid)
    {
        var floor = PartShopService.JunkyardFloor(paid);

        Assert.True(floor > paid, $"paid ${paid}, asks ${floor}");
        Assert.Equal(PartPricing.Round((double)floor), floor);
        Assert.True(floor >= PartPricing.Round(paid * (1 + PartShopRules.JunkyardMargin)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 5)]
    [InlineData(4.5, 5)]
    [InlineData(19, 20)]
    [InlineData(19.5, 20)]
    [InlineData(20, 25)]
    [InlineData(22, 25)]
    [InlineData(995, 1000)]
    public void The_next_price_up_is_the_next_one_round_gives(double price, int above)
    {
        Assert.Equal(above, PartPricing.Above((decimal)price));
        Assert.Equal(above, PartPricing.Round(above));
    }

    [Fact]
    public async Task A_new_part_bought_cheapest_sells_to_a_junkyard_for_less()
    {
        var (shop, part) = await StockedMopar();
        _state.Rules.PartPriceMultiplier = 0.85;
        shop.PlayerSpent = PartShopRules.GoodCustomerSpent;
        shop.Status = DealerStatus.ClosingDown;
        shop.ClosesOn = _state.Date.Date;
        var paid = _service.PriceOfNew(_state, shop, part);

        Assert.True(_service.BuyingPrice(Shop("yard"), new PartInstance(part.Id)) < paid);
    }

    [Fact]
    public async Task A_yard_is_stocked_once_when_two_ask_at_the_same_time()
    {
        // The morning's review and the map, both before the first has had its wrecks taken apart
        var first = _service.RestockAsync(_state, _state.Date, new Random(5));
        var second = _service.RestockAsync(_state, _state.Date, new Random(6), firstOnly: true);
        await Task.WhenAll(first, second);

        Assert.Equal(4, Shop("yard").Yard.Count);
    }

    [Fact]
    public async Task A_junkyard_comes_down_once_or_not_at_all()
    {
        await _service.RestockAsync(_state, _state.Date, new Random(5));
        var yard = Shop("yard");
        var item = yard.Yard[0];
        var asked = item.AskingPrice;

        Assert.True(_service.Haggle(yard, item, new FixedRandom(0.0)));
        Assert.True(item.AskingPrice < asked);
        var lower = item.AskingPrice;

        Assert.False(_service.Haggle(yard, item, new FixedRandom(0.0)));
        Assert.Equal(lower, item.AskingPrice);

        var other = yard.Yard[1];
        var otherAsked = other.AskingPrice;
        Assert.False(_service.Haggle(yard, other, new FixedRandom(0.99)));
        Assert.Equal(otherAsked, other.AskingPrice);
        Assert.True(other.Haggled);
    }

    [Fact]
    public async Task A_rival_gets_a_part_where_it_is_cheapest_and_it_comes_off_that_shelf()
    {
        var (shop, part) = await StockedMopar();
        var prices = _service.RivalPrices(_state);

        Assert.True(prices.TryGetShop(part.Id, out var shopId, out var price));
        Assert.Equal(("hemi", _service.MarkedPriceOfNew(_state, shop, part)), (shopId, price));
        Assert.Equal(price, prices.PriceOf(part));

        // What no shop has comes by mail, with its premium
        var notStocked = new NewspaperPartsService(Parts).Assortment.First(p => shop.Shelves.All(s => s.DefinitionId != p.Id) && p.Make != "mopar");
        Assert.False(prices.TryGetShop(notStocked.Id, out _, out _));
        Assert.Equal(NewspaperPartsService.MailOrderPrice(notStocked, 1.0), prices.PriceOf(notStocked));

        var count = shop.Shelves[0].Count;
        Assert.True(_service.TakeForRival(_state, "hemi", part.Id));
        Assert.Equal(count - 1, shop.Shelves[0].Count);
        Assert.True(shop.Trade > 0);
    }

    [Fact]
    public async Task A_partshops_file_that_did_not_read_leaves_the_saves_shops_as_they_are()
    {
        var (shop, _) = await StockedMopar();
        shop.PlayerSpent = 1234m;

        PartShopRoster.Sync(_state, new FakePartShops());

        Assert.Same(shop, Shop("hemi"));
        Assert.Equal(1234m, shop.PlayerSpent);
        Assert.NotEmpty(shop.Shelves);
    }

    [Fact]
    public async Task A_shop_and_its_stock_survive_the_save()
    {
        await StockedMopar();
        _state.Player.PartOrders.Add(new PartOrder { Part = new PartInstance("x"), ArrivesOn = _state.Date.AddDays(3), Paid = 12 });

        var document = LiteDB.BsonMapper.Global.ToDocument(_state);
        var loaded = LiteDB.BsonMapper.Global.ToObject<GameState>(document);

        var shop = loaded.PartShops.Single(s => s.Id == "hemi");
        Assert.Equal(Shop("hemi").Shelves.Select(s => (s.DefinitionId, s.Count)), shop.Shelves.Select(s => (s.DefinitionId, s.Count)));
        Assert.Equal(12m, Assert.Single(loaded.Player.PartOrders).Paid);
    }
}

public class MailOrderTests
{
    private static readonly CatalogParts Parts = new();

    [Fact]
    public void The_mail_order_charges_its_premium_and_brings_the_part_in_three_days()
    {
        var state = new GameState();
        var paper = new NewspaperPartsService(Parts);
        var part = paper.Assortment.First(p => PartPricing.NewPrice(p) > 20);
        var money = state.Player.Money;

        var price = paper.NewPrice(part, 1.0);
        Assert.Equal(PartPricing.Round(PartPricing.NewPrice(part) * NewspaperPartsService.MailOrderPremium), price);

        var order = paper.BuyNew(state, part);
        Assert.NotNull(order);
        Assert.Equal(money - price, state.Player.Money);
        Assert.Empty(state.Player.Parts);

        Assert.Empty(paper.Deliver(state, state.Date.AddDays(NewspaperPartsService.MailOrderDays - 1)));
        Assert.Single(paper.Deliver(state, state.Date.AddDays(NewspaperPartsService.MailOrderDays)));
        Assert.Contains(state.Player.Parts, p => p.DefinitionId == part.Id);
        Assert.Empty(state.Player.PartOrders);
        Assert.Equal([part.DisplayName ?? part.Name], state.Player.Delivered);
    }

    [Fact]
    public void Nothing_is_ordered_without_the_money()
    {
        var state = new GameState();
        var paper = new NewspaperPartsService(Parts);
        state.Player.Money = 0;
        Assert.Null(paper.BuyNew(state, paper.Assortment[0]));
        Assert.Empty(state.Player.PartOrders);
    }
}

public class PartShopLifeTests
{
    private static readonly CatalogParts Parts = new();

    private static PartShopDefinition Shop(string id, string kind, bool open) =>
        new() { Id = id, Name = id, Region = "Town", Kind = kind, StartsOpen = open, StockLow = 3, StockHigh = 3 };

    [Fact]
    public async Task A_closing_down_sale_clears_out_and_shuts_after_its_last_day()
    {
        var catalog = new FakePartShops(Shop("a", "specialist", true), Shop("b", "speed", true));
        var service = new PartShopService(catalog, Parts, new NewspaperPartsService(Parts));
        var state = new GameState();
        var life = new PartShopLifeService(service, new FixedRandom(0.99));

        await life.ReviewDayAsync(state, state.Date);
        var shop = PartShopRoster.Find(state, "a")!;
        Assert.NotEmpty(shop.Shelves);

        shop.Status = DealerStatus.ClosingDown;
        shop.ClosesOn = state.Date.Date.AddDays(1);

        // The bargain hunters take their share every day of it
        await new PartShopLifeService(service, new FixedRandom(0.0)).ReviewDayAsync(state, state.Date.AddDays(1));
        Assert.Empty(shop.Shelves);
        Assert.Equal(DealerStatus.ClosingDown, shop.Status);

        await life.ReviewDayAsync(state, state.Date.AddDays(2));
        Assert.Equal(DealerStatus.Closed, shop.Status);
        Assert.Empty(shop.Shelves);
        Assert.Contains(state.News, n => n.Headline.Contains("a"));
    }

    [Fact]
    public async Task A_shop_that_shuts_and_opens_again_has_no_regulars()
    {
        var catalog = new FakePartShops(Shop("a", "specialist", true), Shop("b", "speed", true));
        var service = new PartShopService(catalog, Parts, new NewspaperPartsService(Parts));
        var state = new GameState();
        var life = new PartShopLifeService(service, new FixedRandom(0.99));

        await life.ReviewDayAsync(state, state.Date);
        var shop = PartShopRoster.Find(state, "a")!;
        shop.PlayerSpent = PartShopRules.GoodCustomerSpent;
        shop.Status = DealerStatus.ClosingDown;
        shop.ClosesOn = state.Date.Date;

        await life.ReviewDayAsync(state, state.Date.AddDays(1));
        Assert.Equal(DealerStatus.Closed, shop.Status);
        Assert.Equal(0m, shop.PlayerSpent);
    }

    [Fact]
    public async Task A_new_shop_is_of_a_kind_the_town_has_none_of()
    {
        // Two specialists and a speed shop open, a specialist and a junkyard waiting; three of five is the target, so
        // a change at the target goes either way: the coin says open
        var catalog = new FakePartShops(Shop("s1", "specialist", true), Shop("s2", "specialist", true), Shop("sp", "speed", true),
            Shop("s3", "specialist", false), Shop("junk", "junkyard", false));
        var service = new PartShopService(catalog, Parts, new NewspaperPartsService(Parts));
        var state = new GameState();

        await new PartShopLifeService(service, new ScriptedRandom(0.99, 0.0, 0.9)).ReviewDayAsync(state, state.Date);

        Assert.Equal(DealerStatus.Open, PartShopRoster.Find(state, "junk")!.Status);
        Assert.Equal(DealerStatus.Waiting, PartShopRoster.Find(state, "s3")!.Status);
    }

    [Fact]
    public async Task A_game_whose_market_stands_still_gets_its_first_stock_and_keeps_it()
    {
        var catalog = new FakePartShops(Shop("a", "specialist", true));
        var service = new PartShopService(catalog, Parts, new NewspaperPartsService(Parts));
        var state = new GameState { Rules = new GameRules { MarketRefreshEnabled = false } };
        var life = new PartShopLifeService(service, new FixedRandom(0.99));

        await life.ReviewDayAsync(state, state.Date);
        var shop = PartShopRoster.Find(state, "a")!;
        var first = shop.Shelves.Select(s => s.DefinitionId).ToList();
        Assert.NotEmpty(first);

        await life.ReviewDayAsync(state, state.Date.AddDays(30));
        Assert.Equal(first, shop.Shelves.Select(s => s.DefinitionId).ToList());
    }
}

public class TunerShopPriceTests
{
    private static readonly CatalogParts Parts = new();

    [Fact]
    public void A_tuner_pays_what_it_is_told_a_part_costs()
    {
        var build = Parts.Builds.Runnable.Single(b => b.Build.Id.EndsWith("chrysler-v8-pack/chrysler-la-340-275-hp", StringComparison.Ordinal));
        var stock = EngineFactory.CreateStock(Parts.Catalog, build, 0.8, new Random(1))!;

        var listed = EngineTuner.TuneUp(Parts.Catalog, Parts.Builds, stock.Root, 1_000_000m, 1.0, new Random(7), maxUpgrades: 1);
        Assert.NotNull(listed);

        // Every new part at a million: nothing within a budget that bought an upgrade at list price
        var dear = EngineTuner.TuneUp(Parts.Catalog, Parts.Builds, stock.Root, listed!.Cost + 1, 1.0, new Random(7), maxUpgrades: 1,
            newPrice: _ => 1_000_000m);
        Assert.True(dear == null || dear.Upgrades.All(u => u.Group == EngineTuner.BlockGroup));
    }

    [Fact]
    public void An_upgrade_names_the_new_parts_it_put_on_and_a_swap_none()
    {
        var build = Parts.Builds.Runnable.Single(b => b.Build.Id.EndsWith("chrysler-v8-pack/chrysler-la-340-275-hp", StringComparison.Ordinal));
        var stock = EngineFactory.CreateStock(Parts.Catalog, build, 0.8, new Random(1))!;
        var had = stock.Root.SelfAndDescendants().Select(p => p.InstanceId).ToHashSet();

        var result = EngineTuner.TuneUp(Parts.Catalog, Parts.Builds, stock.Root, 1_000_000m, 1.0, new Random(7), maxUpgrades: 2);
        Assert.NotNull(result);

        var onEngine = result!.Engine.SelfAndDescendants().ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var upgrade in result.Upgrades)
        {
            if (upgrade.Group == EngineTuner.BlockGroup)
            {
                Assert.Empty(upgrade.Fitted);
                continue;
            }

            Assert.NotEmpty(upgrade.Fitted);
            Assert.All(upgrade.Fitted, p => Assert.Contains(p, onEngine));
            Assert.All(upgrade.Fitted, p => Assert.DoesNotContain(p.InstanceId, had));
        }
    }
}
