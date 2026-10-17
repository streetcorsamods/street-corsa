using LiteDB;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Tests;

/// <summary>
/// Parts that can be fixed (roadmap step 30, phases 2 and 4): the machine shop's cuts (a block bored, a crank ground, a
/// head milled) and its speed work, from parts/rebuilds.json; and the quality roll on the player's own work
/// </summary>
public sealed class MachiningTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    private static PartInstance OfKind(PartInstance root, string kind) =>
        root.SelfAndDescendants().First(p => Catalog.Get(p.DefinitionId) is { } d && Catalog.WorkOrder.KindOf(d) == kind);

    private static RebuildJob Job(string id) => Catalog.Rebuilds.All.Single(j => j.Id == id);

    private static PartInstance Engine() => TestCars.WithRealEngine(Catalog).Engine!;

    private static double Power(PartInstance engine) => EngineTuner.PowerOf(Catalog, engine) ?? throw new InvalidOperationException("no power");

    [Fact]
    public void The_machine_jobs_are_not_offered_in_the_garage()
    {
        var block = Engine();
        block.Wear = 0.2;

        var garage = Rebuilding.Offers(Catalog, block, loose: true);
        Assert.DoesNotContain(garage, o => o.Job.IsMachine);
        Assert.Contains(Machining.Offers(Catalog, block), o => o.Job.Id == "bore");
    }

    [Fact]
    public void A_bore_goes_to_its_next_size_with_new_pistons_to_suit_until_none_is_left()
    {
        var block = Engine();
        var pistonsBefore = OfKind(block, "pistons");
        pistonsBefore.Wear = 0.3;
        block.Wear = 0.2;
        var bore = Job("bore");

        Assert.Empty(Machining.Apply(Catalog, bore, block));
        Assert.Equal(1, block.CutOf("bore"));
        Assert.Equal(0.97, block.Wear, 3);

        var pistons = OfKind(block, "pistons");
        Assert.NotSame(pistonsBefore, pistons);
        Assert.Equal(pistonsBefore.DefinitionId, pistons.DefinitionId);
        Assert.Equal(1, pistons.CutOf("bore"));
        Assert.Equal(1, pistons.Wear, 3);
        Assert.Equal(pistonsBefore.ParentSlot, pistons.ParentSlot);

        Machining.Apply(Catalog, bore, block);
        Machining.Apply(Catalog, bore, block);
        Assert.Equal(3, block.CutOf("bore"));
        Assert.Equal(PartWords.Say("rebuild.why.no_cuts_left"), Machining.Offers(Catalog, block).Single(o => o.Job.Id == "bore").WhyNot);

        // Past the last size: nothing more comes off
        Assert.Empty(Machining.Apply(Catalog, bore, block));
        Assert.Equal(3, block.CutOf("bore"));
    }

    [Fact]
    public void A_bare_block_bored_comes_back_with_a_set_of_its_stock_pistons_of_the_size()
    {
        var block = new PartInstance(Engine().DefinitionId) { Wear = 0.3 };

        var extras = Machining.Apply(Catalog, Job("bore"), block);

        var pistons = Assert.Single(extras);
        Assert.Equal("pistons", Catalog.WorkOrder.KindOf(Catalog.Get(pistons.DefinitionId)!));
        Assert.Equal(1, pistons.CutOf("bore"));
        Assert.Contains(Machining.Offers(Catalog, new PartInstance(block.DefinitionId)).Single(o => o.Job.Id == "bore").Fitted, n => n.Length > 0);
    }

    [Fact]
    public void Pistons_go_only_into_a_block_bored_to_their_size()
    {
        var block = Engine();
        var stand = PartTrees.Clone(block);
        var pistons = OfKind(stand, "pistons");
        var holder = stand.SelfAndDescendants().First(p => p.Children.Contains(pistons));
        holder.Children.Remove(pistons);
        stand.Cuts["bore"] = 1;

        Assert.NotNull(EngineWork.WhyNotOn(Catalog, stand, pistons, onStand: true));

        var suited = new PartInstance(pistons.DefinitionId) { ParentSlot = pistons.ParentSlot, OwnSlot = pistons.OwnSlot };
        Machining.Apply(Catalog, Job("pistons_to_suit"), suited, step: 1);
        Assert.Equal(1, suited.CutOf("bore"));
        Assert.Null(Machining.WhyNotTogether(Catalog, [stand, suited], [suited]));
    }

    [Fact]
    public void Pistons_to_suit_are_offered_at_every_size_but_their_own()
    {
        var pistons = new PartInstance(OfKind(Engine(), "pistons").DefinitionId);
        pistons.Cuts["bore"] = 2;

        var offers = Machining.Offers(Catalog, pistons).Where(o => o.Job.Id == "pistons_to_suit").ToList();
        Assert.Equal([1, 3], offers.Select(o => o.Step));
        Assert.All(offers, o => Assert.True(o.CanDo));
    }

    [Fact]
    public void The_cuts_reach_the_dyno_and_the_speed_work_makes_power()
    {
        var stock = Power(Engine());

        var bored = Engine();
        Machining.Apply(Catalog, Job("bore"), bored);
        Machining.Apply(Catalog, Job("bore"), bored);
        Machining.Apply(Catalog, Job("bore"), bored);
        Assert.True(Power(bored) > stock, $"bored +.060: {Power(bored):0} hp against {stock:0}");

        var milled = Engine();
        var head = OfKind(milled, "cylinder_head");
        head.Cuts["mill"] = 3;
        Assert.True(Power(milled) > stock, $"milled .030: {Power(milled):0} hp against {stock:0}");

        var ported = Engine();
        foreach (var each in ported.SelfAndDescendants().Where(p => Catalog.Get(p.DefinitionId) is { } d && Catalog.WorkOrder.KindOf(d) == "cylinder_head"))
            each.Cuts["port"] = 1;
        Assert.True(Power(ported) > stock, $"ported: {Power(ported):0} hp against {stock:0}");
    }

    [Fact]
    public void A_cut_takes_from_the_parts_worth_and_speed_work_adds_to_it()
    {
        var block = new PartInstance(Engine().DefinitionId);
        var definition = Catalog.Get(block.DefinitionId)!;
        var standard = PartPricing.Worth(definition, block);

        block.Cuts["bore"] = 3;
        Assert.Equal(standard * 0.65, PartPricing.Worth(definition, block), 6);

        var head = new PartInstance(OfKind(Engine(), "cylinder_head").DefinitionId);
        var headDefinition = Catalog.Get(head.DefinitionId)!;
        var plain = PartPricing.Worth(headDefinition, head);
        head.Cuts["port"] = 1;
        Assert.True(PartPricing.Worth(headDefinition, head) > plain);
    }

    [Fact]
    public void The_machine_shops_price_is_the_works_own_whatever_the_engine_is_worth()
    {
        var block = Engine();
        foreach (var part in block.SelfAndDescendants()) part.Wear = 0.05;
        var job = Job("bore");
        var pistons = Catalog.Get(OfKind(block, "pistons").DefinitionId)!;
        var own = PartPricing.Round((double)Rebuilding.CostOf(job, Catalog.Get(block.DefinitionId)!) + PartPricing.NewPrice(pistons) * job.FitsNewCost);

        Assert.Equal(own, Machining.Offers(Catalog, block).Single(o => o.Job.Id == "bore").Cost);
        Assert.Equal(own, Machining.Offers(Catalog, new PartInstance(block.DefinitionId) { Wear = 0.95 }).Single(o => o.Job.Id == "bore").Cost);
    }

    [Fact]
    public void Speed_work_leaves_the_wear_and_is_done_once()
    {
        var head = new PartInstance(OfKind(Engine(), "cylinder_head").DefinitionId) { Wear = 0.6 };
        var port = Job("port_polish");

        Machining.Apply(Catalog, port, head);
        Assert.Equal(0.6, head.Wear, 6);
        Assert.Equal(1, head.CutOf("port"));
        Assert.Equal(PartWords.Say("rebuild.why.done_already"), Machining.WhyNot(port, head));
    }

    [Fact]
    public void A_job_that_cuts_nothing_can_be_done_again_once_the_part_wants_it()
    {
        var radiator = Catalog.Parts.Values.First(p => Catalog.WorkOrder.KindOf(p) == "radiator");
        var part = new PartInstance(radiator.Id) { Wear = 0.4 };
        var recore = Job("radiator_recore");

        Assert.Null(Machining.WhyNot(recore, part));
        Machining.Apply(Catalog, recore, part);
        Assert.Equal(PartWords.Say("rebuild.why.no_need"), Machining.WhyNot(recore, part));
        Assert.Empty(part.Cuts);
    }

    [Fact]
    public void The_cuts_are_kept_through_a_copy_and_a_save()
    {
        var block = Engine();
        Machining.Apply(Catalog, Job("bore"), block);

        Assert.Equal(1, PartTrees.Clone(block).CutOf("bore"));
        var live = PartTrees.ToInstalled(Catalog, block)!;
        Assert.Equal(1, PartTrees.FromInstalled(live.Root).CutOf("bore"));
        Assert.NotNull(live.Root.CutFigures);

        var doc = BsonMapper.Global.ToDocument(block);
        var back = BsonMapper.Global.ToObject<PartInstance>(doc);
        Assert.Equal(1, back.CutOf("bore"));
        Assert.Equal(1, OfKind(back, "pistons").CutOf("bore"));

        // A save from before the machine shop: a part without cuts reads as standard
        doc.Remove(nameof(PartInstance.Cuts));
        Assert.Equal(0, BsonMapper.Global.ToObject<PartInstance>(doc).CutOf("bore"));
    }

    [Fact]
    public void The_players_own_work_lands_round_what_the_job_can_never_under_its_roll()
    {
        var kit = Job("carb_kit");

        Assert.Equal((kit.Restores - PartRebuilds.KitRoll, Rebuilding.Rough), Rebuilding.Landing(kit, 0));
        var (top, quality) = Rebuilding.Landing(kit, 1);
        Assert.True(top > kit.Restores);
        Assert.Equal(Rebuilding.Good, quality);
        Assert.Equal((Job("rebuilt_exchange").Restores, (string?)null), Rebuilding.Landing(Job("rebuilt_exchange"), 0));

        // Never worse than the part was
        var carb = new PartInstance(OfKind(Engine(), "carburettor").DefinitionId) { Wear = 0.93 };
        var outcome = Rebuilding.Do(kit, carb, new Random(1));
        Assert.True(carb.Wear >= 0.93);
        Assert.Equal(carb.Wear, outcome.Wear, 9);
    }

    [Fact]
    public void A_mods_machine_jobs_are_checked()
    {
        _temp.File("mod/rebuilds.json", """
            { "jobs": [
              { "id": "a", "kinds": [ "block" ], "way": "machine", "cut": "bore", "restores": 0.9 },
              { "id": "b", "kinds": [ "block" ], "way": "machine", "cut": "deck", "restores": 0, "days": 99,
                "sizes": [ { "id": "1", "figures": { "chamber": { "times": 0.9 }, "nonsense": 3 }, "worth": 9 } ] },
              { "id": "c", "kinds": [ "block" ], "cut": "bore", "restores": 0.9 },
              { "id": "d", "kinds": [ "block" ], "way": "machine", "restores": 0 }
            ] }
            """);
        var problems = new List<string>();

        var book = PartRebuilds.Load([("mymod", false, _temp.Combine("mod"))], problems);

        Assert.Equal(["b", "c"], book.All.Select(j => j.Id));
        var b = book.All[0];
        Assert.Equal(3, b.Days);
        Assert.Equal(1, b.Sizes[0].Worth);
        Assert.Single(b.Sizes[0].ReadFigures!.Numbers);
        Assert.Null(book.All[1].Cut);
        Assert.Equal(6, problems.Count);
    }

    [Fact]
    public void Diagnostic_what_the_machine_shop_asks_and_does()
    {
        var output = TestContext.Current.TestOutputHelper!;
        var engine = Engine();
        var stock = Power(engine);
        output.WriteLine($"stock {stock:0} hp");
        foreach (var part in engine.SelfAndDescendants())
        {
            foreach (var offer in Machining.Offers(Catalog, part))
            {
                var trial = Engine();
                var same = trial.SelfAndDescendants().First(p => p.DefinitionId == part.DefinitionId);
                Machining.Apply(Catalog, offer.Job, same, offer.Job.AnySize ? offer.Step : null);
                output.WriteLine($"{part.DefinitionId}: {offer.Name} ${offer.Cost} {offer.Days}d -> {Power(trial):0} hp; worth new {PartPricing.NewPrice(Catalog.Get(part.DefinitionId)!):0}");
            }
        }
    }
}
