using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AcTools.Kn5File;
using TextureReader = AcTools.Render.Base.Utils.TextureReader;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Looks;

/// <summary>
/// A paint job in a colour of the player's own: the car's body textures, as its skin has them, with the paint's colour
/// changed and everything else on them (shading, panel lines, trim, badges) left as it was. The scenes put the tinted
/// textures over the car's own by their names (ShownPartsOnCar), as the race mode does in AC (car_parts.lua).
/// Nothing is written into the car's own files.
///
/// What the paint is comes from the wear's look at the car (<see cref="BodyShell.Paint"/>, its materials). Its colour is
/// what AC draws of them: the diffuse texture, and on a multi-map shader the detail texture multiplied in where the
/// diffuse is see-through (diffuse × lerp(detail, 1, diffuse's alpha)). The colour most of the paint shows is the
/// paint's; the texels close to it, in hue and not far off in lightness, are repainted, shaded as they were. A paint
/// whose colour comes through its detail (the AMC Javelin's, the Dodge Monaco's) has the two baked into its diffuse,
/// tinted there, and its detail made plain white, so the diffuse alone gives the colour. A car whose paint shows no one
/// colour over most of it (a two-tone skin, a livery) cannot be tinted: it is painted in its mod's colours only.
/// </summary>
public static class CarPaint
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Looks");

    /// <summary>The folder in the race mode the tinted textures go in</summary>
    public const string Folder = "paint";

    /// <summary>How much of the paint must show its one colour to be tinted</summary>
    public const double MinShare = 0.7;

    /// <summary>The texture slots that carry a paint's colour</summary>
    public const string Diffuse = "txDiffuse";

    public const string Detail = "txDetail";

    /// <summary>How big the textures are looked at to find the paint's colour, pixels a side at most</summary>
    private const int LookSize = 256;

    /// <summary>The biggest a baked diffuse is made, pixels a side</summary>
    private const int MostBaked = 2048;

    /// <summary>
    /// A detail repeated more often than this over the diffuse is a fine grain (metal flake): its average colour is what
    /// the paint shows, and what it is baked with
    /// </summary>
    private const float FineDetail = 2f;

    // How a texel is judged the paint's colour: its lightness against the paint's within these, its hue within Hue; the
    // fade out to untouched past them; lightnesses compared damped by Damp, so a black paint's grain is not all hue
    private const float Damp = 0.08f;
    private const float LightLow = 0.55f;
    private const float LightHigh = 1.6f;
    private const float LightLowest = 0.45f;
    private const float LightHighest = 1.8f;
    private const float Hue = 0.2f;
    private const float HueMost = 0.3f;

    /// <summary>A diffuse with fewer of its texels changed than this is left as it is</summary>
    private const double SomeChanged = 0.01;

    /// <summary>The way tints are made: in the file names, so one made another way is made again</summary>
    private const int Version = 2;

    /// <summary>How many paint jobs' textures are kept written (a car's few, for this many cars)</summary>
    private const int KeptFiles = 60;

    public static string Cache => Path.Combine(WearLayer.Cache, "paint");

    /// <summary>What each model's paint is, by its model, skin and file stamp: looked at once</summary>
    private static readonly ConcurrentDictionary<string, Lazy<PaintSurface?>> Surfaces = new(StringComparer.OrdinalIgnoreCase);

    // AcTools' reader of any texture it can draw (DDS of any kind, PNG, JPEG) as a PNG: a device of its own, one user at a time
    private static readonly object Reading = new();
    private static TextureReader? _reader;

    private static readonly object Pruning = new();

    #region What the paint is

    /// <summary>
    /// What the paint of the car in <paramref name="carDirectory"/> is in its skin <paramref name="skin"/> (null or empty:
    /// its model's own textures); null when the car has no model, or nothing of it is paint. Slow the first time for a car
    /// and skin: off the UI thread. Never throws for what the car holds.
    /// </summary>
    public static PaintSurface? SurfaceOf(string carDirectory, string? skin)
    {
        if (WearLayer.ModelOf(carDirectory) is not { } model) return null;
        string key;
        try
        {
            var info = new FileInfo(model);
            key = $"{Path.GetFullPath(model)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{SkinFolder(carDirectory, skin)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var lazy = Surfaces.GetOrAdd(key, _ => new Lazy<PaintSurface?>(() => Look(carDirectory, model, skin)));
        return lazy.Value;
    }

    /// <summary>Whether the car's paint in that skin can take a colour of the player's own (<see cref="SurfaceOf"/>)</summary>
    public static bool CanTint(string carDirectory, string? skin) => SurfaceOf(carDirectory, skin)?.CanTint == true;

    private static PaintSurface? Look(string carDirectory, string model, string? skin)
    {
        try
        {
            var started = Environment.TickCount64;
            if (WearLayer.ShellFor(carDirectory) is not { Paint.Count: > 0 } shell) return null;
            var kn5 = Kn5.FromFile(model, SkippingTextureLoader.Instance);
            var materials = kn5.Materials.Values.ToList();
            // The paint's materials, the one most seen first (the body; the chassis seen from below, when it counts, after)
            var paint = shell.Paint.Select(n => materials.FirstOrDefault(m => m.Name == n)).OfType<Kn5Material>()
                .Where(m => m.GetMappingByName(Diffuse)?.Texture != null).ToList();
            if (paint.Count == 0) return null;

            var names = paint.SelectMany(m => new[] { m.GetMappingByName(Diffuse)!.Texture, UsesDetail(m) ? m.GetMappingByName(Detail)?.Texture : null })
                .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var pictures = new Dictionary<string, Picture>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, bytes) in TextureBytes(model, carDirectory, skin, names))
                if (Decode(bytes, LookSize) is { } picture) pictures[name] = picture;

            // What each material shows, each bit by how much of the car it covers, its diffuse's shading taken out where
            // the colour comes through the detail
            var looked = new List<(PaintLayer Layer, List<Sample> Samples)>();
            foreach (var material in paint)
            {
                var index = (uint)materials.IndexOf(material);
                if (!pictures.TryGetValue(material.GetMappingByName(Diffuse)!.Texture, out var diffuse)) continue;
                var detailName = UsesDetail(material) ? material.GetMappingByName(Detail)?.Texture : null;
                var scale = DetailScale(material);
                var detail = detailName != null && pictures.TryGetValue(detailName, out var ep) ? Fine(ep, scale) : null;

                var raw = new List<RawSample>();
                foreach (var node in Meshes(kn5.RootNode).Where(n => n.MaterialId == index)) SampleMesh(node, diffuse, detail, scale, raw);
                if (raw.Count == 0) continue;
                var under = detail == null ? 0f : raw.Sum(r => (1 - r.Diffuse.A) * r.Weight);
                var through = under > raw.Sum(r => r.Weight) / 2f;
                var shade = through ? raw.Sum(r => Lum(r.Diffuse.R, r.Diffuse.G, r.Diffuse.B) * (1 - r.Diffuse.A) * r.Weight) / under : 0f;

                var samples = new List<Sample>();
                foreach (var r in raw)
                {
                    // Where the diffuse is dark (a gap, a crease's shadow) the colour hardly shows, whatever the paint
                    var (cr, cg, cb) = Unshaded(Drawn(r.Diffuse, r.Detail), r.Diffuse, shade);
                    samples.Add(new Sample(cr, cg, cb, r.Weight * Math.Min(1f, ShadingOf(r.Diffuse, shade))));
                }
                looked.Add((new PaintLayer(material.GetMappingByName(Diffuse)!.Texture, through ? detailName : null, scale, shade), samples));
            }

            if (looked.Count == 0)
            {
                Logger.Information("{Car}: its paint's textures could not be read, it is painted in its own colours only", Path.GetFileName(carDirectory));
                return new PaintSurface([], PaintColour.Black, 0, false);
            }

            // The body's colour is the paint's; another material of the same colour (a door, a bonnet) is painted with it,
            // one of another (the chassis, black) is not
            var (colour, share) = Dominant(looked[0].Samples);
            var (cr0, cg0, cb0) = colour.Unit;
            var layers = looked.Where((l, i) => i == 0 || Dominant(l.Samples) is var (c, _) && Closeness(c.Unit.R, c.Unit.G, c.Unit.B, cr0, cg0, cb0) >= 1f)
                .Select(l => l.Layer).Distinct().ToList();
            var baked = layers.Any(l => l.Detail != null);
            var surface = new PaintSurface(layers, colour, share, baked);
            Logger.Information("{Car} ({Skin}): paint {Colour} over {Share:P0} of it, in {Textures}{Baked} ({Ms} ms)", Path.GetFileName(carDirectory),
                string.IsNullOrEmpty(skin) ? "its model's" : skin, colour, share, string.Join(", ", surface.Textures), baked ? ", through its detail" : "",
                Environment.TickCount64 - started);
            return surface;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "{Car}: its paint could not be looked at, it is painted in its own colours only", Path.GetFileName(carDirectory));
            return null;
        }
    }

    /// <summary>A multi-map shader with its detail on multiplies its detail texture in where the diffuse is see-through</summary>
    private static bool UsesDetail(Kn5Material material) =>
        material.ShaderName.Contains("MultiMap", StringComparison.OrdinalIgnoreCase) && material.GetMappingByName(Detail) != null
        && material.GetPropertyByName("useDetail")?.ValueA > 0f;

    /// <summary>How many times the detail repeats over the diffuse's picture</summary>
    private static float DetailScale(Kn5Material material) =>
        material.GetPropertyByName("detailUVMultiplier")?.ValueA is { } scale && float.IsFinite(scale) && scale > 0f ? scale : 1f;

    /// <summary>AC's multi-map colour: the diffuse, times the detail where the diffuse is see-through</summary>
    private static (float R, float G, float B) Drawn((float R, float G, float B, float A) diffuse, (float R, float G, float B, float A)? detail)
    {
        if (detail is not { } e) return (diffuse.R, diffuse.G, diffuse.B);
        var a = diffuse.A;
        return (diffuse.R * (e.R + (1 - e.R) * a), diffuse.G * (e.G + (1 - e.G) * a), diffuse.B * (e.B + (1 - e.B) * a));
    }

    /// <summary>
    /// A colour drawn with a detail under a shaded diffuse (its shading baked in: the AMC Javelin's grey diffuse over its
    /// blue detail), the diffuse's shading taken out: what the paint is, the same in a crease and on a panel. The diffuse's
    /// lightness against <paramref name="shade"/>, its mean under the detail; none (0) takes nothing out.
    /// </summary>
    private static (float R, float G, float B) Unshaded((float R, float G, float B) drawn, (float R, float G, float B, float A) diffuse, float shade)
    {
        var s = ShadingOf(diffuse, shade);
        return (drawn.R / s, drawn.G / s, drawn.B / s);
    }

    private static float ShadingOf((float R, float G, float B, float A) diffuse, float shade) =>
        shade > 0f ? Math.Clamp((Lum(diffuse.R, diffuse.G, diffuse.B) + 0.02f) / (shade + 0.02f), 0.05f, 4f) : 1f;

    private readonly record struct Sample(float R, float G, float B, float Weight);

    /// <summary>A point of the paint: its diffuse and its detail there, and how much of the car it stands for</summary>
    private readonly record struct RawSample((float R, float G, float B, float A) Diffuse, (float R, float G, float B, float A)? Detail, float Weight);

    /// <summary>What a mesh shows at a few points of each triangle, each weighed by its triangle's area</summary>
    private static void SampleMesh(Kn5Node node, Picture diffuse, Picture? detail, float detailScale, List<RawSample> samples)
    {
        ReadOnlySpan<(float A, float B, float C)> points = [(1 / 3f, 1 / 3f, 1 / 3f), (0.6f, 0.2f, 0.2f), (0.2f, 0.6f, 0.2f), (0.2f, 0.2f, 0.6f)];
        var v = node.Vertices;
        var ix = node.Indices;
        for (var t = 0; t + 2 < ix.Length; t += 3)
        {
            var (a, b, c) = (v[ix[t]], v[ix[t + 1]], v[ix[t + 2]]);
            var ab = new System.Numerics.Vector3(b.Position.X - a.Position.X, b.Position.Y - a.Position.Y, b.Position.Z - a.Position.Z);
            var ac = new System.Numerics.Vector3(c.Position.X - a.Position.X, c.Position.Y - a.Position.Y, c.Position.Z - a.Position.Z);
            var area = System.Numerics.Vector3.Cross(ab, ac).Length() / 2f / points.Length;
            if (!(area > 0f)) continue;
            foreach (var (wa, wb, wc) in points)
            {
                var u = a.Tex.X * wa + b.Tex.X * wb + c.Tex.X * wc;
                var w = a.Tex.Y * wa + b.Tex.Y * wb + c.Tex.Y * wc;
                samples.Add(new RawSample(diffuse.At(u, w), detail?.At(u * detailScale, w * detailScale), area));
            }
        }
    }

    /// <summary>The colour most of the samples show (the most common of 16 shades a channel, then the mean of those near it) and how much of them is close to it</summary>
    internal static (PaintColour Colour, double Share) Dominant(IReadOnlyList<(float R, float G, float B, float Weight)> samples) =>
        Dominant(samples.Select(s => new Sample(s.R, s.G, s.B, s.Weight)).ToList());

    private static (PaintColour Colour, double Share) Dominant(List<Sample> samples)
    {
        var bins = new Dictionary<int, float>();
        foreach (var s in samples)
        {
            var key = (Bin(s.R) << 8) | (Bin(s.G) << 4) | Bin(s.B);
            bins[key] = bins.GetValueOrDefault(key) + s.Weight;
        }
        var mode = bins.MaxBy(b => b.Value).Key;
        var (mr, mg, mb) = (((mode >> 8) + 0.5f) / 16f, (((mode >> 4) & 15) + 0.5f) / 16f, ((mode & 15) + 0.5f) / 16f);

        var near = samples.Where(s => Math.Abs(s.R - mr) + Math.Abs(s.G - mg) + Math.Abs(s.B - mb) < 0.15f).ToList();
        var weight = near.Sum(s => s.Weight);
        if (weight > 0f) (mr, mg, mb) = (near.Sum(s => s.R * s.Weight) / weight, near.Sum(s => s.G * s.Weight) / weight, near.Sum(s => s.B * s.Weight) / weight);

        var total = samples.Sum(s => s.Weight);
        var close = samples.Where(s => Closeness(s.R, s.G, s.B, mr, mg, mb) >= 1f).Sum(s => s.Weight);
        return (PaintColour.Of(mr, mg, mb), total > 0f ? close / total : 0);

        static int Bin(float v) => Math.Clamp((int)(v * 16f), 0, 15);
    }

    /// <summary>
    /// How much a colour is the paint's (<paramref name="mr"/>, <paramref name="mg"/>, <paramref name="mb"/>): 1 close in hue
    /// and lightness, 0 another colour, between at the edge
    /// </summary>
    internal static float Closeness(float r, float g, float b, float mr, float mg, float mb)
    {
        var l = Lum(r, g, b);
        var ml = Lum(mr, mg, mb);
        var light = (l + Damp) / (ml + Damp);
        var hue = Math.Abs((r + Damp) / (l + Damp) - (mr + Damp) / (ml + Damp))
                  + Math.Abs((g + Damp) / (l + Damp) - (mg + Damp) / (ml + Damp))
                  + Math.Abs((b + Damp) / (l + Damp) - (mb + Damp) / (ml + Damp));

        var byLight = light < LightLow ? (light - LightLowest) / (LightLow - LightLowest)
            : light > LightHigh ? (LightHighest - light) / (LightHighest - LightHigh)
            : 1f;
        var byHue = hue <= Hue ? 1f : (HueMost - hue) / (HueMost - Hue);
        return Math.Clamp(Math.Min(byLight, byHue), 0f, 1f);
    }

    private static float Lum(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;

    #endregion

    #region Tinting

    /// <summary>
    /// A texel of the paint's colour <paramref name="paint"/> repainted <paramref name="colour"/>, shaded as it was: lighter
    /// or darker than the paint, as much lighter or darker than the new colour; one of another colour left as it is, one at
    /// the edge between
    /// </summary>
    internal static (float R, float G, float B) Tint(float r, float g, float b, PaintColour paint, PaintColour colour)
    {
        var (mr, mg, mb) = paint.Unit;
        var weight = Closeness(r, g, b, mr, mg, mb);
        if (weight <= 0f) return (r, g, b);

        var shade = (Lum(r, g, b) + Damp) / (Lum(mr, mg, mb) + Damp);
        var (tr, tg, tb) = colour.Unit;
        float Mix(float was, float to) => was + (Math.Clamp(to * shade, 0f, 1f) - was) * weight;
        return (Mix(r, tr), Mix(g, tg), Mix(b, tb));
    }

    /// <summary>
    /// The textures of the car in <paramref name="carDirectory"/> (in its skin <paramref name="skin"/>) for its paint in
    /// <paramref name="colour"/>, written once and kept (<see cref="Cache"/>): each texture's name and the file. Empty when
    /// the car's paint cannot be tinted. Slow the first time: off the UI thread.
    /// </summary>
    public static IReadOnlyList<TintedTexture> Tinted(string carDirectory, string? skin, PaintColour colour)
    {
        if (SurfaceOf(carDirectory, skin) is not { CanTint: true } surface || WearLayer.ModelOf(carDirectory) is not { } model) return [];

        var folder = Cache;
        Directory.CreateDirectory(folder);
        var stamp = StableHash.Fnv($"{ShellFile.KeyOf(model)}|{SkinFolder(carDirectory, skin)}|{surface.Colour}|{Version}");
        var car = Path.GetFileName(carDirectory);
        string FileOf(string texture) => Path.Combine(folder, $"{car}_{stamp:x8}_{colour.Hex.TrimStart('#')}_{StableHash.Fnv(texture.ToLowerInvariant()):x8}.png");

        // Made before: as it was written (a diffuse left untouched was not written, and is not over the car's own)
        var listed = Path.Combine(folder, $"{car}_{stamp:x8}_{colour.Hex.TrimStart('#')}.list");
        if (File.Exists(listed))
        {
            var kept = File.ReadAllLines(listed).Where(t => t.Length > 0).Select(t => new TintedTexture(t, FileOf(t))).ToList();
            if (kept.All(t => File.Exists(t.File)))
            {
                foreach (var t in kept) Touch(t.File);
                Touch(listed);
                return kept;
            }
        }

        var started = Environment.TickCount64;
        var pictures = new Dictionary<string, Picture>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, bytes) in TextureBytes(model, carDirectory, skin, surface.Textures))
            if (Decode(bytes, null) is { } picture) pictures[name] = picture;

        var made = new Dictionary<string, Picture>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in surface.Layers)
        {
            if (made.ContainsKey(layer.Diffuse) || !pictures.TryGetValue(layer.Diffuse, out var diffuse)) continue;
            if (layer.Detail != null && pictures.TryGetValue(layer.Detail, out var detail))
            {
                // The colour through the detail: baked into the diffuse and tinted there, the detail made plain white
                made[layer.Diffuse] = Baked(diffuse, detail, layer, surface.Colour, colour);
                if (!made.ContainsKey(layer.Detail)) made[layer.Detail] = White(detail);
            }
            else if (TintPicture(diffuse, surface.Colour, colour) >= SomeChanged)
            {
                made[layer.Diffuse] = diffuse;
            }
        }

        var result = new List<TintedTexture>();
        foreach (var (texture, picture) in made)
        {
            var file = FileOf(texture);
            SafeFile.WriteAllBytes(file, Png.Encode(picture.Rgba, picture.Width, picture.Height));
            result.Add(new TintedTexture(texture, file));
        }
        SafeFile.WriteAllText(listed, string.Join(Environment.NewLine, result.Select(r => r.Texture)));
        Logger.Information("{Car}: paint {Colour} made in {Ms} ms ({Textures})", car, colour, Environment.TickCount64 - started, string.Join(", ", result.Select(r => r.Texture)));
        Prune(folder, result.Select(r => r.File).Append(listed).ToHashSet(StringComparer.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>Repaints a picture's texels of <paramref name="paint"/> in <paramref name="colour"/>; the share of its texels changed</summary>
    private static double TintPicture(Picture picture, PaintColour paint, PaintColour colour)
    {
        var rgba = picture.Rgba;
        var changed = 0L;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var (r, g, b) = (rgba[i] / 255f, rgba[i + 1] / 255f, rgba[i + 2] / 255f);
            var (nr, ng, nb) = Tint(r, g, b, paint, colour);
            var (br, bg, bb) = (ToByte(nr), ToByte(ng), ToByte(nb));
            if (br == rgba[i] && bg == rgba[i + 1] && bb == rgba[i + 2]) continue;
            (rgba[i], rgba[i + 1], rgba[i + 2]) = (br, bg, bb);
            changed++;
        }
        return rgba.Length == 0 ? 0 : changed * 4.0 / rgba.Length;
    }

    /// <summary>
    /// The diffuse with the detail's colour multiplied in as AC draws it, tinted: the colour all in the diffuse, its alpha
    /// as it was; as big as the bigger of the two (the detail repeated as it is over the diffuse), at most <see cref="MostBaked"/>
    /// </summary>
    private static Picture Baked(Picture diffuse, Picture detail, PaintLayer layer, PaintColour paint, PaintColour colour)
    {
        var detailScale = layer.DetailScale;
        detail = Fine(detail, detailScale);
        var width = Math.Min(MostBaked, Math.Max(diffuse.Width, (int)(detail.Width * Math.Max(1f, detailScale))));
        var height = Math.Min(MostBaked, Math.Max(diffuse.Height, (int)(detail.Height * Math.Max(1f, detailScale))));
        var rgba = new byte[width * height * 4];
        Parallel.For(0, height, WearLayer.Work, y =>
        {
            var v = (y + 0.5f) / height;
            for (var x = 0; x < width; x++)
            {
                var u = (x + 0.5f) / width;
                var d = diffuse.At(u, v);
                var shading = ShadingOf(d, layer.Shade);
                var (r, g, b) = Unshaded(Drawn(d, detail.At(u * detailScale, v * detailScale)), d, layer.Shade);
                var (nr, ng, nb) = Tint(r, g, b, paint, colour);
                (nr, ng, nb) = (nr * shading, ng * shading, nb * shading);
                var i = (y * width + x) * 4;
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (ToByte(nr), ToByte(ng), ToByte(nb), ToByte(d.A));
            }
        });
        return new Picture(width, height, rgba);
    }

    /// <summary>A detail as the paint shows it: one of a fine grain (<see cref="FineDetail"/>) its average colour, its alpha as it was</summary>
    private static Picture Fine(Picture detail, float scale)
    {
        if (scale <= FineDetail) return detail;
        var rgba = detail.Rgba;
        double r = 0, g = 0, b = 0;
        for (var i = 0; i < rgba.Length; i += 4) (r, g, b) = (r + rgba[i], g + rgba[i + 1], b + rgba[i + 2]);
        var n = Math.Max(1, rgba.Length / 4);
        var (mr, mg, mb) = ((byte)(r / n), (byte)(g / n), (byte)(b / n));
        var mean = (byte[])rgba.Clone();
        for (var i = 0; i < mean.Length; i += 4) (mean[i], mean[i + 1], mean[i + 2]) = (mr, mg, mb);
        return new Picture(detail.Width, detail.Height, mean);
    }

    /// <summary>A detail made plain white, its alpha (how the paint takes the light) as it was</summary>
    private static Picture White(Picture detail)
    {
        var rgba = (byte[])detail.Rgba.Clone();
        for (var i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 1], rgba[i + 2]) = (255, 255, 255);
        return new Picture(detail.Width, detail.Height, rgba);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    /// <summary>
    /// The car's paint for a scene: each tinted texture's name (<see cref="Tinted"/>) and its bytes as the renderer takes
    /// them best (a DDS with its mips: nothing to decode as it loads). Empty when the car's paint cannot be tinted. Off the
    /// UI thread.
    /// </summary>
    public static IReadOnlyList<(string Texture, byte[] Bytes)> ForScene(string carDirectory, string? skin, PaintColour colour) =>
        Tinted(carDirectory, skin, colour).Select(t => (t.Texture, ForRenderer(ReadPng(File.ReadAllBytes(t.File))))).ToList();

    /// <summary>
    /// The texels of the car's paint textures as it is in <paramref name="skin"/>, painted <paramref name="colour"/> (null:
    /// the skin's own colours), by texture: what a paint job blends from and to (<see cref="Blend"/>). The textures named
    /// (those another paint job changes), or those this one changes. Off the UI thread.
    /// </summary>
    public static IReadOnlyDictionary<string, PaintTexels> TexelsOf(string carDirectory, string? skin, PaintColour? colour, IReadOnlyCollection<string>? names = null)
    {
        var result = new Dictionary<string, PaintTexels>(StringComparer.OrdinalIgnoreCase);
        if (WearLayer.ModelOf(carDirectory) is not { } model || SurfaceOf(carDirectory, skin) is not { } surface) return result;

        var tinted = colour is { } c ? Tinted(carDirectory, skin, c).ToDictionary(t => t.Texture, StringComparer.OrdinalIgnoreCase) : null;
        names ??= tinted?.Keys.ToList() ?? surface.Textures;
        foreach (var (name, bytes) in TextureBytes(model, carDirectory, skin, names))
        {
            var picture = tinted != null && tinted.TryGetValue(name, out var t) ? ReadPng(File.ReadAllBytes(t.File)) : Decode(bytes, null);
            if (picture != null) result[name] = new PaintTexels(picture.Width, picture.Height, picture.Rgba);
        }
        return result;
    }

    /// <summary>
    /// A coat of the paint job: <paramref name="from"/> going over to <paramref name="to"/> (the same texture's; one of
    /// another size is read at the other's), <paramref name="share"/> of the way, as the renderer takes it
    /// (<see cref="ForScene"/>)
    /// </summary>
    public static byte[] Blend(PaintTexels from, PaintTexels to, float share)
    {
        var s = Math.Clamp(share, 0f, 1f);
        var source = new Picture(from.Width, from.Height, from.Rgba);
        var same = from.Width == to.Width && from.Height == to.Height;
        var rgba = new byte[to.Rgba.Length];
        for (var y = 0; y < to.Height; y++)
        for (var x = 0; x < to.Width; x++)
        {
            var i = (y * to.Width + x) * 4;
            if (same)
            {
                for (var k = 0; k < 4; k++) rgba[i + k] = (byte)(from.Rgba[i + k] + (to.Rgba[i + k] - from.Rgba[i + k]) * s + 0.5f);
                continue;
            }
            var (r, g, b, a) = source.At((x + 0.5f) / to.Width, (y + 0.5f) / to.Height);
            rgba[i] = (byte)(r * 255f + (to.Rgba[i] - r * 255f) * s + 0.5f);
            rgba[i + 1] = (byte)(g * 255f + (to.Rgba[i + 1] - g * 255f) * s + 0.5f);
            rgba[i + 2] = (byte)(b * 255f + (to.Rgba[i + 2] - b * 255f) * s + 0.5f);
            rgba[i + 3] = (byte)(a * 255f + (to.Rgba[i + 3] - a * 255f) * s + 0.5f);
        }
        return ForRenderer(new Picture(to.Width, to.Height, rgba));
    }

    /// <summary>A picture as the renderer takes it best: a DDS with its mips when its sides are powers of two, else a PNG</summary>
    private static byte[] ForRenderer(Picture picture) =>
        BitOperations.IsPow2(picture.Width) && BitOperations.IsPow2(picture.Height)
            ? Dds.Encode(picture.Rgba, picture.Width, picture.Height)
            : Png.Encode(picture.Rgba, picture.Width, picture.Height);

    /// <summary>
    /// The car's paint for a race: its tinted textures (<see cref="Tinted"/>) as the race mode's files, in
    /// <paramref name="shown"/>; as it was when the car keeps its own colours or they cannot be made (it races in them).
    /// </summary>
    public static async Task<ShownSet?> ForRaceAsync(ShownSet? shown, string carDirectory, string? skin)
    {
        if (shown?.Paint is not { } colour) return shown;
        try
        {
            var tinted = await Task.Run(() => Tinted(carDirectory, skin, colour));
            if (tinted.Count == 0) return shown;
            return shown.WithPaintFiles(tinted.Select(t => new ShownPaint(
                new ShownModel(Path.Combine(Folder, Path.GetFileName(t.File)), t.File), t.Texture)).ToList());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "The paint of {Car} could not be made for the race: it races in its own colours", Path.GetFileName(carDirectory));
            return shown;
        }
    }

    #endregion

    #region Textures

    /// <summary>The skin's folder, or "" for the model's own textures</summary>
    private static string SkinFolder(string carDirectory, string? skin) =>
        string.IsNullOrEmpty(skin) ? "" : Path.Combine(carDirectory, "skins", skin);

    /// <summary>
    /// The bytes of each of <paramref name="names"/> as AC draws the car in <paramref name="skin"/>: the skin's file of that
    /// name, else the model's own
    /// </summary>
    private static IEnumerable<(string Name, byte[] Bytes)> TextureBytes(string model, string carDirectory, string? skin, IReadOnlyCollection<string> names)
    {
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var folder = SkinFolder(carDirectory, skin);
        if (folder.Length > 0 && Directory.Exists(folder))
        {
            foreach (var file in Directory.GetFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (!wanted.Remove(name)) continue;
                yield return (names.First(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)), File.ReadAllBytes(file));
            }
        }
        if (wanted.Count == 0) yield break;

        var loader = new OnlyTextures(wanted);
        Kn5.FromFile(model, loader);
        foreach (var (name, bytes) in loader.Read) yield return (name, bytes);
    }

    /// <summary>Reads only the textures asked for of a model; the rest is skipped over</summary>
    private sealed class OnlyTextures(IReadOnlySet<string> wanted) : IKn5TextureLoader
    {
        public readonly List<(string Name, byte[] Bytes)> Read = [];

        public void OnNewKn5(string kn5Filename) { }

        public byte[]? LoadTexture(string textureName, AcTools.ReadAheadBinaryReader reader, int textureSize)
        {
            if (!wanted.Contains(textureName))
            {
                reader.Skip(textureSize);
                return null;
            }
            var bytes = reader.ReadBytes(textureSize);
            Read.Add((textureName, bytes));
            return null;
        }
    }

    /// <summary>A texture's texels, RGBA, rows from the top, and how it is read at a texture coordinate (wrapping)</summary>
    private sealed record Picture(int Width, int Height, byte[] Rgba)
    {
        public (float R, float G, float B, float A) At(float u, float v)
        {
            var x = (int)((u - MathF.Floor(u)) * Width) % Width;
            var y = (int)((v - MathF.Floor(v)) * Height) % Height;
            var i = (y * Width + x) * 4;
            return (Rgba[i] / 255f, Rgba[i + 1] / 255f, Rgba[i + 2] / 255f, Rgba[i + 3] / 255f);
        }
    }

    /// <summary>A PNG of ours as texels</summary>
    private static Picture ReadPng(byte[] png)
    {
        var frame = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var (width, height) = (converted.PixelWidth, converted.PixelHeight);
        var texels = new byte[width * height * 4];
        converted.CopyPixels(texels, width * 4, 0);
        // BGRA to RGBA
        for (var i = 0; i < texels.Length; i += 4) (texels[i], texels[i + 2]) = (texels[i + 2], texels[i]);
        return new Picture(width, height, texels);
    }

    /// <summary>
    /// A texture's bytes (any kind AcTools draws) as texels, made at most <paramref name="most"/> a side; null when it
    /// cannot be read. Its colour is read apart from its alpha: where a diffuse is see-through, its colour still counts
    /// (AC multiplies the detail by it), and a reading with the alpha loses it.
    /// </summary>
    private static Picture? Decode(byte[] bytes, int? most)
    {
        byte[] colour, alpha;
        try
        {
            lock (Reading)
            {
                _reader ??= new TextureReader();
                var size = most is { } m ? new System.Drawing.Size(m, m) : (System.Drawing.Size?)null;
                colour = _reader.ToPng(bytes, true, size);
                alpha = _reader.ToPng(bytes, false, size);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning("A paint texture could not be read: {Error}", ex.Message);
            return null;
        }

        var picture = ReadPng(colour);
        var withAlpha = ReadPng(alpha);
        if (withAlpha.Width != picture.Width || withAlpha.Height != picture.Height) return withAlpha;
        for (var i = 3; i < picture.Rgba.Length; i += 4) picture.Rgba[i] = withAlpha.Rgba[i];
        return picture;
    }

    private static IEnumerable<Kn5Node> Meshes(Kn5Node node)
    {
        if (node.NodeClass != Kn5NodeClass.Base)
        {
            yield return node;
            yield break;
        }
        foreach (var child in node.Children)
        foreach (var mesh in Meshes(child))
            yield return mesh;
    }

    private static void Touch(string file)
    {
        try
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only or in use: pruned a little sooner, made again if so
        }
    }

    /// <summary>The oldest tints go past <see cref="KeptFiles"/> cars' worth, but for <paramref name="keep"/></summary>
    private static void Prune(string folder, IReadOnlySet<string> keep)
    {
        lock (Pruning)
        {
            foreach (var file in new DirectoryInfo(folder).GetFiles().Where(f => f.Extension is ".png" or ".list")
                         .OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeptFiles * 3).Where(f => !keep.Contains(f.FullName)))
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // In use: next time
                }
            }
        }
    }

    #endregion
}

/// <summary>A texture's texels, RGBA, rows from the top</summary>
public sealed record PaintTexels(int Width, int Height, byte[] Rgba);

/// <summary>A paint's texture tinted to a colour, written to <paramref name="File"/></summary>
public sealed record TintedTexture(string Texture, string File);

/// <summary>
/// One paint material's colour as AC draws it: its diffuse texture, and the detail multiplied in where the diffuse is
/// see-through (repeated <paramref name="DetailScale"/> times over it), and how light the diffuse is there on average
/// (its shading is taken out to judge the paint's colour, and put back after)
/// </summary>
public sealed record PaintLayer(string Diffuse, string? Detail, float DetailScale, float Shade);

/// <summary>
/// A car's paint in a skin: its layers, the colour most of it shows, how much of it shows that colour, and whether its
/// colour comes through its detail (baked into the diffuse when it is tinted)
/// </summary>
public sealed record PaintSurface(IReadOnlyList<PaintLayer> Layers, PaintColour Colour, double Share, bool ThroughDetail)
{
    public bool CanTint => Layers.Count > 0 && Share >= CarPaint.MinShare;

    /// <summary>The textures a tint may change, by their names in the model</summary>
    public IReadOnlyList<string> Textures =>
        Layers.SelectMany(l => new[] { l.Diffuse, l.Detail }).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
