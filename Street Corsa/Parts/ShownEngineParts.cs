using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using AcTools.Kn5File;
using AcTools.Utils;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts;

/// <summary>
/// The engine parts a car shows from outside, that its own model does not have: a supercharger and what sits on it (the
/// carburettors, the scoop), which stand up through the hood. Placed as the garage's parts view places the engine
/// (<see cref="CarPartsLayout"/>), by the car's wheel hubs as its model has them, then lifted onto the hood where they
/// would hide under it (<see cref="Lift"/>, done where the car's meshes are: the scenes, the race mode). A hood they go
/// through stays as it is.
/// </summary>
public static class ShownEngineParts
{
    /// <summary>The part class that stands up through the hood, by simple name</summary>
    private const string Standing = "SuperCharger";

    /// <summary>
    /// The parts of <paramref name="engine"/> the car shows, each where it sits in the car's model; none without a
    /// supercharger or without the car's hubs
    /// </summary>
    public static List<ShownModelAt> Of(PartsCatalog catalog, InstalledPart? engine, CarAnchors? hubs, EngineLayout layout)
    {
        var shown = new List<ShownModelAt>();
        if (engine == null || hubs == null) return shown;

        foreach (var placed in CarPartsLayout.Build(catalog, hubs, engine, null, layout))
        {
            if (placed.Source is not { } part || !OnSupercharger(part)) continue;
            if (catalog.GetModelPath(placed.Part) is { } path && File.Exists(path))
                shown.Add(new ShownModelAt(new ShownModel(ShownParts.FileName(placed.Part.Id), path), placed.World));
        }
        return shown;
    }

    /// <summary>How far over a part's place the ray that finds the hood above it starts, metres: over any car's roof</summary>
    public const float RayFrom = 3f;

    /// <summary>
    /// How far up the parts go so they stand out of the hood (the engine is placed by an estimate, and on a car whose
    /// estimate is low the whole stack would hide under it): the first part's base (the supercharger's) onto the car's
    /// surface straight above it, as <paramref name="surfaceAbove"/> finds it; never down, and nothing without a surface
    /// </summary>
    public static float Lift(IReadOnlyList<ShownModelAt> parts, Func<Vector3, float?> surfaceAbove)
    {
        if (parts.Count == 0) return 0;
        var basePoint = parts[0].At.Translation;
        return surfaceAbove(basePoint) is { } surface ? Math.Max(0, surface - basePoint.Y) : 0;
    }

    /// <summary>The part is a supercharger or sits on one</summary>
    private static bool OnSupercharger(InstalledPart part)
    {
        for (var p = part; p != null; p = p.Parent)
        {
            if (PartKinds.Is(p.Definition, Standing)) return true;
        }
        return false;
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
