using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using AcTools.Kn5File;
using AcTools.Utils;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts;

/// <summary>
/// The engine parts a car shows from outside, that its own model does not have: whatever stands up out of its hood (a
/// carburettor's air cleaner, a supercharger, a scoop), where the engine has it. Placed as the garage's parts view
/// places the engine (<see cref="CarPartsLayout"/>), by the car's wheel hubs as its model has them, and never moved: a
/// part is shown at its place when it stands out of the car (<see cref="StandsOut"/>, judged where the car's meshes are:
/// the scenes, the race mode), and not at all when it does not. A hood a part goes through stays as it is.
/// </summary>
public static class ShownEngineParts
{
    /// <summary>
    /// How far over the hubs a part's top must be to be worth asking whether it stands out, metres: the engine's upper
    /// half (the heads, the intake and what is on it), not its block, crank or gearbox
    /// </summary>
    public const float UpperEngine = 0.35f;

    /// <summary>How far over a part's place the ray that finds the car's surface above it starts, metres: over any car's roof</summary>
    public const float RayFrom = 3f;

    /// <summary>How far a part must reach past the car's surface to count as standing out of it, metres</summary>
    public const float OutBy = 0.005f;

    /// <summary>
    /// The parts of <paramref name="engine"/> that may stand out of the car, each where it sits in the car's model with
    /// its box; none without the car's hubs
    /// </summary>
    public static List<ShownModelAt> Of(PartsCatalog catalog, InstalledPart? engine, CarAnchors? hubs, EngineLayout layout)
    {
        var shown = new List<ShownModelAt>();
        if (engine == null || hubs == null) return shown;

        var high = new[] { hubs.WheelLF.Y, hubs.WheelRF.Y, hubs.WheelLR.Y, hubs.WheelRR.Y }.Max() + UpperEngine;
        foreach (var placed in CarPartsLayout.Build(catalog, hubs, engine, null, layout))
        {
            if (PartModelBounds.Of(catalog, placed.Part) is not { } box || catalog.GetModelPath(placed.Part) is not { } path) continue;
            if (Corners(box.Min, box.Max).Max(c => Vector3.Transform(c, placed.World).Y) <= high) continue;
            shown.Add(new ShownModelAt(new ShownModel(ShownParts.FileName(placed.Part.Id), path), placed.World, box.Min, box.Max));
        }
        return shown;
    }

    /// <summary>
    /// Whether a part stands out of the car: a corner of its box, or the middle of its top, higher than the car's highest
    /// surface straight over it (as <paramref name="surfaceAbove"/> finds it), or with nothing of the car over it at all
    /// </summary>
    public static bool StandsOut(ShownModelAt part, Func<Vector3, float?> surfaceAbove)
    {
        var top = new Vector3((part.Min.X + part.Max.X) / 2, part.Max.Y, (part.Min.Z + part.Max.Z) / 2);
        foreach (var point in Corners(part.Min, part.Max).Append(top).Select(p => Vector3.Transform(p, part.At)))
        {
            if (surfaceAbove(point) is not { } surface || point.Y > surface + OutBy) return true;
        }
        return false;
    }

    private static IEnumerable<Vector3> Corners(Vector3 min, Vector3 max)
    {
        for (var i = 0; i < 8; i++)
            yield return new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
    }
}

/// <summary>
/// A car's wheel hubs as its main model has them (the WHEEL_xx nodes), read from the model's node tree alone (no
/// meshes, no textures) and kept per folder: the space the race mode and the scenes put a car's shown parts in
/// </summary>
public static class CarHubs
{
    private static readonly ConcurrentDictionary<string, CarAnchors?> Read = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The hubs of the car in <paramref name="carDirectory"/>; null when its model or a wheel node is not there</summary>
    public static CarAnchors? Of(string carDirectory) => Read.GetOrAdd(carDirectory, Load);

    private static CarAnchors? Load(string carDirectory)
    {
        try
        {
            var file = AcPaths.GetMainCarFilename(carDirectory, false);
            if (file == null || !File.Exists(file)) return null;

            var kn5 = Kn5.FromFile(file, SkippingTextureLoader.Instance, SkippingMaterialLoader.Instance, HierarchyOnlyNodeLoader.Instance);
            var hubs = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            Walk(kn5.RootNode, Matrix4x4.Identity, hubs);
            return WheelFit.WheelNodes.All(hubs.ContainsKey)
                ? new CarAnchors(hubs["WHEEL_LF"], hubs["WHEEL_RF"], hubs["WHEEL_LR"], hubs["WHEEL_RR"])
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>Each wheel node's place in the model, the first of each name (the main LOD's)</summary>
    private static void Walk(Kn5Node node, Matrix4x4 parent, Dictionary<string, Vector3> hubs)
    {
        var t = node.Transform;
        var world = node.NodeClass == Kn5NodeClass.Base
            ? new Matrix4x4(t.M11, t.M12, t.M13, t.M14, t.M21, t.M22, t.M23, t.M24, t.M31, t.M32, t.M33, t.M34, t.M41, t.M42, t.M43, t.M44) * parent
            : parent;
        if (node.NodeClass == Kn5NodeClass.Base && Array.IndexOf(WheelFit.WheelNodes, node.Name) >= 0)
            hubs.TryAdd(node.Name, world.Translation);
        foreach (var child in node.Children) Walk(child, world, hubs);
    }
}
