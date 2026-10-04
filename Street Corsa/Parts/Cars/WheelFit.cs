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
/// <param name="WithTyre">The part's tyre is shown (and the car's hidden); false: the rim goes in round the car's tyre</param>
public sealed record WheelFitting(IReadOnlyList<int> Hidden, Matrix4x4 Placement, bool WithTyre = false);

/// <summary>
/// How a part's wheel takes the place of a car's own (the race mode's <c>car_parts.lua</c> does the same in AC; the two
/// are kept to the same rules and tested against the same cars).
///
/// The car's meshes under its wheel node: the tyre and the brake by mesh or material name, the rim everything else; on a
/// car that names no tyre (a mod's "rubber", "pneu", "Reifen"), the tyre is the mesh well outside all the rest. A
/// rim alone keeps the car's tyre and fills the hole the hidden rim leaves: its outer lip as big as the car's, flush with
/// where the car's lip was (no further out than the tyre's sidewall), the barrel going in. A tyre makes the whole wheel
/// the parts', scaled together so the tyre is as big as wanted, centred on the car's tyre. An optional tyre (the factory
/// one, with another rim) is shown only when the rim alone would come out wider than the car's tyre by more than
/// <see cref="RimWiderThanTyre"/>: a 15x11 Cragar round a Chevelle's narrow tyre. Part models are SLRR's: the axle along
/// X, the face towards -X. A left wheel's outboard side is +X, a right one's -X.
/// </summary>
public static class WheelFit
{
    private static readonly string[] Tyre = ["tyre", "tire", "tread", "wall"];
    private static readonly string[] Brake = ["brake", "drum", "disc", "disk", "caliper", "rotor"];

    /// <summary>A part mesh at least this share of the biggest one's radius is the lip</summary>
    private const float LipShare = 0.9f;

    /// <summary>
    /// A car that names no tyre: the meshes at least this share of the biggest radius are its tyre, when every other is
    /// within <see cref="RimInTyre"/> of it (a 15" rim's lip is 0.6 to 0.75 of its tyre)
    /// </summary>
    private const float TyreShare = 0.97f;

    private const float RimInTyre = 0.85f;

    /// <summary>
    /// How much wider than the car's tyre a rim alone may come out before the corner shows the tyre on it as well: a
    /// 15x8 Cragar on a Chevelle's front is 1.09 of its tyre and looks right, a 15x11 on its rear 1.21 and does not
    /// </summary>
    public const float RimWiderThanTyre = 1.15f;

    /// <summary>AC's wheels in its order (0 left front, 1 right front, 2 left rear, 3 right rear)</summary>
    public static readonly string[] WheelNodes = ["WHEEL_LF", "WHEEL_RF", "WHEEL_LR", "WHEEL_RR"];

    /// <summary>Which of the car's meshes under a wheel are its tyre: by name, else by being well outside the rest</summary>
    public static bool[] Tyres(IReadOnlyList<WheelMesh> car)
    {
        var tyres = car.Select(m => Named(m.Name, Tyre) || Named(m.Material, Tyre)).ToArray();
        if (tyres.Any(t => t)) return tyres;

        var others = Enumerable.Range(0, car.Count).Where(i => !Named(car[i].Name, Brake) && !Named(car[i].Material, Brake)).ToList();
        if (others.Count < 2) return tyres;
        var biggest = others.Max(i => car[i].Box.Radius);
        var outer = others.Where(i => car[i].Box.Radius >= biggest * TyreShare).ToList();
        if (outer.Count == others.Count || others.Except(outer).Any(i => car[i].Box.Radius > biggest * RimInTyre)) return tyres;
        foreach (var i in outer) tyres[i] = true;
        return tyres;
    }

    /// <summary>The radius of the car's tyre under a wheel (<see cref="Tyres"/>); 0 when none is found</summary>
    public static float TyreRadius(IReadOnlyList<WheelMesh> car)
    {
        var tyres = Tyres(car);
        return car.Where((_, i) => tyres[i]).Select(m => m.Box.Radius).DefaultIfEmpty(0).Max();
    }

    /// <summary>Whether a name says tyre (a blurred object's, say)</summary>
    public static bool IsTyreName(string? name) => Named(name, Tyre);

    private static bool IsBrake(WheelMesh mesh, bool tyre) => !tyre && (Named(mesh.Name, Brake) || Named(mesh.Material, Brake));

    /// <summary>
    /// The fit of a part's wheel on corner <paramref name="corner"/> (AC's order); null when there is nothing to measure.
    /// </summary>
    /// <param name="partRim">The rim model's meshes, in its own space</param>
    /// <param name="partTyre">The tyre model's meshes in the rim's space; null keeps the car's tyre</param>
    /// <param name="tyreRadius">How big the tyre is to be; 0 or less: as big as the car's</param>
    /// <param name="tyreOptional">Show the tyre only when the rim alone would be too wide for the car's</param>
    public static WheelFitting? Fit(int corner, IReadOnlyList<WheelMesh> car, IReadOnlyList<AxleBox> partRim,
        IReadOnlyList<AxleBox>? partTyre, float tyreRadius = 0, bool tyreOptional = false)
    {
        if (partTyre != null && tyreOptional)
        {
            var alone = Fit(corner, car, partRim, null);
            var tyres = Tyres(car);
            var own = Union(car.Where((_, i) => tyres[i]).Select(m => m.Box).ToList());
            var rim = Union(partRim);
            if (alone != null && own is { } o && rim is { } r
                && (r.MaxX - r.MinX) * Math.Abs(alone.Placement.M22) <= (o.MaxX - o.MinX) * RimWiderThanTyre) return alone;
        }

        var withTyre = partTyre != null;
        var outboard = corner % 2 == 0 ? 1 : -1;
        var side = outboard > 0 ? -1 : 1;

        var hidden = new List<int>();
        var rimBoxes = new List<AxleBox>();
        var tyreBoxes = new List<AxleBox>();
        var isTyre = Tyres(car);
        for (var i = 0; i < car.Count; i++)
        {
            var tyre = isTyre[i];
            var brake = IsBrake(car[i], tyre);
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
            x, 0, 0, 1), withTyre);
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
