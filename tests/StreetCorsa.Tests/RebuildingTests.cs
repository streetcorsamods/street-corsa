using Newtonsoft.Json.Linq;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.Garage;

namespace StreetCorsa.Tests;

/// <summary>Parts that can be fixed: kits and exchange units from parts/rebuilds.json</summary>
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

    [Fact]
    public void Every_base_jobs_steps_have_words_and_name_their_sounds()
    {
        // The words are the core's; the sounds are its setting's (the core has only the menus' clicks), so a step names
        // its cue, and a game whose mods have none plays the step quiet
        var lines = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "text", "ui_parts.json")))["lines"]!;

        foreach (var job in Catalog.Rebuilds.All)
        {
            Assert.NotEmpty(job.Steps);
            foreach (var step in job.Steps)
            {
                Assert.NotNull(lines[$"rebuild.{job.Id}.{step.Id}"]);
                Assert.False(string.IsNullOrWhiteSpace(step.Sound), $"{job.Id}.{step.Id} names no sound");
            }
        }
    }

    [Fact]
    public void A_step_without_an_id_is_told_and_left_out()
    {
        _temp.File("base/rebuilds.json", """
            { "jobs": [ { "id": "a", "kinds": [ "carburettor" ], "steps": [ { "id": "strip" }, { "sound": "work.wash" } ] } ] }
            """);
        var problems = new List<string>();

        var book = PartRebuilds.Load([(ModIds.BaseId, true, _temp.Combine("base"))], problems);

        Assert.Equal(["strip"], book.All.Single().Steps.Select(s => s.Id));
        Assert.Equal("rebuilds.json: a: a step without an id is left out", Assert.Single(problems));
    }

    [Fact]
    public void A_show_is_longer_for_a_longer_job_but_never_keeps_the_player_long()
    {
        Assert.Equal(4, RebuildShow.SecondsFor(5));
        Assert.Equal(10, RebuildShow.SecondsFor(24 * 60));
        Assert.InRange(RebuildShow.SecondsFor(60), 6, 7);
        Assert.True(RebuildShow.SecondsFor(240) > RebuildShow.SecondsFor(90));
    }

    [Fact]
    public void A_show_goes_through_its_steps_its_condition_and_its_clock()
    {
        var job = Catalog.Rebuilds.All.Single(j => j.Id == "carb_kit");
        var start = new DateTime(1965, 6, 1, 9, 0, 0);
        var show = new RebuildShow(job, "Rebuild", 0.4, 0.95, 90, start);

        Assert.Equal(job.Steps.Count, show.Steps.Count);
        Assert.Equal(0, show.StepAt(0));
        Assert.Equal(show.Steps.Count - 1, show.StepAt(1));
        Assert.Equal(1, show.StepAt(1.0 / show.Steps.Count + 0.01));
        Assert.Equal(0.4, show.WearAt(0), 6);
        Assert.Equal(0.95, show.WearAt(1), 6);
        Assert.Equal(0.675, show.WearAt(0.5), 6);
        Assert.Equal(start, show.ClockAt(0));
        Assert.Equal(start.AddMinutes(90), show.ClockAt(1));
        Assert.Equal(start.AddMinutes(90), show.ClockAt(5));
    }

    [Fact]
    public void A_job_without_steps_shows_its_name_over_the_bench()
    {
        var show = new RebuildShow(new RebuildJob { Id = "x", Kinds = ["block"] }, "Clean it", 0.5, 0.8, 30, DateTime.Today);

        Assert.Equal([("Clean it", RebuildShow.BenchSound, (string?)null)], show.Steps);
    }

    [Fact]
    public void A_worn_part_carries_more_grime_and_a_new_one_none()
    {
        Assert.Equal(0, StreetCorsa.Looks.PartGrime.Of(1));
        Assert.Equal(StreetCorsa.Looks.PartGrime.MostGrime, StreetCorsa.Looks.PartGrime.Of(0));
        Assert.True(StreetCorsa.Looks.PartGrime.Of(0.3) > StreetCorsa.Looks.PartGrime.Of(0.6));
        Assert.Equal(0, StreetCorsa.Looks.PartGrime.Of(double.NaN));
    }

    [Fact]
    public void A_show_ends_on_the_games_clock_once_the_time_is_spent()
    {
        var start = new DateTime(1965, 6, 1, 19, 0, 0);
        var show = new RebuildShow(new RebuildJob { Id = "x", Kinds = ["transmission"] }, "Gearbox", 0.4, 0.9, 360, start);
        var nextMorning = new DateTime(1965, 6, 2, 8, 0, 0);

        // Not spent yet: the job's own minutes
        Assert.Equal(start.AddMinutes(360), show.ClockAt(1, start));

        // Spent, past the end of the day: the show ends where the game is
        Assert.Equal(nextMorning, show.ClockAt(1, nextMorning));
        Assert.Equal(start, show.ClockAt(0, nextMorning));
        Assert.True(show.ClockAt(0.5, nextMorning) > start.AddMinutes(180));
    }

    [Fact]
    public void A_part_bought_worn_worked_on_and_sold_on_is_no_money_made()
    {
        const double mostPaid = 0.42;
        foreach (var jobId in new[] { "carb_kit", "rebuilt_exchange" })
        {
            var carb = new PartInstance(OfKind(TestCars.WithRealEngine(Catalog).Engine!, "carburettor").DefinitionId) { Wear = 0.05, Tear = 0.3 };
            var before = PartPricing.WorthOfAssembly(Catalog, carb);
            var offer = Rebuilding.Offers(Catalog, carb, loose: true, resaleFactor: mostPaid).Single(o => o.Job.Id == jobId);
            Assert.True(offer.Cost >= Rebuilding.CostOf(offer.Job, Catalog.Get(carb.DefinitionId)!));

            Rebuilding.Apply(offer.Job, carb);
            var gained = (decimal)((PartPricing.WorthOfAssembly(Catalog, carb) - before) * mostPaid);
            Assert.True(offer.Cost >= gained, $"{jobId}: ${offer.Cost} for ${gained} more at the shop");
        }
    }

    [Fact]
    public void Jobs_for_kinds_shelves_or_sounds_nobody_has_are_told()
    {
        _temp.File("mod/rebuilds.json", """
            { "jobs": [ { "id": "a", "kinds": [ "carburetor" ], "restores": 0.9, "steps": [ { "id": "s", "sound": "work.wsh", "start": "work.bench" } ] },
                        { "id": "b", "groups": [ "brakes", "flux" ], "restores": 0.9 } ] }
            """);
        var book = PartRebuilds.Load([("mymod", false, _temp.Combine("mod"))], []);

        var names = book.UnknownNames(["carburettor"], ["brakes"]);
        Assert.Equal(2, names.Count);
        Assert.Contains(names, p => p.StartsWith("mymod/rebuilds.json: a ", StringComparison.Ordinal) && p.Contains("\"carburetor\""));
        Assert.Contains(names, p => p.StartsWith("mymod/rebuilds.json: b ", StringComparison.Ordinal) && p.Contains("\"flux\""));

        var sounds = book.UnknownSounds(cue => cue == "work.bench");
        Assert.Contains("\"work.wsh\"", Assert.Single(sounds));
    }

    [Fact]
    public void A_part_on_the_bench_turns_round_or_lies_along_it_clear_of_the_board_and_the_vise()
    {
        var bench = System.Numerics.Matrix4x4.CreateRotationY(0.6f) * System.Numerics.Matrix4x4.CreateTranslation(3f, 0f, -1.75f);

        // A carburettor turns round
        var small = new System.Numerics.Vector3(0.2f, 0.15f, 0.2f);
        Assert.Equal(1.3f, WorkbenchLayout.YawOf(-small, small, 1.3f));

        // A gearbox lies along the bench, whichever way its model runs, and never reaches the tool board
        foreach (var half in new[] { new System.Numerics.Vector3(0.4f, 0.2f, 0.15f), new System.Numerics.Vector3(0.15f, 0.2f, 0.4f) })
        {
            for (var turn = 0f; turn < 20f; turn += 0.1f)
            {
                var placement = WorkbenchLayout.PlacementOf(System.Numerics.Matrix4x4.Identity, -half, half, turn, 0f);
                foreach (var x in new[] { -half.X, half.X })
                {
                    foreach (var z in new[] { -half.Z, half.Z })
                    {
                        var corner = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(x, 0f, z), placement);
                        Assert.InRange(corner.Z - WorkbenchLayout.WorkSpot.Z, -WorkbenchLayout.ClearHalfDepth - 1e-4f, WorkbenchLayout.ClearHalfDepth + 1e-4f);
                        Assert.InRange(corner.X, -WorkbenchLayout.HalfLength, WorkbenchLayout.HalfLength - 0.3f);
                    }
                }
            }
        }

        // On a bench stood anywhere, the part's middle is over the bench's work spot, its bottom on the top
        var box = new System.Numerics.Vector3(0.1f, 0.1f, 0.1f);
        var lying = WorkbenchLayout.PlacementOf(bench, -box, box, 0.7f, 0f);
        var middle = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(0f, -0.1f, 0f), lying);
        var top = WorkbenchLayout.TopOf(bench);
        Assert.Equal(top.X, middle.X, 4);
        Assert.Equal(top.Y, middle.Y, 4);
        Assert.Equal(top.Z, middle.Z, 4);
    }
}
