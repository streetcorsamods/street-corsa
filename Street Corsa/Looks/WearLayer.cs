using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using AcTools.Kn5File;
using AcTools.Utils;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Looks;

/// <summary>
/// A car's wear layer: a model of its own drawn over the car's body and windscreen (<see cref="BodyShell"/>), its
/// pictures painted for a <see cref="CarLook"/> (<see cref="WearPainter"/>). The scenes put it on the car as they put the
/// parts it shows (ShownPartsOnCar), the race mode loads it into the car's body in AC (car_parts.lua); in both it sits in
/// the space of the car's model. Nothing is written into the car's own files.
///
/// Reading a car's shell takes a while (looking at the car from all round): it is kept for each model, and the last
/// layers made are kept for their looks, so a car shown again comes at once.
/// </summary>
public static class WearLayer
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Looks");

    /// <summary>
    /// The start of the name of a group in a layer's model that moves with a node of the car (a door): the node's name
    /// follows. The group stands where the node stands at rest, its meshes in the node's space, so put under the car's node
    /// of that name it goes where the node goes (ShownPartsOnCar, car_parts.lua); left where it is, it stays put.
    /// </summary>
    public const string MoverPrefix = "sc_move_";

    /// <summary>The materials and pictures in a layer's model</summary>
    public const string BodyMaterial = "sc_wear";

    public const string ScreenMaterial = "sc_cracks";

    /// <summary>The folder in the race mode a layer's model goes in</summary>
    public const string Folder = "looks";

    /// <summary>
    /// How the work of making a layer is spread: over half the cores at most, so a scene moving meanwhile (a car driving in
    /// while its wear is made) keeps the others for drawing
    /// </summary>
    internal static readonly ParallelOptions Work = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };

    /// <summary>The body picture's size, pixels a side: a car seen close (the garage, a car looked at, a race)</summary>
    public const int FullPicture = 2048;

    /// <summary>...and a car among many, seen from further off (a lot): a quarter of the work and the memory</summary>
    public const int LotPicture = 1024;

    /// <summary>How much of the layers made is kept for the next time they are asked for, bytes of their pictures</summary>
    private const long KeptBytes = 128L * 1024 * 1024;

    /// <summary>
    /// The painter's version, in a race layer's file name: a layer written by an older painter is not the one the scenes
    /// draw, so it is written again
    /// </summary>
    private const int PainterVersion = 3;

    /// <summary>
    /// The shell of each car model read so far, by its file: the same for the model a scene loaded, a race's, and one read
    /// ahead while a scene loads the car (<see cref="WarmAsync"/>); by the model itself for one with no file
    /// </summary>
    private static readonly ConcurrentDictionary<string, KeptShell> ShellsByFile = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many models' shells are kept in memory (a few MB each): a lot's worth and the garage's</summary>
    private const int KeptShells = 32;

    private sealed class KeptShell(Func<BodyShell?> make)
    {
        public readonly Lazy<BodyShell?> Shell = new(make, LazyThreadSafetyMode.ExecutionAndPublication);
        public long Asked;
    }

    private static readonly ConditionalWeakTable<IKn5, Lazy<BodyShell?>> Shells = new();

    /// <summary>
    /// The scenes' layers, each made once however many ask for it at once (a scene's ask finds the one being made ahead and
    /// waits for it, not for a second paint of its own)
    /// </summary>
    private static readonly ConcurrentDictionary<(BodyShell Shell, CarLook Look, int Picture), Kept> Layers = new();

    private sealed class Kept(Func<IKn5> make)
    {
        public readonly Lazy<IKn5> Layer = new(make, LazyThreadSafetyMode.ExecutionAndPublication);
        public long Asked;

        /// <summary>Its pictures' bytes once it is made; nothing while it is being made</summary>
        public long Bytes => Layer.IsValueCreated ? Layer.Value.TexturesData.Values.Sum(t => (long)t.Length) : 0;
    }

    private static long _asks;

    /// <summary>
    /// One layer made ahead at a time: each paint spreads over half the cores (<see cref="Work"/>), so a lot asking for
    /// twenty at once would take every core from the scene drawing meanwhile. A scene's own ask never waits here: it makes
    /// (or joins) its layer at once.
    /// </summary>
    private static readonly SemaphoreSlim Warms = new(1, 1);

    /// <summary>
    /// The layer over the car <paramref name="carModel"/> (its main model, as loaded) for <paramref name="look"/>; null
    /// when there is nothing to draw or nothing of the car is paint. Slow the first time for a model: call it off the UI
    /// thread. Never throws for what the model holds.
    /// </summary>
    public static IKn5? For(IKn5 carModel, CarLook look, int picture = FullPicture)
    {
        if (look.IsClean) return null;
        var shell = carModel.OriginalFilename is { Length: > 0 } file
            ? ShellOf(file, carModel, ShellsIn(Cache))
            : Shells.GetValue(carModel, m => new Lazy<BodyShell?>(() => Read(m, null).Shell)).Value;
        return shell == null ? null : Layer(shell, look.Rounded(), picture);
    }

    /// <summary>
    /// The main model of the car in <paramref name="carDirectory"/>, the file the scenes load (the high-resolution one
    /// when the car has it, as AcTools' CarDescription picks it): the shell made ahead or for a race is the shell of the
    /// model the scenes draw. Null when there is none.
    /// </summary>
    internal static string? ModelOf(string carDirectory) =>
        AcPaths.GetMainCarFilename(carDirectory, true) is { } file && File.Exists(file) ? file : null;

    /// <summary>
    /// Makes the layer of the car in <paramref name="carDirectory"/> for what it shows (<paramref name="shown"/>) ahead,
    /// off the UI thread, while a scene loads the car: <see cref="For"/> finds it made (or being made) when the car is up.
    /// Nothing for a car with no look, or none on disk; one still waiting its turn when <paramref name="cancel"/> is
    /// cancelled (the scene gone, its cars changed) is not made. Never throws.
    /// </summary>
    public static Task WarmAsync(string carDirectory, ShownSet? shown, int picture = FullPicture, CancellationToken cancel = default) =>
        shown?.Look is { } look && Directory.Exists(carDirectory) ? WarmAsync(carDirectory, look, picture, cancel) : Task.CompletedTask;

    private static Task WarmAsync(string carDirectory, CarLook look, int picture, CancellationToken cancel) => Task.Run(async () =>
    {
        try
        {
            if (look.IsClean || ModelOf(carDirectory) is not { } file) return;
            await Warms.WaitAsync(cancel);
            try
            {
                if (ShellOf(file, null, ShellsIn(Cache)) is { } shell) Layer(shell, look.Rounded(), picture);
            }
            finally
            {
                Warms.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Not wanted any more
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning("The wear of {Car} could not be made ahead: {Error}", Path.GetFileName(carDirectory), ex.Message);
        }
    }, CancellationToken.None);

    /// <summary>
    /// The shell of the main model of the car in <paramref name="carDirectory"/>, kept as the wear's is (what its paint is,
    /// for a paint job: <see cref="CarPaint"/>); null without a model, or nothing of it paint. Slow the first time for a
    /// model: off the UI thread.
    /// </summary>
    internal static BodyShell? ShellFor(string carDirectory) =>
        ModelOf(carDirectory) is { } file ? ShellOf(file, null, ShellsIn(Cache)) : null;

    /// <summary>Where the models' shells are kept beside a folder's layers, worked out once for a model (<see cref="ShellFile"/>)</summary>
    private static string ShellsIn(string folder) => Path.Combine(folder, "shells");

    /// <summary>How much of the shells is kept on disk, bytes: the least lately used go past it</summary>
    private const long KeptShellBytes = 150L * 1024 * 1024;

    /// <summary>
    /// The shell of the model in <paramref name="file"/>, worked out once: kept on disk (in <paramref name="shells"/>) from
    /// an earlier time, else from <paramref name="loaded"/> when given, else read, and kept. One that could not be worked
    /// out is not kept on disk: the next session tries again.
    /// </summary>
    private static BodyShell? ShellOf(string file, IKn5? loaded, string shells)
    {
        var path = Path.GetFullPath(file);
        var kept = ShellsByFile.GetOrAdd(path, f => new KeptShell(() => MakeShell(f, loaded, shells)));
        Interlocked.Exchange(ref kept.Asked, Interlocked.Increment(ref _asks));
        var shell = kept.Shell.Value;

        // The least lately asked for of too many go: read again from disk should they come back
        while (ShellsByFile.Count > KeptShells)
        {
            var oldest = ShellsByFile.Where(s => s.Value.Shell.IsValueCreated).MinBy(s => s.Value.Asked).Key;
            if (oldest == null || string.Equals(oldest, path, StringComparison.OrdinalIgnoreCase) || !ShellsByFile.TryRemove(oldest, out _)) break;
        }
        return shell;
    }

    private static BodyShell? MakeShell(string f, IKn5? loaded, string shells)
    {
        string? kept = null;
        try
        {
            kept = ShellFile.PathFor(shells, f);
            if (ShellFile.TryRead(kept, out var shell))
            {
                // Used: the last to go when the shells are pruned
                Touch(kept);
                return shell;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The folder cannot be worked out or read: the shell is read from the model and not kept
            Logger.Warning("The kept wear of {Model} could not be looked for: {Error}", Path.GetFileName(f), ex.Message);
            kept = null;
        }

        BodyShell? made;
        bool worked;
        try
        {
            (made, worked) = Read(loaded ?? Kn5.FromFile(f, SkippingTextureLoader.Instance), f);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning("The model {Model} could not be read for its wear: {Error}", f, ex.Message);
            return null;
        }

        try
        {
            if (kept != null && worked)
            {
                ShellFile.Write(kept, made);
                PruneShells(shells, kept);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning("The wear of {Model} could not be kept for next time: {Error}", Path.GetFileName(f), ex.Message);
        }
        return made;
    }

    /// <summary>
    /// The layer over the car in <paramref name="carDirectory"/> (its main model read from the file) for
    /// <paramref name="look"/>, written as a model into <paramref name="folder"/> (the model's shell kept beside it); the
    /// file's name in it, null when there is nothing to draw or the car's model cannot be read. Off the UI thread.
    /// </summary>
    public static string? WriteFor(string carDirectory, CarLook look, string folder)
    {
        if (look.IsClean || ModelOf(carDirectory) is not { } file) return null;

        var shell = ShellOf(file, null, ShellsIn(folder));
        if (shell == null) return null;

        // Named for the look and the model as it is (a car mod updated gets a new layer, as does a shell worked out another
        // way) and the painter that made it
        var rounded = look.Rounded();
        var name = $"{Path.GetFileName(carDirectory)}_{Hash(rounded, ShellFile.KeyOf(file) + "|" + PainterVersion):x8}.kn5";
        var path = Path.Combine(folder, name);

        // One already written is touched under the pruning's lock: the other car's prune for the same race finds it new and
        // leaves it, or pruned it first and it is written again here
        lock (Pruning)
        {
            if (File.Exists(path))
            {
                Touch(path);
                return name;
            }
        }

        // Not kept with the scenes' layers: a race's is written once and read from its file
        var bytes = new MemoryStream();
        Make(shell, rounded, FullPicture, race: true).Save(bytes);
        Directory.CreateDirectory(folder);
        SafeFile.WriteAllBytes(path, bytes.ToArray());
        return name;
    }

    /// <summary>Marks a kept file used now, for the pruning; one that cannot be touched is left as it is</summary>
    private static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pruned a little sooner, that is all
        }
    }

    /// <summary>The races' layers are pruned one car at a time, and a layer kept is touched in between, never during</summary>
    private static readonly object Pruning = new();

    /// <summary>A layer written or touched this lately is never pruned: a race being set up has its cars' layers named already</summary>
    private static readonly TimeSpan JustWritten = TimeSpan.FromMinutes(10);

    /// <summary>Where the races' layers are written, for the race mode to take (a few kept, the newest)</summary>
    public static string Cache => Path.Combine(Configuration.AppSettings.AppDataPath, "Looks");

    /// <summary>How many races' layers are kept written</summary>
    private const int KeptFiles = 30;

    /// <summary>
    /// <paramref name="shown"/> with its wear layer's model for a race (<see cref="ShownSet.Layer"/>), written for the car
    /// in <paramref name="carDirectory"/> off the caller's thread; without the dirt, which AC draws itself. As it was when
    /// there is no wear to draw, or the layer cannot be made: the car races without it.
    /// </summary>
    public static async Task<ShownSet?> ForRaceAsync(ShownSet? shown, string carDirectory)
    {
        if (shown?.Look is not { } look) return shown;
        try
        {
            var cache = Cache;
            var name = await Task.Run(() =>
            {
                var written = WriteFor(carDirectory, look.WithoutDirt(), cache);
                Prune(cache, written);
                return written;
            });
            return name == null ? shown : shown.WithLayer(new ShownModel(Path.Combine(Folder, name), Path.Combine(cache, name)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "The wear of {Car} could not be made for the race: it races without", Path.GetFileName(carDirectory));
            return shown;
        }
    }

    /// <summary>
    /// The oldest layers written go, but for the newest few, <paramref name="keep"/>, and any written in the last minutes
    /// (the other car of the race being set up)
    /// </summary>
    private static void Prune(string folder, string? keep)
    {
        lock (Pruning)
        {
            if (!Directory.Exists(folder)) return;
            var recent = DateTime.UtcNow - JustWritten;
            Delete(new DirectoryInfo(folder).GetFiles("*.kn5").OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeptFiles)
                .Where(f => f.LastWriteTimeUtc < recent && !string.Equals(f.Name, keep, StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>The shells used least lately go past <see cref="KeptShellBytes"/>, but for <paramref name="keep"/></summary>
    private static void PruneShells(string folder, string keep)
    {
        var bytes = 0L;
        var old = new List<FileInfo>();
        foreach (var shell in new DirectoryInfo(folder).GetFiles("*.shell").OrderByDescending(f => f.LastWriteTimeUtc))
        {
            bytes += shell.Length;
            if (bytes > KeptShellBytes && !string.Equals(shell.FullName, Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)) old.Add(shell);
        }
        Delete(old);
    }

    private static void Delete(IEnumerable<FileInfo> files)
    {
        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use (the race mode's copy is its own): tried again next time
            }
        }
    }

    /// <summary>The model's shell (null: nothing of it is paint), and whether it could be worked out at all</summary>
    private static (BodyShell? Shell, bool Worked) Read(IKn5 model, string? name)
    {
        try
        {
            var started = Environment.TickCount64;
            // What of the body the car's animations move, from its folder: a model read without a file moves nothing
            var movers = name != null && Path.GetDirectoryName(name) is { } car ? BodyShell.MoversOf(car) : null;
            var shell = BodyShell.Of(model, movers: movers);
            if (shell == null) Logger.Information("{Model}: nothing of it is paint, it shows no wear", Path.GetFileName(name));
            else
                Logger.Information("{Model}: wear drawn on {Paint} ({Meshes} mesh(es), {Ms} ms)", Path.GetFileName(name), string.Join(", ", shell.Paint),
                    shell.BodyMeshes.Count, Environment.TickCount64 - started);
            return (shell, true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "{Model}: its wear could not be worked out", Path.GetFileName(name));
            return (null, false);
        }
    }

    /// <summary>A look's figures (and <paramref name="more"/>) as a file name's part: the same look, the same name</summary>
    private static uint Hash(CarLook look, string more) =>
        StableHash.Fnv(string.Join(",", look.Rust, look.Paint, look.Cracks, look.Dirt, look.Front, look.Rear, look.Left, look.Right, look.Seed, more));

    /// <summary>
    /// A scene's layer for a look, made once while it is kept, whoever asks first making it for the rest; the least lately
    /// asked for go past <see cref="KeptBytes"/>. One that could not be made is not kept: the next ask tries again.
    /// </summary>
    private static IKn5 Layer(BodyShell shell, CarLook look, int picture)
    {
        var asked = Interlocked.Increment(ref _asks);
        var key = (shell, look, picture);
        var kept = Layers.GetOrAdd(key, k => new Kept(() => Make(k.Shell, k.Look, k.Picture, race: false)));
        Interlocked.Exchange(ref kept.Asked, asked);
        IKn5 layer;
        try
        {
            layer = kept.Layer.Value;
        }
        catch
        {
            Layers.TryRemove(new KeyValuePair<(BodyShell, CarLook, int), Kept>(key, kept));
            throw;
        }

        while (Layers.Count > 1 && Layers.Values.Sum(l => l.Bytes) > KeptBytes)
        {
            var oldest = Layers.Where(l => l.Value.Layer.IsValueCreated).MinBy(l => l.Value.Asked).Key;
            if (oldest == key || !Layers.TryRemove(oldest, out _)) break;
        }
        return layer;
    }

    /// <summary>
    /// The layer's model: the body's meshes with the wear picture, the windscreen's with the cracks when it has any. A race's
    /// pictures are PNGs (a file to keep: small); a scene's are DDS with their mips, nothing to decode as they load
    /// </summary>
    private static IKn5 Make(BodyShell shell, CarLook look, int picture, bool race)
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = "sc_look";

        // The painter's pictures are lent (a picture's worth of garbage a car would land a collection mid-scene): encoded
        // and given back
        byte[] Encode(byte[] rgba, int w, int h)
        {
            try
            {
                return race ? Png.Encode(rgba, w, h) : Dds.Encode(rgba, w, h);
            }
            finally
            {
                WearPainter.Return(rgba);
            }
        }

        var bodyId = Material(kn5, BodyMaterial, Encode(WearPainter.Body(shell, look, picture), picture, picture), shell, race);
        var k = 0;
        foreach (var mesh in shell.BodyMeshes) Add(kn5, $"sc_wear_{k++}", mesh, bodyId);

        if (look.Cracks > 0.01 && shell.ScreenMeshes.Count > 0)
        {
            var screenId = Material(kn5, ScreenMaterial, Encode(WearPainter.Screen(look), WearPainter.ScreenWidth, WearPainter.ScreenHeight), shell, race);
            k = 0;
            foreach (var mesh in shell.ScreenMeshes) Add(kn5, $"sc_cracks_{k++}", mesh, screenId);
        }
        return kn5;
    }

    /// <summary>
    /// A mesh into the layer's model: under the root, or, for one that moves with a node of the car, into that node's group
    /// (<see cref="MoverPrefix"/>), which stands where the node stands at rest. A moving part whose rest cannot be undone
    /// (a node scaled flat) stays under the root, in the model's space, where it stands at rest.
    /// </summary>
    private static void Add(IKn5 kn5, string name, BodyShell.Mesh mesh, uint material)
    {
        if (mesh.Mover is not { } mover || !Matrix4x4.Invert(mesh.Rest, out var toNode))
        {
            kn5.RootNode.Children.Add(Node(name, mesh.Vertices, mesh.Indices, material));
            return;
        }

        var groupName = MoverPrefix + mover;
        var group = kn5.RootNode.Children.FirstOrDefault(n => n.NodeClass == Kn5NodeClass.Base && n.Name == groupName);
        if (group == null)
        {
            group = Kn5Node.CreateBaseNode(groupName);
            group.Transform = Matrices.ToMat4x4(mesh.Rest);
            kn5.RootNode.Children.Add(group);
        }
        group.Children.Add(Node(name, InNode(mesh.Vertices, mesh.Rest, toNode), mesh.Indices, material));
    }

    /// <summary>
    /// A moving part's vertices in the space of the node it moves with: its points and tangents by
    /// <paramref name="toNode"/>, its normals by that inverted and turned over (<paramref name="rest"/> transposed), so a
    /// node scaled unevenly leaves them square to the panel
    /// </summary>
    private static Kn5Node.Vertex[] InNode(Kn5Node.Vertex[] source, Matrix4x4 rest, Matrix4x4 toNode)
    {
        var forNormals = Matrix4x4.Transpose(rest);
        var vertices = new Kn5Node.Vertex[source.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var v = source[i];
            var p = Vector3.Transform(new Vector3(v.Position.X, v.Position.Y, v.Position.Z), toNode);
            var n = Vector3.Normalize(Vector3.TransformNormal(new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z), forNormals));
            var t = Vector3.Normalize(Vector3.TransformNormal(new Vector3(v.Tangent.X, v.Tangent.Y, v.Tangent.Z), toNode));
            vertices[i] = new Kn5Node.Vertex(new AcTools.Numerics.Vec3(p.X, p.Y, p.Z), new AcTools.Numerics.Vec3(n.X, n.Y, n.Z), v.Tex,
                new AcTools.Numerics.Vec3(t.X, t.Y, t.Z));
        }
        return vertices;
    }

    /// <summary>
    /// A see-through material lit as the car's paint is lit, matt (rust and dirt do not shine), its picture's alpha what
    /// covers the paint; returns its id
    /// </summary>
    private static uint Material(IKn5 kn5, string name, byte[] picture, BodyShell shell, bool race)
    {
        var texture = name + (race ? ".png" : ".dds");
        kn5.Textures[texture] = new Kn5Texture { Name = texture, Active = true, Length = picture.Length };
        kn5.TexturesData[texture] = picture;
        kn5.Materials[name] = new Kn5Material
        {
            Name = name,
            ShaderName = "ksPerPixel",
            BlendMode = Kn5MaterialBlendMode.AlphaBlend,
            AlphaTested = false,
            DepthMode = Kn5MaterialDepthMode.DepthNoWrite,
            ShaderProperties =
            [
                new Kn5Material.ShaderProperty { Name = "ksAmbient", ValueA = shell.Ambient },
                new Kn5Material.ShaderProperty { Name = "ksDiffuse", ValueA = shell.Diffuse },
                new Kn5Material.ShaderProperty { Name = "ksSpecular", ValueA = name == ScreenMaterial ? 0.3f : 0.05f },
                new Kn5Material.ShaderProperty { Name = "ksSpecularEXP", ValueA = name == ScreenMaterial ? 40f : 8f },
                new Kn5Material.ShaderProperty { Name = "ksEmissive", ValueC = new AcTools.Numerics.Vec3(0, 0, 0) },
                new Kn5Material.ShaderProperty { Name = "ksAlphaRef", ValueA = 0 }
            ],
            TextureMappings = [new Kn5Material.TextureMapping { Name = "txDiffuse", Texture = texture, Slot = 0 }]
        };
        return (uint)kn5.Materials.Keys.ToList().IndexOf(name);
    }

    private static Kn5Node Node(string name, Kn5Node.Vertex[] vertices, ushort[] indices, uint material)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in vertices)
        {
            var p = new Vector3(v.Position.X, v.Position.Y, v.Position.Z);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        var middle = (min + max) / 2;
        return new Kn5Node
        {
            NodeClass = Kn5NodeClass.Mesh,
            Name = name,
            Active = true,
            Children = [],
            // Its own shadow would fall on the paint under it, a hair away
            CastShadows = false,
            IsVisible = true,
            IsRenderable = true,
            IsTransparent = true,
            Vertices = vertices,
            Indices = indices,
            MaterialId = material,
            BoundingSphereCenter = new AcTools.Numerics.Vec3(middle.X, middle.Y, middle.Z),
            BoundingSphereRadius = Vector3.Distance(min, max) / 2
        };
    }
}
