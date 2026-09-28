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

            // Between the four cells round the point, the ones with ground counting by how near they are
            float sum = 0f, weight = 0f;
            foreach (var (dc, dr, w) in new[] { (0, 0, (1 - fu) * (1 - fv)), (1, 0, fu * (1 - fv)), (0, 1, (1 - fu) * fv), (1, 1, fu * fv) })
            {
                var c = column + dc;
                var r = row + dr;
                if (c < 0 || r < 0 || c >= _columns || r >= _rows) continue;
                var height = _heights[r * _columns + c];
                if (!float.IsFinite(height) || w <= 0f) continue;
                sum += height * w;
                weight += w;
            }

            return weight > 0f ? sum / weight : 0f;
        }
    }
}
