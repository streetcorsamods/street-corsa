using System.Buffers;
using System.Numerics;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Looks;

/// <summary>
/// Paints the wear layer's pictures for a <see cref="CarLook"/>: what each pixel shows is worked out from the point of the
/// car it stands for (<see cref="BodyShell"/>'s views), so rust grows low on the body and round the wheel arches, dirt
/// is thickest low down and behind the wheels, a hit leaves its scrapes and creases at its end of the car, the sun fades
/// the top. Nothing of the car's own texture is used: the layer is drawn over the paint, see-through where there is no
/// wear. The windscreen gets its cracks in a picture of its own.
/// </summary>
internal static class WearPainter
{
    /// <summary>The windscreen's picture, pixels</summary>
    public const int ScreenWidth = 1024;

    public const int ScreenHeight = 512;

    /// <summary>A colour with its cover, straight (not premultiplied), 0..1</summary>
    private struct Rgba(float r, float g, float b, float a)
    {
        public float R = r, G = g, B = b, A = a;

        /// <summary><paramref name="top"/> laid over this</summary>
        public void Over(float r, float g, float b, float a)
        {
            if (a <= 0) return;
            var outA = a + A * (1 - a);
            if (outA <= 1e-6f) return;
            R = (r * a + R * A * (1 - a)) / outA;
            G = (g * a + G * A * (1 - a)) / outA;
            B = (b * a + B * A * (1 - a)) / outA;
            A = outA;
        }
    }

    /// <summary>
    /// The body's picture, RGBA rows from the top, <paramref name="size"/> pixels a side (the shell's views scaled to it:
    /// the layer's corners point into the picture by share, whatever its size); the shell's own size when 0
    /// </summary>
    public static byte[] Body(BodyShell shell, CarLook look, int size = 0)
    {
        if (size <= 0) size = shell.PictureSize;
        var scale = size / (float)shell.PictureSize;
        var panels = shell.Panels.Select(p => p with
        {
            X = (int)(p.X * scale), Y = (int)(p.Y * scale), Width = Math.Max(1, (int)(p.Width * scale)), Height = Math.Max(1, (int)(p.Height * scale))
        }).ToList();
        var pixels = new byte[size * size * 4];
        var noise = new Noise(look.Seed);
        var detail = new Noise(look.Seed ^ 0x5bd1e995);
        // The lines' cover, a byte a pixel, lent: a picture's worth of arrays made and dropped for every car would keep the
        // garbage collector busy while a scene moves
        var lines = ArrayPool<byte>.Shared.Rent(size * size);
        try
        {
            Lines(lines, shell, panels, look, size);
            foreach (var panel in panels) PaintPanel(shell, panel, look, noise, detail, lines, pixels, size);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lines);
        }

        Bleed(pixels, size, size);
        return pixels;
    }

    private static void PaintPanel(BodyShell shell, BodyShell.Panel panel, CarLook look, Noise noise, Noise detail, byte[] lines, byte[] pixels, int size)
    {
            Parallel.For(0, panel.Height, WearLayer.Work, row =>
            {
                for (var column = 0; column < panel.Width; column++)
                {
                    var u = (column + 0.5f) / panel.Width;
                    var v = (row + 0.5f) / panel.Height;
                    var p = PointOf(shell, panel, u, v);
                    var x = panel.X + column;
                    var y = panel.Y + row;
                    var colour = Pixel(shell, panel.Side, p, look, noise, detail, lines[y * size + x] / 255f);
                    if (colour.A <= 0.003f) continue;
                    var at = (y * size + x) * 4;
                    pixels[at] = ToByte(colour.R);
                    pixels[at + 1] = ToByte(colour.G);
                    pixels[at + 2] = ToByte(colour.B);
                    pixels[at + 3] = ToByte(colour.A);
                }
            });
    }

    /// <summary>The point of the car a view's pixel stands for: on the face of the car's box that view looks at</summary>
    private static Vector3 PointOf(BodyShell shell, BodyShell.Panel panel, float u, float v)
    {
        var a = panel.MinA + (panel.MaxA - panel.MinA) * u;
        var top = panel.Side is BodyShell.Side.Top or BodyShell.Side.Bottom;
        var b = top ? panel.MinB + (panel.MaxB - panel.MinB) * v : panel.MaxB - (panel.MaxB - panel.MinB) * v;
        var min = shell.Min;
        var max = shell.Max;
        return panel.Side switch
        {
            BodyShell.Side.Left => new Vector3(shell.LeftSign > 0 ? max.X : min.X, b, a),
            BodyShell.Side.Right => new Vector3(shell.LeftSign > 0 ? min.X : max.X, b, a),
            BodyShell.Side.Front => new Vector3(a, b, shell.FrontSign > 0 ? max.Z : min.Z),
            BodyShell.Side.Back => new Vector3(a, b, shell.FrontSign > 0 ? min.Z : max.Z),
            BodyShell.Side.Top => new Vector3(b, max.Y, a),
            _ => new Vector3(b, min.Y, a)
        };
    }

    private static Rgba Pixel(BodyShell shell, BodyShell.Side side, Vector3 p, CarLook look, Noise noise, Noise detail, float line)
    {
        var size = shell.Size;
        var h = Math.Clamp((p.Y - shell.Min.Y) / Math.Max(0.1f, size.Y), 0, 1);
        var along = Math.Clamp((p.Z - shell.Min.Z) / Math.Max(0.1f, size.Z), 0, 1);
        var nose = shell.FrontSign > 0 ? along : 1 - along;
        var leftness = Math.Clamp((p.X - shell.Min.X) / Math.Max(0.1f, size.X), 0, 1);
        if (shell.LeftSign < 0) leftness = 1 - leftness;
        var low = SmoothStep(0.5f, 0.06f, h);
        var colour = new Rgba(0, 0, 0, 0);

        // The hits: a crumpled surface lit from above-front, its creases dark on one side and catching the light on the other
        var hit = Hit(look, side, nose, leftness);
        if (hit > 0.01f)
        {
            const float step = 0.004f;
            var (du, dv) = Across(side);
            var here = Crumple(noise, p) * hit;
            var dx = (Crumple(noise, p + du * step) * hit - here) / step;
            var dy = (Crumple(noise, p + dv * step) * hit - here) / step;
            var shade = Math.Clamp((dy * 0.8f - dx * 0.3f) * 0.035f, -1f, 1f);
            if (shade < 0) colour.Over(0.05f, 0.05f, 0.06f, -shade * 0.55f);
            else colour.Over(1f, 1f, 1f, shade * 0.3f);
        }

        // Worn paint: a top faded chalky by the sun, chips down to the primer or the metal
        var paint = (float)look.Paint;
        if (paint > 0.005f)
        {
            var sun = side == BodyShell.Side.Top ? 1f : side is BodyShell.Side.Left or BodyShell.Side.Right ? SmoothStep(0.65f, 0.95f, h) * 0.6f : 0.2f;
            if (sun > 0) colour.Over(0.86f, 0.85f, 0.82f, paint * sun * 0.4f * noise.Fbm(p * 2.7f + new Vector3(7, 1, 3), 3));

            var chips = paint * (0.35f + 0.5f * low + 0.6f * SmoothStep(0.8f, 1f, nose)) * 0.07f + hit * 0.06f;
            var c = detail.Fbm(p * 95f, 2);
            if (c > 1 - chips) colour.Over(c > 1 - chips * 0.4f ? 0.74f : 0.58f, c > 1 - chips * 0.4f ? 0.74f : 0.58f, c > 1 - chips * 0.4f ? 0.76f : 0.55f, 1f);
        }

        // Scratches and scrapes (drawn as lines beforehand)
        if (line > 0) colour.Over(0.80f, 0.80f, 0.80f, line);

        // Rust: where the body holds water first, then everywhere
        var rust = (float)look.Rust;
        if (rust > 0.005f)
        {
            var bias = side switch
            {
                BodyShell.Side.Top => 0f,
                BodyShell.Side.Bottom => 0.9f,
                _ => Math.Max(low, Arch(shell, side, p, detail))
            };
            var m = 0.62f * noise.Fbm(p * 2.4f) + 0.38f * detail.Fbm(p * 10.5f) + bias * 0.42f;
            var threshold = 1.08f - 0.78f * rust;
            var over = m - threshold;
            if (over > -0.05f)
            {
                var k = detail.Fbm(p * 38f, 3);
                if (over < 0)
                {
                    // A brown stain round the patch, running down
                    colour.Over(0.42f, 0.22f, 0.10f, 0.4f * (1 + over / 0.05f));
                }
                else if (over < 0.025f)
                {
                    // Paint lifting at the patch's edge
                    colour.Over(0.62f, 0.44f, 0.30f, 0.92f);
                }
                else
                {
                    var deep = SmoothStep(0.08f, 0.22f, over);
                    var r = Lerp(Lerp(0.26f, 0.60f, k), 0.14f, deep * 0.7f);
                    var g = Lerp(Lerp(0.12f, 0.29f, k), 0.07f, deep * 0.7f);
                    var b = Lerp(Lerp(0.06f, 0.11f, k), 0.04f, deep * 0.7f);
                    colour.Over(r, g, b, 1f);
                }
            }
        }

        // Dirt last, over everything: thickest low down and behind the wheels, a film of dust on top
        var dirt = (float)look.Dirt;
        if (dirt > 0.005f)
        {
            var amount = side switch
            {
                BodyShell.Side.Top => 0.22f,
                BodyShell.Side.Bottom => 1f,
                _ => 0.16f + 0.84f * MathF.Pow(low, 1.4f)
            };
            amount += Splash(shell, side, p) * 0.5f;
            var cover = Math.Min(0.9f, dirt * amount * (0.5f + 0.5f * noise.Fbm(p * 4.6f + new Vector3(3, 9, 1), 3)) * 1.25f);
            var mud = detail.Fbm(p * 13f, 2);
            colour.Over(Lerp(0.30f, 0.46f, mud), Lerp(0.26f, 0.40f, mud), Lerp(0.20f, 0.31f, mud), cover);
        }

        return colour;
    }

    /// <summary>How hard the car was hit where a point of it is, 0..1, from the damage of the zones it lies in</summary>
    private static float Hit(CarLook look, BodyShell.Side side, float nose, float leftness)
    {
        static float Of(double kmh) => (float)Math.Clamp(kmh / 70, 0, 1);
        var front = Of(look.Front) * (side == BodyShell.Side.Front ? 1 : side == BodyShell.Side.Back ? 0 : SmoothStep(0.78f, 0.97f, nose));
        var rear = Of(look.Rear) * (side == BodyShell.Side.Back ? 1 : side == BodyShell.Side.Front ? 0 : SmoothStep(0.22f, 0.03f, nose));
        var left = Of(look.Left) * (side == BodyShell.Side.Left ? 1 : side == BodyShell.Side.Right ? 0 : SmoothStep(0.75f, 0.97f, leftness));
        var right = Of(look.Right) * (side == BodyShell.Side.Right ? 1 : side == BodyShell.Side.Left ? 0 : SmoothStep(0.25f, 0.03f, leftness));
        return Math.Max(Math.Max(front, rear), Math.Max(left, right));
    }

    /// <summary>The crumpled surface of a hit, as a height</summary>
    private static float Crumple(Noise noise, Vector3 p)
    {
        // A few sharp creases where the panel folded, over a soft wave or two where it was pushed in
        var ridge = 1 - MathF.Abs(noise.At(p * 3.2f + new Vector3(5, 2, 8)) * 2f);
        ridge *= ridge;
        ridge *= ridge;
        return ridge * 0.5f + noise.At(p * 1.6f) * 0.5f;
    }

    /// <summary>A view's two directions in the model, across it and up it</summary>
    private static (Vector3 U, Vector3 V) Across(BodyShell.Side side) => side switch
    {
        BodyShell.Side.Left or BodyShell.Side.Right => (Vector3.UnitZ, Vector3.UnitY),
        BodyShell.Side.Front or BodyShell.Side.Back => (Vector3.UnitX, Vector3.UnitY),
        _ => (Vector3.UnitZ, Vector3.UnitX)
    };

    /// <summary>How near a point of a side is to the lip of a wheel arch on that side, 0..1</summary>
    private static float Arch(BodyShell shell, BodyShell.Side side, Vector3 p, Noise noise)
    {
        if (side is not (BodyShell.Side.Left or BodyShell.Side.Right) || shell.Hubs.Count < 4) return 0;
        var best = 0f;
        foreach (var hub in shell.Hubs)
        {
            var onLeft = (hub.X - (shell.Min.X + shell.Max.X) / 2) * shell.LeftSign > 0;
            if (onLeft != (side == BodyShell.Side.Left)) continue;
            var radius = Math.Max(0.2f, hub.Y - shell.Min.Y) + 0.06f;
            var d = MathF.Sqrt((p.Z - hub.Z) * (p.Z - hub.Z) + (p.Y - hub.Y) * (p.Y - hub.Y));
            // The lip's band wanders, wider in places, broken in others
            var wander = noise.At(p * 4f) * 0.06f;
            var band = 0.05f + 0.06f * Math.Max(0, noise.At(p * 2.3f + new Vector3(4, 4, 4)) + 0.3f);
            best = Math.Max(best, 1 - SmoothStep(0f, band, MathF.Abs(d - radius + wander)));
        }
        return best;
    }

    /// <summary>Mud thrown up behind a wheel onto the side, 0..1</summary>
    private static float Splash(BodyShell shell, BodyShell.Side side, Vector3 p)
    {
        if (side is not (BodyShell.Side.Left or BodyShell.Side.Right) || shell.Hubs.Count < 4) return 0;
        var best = 0f;
        foreach (var hub in shell.Hubs)
        {
            var onLeft = (hub.X - (shell.Min.X + shell.Max.X) / 2) * shell.LeftSign > 0;
            if (onLeft != (side == BodyShell.Side.Left)) continue;
            var behind = (hub.Z - p.Z) * shell.FrontSign;
            if (behind < 0) continue;
            var radius = Math.Max(0.2f, hub.Y - shell.Min.Y);
            best = Math.Max(best, SmoothStep(0.9f, 0.3f, behind) * SmoothStep(hub.Y + radius * 0.6f, hub.Y - radius * 0.4f, p.Y));
        }
        return best;
    }

    /// <summary>
    /// The scratches and scrapes, drawn as lines into a cover of their own: a few for worn paint along the sides at hip
    /// height (keys, bags, brushes), many more along the zones that were hit
    /// </summary>
    private static void Lines(byte[] cover, BodyShell shell, IReadOnlyList<BodyShell.Panel> panels, CarLook look, int size)
    {
        Array.Clear(cover, 0, size * size);
        var random = new Random(look.Seed * 31 + 7);
        foreach (var panel in panels)
        {
            var sides = panel.Side is BodyShell.Side.Left or BodyShell.Side.Right;
            var area = panel.Width * (float)panel.Height / (size * (float)size);
            var worn = (int)(look.Paint * (sides ? 22 : 6) * area / 0.08f);
            for (var i = 0; i < worn; i++)
            {
                var v = sides ? 0.3f + (float)random.NextDouble() * 0.45f : (float)random.NextDouble();
                Scratch(cover, size, panel, random, (float)random.NextDouble(), v, 0.015f + (float)random.NextDouble() * 0.07f, 0.35f, 0.3f);
            }

            // Scrapes where the car was hit: long, near level, many side by side
            foreach (var (kmh, where) in Zones(look, panel.Side, shell.FrontSign))
            {
                var hit = (float)Math.Clamp(kmh / 70, 0, 1);
                var count = (int)(hit * 60 * area / 0.08f);
                for (var i = 0; i < count; i++)
                {
                    var (u, v) = where(random);
                    Scratch(cover, size, panel, random, u, v, 0.03f + (float)random.NextDouble() * 0.12f, 0.2f, 0.7f);
                }
            }
        }
    }

    /// <summary>The hit zones that lie on a view, each with where on it (u, v) a scrape goes</summary>
    private static IEnumerable<(double Kmh, Func<Random, (float U, float V)> Where)> Zones(CarLook look, BodyShell.Side side, float frontSign)
    {
        float R(Random r) => (float)r.NextDouble();
        switch (side)
        {
            case BodyShell.Side.Front:
                yield return (look.Front, r => (R(r), 0.3f + R(r) * 0.6f));
                break;
            case BodyShell.Side.Back:
                yield return (look.Rear, r => (R(r), 0.3f + R(r) * 0.6f));
                break;
            case BodyShell.Side.Left or BodyShell.Side.Right:
                yield return (side == BodyShell.Side.Left ? look.Left : look.Right, r => (0.1f + R(r) * 0.8f, 0.3f + R(r) * 0.5f));
                // The ends of a side, as the front or the rear took it (the nose is at u = 1 when the model faces +z)
                yield return (look.Front, r => (EndU(r, frontSign > 0), 0.35f + R(r) * 0.45f));
                yield return (look.Rear, r => (EndU(r, frontSign < 0), 0.35f + R(r) * 0.45f));
                break;
        }
    }

    /// <summary>Where across a side's view a scrape at one end goes: its last tenth at the far end (u = 1), else its first</summary>
    private static float EndU(Random r, bool farEnd) => farEnd ? 0.88f + (float)r.NextDouble() * 0.12f : (float)r.NextDouble() * 0.12f;

    /// <summary>A scratch from (u, v) of a view: a slightly wavering line, near level, <paramref name="lengthShare"/> of the view long</summary>
    private static void Scratch(byte[] cover, int size, BodyShell.Panel panel, Random random, float u, float v, float lengthShare, float slope, float strength)
    {
        var angle = ((float)random.NextDouble() * 2 - 1) * slope + (random.Next(2) == 0 ? 0 : MathF.PI);
        var length = lengthShare * panel.Width;
        var x = panel.X + u * panel.Width;
        var y = panel.Y + v * panel.Height;
        var steps = (int)(length * 2);
        var a = strength * (0.6f + 0.4f * (float)random.NextDouble());
        for (var s = 0; s < steps; s++)
        {
            angle += ((float)random.NextDouble() - 0.5f) * 0.05f;
            x += MathF.Cos(angle) * 0.5f;
            y += MathF.Sin(angle) * 0.5f;
            var px = (int)x;
            var py = (int)y;
            if (px < panel.X || py < panel.Y || px >= panel.X + panel.Width || py >= panel.Y + panel.Height) break;
            // Fading in and out at its ends
            var t = s / (float)steps;
            var fade = MathF.Min(1, MathF.Min(t, 1 - t) * 6);
            ref var c = ref cover[py * size + px];
            c = Math.Max(c, ToByte(a * fade));
        }
    }

    /// <summary>The windscreen's picture: cracks running out from where it was struck, rings round it, a frosted spot</summary>
    public static byte[] Screen(CarLook look)
    {
        var w = ScreenWidth;
        var hgt = ScreenHeight;
        var cover = new float[w * hgt];
        var random = new Random(look.Seed * 17 + 3);
        var cracks = (float)Math.Clamp(look.Cracks, 0, 1);
        var strikes = cracks > 0.55f ? 2 : 1;
        for (var s = 0; s < strikes; s++)
        {
            var cx = w * (0.2f + 0.6f * (float)random.NextDouble());
            var cy = hgt * (0.35f + 0.45f * (float)random.NextDouble());
            var rays = 5 + (int)(cracks * 11);
            for (var r = 0; r < rays; r++)
            {
                var angle = r * MathF.Tau / rays + ((float)random.NextDouble() - 0.5f) * 0.6f;
                Crack(cover, w, hgt, random, cx, cy, angle, w * (0.06f + cracks * 0.55f * (0.4f + 0.6f * (float)random.NextDouble())), 0.85f, 2);
            }
            var rings = 1 + (int)(cracks * 2.5f);
            for (var ring = 1; ring <= rings; ring++)
            {
                var radius = (8 + ring * 14) * (0.6f + cracks);
                var start = (float)random.NextDouble() * MathF.Tau;
                var span = MathF.Tau * (0.4f + 0.5f * (float)random.NextDouble());
                for (var t = 0f; t < span; t += 0.6f / radius)
                {
                    var wobble = radius * (1 + ((float)random.NextDouble() - 0.5f) * 0.08f);
                    Plot(cover, w, hgt, cx + MathF.Cos(start + t) * wobble, cy + MathF.Sin(start + t) * wobble, 0.7f);
                }
            }
            for (var y = -10; y <= 10; y++)
                for (var x = -10; x <= 10; x++)
                {
                    var d = MathF.Sqrt(x * x + y * y);
                    if (d < 10) Plot(cover, w, hgt, cx + x, cy + y, 0.55f * (1 - d / 10) * (0.5f + 0.5f * (float)random.NextDouble()));
                }
        }

        var pixels = new byte[w * hgt * 4];
        for (var i = 0; i < cover.Length; i++)
        {
            if (cover[i] <= 0) continue;
            pixels[i * 4] = 232;
            pixels[i * 4 + 1] = 238;
            pixels[i * 4 + 2] = 242;
            pixels[i * 4 + 3] = ToByte(cover[i]);
        }
        Bleed(pixels, w, hgt);
        return pixels;
    }

    /// <summary>One crack: a jagged line that now and then branches</summary>
    private static void Crack(float[] cover, int w, int h, Random random, float x, float y, float angle, float length, float strength, int depth)
    {
        for (var s = 0f; s < length; s += 1f)
        {
            if ((int)s % 6 == 0) angle += ((float)random.NextDouble() - 0.5f) * 0.5f;
            x += MathF.Cos(angle);
            y += MathF.Sin(angle);
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            var a = strength * (1 - s / length * 0.5f);
            Plot(cover, w, h, x, y, a);
            Plot(cover, w, h, x + 0.7f, y + 0.7f, a * 0.4f);
            if (depth > 0 && random.NextDouble() < 0.012)
                Crack(cover, w, h, random, x, y, angle + (random.Next(2) == 0 ? 0.7f : -0.7f), (length - s) * 0.45f, strength * 0.8f, depth - 1);
        }
    }

    private static void Plot(float[] cover, int w, int h, float x, float y, float a)
    {
        var px = (int)x;
        var py = (int)y;
        if (px < 0 || py < 0 || px >= w || py >= h) return;
        ref var c = ref cover[py * w + px];
        c = MathF.Max(c, a);
    }

    /// <summary>
    /// Gives the see-through pixels next to a drawn one its colour (not its cover): a picture shrunk for the distance
    /// (its mip maps) or filtered between pixels then fades a patch's colour out, not into black
    /// </summary>
    private static void Bleed(byte[] pixels, int w, int h)
    {
        // Which pixels are empty as each pass starts, a byte a pixel (lent): only those are written, from neighbours that
        // were not, so a pass reads nothing it has changed
        var empty = ArrayPool<byte>.Shared.Rent(w * h);
        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                Parallel.For(0, h, WearLayer.Work, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var at = (y * w + x) * 4;
                        empty[y * w + x] = pixels[at + 3] == 0 && (pixels[at] | pixels[at + 1] | pixels[at + 2]) == 0 ? (byte)1 : (byte)0;
                    }
                });
                Parallel.For(0, h, WearLayer.Work, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        if (empty[y * w + x] == 0) continue;
                        var at = (y * w + x) * 4;
                        for (var k = 0; k < 4; k++)
                        {
                            var nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0);
                            var ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h || empty[ny * w + nx] != 0) continue;
                            var n = (ny * w + nx) * 4;
                            pixels[at] = pixels[n];
                            pixels[at + 1] = pixels[n + 1];
                            pixels[at + 2] = pixels[n + 2];
                            break;
                        }
                    }
                });
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(empty);
        }
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
