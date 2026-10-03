using StreetCorsa.Dialogs;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Navigation;
using StreetCorsa.Rules;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Scheduler;
using StreetCorsa.Services.Scheduler.Tasks;
using StreetCorsa.Services.Scripting;

namespace StreetCorsa.Tests;

/// <summary>
/// The tests that change the rules of the game in play (the systems a game has), which the whole game reads: run on
/// their own, so no other test plays by a book one of them set
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SystemsCollection
{
    public const string Name = "Systems";
}

/// <summary>E8: the flow (screens, ways out, tiles, cards) as data, and the systems a game can do without</summary>
public sealed class FlowTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static readonly FlowBook BaseFlow = FlowBook.Load(ModSet.BaseOnly(RepoPaths.BaseContent));

    private static FlowBook Read(string json, List<string> problems) => FlowBook.Read(ModMerge.Parse(json)!, problems);

    // ---- the base game's flow is the game's as it was ----

    [Fact]
    public void The_base_games_flow_reads_without_a_problem()
    {
        var problems = new List<string>();
        var book = FlowBook.Load(ModSet.BaseOnly(RepoPaths.BaseContent), problems: problems);

        Assert.Empty(problems);
        Assert.Equal("garage", book.Home);
        Assert.Equal("main_menu", book.Menu);
        // Every kind of screen the program has is in it
        Assert.Equal(ScreenKinds.All.OrderBy(k => k), book.Screens.Values.Select(s => s.Kind).Distinct().OrderBy(k => k));
    }

    [Theory]
    [InlineData("newspaper", "back", "garage")]
    [InlineData("car_viewing", "back", "newspaper")]
    [InlineData("car_viewing", "gone", "newspaper")]
    [InlineData("car_viewing", "bought", "garage")]
    [InlineData("car_viewing", "garage", "garage")]
    [InlineData("dealer_map", "back", "garage")]
    [InlineData("dealer_map", "lot", "dealer_lot")]
    [InlineData("dealer_lot", "back", "dealer_map")]
    [InlineData("dealer_lot", "bought", "garage")]
    [InlineData("part_shops_map", "shop", "part_shop")]
    [InlineData("part_shops_map", "mail_order", "mail_order")]
    [InlineData("part_shop", "back", "part_shops_map")]
    [InlineData("mail_order", "back", "part_shops_map")]
    [InlineData("diner", "back", "garage")]
    [InlineData("diner", "cruise", "street")]
    [InlineData("street", "back", "garage")]
    [InlineData("street", "diner", "diner")]
    [InlineData("gas_station", "back", "garage")]
    [InlineData("car_lot", "back", "garage")]
    [InlineData("garage", "car_lot", "car_lot")]
    [InlineData("garage", "quit", "main_menu")]
    [InlineData("racing_hub", "streets", "diner")]
    [InlineData("career", "won", "main_menu")]
    [InlineData("sound_browser", "back", "main_menu")]
    [InlineData("car_catalog_editor", "back", "main_menu")]
    public void The_base_games_ways_out_lead_where_the_screens_went(string from, string exit, string to)
    {
        Assert.Equal(to, BaseFlow.Exit(from, exit, new SystemRules()));
    }

    [Fact]
    public void The_dealer_map_has_no_way_to_the_mail_order_and_the_part_shops_map_has()
    {
        var all = new SystemRules();

        Assert.Null(BaseFlow.Exit("dealer_map", "mail_order", all));
        Assert.Equal("mail_order", BaseFlow.Exit("part_shops_map", "mail_order", all));
    }

    [Fact]
    public void The_base_games_tiles_stand_where_the_garages_buttons_stood()
    {
        var tiles = BaseFlow.TilesOf("garage", new SystemRules());

        string[] Spot(string spot) => tiles.Where(t => t.Tile.Spot == spot).Select(t => t.Id).ToArray();
        Assert.Equal(["quit"], Spot(TileSpots.TopLeft));
        Assert.Equal(["go_race"], Spot(TileSpots.Left));
        Assert.Equal(["car_dealers", "part_dealers", "newspaper"], Spot(TileSpots.BottomLeft));
        Assert.Equal(["car_info"], Spot(TileSpots.Right));
        Assert.Equal(["gas_station"], Spot(TileSpots.BottomRight));
        Assert.Equal(["career", "calendar", "radio"], Spot(TileSpots.TopRight));

        var large = tiles.Where(t => t.Tile.IsLarge).Select(t => t.Id).OrderBy(i => i);
        Assert.Equal(new[] { "car_dealers", "go_race", "newspaper", "part_dealers" }, large);
        Assert.Equal(9, tiles.Single(t => t.Id == "car_dealers").Tile.Padding);
        Assert.Equal([TileRequirements.CarHere], tiles.Single(t => t.Id == "gas_station").Tile.Requires);
        Assert.Equal([TileRequirements.Car], tiles.Single(t => t.Id == "car_info").Tile.Requires);
    }

    [Fact]
    public void The_base_games_cards_are_load_new_and_settings_from_the_left()
    {
        Assert.Equal(["load_game", "new_game", "settings"], BaseFlow.CardsInOrder().Select(c => c.Card.Card));
    }

    // ---- a system switched off ----

    [Fact]
    public void A_screen_of_a_system_switched_off_is_not_there_nor_the_ways_to_it()
    {
        var noDiner = new SystemRules { Diner = false };

        Assert.False(BaseFlow.IsOpen("diner", noDiner));
        Assert.Null(BaseFlow.Exit("racing_hub", "streets", noDiner));
        Assert.Null(BaseFlow.Exit("street", "diner", noDiner));
        Assert.Equal("garage", BaseFlow.Exit("street", "back", noDiner));
    }

    [Fact]
    public void The_tiles_to_a_screen_switched_off_are_not_shown()
    {
        var tiles = BaseFlow.TilesOf("garage", new SystemRules { Paper = false, Station = false, Career = false }).Select(t => t.Id).ToList();

        Assert.DoesNotContain("newspaper", tiles);
        Assert.DoesNotContain("gas_station", tiles);
        Assert.DoesNotContain("career", tiles);
        Assert.Contains("car_dealers", tiles);
        Assert.Contains("calendar", tiles);
    }

    [Fact]
    public void With_no_part_shops_the_mail_order_has_a_tile_of_its_own_that_leads_home()
    {
        var noShops = new SystemRules { PartShops = false };

        var tiles = BaseFlow.TilesOf("garage", noShops).Select(t => t.Id).ToList();
        Assert.DoesNotContain("part_dealers", tiles);
        Assert.Contains("mail_order", tiles);
        Assert.DoesNotContain("mail_order", BaseFlow.TilesOf("garage", new SystemRules()).Select(t => t.Id));

        // Its back leads to the part shops' map, which is not there: it goes home instead
        Assert.Null(BaseFlow.Exit("mail_order", "back", noShops));
    }

    [Fact]
    public void Every_switch_is_a_name_and_a_name_there_is_not_is_on()
    {
        var rules = new SystemRules();
        foreach (var name in SystemRules.Names)
        {
            Assert.True(rules.IsOn(name));
            Assert.True(SystemRules.IsName(name.ToUpperInvariant()));
        }

        Assert.True(rules.IsOn(null));
        Assert.True(rules.IsOn("nitrous"));
        Assert.False(SystemRules.IsName("nitrous"));
        Assert.False(new SystemRules { EngineStand = false }.IsOn("enginestand"));
    }

    [Theory]
    [InlineData("diner", "diner")]
    [InlineData("street", "street")]
    [InlineData("gas_station", "station")]
    [InlineData("newspaper", "paper")]
    [InlineData("dealer_lot", "carDealers")]
    [InlineData("part_shop", "partShops")]
    [InlineData("mail_order", "mailOrder")]
    [InlineData("career", "career")]
    [InlineData("garage", null)]
    public void A_screen_belongs_to_its_kinds_system_unless_it_names_one(string id, string? system)
    {
        Assert.Equal(system, BaseFlow.Screen(id)!.System);
    }

    // ---- a mod's flow ----

    [Fact]
    public void A_mods_flow_goes_over_the_base_games_key_by_key()
    {
        _temp.File($"{ModIds.BaseId}/mod.json", """{ "id": "streetcorsa" }""");
        _temp.File($"{ModIds.BaseId}/flow/flow.json", File.ReadAllText(Path.Combine(RepoPaths.BaseContent, "flow", "flow.json")));
        _temp.File("drift/mod.json", """{ "id": "drift" }""");
        _temp.File("drift/flow/flow.json", """
            {
              "home": "shed",
              "screens": {
                "shed": { "kind": "garage", "endDay": true, "exits": { "quit": "main_menu" } },
                "touge": { "kind": "street", "exits": { "back": "shed" } },
                "diner": { "exits": { "back": "touge" } },
                "newspaper": { "off": true }
              },
              "tiles": { "garage": { "career": { "off": true }, "touge": { "to": "touge", "icon": "icons/garage", "spot": "right", "order": 2 } } },
              "cards": { "settings": { "order": 0 } }
            }
            """);
        var problems = new List<string>();

        var book = FlowBook.Load(ModSet.Load(_temp.Path), problems: problems);

        Assert.Empty(problems);
        Assert.Equal("shed", book.Home);
        Assert.Equal("street", book.KindOf("touge"));
        Assert.Equal("touge", book.Exit("diner", "back", new SystemRules()));
        Assert.Equal("street", book.Exit("diner", "cruise", new SystemRules()));
        Assert.Null(book.Screen("newspaper"));
        Assert.Null(book.Exit("car_viewing", "back", new SystemRules()));
        var tiles = book.TilesOf("garage", new SystemRules()).Select(t => t.Id).ToList();
        Assert.DoesNotContain("career", tiles);
        Assert.DoesNotContain("newspaper", tiles);
        Assert.Equal(["car_info", "touge"], book.TilesOf("garage", new SystemRules()).Where(t => t.Tile.Spot == TileSpots.Right).Select(t => t.Id));
        Assert.Equal("settings", book.CardsInOrder()[0].Id);
    }

    [Fact]
    public void Home_is_a_garage()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "home": "touge",
              "screens": {
                "garage": { "kind": "garage" },
                "touge": { "kind": "street" },
                "main_menu": { "kind": "main_menu" }
              }
            }
            """, problems);

        Assert.Equal("garage", book.Home);
        Assert.Contains(problems, p => p.Contains("home \"touge\" is a street screen"));
    }

    [Fact]
    public void What_a_flow_cannot_use_is_told_and_left_out()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "home": "nowhere",
              "screens": {
                "garage": { "kind": "garage", "endDay": true, "exits": { "back": "limbo" } },
                "main_menu": { "kind": "main_menu", "endDay": true },
                "shop": { "kind": "part_shop", "system": "teleport" },
                "moon": { "kind": "moon_base" }
              },
              "tiles": {
                "garage": {
                  "a": { "to": "shop" },
                  "b": { "to": "fly" },
                  "c": { "to": "calendar", "spot": "ceiling", "requires": [ "wings" ] },
                  "d": { }
                },
                "attic": { "x": { "to": "radio" } }
              },
              "cards": { "x": { "card": "credits" } }
            }
            """, problems);

        Assert.Equal("garage", book.Home);
        Assert.Null(book.Screen("moon"));
        Assert.Null(book.Screen("shop")!.System);
        Assert.Empty(book.Screen("garage")!.Exits);
        Assert.True(book.Screen("garage")!.EndDay);
        Assert.False(book.Screen("main_menu")!.EndDay);
        var tiles = book.Tiles["garage"];
        Assert.Equal(["c"], tiles.Keys);
        Assert.Equal(TileSpots.BottomLeft, tiles["c"].Spot);
        Assert.Empty(tiles["c"].Requires);
        Assert.Empty(book.Cards);

        foreach (var said in new[]
                 {
                     "home \"nowhere\"", "\"moon_base\" is no kind", "\"teleport\" is no system", "no screen \"limbo\"",
                     "main_menu screen; endDay", "a part_shop screen cannot be opened from a tile", "\"fly\" is no screen",
                     "\"ceiling\" is no spot", "requires \"wings\"", "tiles.garage.d: goes nowhere", "tiles.attic: there is no screen",
                     "\"credits\" is no card"
                 })
            Assert.Contains(problems, p => p.Contains(said));
    }

    // ---- the scripts' facts ----

    [Fact]
    public void A_condition_on_the_screens_holds_for_the_screens_the_player_came_from_and_goes_to()
    {
        _temp.File($"{ModIds.BaseId}/scripts/feels.json", File.ReadAllText(Path.Combine(RepoPaths.Content, ModIds.BaseId, "scripts", "feels.json")));
        _temp.File("mymod/mod.json", """{ "id": "mymod" }""");
        _temp.File("mymod/scripts/mine.json", """
            { "scripts": [
              { "id": "from_lot", "trigger": "screen.garage.enter", "when": { "from": "car_lot" }, "steps": [ { "do": "wait", "seconds": 1 } ] },
              { "id": "to_paper", "trigger": "screen.garage.leave", "when": { "to": [ "newspaper", "dealer_map" ] }, "steps": [ { "do": "wait", "seconds": 1 } ] }
            ] }
            """);
        var problems = new List<string>();
        var book = ScriptBook.Load(ModSet.Load(_temp.Path), problems);

        Assert.Empty(problems);
        Assert.Equal("screen.garage.enter", ScriptTriggers.ScreenEnter("garage"));
        Assert.NotNull(book.For(ScriptTriggers.ScreenEnter("garage"), ScriptFacts.None with { From = "CAR_LOT" }, null));
        Assert.Null(book.For(ScriptTriggers.ScreenEnter("garage"), ScriptFacts.None with { From = "newspaper" }, null));
        Assert.Null(book.For(ScriptTriggers.ScreenEnter("garage"), ScriptFacts.None, null));
        Assert.NotNull(book.For(ScriptTriggers.ScreenLeave("garage"), ScriptFacts.None with { To = "dealer_map" }, null));
        Assert.Null(book.For(ScriptTriggers.ScreenLeave("garage"), ScriptFacts.None with { To = "diner" }, null));
    }

    // ---- the race's way back ----

    private sealed class Screen : IScreen
    {
        public void Enter() { }
        public void Exit() { }
        public void Resume() { }
    }

    private static NavigationService Navigation(DialogService dialogs) => new(
        dialogs, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, null!, null!, null!, null!, _ => { }, BaseFlow);

    [Fact]
    public void A_race_comes_back_to_the_screen_it_was_set_up_from()
    {
        var navigation = Navigation(new DialogService());
        Assert.True(navigation.SafeNavigate("garage", () => new Screen(), "garage"));

        // The race screen cannot be built here (no services); where it comes back to is settled before
        var hubRace = new DragRaceLaunchIntent();
        navigation.GoRace(new GameState(), hubRace);
        Assert.Equal("garage", hubRace.ReturnTo);

        var streetRace = new DragRaceLaunchIntent { ReturnTo = "street" };
        navigation.GoRace(new GameState(), streetRace);
        Assert.Equal("street", streetRace.ReturnTo);
    }

    [Fact]
    public void A_screen_knows_its_id_and_the_one_on_show_before_it()
    {
        var navigation = Navigation(new DialogService());
        var garage = new Screen();
        navigation.SafeNavigate("garage", () => garage, "garage");

        Assert.Equal("garage", navigation.CurrentId);
        Assert.True(navigation.IsHome(navigation.CurrentId));
        Assert.Null(navigation.IdOf(new Screen()));
    }

    // ---- leave scripts ----

    private sealed class StagedScreen : IScreen, IScriptedScreen
    {
        public readonly List<string> Played = [];
        public TaskCompletionSource Show = new();
        public int PutBack;

        public StagedScreen() => PlayScreenScript = (trigger, _) =>
        {
            Played.Add(trigger);
            return Show.Task;
        };

        public void Enter() { }
        public void Exit() { }
        public void Resume() { }
        public Func<string, ScriptFacts, Task?>? PlayScreenScript { get; set; }
        public ScriptFacts ScriptFacts => ScriptFacts.None;
        public void PutBackAfterLeave() => PutBack++;
    }

    [Fact]
    public void A_way_out_that_leads_nowhere_plays_no_leave_script()
    {
        var problems = new List<string>();
        var noLot = Read("""{ "screens": { "garage": { "kind": "garage", "exits": { "car_lot": "car_lot" } }, "main_menu": { "kind": "main_menu" }, "car_lot": { "kind": "car_lot", "off": true } } }""", problems);
        var navigation = new NavigationService(new DialogService(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, _ => { }, noLot);
        var garage = new StagedScreen();
        navigation.SafeNavigate("garage", () => garage, "garage");

        Assert.False(navigation.Leave(garage, "car_lot", new GameState()));
        Assert.Empty(garage.Played);
        Assert.Equal(0, garage.PutBack);
    }

    [Fact]
    public async Task An_awaited_leave_plays_the_script_then_the_curtain_then_goes()
    {
        var navigation = Navigation(new DialogService());
        var lot = new StagedScreen();
        navigation.SafeNavigate("car_lot", () => lot, "car_lot");
        var curtain = false;

        var leaving = navigation.LeaveAsync(lot, "back", new GameState(), first: () =>
        {
            curtain = true;
            return Task.CompletedTask;
        });

        Assert.Equal(["screen.car_lot.leave"], lot.Played);
        Assert.True(navigation.IsLeaving(lot));
        Assert.False(curtain);
        // A second way out meanwhile is not taken
        Assert.True(navigation.Leave(lot, "back", new GameState()));
        Assert.Single(lot.Played);

        lot.Show.SetResult();
        Assert.True(await leaving);
        Assert.True(curtain);
        Assert.Equal("garage", navigation.CurrentId);
        Assert.False(navigation.IsLeaving(lot));
        Assert.Equal(0, lot.PutBack);
    }

    [Fact]
    public void A_screen_the_flow_does_not_have_is_told_to_the_player()
    {
        var problems = new List<string>();
        var bare = Read("""{ "screens": { "garage": { "kind": "garage" }, "main_menu": { "kind": "main_menu" } } }""", problems);
        var dialogs = new DialogService();
        var navigation = new NavigationService(dialogs, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, _ => { }, bare);

        Assert.False(navigation.GoKind(ScreenKinds.SoundBrowser, null));
        Assert.True(dialogs.IsDialogOpen);
    }

    // ---- the window's End Day ----

    [Fact]
    public void The_day_ends_only_from_the_screens_the_flow_says()
    {
        var navigation = Navigation(new DialogService());

        navigation.SafeNavigate("diner", () => new Screen(), "diner");
        Assert.False(navigation.CanEndDayHere);

        var problems = new List<string>();
        var dinerNight = Read("""{ "screens": { "garage": { "kind": "garage" }, "main_menu": { "kind": "main_menu" }, "diner": { "kind": "diner", "endDay": true } } }""", problems);
        var withNights = new NavigationService(new DialogService(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, _ => { }, dinerNight);
        withNights.SafeNavigate("diner", () => new Screen(), "diner");
        Assert.True(withNights.CanEndDayHere);
        Assert.Empty(problems);
    }
}

/// <summary>The systems switched off in the rules of the game in play</summary>
[Collection(SystemsCollection.Name)]
public sealed class SystemSwitchTests
{
    private sealed class CountingTask(string? system) : IScheduledTask
    {
        public int Runs;
        public string TaskId => "counting_" + (system ?? "none");
        public int IntervalDays => 1;
        public string? System => system;

        public Task ExecuteAsync(GameState gameState, DateTime currentDate)
        {
            Runs++;
            return Task.CompletedTask;
        }
    }

    private static void PlayWith(SystemRules systems, Action test)
    {
        var before = RuleBook.InPlay();
        RuleBook.Current = new RuleBook { Systems = systems };
        try
        {
            test();
        }
        finally
        {
            before.Restore();
        }
    }

    [Fact]
    public void The_nights_work_of_a_system_switched_off_is_not_done()
    {
        PlayWith(new SystemRules { CarDealers = false }, () =>
        {
            var scheduler = new GameTimeScheduler();
            var dealers = new CountingTask("carDealers");
            var always = new CountingTask(null);
            scheduler.RegisterTask(dealers);
            scheduler.RegisterTask(always);
            var game = new GameState { Date = new DateTime(1965, 5, 1, 9, 0, 0) };

            scheduler.OnTimeAdvancedAsync(game, game.Date, game.Date.AddDays(1)).GetAwaiter().GetResult();

            Assert.Equal(0, dealers.Runs);
            Assert.Equal(1, always.Runs);
        });
    }

    [Fact]
    public void With_no_gas_station_the_cars_are_filled_and_washed_overnight()
    {
        var car = new Car("some_car") { FuelLitres = 2, OilLevel = 0.4, BodyDirt = 0.7 };
        var game = new GameState();
        game.Player.Cars = [car];

        PlayWith(new SystemRules(), () => new StationStandInTask().ExecuteAsync(game, DateTime.Today).GetAwaiter().GetResult());
        Assert.Equal(2, car.FuelLitres);

        PlayWith(new SystemRules { Station = false }, () => new StationStandInTask().ExecuteAsync(game, DateTime.Today).GetAwaiter().GetResult());
        Assert.Null(car.FuelLitres);
        Assert.Null(car.OilLevel);
        Assert.Equal(0, car.BodyDirt);
    }

    [Fact]
    public void With_no_police_none_come()
    {
        PlayWith(new SystemRules { Police = false }, () => Assert.Null(Services.Police.PoliceCars.Installed()));
    }

    [Fact]
    public void With_no_career_screen_the_game_is_won_on_the_rules_path()
    {
        var game = new GameState();

        PlayWith(new SystemRules { Career = false, CareerPath = "king" }, () =>
            Assert.Equal("king", Services.Career.VictoryConditionService.ActivePath(game)));
        PlayWith(new SystemRules { Career = false }, () =>
            Assert.Null(Services.Career.VictoryConditionService.ActivePath(game)));
        // With the screen, the player picks
        PlayWith(new SystemRules { CareerPath = "king" }, () =>
            Assert.Null(Services.Career.VictoryConditionService.ActivePath(game)));
        game.Career.ActiveVictoryType = "money";
        PlayWith(new SystemRules { Career = false, CareerPath = "king" }, () =>
            Assert.Equal("money", Services.Career.VictoryConditionService.ActivePath(game)));
    }
}
