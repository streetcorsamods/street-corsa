using StreetCorsa.Dialogs;
using StreetCorsa.Dialogs.Confirmation;
using StreetCorsa.Dialogs.Information;
using StreetCorsa.Logging;
using StreetCorsa.Models.AC;
using StreetCorsa.Models.Catalog;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Navigation;
using StreetCorsa.Rules;
using StreetCorsa.Screens.Diner;
using StreetCorsa.Screens.RaceLoading;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Tests;

/// <summary>
/// The tech audit of 2026-10-25, worker F (the screens, dialogs, navigation and styles): a screen whose entering fails
/// after an await is left again for the screen before, as one whose Enter throws; the kinds are built off one table; the
/// way back after a race; the window's money card as a view model; the purchase question answered once; a rival who
/// turns a challenge down; the diner's track previews read off the UI thread
/// </summary>
public sealed class Audit1025FTests
{
    /// <summary>
    /// No synchronization context while the test runs: an await's continuation then runs at once where the task it waits
    /// for completes, so what the navigation does after a screen's entering fails is done by the time the test looks
    /// </summary>
    private sealed class NoContext : IDisposable
    {
        private readonly SynchronizationContext? _was = SynchronizationContext.Current;

        public NoContext() => SynchronizationContext.SetSynchronizationContext(null);

        public void Dispose() => SynchronizationContext.SetSynchronizationContext(_was);
    }

    private class Screen : IScreen
    {
        public List<string> Calls { get; } = new();
        public void Enter() => Calls.Add("Enter");
        public void Exit() => Calls.Add("Exit");
        public void Resume() => Calls.Add("Resume");
    }

    /// <summary>A screen whose entering waits for <see cref="Gate"/>; it fails at once with <paramref name="failAtOnce"/></summary>
    private sealed class LoadingScreen(bool failAtOnce = false) : Screen, IScreen
    {
        public TaskCompletionSource Gate { get; } = new();
        public Task? Entering { get; private set; }

        Task IScreen.EnterAsync() => Entering = LoadAsync();

        private async Task LoadAsync()
        {
            Calls.Add("EnterAsync");
            if (failAtOnce) throw new InvalidOperationException("enter failed before its first await");
            await Gate.Task;
            Calls.Add("Loaded");
        }
    }

    // ---- the navigation: an entering that fails after an await ----

    [Fact]
    public void An_async_enter_that_fails_before_its_first_await_leaves_the_player_where_they_were()
    {
        using var _ = new NoContext();
        var dialogs = new DialogService();
        var navigation = TestNavigation.WithoutServices(dialogs);
        var home = new Screen();
        navigation.SafeNavigate("home", () => home);
        var broken = new LoadingScreen(failAtOnce: true);

        Assert.False(navigation.SafeNavigate("broken", () => broken));

        Assert.Same(home, navigation.CurrentScreen);
        Assert.Equal(new[] { "Enter", "Exit", "Resume" }, home.Calls);
        Assert.Equal(new[] { "Enter", "EnterAsync", "Exit" }, broken.Calls);
        Assert.IsType<InformationDialogViewModel>(dialogs.CurrentDialog);
    }

    [Fact]
    public void An_async_enter_that_fails_later_while_its_screen_is_up_puts_the_screen_before_back()
    {
        using var _ = new NoContext();
        var dialogs = new DialogService();
        var navigation = TestNavigation.WithoutServices(dialogs);
        var home = new Screen();
        navigation.SafeNavigate("home", () => home);
        var shop = new LoadingScreen();

        // Up while it loads
        Assert.True(navigation.SafeNavigate("shop", () => shop));
        Assert.Same(shop, navigation.CurrentScreen);
        Assert.False(dialogs.IsDialogOpen);

        shop.Gate.SetException(new InvalidOperationException("the shop could not open"));

        Assert.Same(home, navigation.CurrentScreen);
        Assert.Equal(new[] { "Enter", "Exit", "Resume" }, home.Calls);
        Assert.Equal(new[] { "Enter", "EnterAsync", "Exit" }, shop.Calls);
        Assert.IsType<InformationDialogViewModel>(dialogs.CurrentDialog);
    }

    [Fact]
    public void An_async_enter_that_fails_after_the_player_went_on_takes_them_nowhere()
    {
        using var _ = new NoContext();
        var dialogs = new DialogService();
        var navigation = TestNavigation.WithoutServices(dialogs);
        var home = new Screen();
        navigation.SafeNavigate("home", () => home);
        var shop = new LoadingScreen();
        navigation.SafeNavigate("shop", () => shop);
        var next = new Screen();
        navigation.SafeNavigate("next", () => next);

        shop.Gate.SetException(new InvalidOperationException("the shop could not open"));

        Assert.True(shop.Entering!.IsFaulted);
        Assert.Same(next, navigation.CurrentScreen);
        Assert.Equal(new[] { "Enter" }, next.Calls);
        Assert.Equal(new[] { "Enter", "Exit" }, home.Calls);
        Assert.False(dialogs.IsDialogOpen);
    }

    [Fact]
    public void An_async_enter_that_is_cancelled_or_done_keeps_its_screen_up()
    {
        using var _ = new NoContext();
        var dialogs = new DialogService();
        var navigation = TestNavigation.WithoutServices(dialogs);
        var home = new Screen();
        navigation.SafeNavigate("home", () => home);

        var cancelled = new LoadingScreen();
        navigation.SafeNavigate("cancelled", () => cancelled);
        cancelled.Gate.SetCanceled(TestContext.Current.CancellationToken);
        Assert.Same(cancelled, navigation.CurrentScreen);

        var loaded = new LoadingScreen();
        navigation.SafeNavigate("loaded", () => loaded);
        loaded.Gate.SetResult();
        Assert.Same(loaded, navigation.CurrentScreen);
        Assert.Equal(new[] { "Enter", "EnterAsync", "Loaded" }, loaded.Calls);
        Assert.False(dialogs.IsDialogOpen);
    }

    [Fact]
    public void A_screen_without_an_async_enter_enters_as_before()
    {
        var dialogs = new DialogService();
        var navigation = TestNavigation.WithoutServices(dialogs);
        var plain = new Screen();

        Assert.True(navigation.SafeNavigate("plain", () => plain));

        Assert.Equal(Task.CompletedTask, ((IScreen)plain).EnterAsync());
        Assert.Equal(new[] { "Enter" }, plain.Calls);
    }

    // ---- the navigation: the kinds off one table ----

    [Fact]
    public void A_screen_not_told_what_it_needs_is_not_gone_to_and_the_player_is_told()
    {
        var problems = new List<string>();
        var flow = FlowBook.Read(ModMerge.Parse("""
            {
              "screens": {
                "garage": { "kind": "garage" },
                "main_menu": { "kind": "main_menu" },
                "viewing": { "kind": "car_viewing" },
                "lot": { "kind": "dealer_lot" }
              }
            }
            """)!, problems);
        var dialogs = new DialogService();
        var navigation = TestNavigation.WithoutServices(dialogs, flow);

        Assert.False(navigation.Go("viewing", new GameState()));
        Assert.Null(navigation.CurrentScreen);
        Assert.IsType<InformationDialogViewModel>(dialogs.CurrentDialog);

        dialogs.CloseDialog();
        Assert.False(navigation.Go("lot", new GameState()));
        Assert.IsType<InformationDialogViewModel>(dialogs.CurrentDialog);
    }

    // ---- the way back after a race ----

    private static readonly FlowBook RaceFlow = FlowBook.Read(ModMerge.Parse("""
        {
          "screens": {
            "garage": { "kind": "garage" },
            "main_menu": { "kind": "main_menu" },
            "diner": { "kind": "diner", "exits": { "after_race": "street" } },
            "street": { "kind": "street" },
            "paper": { "kind": "newspaper" },
            "hub": { "kind": "racing_hub" }
          }
        }
        """)!, new List<string>());

    [Theory]
    [InlineData("paper", false, false, AfterRaceWay.Back, "paper")]
    [InlineData("paper", true, false, AfterRaceWay.Towed, "paper")]
    [InlineData("street", false, true, AfterRaceWay.NightOver, "street")]
    [InlineData("street", false, false, AfterRaceWay.Back, "street")]
    [InlineData("paper", false, true, AfterRaceWay.Back, "paper")]
    [InlineData("diner", false, false, AfterRaceWay.Back, "street")]
    [InlineData("diner", false, true, AfterRaceWay.NightOver, "street")]
    [InlineData("hub", false, false, AfterRaceWay.Overlay, "hub")]
    [InlineData("gone", false, false, AfterRaceWay.Home, "gone")]
    [InlineData(null, false, false, AfterRaceWay.Home, null)]
    public void After_a_race_the_player_goes_back_where_it_was_set_up_else_home(string? returnTo, bool towed, bool nightOver,
        AfterRaceWay way, string? back)
    {
        var (gotWay, gotBack) = RaceLoadingScreenViewModel.WayBack(RaceFlow, returnTo, towed, nightOver, new SystemRules());

        Assert.Equal(way, gotWay);
        Assert.Equal(back, gotBack);
    }

    [Fact]
    public void After_a_race_a_screen_whose_system_is_off_sends_the_player_home()
    {
        var (way, _) = RaceLoadingScreenViewModel.WayBack(RaceFlow, "paper", false, false, new SystemRules { Paper = false });

        Assert.Equal(AfterRaceWay.Home, way);
    }

    // ---- the window's money card ----

    private sealed class OutOfGame : Screen, IScreen
    {
        bool IScreen.InGame => false;
    }

    [Fact]
    public void The_money_card_shows_over_a_games_screens_and_tells_only_what_changed()
    {
        GameState? game = null;
        IScreen? screen = new Screen();
        var canEndDay = false;
        var status = new GameStatusViewModel(() => game, () => screen, () => canEndDay);
        var told = new List<string?>();
        status.PropertyChanged += (_, e) => told.Add(e.PropertyName);

        status.Refresh();
        Assert.False(status.IsShown);
        Assert.Empty(told);

        game = new GameState();
        game.Player.Money = 1234;
        canEndDay = true;
        status.Refresh();
        Assert.True(status.IsShown);
        Assert.True(status.IsEndDayShown);
        Assert.Equal(StreetCorsa.Helpers.Money.Text(1234m), status.MoneyText);
        Assert.Equal(GameText.Date(game.Date, "format.time"), status.TimeText);
        Assert.Equal(GameText.Date(game.Date, "format.date.long"), status.DateText);

        // Read again with nothing changed: nothing is told
        told.Clear();
        status.Refresh();
        Assert.Empty(told);

        game.Player.Money = 99;
        status.Refresh();
        Assert.Equal(new[] { nameof(GameStatusViewModel.MoneyText) }, told);

        // The main screen, say: no card, and no End Day
        screen = new OutOfGame();
        status.Refresh();
        Assert.False(status.IsShown);
        Assert.False(status.IsEndDayShown);
    }

    // ---- buying a car ----

    private sealed class Purchases : ICarPurchaseService
    {
        public int Bought { get; private set; }
        public Func<PurchaseResult> Result { get; set; } = () => new PurchaseResult(PurchaseOutcome.Bought, "Bought it");
        public Exception? Fails { get; set; }

        public Task<PurchaseResult> PurchaseAsync(GameState gameState, UsedCarListing listing, CarDefinition carDef)
        {
            Bought++;
            return Fails != null ? Task.FromException<PurchaseResult>(Fails) : Task.FromResult(Result());
        }

        public Task<PurchaseResult> PurchaseFromRivalAsync(GameState gameState, RivalCarAd ad, CarDefinition carDef) =>
            PurchaseAsync(gameState, new UsedCarListing(), carDef);

        public SwitchOutcome SwitchTo(GameState gameState, Car car) => SwitchOutcome.Switched;
    }

    private static UsedCarListingViewModel ForSale() => new()
    {
        Listing = new UsedCarListing { CarDefinitionId = "car", Price = 500 },
        CarDefinition = new CarDefinition { Id = "car", Brand = "Ford", Name = "Falcon" }
    };

    private static PurchaseFlow Flow(DialogService dialogs, Purchases purchases) =>
        new(dialogs, purchases, new NullCarPartsService(), AppLoggerFactory.CreateLogger("Test"));

    [Fact]
    public void A_purchase_said_yes_to_twice_buys_once()
    {
        using var _ = new NoContext();
        var dialogs = new DialogService();
        var purchases = new Purchases();
        var finished = 0;

        Flow(dialogs, purchases).Offer(new GameState(), ForSale(), () => true, _ => finished++);
        var question = Assert.IsType<ConfirmationDialogViewModel>(dialogs.CurrentDialog);
        question.YesCommand.Execute(null);
        question.YesCommand.Execute(null);

        Assert.Equal(1, purchases.Bought);
        Assert.Equal(1, finished);
        Assert.Equal(GameText.Say("purchase.done.title"), Assert.IsType<InformationDialogViewModel>(dialogs.CurrentDialog).Title);
    }

    [Fact]
    public void A_purchase_said_no_to_buys_nothing_and_does_not_move_on()
    {
        var dialogs = new DialogService();
        var purchases = new Purchases();
        var finished = 0;

        Flow(dialogs, purchases).Offer(new GameState(), ForSale(), () => true, _ => finished++);
        Assert.IsType<ConfirmationDialogViewModel>(dialogs.CurrentDialog).NoCommand.Execute(null);

        Assert.Equal(0, purchases.Bought);
        Assert.Equal(0, finished);
        Assert.False(dialogs.IsDialogOpen);
    }

    [Fact]
    public void A_purchase_that_fails_tells_the_player_and_does_not_move_on()
    {
        using var _ = new NoContext();
        var dialogs = new DialogService();
        var purchases = new Purchases { Fails = new InvalidOperationException("no engine") };
        var finished = 0;

        Flow(dialogs, purchases).Offer(new GameState(), ForSale(), () => true, _ => finished++);
        Assert.IsType<ConfirmationDialogViewModel>(dialogs.CurrentDialog).YesCommand.Execute(null);

        Assert.Equal(0, finished);
        Assert.Equal(GameText.Say("purchase.failed.title"), Assert.IsType<InformationDialogViewModel>(dialogs.CurrentDialog).Title);
    }

    [Fact]
    public void A_player_gone_off_the_screen_is_not_asked_to_switch_cars()
    {
        using var _ = new NoContext();
        var dialogs = new DialogService();
        var bought = new Car { DefinitionId = "car" };
        var purchases = new Purchases
        {
            Result = () => new PurchaseResult(PurchaseOutcome.Bought, "Bought it", Car: bought, CarName: "Falcon", AskToSwitch: true)
        };
        var finished = 0;

        Flow(dialogs, purchases).Offer(new GameState(), ForSale(), () => false, _ => finished++);
        Assert.IsType<ConfirmationDialogViewModel>(dialogs.CurrentDialog).YesCommand.Execute(null);

        Assert.Equal(1, finished);
        // Only what came of it: no question after it
        Assert.IsType<InformationDialogViewModel>(dialogs.CurrentDialog);
        dialogs.CloseDialog();
        Assert.False(dialogs.IsDialogOpen);
    }

    // ---- a challenge turned down ----

    private sealed class SaysNo : IOpponentChallengeService
    {
        public int Asked { get; private set; }

        public ChallengeResponse EvaluateChallenge(Opponent opponent, Player player, Car playerCar, Car opponentCar, bool isPinkSlip,
            decimal cashWager, double pinkSlipFactor = 1.0)
        {
            Asked++;
            return new ChallengeResponse { Accepted = false, Message = "Not tonight" };
        }

        public bool CanRace(Car car) => true;
        public bool WouldStakePinkSlips(Opponent opponent, Car playerCar, Car opponentCar) => false;
    }

    [Fact]
    public async Task A_rival_who_turns_a_challenge_down_says_why_and_nothing_is_set_up()
    {
        var dialogs = new DialogService();
        var navigation = TestNavigation.WithoutServices(dialogs);
        var rival = new SaysNo();
        var launcher = new ChallengeLauncher(navigation, dialogs, new GameState(), new NoCars(), rival,
            new RaceSetupBuilder(new NullCarPartsService(), null!), AppLoggerFactory.CreateLogger("Test"));

        var outcome = await launcher.RunAsync(new ChallengeRequest
        {
            Opponent = new Opponent(),
            OpponentCar = new Car { DefinitionId = "theirs" },
            OpponentDefinition = new CarDefinition { Id = "theirs" },
            PlayerCar = new Car { DefinitionId = "mine" },
            Track = new TrackInfo { TrackId = "strip" }
        }, new object());

        Assert.Equal(ChallengeStatus.Declined, outcome.Status);
        Assert.Equal("Not tonight", outcome.Message);
        Assert.Equal(1, rival.Asked);
        Assert.Null(navigation.CurrentScreen);
        Assert.False(dialogs.IsDialogOpen);
    }

    // ---- the diner's track cards ----

    [Fact]
    public async Task A_track_cards_preview_is_read_off_the_ui_thread_and_the_card_told_when_it_is_in()
    {
        var card = new TrackCardViewModel { PreviewPath = Path.Combine(Path.GetTempPath(), $"sc-no-preview-{Guid.NewGuid():N}.png") };
        var told = new TaskCompletionSource();
        card.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrackCardViewModel.PreviewImage)) told.TrySetResult();
        };

        // Asking does not wait for the reading
        Assert.Null(card.PreviewImage);
        await told.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // A file that cannot be read is no picture
        Assert.Null(card.PreviewImage);
    }

    [Fact]
    public void A_track_card_without_a_preview_reads_nothing()
    {
        var card = new TrackCardViewModel();
        var told = 0;
        card.PropertyChanged += (_, _) => told++;

        Assert.Null(card.PreviewImage);
        Assert.False(card.HasPreview);
        Assert.Equal(0, told);
    }
}
