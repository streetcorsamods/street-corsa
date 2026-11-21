using System.Numerics;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Station;

namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// Which of a street's pieces a scene stands up: those within sight of where the car stands and of the ways it drives
    /// in and out. A town of streets (Street Rod's, docs/street-rod-town.md) lays out more pieces than the renderer can
    /// draw a frame (each its meshes, its materials, its shadow): every viewport sees one corner of it, and the blocks
    /// beyond are hidden behind the near ones. A street the size of one corner keeps every piece.
    /// </summary>
    public static class StreetReach
    {
        /// <summary>How far round where the car stands the pieces are stood up, metres</summary>
        public const float AroundStand = 140f;

        /// <summary>How far either side of a way the car drives in or out along the pieces are stood up, metres</summary>
        public const float AlongWay = 60f;

        /// <summary>
        /// A small piece (a parked car, a lamp post, a pole, a signal, a hydrant: nothing over <see cref="SmallSize"/> across
        /// or <see cref="SmallHeight"/> high) is stood up this much nearer, a share of the reach: a few pixels a block off,
        /// it is most of the meshes a street draws
        /// </summary>
        public const float SmallShare = 0.6f;

        public const float SmallSize = 8f;

        public const float SmallHeight = 12f;

        /// <summary>
        /// How far round where the car stands a street's own model (its stage) is drawn, metres: a town's reaches further
        /// than anything seen over the near blocks (the gas station's, seen whole to its crest, has no reach)
        /// </summary>
        public const float ModelReach = 260f;

        /// <summary>
        /// The pieces within <see cref="AroundStand"/> of <paramref name="stand"/> or <see cref="AlongWay"/> of a way, the
        /// small ones of <paramref name="kit"/> (when given) within <see cref="SmallShare"/> of that
        /// </summary>
        public static IReadOnlyList<PlacedPiece> Around(IReadOnlyList<PlacedPiece> pieces, Vector2 stand, IEnumerable<IReadOnlyList<GroundPoint>>? ways = null,
            StreetKit? kit = null)
        {
            var segments = (ways ?? []).SelectMany(Segments).ToList();
            return pieces.Where(p =>
            {
                var share = kit?.Find(p.Piece) is { } piece && IsSmall(piece, p.Scale) ? SmallShare : 1f;
                var (around, along) = (AroundStand * share, AlongWay * share);
                var at = new Vector2(p.X, p.Z);
                return Vector2.DistanceSquared(at, stand) <= around * around
                       || segments.Any(s => DistanceSquared(at, s.A, s.B) <= along * along);
            }).ToList();
        }

        /// <summary>A piece no bigger than a parked car or a lamp post, as it stands</summary>
        public static bool IsSmall(StreetPiece piece, float scale) =>
            MathF.Max(piece.Width, piece.Depth) * scale <= SmallSize && piece.Height * scale <= SmallHeight;

        private static IEnumerable<(Vector2 A, Vector2 B)> Segments(IReadOnlyList<GroundPoint> way)
        {
            for (var k = 0; k + 1 < way.Count; k++)
                yield return (new Vector2(way[k].X, way[k].Z), new Vector2(way[k + 1].X, way[k + 1].Z));
            if (way.Count == 1) yield return (new Vector2(way[0].X, way[0].Z), new Vector2(way[0].X, way[0].Z));
        }

        /// <summary>A point's distance from a stretch, squared</summary>
        internal static float DistanceSquared(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var length = ab.LengthSquared();
            var t = length <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / length, 0f, 1f);
            return Vector2.DistanceSquared(p, a + ab * t);
        }
    }
}
