using System.Numerics;
using StreetCorsa.Parts;

namespace StreetCorsa.Services.PartShops;

/// <summary>
/// A part of the shop's stock to stand in the shop: what it is (its key, its group as the shelves list kinds), and the
/// box its model fills as it stands, in its own space
/// </summary>
public sealed record ShopShownItem(string Key, string Group, Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;
}

/// <summary>Where one part of the stock stands: its place, and the matrix that puts its model there</summary>
public sealed record ShopStanding(ShopShownItem Item, ShopPlace Place, Matrix4x4 World);

/// <summary>
/// Which part of the stock stands in which of a shop's places (<see cref="ShopScene.Places"/>), as a shopkeeper would lay
/// it out: a whole engine on the stand, tyres and rims in the rack, the big parts on the table and the bottom shelves,
/// the rest on the shelves; one part a place, the places in the scene's order (the best seen first), turned a quarter
/// when it fits that way only. A place that holds a shelf's group by its id ("crankshafts": the machine shop's crank
/// grinder) takes a part of that group before any other place does, and nothing else. A part too big for every place
/// left is not shown. Pure figures, for the tests.
/// </summary>
public static class ShopStockLayout
{
    // A part may be this much bigger than its place's room: the room is drawn tight round the shelves' boards
    private const float Slack = 1.05f;

    public static List<ShopStanding> Place(IReadOnlyList<ShopShownItem> items, IReadOnlyList<ShopPlace> places)
    {
        var free = places.Select((p, i) => (Place: p, Index: i)).ToList();
        var standing = new List<ShopStanding>();

        // Engines, then wheels, then the rest biggest first: the big ones get the big places before the small fill them
        var ordered = items
            .OrderBy(i => ClassOf(i) switch { ShopHolds.Engine => 0, ShopHolds.Wheel => 1, _ => 2 })
            .ThenByDescending(i => i.Size.X * i.Size.Y * i.Size.Z)
            .ToList();

        foreach (var item in ordered)
        {
            foreach (var holds in Takes(ClassOf(item)).Prepend(item.Group))
            {
                var found = free.FirstOrDefault(f => f.Place.Holds == holds && Turn(item.Size, f.Place) != null);
                if (found.Place == null) continue;

                free.Remove(found);
                standing.Add(new ShopStanding(item, found.Place, World(item, found.Place, Turn(item.Size, found.Place)!.Value)));
                break;
            }
        }

        // Nothing on any stand (a shop with no engines): the biggest part shown elsewhere goes on one, so they are never all bare
        if (standing.All(s => s.Place.Holds != ShopHolds.Engine) && free.FirstOrDefault(f => f.Place.Holds == ShopHolds.Engine) is { Place: { } stand } standFree)
        {
            var biggest = standing
                .Where(s => s.Place.Holds is ShopHolds.Large or ShopHolds.Shelf && Turn(s.Item.Size, stand) != null)
                .OrderByDescending(s => s.Item.Size.X * s.Item.Size.Y * s.Item.Size.Z)
                .FirstOrDefault();
            if (biggest != null)
            {
                standing.Remove(biggest);
                free.Remove(standFree);
                standing.Insert(0, new ShopStanding(biggest.Item, stand, World(biggest.Item, stand, Turn(biggest.Item.Size, stand)!.Value)));
            }
        }

        return standing;
    }

    /// <summary>What kind of place a part goes in first: an engine's, a wheel's, or a shelf's</summary>
    public static string ClassOf(ShopShownItem item) => item.Group switch
    {
        PartGroups.Blocks => ShopHolds.Engine,
        PartGroups.Tyres or PartGroups.Rims => ShopHolds.Wheel,
        _ => ShopHolds.Shelf
    };

    /// <summary>The places a part of a kind may stand in, the best first</summary>
    private static string[] Takes(string kind) => kind switch
    {
        ShopHolds.Engine => [ShopHolds.Engine, ShopHolds.Large],
        ShopHolds.Wheel => [ShopHolds.Wheel, ShopHolds.Large, ShopHolds.Shelf],
        _ => [ShopHolds.Shelf, ShopHolds.Large]
    };

    /// <summary>The quarter turns (0 or 1) the part fits its place with, square first; null when it fits neither way</summary>
    public static int? Turn(Vector3 size, ShopPlace place)
    {
        if (size.Y > place.Height * Slack) return null;
        if (size.X <= place.Width * Slack && size.Z <= place.Depth * Slack) return 0;
        if (size.Z <= place.Width * Slack && size.X <= place.Depth * Slack) return 1;
        return null;
    }

    /// <summary>The part's model stood in the place: the middle of its box over the place's middle, its bottom on the place's floor, turned as the place faces</summary>
    public static Matrix4x4 World(ShopShownItem item, ShopPlace place, int turns)
    {
        var centre = (item.Min + item.Max) / 2f;
        var heading = (place.Heading + turns * 90f) * MathF.PI / 180f;
        return Matrix4x4.CreateTranslation(-centre.X, -item.Min.Y, -centre.Z)
               * Matrix4x4.CreateRotationY(heading)
               * Matrix4x4.CreateTranslation(place.X, place.Y, place.Z);
    }
}
