using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using AcTools.Kn5File;
using AcTools.Render.Base;
using AcTools.Render.Base.Objects;
using AcTools.Render.Base.Structs;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5Specific.Textures;
using StreetCorsa.Logging;
using StreetCorsa.Looks;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using static StreetCorsa.Helpers.Matrices;

namespace StreetCorsa.Controls;

/// <summary>
/// What a car shows in the game's own scenes that its model does not have (<see cref="ShownSet"/>), as the race mode puts
/// it on in AC. The wheels: the part models go in a node of their own under the car's WHEEL_xx node, so they turn and
/// steer with it in every scripted move, and the car's meshes they replace are switched off (placed by
/// <see cref="WheelFit"/>), with its blurred objects of that wheel (blurred_objects.ini) as the race mode has them: off,
/// except one that is the wheel node or holds it, which is on. Nothing in the scenes swaps those by speed, so they stay
/// so. The engine parts that stand out: under the car's root, where they sit in its model. Only the car's main model is
/// loaded here, so there are no far LODs to leave alone. A part model that cannot be read or put on goes no further than
/// itself: the car is shown without it.
/// </summary>
public static class ShownPartsOnCar
{
    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("ShownParts");

    /// <summary>How many part models are kept read for the next car (each with its textures); a car holds on to its own</summary>
    private const int KeptModels = 24;

    /// <summary>The part models read lately, by file, with when each was last asked for: a part's model is the same in every scene</summary>
    private static readonly ConcurrentDictionary<string, (IKn5 Model, long Asked)> Models = new(StringComparer.OrdinalIgnoreCase);

    private static long _asks;

    /// <summary>
    /// What is on a car: the set asked for, the part nodes put in, what of the car was switched and how it was before, and
    /// its wear layer (kept apart: it stays on while only the parts change)
    /// </summary>
    private sealed class OnCar
    {
        public ShownSet? Shown;
        public readonly List<(RenderableList Holder, RenderableList Parts)> Nodes = [];
        public readonly List<(IRenderableObject Object, bool Was)> Switched = [];

        /// <summary>
        /// Each model's textures, once for the car (its four rims draw from the same), with how many of its files draw from
        /// them: the last file let go of (taken off, or the car itself let go of with everything in it) lets them go
        /// </summary>
        public readonly Dictionary<IKn5, (ITexturesProvider Textures, int Users)> Textures = [];

        /// <summary>The wear layer on the car: for what look, at what picture size, and its node</summary>
        public (CarLook Look, int Picture, RenderableList Holder, RenderableList Node)? Wear;

        /// <summary>The colour the car's paint textures were tinted for, and the skin they were tinted in; null in its own colours</summary>
        public (PaintColour Colour, string? Skin)? Paint;

        /// <summary>How many coats of a paint job went on (<see cref="Coat"/>), and whether the paint textures on are one</summary>
        public int Coats;

        public bool Coated;

        public int Version;

        public void Switch(IRenderableObject o, bool on)
        {
            Switched.Add((o, o.IsEnabled));
            o.IsEnabled = on;
        }
    }

    private static readonly ConditionalWeakTable<Kn5RenderableCar, OnCar> Cars = new();

    /// <summary>
    /// How the corners of <paramref name="car"/> sit as it was last asked to show (<see cref="ShownSet.Heights"/>): what
    /// the scene's body rock leans its body by (CarBodyRock); null when as its model has them
    /// </summary>
    public static CornerHeights? HeightsOf(Kn5RenderableCar? car) =>
        car != null && Cars.TryGetValue(car, out var on) ? on.Shown?.Heights : null;

    /// <summary>A part model drawn with the car's one set of its textures</summary>
    /// <param name="loadAtOnce">Its pictures loaded as it is first drawn, not on the side: the wear layer's, which are DDS
    /// with their mips (nothing to decode) and must not be drawn a frame without them</param>
    private sealed class PartFile(IKn5 kn5, SlimDX.Matrix matrix, OnCar on, bool loadAtOnce = false) : Kn5RenderableFile(kn5, matrix)
    {
        protected override ITexturesProvider InitializeTextures(IDeviceContextHolder contextHolder)
        {
            lock (on.Textures)
            {
                var (textures, users) = on.Textures.TryGetValue(OriginalFile, out var kept) ? kept : (new Kn5TexturesProvider(OriginalFile, !loadAtOnce), 0);
                on.Textures[OriginalFile] = (textures, users + 1);
                return new Lent(textures, OriginalFile, on);
            }
        }

        /// <summary>
        /// The car's textures, lent to one file: let go of when the last file drawing from them is (the file taken off, or
        /// the car let go of by its scene with all it holds), not with each. SlimDX's views have no finalizer: textures
        /// never let go of stay on the card.
        /// </summary>
        private sealed class Lent(ITexturesProvider textures, IKn5 model, OnCar on) : ITexturesProvider
        {
            private bool _returned;

            public IRenderableTexture GetTexture(IDeviceContextHolder contextHolder, string key) => textures.GetTexture(contextHolder, key);

            public void Dispose()
            {
                lock (on.Textures)
                {
                    if (_returned) return;
                    _returned = true;
                    if (!on.Textures.TryGetValue(model, out var kept) || !ReferenceEquals(kept.Textures, textures)) return;
                    if (kept.Users > 1)
                    {
                        on.Textures[model] = (textures, kept.Users - 1);
                        return;
                    }
                    on.Textures.Remove(model);
                }
                textures.Dispose();
            }
        }
    }

    /// <summary>
    /// Makes <paramref name="car"/> show <paramref name="shown"/> (null: the car as it is): what an earlier call put on
    /// comes off first, and the same set again changes nothing. The wear layer is the exception: one for the same look
    /// stays on, and one for another look stays until the new one is made, then they swap. The models are read off the UI
    /// thread; the car is changed on the caller's. True when the car changed. Never throws for what the part models or the
    /// car hold.
    /// </summary>
    /// <param name="device">The renderer's device, the car's paint textures go onto it (<see cref="ShownSet.Paint"/>)</param>
    /// <param name="wearPicture">How big the car's wear picture is drawn (<see cref="WearLayer.FullPicture"/>, or
    /// <see cref="WearLayer.LotPicture"/> for a car among many)</param>
    /// <param name="gone">Cancelled as the car's renderer goes: nothing more is put on the car after a wait, as its device
    /// may be disposed by then (false then)</param>
    public static async Task<bool> SetAsync(Kn5RenderableCar? car, ShownSet? shown, DeviceContextHolder device, int wearPicture = WearLayer.FullPicture,
        CancellationToken gone = default)
    {
        if (car == null) return false;
        var on = Cars.GetOrCreateValue(car);
        if (Equals(on.Shown, shown)) return false;

        var version = ++on.Version;
        on.Shown = shown;
        Clear(on);
        var keepWear = on.Wear is { } wear && shown?.Look is { } wanted && wear.Look == wanted && wear.Picture == wearPicture;
        if (!keepWear && shown?.Look == null) ClearWear(on);
        if (shown?.Paint == null) ClearPaint(car, on);
        if (shown == null) return true;

        // The paint first: a car painted is seen in its colour from the start, not after its parts are put on
        if (shown.Paint is { } colour && !await PutPaintAsync(car, colour, on, version, device, gone)) return false;

        var files = shown.Models.Select(m => m.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Dictionary<string, IKn5>? models;
        try
        {
            models = await Task.Run(() => files.ToDictionary(f => f, Read, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // AcTools throws a plain Exception for a file that is no KN5. The wear still goes on: AC draws it all the same.
            Logger.Warning("A part's model could not be read, the car is shown without its parts: {Error}", ex.Message);
            models = null;
        }

        // Asked again while the models were read: the later call puts its own on
        if (Stale(on, version, gone)) return false;

        if (models != null && !await PutPartsAsync(car, shown, models, on, version, gone)) return false;

        if (shown.Look is not { } look || keepWear) return true;
        try
        {
            return await PutLookAsync(car, look, on, version, wearPicture, gone);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "The wear of {Car} is not shown", car.RootObject?.Name);
            if (!Stale(on, version, gone)) ClearWear(on);
            return true;
        }
    }

    /// <summary>
    /// Another set was asked for since <paramref name="version"/>, or the car's renderer went (<paramref name="gone"/>):
    /// what this call was putting on goes no further
    /// </summary>
    private static bool Stale(OnCar on, int version, CancellationToken gone) => version != on.Version || gone.IsCancellationRequested;

    /// <summary>The wheels and the engine parts that stand out; false when the car was asked to show something else meanwhile</summary>
    private static async Task<bool> PutPartsAsync(Kn5RenderableCar car, ShownSet shown, Dictionary<string, IKn5> models, OnCar on, int version,
        CancellationToken gone)
    {
        var wheels = shown.Wheels;
        for (var corner = 0; corner < Math.Min(wheels.Count, WheelFit.WheelNodes.Length); corner++)
        {
            if (wheels[corner] is not { } wheel) continue;
            try
            {
                Apply(car, corner, wheel, models, on);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Logger.Warning(ex, "Wheel {Corner} of {Car} keeps its own", WheelFit.WheelNodes[corner], car.RootObject?.Name);
            }
        }

        if (shown.Extras.Count == 0) return true;
        try
        {
            return await PutExtrasAsync(car, shown, models, on, version, gone);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "The engine parts of {Car} are not shown", car.RootObject?.Name);
            return true;
        }
    }

    /// <summary>
    /// The car's wear layer (<see cref="WearLayer"/>) over its body, in its model's space; made off the UI thread (the first
    /// time for a model it looks the car over), the layer of an earlier look on till then. False when the car was asked to
    /// show something else in the meantime.
    /// </summary>
    private static async Task<bool> PutLookAsync(Kn5RenderableCar car, CarLook look, OnCar on, int version, int picture, CancellationToken gone)
    {
        var model = car.OriginalFile;
        var kn5Root = ToNumerics(model.RootNode.Transform);
        if (car.RootObject is not { } root || !Matrix4x4.Invert(kn5Root, out var fromRoot)) return true;

        var layer = await Task.Run(() => WearLayer.For(model, look, picture));
        if (Stale(on, version, gone)) return false;

        ClearWear(on);
        if (layer == null) return true;
        var node = new RenderableList("sc_look", SlimDX.Matrix.Identity) { new PartFile(layer, ToSlim(fromRoot), on, loadAtOnce: true) };
        root.Add(node);
        on.Wear = (look, picture, root, node);
        return true;
    }

    /// <summary>
    /// The car's paint textures tinted <paramref name="colour"/> (Looks.CarPaint) over its own, in the skin it is drawn in;
    /// made off the UI thread (the first time for a car, skin and colour they are written). False when the car was asked to
    /// show something else in the meantime. A car whose paint cannot be tinted stays in its own colours.
    /// </summary>
    private static async Task<bool> PutPaintAsync(Kn5RenderableCar car, PaintColour colour, OnCar on, int version, DeviceContextHolder device,
        CancellationToken gone)
    {
        var skin = car.CurrentSkin;
        if (!on.Coated && on.Paint is { } painted && painted.Colour == colour && string.Equals(painted.Skin, skin, StringComparison.OrdinalIgnoreCase)) return true;

        var coats = on.Coats;
        IReadOnlyList<(string Texture, byte[] Bytes)> textures;
        try
        {
            textures = await Task.Run(() => CarPaint.ForScene(car.RootDirectory, skin, colour));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "The paint of {Car} is not shown: it is drawn in its own colours", car.RootObject?.Name);
            if (Stale(on, version, gone)) return false;
            if (coats == on.Coats) ClearPaint(car, on);
            return true;
        }
        if (Stale(on, version, gone)) return false;

        // A coat of a paint job went on while this was made: the job's paint stays, the next set asked for puts its own
        if (coats != on.Coats) return true;

        ClearPaint(car, on);
        foreach (var (texture, bytes) in textures) car.OverrideTexture(device, texture, bytes);
        on.Paint = (colour, skin);
        return true;
    }

    /// <summary>The car back in its skin's own colours</summary>
    private static void ClearPaint(Kn5RenderableCar car, OnCar on)
    {
        if (on.Paint == null && !on.Coated) return;
        on.Paint = null;
        on.Coated = false;
        car.ClearProceduralOverrides();
    }

    /// <summary>
    /// <paramref name="car"/>'s paint textures as <paramref name="textures"/> have them (a coat of a paint job on its way,
    /// Looks.CarPaint.Blend); what it shows (<see cref="SetAsync"/>) puts its own back the next time it is asked to show
    /// something else
    /// </summary>
    public static void Coat(Kn5RenderableCar car, DeviceContextHolder device, IEnumerable<(string Texture, byte[] Bytes)> textures)
    {
        var on = Cars.GetOrCreateValue(car);
        foreach (var (texture, bytes) in textures) car.OverrideTexture(device, texture, bytes);

        // Not the paint it was given: a paint still being made for an earlier set is not put over it, and whatever the
        // car is asked to show next puts its own on afresh
        on.Coats++;
        on.Coated = true;
        on.Paint = null;
        on.Shown = null;
    }

    /// <summary>Takes the wear layer off, its textures let go of with it</summary>
    private static void ClearWear(OnCar on)
    {
        if (on.Wear is not { } wear) return;
        on.Wear = null;
        wear.Holder.Remove(wear.Node);
        wear.Node.Dispose();
    }

    /// <summary>A part's model, read once while it is asked for; the least lately asked for of too many is let go of</summary>
    private static IKn5 Read(string file)
    {
        var asked = Interlocked.Increment(ref _asks);
        var model = Models.AddOrUpdate(file, f => (Kn5.FromFile(f), asked), (_, kept) => (kept.Model, asked)).Model;
        while (Models.Count > KeptModels)
        {
            var oldest = Models.MinBy(m => m.Value.Asked).Key;
            if (oldest.Equals(file, StringComparison.OrdinalIgnoreCase) || !Models.TryRemove(oldest, out _)) break;
        }
        return model;
    }

    /// <summary>
    /// The engine parts that stand out of the car, where they sit in its model (its root's children); false when the car
    /// was asked to show something else in the meantime
    /// </summary>
    private static async Task<bool> PutExtrasAsync(Kn5RenderableCar car, ShownSet shown, Dictionary<string, IKn5> models, OnCar on, int version,
        CancellationToken gone)
    {
        // The model's space is its KN5's: the root's own transform as the file has it, not what the scene adds to the root's
        // matrix (the garage lifts the car onto its ground there: 25 cm on the Chevelle '70)
        var kn5Root = ToNumerics(car.OriginalFile.RootNode.Transform);
        if (car.RootObject is not { } root || !Matrix4x4.Invert(kn5Root, out var fromRoot)) return true;

        // The car's meshes as they are now, taken on this thread; their triangles are put together and asked off it
        var meshes = MeshesOn(root, kn5Root);
        var standing = await Task.Run(() =>
        {
            var surface = Surface(meshes);
            return shown.Extras.Where(e =>
            {
                var stands = ShownEngineParts.StandsOut(e, surface.Above);
                Logger.Debug("{Part}: {Verdict}", e.Model.Name, stands ? "stands out" : "stays under the hood");
                return stands;
            }).ToList();
        });
        if (Stale(on, version, gone)) return false;

        var extras = new RenderableList("sc_parts", SlimDX.Matrix.Identity);
        foreach (var extra in standing) extras.Add(new PartFile(models[extra.Model.Source], ToSlim(extra.At * fromRoot), on));
        root.Add(extras);
        on.Nodes.Add((root, extras));
        return true;
    }

    /// <summary>The car's triangles in its model's space, each with the stretch of ground it covers seen from above</summary>
    private sealed class CarSurface(List<(Vector3 A, Vector3 B, Vector3 C, float MinX, float MaxX, float MinZ, float MaxZ)> triangles)
    {
        /// <summary>
        /// The car's highest surface over a point of its model (the height in the model's space), null when nothing of it
        /// is there: every triangle the vertical line through the point crosses, whichever way it faces
        /// </summary>
        public float? Above(Vector3 point)
        {
            float? highest = null;
            foreach (var t in triangles)
            {
                if (point.X < t.MinX || point.X > t.MaxX || point.Z < t.MinZ || point.Z > t.MaxZ) continue;
                if (Helpers.Triangles.HeightAt(t.A, t.B, t.C, point.X, point.Z) is { } y && (highest == null || y > highest)) highest = y;
            }
            return highest;
        }
    }

    /// <summary>A mesh of the car that is on, with what takes its vertices into the model's space</summary>
    private sealed record OnMesh(InputLayouts.VerticePNTG[] Vertices, ushort[] Indices, Matrix4x4 ToModel);

    /// <summary>The car's meshes that are on (the ones we put in are not in yet), taken on the caller's thread</summary>
    private static List<OnMesh> MeshesOn(RenderableList root, Matrix4x4 kn5Root)
    {
        var meshes = new List<OnMesh>();
        // World to the model's space: everything the root is placed by taken off, the KN5 root's own transform put back
        if (!Matrix4x4.Invert(ToNumerics(root.Matrix), out var fromWorld)) return meshes;
        var toModel = fromWorld * kn5Root;
        foreach (var mesh in root.GetAllChildren().OfType<Kn5RenderableObject>())
        {
            if (mesh.IsEnabled && mesh.Vertices is { Length: > 0 } vertices && mesh.Indices is { } indices)
                meshes.Add(new OnMesh(vertices, indices, ToNumerics(mesh.ParentMatrix) * toModel));
        }
        return meshes;
    }

    private static CarSurface Surface(List<OnMesh> meshes)
    {
        var triangles = new List<(Vector3, Vector3, Vector3, float, float, float, float)>();
        foreach (var (vertices, indices, m) in meshes)
        {
            for (var t = 0; t + 2 < indices.Length; t += 3)
            {
                var a = Model(vertices[indices[t]].Position, m);
                var b = Model(vertices[indices[t + 1]].Position, m);
                var c = Model(vertices[indices[t + 2]].Position, m);
                triangles.Add((a, b, c, Math.Min(a.X, Math.Min(b.X, c.X)), Math.Max(a.X, Math.Max(b.X, c.X)),
                    Math.Min(a.Z, Math.Min(b.Z, c.Z)), Math.Max(a.Z, Math.Max(b.Z, c.Z))));
            }
        }
        return new CarSurface(triangles);
    }

    private static Vector3 Model(SlimDX.Vector3 p, Matrix4x4 m) => Vector3.Transform(new Vector3(p.X, p.Y, p.Z), m);

    /// <summary>
    /// Takes off the parts put on: the car's own switched back as they were, the part nodes out and let go (their textures
    /// with the last file drawing from them). The wear layer is <see cref="ClearWear"/>'s.
    /// </summary>
    private static void Clear(OnCar on)
    {
        for (var i = on.Switched.Count - 1; i >= 0; i--) on.Switched[i].Object.IsEnabled = on.Switched[i].Was;
        foreach (var (holder, parts) in on.Nodes)
        {
            holder.Remove(parts);
            parts.Dispose();
        }
        on.Switched.Clear();
        on.Nodes.Clear();
    }

    private static void Apply(Kn5RenderableCar car, int corner, ShownWheel wheel, Dictionary<string, IKn5> models, OnCar on)
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
        var fits = meshes.Select(m => m.Fit).ToList();

        var rim = models[wheel.Rim.Source];
        var tyre = wheel.Tyre is { } t ? models[t.Source] : null;
        var partTyre = tyre == null ? null : Boxes(tyre, wheel.TyreOnRim);
        if (WheelFit.Fit(corner, fits, Boxes(rim, Matrix4x4.Identity), partTyre, WheelFit.TyreRadius(fits) * wheel.TyreScale,
                wheel.TyreOptional) is not { } fitting)
        {
            Logger.Information("{Wheel} of {Car}: nothing to measure, its own wheel stays", name, car.RootObject?.Name);
            return;
        }

        // The tyre only when the fit takes it: an optional one stays out while the rim fits in the car's own tyre
        var parts = new RenderableList("sc_wheel_" + name, ToSlim(fitting.Placement));
        parts.Add(new PartFile(rim, SlimDX.Matrix.Identity, on));
        if (tyre != null && fitting.WithTyre) parts.Add(new PartFile(tyre, ToSlim(wheel.TyreOnRim), on));
        node.Add(parts);
        on.Nodes.Add((node, parts));
        foreach (var hidden in fitting.Hidden) on.Switch(meshes[hidden].Mesh, false);
        SwitchBlurred(car, corner, node, fitting.WithTyre, on);
    }

    /// <summary>
    /// The car's blurred objects of a wheel (blurred_objects.ini), as car_parts.lua has them: one that is the wheel node or
    /// holds it on (our parts are in it), the others off (our parts have no blurred model), a tyre's only when ours comes along
    /// </summary>
    private static void SwitchBlurred(Kn5RenderableCar car, int corner, Kn5RenderableList wheel, bool withTyre, OnCar on)
    {
        if (car.RootObject is not { } root) return;
        foreach (var blurred in car.BlurredObjects.Where(b => b.WheelIndex == corner).Select(b => b.Name).Distinct(StringComparer.Ordinal))
        {
            var objects = root.GetAllChildren().Where(o => o is Kn5RenderableList l ? l.OriginalNode.Name == blurred
                : o is Kn5RenderableObject m && m.OriginalNode.Name == blurred).ToList();
            foreach (var o in objects)
            {
                if (ReferenceEquals(o, wheel) || (o is RenderableList list && list.GetAllChildren().Contains(wheel))) on.Switch(o, true);
                else if (withTyre || !WheelFit.IsTyreName(blurred)) on.Switch(o, false);
            }
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
}
