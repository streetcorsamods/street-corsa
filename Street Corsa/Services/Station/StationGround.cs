using System.IO;
using StreetCorsa.Logging;

namespace StreetCorsa.Services.Station
{
    /// <summary>
    /// The height of a scene's ground, from the file tools/StationScene writes next to its model (ground.bin): "SCG1",
    /// the cell size, the columns and rows, the lowest x and z, then a height a cell, row by row (z, then x), NaN where
    /// there is none. Read between the cells; a point off the grid or without ground is at 0, the middle's height.
    /// </summary>
    public sealed class StationGround
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Station");

        private readonly float _cell;
        private readonly int _columns;
        private readonly int _rows;
        private readonly float _minX;
        private readonly float _minZ;
        private readonly float[] _heights;

        /// <summary>Flat ground at 0: a station without its file</summary>
        public static StationGround Flat { get; } = new(1f, 0, 0, 0f, 0f, []);

        /// <summary>
        /// The most cells a ground may have (a 2 km square at the metre, 16 MB of heights): a header asking for more is a
        /// file that makes no sense, however long it is, and nothing that big is made room for
        /// </summary>
        public const long MaxCells = 4_000_000;

        public StationGround(float cell, int columns, int rows, float minX, float minZ, float[] heights)
        {
            _cell = cell;
            _columns = columns;
            _rows = rows;
            _minX = minX;
            _minZ = minZ;
            _heights = heights;
        }

        /// <summary>The ground in the file; flat when there is none or it cannot be read (logged)</summary>
        public static StationGround Load(string file)
        {
            try
            {
                if (!File.Exists(file)) return Flat;
                using var reader = new BinaryReader(File.OpenRead(file));
                if (new string(reader.ReadChars(4)) != "SCG1") throw new InvalidDataException("not a ground file");

                var cell = reader.ReadSingle();
                var columns = reader.ReadInt32();
                var rows = reader.ReadInt32();
                var minX = reader.ReadSingle();
                var minZ = reader.ReadSingle();
                if (!(cell > 0) || columns is <= 0 or > 10000 || rows is <= 0 or > 10000) throw new InvalidDataException("a grid that makes no sense");
                if ((long)columns * rows > MaxCells) throw new InvalidDataException($"a {columns} x {rows} grid is more than {MaxCells} cells");

                // The heights the header asks for must be what the file holds, before room is made for them: a mod's
                // 24-byte file can ask for a hundred million
                var stream = reader.BaseStream;
                if (stream.Length - stream.Position != 4L * columns * rows)
                    throw new InvalidDataException($"a {columns} x {rows} grid in a file of {stream.Length} bytes");

                var heights = new float[columns * rows];
                for (var i = 0; i < heights.Length; i++) heights[i] = reader.ReadSingle();
                return new StationGround(cell, columns, rows, minX, minZ, heights);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "The ground {Path} could not be read: the station is flat", file);
                return Flat;
            }
        }

        /// <summary>The ground's height at a point, metres</summary>
        public float HeightAt(float x, float z)
        {
            if (_heights.Length == 0) return 0f;

            var u = (x - _minX) / _cell;
            var v = (z - _minZ) / _cell;
            var column = (int)MathF.Floor(u);
            var row = (int)MathF.Floor(v);
            var fu = u - column;
            var fv = v - row;

            // Between the four cells round the point, the ones with ground counting by how near they are (unrolled: this
            // is asked four times a frame while a car drives)
            float sum = 0f, weight = 0f;
            Add(column, row, (1 - fu) * (1 - fv));
            Add(column + 1, row, fu * (1 - fv));
            Add(column, row + 1, (1 - fu) * fv);
            Add(column + 1, row + 1, fu * fv);
            return weight > 0f ? sum / weight : 0f;

            void Add(int c, int r, float w)
            {
                if (c < 0 || r < 0 || c >= _columns || r >= _rows) return;
                var height = _heights[r * _columns + c];
                if (!float.IsFinite(height) || w <= 0f) return;
                sum += height * w;
                weight += w;
            }
        }
    }
}
