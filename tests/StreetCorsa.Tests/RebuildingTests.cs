using Newtonsoft.Json.Linq;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Tests;

/// <summary>Parts that can be fixed (roadmap step 30, phase 1): kits and exchange units from parts/rebuilds.json</summary>
public sealed class RebuildingTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static PartsCatalog Catalog => TestCars.Catalog.Value;

    /// <summary>The first part of the work order kind in the tree</summary>
    private static PartInstance OfKind(PartInstance root, string kind) =>
        root.SelfAndDescendants().First(p => Catalog.Get(p.DefinitionId) is { } d && Catalog.WorkOrder.KindOf(d) == kind);

    private static RebuildOffer Offer(PartInstance part, string jobId, bool loose, double priceMultiplier = 1.0) =>
        Rebuilding.Offers(Catalog, part, loose, priceMultiplier).Single(o => o.Job.Id == jobId);

    [Fact]
    public void The_base_games_jobs_load_without_problems_and_name_kinds_and_shelves_there_are()
    {
        Assert.DoesNotContain(Catalog.Problems, p => p.Contains(PartRebuilds.FileName, StringComparison.Ordinal));
        Assert.NotEmpty(Catalog.Rebuilds.All);

        var kinds = Catalog.Parts.Values.Select(Catalog.WorkOrder.KindOf).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var job in Catalog.Rebuilds.All)
        {
            Assert.All(job.Kinds, k => Assert.Contains(k, kinds));
            Assert.All(job.Groups, g => Assert.NotNull(PartGroups.Current.Get(g)));
        }
    }

    [Fact]
    public void Every_base_job_has_its_words()
    {
        var lines = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "text", "ui_parts.json")))["lines"]!;
        Assert.All(Catalog.Rebuilds.All, job => Assert.NotNull(lines["rebuild." + job.Id]));
    }

    [Fact]
    public void A_carburettor_is_rebuilt_off_the_engine_and_comes_up_to_what_the_kit_can()
    {
        var car = TestCars.WithRealEngine(Catalog);
        var carb = OfKind(car.Engine!, "carburettor");
        carb.Wear = 0.4;

        Assert.Equal(PartWords.Say("rebuild.why.off"), Offer(carb, "carb_kit", loose: false).WhyNot);
        Assert.Equal(PartWords.Say("rebuild.why.off.exchange"), Offer(carb, "rebuilt_exchange", loose: false).WhyNot);

        var kit = Offer(carb, "carb_kit", loose: true);
        Assert.True(kit.CanDo);
        Assert.True(kit.Cost >= 8);
        Assert.Equal(0.95, kit.After, 3);

        Rebuilding.Apply(kit.Job, carb);
        Assert.Equal(0.95, carb.Wear, 3);
        Assert.Equal(PartWords.Say("rebuild.why.no_need"), Offer(carb, "carb_kit", loose: true).WhyNot);
    }

    [Fact]
    public void Kits_come_before_exchange_units()
    {
        var car = TestCars.WithRealEngine(Catalog);
        var offers = Rebuilding.Offers(Catalog, OfKind(car.Engine!, "carburettor"), loose: true);
        Assert.Equal(["carb_kit", "rebuilt_exchange"], offers.Select(o => o.Job.Id));
    }

    [Fact]
    public void A_tune_up_is_done_with_the_distributor_in_the_car()
    {
        var car = TestCars.WithRealEngine(Catalog);
        var distributor = OfKind(car.Engine!, "distributor");
        distributor.Wear = 0.5;

        Assert.True(Offer(distributor, "tune_up", loose: false).CanDo);
    }

    [Fact]
    public void Pistons_too_worn_for_a_ring_job_want_a_machine_shop_and_a_ring_job_wants_them_out_and_bare()
    {
        var pistons = new PartInstance(OfKind(TestCars.WithRealEngine(Catalog).Engine!, "pistons").DefinitionId) { Wear = 0.2 };
        Assert.Equal(PartWords.Say("rebuild.why.too_worn"), Offer(pistons, "ring_job", loose: true).WhyNot);

        pistons.Wear = 0.5;
        Assert.Equal(PartWords.Say("rebuild.why.off"), Offer(pistons, "ring_job", loose: false).WhyNot);
        pistons.Children.Add(new PartInstance("anything"));
        Assert.Equal(PartWords.Say("rebuild.why.bare"), Offer(pistons, "ring_job", loose: true).WhyNot);
        pistons.Children.Clear();
        Assert.True(Offer(pistons, "ring_job", loose: true).CanDo);
    }

    [Fact]
    public void A_kit_never_makes_a_part_worse_and_leaves_its_damage_to_the_repair_shop()
    {
        var carb = new PartInstance(OfKind(TestCars.WithRealEngine(Catalog).Engine!, "carburettor").DefinitionId) { Wear = 0.97, Tear = 0.5 };
        var job = Catalog.Rebuilds.All.Single(j => j.Id == "carb_kit");

        Rebuilding.Apply(job, carb);
        Assert.Equal(0.97, carb.Wear, 3);
        Assert.Equal(0.5, carb.Tear, 3);
    }

    [Fact]
    public void A_rebuilt_unit_from_the_counter_takes_the_damage_off_a_part_with_little_mileage()
    {
        var pump = new PartInstance(OfKind(TestCars.WithRealEngine(Catalog).Engine!, "water_pump").DefinitionId) { Wear = 0.99, Tear = 0.4 };

        var swap = Offer(pump, "rebuilt_exchange", loose: true);
        Assert.True(swap.CanDo);

        Rebuilding.Apply(swap.Job, pump);
        Assert.Equal(0.99, pump.Wear, 3);
        Assert.Equal(1, pump.Tear, 3);
    }

    [Fact]
    public void The_cost_is_a_share_of_the_new_price_at_the_saves_part_prices()
    {
        var carb = new PartInstance(OfKind(TestCars.WithRealEngine(Catalog).Engine!, "carburettor").DefinitionId) { Wear = 0.3 };
        var definition = Catalog.Get(carb.DefinitionId)!;
        var swap = Offer(carb, "rebuilt_exchange", loose: true);

        Assert.Equal(PartPricing.Round(Math.Max(10, 0.45 * PartPricing.NewPrice(definition))), swap.Cost);
        Assert.Equal(PartPricing.Round(Math.Max(10, 0.45 * PartPricing.NewPrice(definition)) * 2), Offer(carb, "rebuilt_exchange", true, 2).Cost);
    }

    [Fact]
    public void A_brake_is_relined_by_its_shelf_on_the_car()
    {
        var brake = Catalog.Parts.Values.First(p => PartKinds.GroupOf(p) == "brakes");
        var part = new PartInstance(brake.Id) { Wear = 0.3 };

        Assert.True(Offer(part, "brake_reline", loose: false).CanDo);
    }

    [Fact]
    public void A_mod_replaces_a_job_by_its_id_takes_one_out_and_its_broken_jobs_are_told()
    {
        _temp.File("base/rebuilds.json", """
            { "jobs": [ { "id": "a", "kinds": [ "carburettor" ], "restores": 0.9 }, { "id": "b", "kinds": [ "pistons" ] } ] }
            """);
        _temp.File("mod/rebuilds.json", """
            { "jobs": [ { "id": "A", "kinds": [ "camshaft" ], "restores": 0.8, "way": "exchange" }, { "id": "b", "disabled": true },
                        { "id": "c" }, { "kinds": [ "block" ] }, { "id": "d", "kinds": [ "block" ], "restores": 1.5 },
                        { "id": "e", "kinds": [ "block" ], "way": "magic", "minutes": -5 } ] }
            """);

        var problems = new List<string>();
        var book = PartRebuilds.Load([(ModIds.BaseId, true, _temp.Combine("base")), ("mymod", false, _temp.Combine("mod"))], problems);

        Assert.Equal(["A", "e"], book.All.Select(j => j.Id));
        var a = book.All[0];
        Assert.Equal(["camshaft"], a.Kinds);
        Assert.True(a.IsExchange);
        Assert.Equal(PartRebuilds.Kit, book.All[1].Way);
        Assert.Equal(60, book.All[1].Minutes);
        Assert.Equal(5, problems.Count);
        Assert.All(problems, p => Assert.StartsWith("mymod/rebuilds.json: ", p));
    }

    [Fact]
    public void A_broken_file_is_told_and_its_jobs_left_out()
    {
        _temp.File("base/rebuilds.json", "{ \"jobs\": [ ");
        var problems = new List<string>();

        var book = PartRebuilds.Load([(ModIds.BaseId, true, _temp.Combine("base"))], problems);

        Assert.Empty(book.All);
        Assert.StartsWith("rebuilds.json: ", Assert.Single(problems));
    }
}
