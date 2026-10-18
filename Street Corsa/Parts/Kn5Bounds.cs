using System.Numerics;
using AcTools.Kn5File;

namespace StreetCorsa.Parts;

/// <summary>The box a set of KN5 vertices fills: the one fold for a part's model, a merged model and a box mesh</summary>
public static class Kn5Bounds
{
    /// <returns>Null for no vertices at all</returns>
    public static (Vector3 Min, Vector3 Max)? Of(IEnumerable<Kn5Node.Vertex> vertices) => Of(vertices, null);

    /// <summary>The box of the vertices carried by <paramref name="world"/> (none: as they are); null for no vertices at all</summary>
    public static (Vector3 Min, Vector3 Max)? Of(IEnumerable<Kn5Node.Vertex> vertices, Matrix4x4? world)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in vertices)
        {
            var p = new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
            if (world is { } matrix) p = Vector3.Transform(p, matrix);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return min.X <= max.X ? (min, max) : null;
    }

    /// <summary>The box round both; one of them alone when the other is none</summary>
    public static (Vector3 Min, Vector3 Max)? Union((Vector3 Min, Vector3 Max)? one, (Vector3 Min, Vector3 Max)? other) =>
        one is var (aMin, aMax) ? other is var (bMin, bMax) ? (Vector3.Min(aMin, bMin), Vector3.Max(aMax, bMax)) : one : other;
}
