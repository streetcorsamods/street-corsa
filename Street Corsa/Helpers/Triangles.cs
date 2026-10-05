using System.Numerics;

namespace StreetCorsa.Helpers;

/// <summary>A triangle seen from above: what the cars' surfaces in the scenes and the scene builders' ground both ask of one</summary>
public static class Triangles
{
    // How far outside a triangle a point may be and still count as in it: a point on the edge two triangles share is in
    // both, never in neither for a rounding
    private const float Slack = 1e-4f;

    /// <summary>The height of triangle abc over (x, z), null when (x, z) is outside it seen from above</summary>
    public static float? HeightAt(Vector3 a, Vector3 b, Vector3 c, float x, float z)
    {
        var d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
        if (MathF.Abs(d) < 1e-9f) return null;
        var u = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / d;
        var v = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / d;
        var w = 1 - u - v;
        return u < -Slack || v < -Slack || w < -Slack ? null : u * a.Y + v * b.Y + w * c.Y;
    }
}
