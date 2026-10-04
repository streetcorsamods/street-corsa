using System.Numerics;

namespace StreetCorsa.Looks;

/// <summary>
/// Gradient noise in three dimensions (Perlin's improved noise) from a seed, and its sum over octaves: what rust, dirt and
/// worn paint are grown from. The same seed gives the same noise every time, so a car looks the same in every scene and
/// in AC.
/// </summary>
internal sealed class Noise
{
    private readonly int[] _perm = new int[512];

    public Noise(int seed)
    {
        var random = new Random(seed);
        var p = Enumerable.Range(0, 256).ToArray();
        for (var i = 255; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (var i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    /// <summary>The noise at a point, about -1 to 1</summary>
    public float At(float x, float y, float z)
    {
        var xi = (int)MathF.Floor(x);
        var yi = (int)MathF.Floor(y);
        var zi = (int)MathF.Floor(z);
        x -= xi;
        y -= yi;
        z -= zi;
        xi &= 255;
        yi &= 255;
        zi &= 255;
        var u = Fade(x);
        var v = Fade(y);
        var w = Fade(z);
        var p = _perm;
        var a = p[xi] + yi;
        var aa = p[a] + zi;
        var ab = p[a + 1] + zi;
        var b = p[xi + 1] + yi;
        var ba = p[b] + zi;
        var bb = p[b + 1] + zi;

        return Lerp(w,
            Lerp(v, Lerp(u, Grad(p[aa], x, y, z), Grad(p[ba], x - 1, y, z)),
                Lerp(u, Grad(p[ab], x, y - 1, z), Grad(p[bb], x - 1, y - 1, z))),
            Lerp(v, Lerp(u, Grad(p[aa + 1], x, y, z - 1), Grad(p[ba + 1], x - 1, y, z - 1)),
                Lerp(u, Grad(p[ab + 1], x, y - 1, z - 1), Grad(p[bb + 1], x - 1, y - 1, z - 1))));
    }

    public float At(Vector3 p) => At(p.X, p.Y, p.Z);

    /// <summary>
    /// The noise summed over <paramref name="octaves"/>, each twice as fine and half as strong as the one before, brought
    /// to about 0 to 1 (0.5 on average)
    /// </summary>
    public float Fbm(Vector3 p, int octaves = 4)
    {
        float sum = 0, amplitude = 0.5f, total = 0;
        for (var i = 0; i < octaves; i++)
        {
            sum += At(p) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            // Turned a little at every octave, so the octaves' grids do not line up
            p = new Vector3(p.X * 2.03f + 17.1f, p.Y * 2.03f - 9.7f, p.Z * 2.03f + 3.3f);
        }
        return Math.Clamp(0.5f + sum / total * 0.75f, 0f, 1f);
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static float Lerp(float t, float a, float b) => a + t * (b - a);

    private static float Grad(int hash, float x, float y, float z)
    {
        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : h is 12 or 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}
