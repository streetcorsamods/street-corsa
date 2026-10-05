using StreetCorsa.Services.Station;

namespace StreetCorsa.Services.Street
{
    /// <summary>The ways a car drives through a scene (a gas station's, the diner's), as their files give them, read the one way</summary>
    internal static class ScenePaths
    {
        /// <summary>A way that gets a car somewhere: two points at least, not all on one spot</summary>
        internal static bool GoesSomewhere(IReadOnlyList<GroundPoint> points) =>
            points.Count >= 2 && points.Any(p => MathF.Abs(p.X - points[0].X) + MathF.Abs(p.Z - points[0].Z) >= 1f);

        /// <summary>The <c>[x, z]</c> points of a file's way; one that is not two numbers is skipped</summary>
        internal static List<GroundPoint> Points(List<float[]>? points) =>
            (points ?? []).Where(p => p is { Length: 2 } && float.IsFinite(p[0]) && float.IsFinite(p[1])).Select(p => new GroundPoint(p[0], p[1])).ToList();
    }
}
