using System.Numerics;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Services.Garage;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>
/// The tests that move the engine stand (<see cref="EngineStandLayout.PlaceAt"/>), which every layout of the stand reads:
/// run on their own, after the others, so no other test sees the stand anywhere but where it stands by default
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StandSpotCollection
{
    public const string Name = "Stand spot";
}

/// <summary>The game's own garage (garages/workshop): its room, its lights, its dressing of props and where the stand stands</summary>
[Collection(StandSpotCollection.Name)]
public class OwnGarageTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose()
    {
        EngineStandLayout.PlaceAt(null);
        _temp.Dispose();
    }

    private string Showroom(string garage) => Path.Combine(_temp.Path, "showrooms", garage, garage + ".kn5");

    private static string Workshop => Path.Combine(RepoPaths.BaseContent, "garages", "workshop", "workshop.kn5");

    private void Base() => _temp.File($"{ModIds.BaseId}/{ModSet.ManifestFileName}", """{ "id": "streetcorsa" }""");

    [Fact]
    public void A_prop_stands_at_every_place_its_garage_lists()
    {
        Base();
        _temp.File($"{ModIds.BaseId}/props/old_tyre/old_tyre.kn5", "x");
        _temp.File($"{ModIds.BaseId}/props/old_tyre/prop.json", """
            { "name": "Old tyre", "places": {
                "barn": [ { "position": [1, 0, 2], "heading": 10, "tilt": 90 }, { "position": [1, 0.17, 2] } ],
                "garage": { "position": [5, 0, 5] } } }
            """);

        var mods = ModSet.Load(_temp.Path);
        var tyres = GarageProps.FindAll(mods, Showroom("barn"), []);

        Assert.Equal(2, tyres.Count);
        Assert.Equal(new Vector3(1f, 0.17f, 2f), tyres[1].Position);
        Assert.Equal(90f, tyres[0].Tilt);
        Assert.True(tyres[0].IsLaid);
        Assert.False(tyres[1].IsLaid);

        // The one id the game draws itself takes its first place only, and is left out of the dressing
        Assert.Single(GarageProps.FindAll(mods, Showroom("garage"), []));
        Assert.Empty(GarageProps.FindAll(mods, Showroom("garage"), ["old_tyre"]));
        Assert.Equal(new Vector3(1f, 0f, 2f), GarageProps.Find(mods, "old_tyre", Showroom("barn"))!.Position);
    }

    [Fact]
    public void A_short_or_odd_place_is_left_out_and_the_rest_stand()
    {
        Base();
        _temp.File($"{ModIds.BaseId}/props/can/can.kn5", "x");
        _temp.File($"{ModIds.BaseId}/props/can/prop.json", """
            { "places": { "barn": [ { "position": [1, 0] }, 7, { "position": [4, 0, 4] } ] } }
            """);

        var problems = new List<string>();
        var cans = GarageProps.FindAll(ModSet.Load(_temp.Path), Showroom("barn"), [], problems);

        Assert.Equal(new Vector3(4f, 0f, 4f), Assert.Single(cans).Position);
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void A_laid_prop_is_stood_back_on_its_base_over_its_spot()
    {
        // A tyre stood up: 0.6 high, 0.17 thick, its base's middle at the origin
        var min = new Vector3(-0.3f, 0f, -0.085f);
        var max = new Vector3(0.3f, 0.6f, 0.085f);
        var tyre = new PlacedProp("old_tyre", "Old tyre", "", new Vector3(2f, 0.5f, -1f), 30f, Tilt: 90f);

        var placement = tyre.PlacementOf(min, max);
        var low = new Vector3(float.MaxValue);
        var high = new Vector3(float.MinValue);
        for (var corner = 0; corner < 8; corner++)
        {
            var p = Vector3.Transform(new Vector3((corner & 1) == 0 ? min.X : max.X, (corner & 2) == 0 ? min.Y : max.Y, (corner & 4) == 0 ? min.Z : max.Z), placement);
            low = Vector3.Min(low, p);
            high = Vector3.Max(high, p);
        }

        // Lying flat: as thick as it was deep, its bottom on the place's height, its middle over the spot
        Assert.Equal(0.17f, high.Y - low.Y, 3);
        Assert.Equal(0.5f, low.Y, 3);
        Assert.Equal(2f, (low.X + high.X) / 2f, 3);
        Assert.Equal(-1f, (low.Z + high.Z) / 2f, 3);

        // Not laid, the placement is the plain one
        var upright = tyre with { Tilt = 0f };
        Assert.Equal(upright.Placement, upright.PlacementOf(min, max));
    }

    [Fact]
    public void The_engine_stand_stands_where_the_garage_puts_it_else_beside_the_car()
    {
        EngineStandLayout.PlaceAt(new Vector3(3.6f, 0.1f, 0.6f));
        Assert.Equal(new Vector3(3.6f, 0.1f + EngineStandLayout.CrankHeight, 0.6f), EngineStandLayout.Spindle);

        EngineStandLayout.PlaceAt(new Vector3(float.NaN, 0f, 0f));
        Assert.Equal(EngineStandLayout.DefaultSpot, EngineStandLayout.Spot);

        EngineStandLayout.PlaceAt(null);
        Assert.Equal(EngineStandLayout.DefaultSpot, EngineStandLayout.Spot);
    }

    [Fact]
    public void A_garage_without_a_room_says_nothing_and_a_bad_room_is_told()
    {
        Base();
        Assert.False(GarageRooms.Load(ModSet.Load(_temp.Path), "barn").IsLit);

        _temp.File($"{ModIds.BaseId}/garages/barn/scene.json", """
            { "room": { "minX": 1, "maxX": 7, "minZ": -7, "maxZ": 5, "height": 3 },
              "lights": { "day": { "brightness": 0.7, "ambient": 1 }, "noon": { "brightness": 1 } },
              "lamps": [ { "x": 0, "y": 2.6, "z": 0, "brightness": 0.7, "range": 9 } ],
              "glow": { "day": { "gr_window": 1.1 }, "night": { "gr_window": 9 } } }
            """);

        var room = GarageRooms.Load(ModSet.Load(_temp.Path), "barn");

        // Its walls are not round the car's spot: the camera keeps its own limits
        Assert.Null(room.Walls);
        Assert.True(room.IsLit);
        var night = room.Dress(StreetLight.Night);
        Assert.Single(night.Lighting.Lamps);
        Assert.Empty(night.Glow);
        Assert.Equal(1.1f, room.Dress(StreetLight.Day).Glow["gr_window"]);
        Assert.Equal("GARAGE_SKY_NIGHT", night.Sky);
    }

    [Fact]
    public void The_base_games_garage_is_its_own_with_walls_lights_and_a_door_out_on_the_driveway()
    {
        Assert.Equal("workshop", Rules.GameFile.Load(ModSet.Load(RepoPaths.Content)).Garage);
        Assert.True(File.Exists(Workshop));

        var room = GarageRooms.Load(ModSet.Load(RepoPaths.Content), "workshop");
        var walls = Assert.NotNull(room.Walls);
        Assert.True(walls.MinX < -4f && walls.MaxX > 4f && walls.MinZ < -4f && walls.MaxZ > 4f);
        foreach (var light in Enum.GetValues<StreetLight>())
        {
            Assert.True(room.Lights.ContainsKey(light), $"no {light} light");
            Assert.NotEmpty(room.Dress(light).Lighting.Lamps);
        }

        // A car taken out of the lot comes in from outside the door, behind the back wall
        var door = Services.Scripting.GarageScene.Load(ModSet.Load(RepoPaths.Content), "workshop").Marks["door"];
        Assert.True(-door.Ahead > -walls.MinZ);
    }

    [Fact]
    public void Every_prop_of_the_base_game_has_its_model_a_credit_and_a_place_in_the_garage()
    {
        var mods = ModSet.Load(RepoPaths.Content);
        var problems = new List<string>();
        var props = GarageProps.FindAll(mods, Workshop, ["radio", WorkbenchLayout.PropId, LiftLayout.PropId, EngineStandLayout.PropId], problems);

        Assert.Empty(problems);
        var ids = Directory.GetDirectories(Path.Combine(RepoPaths.BaseContent, "props")).Select(Path.GetFileName)
            .Except([WorkbenchLayout.PropId, LiftLayout.PropId, EngineStandLayout.PropId]).ToList();
        Assert.Equal(ids.Order(), props.Select(p => p.Id).Distinct().Order());
        Assert.All(props, p =>
        {
            Assert.True(File.Exists(p.Model), p.Id);
            Assert.False(string.IsNullOrWhiteSpace(p.Credit), p.Id);
        });

        // The game's own props stand in it too
        foreach (var id in new[] { WorkbenchLayout.PropId, LiftLayout.PropId, EngineStandLayout.PropId })
            Assert.NotNull(GarageProps.Find(mods, id, Workshop, builtIn: true));
        Assert.NotNull(GarageProps.Find(mods, "radio", Workshop));
    }

    [Fact]
    public void Nothing_of_the_dressing_stands_where_the_car_or_the_stand_go()
    {
        var mods = ModSet.Load(RepoPaths.Content);
        var props = GarageProps.FindAll(mods, Workshop, ["radio", WorkbenchLayout.PropId, LiftLayout.PropId, EngineStandLayout.PropId]);
        var stand = GarageProps.Find(mods, EngineStandLayout.PropId, Workshop, builtIn: true)!.Position;

        foreach (var prop in props)
        {
            // The car's bay, a big car's length and width with room round it
            Assert.False(Math.Abs(prop.Position.X) < 1.6f && Math.Abs(prop.Position.Z) < 3.4f, $"{prop.Id} is in the car's bay");
            // The stand and an engine on it
            Assert.False(Vector2.Distance(new(prop.Position.X, prop.Position.Z), new(stand.X, stand.Z)) < 1.2f, $"{prop.Id} is at the stand");
        }
    }
}
