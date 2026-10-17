using System.Numerics;
using AcTools.Kn5File;
using AcTools.Numerics;

namespace StreetCorsa.Parts;

/// <summary>
/// The garage's workbench: where a loose part is worked on (the parts view's "Work on it" jobs). A garage prop of its
/// own (<c>props/workbench</c>), placed by garage as any prop is; its model, unless a mod gives one, is drawn here from
/// boxes as the stand is: a wooden top on steel legs with a shelf under it, a tool board along the back and a vise on
/// the front corner. Everything here is in the bench's own space: its middle on the floor at the origin, its front +Z.
/// </summary>
public static class WorkbenchLayout
{
    /// <summary>The prop's id in the mods' props folders</summary>
    public const string PropId = "workbench";

    /// <summary>Height of the bench's top</summary>
    public const float TopHeight = 0.9f;

    /// <summary>Half the bench's length (along X) and depth (along Z)</summary>
    public const float HalfLength = 0.75f;

    public const float HalfDepth = 0.34f;

    /// <summary>
    /// Where a part lies on the top: left of the middle and a little forward, clear of the vise on the right front
    /// corner and of the tool board along the back
    /// </summary>
    public static readonly Vector3 WorkSpot = new(-0.12f, TopHeight, 0.01f);

    /// <summary>How far round <see cref="WorkSpot"/> a part may reach and still turn whole circles without touching the board or the vise</summary>
    public const float ClearRadius = 0.3f;

    /// <summary>How far in front of and behind <see cref="WorkSpot"/> a part may reach: the tool board behind it</summary>
    public const float ClearHalfDepth = 0.3f;

    /// <summary>The most a part too long to turn round sways either way, radians</summary>
    public const float MostSway = 0.35f;

    private const string BenchNode = "workbench";

    /// <summary>Where a part lies on a bench placed by <paramref name="bench"/>: the middle of its top, where the part goes</summary>
    public static Vector3 TopOf(Matrix4x4 bench) => Vector3.Transform(WorkSpot, bench);

    /// <summary>
    /// How a part of this box is turned about the upright at <paramref name="turn"/> radians of its turning: a part that
    /// fits <see cref="ClearRadius"/> turns round; a longer one lies along the bench and sways only as far as stays in
    /// front of the tool board
    /// </summary>
    public static float YawOf(Vector3 min, Vector3 max, float turn)
    {
        var halfX = (max.X - min.X) / 2f;
        var halfZ = (max.Z - min.Z) / 2f;
        if (MathF.Sqrt(halfX * halfX + halfZ * halfZ) <= ClearRadius) return turn;

        // Its long side along the bench
        var along = halfX >= halfZ ? 0f : MathF.PI / 2f;
        var (length, width) = halfX >= halfZ ? (halfX, halfZ) : (halfZ, halfX);

        // Turned by a, its corners reach length * sin a + width * cos a forward and back
        var sway = 0f;
        for (var a = 0.01f; a <= MostSway; a += 0.01f)
        {
            if (length * MathF.Sin(a) + width * MathF.Cos(a) > ClearHalfDepth) break;
            sway = a;
        }

        return along + sway * MathF.Sin(turn);
    }

    /// <summary>
    /// A model lying on a bench placed by <paramref name="bench"/>: its middle over <see cref="WorkSpot"/>, its lowest
    /// point on the wood, turned as <see cref="YawOf"/> has it and raised <paramref name="lift"/> above the top (on its
    /// way down to it)
    /// </summary>
    public static Matrix4x4 PlacementOf(Matrix4x4 bench, Vector3 min, Vector3 max, float turn, float lift)
    {
        var centre = (min + max) / 2f;
        return Matrix4x4.CreateTranslation(-centre.X, -min.Y, -centre.Z)
               * Matrix4x4.CreateRotationY(YawOf(min, max, turn))
               * Matrix4x4.CreateTranslation(WorkSpot + new Vector3(0f, lift, 0f))
               * bench;
    }

    /// <summary>The bench drawn from boxes, as a model of its own</summary>
    public static IKn5 CreateModel()
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = BenchNode;
        AddTo(kn5);
        return kn5;
    }

    /// <summary>The bench added to a model, as a node of its own that is no part's</summary>
    private static void AddTo(IKn5 kn5)
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
        var node = Kn5Node.CreateBaseNode(BenchNode + "_boxes");
        node.Children.Add(PlaceholderMesh.Boxes("bench_wood", wood, timber));
        node.Children.Add(PlaceholderMesh.Boxes("bench_steel", steel, paint));
        node.Transform = new Mat4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        kn5.RootNode.Children.Add(node);
    }
}
