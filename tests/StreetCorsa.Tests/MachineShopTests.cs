using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;

namespace StreetCorsa.Tests;

/// <summary>The machine shop on the part shops' map: parts left there from the shelf or the stand, and brought back</summary>
public sealed class MachineShopTests
{
    private static readonly CatalogParts Parts = new();

    private static readonly PartShopDefinition Machinist = new() { Id = "gardena", Name = "Gardena Machine", Region = "Gardena", Kind = "machine" };

    private readonly GameState _state = new();
    private readonly PartShopService _service;

    public MachineShopTests()
    {
        _service = new PartShopService(new FakePartShops(Machinist), Parts, new NewspaperPartsService(Parts));
        PartShopRoster.Sync(_state, _service.Catalog);
        _state.Date = new DateTime(1965, 6, 1, 10, 0, 0);
        _state.Player.Money = 100_000;
    }

    private PartShopLocation Shop => PartShopRoster.Find(_state, "gardena")!;

    private IMachineShopService Machine => _service.Machine;

    private static PartInstance Engine() => TestCars.WithRealEngine(Parts.Catalog).Engine!;

    private MachineWork Work(MachinePiece piece, string jobId) =>
        Machine.Offers(_state, Shop, piece).First(w => w.Offer.Job.Id == jobId && w.Offer.CanDo);

    [Fact]
    public void A_bare_block_off_the_shelf_is_left_paid_for_and_comes_back_bored_with_its_pistons()
    {
        var block = new PartInstance(Engine().DefinitionId) { Wear = 0.3 };
        _state.Player.Parts.Add(block);
        var piece = Assert.Single(Machine.Pieces(_state, Shop));
        Assert.False(piece.AtShop);

        var work = Work(piece, "bore");
        var money = _state.Player.Money;
        var order = Machine.Order(_state, Shop, work, new Random(1));

        Assert.NotNull(order);
        Assert.Empty(_state.Player.Parts);
        Assert.Equal(money - work.Offer.Cost, _state.Player.Money);
        Assert.Equal(work.Offer.Cost, Shop.PlayerSpent);
        Assert.Equal(_state.Date.Date.AddDays(work.Offer.Days), order.ReadyOn);
        // The block and the set of pistons made for it
        Assert.Equal(2, _state.Player.MachineWork.Count);
        Assert.True(Machine.Pieces(_state, Shop).All(p => p.AtShop));

        Assert.Empty(Machine.Deliver(_state, order.ReadyOn.AddDays(-1)));
        Assert.Equal(2, Machine.Deliver(_state, order.ReadyOn).Count);
        Assert.Empty(_state.Player.MachineWork);
        Assert.Equal(2, _state.Player.Parts.Count);
        Assert.All(_state.Player.Parts, p => Assert.Equal(1, p.CutOf("bore")));
        // Told of as back from the shop, not as the mail
        Assert.Empty(_state.Player.Delivered);
        Assert.Equal(2, _state.Player.MachineReturned.Count);
    }

    [Fact]
    public void A_bare_blocks_new_pistons_wait_as_long_as_the_block_does()
    {
        var block = new PartInstance(Engine().DefinitionId) { Wear = 0.3 };
        _state.Player.Parts.Add(block);
        Machine.Order(_state, Shop, Work(Machine.Pieces(_state, Shop).Single(), "bore"), new Random(1));

        var atShop = Machine.Pieces(_state, Shop).Single(p => ReferenceEquals(p.Part, block));
        var order = Machine.Order(_state, Shop, Work(atShop, "blueprint"), new Random(1))!;

        Assert.All(_state.Player.MachineWork, o => Assert.Equal(order.ReadyOn, o.ReadyOn));
    }

    [Fact]
    public void An_engine_back_on_the_stand_is_said_to_be_there()
    {
        _state.Player.EngineStand = Engine();
        var order = Machine.Order(_state, Shop, Work(Machine.Pieces(_state, Shop).Single(p => p.FromStand), "blueprint"), new Random(1))!;

        Machine.Deliver(_state, order.ReadyOn);

        Assert.Contains(Services.Text.GameText.Say("machine.delivered.stand", ("part", Parts.Catalog.Get(order.Part.DefinitionId)!.DisplayName ?? Parts.Catalog.Get(order.Part.DefinitionId)!.Name), ("shop", order.ShopName)),
            _state.Player.MachineReturned);
    }

    [Fact]
    public void The_offers_priced_on_a_quote_are_the_offers_at_the_shop()
    {
        var engine = Engine();
        foreach (var part in engine.SelfAndDescendants()) part.Wear = 0.4;
        _state.Player.EngineStand = engine;
        var piece = Machine.Pieces(_state, Shop).Single(p => p.FromStand);
        var quote = Machine.QuoteAt(_state, Shop)!;

        // On a copy, as the counter prices them off the UI thread
        var copy = piece with { Part = PartTrees.Clone(engine, keepIds: true) };
        var shop = Machine.Offers(_state, Shop, piece).Select(w => (w.Part.InstanceId, w.Offer.Job.Id, w.Offer.Step, w.Offer.Cost)).ToList();
        var copied = Machine.Offers(quote, copy).Select(w => (w.Part.InstanceId, w.Offer.Job.Id, w.Offer.Step, w.Offer.Cost)).ToList();
        Assert.Equal(shop, copied);
        Assert.Empty(engine.Cuts);
    }

    [Fact]
    public void An_engine_off_the_stand_goes_back_on_it_and_work_added_while_it_is_there_keeps_it_longer()
    {
        var engine = Engine();
        _state.Player.EngineStand = engine;
        var piece = Machine.Pieces(_state, Shop).Single(p => p.FromStand);

        var bore = Work(piece, "bore");
        var first = Machine.Order(_state, Shop, bore, new Random(1))!;
        Assert.Null(_state.Player.EngineStand);

        var atShop = Machine.Pieces(_state, Shop).Single(p => p.AtShop);
        var blueprint = Work(atShop, "blueprint");
        var again = Machine.Order(_state, Shop, blueprint, new Random(1));
        Assert.Same(first, again);
        Assert.Single(_state.Player.MachineWork);
        Assert.Equal(_state.Date.Date.AddDays(Math.Max(blueprint.Offer.Days, bore.Offer.Days)), first.ReadyOn);

        Machine.Deliver(_state, first.ReadyOn);
        Assert.Same(engine, _state.Player.EngineStand);
        Assert.Equal(1, engine.CutOf("blueprint"));
    }

    [Fact]
    public void An_engine_back_to_a_stand_taken_meanwhile_goes_on_the_shelf()
    {
        _state.Player.EngineStand = Engine();
        var order = Machine.Order(_state, Shop, Work(Machine.Pieces(_state, Shop).Single(p => p.FromStand), "bore"), new Random(1))!;
        _state.Player.EngineStand = Engine();

        Machine.Deliver(_state, order.ReadyOn);

        Assert.Contains(order.Part, _state.Player.Parts);
    }

    [Fact]
    public void Nothing_is_left_at_a_shop_that_has_shut_or_without_the_money()
    {
        _state.Player.Parts.Add(new PartInstance(Engine().DefinitionId) { Wear = 0.3 });
        var work = Work(Machine.Pieces(_state, Shop).Single(), "bore");

        _state.Player.Money = work.Offer.Cost - 1;
        Assert.Null(Machine.Order(_state, Shop, work, new Random(1)));

        _state.Player.Money = 100_000;
        Shop.Status = DealerStatus.Closed;
        Assert.Null(Machine.Order(_state, Shop, work, new Random(1)));
        Assert.Single(_state.Player.Parts);
    }

    [Fact]
    public void A_regular_pays_less_for_the_work()
    {
        var crank = Engine().SelfAndDescendants().First(p => Parts.Catalog.WorkOrder.KindOf(Parts.Catalog.Get(p.DefinitionId)!) == "crankshaft");
        _state.Player.Parts.Add(new PartInstance(crank.DefinitionId) { Wear = 0.3 });
        var piece = Machine.Pieces(_state, Shop).Single();
        var before = Work(piece, "crank_grind").Offer.Cost;

        Shop.PlayerSpent = PartShopRules.GoodCustomerSpent;

        Assert.True(Work(piece, "crank_grind").Offer.Cost < before, $"{before} for a stranger, {Work(piece, "crank_grind").Offer.Cost} for a good customer");
    }

    [Fact]
    public void Pistons_in_a_block_are_offered_new_at_the_blocks_size_only()
    {
        var engine = Engine();
        _state.Player.EngineStand = engine;
        var piece = Machine.Pieces(_state, Shop).Single(p => p.FromStand);
        Assert.DoesNotContain(Machine.Offers(_state, Shop, piece), w => w.Offer.Job.Id == "pistons_to_suit");

        // Bored, with the old pistons back in: new ones of its size
        engine.Cuts["bore"] = 2;
        var suit = Assert.Single(Machine.Offers(_state, Shop, piece), w => w.Offer.Job.Id == "pistons_to_suit");
        Assert.Equal(2, suit.Offer.Step);
    }

    [Fact]
    public void Work_on_a_part_of_an_engine_is_charged_for_what_the_engine_gains()
    {
        var engine = Engine();
        foreach (var part in engine.SelfAndDescendants()) part.Wear = 0.35;
        _state.Player.EngineStand = engine;
        var piece = Machine.Pieces(_state, Shop).Single(p => p.FromStand);
        var cam = Machine.Offers(_state, Shop, piece).First(w => w.Offer.Job.Id == "hot_grind");

        var alone = Machining.CostFor(Parts.Catalog, cam.Offer.Job, Parts.Catalog.Get(cam.Part.DefinitionId)!, cam.Part, valueShare: PartShopRules.MachineValueShare);
        Assert.True(cam.Offer.Cost >= alone);
    }

    [Fact]
    public void A_machine_shop_buys_nothing_and_stocks_nothing()
    {
        var part = new PartInstance(Engine().DefinitionId);
        _state.Player.Parts.Add(part);

        Assert.False(_service.Sell(_state, Shop, part));
        Assert.Contains(part, _state.Player.Parts);
        Assert.Empty(_service.AssortmentOf(Machinist));
    }

    [Fact]
    public async Task A_machine_shop_is_never_restocked()
    {
        await _service.RestockAsync(_state, _state.Date, new Random(1));

        Assert.Null(Shop.RestockedOn);
        Assert.Empty(Shop.Shelves);
    }
}
