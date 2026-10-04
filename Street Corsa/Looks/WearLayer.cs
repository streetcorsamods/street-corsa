using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using AcTools.Kn5File;
using AcTools.Utils;
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
    private const long KeptBytes = 200L * 1024 * 1024;

    /// <summary>
    /// The shell of each car model read so far, by its file: the same for the model a scene loaded, a race's, and one read
    /// ahead while a scene loads the car (<see cref="WarmAsync"/>); by the model itself for one with no file
    /// </summary>
    private static readonly ConcurrentDictionary<string, Lazy<BodyShell?>> ShellsByFile = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConditionalWeakTable<IKn5, Lazy<BodyShell?>> Shells = new();

    private static readonly ConcurrentDictionary<(BodyShell Shell, CarLook Look, int Picture, bool Race), (IKn5 Layer, long Asked, long Bytes)> Layers = new();

    private static long _asks;

    /// <summary>
    /// The layer over the car <paramref name="carModel"/> (its main model, as loaded) for <paramref name="look"/>; null
    /// when there is nothing to draw or nothing of the car is paint. Slow the first time for a model: call it off the UI
    /// thread. Never throws for what the model holds.
    /// </summary>
    public static IKn5? For(IKn5 carModel, CarLook look, int picture = FullPicture)
    {
        if (look.IsClean) return null;
        var shell = carModel.OriginalFilename is { Length: > 0 } file
            ? ShellOf(file, carModel)
            : Shells.GetValue(carModel, m => new Lazy<BodyShell?>(() => Read(m, null))).Value;
        return shell == null ? null : Layer(shell, look.Rounded(), picture, race: false);
    }

    /// <summary>
    /// Makes the layer of the car in <paramref name="carDirectory"/> for <paramref name="look"/> ahead, off the UI thread,
    /// while a scene loads the car: <see cref="For"/> finds it made (or being made) when the car is up. Never throws.
    /// </summary>
    public static Task WarmAsync(string carDirectory, CarLook look, int picture = FullPicture) => Task.Run(() =>
    {
        try
        {
            if (look.IsClean || AcPaths.GetMainCarFilename(carDirectory, false) is not { } file || !File.Exists(file)) return;
            if (ShellOf(file, null) is { } shell) Layer(shell, look.Rounded(), picture, race: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning("The wear of {Car} could not be made ahead: {Error}", Path.GetFileName(carDirectory), ex.Message);
        }
    });

    /// <summary>Where the models' shells are kept, worked out once for a model (<see cref="ShellFile"/>)</summary>
    private static string ShellCache => Path.Combine(Cache, "shells");

    /// <summary>
    /// The shell of the model in <paramref name="file"/>, worked out once: kept on disk from an earlier time, else from
    /// <paramref name="loaded"/> when given, else read, and kept
    /// </summary>
    private static BodyShell? ShellOf(string file, IKn5? loaded) =>
        ShellsByFile.GetOrAdd(Path.GetFullPath(file), f => new Lazy<BodyShell?>(() =>
        {
            string? kept = null;
            try
            {
                kept = ShellFile.PathFor(ShellCache, f);
                if (ShellFile.TryRead(kept, out var shell)) return shell;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                kept = null;
            }

            BodyShell? made;
            try
            {
                made = Read(loaded ?? Kn5.FromFile(f, SkippingTextureLoader.Instance), f);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Logger.Warning("The model {Model} could not be read for its wear: {Error}", f, ex.Message);
                return null;
            }

            try
            {
                if (kept != null) ShellFile.Write(kept, made);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Warning("The wear of {Model} could not be kept for next time: {Error}", Path.GetFileName(f), ex.Message);
            }
            return made;
        })).Value;

    /// <summary>
    /// The layer over the car in <paramref name="carDirectory"/> (its main model read from the file) for
    /// <paramref name="look"/>, written as a model into <paramref name="folder"/>; the file's name in it, null when there is
    /// nothing to draw or the car's model cannot be read. Off the UI thread.
    /// </summary>
    public static string? WriteFor(string carDirectory, CarLook look, string folder)
    {
        if (look.IsClean) return null;
        var file = AcPaths.GetMainCarFilename(carDirectory, false);
        if (file == null || !File.Exists(file)) return null;

        var shell = ShellOf(file, null);
        if (shell == null) return null;

        var rounded = look.Rounded();
        var name = $"{Path.GetFileName(carDirectory)}_{Hash(rounded):x8}.kn5";
        var path = Path.Combine(folder, name);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(folder);
            var temp = path + ".tmp";
            Layer(shell, rounded, FullPicture, race: true).Save(temp);
            File.Move(temp, path, overwrite: true);
        }
        return name;
    }

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

    /// <summary>The oldest layers written go, but for the newest few and <paramref name="keep"/></summary>
    private static void Prune(string folder, string? keep)
    {
        if (!Directory.Exists(folder)) return;
        var old = new DirectoryInfo(folder).GetFiles("*.kn5").OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeptFiles)
            .Where(f => !string.Equals(f.Name, keep, StringComparison.OrdinalIgnoreCase));
        foreach (var file in old)
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
                // In use (the race mode's copy is its own): tried again next time
            }
        }
    }

    private static BodyShell? Read(IKn5 model, string? name)
    {
        try
        {
            var started = Environment.TickCount64;
            var shell = BodyShell.Of(model);
            if (shell == null) Logger.Information("{Model}: nothing of it is paint, it shows no wear", Path.GetFileName(name));
            else
                Logger.Information("{Model}: wear drawn on {Paint} ({Meshes} mesh(es), {Ms} ms)", Path.GetFileName(name), string.Join(", ", shell.Paint),
                    shell.BodyMeshes.Count, Environment.TickCount64 - started);
            return shell;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "{Model}: its wear could not be worked out", Path.GetFileName(name));
            return null;
        }
    }

    /// <summary>A look's figures as a file name's part: the same look, the same name</summary>
    private static uint Hash(CarLook look)
    {
        var text = string.Join(",", look.Rust, look.Paint, look.Cracks, look.Dirt, look.Front, look.Rear, look.Left, look.Right, look.Seed);
        var hash = 2166136261u;
        foreach (var c in text) hash = (hash ^ c) * 16777619u;
        return hash;
    }

    /// <summary>The layer for a look, made once while it is kept; the least lately asked for go past <see cref="KeptBytes"/></summary>
    private static IKn5 Layer(BodyShell shell, CarLook look, int picture, bool race)
    {
        var asked = Interlocked.Increment(ref _asks);
        var key = (shell, look, picture, race);
        var layer = Layers.AddOrUpdate(key, k =>
        {
            var made = Make(k.Shell, k.Look, k.Picture, k.Race);
            return (made, asked, made.TexturesData.Values.Sum(t => (long)t.Length));
        }, (_, kept) => (kept.Layer, asked, kept.Bytes)).Layer;
        while (Layers.Count > 1 && Layers.Values.Sum(l => l.Bytes) > KeptBytes)
        {
            var oldest = Layers.MinBy(l => l.Value.Asked).Key;
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
        byte[] Encode(byte[] rgba, int w, int h) => race ? Png.Encode(rgba, w, h) : Dds.Encode(rgba, w, h);

        var bodyId = Material(kn5, BodyMaterial, Encode(WearPainter.Body(shell, look, picture), picture, picture), shell, race);
        var k = 0;
        foreach (var mesh in shell.BodyMeshes) kn5.RootNode.Children.Add(Node($"sc_wear_{k++}", mesh, bodyId));

        if (look.Cracks > 0.01 && shell.ScreenMeshes.Count > 0)
        {
            var screenId = Material(kn5, ScreenMaterial, Encode(WearPainter.Screen(look), WearPainter.ScreenWidth, WearPainter.ScreenHeight), shell, race);
            k = 0;
            foreach (var mesh in shell.ScreenMeshes) kn5.RootNode.Children.Add(Node($"sc_cracks_{k++}", mesh, screenId));
        }
        return kn5;
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

    private static Kn5Node Node(string name, BodyShell.Mesh mesh, uint material)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in mesh.Vertices)
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
            Vertices = mesh.Vertices,
            Indices = mesh.Indices,
            MaterialId = material,
            BoundingSphereCenter = new AcTools.Numerics.Vec3(middle.X, middle.Y, middle.Z),
            BoundingSphereRadius = Vector3.Distance(min, max) / 2
        };
    }
}
