using System.Numerics;

namespace StreetCorsa.Parts.Cars;

/// <summary>A mesh's box in a wheel's space: x along the axle, and its radius (the farthest y or z off the axle)</summary>
public readonly record struct AxleBox(float MinX, float MaxX, float Radius)
{
    /// <summary>The box of local corners <paramref name="min"/>..<paramref name="max"/> carried by <paramref name="toSpace"/></summary>
    public static AxleBox Of(Vector3 min, Vector3 max, Matrix4x4 toSpace)
    {
        float minX = float.MaxValue, maxX = float.MinValue, radius = 0;
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
            var p = Vector3.Transform(corner, toSpace);
            minX = Math.Min(minX, p.X);
            maxX = Math.Max(maxX, p.X);
            radius = Math.Max(radius, Math.Max(Math.Abs(p.Y), Math.Abs(p.Z)));
        }
        return new AxleBox(minX, maxX, radius);
    }
}

/// <summary>One of the car's meshes under a wheel node: its names and its box in the wheel's space</summary>
public sealed record WheelMesh(string Name, string? Material, AxleBox Box);

/// <summary>What a wheel's fit comes to: the car's meshes that go, and where the parts' node goes in the wheel</summary>
/// <param name="Hidden">Indices into the car's meshes</param>
/// <param name="Placement">The parts' node in the wheel node's space: scaled, turned to face out, moved along the axle</param>
public sealed record WheelFitting(IReadOnlyList<int> Hidden, Matrix4x4 Placement);

/// <summary>
/// How a part's wheel takes the place of a car's own (the race mode's <c>car_parts.lua</c> does the same in AC; the two
/// are kept to the same rules and tested against the same cars).
///
/// The car's meshes under its wheel node: the tyre and the brake by mesh or material name, the rim everything else. A
/// rim alone keeps the car's tyre and fills the hole the hidden rim leaves: its outer lip as big as the car's, flush with
/// where the car's lip was (no further out than the tyre's sidewall), the barrel going in. A tyre makes the whole wheel
/// the parts', scaled together so the tyre is as big as wanted, centred on the car's tyre. Part models are SLRR's: the
/// axle along X, the face towards -X. A left wheel's outboard side is +X, a right one's -X.
/// </summary>
public static class WheelFit
{
    private static readonly string[] Tyre = ["tyre", "tire", "tread", "wall"];
    private static readonly string[] Brake = ["brake", "drum", "disc", "disk", "caliper", "rotor"];

    /// <summary>A part mesh at least this share of the biggest one's radius is the lip</summary>
    private const float LipShare = 0.9f;

    /// <summary>AC's wheels in its order (0 left front, 1 right front, 2 left rear, 3 right rear)</summary>
    public static readonly string[] WheelNodes = ["WHEEL_LF", "WHEEL_RF", "WHEEL_LR", "WHEEL_RR"];

    public static bool IsTyre(WheelMesh mesh) => Named(mesh.Name, Tyre) || Named(mesh.Material, Tyre);

    public static bool IsBrake(WheelMesh mesh) => !IsTyre(mesh) && (Named(mesh.Name, Brake) || Named(mesh.Material, Brake));

    /// <summary>
    /// The fit of a part's wheel on corner <paramref name="corner"/> (AC's order); null when there is nothing to measure.
    /// </summary>
    /// <param name="partRim">The rim model's meshes, in its own space</param>
    /// <param name="partTyre">The tyre model's meshes in the rim's space; null keeps the car's tyre</param>
    /// <param name="tyreRadius">How big the tyre is to be; 0 or less: as big as the car's</param>
    public static WheelFitting? Fit(int corner, IReadOnlyList<WheelMesh> car, IReadOnlyList<AxleBox> partRim,
        IReadOnlyList<AxleBox>? partTyre, float tyreRadius = 0)
    {
        var withTyre = partTyre != null;
        var outboard = corner % 2 == 0 ? 1 : -1;
        var side = outboard > 0 ? -1 : 1;

        var hidden = new List<int>();
        var rimBoxes = new List<AxleBox>();
        var tyreBoxes = new List<AxleBox>();
        for (var i = 0; i < car.Count; i++)
        {
            var tyre = IsTyre(car[i]);
            var brake = IsBrake(car[i]);
            if (!brake && (withTyre || !tyre)) hidden.Add(i);
            if (tyre) tyreBoxes.Add(car[i].Box);
            else if (!brake) rimBoxes.Add(car[i].Box);
        }

        float scale, x;
        if (withTyre)
        {
            var part = Union(partTyre!);
            var own = Union(tyreBoxes);
            var radius = tyreRadius > 0 ? tyreRadius : own?.Radius ?? 0;
            if (part is not { Radius: > 0 } || radius <= 0) return null;

            scale = radius / part.Value.Radius;
            var centre = own is { } o ? (o.MinX + o.MaxX) / 2 : 0;
            x = centre - side * (part.Value.MinX + part.Value.MaxX) / 2 * scale;
        }
        else
        {
            var (ownRadius, ownFace) = LipOf(rimBoxes, outboard);
            var (partRadius, partFace) = LipOf(partRim, -1);
            if (ownRadius <= 0 || partRadius <= 0 || ownFace == null || partFace == null) return null;

            // A hubcap bulges past the lip: the face goes no further out than the tyre's sidewall
            var face = ownFace.Value;
            if (FaceOf(tyreBoxes, outboard) is { } tyreFace) face = outboard > 0 ? Math.Min(face, tyreFace) : Math.Max(face, tyreFace);
            scale = ownRadius / partRadius;
            x = face - side * partFace.Value * scale;
        }

        return new WheelFitting(hidden, new Matrix4x4(
            side * scale, 0, 0, 0,
            0, scale, 0, 0,
            0, 0, side * scale, 0,
            x, 0, 0, 1));
    }

    private static bool Named(string? name, string[] words) =>
        name != null && words.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static AxleBox? Union(IReadOnlyList<AxleBox> boxes) =>
        boxes.Count == 0 ? null : new AxleBox(boxes.Min(b => b.MinX), boxes.Max(b => b.MaxX), boxes.Max(b => b.Radius));

    private static float? FaceOf(IEnumerable<AxleBox> boxes, int outboard)
    {
        float? face = null;
        foreach (var box in boxes)
        {
            var x = outboard > 0 ? box.MaxX : box.MinX;
            face = face == null ? x : outboard > 0 ? Math.Max(face.Value, x) : Math.Min(face.Value, x);
        }
        return face;
    }

    private static (float Radius, float? Face) LipOf(IReadOnlyList<AxleBox> boxes, int outboard)
    {
        var radius = boxes.Count == 0 ? 0 : boxes.Max(b => b.Radius);
        return (radius, FaceOf(boxes.Where(b => b.Radius >= radius * LipShare), outboard));
    }
}
