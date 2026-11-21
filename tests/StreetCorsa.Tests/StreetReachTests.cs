using System.Numerics;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>Street Rod's town: a scene stands up the pieces near where the car stands and along its ways, not the whole town</summary>
public class StreetReachTests
{
    private static PlacedPiece At(float x, float z) => new("shop", x, 0.15f, z, 0f, 1f, 0);

    [Fact]
    public void Pieces_near_the_stand_or_a_way_stay_and_the_far_blocks_go()
    {
        var pieces = new[] { At(10, 0), At(StreetReach.AroundStand - 1, 0), At(StreetReach.AroundStand + 1, 0), At(400, 30), At(400, 200) };
        var way = new List<GroundPoint> { new(-500, 0), new(500, 0) };

        var standing = StreetReach.Around(pieces, Vector2.Zero);
        Assert.Equal([pieces[0], pieces[1]], standing);

        // 30 m off a way the car drives along is in sight; 200 m off is not
        var withWay = StreetReach.Around(pieces, Vector2.Zero, [way]);
        Assert.Equal([pieces[0], pieces[1], pieces[2], pieces[3]], withWay);
    }

    [Fact]
    public void A_points_distance_from_a_stretch_is_to_its_nearest_point_ends_included()
    {
        Assert.Equal(9f, StreetReach.DistanceSquared(new Vector2(5, 3), new Vector2(0, 0), new Vector2(10, 0)), 3);
        Assert.Equal(25f, StreetReach.DistanceSquared(new Vector2(-3, 4), new Vector2(0, 0), new Vector2(10, 0)), 3);
        Assert.Equal(25f, StreetReach.DistanceSquared(new Vector2(3, 4), new Vector2(0, 0), new Vector2(0, 0)), 3);
    }
}
