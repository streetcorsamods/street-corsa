using System.Globalization;
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
/// of what it was made from, made again when that changes; a road that could not be made is marked with the same
/// stamp and not tried again until it changes.
/// </summary>
public static class RoadBackdrops
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Backdrops");

    /// <summary>Bumped when what a backdrop is made of changes: every one is made again</summary>
    public const int Version = 3;

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
    public static List<string> Pieces(string trackFolder, string? layout) =>
        Placed(trackFolder, layout).Select(p => p.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// The models a layout loads (<see cref="Pieces"/>), each with where its <c>[MODEL_n]</c> puts it: POSITION, and
    /// ROTATION in degrees. AC's own reading of ROTATION is not written down; the Kunos layouts turn their start lights
    /// with the first figure alone (ks_barcelona 213,0,0, ks_highlands -17,0,0), so it is taken as Content Manager's
    /// track preview takes it (<c>RotationYawPitchRoll(x, y, z)</c>): heading round the upright axis, then pitch, then
    /// roll, the model turned about its own origin and then moved to POSITION.
    /// </summary>
    internal static List<(string File, Mat4x4 Placement)> Placed(string trackFolder, string? layout)
    {
        var ini = Path.Combine(trackFolder, string.IsNullOrEmpty(layout) ? "models.ini" : $"models_{layout}.ini");
        if (File.Exists(ini))
        {
            var listed = new List<(string File, Mat4x4 Placement)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in Models(File.ReadAllLines(ini)))
            {
                var file = Path.Combine(trackFolder, model.File);
                if (!File.Exists(file)) continue;
                var placement = Placement(model.Position, model.Rotation);
                // The same model twice in the same place is drawn once
                if (seen.Add(file + "|" + string.Join(",", model.Position) + "|" + string.Join(",", model.Rotation))) listed.Add((file, placement));
            }

            if (listed.Count > 0) return listed;
        }

        var own = Path.Combine(trackFolder, Path.GetFileName(Path.TrimEndingDirectorySeparator(trackFolder)) + ".kn5");
        return File.Exists(own) ? [(own, Mat4x4.Identity)] : [];
    }

    /// <summary>The sections of a models ini that name a FILE, with their POSITION and ROTATION (nought where not given)</summary>
    private static IEnumerable<(string File, float[] Position, float[] Rotation)> Models(IEnumerable<string> lines)
    {
        string? file = null;
        float[] position = [0, 0, 0], rotation = [0, 0, 0];
        foreach (var raw in lines.Append("[END]"))
        {
            var line = raw.Split(';')[0].Trim();
            if (line.StartsWith('['))
            {
                if (file != null) yield return (file, position, rotation);
                (file, position, rotation) = (null, [0, 0, 0], [0, 0, 0]);
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals < 0) continue;
            var (key, value) = (line[..equals].Trim(), line[(equals + 1)..].Trim());
            if (key.Equals("FILE", StringComparison.OrdinalIgnoreCase) && value.Length > 0) file = value;
            else if (key.Equals("POSITION", StringComparison.OrdinalIgnoreCase)) position = Three(value);
            else if (key.Equals("ROTATION", StringComparison.OrdinalIgnoreCase)) rotation = Three(value);
        }
    }

    private static float[] Three(string value)
    {
        var parts = value.Split(',');
        var three = new float[3];
        for (var i = 0; i < 3 && i < parts.Length; i++)
            three[i] = float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f) ? f : 0;
        return three;
    }

    private static Mat4x4 Placement(float[] position, float[] rotation)
    {
        var turn = rotation.All(r => r == 0)
            ? Mat4x4.Identity
            : Mat4x4.CreateFromYawPitchRoll(rotation[0] * MathF.PI / 180, rotation[1] * MathF.PI / 180, rotation[2] * MathF.PI / 180);
        return turn * Mat4x4.CreateTranslation(position[0], position[1], position[2]);
    }

    /// <summary>
    /// The stretches of a line <paramref name="length"/> metres long that a backdrop keeps and its camera films, as
    /// distances along it: two, apart, on a long road; the whole line on a short one
    /// </summary>
    public static IReadOnlyList<(float From, float To)> Stretches(float length) =>
        length < WholeUnder ? [(0f, length)] : StretchStarts.Select(s => (s * length, s * length + StretchLength)).ToList();

    /// <summary>The road's backdrop when it is made and still matches what it was made from; else null</summary>
    public static string? Ready(string trackFolder, string trackId, string? layout, string? root = null) =>
        Matches(StampOf(FileFor(trackId, layout, root)), trackFolder, layout) && File.Exists(FileFor(trackId, layout, root))
            ? FileFor(trackId, layout, root)
            : null;

    /// <summary>
    /// Whether the road's backdrop could not be made from what it has now (marked when the make failed): not tried
    /// again until a model or the AI line changes
    /// </summary>
    public static bool Failed(string trackFolder, string trackId, string? layout, string? root = null) =>
        Matches(FailedOf(FileFor(trackId, layout, root)), trackFolder, layout);

    /// <summary>
    /// The road's backdrop, made now when it is not ready (seconds: run it off the UI thread). Null for a road with no
    /// models or no AI line to film along, or one that cannot be made (logged once, then known by <see cref="Failed"/>).
    /// A cancel stops the make between models and meshes (<see cref="OperationCanceledException"/>), leaving no file.
    /// </summary>
    public static async Task<string?> GetAsync(string trackFolder, string trackId, string? layout, CancellationToken cancel = default, string? root = null)
    {
        if (Ready(trackFolder, trackId, layout, root) is { } ready) return ready;
        if (Failed(trackFolder, trackId, layout, root)) return null;

        await Making.WaitAsync(cancel);
        try
        {
            // Made (or found not to make) meanwhile by another ask
            if (Ready(trackFolder, trackId, layout, root) is { } made) return made;
            if (Failed(trackFolder, trackId, layout, root)) return null;
            cancel.ThrowIfCancellationRequested();
            return await Task.Run(() => Make(trackFolder, trackId, layout, root, cancel), cancel);
        }
        finally
        {
            Making.Release();
        }
    }

    /// <summary>
    /// Makes the road's backdrop and its stamp; null when it cannot be made (logged, and marked so that it is not tried
    /// again until the road changes). A cancel throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static string? Make(string trackFolder, string trackId, string? layout, string? root = null, CancellationToken cancel = default)
    {
        using var texels = new GpuTexels();
        return Make(trackFolder, trackId, layout, root, texels, cancel);
    }

    /// <param name="texels">What reads a picture to make it smaller; none: only those with levels to drop are</param>
    internal static string? Make(string trackFolder, string trackId, string? layout, string? root, ITexels? texels, CancellationToken cancel = default)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var file = FileFor(trackId, layout, root);
        string? stamp = null;
        try
        {
            var pieces = Placed(trackFolder, layout);
            if (pieces.Count == 0 || AiLine.FileOf(trackFolder, layout) is not { } lineFile)
            {
                Logger.Information("{Track} {Layout}: no models or no AI line, no backdrop", trackId, layout);
                return null;
            }

            // Taken before the models are read: one changed while they are is made again next time
            stamp = Stamp(trackFolder, layout);
            var line = AiLine.Read(lineFile).Points;
            var kn5 = Merge(pieces, new Corridor(Filmed(line)), texels, cancel);

            cancel.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            SafeFile.SweepTemps(Path.GetDirectoryName(file)!);
            Save(kn5, file);
            SafeFile.WriteAllText(StampOf(file), stamp);
            TryDelete(FailedOf(file));
            Logger.Information("{Track} {Layout}: backdrop of {Pieces} models made, {Mb} MB, in {Ms} ms", trackId, layout, pieces.Count,
                new FileInfo(file).Length / 1048576, (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return file;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Error(ex, "Could not make the backdrop of {Track} {Layout}", trackId, layout);
            // Out of memory says nothing of the road: another time it may be made
            if (stamp != null && ex is not OutOfMemoryException) MarkFailed(file, stamp);
            return null;
        }
    }

    /// <summary>
    /// The pieces in one KN5, each where its models ini puts it, what lies off the road left out (<see cref="Crop"/>).
    /// Each piece is made small as soon as it is read, before the next one is: the materials its kept meshes use (read
    /// with every material named once: a KN5 may give two the same name, AC goes by their order, AcTools by name, and
    /// the meshes after the first such pair would come out in the wrong material or none), renamed where an earlier
    /// piece took the name, and the pictures those use, made small, renamed where an earlier piece has another picture
    /// of the same name. A piece whose root is switched off is left out, as AC never draws it.
    /// </summary>
    internal static IKn5 Merge(IReadOnlyList<(string File, Mat4x4 Placement)> pieces, Corridor corridor, ITexels? texels, CancellationToken cancel = default)
    {
        if (pieces.Count == 0) throw new InvalidDataException("no models");
        var all = Kn5.CreateEmpty();
        foreach (var (piece, placement) in pieces)
        {
            cancel.ThrowIfCancellationRequested();
            IKn5 kn5;
            // Read from a stream of our own: AcTools' FromFile leaves the file open when it is not a KN5
            using (var stream = new FileStream(piece, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                kn5 = Kn5.FromStream(stream, DefaultKn5TextureLoader.Instance, new UniqueMaterialLoader(), DefaultKn5NodeLoader.Instance);
            var top = kn5.RootNode;
            if (!top.Active) continue;

            // A Base root's transform is the piece's, under its placement; a lone mesh sits in the placement's frame
            var isBase = top.NodeClass == Kn5NodeClass.Base;
            var frame = isBase ? top.Transform * placement : placement;
            var nodes = isBase ? top.Children ?? [] : [top];
            nodes.RemoveAll(child => !Crop(child, corridor, frame, cancel));
            if (nodes.Count == 0) continue;

            // The piece's materials its meshes use after the ones there are, renamed where a name is taken; its meshes
            // renumbered to them
            var uses = new HashSet<uint>();
            foreach (var node in nodes) MaterialsOf(node, uses);
            var renumber = new Dictionary<uint, uint>();
            var kept = new List<Kn5Material>();
            uint index = 0;
            foreach (var (name, material) in kn5.Materials ?? [])
            {
                if (uses.Contains(index))
                {
                    var unique = name;
                    for (var n = 2; all.Materials.ContainsKey(unique); n++) unique = $"{name}~{n}";
                    material.Name = unique;
                    renumber[index] = (uint)all.Materials.Count;
                    all.Materials[unique] = material;
                    kept.Add(material);
                }

                index++;
            }

            AddTextures(all, kn5, kept, texels, cancel);
            foreach (var node in nodes) Renumber(node, renumber);

            var wrap = Kn5Node.CreateBaseNode(Path.GetFileNameWithoutExtension(piece), nodes);
            wrap.Transform = frame;
            all.RootNode.Children.Add(wrap);
        }

        return all;
    }

    /// <summary>
    /// The piece's pictures its kept materials use, made small (the traffic models' pipeline), into the backdrop; one
    /// whose name the backdrop has for another picture renamed, and those materials pointed at the new name
    /// </summary>
    private static void AddTextures(IKn5 all, IKn5 kn5, IReadOnlyList<Kn5Material> materials, ITexels? texels, CancellationToken cancel)
    {
        var used = new HashSet<string>(materials.SelectMany(m => m.TextureMappings ?? []).Select(t => t.Texture), StringComparer.OrdinalIgnoreCase);
        foreach (var (name, data) in kn5.TexturesData ?? [])
        {
            if (!used.Contains(name) || data is not { Length: > 0 }) continue;
            cancel.ThrowIfCancellationRequested();
            var (small, _) = TrafficTextures.Process(data, MaxTexture, texels);

            var unique = name;
            for (var n = 2; all.TexturesData.TryGetValue(unique, out var there) && !there.AsSpan().SequenceEqual(small); n++)
                unique = Path.GetFileNameWithoutExtension(name) + "~" + n + Path.GetExtension(name);
            if (!all.TexturesData.ContainsKey(unique))
            {
                all.Textures[unique] = new Kn5Texture { Name = unique, Active = kn5.Textures?.GetValueOrDefault(name)?.Active ?? true, Length = small.Length };
                all.TexturesData[unique] = small;
            }

            if (unique == name) continue;
            foreach (var mapping in materials.SelectMany(m => m.TextureMappings ?? []))
                if (string.Equals(mapping.Texture, name, StringComparison.OrdinalIgnoreCase)) mapping.Texture = unique;
        }
    }

    private static void MaterialsOf(Kn5Node node, HashSet<uint> uses)
    {
        if (node.NodeClass != Kn5NodeClass.Base) uses.Add(node.MaterialId);
        foreach (var child in node.Children ?? []) MaterialsOf(child, uses);
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
    /// rendered: colliders, hidden spares); false when nothing of the node is left. <paramref name="parent"/> is the
    /// frame the node sits in. Only a Base node has a transform of its own (AcTools reads none for a mesh): a mesh's
    /// points and bounding sphere are in its parent's frame, and are taken through it. A big spread-out mesh is cut
    /// triangle by triangle.
    /// </summary>
    private static bool Crop(Kn5Node node, Corridor corridor, Mat4x4 parent, CancellationToken cancel)
    {
        if (!node.Active) return false;
        if (node.NodeClass == Kn5NodeClass.Base)
        {
            cancel.ThrowIfCancellationRequested();
            var world = node.Transform * parent;
            node.Children?.RemoveAll(child => !Crop(child, corridor, world, cancel));
            return node.Children is { Count: > 0 };
        }

        if (!node.IsVisible || !node.IsRenderable) return false;
        var (centre, radius) = SphereOf(node, parent);
        if (!corridor.Near(centre, (radius < SmallRadius ? SmallReach : CorridorReach) + radius)) return false;
        return radius <= SmallReach || node.Indices is not { Length: > CutFromIndices } || CutTriangles(node, corridor, parent);
    }

    /// <summary>
    /// A mesh's bounding sphere in the world: its own through its frame, or one round its points where it has none (a
    /// skinned mesh's is not read)
    /// </summary>
    internal static (Vec3 Centre, float Radius) SphereOf(Kn5Node node, Mat4x4 world)
    {
        Vec3 centre;
        float radius;
        if (node.NodeClass == Kn5NodeClass.Mesh && node.BoundingSphereRadius > 0)
        {
            (centre, radius) = (node.BoundingSphereCenter, node.BoundingSphereRadius);
        }
        else if (node.Vertices is { Length: > 0 } points)
        {
            var (low, high) = (points[0].Position, points[0].Position);
            foreach (var p in points) (low, high) = (Vec3.Min(low, p.Position), Vec3.Max(high, p.Position));
            centre = (low + high) / 2f;
            radius = 0;
            foreach (var p in points) radius = MathF.Max(radius, Vec3.Distance(centre, p.Position));
        }
        else
        {
            (centre, radius) = (node.BoundingSphereCenter, 0f);
        }

        if (world.IsIdentity) return (centre, radius);
        return (Vec3.Transform(centre, world), radius * ScaleOf(world));
    }

    // The most a transform stretches a length by
    private static float ScaleOf(Mat4x4 m) => MathF.Sqrt(MathF.Max(m.M11 * m.M11 + m.M12 * m.M12 + m.M13 * m.M13,
        MathF.Max(m.M21 * m.M21 + m.M22 * m.M22 + m.M23 * m.M23, m.M31 * m.M31 + m.M32 * m.M32 + m.M33 * m.M33)));

    /// <summary>
    /// A mesh's small triangles (grass, stones) kept near the road only, its big ones (the land) out to the corridor;
    /// false when none is left. Its points are taken through <paramref name="world"/>, the frame it sits in.
    /// </summary>
    private static bool CutTriangles(Kn5Node node, Corridor corridor, Mat4x4 world)
    {
        var points = node.Vertices;
        var indices = node.Indices;
        if (points.Length == 0) return true;
        Func<Vec3, Vec3> frame = world.IsIdentity ? p => p : p => Vec3.Transform(p, world);

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
        var weights = node.VerticeWeights is { } w && w.Length == points.Length ? new List<Kn5Node.VerticeWeight>() : null;
        var cut = new ushort[keep.Count];
        for (var i = 0; i < keep.Count; i++)
        {
            if (!moved.TryGetValue(keep[i], out var to))
            {
                moved[keep[i]] = to = (ushort)kept.Count;
                kept.Add(points[keep[i]]);
                uv2?.Add(node.Uv2![keep[i]]);
                weights?.Add(node.VerticeWeights![keep[i]]);
            }

            cut[i] = to;
        }

        node.Vertices = kept.ToArray();
        node.Indices = cut;
        if (uv2 != null) node.Uv2 = uv2.ToArray();
        else if (node.Uv2 != null) node.Uv2 = null;
        if (weights != null) node.VerticeWeights = weights.ToArray();
        return true;
    }

    private static void Renumber(Kn5Node node, Dictionary<uint, uint> renumber)
    {
        if (node.NodeClass != Kn5NodeClass.Base && renumber.TryGetValue(node.MaterialId, out var to)) node.MaterialId = to;
        foreach (var child in node.Children ?? []) Renumber(child, renumber);
    }

    /// <summary>
    /// The backdrop written straight to the disk (a road's is tens of megabytes: no copy in memory), as
    /// <see cref="SafeFile"/> writes: a temp file beside it, named as SafeFile names its own so that its sweep takes one
    /// a crash left, flushed, then renamed over the old one
    /// </summary>
    private static void Save(IKn5 kn5, string file)
    {
        var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, $".{Path.GetFileName(file)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16))
            {
                kn5.Save(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, file, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void MarkFailed(string file, string stamp)
    {
        try
        {
            SafeFile.WriteAllText(FailedOf(file), stamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not marked: it is tried again next time
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            SafeFile.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left: a stale mark or temp is harmless (a mark no longer matches once the road is made)
        }
    }

    /// <summary>Whether the stamp kept in <paramref name="kept"/> is what the road is made of now</summary>
    private static bool Matches(string kept, string trackFolder, string? layout)
    {
        try
        {
            return File.Exists(kept) && File.ReadAllText(kept) == Stamp(trackFolder, layout);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>What a backdrop was made from: the version, and each model's and the AI line's size and time</summary>
    private static string Stamp(string trackFolder, string? layout)
    {
        var ini = Path.Combine(trackFolder, string.IsNullOrEmpty(layout) ? "models.ini" : $"models_{layout}.ini");
        var files = Pieces(trackFolder, layout).Append(AiLine.FileOf(trackFolder, layout)).Append(File.Exists(ini) ? ini : null).OfType<string>()
            .Select(f => new FileInfo(f))
            .Select(f => new { name = f.Name, size = f.Length, time = f.LastWriteTimeUtc.Ticks });
        return JsonSerializer.Serialize(new { version = Version, files });
    }

    private static string StampOf(string file) => Path.ChangeExtension(file, ".json");

    private static string FailedOf(string file) => Path.ChangeExtension(file, ".failed");

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

        // How far the line's points go, in plan view
        private readonly float _minX = float.MaxValue, _maxX = float.MinValue, _minZ = float.MaxValue, _maxZ = float.MinValue;

        public Corridor(IEnumerable<AiLine.Point> line)
        {
            foreach (var p in line)
            {
                var (x, z) = ((float)p.X, (float)p.Z);
                var key = ((int)MathF.Floor(x / Cell), (int)MathF.Floor(z / Cell));
                if (!_grid.TryGetValue(key, out var cell)) _grid[key] = cell = [];
                cell.Add((x, z));
                (_minX, _maxX, _minZ, _maxZ) = (MathF.Min(_minX, x), MathF.Max(_maxX, x), MathF.Min(_minZ, z), MathF.Max(_maxZ, z));
            }
        }

        /// <summary>
        /// Whether a point is within <paramref name="reach"/> metres of the line, in plan view. A reach round the whole
        /// line (a sky dome's) is near at once; else only the cells within reach that the line can have are looked in,
        /// or every cell the line has where those would be more
        /// </summary>
        public bool Near(Vec3 at, float reach)
        {
            if (_grid.Count == 0 || !(reach > 0)) return false;
            var reach2 = reach * reach;

            var farX = MathF.Max(MathF.Abs(at.X - _minX), MathF.Abs(at.X - _maxX));
            var farZ = MathF.Max(MathF.Abs(at.Z - _minZ), MathF.Abs(at.Z - _maxZ));
            if (farX * farX + farZ * farZ < reach2) return true;

            float fromX = MathF.Max(at.X - reach, _minX), toX = MathF.Min(at.X + reach, _maxX);
            float fromZ = MathF.Max(at.Z - reach, _minZ), toZ = MathF.Min(at.Z + reach, _maxZ);
            if (!(fromX <= toX && fromZ <= toZ)) return false;

            int x0 = (int)MathF.Floor(fromX / Cell), x1 = (int)MathF.Floor(toX / Cell);
            int z0 = (int)MathF.Floor(fromZ / Cell), z1 = (int)MathF.Floor(toZ / Cell);
            if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > _grid.Count)
                return _grid.Values.Any(cell => Within(cell, at, reach2));

            for (var x = x0; x <= x1; x++)
            for (var z = z0; z <= z1; z++)
                if (_grid.TryGetValue((x, z), out var cell) && Within(cell, at, reach2)) return true;

            return false;
        }

        private static bool Within(List<(float X, float Z)> cell, Vec3 at, float reach2)
        {
            foreach (var (px, pz) in cell)
                if ((px - at.X) * (px - at.X) + (pz - at.Z) * (pz - at.Z) < reach2) return true;
            return false;
        }
    }
}
