using AcTools.Kn5File;
using AcTools.KsAnimFile;
using StreetCorsa.Controls;
using StreetCorsa.Mods;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>The game's own gas station (roadmap step 35): its file, its attendant and the actor he is</summary>
public class OwnStationTests
{
    private static ModSet Mods => ModSet.Load(RepoPaths.Content);

    [Fact]
    public void The_base_games_station_is_ours_with_its_model_and_its_attendant_on_the_island()
    {
        var station = Assert.Single(StationScenes.LoadAll(Mods), s => s.Id == "crossroads");
        Assert.True(station.HasModel, station.Kn5);
        Assert.True(File.Exists(station.GroundFile));
        Assert.Contains("Wardster", station.Credit);

        var spot = Assert.IsType<AttendantSpot>(station.Attendant);
        Assert.Equal(StationScene.DefaultAttendant, spot.Prop);
        Assert.True(spot.Z > 1.3f && spot.Z < 2.7f, "on the island at z 2");
        // The ground's height file raises the island itself, so he stands on it with nothing added
        Assert.Equal(0f, spot.Y);
        Assert.Equal(0.15f, StationGround.Load(station.GroundFile).HeightAt(spot.X, spot.Z), 2);
        Assert.True(station.LookUpAtPump > 0f);

        // Self-lit at night: the tubes under the canopy and the pumps' globes
        var glow = ((IDriveInScene)station).GlowAt(StreetLight.Night);
        Assert.True(glow["st_tube"] > 1f);
        Assert.True(glow["GasDispenser_Emission"] > 0.5f);
        Assert.DoesNotContain(((IDriveInScene)station).GlowAt(StreetLight.Day), g => g.Value > 0.5f);
    }

    [Fact]
    public void The_attendant_prop_names_his_model_and_his_talk()
    {
        var problems = new List<string>();
        var actor = ActorProps.Find(Mods, StationScene.DefaultAttendant, problems);
        Assert.Empty(problems);
        Assert.NotNull(actor);
        Assert.EndsWith("attendant.kn5", actor.Model);
        Assert.EndsWith("talk.ksanim", actor.Animation);
        Assert.Equal(24.8f, actor.Seconds, 2);
        Assert.Contains("LasquetiSpice", actor.Credit);
    }

    [Fact]
    public void The_attendants_skin_is_on_bones_the_model_has_and_his_talk_moves_them()
    {
        var actor = ActorProps.Find(Mods, StationScene.DefaultAttendant)!;
        var kn5 = Kn5.FromFile(actor.Model);
        var dummies = new HashSet<string>(StringComparer.Ordinal);
        var skins = new List<Kn5Node>();
        void Walk(Kn5Node node)
        {
            if (node.NodeClass == Kn5NodeClass.Base)
            {
                dummies.Add(node.Name);
                foreach (var child in node.Children) Walk(child);
            }
            else if (node.NodeClass == Kn5NodeClass.SkinnedMesh) skins.Add(node);
        }
        Walk(kn5.RootNode);

        var skin = Assert.Single(skins);
        Assert.InRange(skin.Bones.Length, 1, 64);
        Assert.All(skin.Bones, bone => Assert.Contains(bone.Name, dummies));
        Assert.Equal(skin.Vertices.Length, skin.VerticeWeights.Length);
        Assert.All(skin.VerticeWeights, w => Assert.InRange(w.Weights.X + w.Weights.Y + w.Weights.Z + w.Weights.W, 0.99f, 1.01f));
        Assert.All(skin.VerticeWeights, w => Assert.True(w.Indices.X >= 0 && w.Indices.X < skin.Bones.Length));
        Assert.Equal("ksSkinnedMesh", kn5.Materials.Values.ElementAt((int)skin.MaterialId).ShaderName);

        var talk = KsAnim.FromFile(actor.Animation!);
        Assert.True(talk.Entries.Count > 20);
        Assert.All(talk.Entries.Keys, bone => Assert.Contains(bone, dummies));
        Assert.All(talk.Entries.Values, entry => Assert.True(entry.Size > 100, "a talk of many frames"));
    }

    [Fact]
    public void The_land_round_the_station_rolls_beyond_the_forecourt_and_the_road_and_stays_flat_where_the_car_goes()
    {
        var station = Assert.Single(StationScenes.LoadAll(Mods), s => s.Id == "crossroads");
        var ground = StationGround.Load(station.GroundFile);

        // The car's ways in and out, and the road the whole way, are level
        foreach (var (x, z) in station.Arrive.Concat(station.Leave).Concat(new[] { -140f, -60f, 0f, 60f, 140f }.Select(x => new GroundPoint(x, 32.1f))).Append(new GroundPoint(10f, 16f)))
            Assert.True(MathF.Abs(ground.HeightAt(x, z)) < 0.01f, $"({x}, {z}) is at {ground.HeightAt(x, z)}");

        // The hills beyond: higher than the forecourt, but never a cliff
        var hills = new[] { (-100f, -100f), (100f, -110f), (-120f, 120f), (120f, 120f), (0f, -140f) }.Select(p => ground.HeightAt(p.Item1, p.Item2)).ToList();
        Assert.True(hills.Max() > 2f, string.Join(", ", hills));
        Assert.All(hills, h => Assert.InRange(h, -3f, 30f));
    }

    [Fact]
    public void The_stations_grass_and_trees_sway_under_nodes_of_their_own_which_cast_no_shadow_of_their_square()
    {
        var station = Assert.Single(StationScenes.LoadAll(Mods), s => s.Id == "crossroads");
        var kn5 = Kn5.FromFile(station.Kn5);
        var sway = kn5.RootNode.Children.Where(n => n.NodeClass == Kn5NodeClass.Base && n.Name.StartsWith(GarageRenderer.SwayPrefix, StringComparison.Ordinal)).ToList();
        Assert.True(sway.Count > 100, $"{sway.Count} swaying nodes");
        var cutOuts = 0;
        foreach (var node in sway)
        {
            var meshes = node.Children.Where(c => c.NodeClass == Kn5NodeClass.Mesh).ToList();
            Assert.NotEmpty(meshes);
            // Pivoted at the foot of what it holds, so a lean keeps its base in the ground
            Assert.InRange(meshes.Min(m => m.Vertices.Min(v => v.Position.Y)), -0.01f, 0.01f);
            Assert.True(MathF.Abs(node.Transform.M41) <= 170f && MathF.Abs(node.Transform.M43) <= 170f);
            foreach (var mesh in meshes)
            {
                // A card cut out by its alpha casts no shadow: the shadow pass would draw its whole square. A real
                // bush's trunk is drawn whole and may cast
                var material = kn5.Materials.Values.ElementAt((int)mesh.MaterialId);
                if (!material.AlphaTested) continue;
                cutOuts++;
                Assert.Equal("ksPerPixelAT", material.ShaderName);
                Assert.False(mesh.CastShadows);
            }
        }
        Assert.True(cutOuts > 100, $"{cutOuts} cut-out meshes sway");

        // The shade under a tree is blended over the land, and the land itself is drawn whole
        var shade = kn5.Materials.Values.Single(m => m.Name == "st_shade");
        Assert.Equal(Kn5MaterialBlendMode.AlphaBlend, shade.BlendMode);
        Assert.False(kn5.Materials.Values.Single(m => m.Name == "st_grass").AlphaTested);
    }

    [Fact]
    public void The_wind_leans_what_stands_in_it_a_little_and_never_the_same_everywhere()
    {
        for (var t = 0f; t < 60f; t += 0.37f)
        {
            foreach (var (x, z) in new[] { (0f, 0f), (-150f, 30f), (80f, -120f) })
            {
                var (leanX, leanZ) = WindSway.LeanAt(t, x, z);
                Assert.InRange(MathF.Sqrt(leanX * leanX + leanZ * leanZ), 0f, WindSway.Most);
                Assert.True(leanX > 0f, "with the wind, along the road");
            }
        }

        var here = WindSway.LeanAt(5f, 0f, 0f);
        var there = WindSway.LeanAt(5f, 60f, 0f);
        Assert.NotEqual(here, there);
        Assert.NotEqual(here, WindSway.LeanAt(6f, 0f, 0f));
    }

    [Fact]
    public void A_stations_attendant_is_left_out_when_his_spot_is_nowhere_and_defaults_otherwise()
    {
        using var temp = new TempDir();
        var folder = Path.Combine(temp.Path, "a");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "station.json"),
            """{ "id": "a", "arrive": [[0, 10], [0, 0]], "leave": [[0, 0], [1, 1]], "attendant": { "z": 2 } }""");
        Assert.Null(StationScenes.Load(folder)!.Attendant);

        File.WriteAllText(Path.Combine(folder, "station.json"),
            """{ "id": "a", "arrive": [[0, 10], [0, 0]], "leave": [[0, 0], [1, 1]], "attendant": { "x": 1, "z": 2, "prop": "../elsewhere" }, "lookUpAtPump": 90 }""");
        var station = StationScenes.Load(folder)!;
        var spot = Assert.IsType<AttendantSpot>(station.Attendant);
        Assert.Equal(StationScene.DefaultAttendant, spot.Prop);
        Assert.Equal(0f, spot.Y);
        Assert.Equal(0f, spot.Heading);
        Assert.Equal(20f, station.LookUpAtPump);
    }

    [Fact]
    public void An_actor_prop_with_a_bad_animation_stands_still_and_one_outside_its_folder_is_left_out()
    {
        using var temp = new TempDir();
        var mod = Path.Combine(temp.Path, "mymod");
        var prop = Path.Combine(mod, "props", "man");
        Directory.CreateDirectory(prop);
        File.WriteAllText(Path.Combine(mod, "mod.json"), """{ "id": "mymod", "name": "My mod" }""");
        File.WriteAllBytes(Path.Combine(prop, "man.kn5"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(prop, "prop.json"), """{ "name": "Man", "animation": "walk.ksanim", "seconds": 3 }""");
        var mods = ModSet.Load(temp.Path);

        var problems = new List<string>();
        var actor = ActorProps.Find(mods, "man", problems);
        Assert.NotNull(actor);
        Assert.Null(actor.Animation);
        Assert.Contains(problems, p => p.Contains("walk.ksanim"));

        File.WriteAllBytes(Path.Combine(prop, "walk.ksanim"), [1]);
        File.WriteAllText(Path.Combine(prop, "prop.json"), """{ "name": "Man", "animation": "walk.ksanim", "seconds": 0 }""");
        problems.Clear();
        Assert.Null(ActorProps.Find(mods, "man", problems)!.Animation);
        Assert.Contains(problems, p => p.Contains("seconds"));

        File.WriteAllText(Path.Combine(prop, "prop.json"), """{ "name": "Man", "animation": "walk.ksanim", "seconds": 3 }""");
        Assert.Equal(3f, ActorProps.Find(mods, "man")!.Seconds);

        File.WriteAllText(Path.Combine(prop, "prop.json"), """{ "name": "Man", "model": "../../man.kn5" }""");
        problems.Clear();
        Assert.Null(ActorProps.Find(mods, "man", problems));
        Assert.Contains(problems, p => p.Contains("outside"));
        Assert.Null(ActorProps.Find(mods, "nobody"));
    }
}
