using System.Numerics;
using AcTools.Numerics;

namespace StreetCorsa.Helpers;

/// <summary>A matrix in each of the three kinds the game works with: System.Numerics, AcTools' KN5 files and SlimDX's renderer</summary>
public static class Matrices
{
    public static Matrix4x4 ToNumerics(Mat4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);

    public static Matrix4x4 ToNumerics(SlimDX.Matrix m) => new(
        m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);

    public static Mat4x4 ToMat4x4(Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);

    public static SlimDX.Matrix ToSlim(Matrix4x4 m) => new()
    {
        M11 = m.M11, M12 = m.M12, M13 = m.M13, M14 = m.M14,
        M21 = m.M21, M22 = m.M22, M23 = m.M23, M24 = m.M24,
        M31 = m.M31, M32 = m.M32, M33 = m.M33, M34 = m.M34,
        M41 = m.M41, M42 = m.M42, M43 = m.M43, M44 = m.M44
    };
}
