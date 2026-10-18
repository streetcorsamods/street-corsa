using System.Collections.Specialized;
using System.Text.Json.Nodes;
using StreetCorsa.Dialogs;
using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;
using StreetCorsa.Screens.CarCatalogEditor;
using StreetCorsa.Screens.Garage;
using StreetCorsa.Screens.PartShop;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.PartShops;
using StreetCorsa.ViewModels;
using PartShopRules = StreetCorsa.Rules.PartShopRules;

namespace StreetCorsa.Tests;

/// <summary>
/// The tech audit of 2026-10-18, worker F (the screens, dialogs, navigation and styles): the machine shop's counter
/// stops saying "pricing" whatever fails and lets go of its cancellation, the workbench drops the last car's picks
/// before the lift reads them, the clerks' figures are the rules', the displacement is a measure, the lists are filled
/// in one go, and the pieces the two shop counters shared are one each
/// </summary>
public sealed class Audit1018FTests
{
    private static readonly CatalogParts Parts = new();

    private static readonly PartShopDefinition Machinist = new() { Id = "gardena", Name = "Gardena Machine", Region = "Gardena", Kind = "machine" };

    // ---- the rules ----

    [Fact]
    public void The_counters_figures_are_the_rules_and_a_senseless_one_keeps_the_base_games()
    {
        var rules = new PartShopRules();
        Assert.Equal(5, rules.MostPicks);
        Assert.Equal(40, rules.MostDynoRuns);
        Assert.Equal(12, rules.MachineDynoRuns);
        Assert.Equal(5, rules.OthersWork);

        var problems = new List<string>();
        var book = RuleBook.Read(new JsonObject
        {
            ["partShops"] = new JsonObject { ["mostPicks"] = -3, ["othersWork"] = 2, ["machineDynoRuns"] = 100_000 }
        }, problems);

        Assert.Equal(5, book.PartShops.MostPicks);
        Assert.Equal(2, book.PartShops.OthersWork);
        Assert.Equal(12, book.PartShops.MachineDynoRuns);
        Assert.Contains(problems, p => p.StartsWith("partShops.mostPicks (-3)", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("partShops.machineDynoRuns (100000)", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("othersWork", StringComparison.Ordinal));
    }

    // ---- the notice the two counters shared ----

    [Fact]
    public void The_notice_under_a_shops_name_says_the_sale_and_the_regulars_discount_once_for_both_counters()
    {
        var today = new DateTime(1965, 6, 1);
        var shop = new PartShopLocation { Id = "x" };
        Assert.Empty(PartShopNotice.Lines(shop, today));
        Assert.Equal(string.Empty, PartShopNotice.Text(shop, today));
        Assert.Equal(string.Empty, PartShopNotice.Text(null, today));
        // A new customer: what the clerk is told of them
        Assert.Single(PartShopNotice.Facts(shop, today));

        shop.PlayerSpent = Services.PartShops.PartShopRules.GoodCustomerSpent;
        var regular = Assert.Single(PartShopNotice.Lines(shop, today));
        Assert.Contains("10%", regular);

        shop.Status = DealerStatus.ClosingDown;
        shop.ClosesOn = today.AddDays(2);
        var lines = PartShopNotice.Lines(shop, today);
        Assert.Equal(2, lines.Count);
        Assert.Contains("CLOSING DOWN", lines[0]);
        Assert.Equal(string.Join(PartShopNotice.Separator, lines), PartShopNotice.Text(shop, today));
        // The sale, the discount, what they have spent
        Assert.Equal(3, PartShopNotice.Facts(shop, today).Count());
    }

    // ---- the lists filled in one go ----

    [Fact]
    public void A_bulk_collection_replaces_its_items_with_one_reset()
    {
        var list = new BulkObservableCollection<int> { 1, 2, 3 };
        var changes = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => changes.Add(e.Action);

        list.ReplaceAll([7, 8]);

        Assert.Equal([7, 8], list);
        Assert.Equal([NotifyCollectionChangedAction.Reset], changes);

        list.ReplaceAll([]);
        Assert.Empty(list);
        Assert.Equal(2, changes.Count);
    }

    // ---- the displacement as a measure ----

    [Fact]
    public void An_engines_displacement_is_litres_or_cubic_inches_by_the_players_units()
    {
        var measure = Measure.Displacement(5.7);
        Assert.Equal(5.7, measure.In(UnitSystem.Metric));
        Assert.Equal(347.8, measure.In(UnitSystem.Imperial), 0.1);
        Assert.Equal("l", Measure.UnitOf(Quantity.Displacement, UnitSystem.Metric));
        Assert.Equal("cui", Measure.UnitOf(Quantity.Displacement, UnitSystem.Imperial));
    }

    // ---- the job show's sound and steps ----

    [Fact]
    public void A_job_show_player_tells_one_show_from_the_next()
    {
        var player = new JobShowPlayer();
        var first = player.Begin();
        Assert.True(player.IsCurrent(first));

        var second = player.Begin();
        Assert.False(player.IsCurrent(first));
        Assert.True(player.IsCurrent(second));

        player.Cancel();
        Assert.False(player.IsCurrent(second));
    }

    [Fact]
    public void Every_rebuild_jobs_steps_are_shown_with_a_sound_each_and_a_job_without_steps_as_one()
    {
        foreach (var job in Parts.Catalog.Rebuilds.All)
        {
            var steps = RebuildShow.StepsOf(job, "the job");
            Assert.Equal(Math.Max(1, job.Steps.Count), steps.Count);
            Assert.All(steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Sound)));
            if (job.Steps.Count == 0) Assert.Equal(("the job", RebuildShow.BenchSound, (string?)null), steps[0]);
        }
    }

    // ---- the catalog editor's axle guesses ----

    [Fact]
    public void The_axle_picks_guess_once_a_refresh_however_many_lists_read_the_guess()
    {
        var choices = 0;
        var gears = 0;
        var picks = new AxlePicks(Parts.Catalog, _ => { choices++; return null; }, null, _ => { gears++; return null; });

        _ = picks.HousingOptions;
        _ = picks.CarrierOptions;
        _ = picks.RatioOptions;
        _ = picks.Housing;
        _ = picks.Carrier;
        _ = picks.FrontBrakeOptions;
        _ = picks.RearBrakeOptions;
        _ = picks.FrontSpringOptions;
        _ = picks.RearSpringOptions;
        Assert.Equal(1, choices);
        Assert.Equal(1, gears);

        // A pick: the guesses are made again, once
        picks.Housing = picks.HousingOptions[1];
        _ = picks.CarrierOptions;
        _ = picks.RatioOptions;
        _ = picks.FrontBrakeOptions;
        Assert.Equal(2, choices);
        Assert.Equal(2, gears);
    }

    // ---- the workbench ----

    private static async Task Until(Func<bool> done)
    {
        for (var waited = 0; !done() && waited < 20_000; waited += 20) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(done());
    }

    [Fact]
    public async Task Another_car_in_the_bay_drops_the_shelf_pick_before_the_lift_is_read_and_the_lift_is_a_command()
    {
        var state = new GameState();
        var car = TestCars.WithRealEngine(Parts.Catalog);
        var other = TestCars.WithRealEngine(Parts.Catalog);
        state.Player.Cars.Add(car);
        state.Player.Cars.Add(other);
        state.Player.Parts.Add(new PartInstance(car.Engine!.DefinitionId) { Wear = 0.5 });

        var workbench = new PartsWorkbenchViewModel(Parts, state, () => { }, _ => Task.CompletedTask);
        workbench.SetCar(car);
        await Until(() => workbench.HasShelfItems);
        workbench.IsCarView = true;

        workbench.SelectedShelfItem = workbench.Shelf[0];
        Assert.NotNull(workbench.SelectedShelfItem);
        Assert.False(workbench.LiftCommand.CanExecute(null) && workbench.IsLifted);

        // The pick goes with the car: nothing of the last car's is read against the new one
        workbench.SetCar(other);
        Assert.Null(workbench.SelectedShelfItem);
        Assert.Null(workbench.SelectedPart);
        Assert.Null(workbench.Candidates);
        await Until(() => workbench.HasShelfItems);

        // The lift's button is a command that steps the lift; what it reads is the car's stop
        Assert.True(workbench.LiftCommand.CanExecute(null));
        Assert.False(workbench.IsLifted);
        workbench.LiftCommand.Execute(null);
        Assert.True(workbench.IsLifted);
        Assert.Equal(LiftStop.WheelsClear, other.LiftStop);
        Assert.Equal(LiftStop.Down, car.LiftStop);
    }

    [Fact]
    public async Task The_engine_sheets_displacement_is_written_in_the_players_units_with_its_compression()
    {
        var state = new GameState();
        var car = TestCars.WithRealEngine(Parts.Catalog);
        state.Player.Cars.Add(car);

        var workbench = new PartsWorkbenchViewModel(Parts, state, () => { }, _ => Task.CompletedTask);
        workbench.SetCar(car);
        await Until(() => workbench.PowerDisplay != "-");

        var unit = Measure.UnitOf(Quantity.Displacement, Locale.Units);
        Assert.Contains(unit, workbench.DisplacementDisplay);
        Assert.Contains(":1", workbench.DisplacementDisplay);
        Assert.DoesNotContain("61023", workbench.DisplacementDisplay);
    }

    // ---- the machine shop's counter ----

    /// <summary>The real machine shop, whose pieces cannot be read a second time (the pricing's)</summary>
    private sealed class FaultingMachine(IMachineShopService real) : IMachineShopService
    {
        public int Reads { get; private set; }

        public IReadOnlyList<MachinePiece> Pieces(GameState gameState, PartShopLocation shop)
        {
            if (++Reads > 1) throw new InvalidOperationException("the pieces are gone");
            return real.Pieces(gameState, shop);
        }

        public IReadOnlyList<MachineWork> Offers(GameState gameState, PartShopLocation shop, MachinePiece piece) => real.Offers(gameState, shop, piece);
        public MachineQuote? QuoteAt(GameState gameState, PartShopLocation shop) => real.QuoteAt(gameState, shop);
        public IReadOnlyList<MachineWork> Offers(MachineQuote quote, MachinePiece piece) => real.Offers(quote, piece);
        public MachineOrder? Order(GameState gameState, PartShopLocation shop, MachineWork work, Random random) => real.Order(gameState, shop, work, random);
        public List<MachineOrder> Deliver(GameState gameState, DateTime date) => real.Deliver(gameState, date);
    }

    private sealed class FaultingShops(PartShopService real) : IPartShopService
    {
        public FaultingMachine FaultingMachine { get; } = new(real.Machine);

        public IPartShopCatalog Catalog => real.Catalog;
        public bool IsAvailable => real.IsAvailable;
        public IReadOnlyList<PartDefinition> AssortmentOf(PartShopDefinition shop) => real.AssortmentOf(shop);
        public decimal PriceOfNew(GameState gameState, PartShopLocation shop, PartDefinition part) => real.PriceOfNew(gameState, shop, part);
        public decimal MarkedPriceOfNew(GameState gameState, PartShopLocation shop, PartDefinition part) => real.MarkedPriceOfNew(gameState, shop, part);
        public decimal PriceOfUsed(GameState gameState, PartShopLocation shop, PartAd item) => real.PriceOfUsed(gameState, shop, item);
        public decimal BuyingPrice(PartShopLocation shop, PartInstance part) => real.BuyingPrice(shop, part);
        public bool BuyNew(GameState gameState, PartShopLocation shop, PartDefinition part) => real.BuyNew(gameState, shop, part);
        public bool BuyUsed(GameState gameState, PartShopLocation shop, PartAd item) => real.BuyUsed(gameState, shop, item);
        public bool Haggle(PartShopLocation shop, PartAd item, Random random) => real.Haggle(shop, item, random);
        public bool Sell(GameState gameState, PartShopLocation shop, PartInstance part) => real.Sell(gameState, shop, part);
        public Task RestockAsync(GameState gameState, DateTime date, Random random, bool firstOnly = false) => real.RestockAsync(gameState, date, random, firstOnly);
        public RivalPriceList RivalPrices(GameState gameState, DateTime? date = null) => real.RivalPrices(gameState, date);
        public bool TakeForRival(GameState gameState, string shopId, string partId) => real.TakeForRival(gameState, shopId, partId);
        public IMachineShopService Machine => FaultingMachine;
    }

    private sealed class NoSaves : Services.Storage.IGameStateRepository
    {
        public GameState? Load(string saveName) => null;
        public GameState CreateNew(string saveName, string playerName, GameRules? rules = null) => new();
        public void Save(GameState state, string saveName) { }
        public void Save(GameState state, string saveName, Action<LiteDB.LiteDatabase>? sameTransaction) { }
        public bool Exists(string saveName) => false;
        public void Delete(string saveName) { }
        public List<string> ListSaves() => [];
        public void Dispose() { }
    }

    [Fact]
    public async Task The_machine_shops_counter_stops_saying_pricing_when_the_pricing_fails_before_it_starts_and_lets_its_cancellation_go()
    {
        var state = new GameState { Date = new DateTime(1965, 6, 1, 10, 0, 0) };
        state.Player.Money = 100_000;
        state.Player.Parts.Add(new PartInstance(TestCars.WithRealEngine(Parts.Catalog).Engine!.DefinitionId) { Wear = 0.3 });
        var shops = new FaultingShops(new PartShopService(new FakePartShops(Machinist), Parts, new NewspaperPartsService(Parts)));
        PartShopRoster.Sync(state, shops.Catalog);

        var dialogs = new DialogService();
        var navigation = TestNavigation.WithoutServices(dialogs);
        var counter = new MachineShopScreenViewModel(navigation, dialogs, state, "gardena", shops, Parts, new NoSaves());
        // NavigateTo enters the screen, as the game does
        navigation.NavigateTo(counter);

        // The pieces are listed (the first read), then priced (the second, which fails before the work goes to a worker)
        await Until(() => counter.HasPieces && shops.FaultingMachine.Reads >= 2);
        await Until(() => !counter.IsPricing);
        Assert.Equal(string.Empty, counter.StatusText);
        Assert.Empty(counter.Jobs);

        var cancel = typeof(MachineShopScreenViewModel).GetField("_pricingCancel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.Null(cancel.GetValue(counter));

        counter.Exit();
        Assert.Null(cancel.GetValue(counter));
    }

    [Fact]
    public async Task The_machine_shops_pieces_are_listed_once_and_the_jobs_once_a_refresh()
    {
        var state = new GameState { Date = new DateTime(1965, 6, 1, 10, 0, 0) };
        state.Player.Money = 100_000;
        var block = new PartInstance(TestCars.WithRealEngine(Parts.Catalog).Engine!.DefinitionId) { Wear = 0.3 };
        state.Player.Parts.Add(block);
        var shops = new PartShopService(new FakePartShops(Machinist), Parts, new NewspaperPartsService(Parts));
        PartShopRoster.Sync(state, shops.Catalog);

        var dialogs = new DialogService();
        var navigation = TestNavigation.WithoutServices(dialogs);
        var counter = new MachineShopScreenViewModel(navigation, dialogs, state, "gardena", shops, Parts, new NoSaves());
        var pieceChanges = 0;
        var jobChanges = 0;
        counter.Pieces.CollectionChanged += (_, _) => pieceChanges++;
        counter.Jobs.CollectionChanged += (_, _) => jobChanges++;
        // NavigateTo enters the screen, as the game does
        navigation.NavigateTo(counter);

        await Until(() => counter.HasJobs && !counter.IsPricing);
        var piece = Assert.Single(counter.Pieces);
        Assert.Same(piece, counter.SelectedPiece);
        Assert.Same(block, piece.Piece.Part);
        // One reset for the pieces; the jobs: emptied as the pieces were listed, then filled once priced
        Assert.Equal(1, pieceChanges);
        Assert.Equal(2, jobChanges);
        Assert.All(counter.Jobs, j => Assert.Same(block, j.Work.Piece.Part));

        counter.Exit();
    }

    // ---- the repair bay's can of gas ----

    [Fact]
    public void The_can_of_gas_is_paid_at_the_towns_pump_price_not_the_first_mods()
    {
        var method = typeof(Dialogs.RepairShop.RepairShopDialogViewModel).GetMethod("PumpPrices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var prices = (StationPrices?)method.Invoke(null, null);
        var stations = Services.Station.StationScenes.Installed;
        if (stations.Count == 0)
        {
            Assert.Null(prices);
            return;
        }

        Assert.NotNull(prices);
        Assert.Equal(stations.Average(s => s.Prices.FuelPerLitre), prices.FuelPerLitre);
        Assert.Equal(stations.Average(s => s.Prices.Tow), prices.Tow);
    }
}
