using System.IO;
using System.Text.Json;
using AcTools;
using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Configuration;
using StreetCorsa.Logging;
using StreetCorsa.Services.Race.Traffic;
using StreetCorsa.Services.Race.Traffic.Conversion;
using StreetCorsa.Helpers;

namespace StreetCorsa.Services.Showcase;

/// <summary>
/// A road made light enough to film behind a screen: a stretch or two of it (<see cref="Stretches"/>), every model its
/// layout loads merged into one KN5 with what lies off those stretches left out (small things kept only near them, the
/// land out to a wider corridor) and what AC never draws dropped, its pictures made small. A road is hundreds of
/// megabytes and seconds to load as AC has it; its backdrop tens of megabytes and under a second. Made once on the
/// player's machine (a Kunos road's is never shipped) and kept in <c>%AppData%\StreetCorsa\Backdrops</c> with a stamp
/// of what it was made from, made again when that changes.
/// </summary>
public static class RoadBackdrops
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Backdrops");

    /// <summary>Bumped when what a backdrop is made of changes: every one is made again</summary>
    public const int Version = 2;

    /// <summary>
    /// How long a stretch filmed is, in metres along the AI line: a visit of a road's, the look ahead and a margin
    /// (<see cref="Controls.RoadBackdropViewport3D"/> flies about 220 m of it)
    /// </summary>
    public const float StretchLength = 340f;

    // Where the stretches start, as shares of the line; a line shorter than this is filmed whole
    private static readonly float[] StretchStarts = [0.2f, 0.62f];
    private const float WholeUnder = StretchLength * 4;

    // How far from the AI line things stay in the picture, in metres: the land out to the corridor, a thing smaller than
    // SmallRadius (a stone, a tuft, a sign) out to SmallReach. Its pictures no bigger than MaxTexture a side. The look of
    // a backdrop, not a figure the game plays by.
    private const float CorridorReach = 500f;
    private const float SmallRadius = 15f;
    private const float SmallReach = 200f;
    private const float SmallTriangle = 12f;
    private const int MaxTexture = 512;

    // A mesh this big (and this spread out) is cut triangle by triangle rather than kept or dropped whole
    private const int CutFromIndices = 6000;

    // One made at a time: each holds a road's models in memory
    private static readonly SemaphoreSlim Making = new(1, 1);

    public static string DefaultRoot => Path.Combine(AppSettings.AppDataPath, "Backdrops");

    /// <summary>The backdrop's file for a road: <c>&lt;track&gt;[__&lt;layout&gt;].kn5</c></summary>
    public static string FileFor(string trackId, string? layout, string? root = null) =>
        Path.Combine(root ?? DefaultRoot, Safe(trackId) + (string.IsNullOrEmpty(layout) ? "" : "__" + Safe(layout)) + ".kn5");

    /// <summary>
    /// The models a layout loads: its <c>models_&lt;layout&gt;.ini</c> (or <c>models.ini</c>) FILE entries that are there,
    /// else the track's own <c>&lt;folder&gt;.kn5</c>; none when it has neither
    /// </summary>
    public static List<string> Pieces(string trackFolder, string? layout)
    {
        var ini = Path.Combine(trackFolder, string.IsNullOrEmpty(layout) ? "models.ini" : $"models_{layout}.ini");
        if (File.Exists(ini))
        {
            var listed = File.ReadAllLines(ini)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("FILE=", StringComparison.OrdinalIgnoreCase))
                .Select(l => Path.Combine(trackFolder, l[5..].Trim()))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (listed.Count > 0) return listed;
        }

        var own = Path.Combine(trackFolder, Path.GetFileName(Path.TrimEndingDirectorySeparator(trackFolder)) + ".kn5");
        return File.Exists(own) ? [own] : [];
    }

    /// <summary>
    /// The stretches of a line <paramref name="length"/> metres long that a backdrop keeps and its camera films, as
    /// distances along it: two, apart, on a long road; the whole line on a short one
    /// </summary>
    public static IReadOnlyList<(float From, float To)> Stretches(float length) =>
        length < WholeUnder ? [(0f, length)] : StretchStarts.Select(s => (s * length, s * length + StretchLength)).ToList();

    /// <summary>The road's backdrop when it is made and still matches what it was made from; else null</summary>
    public static string? Ready(string trackFolder, string trackId, string? layout, string? root = null)
    {
        try
        {
            var file = FileFor(trackId, layout, root);
            if (!File.Exists(file) || !File.Exists(StampOf(file))) return null;
            return File.ReadAllText(StampOf(file)) == Stamp(trackFolder, layout) ? file : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The road's backdrop, made now when it is not ready (seconds: run it off the UI thread). Null for a road with no
    /// models or no AI line to film along, or one that cannot be made (logged).
    /// </summary>
    public static async Task<string?> GetAsync(string trackFolder, string trackId, string? layout, CancellationToken cancel = default, string? root = null)
    {
        if (Ready(trackFolder, trackId, layout, root) is { } ready) return ready;

        await Making.WaitAsync(cancel);
        try
        {
            // Made meanwhile by another ask
            if (Ready(trackFolder, trackId, layout, root) is { } made) return made;
            cancel.ThrowIfCancellationRequested();
            return await Task.Run(() => Make(trackFolder, trackId, layout, root), cancel);
        }
        finally
        {
            Making.Release();
        }
    }

    /// <summary>Makes the road's backdrop and its stamp; null when it cannot be made (logged)</summary>
    public static string? Make(string trackFolder, string trackId, string? layout, string? root = null)
    {
        using var texels = new GpuTexels();
        return Make(trackFolder, trackId, layout, root, texels);
    }

    /// <param name="texels">What reads a picture to make it smaller; none: only those with levels to drop are</param>
    internal static string? Make(string trackFolder, string trackId, string? layout, string? root, ITexels? texels)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var pieces = Pieces(trackFolder, layout);
            if (pieces.Count == 0 || AiLine.FileOf(trackFolder, layout) is not { } lineFile)
            {
                Logger.Information("{Track} {Layout}: no models or no AI line, no backdrop", trackId, layout);
                return null;
            }

            var line = AiLine.Read(lineFile).Points;
            var kn5 = Merge(pieces, new Corridor(Filmed(line)));
            Shrink(kn5, texels);

            var file = FileFor(trackId, layout, root);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using (var memory = new MemoryStream())
            {
                kn5.Save(memory);
                SafeFile.WriteAllBytes(file, memory.ToArray());
            }

            SafeFile.WriteAllText(StampOf(file), Stamp(trackFolder, layout));
            Logger.Information("{Track} {Layout}: backdrop of {Pieces} models made, {Mb} MB, in {Ms} ms", trackId, layout, pieces.Count,
                new FileInfo(file).Length / 1048576, (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return file;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not make the backdrop of {Track} {Layout}", trackId, layout);
            return null;
        }
    }

    /// <summary>
    /// The pieces in one KN5, read with every material named once (a KN5 may give two the same name: AC goes by their
    /// order, AcTools by name, and the meshes after the first such pair would come out in the wrong material or none),
    /// what lies off the road left out
    /// </summary>
    internal static IKn5 Merge(IReadOnlyList<string> pieces, Corridor corridor)
    {
        IKn5? all = null;
        foreach (var piece in pieces)
        {
            var kn5 = Kn5.FromFile(piece, DefaultKn5TextureLoader.Instance, new UniqueMaterialLoader(), DefaultKn5NodeLoader.Instance);
            Crop(kn5.RootNode, corridor);
            if (all == null)
            {
                all = kn5;
                continue;
            }

            // The piece's materials after the ones there are, renamed where a name is taken; its meshes renumbered to them
            var renumber = new Dictionary<uint, uint>();
            uint index = 0;
            foreach (var (name, material) in kn5.Materials.ToList())
            {
                var unique = name;
                for (var n = 2; all.Materials.ContainsKey(unique); n++) unique = $"{name}~{n}";
                material.Name = unique;
                renumber[index++] = (uint)all.Materials.Count;
                all.Materials[unique] = material;
            }

            foreach (var (name, texture) in kn5.Textures)
            {
                if (all.Textures.ContainsKey(name)) continue;
                all.Textures[name] = texture;
                all.TexturesData[name] = kn5.TexturesData[name];
            }

            Renumber(kn5.RootNode, renumber);
            all.RootNode.Children.AddRange(kn5.RootNode.Children);
        }

        return all ?? throw new InvalidDataException("no models");
    }

    /// <summary>The points of the line on its stretches (<see cref="Stretches"/>), a little before and after them</summary>
    internal static List<AiLine.Point> Filmed(IReadOnlyList<AiLine.Point> line)
    {
        const double pad = 60;
        var along = 0.0;
        var at = new double[line.Count];
        for (var i = 1; i < line.Count; i++)
        {
            var (a, b) = (line[i - 1], line[i]);
            along += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y) + (b.Z - a.Z) * (b.Z - a.Z));
            at[i] = along;
        }

        var stretches = Stretches((float)along);
        return line.Where((_, i) => stretches.Any(s => at[i] >= s.From - pad && at[i] <= s.To + pad)).ToList();
    }

    /// <summary>
    /// Leaves out what lies off the road, and what AC never draws (a node switched off, a mesh not visible or not
    /// rendered: colliders, hidden spares); false when nothing of the node is left. A KN5's bounding spheres and its
    /// meshes' points are where they are in the world (on some roads only once their nodes' transforms are taken; the
    /// sphere says which). A big spread-out mesh is cut triangle by triangle.
    /// </summary>
    private static bool Crop(Kn5Node node, Corridor corridor, Mat4x4? parent = null)
    {
        if (!node.Active) return false;
        var world = node.Transform * (parent ?? Mat4x4.Identity);
        if (node.NodeClass == Kn5NodeClass.Base)
        {
            node.Children?.RemoveAll(child => !Crop(child, corridor, world));
            return node.Children is { Count: > 0 };
        }

        if (!node.IsVisible || !node.IsRenderable) return false;
        var radius = node.BoundingSphereRadius;
        if (!corridor.Near(node.BoundingSphereCenter, (radius < SmallRadius ? SmallReach : CorridorReach) + radius)) return false;
        return radius <= SmallReach || node.Indices is not { Length: > CutFromIndices } || CutTriangles(node, corridor, world);
    }

    /// <summary>
    /// A mesh's small triangles (grass, stones) kept near the road only, its big ones (the land) out to the corridor;
    /// false when none is left. Its points are taken as they are, or through its transform, whichever puts them round its
    /// sphere; a mesh whose points sit round it neither way is kept whole.
    /// </summary>
    private static bool CutTriangles(Kn5Node node, Corridor corridor, Mat4x4 world)
    {
        var points = node.Vertices;
        var indices = node.Indices;
        if (FrameOf(node, world) is not { } frame) return true;

        var keep = new List<ushort>(indices.Length);
        for (var t = 0; t + 2 < indices.Length; t += 3)
        {
            Vec3 a = frame(points[indices[t]].Position), b = frame(points[indices[t + 1]].Position), c = frame(points[indices[t + 2]].Position);
            var edge = MathF.Max(Vec3.Distance(a, b), MathF.Max(Vec3.Distance(b, c), Vec3.Distance(c, a)));
            if (corridor.Near((a + b + c) / 3f, (edge < SmallTriangle ? SmallReach : CorridorReach) + edge))
                keep.AddRange([indices[t], indices[t + 1], indices[t + 2]]);
        }

        if (keep.Count == 0) return false;
        if (keep.Count == indices.Length) return true;

        var moved = new Dictionary<ushort, ushort>();
        var kept = new List<Kn5Node.Vertex>();
        var uv2 = node.Uv2 is { } second && second.Length == points.Length ? new List<Vec2>() : null;
        var cut = new ushort[keep.Count];
        for (var i = 0; i < keep.Count; i++)
        {
            if (!moved.TryGetValue(keep[i], out var to))
            {
                moved[keep[i]] = to = (ushort)kept.Count;
                kept.Add(points[keep[i]]);
                uv2?.Add(node.Uv2![keep[i]]);
            }

            cut[i] = to;
        }

        node.Vertices = kept.ToArray();
        node.Indices = cut;
        if (uv2 != null) node.Uv2 = uv2.ToArray();
        else if (node.Uv2 != null) node.Uv2 = null;
        return true;
    }

    /// <summary>
    /// How a mesh's points come to where they are in the world: as they are, or through its transform, whichever puts
    /// their middle within its sphere (the nearer one); null when neither does
    /// </summary>
    private static Func<Vec3, Vec3>? FrameOf(Kn5Node node, Mat4x4 world)
    {
        var points = node.Vertices;
        if (points.Length == 0) return null;
        var sum = new Vec3(0, 0, 0);
        foreach (var p in points) sum += p.Position;
        var middle = sum / points.Length;

        var raw = Vec3.Distance(middle, node.BoundingSphereCenter);
        var moved = Vec3.Distance(Vec3.Transform(middle, world), node.BoundingSphereCenter);
        var room = node.BoundingSphereRadius * 1.05f + 1f;
        if (raw <= room && raw <= moved) return p => p;
        if (moved <= room) return p => Vec3.Transform(p, world);
        return null;
    }

    private static void Renumber(Kn5Node node, Dictionary<uint, uint> renumber)
    {
        if (node.NodeClass != Kn5NodeClass.Base && renumber.TryGetValue(node.MaterialId, out var to)) node.MaterialId = to;
        foreach (var child in node.Children ?? []) Renumber(child, renumber);
    }

    /// <summary>The pictures no material uses left out, the rest made small (the traffic models' pipeline)</summary>
    private static void Shrink(IKn5 kn5, ITexels? texels)
    {
        var used = new HashSet<string>(kn5.Materials.Values.SelectMany(m => m.TextureMappings ?? []).Select(t => t.Texture), StringComparer.OrdinalIgnoreCase);
        foreach (var name in kn5.Textures.Keys.ToList())
        {
            if (!used.Contains(name))
            {
                kn5.Textures.Remove(name);
                kn5.TexturesData.Remove(name);
                continue;
            }

            if (kn5.TexturesData.GetValueOrDefault(name) is not { } bytes) continue;
            var (small, _) = TrafficTextures.Process(bytes, MaxTexture, texels);
            kn5.TexturesData[name] = small;
            kn5.Textures[name].Length = small.Length;
        }
    }

    /// <summary>What a backdrop was made from: the version, and each model's and the AI line's size and time</summary>
    private static string Stamp(string trackFolder, string? layout)
    {
        var files = Pieces(trackFolder, layout).Append(AiLine.FileOf(trackFolder, layout)).OfType<string>()
            .Select(f => new FileInfo(f))
            .Select(f => new { name = f.Name, size = f.Length, time = f.LastWriteTimeUtc.Ticks });
        return JsonSerializer.Serialize(new { version = Version, files });
    }

    private static string StampOf(string file) => Path.ChangeExtension(file, ".json");

    private static string Safe(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));

    /// <summary>Materials named once each, as they are read</summary>
    private sealed class UniqueMaterialLoader : IKn5MaterialLoader
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public void OnNewKn5(string kn5)
        {
            _seen.Clear();
            DefaultKn5MaterialLoader.Instance.OnNewKn5(kn5);
        }

        public Kn5Material LoadMaterial(ReadAheadBinaryReader reader)
        {
            var material = DefaultKn5MaterialLoader.Instance.LoadMaterial(reader);
            var name = material.Name;
            for (var n = 2; !_seen.Add(material.Name); n++) material.Name = $"{name}#{n}";
            return material;
        }
    }

    /// <summary>The ground along a road's AI line, in plan view, looked up in a grid</summary>
    internal sealed class Corridor
    {
        private const float Cell = 100f;
        private readonly Dictionary<(int, int), List<(float X, float Z)>> _grid = new();

        public Corridor(IEnumerable<AiLine.Point> line)
        {
            foreach (var p in line)
            {
                var key = ((int)Math.Floor(p.X / Cell), (int)Math.Floor(p.Z / Cell));
                if (!_grid.TryGetValue(key, out var cell)) _grid[key] = cell = [];
                cell.Add(((float)p.X, (float)p.Z));
            }
        }

        /// <summary>Whether a point is within <paramref name="reach"/> metres of the line, in plan view</summary>
        public bool Near(Vec3 at, float reach)
        {
            var cells = (int)Math.Ceiling(reach / Cell);
            int cx = (int)Math.Floor(at.X / Cell), cz = (int)Math.Floor(at.Z / Cell);
            var reach2 = reach * reach;
            for (var x = cx - cells; x <= cx + cells; x++)
            for (var z = cz - cells; z <= cz + cells; z++)
            {
                if (!_grid.TryGetValue((x, z), out var cell)) continue;
                foreach (var (px, pz) in cell)
                    if ((px - at.X) * (px - at.X) + (pz - at.Z) * (pz - at.Z) < reach2) return true;
            }

            return false;
        }
    }
}
