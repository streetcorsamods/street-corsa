using StreetCorsa.Audio;
using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;
using StreetCorsa.Services.CarLot;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>
/// Tech audit 2026-10-18, the parts, part shop, market and rival services: the price book nobody waits for on the UI
/// thread, a save's lost parts told and purged everywhere, shelves merged after a rename, a listing made before the car
/// and the money move, two overlapping reviews of the rivals, the ground file's cap, the figures that became rules, one
/// shape rule, the paper's assortment by a group flag, no built-in dealers, and the scenes dressed the one way
/// </summary>
public sealed class Audit1018ETests : IDisposable
{
    private readonly TempDir _temp = new();

    public Audit1018ETests() => TestLogging.SilenceLogging();

    public void Dispose() => _temp.Dispose();

    private const string Pack = """
        [
          { "id": "engines/t/cam", "class": "camshaft", "slots": [ { "id": 1 } ] },
          { "id": "engines/t/rail", "class": "intake_manifold", "slots": [ { "id": 1 } ] }
        ]
        """;

    private PartsCatalog Catalog(string? aliases = null)
    {
        _temp.Parts("parts/engines/t", Pack);
        if (aliases != null) _temp.File("parts/" + PartFiles.AliasesFileName, aliases);
        return PartsCatalog.Load(_temp.Combine("parts"));
    }

    // ----- 1. The price book: made once, kept, never waited for by a caller that must not -----

    [Fact]
    public void The_price_book_is_not_made_for_a_caller_that_must_not_wait_and_is_kept_once_made()
    {
        var measured = 0;
        IReadOnlyDictionary<string, double>? given = null;
        var folders = new[] { _temp.Combine("parts") };
        Directory.CreateDirectory(folders[0]);
        var path = _temp.Combine("part_power.json");
        var store = new PriceBookStore(() => folders, kept =>
        {
            measured++;
            given = kept;
            return PartPriceBook.Empty;
        }, path);

        Assert.Null(store.IfMade);
        Assert.Equal(0, measured);

        Assert.Same(PartPriceBook.Empty, store.Book);
        Assert.Equal(1, measured);
        Assert.Null(given);
        Assert.NotNull(store.IfMade);
        Assert.Same(PartPriceBook.Empty, store.Book);
        Assert.Equal(1, measured);
        Assert.True(File.Exists(path));

        // The next session finds the measuring kept for the same parts
        var again = new PriceBookStore(() => folders, kept =>
        {
            given = kept;
            return PartPriceBook.Empty;
        }, path);
        _ = again.Book;
        Assert.NotNull(given);
    }

    [Fact]
    public void A_parts_service_does_not_take_the_prices_over_when_it_is_made()
    {
        var book = PartPricing.Book;
        var source = PartPricing.Source;
        var jobs = PartPricing.Jobs;
        var engines = EngineRules.Source;
        var cars = CarPricing.Source;

        _ = new CarPartsService(new MemoryCatalog(), new MemoryProfiles(), _ => false);
        _ = new Services.Catalog.CarProfileService(new MemoryCatalog(), new MemoryProfiles());

        Assert.Same(book, PartPricing.Book);
        Assert.Same(source, PartPricing.Source);
        Assert.Same(jobs, PartPricing.Jobs);
        Assert.Same(engines, EngineRules.Source);
        Assert.Same(cars, CarPricing.Source);
    }

    // ----- 2. A save's lost parts: told, and gone from the yards, the mail order and the machine shops too -----

    [Fact]
    public void Parts_the_catalog_lost_are_purged_everywhere_and_told_while_a_mods_wait()
    {
        var catalog = Catalog();
        var game = GameState.CreateNew("Lost parts");
        game.Player.Parts.Add(new PartInstance("engines/gone/cam"));
        game.Player.Parts.Add(new PartInstance("mymod:engines/x/cam"));
        game.Player.Parts.Add(new PartInstance("engines/t/cam"));
        game.Player.PartOrders.Add(new PartOrder { Part = new PartInstance("engines/gone/rail") });
        game.Player.PartOrders.Add(new PartOrder { Part = new PartInstance("mymod:engines/x/rail") });
        game.Player.MachineWork.Add(new MachineOrder { Part = new PartInstance("engines/gone/cam") });
        var yard = new PartShopLocation { Id = "yard", Name = "Yard" };
        yard.Yard.Add(new PartAd(new PartInstance("engines/gone/cam"), 10m, "Yard"));
        yard.Yard.Add(new PartAd(new PartInstance("engines/t/cam"), 10m, "Yard"));
        game.PartShops.Add(yard);

        Assert.True(new SavedPartsMigration(catalog, AppLoggerFactory.CreateLogger("Test")).BringUpToDate(game));

        Assert.Equal(["mymod:engines/x/cam", "engines/t/cam"], game.Player.Parts.Select(p => p.DefinitionId));
        Assert.Equal(["mymod:engines/x/rail"], game.Player.PartOrders.Select(o => o.Part.DefinitionId));
        Assert.Empty(game.Player.MachineWork);
        Assert.Equal(["engines/t/cam"], yard.Yard.Select(a => a.Part.DefinitionId));
        Assert.Contains(ModProblems.All, p => p.StartsWith("Saved parts:") && p.Contains("engines/gone/cam") && p.Contains("engines/gone/rail") && !p.Contains("mymod:"));
    }

    /// <summary>
    /// A part of the base game that moved to a mod (the Ford six, 2026-10-20): a save of before holds its bare id, which
    /// the base game's alias sends to the mod's. Without the mod the engine waits on the shelf under the mod's id, as one
    /// of a mod's always did, and nothing of it is lost.
    /// </summary>
    [Fact]
    public void A_base_part_that_moved_to_a_mod_waits_for_the_mod_under_its_new_id()
    {
        var catalog = Catalog("""{ "engines/six/block": "six:engines/six/block", "engines/six/cam": "six:engines/six/cam" }""");
        var game = GameState.CreateNew("Moved to a mod");
        var block = new PartInstance("engines/six/block") { ParentSlot = PartInstance.CarEngineSlot };
        block.Children.Add(new PartInstance("engines/six/cam") { ParentSlot = 10, OwnSlot = 1 });
        var car = new Car("car_a") { InstanceId = Guid.NewGuid() };
        car.Parts.Add(block);
        game.Player.Cars.Add(car);
        game.Player.Parts.Add(new PartInstance("engines/six/cam"));
        game.Player.EngineStand = new PartInstance("engines/six/block");

        Assert.True(new SavedPartsMigration(catalog, AppLoggerFactory.CreateLogger("Test")).BringUpToDate(game));

        Assert.Null(car.Engine);
        Assert.Null(game.Player.EngineStand);
        Assert.Equal(["six:engines/six/block", "six:engines/six/block", "six:engines/six/cam"], game.Player.Parts.Select(p => p.DefinitionId).Order());
        Assert.DoesNotContain(ModProblems.All, p => p.StartsWith("Saved parts:") && p.Contains("engines/six/"));
    }

    [Fact]
    public void What_comes_off_paid_work_goes_on_the_shelf_and_departed_rivals_and_bare_shops_are_brought_up_to_date()
    {
        var catalog = Catalog("""{ "engines/old/Cam": "engines/t/cam" }""");
        var game = GameState.CreateNew("Second review");
        var cam = new PartInstance("engines/t/cam");
        cam.Children.Add(new PartInstance("engines/t/rail") { ParentSlot = 5, OwnSlot = 1 });
        game.Player.MachineWork.Add(new MachineOrder { Part = cam });
        var departed = new Racer(RacerType.AI, "Gone");
        departed.Parts.Add(new PartInstance("engines/old/Cam"));
        game.Racers.Departed["Gone"] = departed;
        game.PartShops.Add(new PartShopLocation { Id = "bare", Name = "Bare", Shelves = null!, Yard = null! });

        Assert.True(new SavedPartsMigration(catalog, AppLoggerFactory.CreateLogger("Test")).BringUpToDate(game));

        Assert.Empty(cam.Children);
        Assert.Contains(game.Player.Parts, p => p.DefinitionId == "engines/t/rail");
        Assert.Equal("engines/t/cam", Assert.Single(departed.Parts).DefinitionId);
    }

    // ----- 3. Two shelves of one part after a rename -----

    [Fact]
    public void Two_old_ids_aliased_to_one_part_leave_one_shelf_with_both_counts()
    {
        var catalog = Catalog("""{ "engines/old/Rail": "engines/t/rail", "engines/older/Rail": "engines/t/rail" }""");
        var shop = new PartShopLocation
        {
            Shelves = [new ShopShelf { DefinitionId = "engines/old/Rail", Count = 2 }, new ShopShelf { DefinitionId = "engines/t/cam", Count = 1 }, new ShopShelf { DefinitionId = "engines/older/Rail", Count = 3 }]
        };

        Assert.Equal(3, PartShopService.RenameShelves(catalog, [shop]));

        Assert.Equal([("engines/t/rail", 5), ("engines/t/cam", 1)], shop.Shelves.Select(s => (s.DefinitionId, s.Count)));
        Assert.Equal(0, PartShopService.RenameShelves(catalog, [shop]));
    }

    // ----- 4. The listing first, then the car and the money -----

    [Fact]
    public async Task A_listing_that_cannot_be_made_leaves_the_players_car_and_money_where_they_were()
    {
        var market = new FakeMarket(2000m) { ListCarThrows = new InvalidOperationException("no words") };
        var service = new CarSaleService(market, new QuietTime(), new RaceResultProcessorTests.FakeRepository());
        var state = GameState.CreateNew("P");
        var car = new Car("car_a") { InstanceId = Guid.NewGuid() };
        state.Player.Cars.Add(car);
        var money = state.Player.Money;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SellToDealerAsync(state, car));

        Assert.Contains(car, state.Player.Cars);
        Assert.Equal(money, state.Player.Money);
        Assert.Empty(state.UsedCarMarket);
    }

    [Fact]
    public async Task A_rivals_sale_whose_listing_cannot_be_made_leaves_the_car_and_the_money_where_they_were()
    {
        var market = new FakeMarket(1000m) { ListCarThrows = new InvalidOperationException("no words") };
        var life = new OpponentLifeService(new NoParts(), market, new NoCars(), random: new FixedRandom(0.99));
        var state = GameState.CreateNew("P");
        var racer = new Opponent("Ann", 30, Gender.Female, 92, 50) { Money = 500m, Status = RacerStatus.ReadyToRace };
        var driven = new Car("car_a") { PowerHp = 200 };
        var spare = new Car("car_b") { PowerHp = 100 };
        racer.Cars.AddRange([driven, spare]);
        state.Racers.AddRacer(racer);
        // An ad that ran its time: the spare goes to a dealer this morning
        RivalCarAds.Post(state, racer, spare, 900m, state.Date.AddDays(-CarSaleService.AdDays - 1));

        await life.ReviewDayAsync(state, state.Date);

        Assert.Contains(spare, racer.Cars);
        Assert.Equal(500m, racer.Money);
        Assert.Empty(state.UsedCarMarket);
    }

    // ----- 5. Two overlapping reviews keep their own day's lookups -----

    /// <summary>Parts that are there, with nothing in them, and that give the cars their parts only when the test says so</summary>
    private sealed class GatedBatchParts : NullCarPartsService, ICarPartsService
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Batches;

        public override bool IsAvailable => true;
        public override PartsCatalog Catalog => PartsCatalog.Empty(string.Empty);
        public override EngineBuildIndex Builds => EngineBuildIndex.Empty;

        public async Task<int> EnsurePartsAsync(IEnumerable<Car> cars)
        {
            Interlocked.Increment(ref Batches);
            await Release.Task;
            return 0;
        }
    }

    private static (GameState State, Opponent Racer) TownWithASpareCar(string name)
    {
        var state = GameState.CreateNew(name);
        var racer = new Opponent(name, 30, Gender.Male, 92, 50) { Money = 500m, Status = RacerStatus.ReadyToRace };
        racer.Cars.AddRange([new Car("car_a") { PowerHp = 200 }, new Car("car_b") { PowerHp = 150 }, new Car("car_c") { PowerHp = 100 }]);
        state.Racers.AddRacer(racer);
        return (state, racer);
    }

    [Fact]
    public async Task Two_overlapping_reviews_each_value_the_cars_by_their_own_days_valuer()
    {
        var parts = new GatedBatchParts();
        var market = new FakeMarket(1000m) { ValuersInTurn = new Queue<decimal>([1000m, 2000m]) };
        var life = new OpponentLifeService(parts, market, new NoCars(), random: new FixedRandom(0.99));
        var (first, ann) = TownWithASpareCar("Ann");
        var (second, bob) = TownWithASpareCar("Bob");

        // Both reviews are under way, both waiting for the cars' parts, the second begun after the first
        var reviewA = life.ReviewDayAsync(first, first.Date);
        var reviewB = life.ReviewDayAsync(second, second.Date);
        Assert.Equal(2, parts.Batches);
        parts.Release.SetResult();
        await Task.WhenAll(reviewA, reviewB);

        // Each racer put the spare car in the paper at a share of what their own review valued it at (0.8985 of it)
        var annAsks = Assert.Single(first.NewspaperAds.RivalCars, a => a.RivalName == ann.Name).AskingPrice;
        var bobAsks = Assert.Single(second.NewspaperAds.RivalCars, a => a.RivalName == bob.Name).AskingPrice;
        Assert.Equal(900m, annAsks);
        Assert.Equal(1800m, bobAsks);
    }

    [Fact]
    public async Task The_cars_bought_this_morning_get_their_parts_in_one_batch_before_the_tuners()
    {
        var parts = new GatedBatchParts();
        parts.Release.SetResult();
        var life = new OpponentLifeService(parts, new FakeMarket(1000m), new NoCars(), random: new FixedRandom(0.99));
        var state = GameState.CreateNew("P");
        var racer = new Opponent("Ann", 30, Gender.Female, 92, 50) { Money = 2000m, Status = RacerStatus.Retired };
        state.Racers.AddRacer(racer);
        state.UsedCarMarket.Add(new UsedCarListing { CarDefinitionId = "car_runner", Price = 900m, PowerHp = 180, Condition = 0.8f, DealerLocation = "downtown_motors" });

        await life.ReviewDayAsync(state, state.Date);

        // No car lacked its parts at the start of the day (nothing to batch then); the one bought this morning went
        // through the batch after the purchases, never one by one on the caller's thread
        Assert.Equal("car_runner", Assert.Single(racer.Cars).DefinitionId);
        Assert.Equal(1, parts.Batches);
    }

    // ----- 6. The ground file's cap -----

    [Fact]
    public void A_ground_header_asking_for_millions_of_cells_is_flat()
    {
        var file = _temp.Combine("ground.bin");
        using (var writer = new BinaryWriter(File.Create(file)))
        {
            writer.Write("SCG1"u8);
            writer.Write(1f);
            writer.Write(2500);
            writer.Write(2500);
            writer.Write(0f);
            writer.Write(0f);
            for (var i = 0; i < 4; i++) writer.Write(1f);
        }

        Assert.Equal(4_000_000, StationGround.MaxCells);
        Assert.Same(StationGround.Flat, StationGround.Load(file));
    }

    // ----- 7. The figures that became rules -----

    [Fact]
    public void The_new_rules_hold_the_figures_the_code_played_by()
    {
        var book = new RuleBook();
        Assert.Equal(0.35, book.Market.UsedCarTuneChance);
        Assert.Equal(0.3, book.Market.UsedCarMinTuneLevel);
        Assert.Equal(0.6, book.Market.PaperBaseChance);
        Assert.Equal(0.9, book.Market.PaperFairShare);
        Assert.Equal(1.2, book.Market.PaperChanceSlope);
        Assert.Equal(0.1, book.Market.PaperUnpricedChance);
        Assert.Equal(3, book.Rivals.MaxTunersPerDay);
        Assert.Equal(0.15, book.Rivals.TuneChance);
        Assert.Equal(0.3, book.Rivals.SaleTuneChance);
        Assert.Equal(7, book.Rivals.StreetTalkDays);
        Assert.Equal(40, book.Rivals.MaxStreetTalk);
        Assert.Equal(0.8, book.Economy.PartAdMinAskShare);
        Assert.Equal(1.2, book.Economy.PartAdMaxAskShare);
        Assert.Equal(0.3, book.Economy.WearFloor);
        Assert.Equal(0.5, book.PartShops.TopUpChance);

        Assert.Equal(3, OpponentRules.MaxTunersPerDay);
        Assert.Equal(7, OpponentRules.StreetTalkDays);
        Assert.Equal(0.5, Services.PartShops.PartShopRules.TopUpChance);
    }

    [Fact]
    public void The_new_rules_are_held_to_what_the_game_can_play_by()
    {
        var problems = new List<string>();
        var merged = ModMerge.Parse("""
            {
              "rivals": { "tuneChance": 7, "maxTunersPerDay": -1 },
              "partShops": { "topUpChance": -0.5 },
              "market": { "paperBaseChance": 2, "usedCarTuneChance": 1.5, "paperChanceSlope": 1000 },
              "economy": { "wearFloor": 3, "partAdMinAskShare": 1.5, "partAdMaxAskShare": 0.9 }
            }
            """)!;

        var book = RuleBook.Read(merged, problems);

        Assert.Equal(0.15, book.Rivals.TuneChance);
        Assert.Equal(3, book.Rivals.MaxTunersPerDay);
        Assert.Equal(0.5, book.PartShops.TopUpChance);
        Assert.Equal(0.6, book.Market.PaperBaseChance);
        Assert.Equal(0.35, book.Market.UsedCarTuneChance);
        Assert.Equal(1.2, book.Market.PaperChanceSlope);
        Assert.Equal(0.3, book.Economy.WearFloor);
        // The least and the most the wrong way round are turned round
        Assert.Equal(0.9, book.Economy.PartAdMinAskShare);
        Assert.Equal(1.5, book.Economy.PartAdMaxAskShare);
        foreach (var name in new[] { "rivals.tuneChance", "rivals.maxTunersPerDay", "partShops.topUpChance", "market.paperBaseChance", "market.usedCarTuneChance",
                     "market.paperChanceSlope", "economy.wearFloor", "economy.partAdMinAskShare" })
            Assert.Contains(problems, p => p.Contains(name));
    }

    [Fact]
    public void An_engine_that_does_not_run_is_given_no_figures_and_the_sim_stands_in_its_own()
    {
        var sim = new EngineSim(new EngineSpec("dead", 0, 0, 0, 0, _ => 0, 0, null, "no spark"));

        Assert.Equal(800, sim.Idle);
        Assert.Equal(6000, sim.Limiter);
        Assert.Equal(300, sim.PeakTorque);
        Assert.InRange(sim.Inertia, 0.08, 0.8);
    }

    // ----- 8. One shape rule -----

    [Theory]
    [InlineData(0.0, 0.3)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.5, 0.65)]
    [InlineData(double.NaN, 0.3)]
    public void A_things_shape_is_the_parts_one_rule_and_the_cars(double condition, double expected)
    {
        Assert.Equal(expected, PartPricing.Shape(condition), 9);
        Assert.Equal((decimal)expected, CarValuation.Shape(condition));
    }

    [Fact]
    public void A_wear_that_is_no_number_is_as_new_for_a_part_and_worn_out_for_a_body()
    {
        Assert.Equal(1.0, PartPricing.Shape(double.NaN, ifNotFinite: 1));
        Assert.Equal(0.3, PartPricing.Shape(double.NaN));
        Assert.Equal(0.3, new PartPricing.Figures(1, 0.6, 0.4).WearFloor);
    }

    // ----- 9. The paper's assortment by a group flag -----

    [Fact]
    public void The_assortment_by_the_groups_for_sale_is_what_the_engine_packs_folder_gave()
    {
        var parts = new CatalogParts();
        var catalog = parts.Catalog;
        var usedByBuilds = catalog.EngineBuilds.SelectMany(b => b.Parts).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var before = catalog.Parts.Values
            .Where(p => usedByBuilds.Contains(p.Id) ||
                        (p.LocalId.StartsWith("engines/", StringComparison.OrdinalIgnoreCase) && p.IsRealPart) ||
                        (p.IsRealPart && RunningGear.IsRunningGear(PartKinds.GroupOf(p)) && (PartKinds.GroupOf(p) != PartKinds.SwayBars || RunningGear.IsWorkingSwayBar(p))) ||
                        RearAxle.IsAxleGroup(PartKinds.GroupOf(p)))
            // and, since 2026-10-28, the traction bars and lowering blocks (they were on no shelf at all)
            .Concat(catalog.Parts.Values.Where(p => p.IsRealPart && PartKinds.GroupOf(p) is "traction_bars" or "lowering_blocks"))
            // and, since 2026-10-28 too, the brake valves, steering angle kits, spoilers and electronics (step 42's chassis slots)
            .Concat(catalog.Parts.Values.Where(p => p.IsRealPart && PartKinds.GroupOf(p) is "brake_valves" or "steering_kits" or "spoilers" or "electronics"))
            .Distinct()
            // and, since 2026-10-22, never a wheel with no model
            .Where(p => PartKinds.GroupOf(p) is not (PartKinds.Rims or PartKinds.Tyres) || catalog.GetModelPath(p) != null)
            .Select(p => p.Id)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var now = new NewspaperPartsService(parts).Assortment.Select(p => p.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(PartGroups.Current.ForSale(PartGroups.Blocks));
        Assert.False(PartGroups.Current.ForSale(PartGroups.Tyres));
        Assert.NotEmpty(now);
        var gone = before.Except(now, StringComparer.OrdinalIgnoreCase).Select(id => $"{id} ({PartKinds.GroupOf(catalog.Get(id)!)})").ToList();
        var added = now.Except(before, StringComparer.OrdinalIgnoreCase).Select(id => $"{id} ({PartKinds.GroupOf(catalog.Get(id)!)})").ToList();
        Assert.True(gone.Count == 0 && added.Count == 0, $"no longer for sale: {string.Join(", ", gone)}; newly for sale: {string.Join(", ", added)}");
    }

    [Fact]
    public void A_mods_group_puts_its_kind_up_for_sale_or_takes_it_off()
    {
        _temp.File("base/part_groups.json", """{ "groups": [ { "id": "camshafts", "classes": [ "camshaft" ], "for_sale": true }, { "id": "accessories" } ] }""");
        _temp.File("mod/part_groups.json", """{ "groups": [ { "id": "camshafts", "for_sale": false }, { "id": "mascots", "classes": [ "mascot" ], "for_sale": true } ] }""");

        var groups = PartGroups.Load([_temp.Combine("base"), _temp.Combine("mod")]);

        Assert.False(groups.ForSale("camshafts"));
        Assert.True(groups.ForSale("mascots"));
        Assert.False(groups.ForSale("accessories"));
    }

    [Fact]
    public void The_attendant_looks_at_the_groups_with_a_hint_and_a_mod_gives_one_or_changes_it()
    {
        var core = PartGroups.Load([Path.Combine(RepoPaths.BaseContent, ContentTypes.Parts)]);
        Assert.Equal(
            ["air_filters=air_filter", "carburettors=carburettor", "exhaust=exhaust", "flywheels=clutch"],
            core.All.Where(g => g.Hint != null).Select(g => $"{g.Id}={g.Hint}").Order(StringComparer.Ordinal));

        _temp.File("base/part_groups.json", """{ "groups": [ { "id": "carburettors", "classes": [ "fuel_system" ], "hint": "carburettor" }, { "id": "chargers", "classes": [ "turbocharger" ] } ] }""");
        _temp.File("mod/part_groups.json", """{ "groups": [ { "id": "carburettors", "hint": "injection" }, { "id": "chargers", "hint": "turbo" } ] }""");

        var groups = PartGroups.Load([_temp.Combine("base"), _temp.Combine("mod")]);

        Assert.Equal("injection", groups.Get("carburettors")!.Hint);
        Assert.Equal("turbo", groups.Get("chargers")!.Hint);
    }

    // ----- 11. No built-in dealers -----

    [Fact]
    public void A_market_without_a_dealer_in_any_mod_has_no_lots_and_says_so()
    {
        var market = new UsedCarMarketService(new MemoryCatalog(), new MemoryProfiles(), null, new FakeDealers());

        Assert.Empty(market.GetDefaultDealers());
        Assert.Contains(ModProblems.All, p => p.StartsWith("Dealers:") && p.Contains("no mod has a car dealer"));
        Assert.Empty(new UsedCarMarketService(new MemoryCatalog(), new MemoryProfiles()).GetDefaultDealers());
    }

    // ----- 14. The scenes dressed the one way -----

    [Fact]
    public void A_scene_is_dressed_with_its_looks_lamps_pictures_and_glow_at_the_hour()
    {
        var lamp = new StreetLamp(0f, 1f, 0f, [255, 255, 255], 1f, 3f);
        var day = SceneLighting.Default with { Lamps = [lamp] };
        var lights = new Dictionary<StreetLight, StreetLighting> { [StreetLight.Day] = day };
        var pictures = new Dictionary<string, string> { ["wall"] = "brick" };
        var glows = new Dictionary<StreetLight, float> { [StreetLight.Night] = 0.8f };

        var night = SceneDressing.Of(lights, StreetLight.Night, [200, 100, 50], pictures, [0.3f, 0.7f, 0.65f], "glass", glows);
        var noon = SceneDressing.Of(lights, StreetLight.Day, null, null, [0.3f, 0.7f, 0.65f], "glass", glows);
        pictures["wall"] = "plaster";

        Assert.Equal(new byte[] { 200, 100, 50 }, Assert.Single(night.Lighting.Lamps).Color);
        Assert.Equal("brick", night.Pictures["wall"]);
        Assert.Equal(0.8f, night.Glow["glass"]);
        Assert.Equal(new byte[] { 255, 255, 255 }, Assert.Single(noon.Lighting.Lamps).Color);
        Assert.Empty(noon.Pictures);
        Assert.Empty(noon.Glow);
    }
}
