using AcTools.Kn5File;
using AcTools.Numerics;
using StreetCorsa.Helpers;

namespace StreetCorsa.Tools;

/// <summary>
/// The ground's height over a square round the middle of the scene, from the faces of the scene that look up, as the
/// game reads it (Services/Station/StationGround.cs): "SCG1", the cell size, the columns and rows, the lowest x and z,
/// then a height a cell (row by row, z then x), NaN where there is no ground.
///
/// A cell's ground is its lowest upward face, or a face a little above it (a kerb, the road over the terrain, the
/// markings on it): a roof, a canopy or a car park's upper deck, well above, is not ground. The track's physics mesh
/// would be the right thing, but it has only the race's roads: no forecourt.
/// </summary>
internal static class Ground
{
    public const float Cell = 0.5f;

    /// <summary>A face this much over the lowest in its cell is still the ground</summary>
    private const float Above = 0.6f;

    /// <summary>Faces further than this above or below the middle are not the scene's ground (a track has odd meshes deep under it)</summary>
    private const float Band = 40f;

    /// <summary>A face tilted more than this (its normal's height under it) is a wall, not ground</summary>
    private const float Upward = 0.7f;

    public sealed class Grid(int size, float half)
    {
        public readonly int Size = size;
        public readonly float Half = half;
        public readonly float[] Heights = Filled(size);

        private static float[] Filled(int size)
        {
            var heights = new float[size * size];
            Array.Fill(heights, float.NaN);
            return heights;
        }

        /// <summary>The ground at a point, or the nearest cell within reach that has some</summary>
        public float? Nearest(float x, float z, float reach)
        {
            float? best = null;
            var bestDistance = float.MaxValue;
            var cells = (int)MathF.Ceiling(reach / Cell);
            var (cx, cz) = ((int)MathF.Round((x + Half) / Cell), (int)MathF.Round((z + Half) / Cell));
            for (var row = Math.Max(0, cz - cells); row <= Math.Min(Size - 1, cz + cells); row++)
            {
                for (var column = Math.Max(0, cx - cells); column <= Math.Min(Size - 1, cx + cells); column++)
                {
                    var height = Heights[row * Size + column];
                    var distance = (row - cz) * (row - cz) + (column - cx) * (column - cx);
                    if (float.IsNaN(height) || distance >= bestDistance) continue;
                    best = height;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public void Lower(float by)
        {
            for (var i = 0; i < Heights.Length; i++) Heights[i] -= by;
        }

        /// <summary>Whole or not at all (SafeFile): a builder stopped half way leaves the shipped ground as it was</summary>
        public void Write(string file)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write("SCG1"u8);
                writer.Write(Cell);
                writer.Write(Size);
                writer.Write(Size);
                writer.Write(-Half);
                writer.Write(-Half);
                foreach (var height in Heights) writer.Write(height);
            }

            SafeFile.WriteAllBytes(file, bytes.ToArray());
        }
    }

    /// <summary>The ground under the upward faces of the meshes, in the scene's space</summary>
    public static Grid Build(IReadOnlyList<Kn5Node> meshes, float half)
    {
        var grid = new Grid((int)MathF.Ceiling(2 * half / Cell) + 1, half);
        var lowest = new float[grid.Heights.Length];
        Array.Fill(lowest, float.NaN);

        // First the lowest face in every cell, then the highest face near it
        foreach (var pass in new[] { 0, 1 })
        {
            foreach (var mesh in meshes)
            {
                for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
                {
                    var a = mesh.Vertices[mesh.Indices[i]].Position;
                    var b = mesh.Vertices[mesh.Indices[i + 1]].Position;
                    var c = mesh.Vertices[mesh.Indices[i + 2]].Position;
                    var normal = Vec3.Cross(b - a, c - a);
                    var length = normal.Length();
                    if (length < 1e-9f || MathF.Abs(normal.Y) / length < Upward) continue;
                    if (MathF.Abs(a.Y) > Band || MathF.Abs(b.Y) > Band || MathF.Abs(c.Y) > Band) continue;

                    Rasterise(grid, a, b, c, (index, y) =>
                    {
                        if (pass == 0)
                        {
                            if (float.IsNaN(lowest[index]) || y < lowest[index]) lowest[index] = y;
                        }
                        else if (y <= lowest[index] + Above && (float.IsNaN(grid.Heights[index]) || y > grid.Heights[index]))
                        {
                            grid.Heights[index] = y;
                        }
                    });
                }
            }
        }

        return grid;
    }

    private static void Rasterise(Grid grid, Vec3 a, Vec3 b, Vec3 c, Action<int, float> cell)
    {
        var half = grid.Half;
        var fromX = Math.Max(0, (int)MathF.Floor((Math.Min(a.X, Math.Min(b.X, c.X)) + half) / Cell));
        var toX = Math.Min(grid.Size - 1, (int)MathF.Ceiling((Math.Max(a.X, Math.Max(b.X, c.X)) + half) / Cell));
        var fromZ = Math.Max(0, (int)MathF.Floor((Math.Min(a.Z, Math.Min(b.Z, c.Z)) + half) / Cell));
        var toZ = Math.Min(grid.Size - 1, (int)MathF.Ceiling((Math.Max(a.Z, Math.Max(b.Z, c.Z)) + half) / Cell));
        for (var row = fromZ; row <= toZ; row++)
        {
            for (var column = fromX; column <= toX; column++)
            {
                if (Height(a, b, c, column * Cell - half, row * Cell - half) is { } y) cell(row * grid.Size + column, y);
            }
        }
    }

    /// <summary>The height of a triangle over a point, seen from above; null when the point is outside it (the game's own probe)</summary>
    private static float? Height(Vec3 a, Vec3 b, Vec3 c, float x, float z) =>
        Triangles.HeightAt(new(a.X, a.Y, a.Z), new(b.X, b.Y, b.Z), new(c.X, c.Y, c.Z), x, z);
}
