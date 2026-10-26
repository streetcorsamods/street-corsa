using System.Numerics;

namespace StreetCorsa.Helpers;

/// <summary>
/// A box given by its least and most corners (a model's bounds): its eight corners, and the box that holds it once it
/// is turned and moved. Plain BCL on purpose: the tools compile it by link.
/// </summary>
public static class Box3
{
    /// <summary>Corner <paramref name="index"/> (0..7) of the box: bit 0 picks x, bit 1 y, bit 2 z, the most for a 1</summary>
    public static Vector3 Corner(Vector3 min, Vector3 max, int index) =>
        new((index & 1) == 0 ? min.X : max.X, (index & 2) == 0 ? min.Y : max.Y, (index & 4) == 0 ? min.Z : max.Z);

    /// <summary>The eight corners of the box</summary>
    public static IEnumerable<Vector3> Corners(Vector3 min, Vector3 max)
    {
        for (var i = 0; i < 8; i++) yield return Corner(min, max, i);
    }

    /// <summary>The box, square to the axes, that holds the box <paramref name="min"/>..<paramref name="max"/> carried by <paramref name="transform"/></summary>
    public static (Vector3 Min, Vector3 Max) Bounds(Vector3 min, Vector3 max, Matrix4x4 transform)
    {
        var low = new Vector3(float.MaxValue);
        var high = new Vector3(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var p = Vector3.Transform(Corner(min, max, i), transform);
            low = Vector3.Min(low, p);
            high = Vector3.Max(high, p);
        }

        return (low, high);
    }
}
