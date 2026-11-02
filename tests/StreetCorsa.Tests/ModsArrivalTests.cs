using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Scheduler;
using StreetCorsa.Services.Storage;

namespace StreetCorsa.Tests;

/// <summary>
/// A save opened with mods new since it was last played (<see cref="ModsInSave.Arrived"/>, <see cref="ModsArrival"/>):
/// which mods count as new, and the daily tasks run at once for them
/// </summary>
public sealed class ModsArrivalTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private ModSet InPlay()
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa", "version": "0.1.0" }""");
        _temp.File("Content/settings/rods/mod.json", """{ "id": "rods", "type": "setting", "version": "1.1", "name": "Rods" }""");
        _temp.File("Content/engines/v8/mod.json", """{ "id": "v8", "version": "1.0", "name": "Big V8" }""");
        _temp.File("Content/engines/six/mod.json", """{ "id": "six", "version": "2.0" }""");
        return ModSet.Load(_temp.Combine("Content"), setting: "rods", exactSetting: true);
    }

    private static GameState PlayedWith(params (string Id, string Version)[] mods) =>
        new() { Mods = mods.Select(m => new ModRef { Id = m.Id, Name = m.Id, Version = m.Version }).ToList() };

    [Fact]
    public void A_mod_installed_or_updated_since_the_last_save_is_new_and_the_core_and_older_ones_are_not()
    {
        var mods = InPlay();
        var game = PlayedWith(("streetcorsa", "0.0.9"), ("rods", "1.0"), ("six", "2.5"));

        var arrived = ModsInSave.Arrived(game, mods).Select(m => m.Id).Order().ToList();

        // v8 is new, rods went 1.0 -> 1.1; six is older than played with (warned of elsewhere), the core is no news
        Assert.Equal(["rods", "v8"], arrived);
    }

    [Fact]
    public void A_save_played_with_every_mod_in_play_has_nothing_new_nor_one_that_names_no_mods()
    {
        var mods = InPlay();

        Assert.Empty(ModsInSave.Arrived(PlayedWith(("streetcorsa", "0.1.0"), ("rods", "1.1"), ("v8", "1.0"), ("six", "2.0")), mods));
        Assert.Empty(ModsInSave.Arrived(new GameState { Mods = [] }, mods));
    }

    [Fact]
    public void The_note_names_each_new_mod_and_what_an_updated_one_went_from()
    {
        var mods = InPlay();
        var game = PlayedWith(("streetcorsa", "0.1.0"), ("rods", "1.0"), ("six", "2.0"));

        var note = ModsInSave.ArrivedNote(game, ModsInSave.Arrived(game, mods));

        Assert.Contains("Big V8 1.0", note);
        Assert.Contains("Rods: updated from 1.0 to 1.1", note);
    }

    [Fact]
    public async Task New_mods_give_every_open_shop_a_whole_new_delivery_and_none_leave_the_shelves_alone()
    {
        _temp.File("Content/streetcorsa/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File("Content/streetcorsa/partshops/shops.json", """
            { "shops": [
              { "id": "speed", "name": "Speed", "kind": "speed", "mapX": 0.4, "mapY": 0.2, "travelHours": 1 },
              { "id": "wreckers", "name": "Wreckers", "kind": "junkyard", "mapX": 0.8, "mapY": 0.2, "travelHours": 1 }
            ] }
            """);
        _temp.File("Content/extra/mod.json", """{ "id": "extra", "version": "1.0" }""");
        var mods = ModSet.Load(_temp.Combine("Content"));
        Assert.NotNull(mods.Get("extra"));

        var shops = new Services.PartShops.PartShopCatalog(mods);
        var parts = new CatalogParts();
        var shopService = new Services.PartShops.PartShopService(shops, parts, new Services.Parts.NewspaperPartsService(parts, shops));
        var arrival = new ModsArrival(shopService, new Services.Dealers.DealerCatalog(mods), new GameTimeScheduler());
        var state = GameState.CreateNew("P");
        Services.PartShops.PartShopRoster.Sync(state, shops);
        await shopService.RestockAsync(state, state.Date, new Random(1), firstOnly: true);
        var speed = Services.PartShops.PartShopRoster.Find(state, "speed")!;
        speed.Shelves.Add(new ShopShelf { DefinitionId = "left_over", Count = 3 });

        // Played with every mod: nothing turns over
        state.Mods = mods.References();
        Assert.Empty(await arrival.FreshenAsync(state, mods));
        Assert.Contains(speed.Shelves, s => s.DefinitionId == "left_over");

        // Played before "extra" was installed: a whole new delivery
        state.Mods = [new ModRef { Id = "streetcorsa" }];
        var arrived = await arrival.FreshenAsync(state, mods);

        Assert.Equal(["extra"], arrived.Select(m => m.Id));
        Assert.DoesNotContain(speed.Shelves, s => s.DefinitionId == "left_over");
        Assert.NotEmpty(speed.Shelves);
        Assert.Equal(state.Date.Date, speed.RestockedOn);
        Assert.NotEmpty(Services.PartShops.PartShopRoster.Find(state, "wreckers")!.Yard);
    }

    private sealed class CountingTask(string id) : IScheduledTask
    {
        public int Runs;
        public string TaskId => id;
        public int IntervalDays => 1;

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            Runs++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Run_now_runs_the_tasks_named_whether_due_or_not_and_marks_them_run()
    {
        var scheduler = new GameTimeScheduler();
        var market = new CountingTask("market_refresh");
        var other = new CountingTask("rust");
        scheduler.RegisterTask(market);
        scheduler.RegisterTask(other);
        var date = new DateTime(1970, 6, 1);
        var game = new GameState();
        game.ScheduledTasks.Add(new ScheduledTaskState { TaskId = "market_refresh", LastExecuted = date });

        var failed = await scheduler.RunNowAsync(game, date, ModsArrival.TasksRunNow);

        Assert.Empty(failed);
        Assert.Equal(1, market.Runs);
        Assert.Equal(0, other.Runs);
        Assert.Equal(date, game.ScheduledTasks.Single(t => t.TaskId == "market_refresh").LastExecuted);
    }
}
