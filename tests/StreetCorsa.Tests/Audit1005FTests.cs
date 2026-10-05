using System.Windows.Threading;
using LiteDB;
using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Navigation;
using StreetCorsa.Screens.LoadGame;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Storage;

namespace StreetCorsa.Tests;

/// <summary>The 2026-10-05 audit's fixes to the screens, the navigation and the dialogs</summary>
public class Audit1005FTests
{
    // ---- fakes ----

    private sealed class PlainScreen : IScreen
    {
        public void Enter() { }
        public void Exit() { }
        public void Resume() { }
    }

    /// <summary>A save read only once the test lets it be, so the Load card can be closed while it is read</summary>
    private sealed class HeldSaves(GameState stored) : IGameStateRepository
    {
        public readonly ManualResetEventSlim Release = new();

        public GameState? Load(string saveName)
        {
            Release.Wait(TimeSpan.FromSeconds(10));
            return stored;
        }

        public void Save(GameState state, string saveName) { }
        public void Save(GameState state, string saveName, Action<LiteDatabase>? sameTransaction) { }
        public bool Exists(string saveName) => false;
        public void Delete(string saveName) { }
        public List<string> ListSaves() => [];
        public GameState CreateNew(string saveName, string playerName, GameRules? rules = null) => new() { SaveName = saveName };
        public void Dispose() => Release.Dispose();
    }

    private static NavigationService Navigation(DialogService dialogs, FlowBook? flow = null) => new(
        dialogs, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, null!, null!, null!, null!, _ => { }, flow);

    private static FlowBook Flow(string json) => FlowBook.Read(ModMerge.Parse(json)!, []);

    private const string TwoScreens = """
        { "screens": { "garage": { "kind": "garage" }, "main_menu": { "kind": "main_menu" },
          "car_lot": { "kind": "car_lot", "exits": { "back": "garage" } },
          "newspaper": { "kind": "newspaper", "exits": { "back": "garage" } } } }
        """;

    /// <summary>What was put off to later on this thread's dispatcher runs now</summary>
    private static void RunLater() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    // ---- a screen already left takes nobody anywhere ----

    [Fact]
    public void A_screen_no_longer_on_show_leaves_from_nowhere()
    {
        var navigation = Navigation(new DialogService(), Flow(TwoScreens));
        var lot = new PlainScreen();
        var paper = new PlainScreen();
        navigation.SafeNavigate("car_lot", () => lot, "car_lot");
        navigation.SafeNavigate("newspaper", () => paper, "newspaper");

        Assert.False(navigation.Leave(lot, "back", new GameState()));
        Assert.False(navigation.GoFrom(lot, "garage", new GameState()));
        Assert.Same(paper, navigation.CurrentScreen);
    }

    [Fact]
    public async Task An_awaited_leave_from_a_screen_left_goes_nowhere()
    {
        var navigation = Navigation(new DialogService(), Flow(TwoScreens));
        var lot = new PlainScreen();
        var paper = new PlainScreen();
        navigation.SafeNavigate("car_lot", () => lot, "car_lot");
        navigation.SafeNavigate("newspaper", () => paper, "newspaper");

        Assert.False(await navigation.LeaveAsync(lot, "back", new GameState()));
        Assert.Same(paper, navigation.CurrentScreen);
    }

    // ---- home later: once, and only for the screen that asked ----

    [Fact]
    public void Home_later_does_not_take_the_player_home_from_a_screen_they_went_to_since()
    {
        var dialogs = new DialogService();
        var navigation = Navigation(dialogs, Flow(TwoScreens));
        var lot = new PlainScreen();
        var paper = new PlainScreen();
        navigation.SafeNavigate("car_lot", () => lot, "car_lot");

        navigation.GoHomeLater(lot, new GameState());
        navigation.SafeNavigate("newspaper", () => paper, "newspaper");
        RunLater();

        Assert.Same(paper, navigation.CurrentScreen);
        Assert.Null(dialogs.CurrentDialog);
    }

    [Fact]
    public void Home_asked_for_twice_is_built_once()
    {
        var navigation = Navigation(new DialogService());
        var stuck = new PlainScreen();
        navigation.SafeNavigate("stuck", () => stuck);
        var changes = 0;
        navigation.ScreenChanging += () => changes++;

        navigation.GoHomeLater(stuck, new GameState());
        navigation.GoHomeLater(stuck, new GameState());
        RunLater();

        Assert.IsType<Screens.Garage.GarageScreenViewModel>(navigation.CurrentScreen);
        Assert.Equal(1, changes);
    }

    // ---- a mod's way out to a screen that must be told what to show ----

    [Fact]
    public void A_way_out_that_cannot_tell_a_screen_what_to_show_is_left_out()
    {
        var problems = new List<string>();
        var book = FlowBook.Read(ModMerge.Parse("""
            { "screens": { "garage": { "kind": "garage" }, "main_menu": { "kind": "main_menu" },
              "dealer_map": { "kind": "dealer_map", "exits": { "back": "dealer_lot", "lot": "dealer_lot" } },
              "dealer_lot": { "kind": "dealer_lot", "exits": { "back": "dealer_map" } } } }
            """)!, problems);

        Assert.Null(book.Exit("dealer_map", "back"));
        Assert.Equal("dealer_lot", book.Exit("dealer_map", "lot"));
        Assert.Contains(problems, p => p.StartsWith("screens.dealer_map.exits.back:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_base_games_ways_out_all_tell_what_they_open()
    {
        var problems = new List<string>();
        FlowBook.Load(ModSet.BaseOnly(RepoPaths.BaseContent), problems: problems);

        Assert.Empty(problems);
    }

    // ---- a dialog closes itself, never the one in front of it ----

    [Fact]
    public void A_dialog_closing_late_does_not_close_the_one_that_came_in_front()
    {
        var dialogs = new DialogService();
        var late = new InformationDialogViewModel(dialogs, "first", "First");
        var urgent = new InformationDialogViewModel(dialogs, "second", "Second");
        dialogs.ShowDialog(late);
        dialogs.ShowDialog(urgent, jumpQueue: true);

        // The covered dialog's own close (a callback that came late) takes it out of the queue only
        late.OkCommand.Execute(null);

        Assert.Same(urgent, dialogs.CurrentDialog);
        urgent.OkCommand.Execute(null);
        Assert.Null(dialogs.CurrentDialog);
    }

    // ---- the Load card closed while a save is read ----

    [Fact]
    public async Task A_save_read_after_its_card_closed_asks_nothing()
    {
        // Played with a mod that is not here: it would ask whether to go on without it
        var game = new GameState { SaveName = "tester", Mods = [new ModRef { Id = "not_here", Name = "Not Here", Version = "1" }] };
        using var saves = new HeldSaves(game);
        var dialogs = new DialogService();
        var navigation = Navigation(dialogs);
        var opened = false;
        var loadGame = new LoadGameScreenViewModel(navigation, dialogs, saves, new NoParts(), _ => opened = true, () => { });
        loadGame.Enter();

        loadGame.LoadSaveCommand.Execute(new SaveGameInfo { SaveName = "tester", PlayerName = "Tester" });
        Assert.True(loadGame.IsBusy);
        loadGame.Exit();
        saves.Release.Set();

        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (loadGame.IsBusy && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.False(loadGame.IsBusy);
        Assert.Null(dialogs.CurrentDialog);
        Assert.False(opened);
    }

    [Fact]
    public async Task The_load_card_stays_busy_until_the_mods_question_is_answered()
    {
        var game = new GameState { SaveName = "tester", Mods = [new ModRef { Id = "not_here", Name = "Not Here", Version = "1" }] };
        using var saves = new HeldSaves(game);
        saves.Release.Set();
        var dialogs = new DialogService();
        var loadGame = new LoadGameScreenViewModel(Navigation(dialogs), dialogs, saves, new NoParts(), _ => { }, () => { });
        loadGame.Enter();

        loadGame.LoadSaveCommand.Execute(new SaveGameInfo { SaveName = "tester", PlayerName = "Tester" });
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (dialogs.CurrentDialog == null && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20, TestContext.Current.CancellationToken);

        var question = Assert.IsType<Dialogs.Confirmation.ConfirmationDialogViewModel>(dialogs.CurrentDialog);
        Assert.True(loadGame.IsBusy);
        question.NoCommand.Execute(null);
        Assert.False(loadGame.IsBusy);
    }

    // ---- percentages in the player's language ----

    [Fact]
    public void A_share_is_written_as_the_text_pools_write_a_percentage()
    {
        Assert.Equal("82%", ConditionDisplay.PercentOf(0.824));
        Assert.Equal("57%", ConditionDisplay.Of(0.579).Percent);
    }
}
