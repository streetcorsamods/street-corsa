using AcTools.Render.Kn5Specific.Objects;
using SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// The wind over a scene: what its model keeps under a node named <see cref="SwayPrefix"/> (the grass clumps on the flat
/// land round the gas station, a cell of them to a node; each tree) leans with the gusts, the node sheared about its
/// foot so its base stays in the ground and its top moves. tools/Shared/PreparedScene.cs writes the nodes, and nothing
/// under them casts a shadow; a mod's that does has its shadow drawn again as the attendant's is, ten times a second.
/// </summary>
public partial class GarageRenderer
{
    /// <summary>What a scene's nodes the wind moves are named after (PreparedScene.SwayPrefix is the same)</summary>
    public const string SwayPrefix = "SWAY_";

    private List<Swaying>? _sway;
    private object? _swayOf;
    private bool _swayCasts;
    private float _swayShadowAge;
    private float _windSeconds;

    /// <summary>The scene has something the wind moves: it is drawn on while it stands</summary>
    public bool WindBlows => Sway.Count > 0;

    /// <summary>
    /// A node the wind leans: its own matrix as made, where it stands in the world, and how the world's lean (x, z) reads in
    /// its own space (a piece of the dressing may stand turned and scaled, under a dummy of its own)
    /// </summary>
    private readonly record struct Swaying(Kn5RenderableList Node, Matrix Home, float X, float Z, Vector2 LeanX, Vector2 LeanZ);

    private List<Swaying> Sway
    {
        get
        {
            if (_sway == null || !ReferenceEquals(_swayOf, ShowroomNode))
            {
                _swayOf = ShowroomNode;
                // The scene's, and the dressing's (a street's pieces): each leans as the wind is where it stands in the world
                _sway = (ShowroomNode?.Dummies.Where(IsSway) ?? [])
                    .Concat(_dressing.SelectMany(f => f.Dummies.Where(IsSway)))
                    .Select(SwayingOf)
                    .ToList();
                _swayCasts = _sway.Any(s => s.Node.GetAllChildren().OfType<Kn5RenderableObject>().Any(m => m.OriginalNode.CastShadows));
            }

            return _sway;
        }
    }

    private static bool IsSway(Kn5RenderableList node) => node.Name?.StartsWith(SwayPrefix, StringComparison.Ordinal) == true;

    private static Swaying SwayingOf(Kn5RenderableList node)
    {
        // The node's world as made: a lean of the world's (per metre of the world's height) is the world's way turned back
        // into the node's own, times its scale (a metre of its own height is that many of the world's)
        var world = node.LocalMatrix * node.ParentMatrix;
        var inverse = Matrix.Invert(world);
        var scale = Vector3.TransformNormal(Vector3.UnitY, world).Length();
        var x = Vector3.TransformNormal(Vector3.UnitX, inverse) * scale;
        var z = Vector3.TransformNormal(Vector3.UnitZ, inverse) * scale;
        return new Swaying(node, node.LocalMatrix, world.M41, world.M43, new Vector2(x.X, x.Z), new Vector2(z.X, z.Z));
    }

    /// <summary>The wind blown on by <paramref name="dt"/> seconds, every swaying node leaned as it is now; true when anything moved</summary>
    public bool AdvanceWind(float dt)
    {
        var sway = Sway;
        if (sway.Count == 0) return false;

        _windSeconds += Math.Min(dt, MaxTickSeconds);
        foreach (var s in sway)
        {
            var (leanX, leanZ) = WindSway.LeanAt(_windSeconds, s.X, s.Z);
            var lean = s.LeanX * leanX + s.LeanZ * leanZ;
            var shear = Matrix.Identity;
            shear.M21 = lean.X;
            shear.M23 = lean.Y;
            s.Node.LocalMatrix = shear * s.Home;
        }

        if (_swayCasts)
        {
            // Something leaning casts a shadow (nothing of the game's own scenes does: each redraw is a shadow pass over
            // the whole scene), which would otherwise stand still until the car moved
            _swayShadowAge += dt;
            if (_swayShadowAge >= ActorShadowSeconds)
            {
                _swayShadowAge = 0f;
                SetShadowsDirty();
            }
        }

        IsDirty = true;
        return true;
    }
}

/// <summary>
/// How far the wind leans what stands in it, as a shear: a point's height times the lean is how far it moves. A steady
/// breeze with gusts rolling across the land along the road (towards +x, a little towards the road), and a flutter
/// on top; the lean stays small, as grass in a breeze does, and never the same two places at once.
/// </summary>
public static class WindSway
{
    private const float Steady = 0.02f;
    private const float Gust = 0.045f;
    private const float Flutter = 0.012f;

    // The wind's way: along the road, a little towards it
    private const float WayX = 0.9f;
    private const float WayZ = 0.44f;

    /// <summary>The most anything leans, as a share of its height</summary>
    public const float Most = Steady + Gust + Flutter + 0.008f;

    /// <summary>The lean (x, z, as shares of a point's height) at <paramref name="seconds"/> for what stands at (x, z)</summary>
    public static (float X, float Z) LeanAt(float seconds, float x, float z)
    {
        // A gust is a wave rolling with the wind: later the further along it
        var along = x * WayX + z * WayZ;
        var gust = 0.5f + 0.5f * MathF.Sin(0.55f * seconds - along / 24f) * MathF.Sin(0.21f * seconds - along / 90f + 1f);
        var flutter = MathF.Sin(2.9f * seconds + x * 0.37f + z * 0.23f);
        var strength = Steady + Gust * gust + Flutter * flutter;
        var across = 0.008f * MathF.Sin(2.3f * seconds + x * 0.5f - z * 0.3f);
        return (strength * WayX - across * WayZ, strength * WayZ + across * WayX);
    }
}
