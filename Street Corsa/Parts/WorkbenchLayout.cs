using System.Numerics;
using AcTools.Kn5File;
using AcTools.Numerics;

namespace StreetCorsa.Parts;

/// <summary>
/// The garage's workbench, behind the engine stand: where a loose part is worked on (the parts view's "Work on it"
/// jobs). Drawn from boxes as the stand is, a wooden top on steel legs with a shelf under it, always there with the
/// stand (<see cref="EngineStandLayout.AddTo"/>).
/// </summary>
public static class WorkbenchLayout
{
    /// <summary>The middle of the bench on the floor: behind the stand (the car's rear is -Z), clear of its base</summary>
    public static readonly Vector3 Spot = new(EngineStandLayout.Spot.X, 0f, -1.75f);

    /// <summary>Height of the bench's top</summary>
    public const float TopHeight = 0.9f;

    /// <summary>Half the bench's length (along X) and depth (along Z)</summary>
    public const float HalfLength = 0.75f;

    public const float HalfDepth = 0.34f;

    /// <summary>Where a part lies on the bench: the middle of its top</summary>
    public static Vector3 Top => Spot + new Vector3(0f, TopHeight, 0f);

    private const string BenchNode = "workbench";

    /// <summary>
    /// A model lying on the bench: its middle over <see cref="Top"/>, its lowest point on the wood, turned
    /// <paramref name="yaw"/> about the upright and raised <paramref name="lift"/> above it (on its way down to it)
    /// </summary>
    public static Matrix4x4 PlacementOf(Vector3 min, Vector3 max, float yaw, float lift)
    {
        var centre = (min + max) / 2f;
        return Matrix4x4.CreateTranslation(-centre.X, -min.Y, -centre.Z)
               * Matrix4x4.CreateRotationY(yaw)
               * Matrix4x4.CreateTranslation(Top + new Vector3(0f, lift, 0f));
    }

    /// <summary>The bench added to a model, as a node of its own that is no part's</summary>
    public static void AddTo(IKn5 kn5)
    {
        const float top = TopHeight;
        const float l = HalfLength;
        const float d = HalfDepth;
        const float leg = 0.025f;
        const float shelf = 0.18f;

        var wood = new List<(Vector3 Centre, Vector3 Half)>
        {
            (new(0f, top - 0.025f, 0f), new(l, 0.025f, d)),
            (new(0f, shelf, 0f), new(l - 0.03f, 0.012f, d - 0.03f)),
            // A board along the back to hang tools on
            (new(0f, top + 0.22f, -d + 0.01f), new(l, 0.22f, 0.01f))
        };

        var steel = new List<(Vector3 Centre, Vector3 Half)>();
        foreach (var x in new[] { -l + 0.04f, l - 0.04f })
        {
            foreach (var z in new[] { -d + 0.04f, d - 0.04f })
                steel.Add((new(x, (top - 0.05f) / 2f, z), new(leg, (top - 0.05f) / 2f, leg)));

            // The rails that hold the shelf
            steel.Add((new(x, shelf - 0.02f, 0f), new(leg, 0.012f, d - 0.04f)));
        }

        // A vise bolted to the front corner, its jaws, and its handle
        steel.Add((new(l - 0.18f, top + 0.05f, d - 0.06f), new(0.07f, 0.05f, 0.06f)));
        steel.Add((new(l - 0.18f, top + 0.12f, d + 0.0f), new(0.08f, 0.03f, 0.012f)));
        steel.Add((new(l - 0.18f, top + 0.06f, d + 0.06f), new(0.11f, 0.008f, 0.008f)));

        var timber = PlaceholderMesh.SolidMaterial(kn5, "bench_wood", 0x6E, 0x4A, 0x2C, 0.45f, 0.55f, 0.08f, 12f);
        var paint = PlaceholderMesh.SolidMaterial(kn5, "bench_steel", 0x2E, 0x4A, 0x3A, 0.4f, 0.55f, 0.3f, 40f);
        var node = Kn5Node.CreateBaseNode(BenchNode);
        node.Children.Add(PlaceholderMesh.Boxes("bench_wood", wood, timber));
        node.Children.Add(PlaceholderMesh.Boxes("bench_steel", steel, paint));
        node.Transform = new Mat4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, Spot.X, Spot.Y, Spot.Z, 1);
        kn5.RootNode.Children.Add(node);
    }
}
