using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using AcTools.Kn5File;
using StreetCorsa.Logging;
using StreetCorsa.Parts.Cars;
using static StreetCorsa.Helpers.Matrices;

namespace StreetCorsa.Looks;

/// <summary>
/// Where a car's wear can show: the outside of its painted body and its windscreen, read from its main model once. What
/// is the body is not in a name (a mod calls its paint "body", "EXT_Car_Paint", "c1" or "Material") nor in the shader
/// (four in five of the game's cars have no dirt shader), so it is found by looking: the car is drawn from all round in a
/// depth buffer of its own, the triangles seen from outside kept, and the materials that cover most of what is seen (but
/// for glass, tyres, rims, chrome, lights and the like) are the paint. Only the seen triangles of those carry wear: an
/// inner door panel or a dashboard in the paint's material stays clean.
///
/// Each triangle is laid on one of six flat views of the car (its sides, front, back, top and bottom), by the way it
/// faces, so the picture of the wear is the car seen from those sides: a patch of rust low on the door is low on the door
/// in the picture, whatever the model's own texture layout. Everything here is in the space of the model (its KN5, root
/// included), where AC and the scenes put the layer.
/// </summary>
internal sealed class BodyShell
{
    /// <summary>
    /// The way a shell is worked out: up by one whenever what <see cref="Of"/> picks changes, so the shells kept on disk
    /// (<see cref="ShellFile"/>) and the races' layers worked out the old way are worked out again
    /// </summary>
    public const int Version = 4;

    /// <summary>How far the layer stands off the body, metres: enough that the two never fight over a pixel</summary>
    public const float StandOff = 0.0025f;

    /// <summary>One of the six views a triangle is laid on</summary>
    public enum Side { Left, Right, Front, Back, Top, Bottom }

    /// <summary>Where a view lies in the picture, in pixels, and what in the model it shows (two of x, y, z)</summary>
    public sealed record Panel(Side Side, int X, int Y, int Width, int Height, float MinA, float MaxA, float MinB, float MaxB);

    /// <summary>
    /// A mesh of the layer: its vertices already in the model's space with their place in the picture. A mesh of a part of
    /// the body that a car's animation moves (a door, the hood, the trunk, pop-up lamps) names the node it moves with
    /// (<paramref name="Mover"/>) and where that node stands at rest in the model's space (<paramref name="Rest"/>), so its
    /// wear can go with it; null for the body that stays put.
    /// </summary>
    public sealed record Mesh(Kn5Node.Vertex[] Vertices, ushort[] Indices, string? Mover = null, Matrix4x4 Rest = default);

    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }

    /// <summary>+1 when the model's nose is at +z, -1 at -z; +1 when its left is at +x</summary>
    public required float FrontSign { get; init; }

    public required float LeftSign { get; init; }

    /// <summary>The wheels' middles, AC's order; empty when the model has no wheel nodes</summary>
    public required IReadOnlyList<Vector3> Hubs { get; init; }

    /// <summary>The body's picture: its size (square) and the six views on it</summary>
    public required int PictureSize { get; init; }

    public required IReadOnlyList<Panel> Panels { get; init; }

    public required IReadOnlyList<Mesh> BodyMeshes { get; init; }

    /// <summary>The windscreen's meshes, laid on the front view of its own box (<see cref="ScreenMin"/>..<see cref="ScreenMax"/>)</summary>
    public required IReadOnlyList<Mesh> ScreenMeshes { get; init; }

    public required Vector2 ScreenMin { get; init; }
    public required Vector2 ScreenMax { get; init; }

    /// <summary>How the paint takes the light (its ksAmbient, ksDiffuse), for the layer to take it the same</summary>
    public required float Ambient { get; init; }

    public required float Diffuse { get; init; }

    /// <summary>The paint's material names, for the log</summary>
    public required IReadOnlyList<string> Paint { get; init; }

    public Vector3 Size => Max - Min;

    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Looks");

    /// <summary>Wheel groups: what is under them turns or steers, and is no body</summary>
    private static readonly Regex WheelGroup = new("^(WHEEL|SUSP|HUB|DISC)_(LF|RF|LR|RR)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Materials that are never the paint, by a word in their name</summary>
    private static readonly Regex NotPaint = new(
        "glass|window|screen|shield|lens|light|lamp|tyre|tire|rim|wheel|chrome|chrom|grill|badge|logo|emblem|decal|plate|interior|^int_|_int_|seat|dash|carbon|exhaust|mirror|shadow|black|rubber|plastic|engine|brake|disc|caliper|gauge|steer|leather|leat|alcantara|upholst|fabric|cloth|cockpit|cpit|carpet|headliner|ao_",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Glass = new("glass|window|windscreen|windshield|vetro", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A material drawn see-through: glass, or a decal</summary>
    private static bool SeeThrough(Kn5Material m) =>
        m.BlendMode == Kn5MaterialBlendMode.AlphaBlend || m.ShaderName.Contains("Windscreen", StringComparison.OrdinalIgnoreCase)
        || m.ShaderName.Contains("Alpha", StringComparison.OrdinalIgnoreCase);

    private static bool IsGlass(Kn5Material m) =>
        m.ShaderName.Contains("Windscreen", StringComparison.OrdinalIgnoreCase) || Glass.IsMatch(m.Name ?? "");

    /// <summary>A triangle of the model in its space, with the mesh and corners it came from</summary>
    private struct Tri
    {
        public Vector3 A, B, C;
        public int Mesh;
        public int I0, I1, I2;
        public uint Material;
        public bool Wheel;
        public bool Seen;

        /// <summary>The looks (<see cref="Looks"/>) it was seen from, one bit each</summary>
        public int SeenFrom;
    }

    private sealed record SourceMesh(Kn5Node Node, Matrix4x4 ToModel, bool UnderWheel, string? Mover, Matrix4x4 MoverRest);

    /// <summary>
    /// The shell of <paramref name="kn5"/>; null when nothing of it is paint. <paramref name="movers"/> are the nodes the
    /// car's animations move (<see cref="MoversOf"/>): what of the body is under one moves with it.
    /// </summary>
    public static BodyShell? Of(IKn5 kn5, int pictureSize = 2048, IReadOnlySet<string>? movers = null)
    {
        var meshes = new List<SourceMesh>();
        Walk(kn5.RootNode, Matrix4x4.Identity, false, null, default, movers ?? new HashSet<string>(), meshes);
        var materials = kn5.Materials.Values.ToList();
        Kn5Material? MaterialOf(uint id) => id < materials.Count ? materials[(int)id] : null;

        // Every triangle drawn, in the model's space
        var tris = new List<Tri>();
        var hubs = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        CollectHubs(kn5.RootNode, Matrix4x4.Identity, hubs);
        var positions = new List<Vector3[]>();
        for (var m = 0; m < meshes.Count; m++)
        {
            var (node, toModel, underWheel, _, _) = meshes[m];
            var p = node.Vertices.Select(v => Vector3.Transform(new Vector3(v.Position.X, v.Position.Y, v.Position.Z), toModel)).ToArray();
            positions.Add(p);
            var indices = node.Indices;
            for (var t = 0; t + 2 < indices.Length; t += 3)
                tris.Add(new Tri
                {
                    A = p[indices[t]], B = p[indices[t + 1]], C = p[indices[t + 2]], Mesh = m,
                    I0 = indices[t], I1 = indices[t + 1], I2 = indices[t + 2], Material = node.MaterialId, Wheel = underWheel
                });
        }
        if (tris.Count == 0) return null;

        // The car's box: what is not see-through (a shadow plane under the car is see-through)
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var t in tris)
        {
            if (MaterialOf(t.Material) is { } mat && SeeThrough(mat)) continue;
            min = Vector3.Min(min, Vector3.Min(t.A, Vector3.Min(t.B, t.C)));
            max = Vector3.Max(max, Vector3.Max(t.A, Vector3.Max(t.B, t.C)));
        }
        if (min.X > max.X) return null;

        // Glass hides what is behind it as the paint does: the cabin seen through the windows is not outside, and its seats
        // and panels in a material no word catches would pass for paint
        See(tris, min, max);

        // What is seen of each material, by area
        var seenArea = new Dictionary<uint, float>();
        foreach (var t in tris)
        {
            if (!t.Seen || t.Wheel) continue;
            seenArea[t.Material] = seenArea.GetValueOrDefault(t.Material) + Area(t);
        }

        var candidates = seenArea.Where(a => MaterialOf(a.Key) is { } mat && !SeeThrough(mat) && !NotPaint.IsMatch(mat.Name ?? ""))
            .OrderByDescending(a => a.Value).ToList();
        var dirtShader = seenArea.Keys.Where(k => MaterialOf(k) is { } mat && mat.ShaderName.Contains("dirt", StringComparison.OrdinalIgnoreCase)
                                                  && !SeeThrough(mat) && !NotPaint.IsMatch(mat.Name ?? "")).ToHashSet();
        if (candidates.Count == 0) return null;
        var most = candidates[0].Value;
        var paint = candidates.Where(c => c.Value >= most * 0.2f || dirtShader.Contains(c.Key)).Select(c => c.Key).ToHashSet();
        var first = MaterialOf(candidates[0].Key)!;

        var frontSign = 1f;
        var leftSign = 1f;
        var hubList = new List<Vector3>();
        if (WheelFit.WheelNodes.All(hubs.ContainsKey))
        {
            hubList.AddRange(WheelFit.WheelNodes.Select(n => hubs[n]));
            frontSign = hubs["WHEEL_LF"].Z >= hubs["WHEEL_LR"].Z ? 1f : -1f;
            leftSign = hubs["WHEEL_LF"].X >= hubs["WHEEL_RF"].X ? 1f : -1f;
        }

        var panels = Layout(min, max, pictureSize);
        var body = Build(tris.Where(t => t.Seen && !t.Wheel && paint.Contains(t.Material)), meshes, positions,
            (t, n) => Place(panels, pictureSize, SideOf(n, frontSign, leftSign), t));

        // The windscreen: the cabin's glass, seen from ahead of the car, that faces forwards, above the hood (headlamp
        // lenses face forwards too, lower down). Which way a pane faces is away from the middle of the cabin's glass: in a
        // mod neither a face's winding nor its normals are to be trusted (many point the glass's normals inwards, and the
        // rear window passes for the windscreen by them)
        var screenFloor = min.Y + (max.Y - min.Y) * 0.55f;
        var fromAhead = LooksFrom(new Vector3(0, 0, frontSign));

        // Glass by its name or shader; for a model whose glass is "Material #53", whatever is see-through there
        List<Tri> ScreenBy(Func<Kn5Material, bool> glass)
        {
            var panes = tris.Where(t => t.Seen && !t.Wheel && MaterialOf(t.Material) is { } mat && glass(mat)
                                        && (t.A.Y + t.B.Y + t.C.Y) / 3 > screenFloor).ToList();
            var cabin = Vector3.Zero;
            var area = 0f;
            foreach (var t in panes)
            {
                cabin += (t.A + t.B + t.C) / 3 * Area(t);
                area += Area(t);
            }
            if (area <= 0) return [];
            cabin /= area;
            return MainBand(panes.Where(t => (t.SeenFrom & fromAhead) != 0 && Outward(t, cabin).Z * frontSign > 0.25f).ToList(), min.Z, max.Z);
        }
        var screenTris = ScreenBy(IsGlass);
        if (screenTris.Count == 0) screenTris = ScreenBy(SeeThrough);
        var screenMin = new Vector2(float.MaxValue);
        var screenMax = new Vector2(float.MinValue);
        foreach (var t in screenTris)
            foreach (var p in new[] { t.A, t.B, t.C })
            {
                screenMin = Vector2.Min(screenMin, new Vector2(p.X, p.Y));
                screenMax = Vector2.Max(screenMax, new Vector2(p.X, p.Y));
            }
        var screen = screenTris.Count == 0 ? [] : Build(screenTris, meshes, positions, (p, _) =>
            new Vector2((p.X - screenMin.X) / Math.Max(1e-3f, screenMax.X - screenMin.X), 1 - (p.Y - screenMin.Y) / Math.Max(1e-3f, screenMax.Y - screenMin.Y)));

        return new BodyShell
        {
            Min = min, Max = max, FrontSign = frontSign, LeftSign = leftSign, Hubs = hubList, PictureSize = pictureSize, Panels = panels,
            BodyMeshes = body, ScreenMeshes = screen, ScreenMin = screenMin, ScreenMax = screenMax,
            Ambient = first.GetPropertyByName("ksAmbient")?.ValueA ?? 0.4f,
            Diffuse = first.GetPropertyByName("ksDiffuse")?.ValueA ?? 0.4f,
            Paint = paint.Select(id => MaterialOf(id)?.Name ?? "?").ToList()
        };
    }

    private static void Walk(Kn5Node node, Matrix4x4 parent, bool underWheel, string? mover, Matrix4x4 moverRest,
        IReadOnlySet<string> movers, List<SourceMesh> meshes)
    {
        if (!node.Active) return;
        if (node.NodeClass == Kn5NodeClass.Base)
        {
            var local = ToNumerics(node.Transform) * parent;
            var wheel = underWheel || WheelGroup.IsMatch(node.Name ?? "");
            // The nearest node an animation moves is what the meshes under it move with
            if (node.Name is { } name && movers.Contains(name)) (mover, moverRest) = (name, local);
            foreach (var child in node.Children) Walk(child, local, wheel, mover, moverRest, movers, meshes);
        }
        else if (node.IsVisible && node.IsRenderable && node.Vertices is { Length: > 0 } && node.Indices is { Length: > 0 })
        {
            meshes.Add(new SourceMesh(node, parent, underWheel, mover, moverRest));
        }
    }

    /// <summary>
    /// The nodes the car in <paramref name="carDirectory"/> moves by its animations (its animations folder's .ksanim
    /// files: doors, hood, trunk, pop-up lamps, wings): empty for a car with none, or none that read, or a folder that
    /// cannot be listed
    /// </summary>
    public static IReadOnlySet<string> MoversOf(string carDirectory)
    {
        var movers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Animations(carDirectory) ?? [])
        {
            try
            {
                foreach (var name in AcTools.KsAnimFile.KsAnim.FromFile(file).Entries.Keys) movers.Add(name);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // An animation that does not read moves nothing here
            }
        }
        return movers;
    }

    /// <summary>What of the car's animations the shell depends on, for the name of a shell kept on disk: their names and sizes</summary>
    public static string MoversStamp(string carDirectory)
    {
        // A folder that cannot be listed now has a stamp of its own: the shell made without its movers is not taken for
        // the one made once it lists again
        if (Animations(carDirectory) is not { } files) return "unlisted";
        try
        {
            return string.Join(";", files.Select(f => new FileInfo(f))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f => $"{f.Name.ToLowerInvariant()}:{f.Length}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "unlisted";
        }
    }

    /// <summary>The car's .ksanim files: none without an animations folder, null when it cannot be listed (its rights, a mod manager replacing it)</summary>
    private static string[]? Animations(string carDirectory)
    {
        var folder = Path.Combine(carDirectory, "animations");
        try
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.ksanim") : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning("The animations of {Car} could not be listed, its wear moves with nothing: {Error}", carDirectory, ex.Message);
            return null;
        }
    }

    private static void CollectHubs(Kn5Node node, Matrix4x4 parent, Dictionary<string, Vector3> hubs)
    {
        if (node.NodeClass != Kn5NodeClass.Base) return;
        var local = ToNumerics(node.Transform) * parent;
        if (node.Name is { } name && WheelFit.WheelNodes.Contains(name)) hubs.TryAdd(name, local.Translation);
        foreach (var child in node.Children) CollectHubs(child, local, hubs);
    }

    private static float Area(Tri t) => Vector3.Cross(t.B - t.A, t.C - t.A).Length() / 2;

    /// <summary>How finely the car's length is cut to find the windscreen's band, a share of it</summary>
    private const float BandShare = 0.04f;

    /// <summary>
    /// Of the glass facing forwards, the windscreen's: the run of it along the car's length (no gap of a band) that holds the
    /// most of its area. Headlamp lenses standing high at the nose, a light bar or a rear spoiler's glass are runs of their own.
    /// </summary>
    private static List<Tri> MainBand(List<Tri> panes, float minZ, float maxZ)
    {
        if (panes.Count == 0) return panes;
        var bands = (int)MathF.Ceiling(1 / BandShare);
        int BandOf(Tri t) => Math.Clamp((int)(((t.A.Z + t.B.Z + t.C.Z) / 3 - minZ) / Math.Max(1e-3f, maxZ - minZ) / BandShare), 0, bands - 1);
        var areas = new float[bands];
        foreach (var t in panes) areas[BandOf(t)] += Area(t);

        // The runs of bands with glass in them; the one with the most area
        var (bestFrom, bestTo, bestArea) = (0, -1, -1f);
        for (var from = 0; from < bands;)
        {
            if (areas[from] <= 0)
            {
                from++;
                continue;
            }
            var to = from;
            var sum = 0f;
            while (to < bands && areas[to] > 0) sum += areas[to++];
            if (sum > bestArea) (bestFrom, bestTo, bestArea) = (from, to - 1, sum);
            from = to;
        }
        return panes.Where(t => BandOf(t) >= bestFrom && BandOf(t) <= bestTo).ToList();
    }

    /// <summary>The way a face looks out of <paramref name="inside"/>: its normal, turned round when it points in</summary>
    private static Vector3 Outward(Tri t, Vector3 inside)
    {
        var n = FaceNormal(t);
        return Vector3.Dot(n, (t.A + t.B + t.C) / 3 - inside) < 0 ? -n : n;
    }

    private static Vector3 FaceNormal(Tri t)
    {
        var n = Vector3.Cross(t.B - t.A, t.C - t.A);
        var length = n.Length();
        return length > 1e-12f ? n / length : Vector3.UnitY;
    }

    #region Seen from outside

    /// <summary>How fine the depth buffer of each look at the car is, pixels a side</summary>
    private const int SeeResolution = 640;

    /// <summary>A face turned less than this towards a look (the cosine) is seen edge on from it</summary>
    private const float EdgeOn = 0.12f;

    /// <summary>How much nearer than what the depth buffer holds a triangle may be and still count as seen, metres</summary>
    private const float SeeSlack = 0.012f;

    /// <summary>
    /// Marks the triangles seen from outside the car: it is looked at from 18 ways round (sides, ends, above, and between
    /// them), each time every triangle drawn into a depth buffer, then each triangle asked whether its middle or a point
    /// near a corner is at the front there. A triangle too small to fill a pixel is still asked, so the fine parts of a
    /// mesh are not lost. Every triangle hides what is behind it, glass too: what is seen only through a window is inside.
    /// </summary>
    private static void See(List<Tri> tris, Vector3 min, Vector3 max)
    {
        var middle = (min + max) / 2;
        var radius = (max - min).Length() / 2 + 0.05f;
        var array = tris.ToArray();
        var seen = new int[array.Length];
        // The looks at once, each with a depth buffer of its own: a triangle any of them sees is seen
        Parallel.For(0, Looks.Length, WearLayer.Work, k =>
        {
            var look = Looks[k];
            var bit = 1 << k;
            var depth = System.Buffers.ArrayPool<float>.Shared.Rent(SeeResolution * SeeResolution);
            // The buffer's axes: across and up as seen along the look
            var up = MathF.Abs(look.Y) > 0.9f ? Vector3.UnitZ : Vector3.UnitY;
            var across = Vector3.Normalize(Vector3.Cross(up, look));
            var upward = Vector3.Cross(look, across);
            var scale = SeeResolution / (2 * radius);
            Vector3 Project(Vector3 p)
            {
                var d = p - middle;
                return new Vector3((Vector3.Dot(d, across) + radius) * scale, (Vector3.Dot(d, upward) + radius) * scale, Vector3.Dot(d, look));
            }

            Array.Fill(depth, float.MaxValue, 0, SeeResolution * SeeResolution);
            for (var i = 0; i < array.Length; i++)
                Raster(depth, Project(array[i].A), Project(array[i].B), Project(array[i].C));

            for (var i = 0; i < array.Length; i++)
            {
                // A face seen edge on is not seen (it would find the empty pixels along the car's outline); either way
                // round, since a mod's faces do not always wind the same way
                if (MathF.Abs(Vector3.Dot(FaceNormal(array[i]), look)) < EdgeOn) continue;
                var a = Project(array[i].A);
                var b = Project(array[i].B);
                var c = Project(array[i].C);
                var mid = (a + b + c) / 3;
                if (Front(depth, mid) || Front(depth, Vector3.Lerp(mid, a, 0.8f)) || Front(depth, Vector3.Lerp(mid, b, 0.8f)) || Front(depth, Vector3.Lerp(mid, c, 0.8f)))
                    Interlocked.Or(ref seen[i], bit);
            }
            System.Buffers.ArrayPool<float>.Shared.Return(depth);
        });

        for (var i = 0; i < array.Length; i++) tris[i] = array[i] with { Seen = seen[i] != 0, SeenFrom = seen[i] };
    }

    /// <summary>The ways the car is looked at (where each look goes): round it level and from above, and down on it</summary>
    private static readonly Vector3[] Looks = MakeLooks();

    private static Vector3[] MakeLooks()
    {
        var looks = new List<Vector3>();
        foreach (var y in new[] { 0f, 0.6f })
            for (var k = 0; k < 8; k++)
            {
                var a = k * MathF.PI / 4;
                looks.Add(Vector3.Normalize(new Vector3(MathF.Cos(a), -y, MathF.Sin(a))));
            }
        looks.Add(-Vector3.UnitY);
        looks.Add(Vector3.Normalize(new Vector3(0.3f, -1f, 0.2f)));
        return looks.ToArray();
    }

    /// <summary>The looks from the side <paramref name="side"/> points to (from ahead of the car, say), as bits</summary>
    private static int LooksFrom(Vector3 side)
    {
        var bits = 0;
        // A look from that side goes the other way, back across the car
        for (var k = 0; k < Looks.Length; k++)
            if (Vector3.Dot(Looks[k], side) < -0.3f) bits |= 1 << k;
        return bits;
    }

    private static bool Front(float[] depth, Vector3 p)
    {
        var x = (int)p.X;
        var y = (int)p.Y;
        if (x < 0 || y < 0 || x >= SeeResolution || y >= SeeResolution) return false;
        return p.Z <= depth[y * SeeResolution + x] + SeeSlack;
    }

    /// <summary>Draws a triangle's depth, nearest kept, at the pixels whose middles it covers</summary>
    private static void Raster(float[] depth, Vector3 a, Vector3 b, Vector3 c)
    {
        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        var maxX = Math.Min(SeeResolution - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        var maxY = Math.Min(SeeResolution - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        var area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (MathF.Abs(area) < 1e-9f) return;
        var inverse = 1 / area;
        for (var y = minY; y <= maxY; y++)
        {
            var py = y + 0.5f;
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5f;
                var w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) * inverse;
                var w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) * inverse;
                var w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                var z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
                ref var d = ref depth[y * SeeResolution + x];
                if (z < d) d = z;
            }
        }
    }

    #endregion

    #region The picture's views

    /// <summary>The view a triangle facing <paramref name="n"/> is laid on: the axis it faces most along</summary>
    public static Side SideOf(Vector3 n, float frontSign, float leftSign)
    {
        var ax = MathF.Abs(n.X);
        var ay = MathF.Abs(n.Y);
        var az = MathF.Abs(n.Z);
        if (ax >= ay && ax >= az) return n.X * leftSign > 0 ? Side.Left : Side.Right;
        if (az >= ay) return n.Z * frontSign > 0 ? Side.Front : Side.Back;
        return n.Y > 0 ? Side.Top : Side.Bottom;
    }

    /// <summary>
    /// The six views on a square picture: the two sides, the top and the bottom one above the other (each the car's length
    /// wide), the front and the back beside them (its width wide), all at one scale, a few pixels apart
    /// </summary>
    private static List<Panel> Layout(Vector3 min, Vector3 max, int size)
    {
        const int gap = 6;
        var length = max.Z - min.Z;
        var width = max.X - min.X;
        var height = max.Y - min.Y;
        var scale = MathF.Min((size - 3 * gap) / (length + width), (size - 5 * gap) / (2 * height + 2 * width));
        int Px(float metres) => Math.Max(1, (int)MathF.Floor(metres * scale));

        var panels = new List<Panel>();
        var y = gap;
        panels.Add(new Panel(Side.Left, gap, y, Px(length), Px(height), min.Z, max.Z, min.Y, max.Y));
        y += Px(height) + gap;
        panels.Add(new Panel(Side.Right, gap, y, Px(length), Px(height), min.Z, max.Z, min.Y, max.Y));
        y += Px(height) + gap;
        panels.Add(new Panel(Side.Top, gap, y, Px(length), Px(width), min.Z, max.Z, min.X, max.X));
        y += Px(width) + gap;
        panels.Add(new Panel(Side.Bottom, gap, y, Px(length), Px(width), min.Z, max.Z, min.X, max.X));
        var x = 2 * gap + Px(length);
        panels.Add(new Panel(Side.Front, x, gap, Px(width), Px(height), min.X, max.X, min.Y, max.Y));
        panels.Add(new Panel(Side.Back, x, 2 * gap + Px(height), Px(width), Px(height), min.X, max.X, min.Y, max.Y));
        return panels;
    }

    /// <summary>The picture's coordinates (0..1) of a point of the model laid on a view</summary>
    private static Vector2 Place(IReadOnlyList<Panel> panels, int size, Side side, Vector3 p)
    {
        var panel = panels.First(q => q.Side == side);
        var (a, b) = Coordinates(side, p);
        var u = (a - panel.MinA) / Math.Max(1e-4f, panel.MaxA - panel.MinA);
        var v = (b - panel.MinB) / Math.Max(1e-4f, panel.MaxB - panel.MinB);
        // Up is up in the picture of a side or an end; across the car is down the picture of the top and the bottom
        var px = panel.X + Math.Clamp(u, 0, 1) * panel.Width;
        var py = side is Side.Top or Side.Bottom ? panel.Y + Math.Clamp(v, 0, 1) * panel.Height : panel.Y + (1 - Math.Clamp(v, 0, 1)) * panel.Height;
        return new Vector2(px / size, py / size);
    }

    /// <summary>The two of x, y, z a view shows</summary>
    public static (float A, float B) Coordinates(Side side, Vector3 p) => side switch
    {
        Side.Left or Side.Right => (p.Z, p.Y),
        Side.Front or Side.Back => (p.X, p.Y),
        _ => (p.Z, p.X)
    };

    #endregion

    /// <summary>
    /// The layer's meshes for <paramref name="tris"/>: their corners copied, stood off the body along their normals, each
    /// given its place in the picture (a corner shared by triangles laid on two views is copied once for each), cut into
    /// meshes small enough for a KN5's 16-bit indices
    /// </summary>
    private static List<Mesh> Build(IEnumerable<Tri> tris, List<SourceMesh> meshes, List<Vector3[]> positions, Func<Vector3, Vector3, Vector2> uvOf)
    {
        var result = new List<Mesh>();
        var vertices = new List<Kn5Node.Vertex>();
        var indices = new List<ushort>();
        var map = new Dictionary<(int Mesh, int Index, int Side), ushort>();

        string? mover = null;
        var rest = Matrix4x4.Identity;
        void Flush()
        {
            if (indices.Count > 0) result.Add(new Mesh(vertices.ToArray(), indices.ToArray(), mover, mover == null ? default : rest));
            vertices.Clear();
            indices.Clear();
            map.Clear();
        }

        // A mesh for each part that moves, and for the body: one moving part's wear goes with it alone
        foreach (var t in tris.OrderBy(t => meshes[t.Mesh].Mover, StringComparer.Ordinal))
        {
            var source = meshes[t.Mesh];
            if (vertices.Count > 65000 || source.Mover != mover)
            {
                Flush();
                (mover, rest) = (source.Mover, source.MoverRest);
            }
            var face = FaceNormal(t);
            var (node, toModel, _, _, _) = source;
            // The way the corners face decides the view, so a smooth panel is laid on one view all over, not split by its facets
            var corners = new[] { t.I0, t.I1, t.I2 };
            var normals = corners.Select(i => NormalOf(node.Vertices[i], toModel, face)).ToArray();
            var mean = normals[0] + normals[1] + normals[2];
            var key = (int)SideOf(mean.LengthSquared() > 1e-8f ? mean : face, 1, 1);
            for (var k = 0; k < 3; k++)
            {
                if (!map.TryGetValue((t.Mesh, corners[k], key), out var index))
                {
                    var p = positions[t.Mesh][corners[k]];
                    var n = normals[k];
                    var uv = uvOf(p, mean.LengthSquared() > 1e-8f ? mean : face);
                    var at = p + n * StandOff;
                    index = (ushort)vertices.Count;
                    vertices.Add(new Kn5Node.Vertex(new AcTools.Numerics.Vec3(at.X, at.Y, at.Z), new AcTools.Numerics.Vec3(n.X, n.Y, n.Z),
                        new AcTools.Numerics.Vec2(uv.X, uv.Y), Tangent(n)));
                    map[(t.Mesh, corners[k], key)] = index;
                }
                indices.Add(index);
            }
        }
        Flush();
        return result;
    }

    private static Vector3 NormalOf(Kn5Node.Vertex v, Matrix4x4 toModel, Vector3 face)
    {
        var n = Vector3.TransformNormal(new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z), toModel);
        var length = n.Length();
        return length > 1e-6f ? n / length : face;
    }

    private static AcTools.Numerics.Vec3 Tangent(Vector3 n)
    {
        var t = Vector3.Cross(MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX, n);
        t = t.LengthSquared() > 1e-8f ? Vector3.Normalize(t) : Vector3.UnitX;
        return new AcTools.Numerics.Vec3(t.X, t.Y, t.Z);
    }
}
