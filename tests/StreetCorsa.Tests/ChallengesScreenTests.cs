using System.Numerics;
using AcTools.Kn5File;
using StreetCorsa.Mods;
using StreetCorsa.Navigation;
using StreetCorsa.Parts;
using StreetCorsa.Rules;
using StreetCorsa.Services.Showcase;

namespace StreetCorsa.Tests;

/// <summary>
/// The Challenges on a screen of their own: the way back to the screen the player came from (a car picked for it), and
/// the light copies of the roads its backdrop films
/// </summary>
public sealed class ChallengesScreenTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static FlowBook Read(string json, List<string> problems) => FlowBook.Read(ModMerge.Parse(json)!, problems);

    private const string Flow = """
        {
          "home": "home",
          "screens": {
            "main_menu": { "kind": "main_menu" },
            "home": { "kind": "lobby", "exits": { "quit": "main_menu" } },
            "garage": { "kind": "garage", "exits": { "back": "came_from" } },
            "roads": { "kind": "road_picker", "exits": { "back": "home", "garage": "garage" } },
            "challenges": { "kind": "challenges", "exits": { "back": "home", "garage": "garage" } },
            "race": { "kind": "race" }
          }
        }
        """;

    // ---- the way back to where the player came from ----

    [Fact]
    public void A_way_out_to_came_from_is_no_problem_and_is_kept()
    {
        var problems = new List<string>();
        var book = Read(Flow, problems);

        Assert.DoesNotContain(problems, p => p.Contains("came_from"));
        Assert.Equal(FlowBook.CameFrom, book.Screen("garage")!.Exits["back"]);
    }

    [Theory]
    [InlineData("challenges", "challenges")]
    [InlineData("roads", "roads")]
    [InlineData("home", "home")]
    public void A_way_out_to_came_from_leads_back_to_the_screen_the_player_came_from(string cameFrom, string expected)
    {
        var book = Read(Flow, []);

        Assert.Equal(expected, book.Exit("garage", "back", new SystemRules(), cameFrom));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("race")]
    [InlineData("main_menu")]
    [InlineData("garage")]
    [InlineData("nowhere")]
    public void A_way_out_to_came_from_leads_nowhere_when_that_screen_cannot_be_gone_back_to(string? cameFrom)
    {
        // Not known, one that must be told what to show, the main menu, the screen itself, one the flow has not
        var book = Read(Flow, []);

        Assert.Null(book.Exit("garage", "back", new SystemRules(), cameFrom));
    }

    [Fact]
    public void Other_ways_out_pay_no_heed_to_where_the_player_came_from()
    {
        var book = Read(Flow, []);

        Assert.Equal("home", book.Exit("challenges", "back", new SystemRules(), cameFrom: "garage"));
        Assert.Equal("garage", book.Exit("challenges", "garage", new SystemRules(), cameFrom: "roads"));
    }

    [Fact]
    public void A_way_out_to_came_from_on_an_overlay_or_after_a_race_is_a_problem_and_dropped()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "screens": {
                "main_menu": { "kind": "main_menu" },
                "garage": { "kind": "garage", "exits": { "back": "came_from" } },
                "career": { "kind": "career", "exits": { "won": "came_from" } },
                "roads": { "kind": "road_picker", "exits": { "back": "garage", "after_race": "came_from" } }
              }
            }
            """, problems);

        Assert.Contains(problems, p => p.StartsWith("screens.career.exits.won:"));
        Assert.Contains(problems, p => p.StartsWith("screens.roads.exits.after_race:"));
        Assert.False(book.Screen("career")!.Exits.ContainsKey("won"));
        Assert.False(book.Screen("roads")!.Exits.ContainsKey("after_race"));
        // A screen's own way back stays
        Assert.Equal(FlowBook.CameFrom, book.Screen("garage")!.Exits["back"]);
        Assert.Equal("garage", book.Screen("roads")!.Exits["back"]);
    }

    [Fact]
    public void A_screen_may_not_be_called_came_from()
    {
        var problems = new List<string>();
        var book = Read("""
            {
              "screens": {
                "main_menu": { "kind": "main_menu" },
                "garage": { "kind": "garage", "exits": { "back": "came_from" } },
                "came_from": { "kind": "car_lot" }
              }
            }
            """, problems);

        Assert.Contains(problems, p => p.StartsWith("screens.came_from:"));
        Assert.Null(book.Screen("came_from"));
        Assert.True(book.LeadsBack("garage", "back"));
        Assert.False(book.LeadsBack("garage", "quit"));
    }

    private sealed class Screen : IScreen
    {
        public void Enter() { }
        public void Exit() { }
        public void Resume() { }
    }

    [Fact]
    public void A_way_back_to_where_the_player_came_from_tells_that_screen_what_it_was_told()
    {
        var navigation = TestNavigation.WithoutServices(new Dialogs.DialogService(), Read(Flow, []));
        navigation.SafeNavigate("challenges", () => new Screen(), "challenges",
            new FlowArgs { DinerId = "corner", EndDay = true, SkipAnimation = false });
        navigation.SafeNavigate("garage", () => new Screen(), "garage");

        var back = navigation.ArgsBack("garage", new FlowArgs { SkipAnimation = true });

        Assert.Equal("corner", back.DinerId);
        // Said once, not again; and up at once as the screen leaving asks
        Assert.False(back.EndDay);
        Assert.True(back.SkipAnimation);
        // Only for the screen on show
        Assert.Same(FlowArgs.None, navigation.ArgsBack("challenges", null));
    }

    [Fact]
    public void What_was_picked_on_the_screens_built_again_is_kept_per_game()
    {
        var game = new Models.GameState.GameState();
        Screens.Shared.ScreenPicks.Of(game).Challenge = "europe";
        Screens.Shared.ScreenPicks.Of(game).Road = "ks_road/layout";

        Assert.Equal("europe", Screens.Shared.ScreenPicks.Of(game).Challenge);
        Assert.Equal("ks_road/layout", Screens.Shared.ScreenPicks.Of(game).Road);
        Assert.Null(Screens.Shared.ScreenPicks.Of(new Models.GameState.GameState()).Challenge);
    }

    [Fact]
    public void The_challenges_are_a_screen_of_their_own_not_an_overlay()
    {
        Assert.False(ScreenKinds.IsOverlay(ScreenKinds.Challenges));
        Assert.Equal("career", ScreenKinds.SystemOf(ScreenKinds.Challenges));
    }

    // ---- a road's backdrop ----

    /// <summary>
    /// A road along z from 0 to 1000 m: its AI line, and two models. The road's names two materials alike ("first", the
    /// second one blue): AC tells them apart by order; the trees' names a third "first" of its own.
    /// </summary>
    private string Road()
    {
        var folder = _temp.Combine("tracks", "test_road");
        Directory.CreateDirectory(Path.Combine(folder, "ai"));
        File.WriteAllBytes(Path.Combine(folder, "ai", "fast_lane.ai"),
            AiLineBytes(Enumerable.Range(0, 101).Select(i => new Vector3(0, 0, i * 10f)).ToList()));

        var road = Kn5.CreateEmpty();
        var first = PlaceholderMesh.SolidMaterial(road, "first", 200, 0, 0, 0.5f, 0.5f, 0.1f, 10);
        var second = PlaceholderMesh.SolidMaterial(road, "second", 0, 0, 200, 0.5f, 0.5f, 0.1f, 10);
        road.Materials["second"].Name = "first";
        road.RootNode.Children.Add(PlaceholderMesh.Boxes("near_a", [(new Vector3(0, 0, 100), new Vector3(4, 1, 4))], first));
        road.RootNode.Children.Add(PlaceholderMesh.Boxes("near_b", [(new Vector3(5, 0, 200), new Vector3(4, 1, 4))], second));
        // Small and 300 m off: out; big and 600 m off but reaching to within 500: in; small and kilometres off: out
        road.RootNode.Children.Add(PlaceholderMesh.Boxes("far_small", [(new Vector3(300, 0, 500), new Vector3(2, 2, 2))], first));
        road.RootNode.Children.Add(PlaceholderMesh.Boxes("mid_big", [(new Vector3(600, 0, 500), new Vector3(100, 100, 100))], first));
        road.RootNode.Children.Add(PlaceholderMesh.Boxes("far_away", [(new Vector3(3000, 0, 500), new Vector3(2, 2, 2))], first));
        // On the road but never drawn (a collider): out
        var hidden = PlaceholderMesh.Boxes("hidden_wall", [(new Vector3(-6, 0, 300), new Vector3(1, 2, 20))], first);
        hidden.IsVisible = false;
        road.RootNode.Children.Add(hidden);
        road.Save(Path.Combine(folder, "test_road.kn5"));

        var trees = Kn5.CreateEmpty();
        var leaves = PlaceholderMesh.SolidMaterial(trees, "first", 0, 200, 0, 0.5f, 0.5f, 0.1f, 10);
        trees.Textures.Remove("first.bmp", out var texture);
        var data = trees.TexturesData["first.bmp"];
        trees.TexturesData.Remove("first.bmp");
        texture!.Name = "leaves.bmp";
        trees.Textures["leaves.bmp"] = texture;
        trees.TexturesData["leaves.bmp"] = data;
        trees.Materials["first"].TextureMappings[0].Texture = "leaves.bmp";
        trees.RootNode.Children.Add(PlaceholderMesh.Boxes("tree", [(new Vector3(-8, 0, 400), new Vector3(2, 5, 2))], leaves));
        trees.Save(Path.Combine(folder, "trees.kn5"));

        File.WriteAllText(Path.Combine(folder, "models.ini"),
            "[MODEL_0]\nFILE=test_road.kn5\nPOSITION=0,0,0\n\n[MODEL_1]\nFILE=trees.kn5\nPOSITION=0,0,0\n\n[MODEL_2]\nFILE=missing.kn5\n");
        return folder;
    }

    [Fact]
    public void A_roads_models_are_the_ones_its_models_ini_lists_that_are_there_else_its_own()
    {
        var folder = Road();
        Assert.Equal(["test_road.kn5", "trees.kn5"], RoadBackdrops.Pieces(folder, null).Select(Path.GetFileName));

        File.Delete(Path.Combine(folder, "models.ini"));
        Assert.Equal(["test_road.kn5"], RoadBackdrops.Pieces(folder, null).Select(Path.GetFileName));
    }

    [Fact]
    public void A_backdrop_is_every_model_in_one_each_material_where_ac_has_it_what_lies_off_the_road_left_out()
    {
        var folder = Road();
        var root = _temp.Combine("backdrops");

        var file = RoadBackdrops.Make(folder, "test_road", null, root, texels: null);

        Assert.Equal(RoadBackdrops.FileFor("test_road", null, root), file);
        var kn5 = Kn5.FromFile(file!, DefaultKn5TextureLoader.Instance, DefaultKn5MaterialLoader.Instance, DefaultKn5NodeLoader.Instance);
        var meshes = Meshes(kn5.RootNode).ToDictionary(n => n.Name);
        Assert.Equal(["near_a", "near_b", "mid_big", "tree"], meshes.Keys);

        // Each mesh in the material it was given, the two alike-named ones and the trees' own told apart
        var materials = kn5.Materials.Values.ToList();
        Assert.Equal(3, materials.Count);
        string TextureOf(string mesh) => materials[(int)meshes[mesh].MaterialId].TextureMappings[0].Texture;
        Assert.Equal("first.bmp", TextureOf("near_a"));
        Assert.Equal("second.bmp", TextureOf("near_b"));
        Assert.Equal("leaves.bmp", TextureOf("tree"));
        Assert.Equal(3, kn5.Textures.Count);
    }

    [Fact]
    public void A_long_road_is_filmed_on_two_stretches_apart_a_short_one_whole()
    {
        Assert.Equal([(1000f, 1000f + RoadBackdrops.StretchLength), (3100f, 3100f + RoadBackdrops.StretchLength)], RoadBackdrops.Stretches(5000f));
        Assert.Equal([(0f, 1000f)], RoadBackdrops.Stretches(1000f));
    }

    [Fact]
    public void A_long_roads_backdrop_keeps_only_what_lies_by_its_stretches()
    {
        // 5 km along z: the stretches run from 1000 and from 3100 m
        var folder = _temp.Combine("tracks", "long_road");
        Directory.CreateDirectory(Path.Combine(folder, "ai"));
        File.WriteAllBytes(Path.Combine(folder, "ai", "fast_lane.ai"),
            AiLineBytes(Enumerable.Range(0, 501).Select(i => new Vector3(0, 0, i * 10f)).ToList()));
        var kn5 = Kn5.CreateEmpty();
        var grey = PlaceholderMesh.SolidMaterial(kn5, "grey", 90, 90, 90, 0.5f, 0.5f, 0.1f, 10);
        foreach (var z in new[] { 1150, 2000, 3300, 4800 })
            kn5.RootNode.Children.Add(PlaceholderMesh.Boxes($"box_{z}", [(new Vector3(4, 0, z), new Vector3(2, 2, 2))], grey));
        kn5.Save(Path.Combine(folder, "long_road.kn5"));

        var file = RoadBackdrops.Make(folder, "long_road", null, _temp.Combine("backdrops"), texels: null);

        var made = Kn5.FromFile(file!, DefaultKn5TextureLoader.Instance, DefaultKn5MaterialLoader.Instance, DefaultKn5NodeLoader.Instance);
        Assert.Equal(["box_1150", "box_3300"], Meshes(made.RootNode).Select(n => n.Name));
    }

    [Fact]
    public void A_backdrop_is_ready_until_a_model_of_its_road_changes()
    {
        var folder = Road();
        var root = _temp.Combine("backdrops");
        Assert.Null(RoadBackdrops.Ready(folder, "test_road", null, root));

        var file = RoadBackdrops.Make(folder, "test_road", null, root, texels: null);
        Assert.Equal(file, RoadBackdrops.Ready(folder, "test_road", null, root));

        File.SetLastWriteTimeUtc(Path.Combine(folder, "trees.kn5"), DateTime.UtcNow.AddMinutes(5));
        Assert.Null(RoadBackdrops.Ready(folder, "test_road", null, root));
    }

    [Fact]
    public void A_road_with_no_ai_line_has_no_backdrop()
    {
        var folder = Road();
        File.Delete(Path.Combine(folder, "ai", "fast_lane.ai"));

        Assert.Null(RoadBackdrops.Make(folder, "test_road", null, _temp.Combine("backdrops"), texels: null));
    }

    [Fact]
    public void A_backdrops_file_is_named_by_its_track_and_layout()
    {
        Assert.Equal("ks_road__layout_long.kn5", Path.GetFileName(RoadBackdrops.FileFor("ks_road", "layout_long", "x")));
        Assert.Equal("ks_road.kn5", Path.GetFileName(RoadBackdrops.FileFor("ks_road", null, "x")));
    }

    private static IEnumerable<Kn5Node> Meshes(Kn5Node node) =>
        node.NodeClass == Kn5NodeClass.Base ? (node.Children ?? []).SelectMany(Meshes) : [node];

    /// <summary>An AI line file (version 7) through these points, its extras all nought</summary>
    private static byte[] AiLineBytes(IReadOnlyList<Vector3> points)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(7);
        writer.Write(points.Count);
        writer.Write(0);
        writer.Write(0);
        for (var i = 0; i < points.Count; i++)
        {
            writer.Write(points[i].X);
            writer.Write(points[i].Y);
            writer.Write(points[i].Z);
            writer.Write(i * 10f);
            writer.Write(i);
        }

        writer.Write(points.Count);
        for (var i = 0; i < points.Count * 18; i++) writer.Write(0f);
        writer.Flush();
        return stream.ToArray();
    }
}
