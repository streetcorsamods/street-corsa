using LiteDB;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Models.Career.Events;
using StreetCorsa.Models.GameState;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.LoadGame;
using StreetCorsa.Screens.NewGame;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Storage;

namespace StreetCorsa.Tests;

/// <summary>The 2026-09-29 audit's fixes to the screens: the new game's save, the garage that must open, the car copies</summary>
public class AuditFixesETests
{
    // ---- fakes ----

    /// <summary>A save that lives in memory and says when it was written</summary>
    private sealed class MemorySaves(Func<int> eventsSoFar) : IGameStateRepository
    {
        public GameState? Stored { get; set; }

        /// <summary>How many times the events had been generated when the game was last saved</summary>
        public int EventsAtLastSave { get; private set; } = -1;

        public int Saves { get; private set; }

        public GameState? Load(string saveName) => Stored;

        public void Save(GameState state, string saveName) => Save(state, saveName, null);

        public void Save(GameState state, string saveName, Action<LiteDatabase>? sameTransaction)
        {
            Saves++;
            EventsAtLastSave = eventsSoFar();
        }

        public bool Exists(string saveName) => false;
        public void Delete(string saveName) { }
        public List<string> ListSaves() => [];

        public GameState CreateNew(string saveName, string playerName, GameRules? rules = null)
        {
            var state = new GameState { SaveName = saveName };
            state.Player.Name = playerName;
            Save(state, saveName);
            return state;
        }

        public void Dispose() { }
    }

    /// <summary>Generates no events, but counts the times it was asked to</summary>
    private sealed class CountingEvents : IRaceEventService
    {
        public int Generated { get; private set; }
        public IEnumerable<RaceEventDefinition> GetAllEventDefinitions() => [];
        public RaceEventDefinition? GetEventDefinition(string eventId) => null;
        public IEnumerable<RaceEventDefinition> GetEligibleEvents(CareerState career) => [];
        public IEnumerable<RaceEventInstance> GetActiveEvents(CareerState career, DateTime currentTime) => [];

        public List<RaceEventInstance> GenerateEvents(CareerState career, DateTime currentTime, double pinkSlipFactor = 1.0)
        {
            Generated++;
            return [];
        }

        public bool CanEnterEvent(string eventId, string carDefinitionId, Car? carInstance, CareerState career) => false;
        public EventReward? CompleteEvent(Guid eventInstanceId, bool playerWon, CareerState career, DateTime completedAt) => null;
        public int CleanupExpiredEvents(CareerState career, DateTime currentTime) => 0;
    }

    /// <summary>A screen that cannot be left: any navigation away from it fails</summary>
    private sealed class StuckScreen : IScreen
    {
        public volatile bool LeaveTried;

        public void Enter() { }

        public void Exit()
        {
            LeaveTried = true;
            throw new InvalidOperationException("cannot leave");
        }

        public void Resume() { }
    }

    // The services only go to the screens the typed factories build: the garage is built on none of them here
    private static NavigationService Navigation(DialogService dialogs) => TestNavigation.WithoutServices(dialogs);

    // ---- the new game ----

    /// <summary>A setting's card for the New Game screen: the tests' output has the core alone, no setting to list</summary>
    private static SettingCard SomeSetting() => new(new Mods.ModInfo { Id = "some_setting", Name = "Some Setting" });

    [Fact]
    public void A_new_game_is_saved_again_with_its_first_events()
    {
        var events = new CountingEvents();
        var saves = new MemorySaves(() => events.Generated);
        var dialogs = new DialogService();
        var navigation = Navigation(dialogs);
        navigation.SafeNavigate("stuck", () => new StuckScreen());
        var newGame = new NewGameScreenViewModel(navigation, dialogs, saves, events, _ => { }, () => { }) { PlayerName = "Tester" };

        newGame.StartGameCommand.Execute(SomeSetting());

        Assert.Equal(1, events.Generated);
        Assert.Equal(1, saves.EventsAtLastSave);
    }

    [Fact]
    public void A_new_game_becomes_the_apps_game_only_once_its_garage_is_up()
    {
        var events = new CountingEvents();
        var saves = new MemorySaves(() => events.Generated);
        var dialogs = new DialogService();
        var navigation = Navigation(dialogs);
        navigation.SafeNavigate("stuck", () => new StuckScreen());
        var screensWhenSet = new List<IScreen?>();
        var newGame = new NewGameScreenViewModel(navigation, dialogs, saves, events, _ => screensWhenSet.Add(navigation.CurrentScreen), () => { })
            { PlayerName = "Tester" };

        newGame.StartGameCommand.Execute(SomeSetting());

        // A screen that throws on the way out no longer keeps the player on it (the navigation goes on), so the
        // garage opens here; the game is the app's only from then on, never while the old screen is still up
        var screen = Assert.Single(screensWhenSet);
        Assert.IsType<Screens.Garage.GarageScreenViewModel>(screen);
    }

    // ---- loading a game ----

    [Fact]
    public async Task A_loaded_game_becomes_the_apps_game_only_once_its_garage_is_up()
    {
        var saves = new MemorySaves(() => 0) { Stored = new GameState { SaveName = "tester" } };
        var dialogs = new DialogService();
        var navigation = Navigation(dialogs);
        var stuck = new StuckScreen();
        navigation.SafeNavigate("stuck", () => stuck);
        var screensWhenSet = new List<IScreen?>();
        var loadGame = new LoadGameScreenViewModel(navigation, dialogs, saves, new NoParts(), _ => screensWhenSet.Add(navigation.CurrentScreen), () => { });

        loadGame.LoadSaveCommand.Execute(new SaveGameInfo { SaveName = "tester", PlayerName = "Tester" });

        // Read and brought up to date off the UI thread: done once the garage was tried and the busy flag is down
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while ((!stuck.LeaveTried || loadGame.IsBusy) && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.True(stuck.LeaveTried);
        Assert.False(loadGame.IsBusy);
        var screen = Assert.Single(screensWhenSet);
        Assert.IsType<Screens.Garage.GarageScreenViewModel>(screen);
    }

    // ---- the car handed to a worker ----

    [Fact]
    public void A_car_snapshot_has_the_cars_figures_on_a_part_tree_of_its_own()
    {
        var engine = new PartInstance("block") { ParentSlot = PartInstance.CarEngineSlot, Wear = 0.8 };
        engine.Children.Add(new PartInstance("carb") { Wear = 0.5 });
        var car = new Car("some_car")
        {
            Parts = [engine],
            HasPartsAssigned = true,
            FuelLitres = 12.5,
            OilLevel = 0.7,
            BodyDirt = 0.3,
            BodyDamageKmh = [10, 0, 0, 5]
        };

        var copy = car.Snapshot();

        Assert.Equal(car.InstanceId, copy.InstanceId);
        Assert.Equal(12.5, copy.FuelLitres);
        Assert.Equal(0.7, copy.OilLevel);
        Assert.Equal(0.3, copy.BodyDirt);
        Assert.Equal(car.BodyDamageKmh, copy.BodyDamageKmh);
        Assert.NotSame(car.BodyDamageKmh, copy.BodyDamageKmh);

        // The same parts, ids and all, but not the same objects: the workbench may change the car's own meanwhile
        var copied = Assert.Single(copy.Parts);
        Assert.NotSame(engine, copied);
        Assert.Equal(engine.InstanceId, copied.InstanceId);
        Assert.Equal(engine.Children[0].InstanceId, copied.Children[0].InstanceId);

        engine.Children.Clear();
        Assert.Single(copied.Children);
    }

    // ---- small helpers ----

    [Fact]
    public void A_condition_that_is_not_a_number_shows_as_nothing()
    {
        Assert.Equal("0%", ConditionDisplay.Of(double.NaN).Percent);
        Assert.Equal("100%", ConditionDisplay.Of(1.5).Percent);
    }

    [Fact]
    public void A_picture_that_is_not_there_is_no_picture()
    {
        Assert.Null(Thumbnails.Load(null, 100));
        Assert.Null(Thumbnails.Load(Path.Combine(Path.GetTempPath(), $"no-such-picture-{Guid.NewGuid():N}.png"), 100));
    }

    [Fact]
    public void Home_later_waits_for_the_navigation_under_way()
    {
        var dialogs = new DialogService();
        var navigation = Navigation(dialogs);
        var stuck = new StuckScreen();
        navigation.SafeNavigate("stuck", () => stuck);

        navigation.GoHomeLater(stuck, new GameState());

        Assert.Same(stuck, navigation.CurrentScreen);
    }
}
