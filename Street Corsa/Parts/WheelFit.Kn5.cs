using System.Numerics;
using AcTools.Kn5File;
using static StreetCorsa.Helpers.Matrices;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// The part of <see cref="WheelFit"/> that reads a car's model: its wheel hubs as the KN5 has them. Apart from the rest
/// (<c>Parts/Cars/WheelFit.cs</c>), which the tools link and which reads no model.
/// </summary>
public static partial class WheelFit
{
    /// <summary>
    /// Each wheel node's place in the model (<see cref="WheelNodes"/>, by name), the first of each name: the main LOD's,
    /// which comes first in a car's KN5
    /// </summary>
    public static Dictionary<string, Vector3> HubsOf(IKn5 model)
    {
        var hubs = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        Walk(model.RootNode, Matrix4x4.Identity, hubs);
        return hubs;
    }

    private static void Walk(Kn5Node node, Matrix4x4 parent, Dictionary<string, Vector3> hubs)
    {
        var world = node.NodeClass == Kn5NodeClass.Base ? ToNumerics(node.Transform) * parent : parent;
        if (node.NodeClass == Kn5NodeClass.Base && Array.IndexOf(WheelNodes, node.Name) >= 0) hubs.TryAdd(node.Name, world.Translation);
        foreach (var child in node.Children) Walk(child, world, hubs);
    }
}
