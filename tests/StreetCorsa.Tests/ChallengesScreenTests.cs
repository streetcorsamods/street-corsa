using System.Numerics;
using AcTools.Kn5File;
using AcTools.Numerics;
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

    /// <summary>
    /// A road along z from 0 to 1000 m with its own model and these others, written by <paramref name="make"/> into
    /// models.ini's MODEL sections in order (their lines after FILE)
    /// </summary>
    private string RoadWith(string id, params (string File, string Lines, Action<IKn5> Make)[] pieces)
    {
        var folder = _temp.Combine("tracks", id);
        Directory.CreateDirectory(Path.Combine(folder, "ai"));
        File.WriteAllBytes(Path.Combine(folder, "ai", "fast_lane.ai"),
            AiLineBytes(Enumerable.Range(0, 101).Select(i => new Vector3(0, 0, i * 10f)).ToList()));
        var ini = new System.Text.StringBuilder();
        for (var i = 0; i < pieces.Length; i++)
        {
            var kn5 = Kn5.CreateEmpty();
            pieces[i].Make(kn5);
            kn5.Save(Path.Combine(folder, pieces[i].File));
            ini.Append($"[MODEL_{i}]\nFILE={pieces[i].File}\n{pieces[i].Lines}\n\n");
        }

        File.WriteAllText(Path.Combine(folder, "models.ini"), ini.ToString());
        return folder;
    }

    private static Kn5Node Box(IKn5 kn5, string name, Vector3 at, float half = 2, string material = "grey") =>
        PlaceholderMesh.Boxes(name, [(at, new Vector3(half))], PlaceholderMesh.SolidMaterial(kn5, material, 90, 90, 90, 0.5f, 0.5f, 0.1f, 10));

    private static IKn5 Read(string file) =>
        Kn5.FromFile(file, DefaultKn5TextureLoader.Instance, DefaultKn5MaterialLoader.Instance, DefaultKn5NodeLoader.Instance);

    // A mesh's points in the world: through every Base node's transform above it
    private static IEnumerable<(Kn5Node Mesh, Mat4x4 World)> Placed(Kn5Node node, Mat4x4 parent)
    {
        if (node.NodeClass != Kn5NodeClass.Base) return [(node, parent)];
        var world = node.Transform * parent;
        return (node.Children ?? []).SelectMany(child => Placed(child, world));
    }

    private static Vec3 MiddleOf((Kn5Node Mesh, Mat4x4 World) placed) =>
        Vec3.Transform(placed.Mesh.Vertices.Aggregate(new Vec3(0, 0, 0), (sum, v) => sum + v.Position) / placed.Mesh.Vertices.Length, placed.World);

    [Fact]
    public void A_backdrop_puts_each_model_where_its_models_ini_does_and_crops_it_there()
    {
        // Each a small box: at 3 km off but moved onto the road, at the road but moved 3 km off, 500 m to the side but
        // turned onto it, and on the road in a model AC never draws (its root switched off)
        var folder = RoadWith("placed_road",
            ("road.kn5", "POSITION=0,0,0\nROTATION=0,0,0", k => k.RootNode.Children.Add(Box(k, "road_box", new Vector3(4, 0, 100)))),
            ("moved_on.kn5", "POSITION=-3000,0,500", k => k.RootNode.Children.Add(Box(k, "moved_on", new Vector3(3000, 0, 0)))),
            ("moved_off.kn5", "POSITION=3000,0,0", k => k.RootNode.Children.Add(Box(k, "moved_off", new Vector3(0, 0, 400)))),
            ("turned.kn5", "POSITION=0,0,0\nROTATION=-90,0,0", k => k.RootNode.Children.Add(Box(k, "turned", new Vector3(500, 0, 0)))),
            ("off.kn5", "", k =>
            {
                k.RootNode.Active = false;
                k.RootNode.Children.Add(Box(k, "never_drawn", new Vector3(0, 0, 300)));
            }));

        var file = RoadBackdrops.Make(folder, "placed_road", null, _temp.Combine("backdrops"), texels: null);

        var placed = Placed(Read(file!).RootNode, Mat4x4.Identity).ToDictionary(p => p.Mesh.Name);
        Assert.Equal(["road_box", "moved_on", "turned"], placed.Keys);
        Assert.True(Vec3.Distance(MiddleOf(placed["moved_on"]), new Vec3(0, 0, 500)) < 0.01f);
        Assert.True(Vec3.Distance(MiddleOf(placed["turned"]), new Vec3(0, 0, 500)) < 0.01f);
    }

    [Fact]
    public void A_mesh_under_a_moved_node_is_cropped_where_the_node_puts_it()
    {
        // A dummy 3 km off holding a box at its own origin: off the road. One at 500 m along it: on the road. And a big
        // land mesh under it, cut: its tiles near the road kept, the far ones left out
        var folder = RoadWith("dummy_road", ("road.kn5", "", k =>
        {
            var far = Kn5Node.CreateBaseNode("far_dummy");
            far.Transform = Mat4x4.CreateTranslation(3000, 0, 500);
            far.Children.Add(Box(k, "far_sign", Vector3.Zero));
            var near = Kn5Node.CreateBaseNode("near_dummy");
            near.Transform = Mat4x4.CreateTranslation(0, 0, 500);
            near.Children.Add(Box(k, "near_sign", Vector3.Zero));
            // 40 by 40 m tiles from x = -1600 to 1600 at the dummy: 4800 triangles' indices, over the cut's 6000 with the
            // boxes' 36 each
            var tiles = Enumerable.Range(0, 80).SelectMany(i => Enumerable.Range(0, 3).Select(j => (new Vector3(-1600 + i * 40 + 20, -30, -400 + j * 400), new Vector3(20, 1, 20)))).ToList();
            near.Children.Add(PlaceholderMesh.Boxes("land", tiles, PlaceholderMesh.SolidMaterial(k, "grass", 0, 120, 0, 0.5f, 0.5f, 0.1f, 10)));
            k.RootNode.Children.Add(far);
            k.RootNode.Children.Add(near);
        }));

        var file = RoadBackdrops.Make(folder, "dummy_road", null, _temp.Combine("backdrops"), texels: null);

        var placed = Placed(Read(file!).RootNode, Mat4x4.Identity).ToDictionary(p => p.Mesh.Name);
        Assert.Equal(["near_sign", "land"], placed.Keys);
        var land = placed["land"];
        var xs = land.Mesh.Vertices.Select(v => Vec3.Transform(v.Position, land.World).X).ToList();
        Assert.True(xs.Max() < 600 && xs.Min() > -600, $"land kept from {xs.Min()} to {xs.Max()}");
        Assert.True(land.Mesh.Indices.Length < 80 * 3 * 36);
    }

    [Fact]
    public void Two_models_pictures_of_the_same_name_each_stay_their_own()
    {
        void Painted(IKn5 k, string mesh, byte red)
        {
            var id = PlaceholderMesh.SolidMaterial(k, "paint", red, 0, 0, 0.5f, 0.5f, 0.1f, 10);
            k.RootNode.Children.Add(PlaceholderMesh.Boxes(mesh, [(new Vector3(0, 0, 100 + red), new Vector3(2))], id));
        }

        var folder = RoadWith("clash_road",
            ("a.kn5", "", k => Painted(k, "red_box", 200)),
            ("b.kn5", "", k => Painted(k, "dark_box", 50)),
            ("c.kn5", "", k => Painted(k, "red_again", 200)));

        var kn5 = Read(RoadBackdrops.Make(folder, "clash_road", null, _temp.Combine("backdrops"), texels: null)!);

        var materials = kn5.Materials.Values.ToList();
        var meshes = Meshes(kn5.RootNode).ToDictionary(n => n.Name);
        byte[] PictureOf(string mesh) => kn5.TexturesData[materials[(int)meshes[mesh].MaterialId].TextureMappings[0].Texture];
        Assert.Equal(Kn5Bitmap.Solid(200, 0, 0), PictureOf("red_box"));
        Assert.Equal(Kn5Bitmap.Solid(50, 0, 0), PictureOf("dark_box"));
        Assert.Equal(Kn5Bitmap.Solid(200, 0, 0), PictureOf("red_again"));
        // The same picture under the same name is kept once
        Assert.Equal(2, kn5.Textures.Count);
    }

    [Fact]
    public void A_material_only_meshes_left_out_used_is_left_out_with_its_picture()
    {
        var folder = RoadWith("unused_road", ("road.kn5", "", k =>
        {
            k.RootNode.Children.Add(Box(k, "kept", new Vector3(0, 0, 100), material: "asphalt"));
            k.RootNode.Children.Add(Box(k, "dropped", new Vector3(3000, 0, 100), material: "far_paint"));
            k.RootNode.Children.Add(Box(k, "kept_too", new Vector3(0, 0, 200), material: "kerb"));
        }));

        var kn5 = Read(RoadBackdrops.Make(folder, "unused_road", null, _temp.Combine("backdrops"), texels: null)!);

        Assert.Equal(["asphalt", "kerb"], kn5.Materials.Keys);
        Assert.Equal(["asphalt.bmp", "kerb.bmp"], kn5.Textures.Keys.Order());
        var materials = kn5.Materials.Values.ToList();
        Assert.Equal("kerb.bmp", materials[(int)Meshes(kn5.RootNode).Single(n => n.Name == "kept_too").MaterialId].TextureMappings[0].Texture);
    }

    [Fact]
    public void A_cancelled_make_stops_and_leaves_no_backdrop()
    {
        var folder = Road();
        var root = _temp.Combine("backdrops");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => RoadBackdrops.Make(folder, "test_road", null, root, texels: null, cancel.Token));

        Assert.False(File.Exists(RoadBackdrops.FileFor("test_road", null, root)));
        Assert.False(RoadBackdrops.Failed(folder, "test_road", null, root));
    }

    [Fact]
    public async Task A_road_that_cannot_be_made_is_not_tried_again_until_it_changes()
    {
        var folder = Road();
        var root = _temp.Combine("backdrops");
        var trees = Path.Combine(folder, "trees.kn5");
        File.WriteAllBytes(trees, [1, 2, 3, 4]);

        Assert.Null(RoadBackdrops.Make(folder, "test_road", null, root, texels: null));
        Assert.True(RoadBackdrops.Failed(folder, "test_road", null, root));
        Assert.Null(await RoadBackdrops.GetAsync(folder, "test_road", null, TestContext.Current.CancellationToken, root));

        // Mended: tried again, and made
        File.Delete(trees);
        Assert.False(RoadBackdrops.Failed(folder, "test_road", null, root));
        Assert.NotNull(RoadBackdrops.Make(folder, "test_road", null, root, texels: null));
        Assert.False(RoadBackdrops.Failed(folder, "test_road", null, root));
    }

    [Fact]
    public void The_corridor_tells_near_from_far_and_a_sky_dome_round_it_all_at_once()
    {
        var corridor = new RoadBackdrops.Corridor(Enumerable.Range(0, 101).Select(i => new Services.Race.Traffic.AiLine.Point(0, 0, i * 10, 0, 0, 0, 1, 0)));

        Assert.True(corridor.Near(new Vec3(0, 0, 500), 1));
        Assert.True(corridor.Near(new Vec3(150, 0, 500), 160));
        Assert.False(corridor.Near(new Vec3(150, 0, 500), 140));
        Assert.False(corridor.Near(new Vec3(5000, 0, 500), 4990));
        Assert.True(corridor.Near(new Vec3(5000, 0, 500), 5010));
        // Spheres hundreds or thousands of km across: answered without a walk over every cell they cover
        Assert.True(corridor.Near(new Vec3(0, 0, 0), 800_000));
        Assert.True(corridor.Near(new Vec3(2_000_000, 0, 0), 9_000_000));
        Assert.False(corridor.Near(new Vec3(9_000_000, 0, 0), 8_999_000));
        Assert.False(new RoadBackdrops.Corridor([]).Near(new Vec3(0, 0, 0), 1000));
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
