using StreetCorsa.Controls.Street;
using StreetCorsa.Mods;
using StreetCorsa.Services.Diner;
using StreetCorsa.Services.Scripting;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>
/// Step 28, idea 6, reworked in step 37: a diner on its street, the car driving to the curb and off again
/// </summary>
public class DinerStreetTests
{
    [Fact]
    public void What_a_diners_file_leaves_out_is_its_streets_the_model_the_lights_and_the_glow()
    {
        using var temp = new TempDir();
        DinerLevelsTests.Avenue(temp, diners: false);
        var mods = ModSet.Load(temp.Path);
        var diner = Assert.Single(DinerScenes.LoadAll(mods));
        Assert.True(diner.HasModel);
        Assert.Equal("STREET_SKY_NIGHT", StreetScene.SkyNode(StreetLight.Night));
        Assert.Equal("STREET_SKY_DAY", ((IDriveInScene)diner).SkyNodeAt(StreetLight.Day));

        var street = Assert.Single(StreetScenes.LoadAll(mods));
        Assert.Equal(street.Kn5For(StreetLight.Night), diner.Kn5);
        Assert.Equal(street.LightingFor(StreetLight.Night).Ambient, diner.LightingFor(StreetLight.Night).Ambient);
        Assert.Equal(street.LightingFor(StreetLight.Night).Lamps.Count, diner.LightingFor(StreetLight.Night).Lamps.Count);
        Assert.Equal(1.1f, diner.GlowAt(StreetLight.Night)["sign_roof"]);
        Assert.Empty(diner.GlowAt(StreetLight.Day));
        Assert.Equal(2, diner.Bays.Count);
    }

    [Fact]
    public void Its_ways_join_up_and_the_car_stands_at_the_end_of_the_way_in_facing_along_it()
    {
        using var temp = new TempDir();
        DinerLevelsTests.Avenue(temp, diners: false);
        var diner = Assert.Single(DinerScenes.LoadAll(ModSet.Load(temp.Path)));
        var marks = diner.ScriptMarks;

        // In, back out, then off: each way starts where the last one ended
        Assert.Equal(diner.Arrive[^1], diner.BackOut[0]);
        Assert.Equal(diner.BackOut[^1], diner.Leave[0]);
        Assert.Equal(diner.BackOut, marks.Paths["back_out"]);

        // At the curb where the way in ends, facing the way it last went (east, +x), the diner out of the window
        var curb = marks.Stand(diner.StandMark, "arrive")!.Value;
        Assert.Equal(diner.Arrive[^1].X, curb.X, 3);
        Assert.Equal(diner.Arrive[^1].Z, curb.Z, 3);
        Assert.Equal(MathF.PI / 2, curb.Heading, 1);
        Assert.Equal(80f, diner.LookWhileParked);
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
