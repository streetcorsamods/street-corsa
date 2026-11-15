using System.IO;

namespace StreetCorsa.Services.Race.Traffic
{
    /// <summary>
    /// A track's AI line (<c>ai\fast_lane.ai</c>, version 7) as far as a traffic plan needs it: every point's position, how
    /// far the road reaches to its left and right, and the road's normal there. The file is a 16-byte header (version,
    /// point count, lane count, one more int), 20 bytes a point (position, distance, id), then ONE int count and 72 bytes
    /// of extras a point (18 floats: speed, gas, brake, an obsolete lateral g, radius, side left, side right, camber,
    /// direction, the normal, length, the forward vector, tag, grade). What follows (a spatial lookup grid) is not read.
    /// </summary>
    public sealed class AiLine
    {
        /// <summary>The file a track's AI line is, in its (or its layout's) <c>ai</c> folder</summary>
        public const string FileName = "fast_lane.ai";

        private const int Version = 7;
        private const int HeaderBytes = 16;
        private const int PointBytes = 20;
        private const int ExtraFloats = 18;
        private const int SideLeftField = 5;
        private const int SideRightField = 6;
        private const int NormalField = 9;

        /// <summary>One point of the line: where it is, the road's reach to either side of it, in metres, and its normal</summary>
        public readonly record struct Point(double X, double Y, double Z, double SideLeft, double SideRight, double NormalX, double NormalY, double NormalZ);

        public IReadOnlyList<Point> Points { get; }

        private AiLine(IReadOnlyList<Point> points) => Points = points;

        /// <summary>
        /// The AI line of a road: the layout's <c>ai\fast_lane.ai</c>, else the track's; null when it has none
        /// </summary>
        public static string? FileOf(string trackFolder, string? layout)
        {
            if (string.IsNullOrEmpty(trackFolder)) return null;
            var candidates = string.IsNullOrEmpty(layout)
                ? [Path.Combine(trackFolder, "ai", FileName)]
                : new[] { Path.Combine(trackFolder, layout, "ai", FileName), Path.Combine(trackFolder, "ai", FileName) };
            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>The line in a file; throws <see cref="InvalidDataException"/> for a file that is not one</summary>
        public static AiLine Read(string file) => Parse(File.ReadAllBytes(file));

        /// <summary>The line in a file's bytes; throws <see cref="InvalidDataException"/> for bytes that are not one</summary>
        public static AiLine Parse(byte[] bytes)
        {
            if (bytes.Length < HeaderBytes + 4) throw new InvalidDataException("too short for an AI line");
            var version = BitConverter.ToInt32(bytes, 0);
            if (version != Version) throw new InvalidDataException($"AI line version {version}, not {Version}");

            var count = BitConverter.ToInt32(bytes, 4);
            if (count < 2 || HeaderBytes + (long)count * PointBytes + 4 > bytes.Length) throw new InvalidDataException($"an AI line of {count} point(s)");

            var extrasAt = HeaderBytes + count * PointBytes;
            var extras = BitConverter.ToInt32(bytes, extrasAt);
            extrasAt += 4;
            // Without one extra a point there are no widths to place lanes by
            if (extras < count || extrasAt + (long)extras * ExtraFloats * 4 > bytes.Length) throw new InvalidDataException($"an AI line of {count} point(s) with {extras} extra(s)");

            var points = new Point[count];
            for (var i = 0; i < count; i++)
            {
                var at = HeaderBytes + i * PointBytes;
                var extra = extrasAt + i * ExtraFloats * 4;
                float F(int field) => BitConverter.ToSingle(bytes, extra + field * 4);
                points[i] = new Point(BitConverter.ToSingle(bytes, at), BitConverter.ToSingle(bytes, at + 4), BitConverter.ToSingle(bytes, at + 8),
                    F(SideLeftField), F(SideRightField), F(NormalField), F(NormalField + 1), F(NormalField + 2));
            }
            return new AiLine(points);
        }

        /// <summary>A line made of given points, for tests</summary>
        public static AiLine Of(IReadOnlyList<Point> points) => new(points);
    }
}
