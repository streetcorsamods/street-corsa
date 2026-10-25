using StreetCorsa.Controls;
using StreetCorsa.Mods;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>A gas station's attendant: his spot in the station's file, the actor he is, the wind round him</summary>
public class OwnStationTests
{
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
