using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Dealers;
using StreetCorsa.Services.PartShops;
using StreetCorsa.Services.Parts;
using StreetCorsa.Services.Scheduler.Tasks;

namespace StreetCorsa.Tests;

/// <summary>
/// The dealers' and the part shops' mornings as the scheduler runs them: daily, under their own ids, handing the day
/// to their life services, and what those do to a save through the task
/// </summary>
public class ReviewTaskTests
{
    private sealed class RecordingDealerLife : IDealerLifeService
    {
        public List<(GameState State, DateTime Date)> Days { get; } = [];
        public Exception? Throws { get; init; }

        public void ReviewDay(GameState gameState, DateTime date)
        {
            Days.Add((gameState, date));
            if (Throws != null) throw Throws;
        }
    }

    private sealed class GatedShopLife : IPartShopLifeService
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(GameState State, DateTime Date)> Days { get; } = [];

        public async Task ReviewDayAsync(GameState gameState, DateTime date)
        {
            Days.Add((gameState, date));
            await Release.Task;
        }
    }

    [Fact]
    public void The_reviews_run_every_day_under_ids_of_their_own()
    {
        var dealers = new DealerReviewTask(new RecordingDealerLife());
        var shops = new PartShopReviewTask(new GatedShopLife());

        Assert.Equal("dealer_review", dealers.TaskId);
        Assert.Equal("partshop_review", shops.TaskId);
        Assert.Equal(1, dealers.IntervalDays);
        Assert.Equal(1, shops.IntervalDays);
    }

    [Fact]
    public async Task The_dealer_review_hands_the_save_and_the_day_to_the_dealers()
    {
        var life = new RecordingDealerLife();
        var state = GameState.CreateNew("P");
        var day = state.Date.AddDays(3);

        await new DealerReviewTask(life).ExecuteAsync(state, day);

        var (seen, date) = Assert.Single(life.Days);
        Assert.Same(state, seen);
        Assert.Equal(day, date);
    }

    [Fact]
    public async Task A_dealer_review_that_fails_says_so_to_the_scheduler()
    {
        var life = new RecordingDealerLife { Throws = new InvalidOperationException("broken roster") };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new DealerReviewTask(life).ExecuteAsync(GameState.CreateNew("P"), DateTime.Today));
        Assert.Equal("broken roster", error.Message);
    }

    [Fact]
    public async Task The_part_shop_review_is_over_only_when_the_shops_morning_is()
    {
        var life = new GatedShopLife();
        var state = new GameState();
        var day = state.Date.AddDays(1);

        var run = new PartShopReviewTask(life).ExecuteAsync(state, day);
        Assert.False(run.IsCompleted);
        var (seen, date) = Assert.Single(life.Days);
        Assert.Same(state, seen);
        Assert.Equal(day, date);

        life.Release.SetResult();
        await run.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Through_the_task_a_lot_in_its_closing_down_sale_shuts_the_morning_after_its_last_day()
    {
        TestLogging.SilenceLogging();
        var state = GameState.CreateNew("P");
        var catalog = FakeDealers.Named(open: 3, waiting: 0);
        DealerRoster.Sync(state, catalog);
        var closing = state.DealerLocations[0];
        closing.Status = DealerStatus.ClosingDown;
        closing.ClosesOn = state.Date.Date;
        var task = new DealerReviewTask(new DealerLifeService(catalog, new FixedRandom(0.99)));

        await task.ExecuteAsync(state, state.Date);
        Assert.Equal(DealerStatus.ClosingDown, closing.Status);

        await task.ExecuteAsync(state, state.Date.AddDays(1));
        Assert.Equal(DealerStatus.Closed, closing.Status);
    }

    [Fact]
    public async Task Through_the_task_the_shops_stock_up_and_one_in_its_sale_shuts_after_its_last_day()
    {
        var parts = new CatalogParts();
        var catalog = new FakePartShops(
            new PartShopDefinition { Id = "a", Name = "a", Region = "Town", Kind = "specialist", StartsOpen = true, StockLow = 3, StockHigh = 3 },
            new PartShopDefinition { Id = "b", Name = "b", Region = "Town", Kind = "speed", StartsOpen = true, StockLow = 3, StockHigh = 3 });
        var service = new PartShopService(catalog, parts, new NewspaperPartsService(parts));
        var state = new GameState();
        var task = new PartShopReviewTask(new PartShopLifeService(service, new FixedRandom(0.99)));

        await task.ExecuteAsync(state, state.Date);
        var shop = PartShopRoster.Find(state, "a")!;
        Assert.NotEmpty(shop.Shelves);

        shop.Status = DealerStatus.ClosingDown;
        shop.ClosesOn = state.Date.Date.AddDays(1);
        await task.ExecuteAsync(state, state.Date.AddDays(1));
        Assert.Equal(DealerStatus.ClosingDown, shop.Status);

        await task.ExecuteAsync(state, state.Date.AddDays(2));
        Assert.Equal(DealerStatus.Closed, shop.Status);
    }
}
