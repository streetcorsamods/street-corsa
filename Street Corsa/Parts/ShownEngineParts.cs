using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using AcTools.Kn5File;
using AcTools.Utils;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using static StreetCorsa.Helpers.Matrices;

namespace StreetCorsa.Parts;

/// <summary>
/// The engine parts a car shows from outside, that its own model does not have: whatever stands up out of its hood (a
/// carburettor's air cleaner, a supercharger, a scoop), where the engine has it. Placed as the garage's parts view
/// places the engine (<see cref="CarPartsLayout"/>), by the car's wheel hubs as its model has them, and never moved: a
/// part is shown at its place when it stands out of the car (<see cref="StandsOut"/>, judged where the car's meshes are:
/// the scenes, the race mode), and not at all when it does not. A hood a part goes through stays as it is. A part where
/// the factory put it is the car's model's own, as a factory rim is (<see cref="ShownParts"/>): never shown.
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
    /// its box; none without the car's hubs. A part of <paramref name="factory"/> (the ids of the car's factory build, given
    /// when the engine is on its factory block) on nothing but such parts down to the block is where the car's model has it.
    /// </summary>
    public static List<ShownModelAt> Of(PartsCatalog catalog, InstalledPart? engine, CarAnchors? hubs, EngineLayout layout,
        IReadOnlySet<string>? factory = null)
    {
        var shown = new List<ShownModelAt>();
        if (engine == null || hubs == null) return shown;

        var high = new[] { hubs.WheelLF.Y, hubs.WheelRF.Y, hubs.WheelLR.Y, hubs.WheelRR.Y }.Max() + UpperEngine;
        foreach (var placed in CarPartsLayout.Build(catalog, hubs, engine, null, layout))
        {
            if (factory != null && placed.Source is { } source && AsFromFactory(source, factory)) continue;
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

    /// <summary>The part and everything under it down to the block are of the factory build: it sits where the factory put it</summary>
    private static bool AsFromFactory(InstalledPart part, IReadOnlySet<string> factory)
    {
        for (InstalledPart? p = part; p != null; p = p.Parent)
        {
            if (!factory.Contains(p.Definition.Id)) return false;
        }
        return true;
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
    private sealed record Model(CarAnchors? Hubs, ModelFrame? Frame);

    private static readonly ConcurrentDictionary<string, Model> Read = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names of nodes that move on a car (wheels, doors, the hood, the steering...): no frame to find the model by</summary>
    private static readonly string[] Moving =
        ["WHEEL", "SUSP", "HUB", "DISC", "BRAKE", "DOOR", "STEER", "WIPER", "HOOD", "BONNET", "TRUNK", "BOOT", "LIGHT", "FAN",
         "SHIFT", "GEAR", "DRIVER", "CINTURE", "NEEDLE", "ARROW", "MIRROR", "WING", "SPOILER", "FLAP", "HANDBRAKE", "PEDAL"];

    /// <summary>The hubs of the car in <paramref name="carDirectory"/>; null when its model or a wheel node is not there</summary>
    public static CarAnchors? Of(string carDirectory) => Read.GetOrAdd(carDirectory, Load).Hubs;

    /// <summary>
    /// A node of the car's model that does not move on the car, right under its root (none of <see cref="Moving"/>, with
    /// meshes in it), and where it sits in the model; null when there is none
    /// </summary>
    public static ModelFrame? FrameOf(string carDirectory) => Read.GetOrAdd(carDirectory, Load).Frame;

    private static Model Load(string carDirectory)
    {
        try
        {
            var file = AcPaths.GetMainCarFilename(carDirectory, false);
            if (file == null || !File.Exists(file)) return new Model(null, null);

            var kn5 = Kn5.FromFile(file, SkippingTextureLoader.Instance, SkippingMaterialLoader.Instance, HierarchyOnlyNodeLoader.Instance);
            var hubs = WheelFit.HubsOf(kn5);
            var anchors = WheelFit.WheelNodes.All(hubs.ContainsKey)
                ? new CarAnchors(hubs["WHEEL_LF"], hubs["WHEEL_RF"], hubs["WHEEL_LR"], hubs["WHEEL_RR"])
                : null;

            var root = ToNumerics(kn5.RootNode.Transform);
            var frame = kn5.RootNode.Children
                .Where(n => n.NodeClass == Kn5NodeClass.Base && !string.IsNullOrEmpty(n.Name) && HasMesh(n)
                            && !Moving.Any(m => n.Name.Contains(m, StringComparison.OrdinalIgnoreCase)))
                .Select(n => new ModelFrame(n.Name, ToNumerics(n.Transform) * root))
                .FirstOrDefault();
            return new Model(anchors, frame);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // AcTools throws a plain Exception for a file that is no KN5: kept as a car without hubs all the same, not read again
            return new Model(null, null);
        }
    }

    private static bool HasMesh(Kn5Node node) => node.Children.Any(c => c.NodeClass != Kn5NodeClass.Base || HasMesh(c));
}
