using StreetCorsa.Controls.Street;
using StreetCorsa.Mods;
using StreetCorsa.Services.Diner;
using StreetCorsa.Services.Scripting;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>
/// Step 28, idea 6, reworked in step 37: the diner on its own street, on a corner, the car driving to the curb across
/// the street from it and off again
/// </summary>
public class DinerStreetTests
{
    private static DinerScene BaseDiner => Assert.Single(DinerScenes.LoadAll(ModSet.Load(RepoPaths.Content)), s => s.Id.EndsWith("mainstreet"));

    [Fact]
    public void The_base_games_diner_street_reads_with_its_model_its_ground_and_who_made_it()
    {
        var diner = BaseDiner;
        Assert.True(diner.HasModel);
        Assert.True(File.Exists(diner.GroundFile));
        Assert.Contains("Quaternius", diner.Credit);
        Assert.Contains("WaajaaH", diner.Credit);
        Assert.Equal("STREET_SKY_NIGHT", StreetScene.SkyNode(StreetLight.Night));
        Assert.Equal("STREET_SKY_DAY", ((IDriveInScene)diner).SkyNodeAt(StreetLight.Day));

        // What the diner's file leaves out is its street's: the one model, the lights and the glow
        var street = Assert.Single(StreetScenes.LoadAll(ModSet.Load(RepoPaths.Content)));
        Assert.Equal(street.Kn5For(StreetLight.Night), diner.Kn5);
        Assert.Equal(street.LightingFor(StreetLight.Night).Ambient, diner.LightingFor(StreetLight.Night).Ambient);
        Assert.Equal(street.LightingFor(StreetLight.Night).Lamps.Count, diner.LightingFor(StreetLight.Night).Lamps.Count);
        Assert.Equal(1.1f, diner.GlowAt(StreetLight.Night)["sign_roof"]);
        Assert.Empty(diner.GlowAt(StreetLight.Day));
        Assert.Equal(13, diner.Bays.Count);
    }

    [Fact]
    public void Its_ways_join_up_the_car_parks_across_the_street_from_the_diner_and_drives_off_down_the_street()
    {
        var diner = BaseDiner;
        var marks = diner.ScriptMarks;

        // In, back out, then off: each way starts where the last one ended
        Assert.Equal(diner.Arrive[^1], diner.BackOut[0]);
        Assert.Equal(diner.BackOut[^1], diner.Leave[0]);
        Assert.Equal(diner.BackOut, marks.Paths["back_out"]);

        // At the curb across Main Street (the far side from the diner, +z), facing east, the diner out of the driver's
        // window to the left; the backing up before pulling out stays in the parking lane
        var curb = marks.Stand(diner.StandMark, "arrive")!.Value;
        Assert.Equal(diner.Arrive[^1].X, curb.X, 3);
        Assert.InRange(curb.Z, 3f, 6f);
        Assert.Equal(MathF.PI / 2, curb.Heading, 2);
        Assert.InRange(diner.LookWhileParked, 60f, 100f);
        Assert.All(diner.BackOut, p => Assert.InRange(p.Z, 3f, 6f));

        // The racers' cars face the diner: along its front (towards -z), along its west side (towards +x)
        Assert.All(diner.Bays, b => Assert.True(b.Z < -9f && (MathF.Abs(b.Heading - MathF.PI) < 1e-3f || MathF.Abs(b.Heading - MathF.PI / 2) < 1e-3f)));
        Assert.Contains(diner.Bays, b => MathF.Abs(b.Heading - MathF.PI / 2) < 1e-3f);

        // The way off ends down the street eastwards, in the eastbound lane
        Assert.True(diner.Leave[^1].X > 50);
        Assert.InRange(diner.Leave[^1].Z, 0, 6);
    }

    [Fact]
    public void Every_way_runs_over_ground_the_street_has()
    {
        var diner = BaseDiner;
        var ground = StationGround.Load(diner.GroundFile);
        foreach (var point in diner.Arrive.Concat(diner.BackOut).Concat(diner.Leave))
            Assert.InRange(ground.HeightAt(point.X, point.Z), -0.05f, 0.25f);
    }

    [Fact]
    public void A_street_without_a_way_in_is_left_out_and_bad_bays_are_skipped()
    {
        using var temp = new TempDir();
        var folder = Path.Combine(temp.Path, "a");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "diner.json"), """{ "id": "a", "arrive": [[0, 0]], "leave": [[0, 0], [1, 1]] }""");
        Assert.Null(DinerScenes.Load(folder));

        // The diner's scripts back the car out of its bay: a diner with no way back out is left out
        File.WriteAllText(Path.Combine(folder, "diner.json"), """{ "id": "a", "arrive": [[0, 10], [0, 0]], "leave": [[0, 0], [5, 5]], "backOut": [[0, 0]] }""");
        Assert.Null(DinerScenes.Load(folder));

        File.WriteAllText(Path.Combine(folder, "diner.json"),
            """{ "id": "a", "arrive": [[0, 10], [0, 0]], "leave": [[0, 0], [5, 5]], "backOut": [[0, 0], [0, 3]], "bays": [null, { "x": 1, "z": 2, "heading": 90 }] }""");
        var diner = DinerScenes.Load(folder)!;
        Assert.False(diner.HasModel);
        Assert.Equal(diner.BackOut, diner.ScriptMarks.Paths["back_out"]);
        Assert.Equal(new DinerBay(1, 2, MathF.PI / 2), Assert.Single(diner.Bays));
    }

    [Fact]
    public void Back_from_a_race_the_car_stands_in_its_bay_from_the_start_and_can_drive_off()
    {
        var stage = new DriveInStage { IsShown = true };
        var events = new List<string>();
        stage.Arrived += () => events.Add("parked");
        stage.Gone += () => events.Add("gone");

        stage.StandThere(new Audio.EngineRunner());
        Assert.Equal(DriveInPhase.Standing, stage.Phase);
        Assert.False(stage.Towed);

        stage.IsShown = false;
        stage.Leave();
        Assert.Equal(DriveInPhase.Gone, stage.Phase);
        Assert.Equal(["parked", "gone"], events);
    }
}
