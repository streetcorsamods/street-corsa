using System.IO;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using AcTools.Kn5File;
using AcTools.Render.Base.Objects;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Logging;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Controls;

/// <summary>
/// The wheels a car shows in the game's own scenes that its model does not have (<see cref="ShownParts"/>): the part
/// models go in a node of their own under the car's WHEEL_xx node, so they turn and steer with it in every scripted
/// move, and the car's meshes they replace are switched off. Placed by <see cref="WheelFit"/>, as the race mode places
/// them in AC. Only the car's main model is loaded here, so there are no far LODs to leave alone.
/// </summary>
public static class WheelParts
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("WheelParts");

    /// <summary>The part models read so far, by file: a part's model is the same in every scene</summary>
    private static readonly ConcurrentDictionary<string, IKn5> Models = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What is on a car: the wheels asked for, the nodes put in and the car's meshes switched off</summary>
    private sealed class OnCar
    {
        public IReadOnlyList<ShownWheel?> Wheels = [];
        public readonly List<(RenderableList Wheel, RenderableList Parts)> Nodes = [];
        public readonly List<Kn5RenderableObject> Hidden = [];
        public int Version;
    }

    private static readonly ConditionalWeakTable<Kn5RenderableCar, OnCar> Cars = new();

    /// <summary>
    /// Makes <paramref name="car"/> show <paramref name="wheels"/> (AC's corner order; null keeps the car's own): what an
    /// earlier call put on comes off first, and the same wheels again change nothing. The models are read off the UI
    /// thread; the car is changed on the caller's. True when the car changed.
    /// </summary>
    public static async Task<bool> SetAsync(Kn5RenderableCar? car, IReadOnlyList<ShownWheel?>? wheels)
    {
        if (car == null) return false;
        wheels ??= [];
        var on = Cars.GetOrCreateValue(car);
        if (on.Wheels.SequenceEqual(wheels)) return false;

        var version = ++on.Version;
        on.Wheels = wheels;
        Clear(on);
        if (wheels.All(w => w == null)) return true;

        var files = wheels.OfType<ShownWheel>().SelectMany(w => w.Tyre is { } tyre ? new[] { w.Rim.Source, tyre.Source } : [w.Rim.Source])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        try
        {
            await Task.Run(() => { foreach (var file in files) Models.GetOrAdd(file, f => Kn5.FromFile(f)); });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Warning("A wheel part's model could not be read, the car keeps its own wheels: {Error}", ex.Message);
            return true;
        }

        // Asked again while the models were read: the later call puts its own on
        if (version != on.Version) return false;

        for (var corner = 0; corner < Math.Min(wheels.Count, WheelFit.WheelNodes.Length); corner++)
        {
            if (wheels[corner] is not { } wheel) continue;
            try
            {
                Apply(car, corner, wheel, on);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException)
            {
                Logger.Warning(ex, "Wheel {Corner} of {Car} keeps its own", WheelFit.WheelNodes[corner], car.RootObject?.Name);
            }
        }
        return true;
    }

    /// <summary>Takes off what was put on: the car's meshes back on, the part nodes out and let go</summary>
    private static void Clear(OnCar on)
    {
        foreach (var mesh in on.Hidden) mesh.IsEnabled = true;
        foreach (var (wheel, parts) in on.Nodes)
        {
            wheel.Remove(parts);
            parts.Dispose();
        }
        on.Hidden.Clear();
        on.Nodes.Clear();
    }

    private static void Apply(Kn5RenderableCar car, int corner, ShownWheel wheel, OnCar on)
    {
        var name = WheelFit.WheelNodes[corner];
        if (car.RootObject?.GetAllChildren().OfType<Kn5RenderableList>().FirstOrDefault(n => n.OriginalNode.Name == name) is not { } node)
        {
            Logger.Information("{Car} has no {Wheel}: its own wheel stays", car.RootObject?.Name, name);
            return;
        }

        // The car's meshes under the wheel, each in the wheel's space
        var meshes = new List<(Kn5RenderableObject Mesh, WheelMesh Fit)>();
        Collect(car.OriginalFile, node, Matrix4x4.Identity, meshes);

        var rim = Models[wheel.Rim.Source];
        var tyre = wheel.Tyre is { } t ? Models[t.Source] : null;
        var partTyre = tyre == null ? null : Boxes(tyre, wheel.TyreOnRim);
        var ownTyre = meshes.Where(m => WheelFit.IsTyre(m.Fit)).Select(m => m.Fit.Box.Radius).DefaultIfEmpty(0).Max();
        if (WheelFit.Fit(corner, meshes.Select(m => m.Fit).ToList(), Boxes(rim, Matrix4x4.Identity), partTyre,
                ownTyre * wheel.TyreScale) is not { } fitting)
        {
            Logger.Information("{Wheel} of {Car}: nothing to measure, its own wheel stays", name, car.RootObject?.Name);
            return;
        }

        var parts = new RenderableList("sc_wheel_" + name, ToSlim(fitting.Placement));
        parts.Add(new Kn5RenderableFile(rim, SlimDX.Matrix.Identity));
        if (tyre != null) parts.Add(new Kn5RenderableFile(tyre, ToSlim(wheel.TyreOnRim)));
        node.Add(parts);
        parts.ParentMatrix = node.Matrix;
        on.Nodes.Add((node, parts));
        foreach (var hidden in fitting.Hidden)
        {
            meshes[hidden].Mesh.IsEnabled = false;
            on.Hidden.Add(meshes[hidden].Mesh);
        }
    }

    /// <summary>The meshes under a node with what each is carried into the node's space by</summary>
    private static void Collect(IKn5 file, RenderableList list, Matrix4x4 toWheel, List<(Kn5RenderableObject, WheelMesh)> meshes)
    {
        foreach (var child in list)
        {
            switch (child)
            {
                case Kn5RenderableList group:
                    Collect(file, group, ToNumerics(group.LocalMatrix) * toWheel, meshes);
                    break;
                case Kn5RenderableObject mesh when mesh.OriginalNode.Vertices is { Length: > 0 } vertices:
                    var (min, max) = Bounds(vertices);
                    meshes.Add((mesh, new WheelMesh(mesh.OriginalNode.Name, file.GetMaterial(mesh.OriginalNode.MaterialId)?.Name,
                        AxleBox.Of(min, max, toWheel))));
                    break;
            }
        }
    }

    /// <summary>A part model's meshes in the space it is set in by <paramref name="toSpace"/></summary>
    private static List<AxleBox> Boxes(IKn5 model, Matrix4x4 toSpace)
    {
        var boxes = new List<AxleBox>();
        void Walk(Kn5Node node, Matrix4x4 m)
        {
            if (node.NodeClass == Kn5NodeClass.Base)
            {
                var local = ToNumerics(node.Transform) * m;
                foreach (var child in node.Children) Walk(child, local);
            }
            else if (node.Vertices is { Length: > 0 } vertices)
            {
                var (min, max) = Bounds(vertices);
                boxes.Add(AxleBox.Of(min, max, m));
            }
        }
        Walk(model.RootNode, toSpace);
        return boxes;
    }

    private static (Vector3 Min, Vector3 Max) Bounds(Kn5Node.Vertex[] vertices)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in vertices)
        {
            var p = new Vector3(v.Position.X, v.Position.Y, v.Position.Z);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return (min, max);
    }

    private static Matrix4x4 ToNumerics(SlimDX.Matrix m) => new(
        m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);

    private static Matrix4x4 ToNumerics(AcTools.Numerics.Mat4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);

    private static SlimDX.Matrix ToSlim(Matrix4x4 m) => new()
    {
        M11 = m.M11, M12 = m.M12, M13 = m.M13, M14 = m.M14,
        M21 = m.M21, M22 = m.M22, M23 = m.M23, M24 = m.M24,
        M31 = m.M31, M32 = m.M32, M33 = m.M33, M34 = m.M34,
        M41 = m.M41, M42 = m.M42, M43 = m.M43, M44 = m.M44
    };
}
