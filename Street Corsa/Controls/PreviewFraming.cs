using System.IO;
using System.Numerics;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Controls;

/// <summary>
/// How the previews frame what they show: a part alone on the studio floor (<see cref="PartViewport3D"/>), a car stood
/// where its seller has it (<see cref="CarViewingViewport3D"/>). Pure figures, so the tests can hold them to it.
///
/// The orbit is AcTools' own: the camera stands at target + radius * (cos beta cos alpha, sin beta, cos beta sin alpha).
/// </summary>
public static class PreviewFraming
{
    /// <summary>The smallest part is framed as if it were this big across: a bolt is not blown up to fill the picture</summary>
    public const float MinPartRadius = 0.06f;

    /// <summary>How much of the picture's narrower way the part's sphere fills, framed</summary>
    public const float PartFill = 0.88f;

    /// <summary>How far in and out the wheel goes, as shares of the framed distance</summary>
    public const float PartZoomIn = 0.45f;
    public const float PartZoomOut = 2.4f;

    /// <summary>
    /// Where a part's model goes so it stands in the middle of the studio: its box's middle over the origin, its bottom
    /// on the floor. The renderer's shadows are centred on the origin when there is no car, so the part has them.
    /// </summary>
    public static Vector3 PartOffset(Vector3 min, Vector3 max) => new(-(min.X + max.X) / 2f, -min.Y, -(min.Z + max.Z) / 2f);

    /// <summary>The point the camera turns round: the middle of the part as it stands on the floor</summary>
    public static Vector3 PartTarget(Vector3 min, Vector3 max) => new(0f, (max.Y - min.Y) / 2f, 0f);

    /// <summary>The sphere round a box, no smaller than <see cref="MinPartRadius"/></summary>
    public static float BoundingRadius(Vector3 min, Vector3 max) => Math.Max((max - min).Length() / 2f, MinPartRadius);

    /// <summary>
    /// How far from its middle the camera stands for a sphere of <paramref name="radius"/> to fill
    /// <paramref name="fill"/> of the picture's narrower way, with a lens <paramref name="fovY"/> radians high on a
    /// picture <paramref name="aspect"/> wide to its height
    /// </summary>
    public static float FitDistance(float radius, float fovY, float aspect, float fill = PartFill)
    {
        var halfY = fovY / 2f;
        var halfX = MathF.Atan(MathF.Tan(halfY) * Math.Max(0.1f, aspect));
        var half = Math.Min(halfY, halfX) * Math.Clamp(fill, 0.1f, 1f);
        return radius / MathF.Sin(half);
    }

    /// <summary>
    /// How far from its middle the camera stands for a part of <paramref name="size"/> to fill <paramref name="fill"/>
    /// of the picture, seen from <paramref name="beta"/> above level as it turns: across, the widest it gets as it turns
    /// (the diagonal of its footprint); up and down, its height and the far side of its footprint as the camera looks
    /// down on it. A part is never framed smaller than <see cref="MinPartRadius"/> across.
    /// </summary>
    public static float PartDistance(Vector3 size, float fovY, float aspect, float beta, float fill = PartFill)
    {
        var least = MinPartRadius * 2f;
        var (x, y, z) = (Math.Max(size.X, least), Math.Max(size.Y, least), Math.Max(size.Z, least));
        var across = MathF.Sqrt(x * x + z * z);
        var high = y * MathF.Cos(beta) + across * MathF.Sin(Math.Abs(beta));

        var halfY = fovY / 2f;
        var halfX = MathF.Atan(MathF.Tan(halfY) * Math.Max(0.1f, aspect));
        var f = Math.Clamp(fill, 0.1f, 1f);
        var distance = Math.Max(across / 2f / MathF.Tan(halfX * f), high / 2f / MathF.Tan(halfY * f));

        // From the middle: the near side of the part is this much closer, and looks bigger
        return distance + across / 4f;
    }

    /// <summary>
    /// The nearest the camera may see: a little in front of the part however close it is wheeled, never so near that
    /// the depth buffer runs out on a whole engine
    /// </summary>
    public static float NearPlane(float radius) => Math.Clamp(radius * 0.05f, 0.005f, 0.1f);

    /// <summary>
    /// Alpha for an orbit camera that looks at a car, standing heading <paramref name="headingDegrees"/> (0 faces +z),
    /// from <paramref name="fromSide"/> radians round from its left side towards its nose: 0 square off its left, π/2
    /// dead ahead. A car's own +x is its left; turned by its heading, its left is (cos h, 0, -sin h) and its nose
    /// (sin h, 0, cos h), so the way between them is alpha = fromSide - h.
    /// </summary>
    public static float CarCameraAlpha(float headingDegrees, float fromSide) => fromSide - headingDegrees * MathF.PI / 180f;

    /// <summary>
    /// How far back to stand to take in a car <paramref name="length"/> long, with a lens <paramref name="fovY"/> high on
    /// a picture <paramref name="aspect"/> wide: the car about three quarters of the narrower way
    /// </summary>
    public static float CarDistance(float length, float fovY, float aspect) =>
        FitDistance(Math.Max(length, 3f) / 2f * 1.08f, fovY, aspect, 0.92f);
}

/// <summary>
/// A part somebody owns as the preview shows it: the part with whatever is bolted on it, the way it is sold, merged
/// into one model and stood on the studio floor
/// </summary>
public static class PartPreviewModel
{
    /// <summary>
    /// The parts of the tree that have a model of their own to show, placed as they are bolted together; empty when none
    /// has (a part the catalog does not know, or one without a model: the dialog says there is no picture)
    /// </summary>
    public static List<PlacedPart> Placed(PartsCatalog catalog, PartInstance part, Matrix4x4 world)
    {
        if (PartTrees.ToInstalled(catalog, part) is not { } tree) return [];

        // Without their installed part: a part without a model is left out rather than shown as a box
        return PartAssembler.Assemble(tree.Root, world)
            .Where(p => catalog.GetModelPath(p.Part) is { } path && File.Exists(path))
            .Select(p => p with { Source = null })
            .ToList();
    }

    /// <summary>True when something of the part can be shown</summary>
    public static bool HasModel(PartsCatalog catalog, PartInstance part) => Placed(catalog, part, Matrix4x4.Identity).Count > 0;

    /// <summary>
    /// The part merged into one model, stood on the floor in the middle (<see cref="PreviewFraming.PartOffset"/>),
    /// and its box as it stands; null when nothing of it has a model, or the models do not read. Off the UI thread: the
    /// models come off the disk.
    /// </summary>
    public static AssemblyModel? Build(PartsCatalog catalog, PartInstance part)
    {
        try
        {
            var placed = Placed(catalog, part, Matrix4x4.Identity);
            if (placed.Count == 0) return null;

            // Once to measure it, once where it stands: the models are read once, the assembler keeps them
            var measured = PartAssembler.BuildModel(catalog, "preview", placed);
            var offset = Matrix4x4.CreateTranslation(PreviewFraming.PartOffset(measured.Min, measured.Max));
            return PartAssembler.BuildModel(catalog, "preview", placed.Select(p => p with { World = p.World * offset }));
        }
        catch (InvalidOperationException)
        {
            // No part of it has a model that reads
            return null;
        }
    }
}
