using System.Numerics;

namespace StreetCorsa.Looks;

/// <summary>
/// Walks the pixels of a square picture a triangle covers: the ones whose middles it covers, and, with some
/// <c>slack</c>, those within that many pixels of its edges too. The one rasteriser for the shell's depth pass
/// (<see cref="BodyShell"/>, no slack: the nearest triangle at each pixel) and the painter's cover
/// (<see cref="WearPainter"/>, a margin round each triangle so the paint reaches under the edges of the mesh).
/// </summary>
internal static class TriangleRaster
{
    /// <summary>What is done at a covered pixel; a struct, so the JIT inlines it into the loop</summary>
    public interface IPixel
    {
        /// <param name="w0">The weights of the triangle's corners a, b and c at the pixel's middle (they sum to 1)</param>
        void At(int x, int y, float w0, float w1, float w2);
    }

    /// <summary>
    /// Calls <paramref name="pixel"/> for every pixel of a <paramref name="size"/>-square picture that the triangle
    /// a-b-c (in pixels) covers, within <paramref name="slack"/> pixels of its edges. A sliver (no area) covers nothing
    /// without slack, and the few pixels of its box with it: near enough.
    /// </summary>
    public static void ForEachPixel<TPixel>(Vector2 a, Vector2 b, Vector2 c, int size, int slack, ref TPixel pixel) where TPixel : struct, IPixel
    {
        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))) - slack);
        var maxX = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))) + slack);
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))) - slack);
        var maxY = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))) + slack);
        var area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (MathF.Abs(area) < 1e-9f)
        {
            if (slack <= 0) return;
            for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++) pixel.At(x, y, 1f, 0f, 0f);
            return;
        }

        var inverse = 1 / area;
        // A pixel within the slack of an edge: the edge functions scaled to pixels by the edge's length
        var ab = slack > 0 ? slack * MathF.Abs(inverse) * MathF.Max(1e-6f, Vector2.Distance(a, b)) : 0f;
        var bc = slack > 0 ? slack * MathF.Abs(inverse) * MathF.Max(1e-6f, Vector2.Distance(b, c)) : 0f;
        var ca = slack > 0 ? slack * MathF.Abs(inverse) * MathF.Max(1e-6f, Vector2.Distance(c, a)) : 0f;
        for (var y = minY; y <= maxY; y++)
        {
            var py = y + 0.5f;
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5f;
                var w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) * inverse;
                var w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) * inverse;
                var w2 = 1 - w0 - w1;
                if (w0 < -bc || w1 < -ca || w2 < -ab) continue;
                pixel.At(x, y, w0, w1, w2);
            }
        }
    }
}
