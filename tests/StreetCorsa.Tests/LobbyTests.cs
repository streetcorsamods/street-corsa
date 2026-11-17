using StreetCorsa.Controls.Showcase;
using StreetCorsa.Dialogs;
using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Navigation;
using StreetCorsa.Rules;
using StreetCorsa.Screens.Lobby;
using StreetCorsa.Services.Career;
using StreetCorsa.Services.Garage;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>A lobby (Home) as a game's front page, and the roads' screen it drives from</summary>
public sealed class LobbyTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static FlowBook Read(string json, List<string> problems) => FlowBook.Read(ModMerge.Parse(json)!, problems);

    private const string LobbyHome = """
        {
          "home": "home",
          "screens": {
            "main_menu": { "kind": "main_menu" },
            "home": { "kind": "lobby", "exits": { "quit": "main_menu" } },
            "garage": { "kind": "garage", "endDay": true, "exits": { "back": "home" } },
            "roads": { "kind": "road_picker", "exits": { "back": "home", "garage": "garage" } },
            "challenges": { "kind": "challenges" }
          },
          "tiles": {
            "home": {
              "drive": { "to": "roads", "text": "lobby.tile.drive", "spot": "left", "order": 1, "requires": [ "car" ] },
              "challenges": { "to": "challenges", "text": "lobby.tile.challenges", "spot": "left", "order": 2 },
              "garage": { "to": "garage", "text": "lobby.tile.garage", "spot": "left", "order": 3 },
              "settings": { "to": "settings", "text": "lobby.tile.settings", "spot": "top_right", "order": 1 },
              "quit": { "to": "quit", "text": "lobby.tile.quit", "spot": "top_left" }
            }
          }
        }
        """;

    [Fact]
    public void Home_may_be_a_lobby_with_its_tiles_and_ways_out()
    {
        var problems = new List<string>();
        var book = Read(LobbyHome, problems);

        Assert.Empty(problems);
        Assert.Equal("home", book.Home);
        Assert.Equal(ScreenKinds.Lobby, book.KindOf(book.Home));
        Assert.Equal(["drive", "challenges", "garage"],
            book.TilesOf("home", new SystemRules()).Where(t => t.Tile.Spot == TileSpots.Left).Select(t => t.Id));
        Assert.Equal("settings", book.TilesOf("home", new SystemRules()).Single(t => t.Tile.Spot == TileSpots.TopRight).Tile.To);
        Assert.Equal("home", book.Exit("garage", "back", new SystemRules()));
        Assert.Equal("garage", book.Exit("roads", "garage", new SystemRules()));
    }

    [Fact]
    public void Home_of_another_kind_than_a_garage_or_a_lobby_is_told()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "home": "roads",
              "screens": {
                "main_menu": { "kind": "main_menu" },
                "garage": { "kind": "garage" },
                "roads": { "kind": "road_picker" }
              }
            }
            """, problems);

        Assert.Equal("garage", book.Home);
        Assert.Contains(problems, p => p.Contains("home \"roads\" is a road_picker screen, and home must be a garage or a lobby"));
    }

    [Fact]
    public void The_day_is_not_ended_from_a_lobby_or_the_roads()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "screens": {
                "main_menu": { "kind": "main_menu" },
                "garage": { "kind": "garage" },
                "home": { "kind": "lobby", "endDay": true },
                "roads": { "kind": "road_picker", "endDay": true }
              }
            }
            """, problems);

        Assert.False(book.Screen("home")!.EndDay);
        Assert.False(book.Screen("roads")!.EndDay);
        Assert.Contains(problems, p => p.Contains("the day cannot be ended from a lobby screen"));
        Assert.Contains(problems, p => p.Contains("the day cannot be ended from a road_picker screen"));
    }

    [Fact]
    public void Home_for_the_night_is_the_first_garage_when_home_is_a_lobby()
    {
        var problems = new List<string>();
        var navigation = TestNavigation.WithoutServices(new DialogService(), Read(LobbyHome, problems));

        Assert.Equal("home", navigation.HomeFor(null));
        Assert.Equal("home", navigation.HomeFor(new FlowArgs { SkipAnimation = true }));
        Assert.Equal("garage", navigation.HomeFor(new FlowArgs { EndDay = true }));
    }

    [Fact]
    public void Home_for_the_night_is_home_when_home_is_a_garage()
    {
        var navigation = TestNavigation.WithoutServices(new DialogService(), FlowBook.Load(ModSet.BaseOnly(RepoPaths.BaseContent)));

        Assert.Equal("garage", navigation.HomeFor(new FlowArgs { EndDay = true }));
    }

    [Fact]
    public void Settings_is_something_a_tile_does()
    {
        Assert.True(TileActions.IsAction(TileActions.Settings));
        Assert.True(ScreenKinds.CanBeHome(ScreenKinds.Lobby));
        Assert.True(ScreenKinds.CanBeHome(ScreenKinds.Garage));
        Assert.False(ScreenKinds.CanBeHome(ScreenKinds.RoadPicker));
        Assert.False(ScreenKinds.IsOverlay(ScreenKinds.Lobby));
        Assert.False(ScreenKinds.IsOverlay(ScreenKinds.RoadPicker));
    }

    // ---- what the lobby tells of the career ----

    private static ChallengeBook NoChallenges => ChallengeBook.Load(ModSet.BaseOnly(RepoPaths.BaseContent));

    private static GameState Banked(double score) => new()
    {
        Career = new CareerState { RoadBests = new Dictionary<string, double> { ["free|road|"] = score } }
    };

    [Fact]
    public void A_game_with_no_Challenges_and_no_tiers_tells_no_progress()
    {
        Assert.Null(LobbyProgress.Of(Banked(1200), new UnlockRules(), NoChallenges));
    }

    [Fact]
    public void The_progress_tells_the_banked_score_and_the_next_tier_and_what_it_still_wants()
    {
        var rules = new UnlockRules { Tiers = [0, 1000, 5000] };

        var progress = LobbyProgress.Of(Banked(1500), rules, NoChallenges)!;

        Assert.Contains(1500.ToString("N0", Locale.Culture), progress.Banked);
        Assert.Contains("3", progress.NextTier);
        Assert.Contains(3500.ToString("N0", Locale.Culture), progress.NextTier);
        Assert.Equal(0, progress.ChallengeCount);
        Assert.False(progress.HasChallenges);
    }

    [Fact]
    public void Every_tier_open_is_told_as_such()
    {
        var rules = new UnlockRules { Tiers = [0, 1000, 5000] };

        var progress = LobbyProgress.Of(Banked(6000), rules, NoChallenges)!;

        Assert.Equal(Services.Text.GameText.Say("lobby.tiers.all"), progress.NextTier);
    }

    // ---- the car filmed in the garage's room ----

    private static GarageRoom Room(GarageWalls? walls) => GarageRoom.None("studio") with { Walls = walls };

    [Fact]
    public void The_camera_keeps_inside_a_round_rooms_radius_else_its_nearest_wall()
    {
        Assert.Equal(13f, LobbyScreenViewModel.WallRadiusOf(Room(new GarageWalls(-20, 20, -20, 20, 5, 13))));
        Assert.Equal(6f, LobbyScreenViewModel.WallRadiusOf(Room(new GarageWalls(-8, 6, -10, 12, 4))));
        Assert.Equal(12f, LobbyScreenViewModel.WallRadiusOf(Room(null)));
    }

    [Fact]
    public void One_car_in_one_room_is_a_single_set_that_stays()
    {
        var car = new ShowcaseCar("car", "dir", "A car", ["red"]);
        var scene = new ShowcaseScene("studio", "studio.kn5", 13f, GarageRoom.None("studio"));

        var single = new ShowcasePlaylist([car], [scene], new Random(1));
        Assert.True(single.IsSingle);
        var first = single.Next();
        var again = single.Next();
        Assert.Same(scene, first.Scene);
        Assert.Same(scene, again.Scene);
        Assert.Equal(car, Assert.Single(again.Cars).Car);

        Assert.False(new ShowcasePlaylist([car, car with { Id = "other" }], [scene], new Random(1)).IsSingle);
        Assert.False(new ShowcasePlaylist([car], [scene, scene with { Id = "other" }], new Random(1)).IsSingle);
    }

    [Fact]
    public void A_studio_lit_by_its_scene_file_gives_its_light_for_the_day()
    {
        _temp.File("studio/mod.json", """{ "id": "studio", "type": "setting" }""");
        _temp.File("studio/garages/studio/scene.json", """
            {
              "room": { "minX": -13, "maxX": 13, "minZ": -13, "maxZ": 13, "height": 5, "radius": 13 },
              "sun": [ 0.35, 0.9, 0.25 ],
              "lights": { "day": { "brightness": 0.75, "color": [ 255, 252, 248 ], "ambient": 1.1 } }
            }
            """);

        var room = GarageRooms.Load(ModSet.Load(_temp.Path), "studio");

        Assert.True(room.IsLit);
        Assert.Equal(13f, LobbyScreenViewModel.WallRadiusOf(room));
        Assert.NotNull(room.Dress(StreetLight.Day).Lighting);
    }
}
