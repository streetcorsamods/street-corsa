using AcTools.Render.Kn5Specific.Objects;
using SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// The wind over a scene: what its model keeps under a node named <see cref="SwayPrefix"/> (the grass clumps near the
/// gas station's forecourt, a cell of them to a node; each tree) leans with the gusts, the node sheared about its
/// foot so its base stays in the ground and its top moves. tools/Shared/PreparedScene.cs writes the nodes.
/// </summary>
public partial class GarageRenderer
{
    /// <summary>What a scene's nodes the wind moves are named after (PreparedScene.SwayPrefix is the same)</summary>
    public const string SwayPrefix = "SWAY_";

    private List<(Kn5RenderableList Node, Matrix Home, float X, float Z)>? _sway;
    private object? _swayOf;
    private float _windSeconds;

    /// <summary>The scene has something the wind moves: it is drawn on while it stands</summary>
    public bool WindBlows => Sway.Count > 0;

    private List<(Kn5RenderableList Node, Matrix Home, float X, float Z)> Sway
    {
        get
        {
            if (_sway == null || !ReferenceEquals(_swayOf, ShowroomNode))
            {
                _swayOf = ShowroomNode;
                _sway = ShowroomNode?.Dummies.Where(d => d.Name?.StartsWith(SwayPrefix, StringComparison.Ordinal) == true)
                    .Select(d => (d, d.LocalMatrix, d.LocalMatrix.M41, d.LocalMatrix.M43)).ToList() ?? new();
            }

            return _sway;
        }
    }

    /// <summary>The wind blown on by <paramref name="dt"/> seconds, every swaying node leaned as it is now; true when anything moved</summary>
    public bool AdvanceWind(float dt)
    {
        var sway = Sway;
        if (sway.Count == 0) return false;

        _windSeconds += Math.Min(dt, MaxTickSeconds);
        foreach (var (node, home, x, z) in sway)
        {
            var (leanX, leanZ) = WindSway.LeanAt(_windSeconds, x, z);
            var shear = Matrix.Identity;
            shear.M21 = leanX;
            shear.M23 = leanZ;
            node.LocalMatrix = shear * home;
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
