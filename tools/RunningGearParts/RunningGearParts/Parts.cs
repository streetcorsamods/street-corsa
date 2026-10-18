using AcTools.Numerics;

namespace RunningGearParts;

/// <summary>A rear axle family: its ring gear (inches), its tubes, whether its carrier drops out of the front (a banjo), its cover</summary>
internal sealed record AxleFamily(string Id, string Pack, float RingInches, float TubeRadius, bool Banjo, int CoverBolts, Func<float, float> Cover);

/// <summary>
/// The running gear's own models, in each part's space (metres, Y up, the front towards -Z):
/// <list type="bullet">
/// <item>on the car (the axle housing and what is on it, a sway bar, a leaf spring's traction bar): +X the car's left, the
/// origin where the part mounts (the axle's middle, the axle seat on top of the leaf);</item>
/// <item>at a wheel (a drum's wheel cylinder, a shock): +X towards the outside of the car, the hub's axis along X.</item>
/// </list>
/// </summary>
internal static class Parts
{
    private const float In = 0.0254f;

    // Where the axle housing ends each side (the brake's backing plate goes on it), and where the leaves sit under it
    private const float HousingEnd = 0.655f;
    private const float PerchX = 0.47f;

    /// <summary>
    /// How much bigger the middle of an axle (the housing's centre section, the ring and pinion, the carrier's case) is drawn
    /// than its ring gear alone would make it: the cast iron round the gears is thick (the owner found them small in game at
    /// 1, big at 1.25)
    /// </summary>
    public const float CentreScale = 1.1f;

    private static Vec3 X(float x) => new(x, 0, 0);
    private static Vec3 V(float x, float y, float z) => new(x, y, z);

    public static readonly AxleFamily[] Families =
    [
        new("gm_10bolt", "axles_gm", 8.2f, 0.035f, false, 10, a => 1f + 0.04f * MathF.Cos(2 * a)),
        new("bop_10bolt", "axles_gm", 8.5f, 0.036f, false, 10, a => 1f + 0.03f * MathF.Cos(3 * a)),
        new("gm_12bolt", "axles_gm", 8.875f, 0.038f, false, 12, _ => 1f),
        new("ford_8", "axles_ford", 8.0f, 0.036f, true, 10, _ => 1f),
        new("ford_9", "axles_ford", 9.0f, 0.040f, true, 10, _ => 1f),
        new("chrysler_825", "axles_mopar", 8.25f, 0.036f, false, 10, a => 1f + 0.05f * MathF.Cos(4 * a)),
        new("chrysler_875", "axles_mopar", 8.75f, 0.038f, true, 10, _ => 1f),
        new("dana_44", "axles_dana", 8.5f, 0.039f, false, 10, a => 1f + 0.12f * MathF.Cos(a - 0.8f)),
        new("dana_60", "axles_dana", 9.75f, 0.044f, false, 10, a => 1f + 0.14f * MathF.Cos(a - 0.8f) + 0.04f * MathF.Cos(2 * a))
    ];

    /// <summary>The middle of the housing round its ring gear, metres</summary>
    private static float Belly(AxleFamily family) => family.RingInches * In / 2 + 0.025f;

    /// <summary>A direction square to the Z axis at an angle: X at 0, Y (up) at a quarter turn</summary>
    private static Vec3 AroundZ(float angle) => V(MathF.Cos(angle), MathF.Sin(angle), 0);

    /// <summary>A direction square to the X axis at an angle: up at 0, the back at a quarter turn</summary>
    private static Vec3 AroundX(float angle) => V(0, MathF.Cos(angle), MathF.Sin(angle));

    /// <summary>
    /// A rear axle housing: the tubes out to the backing plates with the leaves' perches under them, and the middle: a
    /// Salisbury's (the 10- and 12-bolts, the 8-1/4, the Danas) with its pinion snout and its bolted cover at the back, or a
    /// banjo's (the Ford 8 and 9-inch, the 8-3/4) with the drop-out third member bolted into its front
    /// </summary>
    public static Model Housing(AxleFamily family)
    {
        var model = new Model(family.Id);
        var paint = model[Finish.Paint];
        var r = Belly(family);
        var tube = family.TubeRadius;

        // The middle (its bosses, the centre section, the cover, the nose and yoke) made at the ring gear's size, put in at
        // CentreScale; the tubes start inside it
        var centre = new Model(family.Id);
        var iron = centre[Finish.CastIron];

        foreach (var side in new[] { 1f, -1f })
        {
            paint.Tube(X(side * r * 0.5f), X(side * (HousingEnd - 0.05f)), tube, 20);
            iron.Tube(X(side * r * 0.4f), X(side * r * 1.05f), tube * 1.4f, 20);
            // The end: the backing plate's flange and the bearing's bore
            model[Finish.CastIron].Tube(X(side * (HousingEnd - 0.055f)), X(side * (HousingEnd - 0.043f)), 0.068f, 20);
            model[Finish.CastIron].Tube(X(side * (HousingEnd - 0.05f)), X(side * HousingEnd), tube * 1.22f, 20);
            // The spring perch the leaf is clamped to, under the tube
            paint.Block(V(side * PerchX - 0.035f, -tube - 0.012f, -0.05f), V(side * PerchX + 0.035f, -tube + 0.006f, 0.05f));
            // The shock's stud on the perch: behind the axle on the left, ahead of it on the right (staggered, as the shocks
            // of the day were, and as the wheel's side turns a shock round)
            paint.Tube(V(side * (PerchX + 0.04f), -tube - 0.005f, side * 0.06f), V(side * (PerchX + 0.04f), -tube - 0.005f, side * 0.11f), 0.008f, 10);
        }

        if (family.Banjo)
        {
            // The banjo: round in front where the third member goes in, a smooth dome welded at the back
            iron.Lathe(Vec3.Zero, Vec3.UnitZ, [(-0.075f, 0f), (-0.075f, r * 1.08f), (-0.062f, r * 1.08f), (-0.05f, r), (0.03f, r), (0.085f, r * 0.7f), (0.11f, r * 0.25f), (0.112f, 0f)], 28);
            // The third member: its flange on the banjo's, its nose out to the pinion's yoke
            var nose = V(0, -0.02f, 0);
            iron.Lathe(nose, Vec3.UnitZ, [(-0.32f, 0f), (-0.32f, 0.03f), (-0.28f, 0.03f), (-0.28f, 0.05f), (-0.21f, 0.056f), (-0.15f, 0.07f), (-0.11f, r * 0.75f), (-0.09f, r), (-0.075f, r), (-0.075f, 0f)], 24);
            for (var k = 0; k < family.CoverBolts; k++)
            {
                var at = nose + AroundZ(MathF.Tau * k / family.CoverBolts + 0.3f) * (r * 0.92f);
                centre[Finish.Steel].Hex(at + V(0, 0, -0.09f), at + V(0, 0, -0.099f), 0.008f);
            }

            Yoke(centre, nose + V(0, 0, -0.32f));
        }
        else
        {
            // The Salisbury: the carrier is the housing's own casting, closed by a cover at the back
            iron.Lathe(Vec3.Zero, Vec3.UnitZ, [(-0.09f, 0f), (-0.09f, 0.07f), (-0.05f, r * 0.85f), (-0.01f, r), (0.045f, r), (0.06f, r * 0.96f), (0.06f, 0f)], 28, family.Cover);
            var nose = V(0, -0.03f, 0);
            iron.Lathe(nose, Vec3.UnitZ, [(-0.25f, 0f), (-0.25f, 0.03f), (-0.21f, 0.03f), (-0.21f, 0.045f), (-0.15f, 0.05f), (-0.085f, 0.062f), (-0.085f, 0f)], 20);
            var cover = r * 1.06f;
            centre[Finish.Paint].Lathe(Vec3.Zero, Vec3.UnitZ, [(0.06f, 0f), (0.06f, cover), (0.072f, cover), (0.088f, cover * 0.9f), (0.105f, cover * 0.68f), (0.114f, cover * 0.38f), (0.117f, 0f)], 32, family.Cover);
            for (var k = 0; k < family.CoverBolts; k++)
            {
                var angle = MathF.Tau * k / family.CoverBolts + 0.2f;
                var at = AroundZ(angle) * (cover * 0.93f * family.Cover(angle));
                centre[Finish.Steel].Hex(at + V(0, 0, 0.072f), at + V(0, 0, 0.081f), 0.0075f);
            }

            // The fill plug
            centre[Finish.Steel].Hex(V(r * 0.55f, r * 0.2f, 0.088f), V(r * 0.55f, r * 0.2f, 0.096f), 0.01f);
            Yoke(centre, nose + V(0, 0, -0.25f));
        }

        return model.Add(centre, CentreScale);
    }

    /// <summary>The pinion's yoke, the driveshaft's U-joint's other half, in front of the snout</summary>
    private static void Yoke(Model model, Vec3 front)
    {
        var steel = model[Finish.Steel];
        steel.Tube(front, front + V(0, 0, -0.035f), 0.026f, 16);
        foreach (var side in new[] { 1f, -1f })
            steel.Block(front + V(side * 0.03f, 0, -0.055f), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, V(0.006f, 0.022f, 0.022f));
    }

    /// <summary>
    /// A ring and pinion: the ring gear on the carrier's right flange (the ring's teeth facing the car's left), the pinion
    /// coming in from the front a little below the axle's line, as a hypoid's does
    /// </summary>
    public static Model RingPinion(AxleFamily family)
    {
        var model = new Model("ring_" + family.Id);
        var steel = model[Finish.Steel];
        var outer = family.RingInches * In / 2;
        var inner = outer * 0.6f;

        steel.Lathe(Vec3.Zero, Vec3.UnitX, [(-0.058f, inner), (-0.058f, outer * 0.93f), (-0.034f, outer), (-0.03f, outer), (-0.03f, inner * 1.1f), (-0.045f, inner), (-0.058f, inner)], 48);
        var teeth = (int)MathF.Round(family.RingInches * 4.8f);
        var middle = (inner * 1.15f + outer * 0.97f) / 2;
        for (var k = 0; k < teeth; k++)
        {
            var angle = MathF.Tau * k / teeth;
            var radial = AroundX(angle);
            steel.Block(V(-0.026f, 0, 0) + radial * middle, Vec3.UnitX, radial, Vec3.Cross(Vec3.UnitX, radial),
                V(0.004f, (outer * 0.97f - inner * 1.15f) / 2, MathF.PI * middle / teeth * 0.45f));
        }

        // The ring's bolts, on its back
        for (var k = 0; k < 10; k++)
        {
            var at = AroundX(MathF.Tau * k / 10) * (inner * 1.12f);
            model[Finish.CastIron].Hex(at + X(-0.058f), at + X(-0.066f), 0.007f);
        }

        // The pinion: its head at the ring's teeth, its shaft out through the snout to the yoke
        var head = outer * 0.78f;
        var centre = V(-0.01f, -outer * 0.2f, 0);
        steel.Lathe(centre, Vec3.UnitZ,
        [
            (-0.24f, 0f), (-0.24f, 0.016f), (-head - 0.05f, 0.016f), (-head - 0.05f, 0.024f), (-head - 0.035f, 0.024f), (-head - 0.035f, 0.017f),
            (-head - 0.022f, 0.017f), (-head - 0.022f, 0.034f), (-head + 0.012f, 0.022f), (-head + 0.012f, 0f)
        ], 20);
        for (var k = 0; k < 11; k++)
        {
            var angle = MathF.Tau * k / 11;
            var radial = AroundZ(angle);
            steel.Block(centre + V(0, 0, -head - 0.005f) + radial * 0.029f, Vec3.UnitZ, radial, Vec3.Cross(Vec3.UnitZ, radial), V(0.016f, 0.004f, 0.004f));
        }

        // At the size of the housing's middle it turns in
        return new Model(model.Name).Add(model, CentreScale);
    }

    /// <summary>
    /// An axle shaft, the left one (the right's slot turns it round): from its splined end at the carrier out to its
    /// flange at the wheel, the bearing just inside it, the wheel studs through it
    /// </summary>
    public static Model AxleShaft()
    {
        var model = new Model("axle_shaft");
        var steel = model[Finish.Steel];
        steel.Tube(X(-0.30f), X(0.27f), 0.016f, 14);
        steel.Tube(X(-0.312f), X(-0.28f), 0.0185f, 14);
        model[Finish.CastIron].Tube(X(0.245f), X(0.268f), 0.03f, 18);
        steel.Tube(X(0.27f), X(0.279f), 0.068f, 28);
        steel.Tube(X(0.279f), X(0.29f), 0.036f, 20);
        for (var k = 0; k < 5; k++)
        {
            var at = AroundX(MathF.Tau * k / 5) * 0.0603f;
            model[Finish.Chrome].Tube(at + X(0.262f), at + X(0.315f), 0.0063f, 8);
        }

        return model;
    }

    /// <summary>
    /// A drum's wheel cylinder, at the top of the backing plate between the shoes' tips (the drum's origin is its outer
    /// face, the backing plate a hand's width inboard)
    /// </summary>
    public static Model WheelCylinder()
    {
        var model = new Model("wheel_cylinder");
        const float x = -0.062f, y = 0.103f;
        model[Finish.CastIron].Tube(V(x, y, -0.034f), V(x, y, 0.034f), 0.0125f, 16);
        model[Finish.CastIron].Block(V(x - 0.012f, y, 0), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, V(0.012f, 0.011f, 0.014f));
        foreach (var side in new[] { 1f, -1f })
        {
            model[Finish.Rubber].Tube(V(x, y, side * 0.034f), V(x, y, side * 0.044f), 0.011f, 12);
            model[Finish.Steel].Tube(V(x, y, side * 0.044f), V(x, y, side * 0.054f), 0.004f, 8);
        }

        model[Finish.Brass].Tube(V(x, y + 0.011f, -0.012f), V(x, y + 0.025f, -0.014f), 0.003f, 8);
        return model;
    }

    /// <summary>
    /// A tube shock up from its lower eye, the body in the maker's colour; a Koni has its adjusting collar, a drag shock its
    /// band. The origin is the spring seat: the eye is on the axle perch's stud, a little outboard of the seat and behind
    /// the axle (the right side's turns it round to ahead of it)
    /// </summary>
    public static Model Shock(string id, Finish body, bool collar = false)
    {
        var model = new Model(id);
        var eye = V(0.04f, 0.017f, 0.085f);
        Vec3 Up(float y) => eye + V(0, y, 0);
        model[Finish.CastIron].Tube(eye + V(-0.016f, 0, 0), eye + V(0.016f, 0, 0), 0.012f, 14);
        model[Finish.Rubber].Tube(eye + V(-0.017f, 0, 0), eye + V(0.017f, 0, 0), 0.0065f, 10);
        model[body].Tube(Up(0.012f), Up(0.24f), 0.024f, 20);
        model[body].Tube(Up(0.24f), Up(0.252f), 0.0265f, 20);
        if (collar) model[Finish.Steel].Tube(Up(0.03f), Up(0.045f), 0.0265f, 20);
        model[Finish.Chrome].Tube(Up(0.252f), Up(0.365f), 0.008f, 12);
        model[Finish.Rubber].Tube(Up(0.365f), Up(0.38f), 0.02f, 14);
        model[Finish.Steel].Tube(Up(0.38f), Up(0.386f), 0.022f, 14);
        model[Finish.Steel].Hex(Up(0.386f), Up(0.396f), 0.009f);
        return model;
    }

    /// <summary>A torsion bar along the car, its hex ends in the lower arm in front and the anchor at the back</summary>
    public static Model TorsionBar(string id, float inches)
    {
        var model = new Model(id);
        var radius = inches * In / 2;
        model[Finish.Paint].Tube(V(0, 0, -0.42f), V(0, 0, 0.42f), radius, 14);
        model[Finish.Paint].Hex(V(0, 0, -0.47f), V(0, 0, -0.42f), radius * 1.5f);
        model[Finish.Paint].Hex(V(0, 0, 0.42f), V(0, 0, 0.47f), radius * 1.5f);
        model[Finish.CastIron].Block(V(-0.035f, -0.03f, 0.44f), V(0.035f, 0.035f, 0.5f));
        return model;
    }

    /// <summary>
    /// A sway bar across the axle (its origin a little ahead of it): bent back at each end to its link, held in two
    /// rubber bushings
    /// </summary>
    public static Model SwayBar(string id, float inches)
    {
        var model = new Model(id);
        var radius = inches * In / 2;
        model[Finish.Paint].Sweep(GearMesh.Rounded([V(-0.52f, 0, 0.17f), V(-0.47f, 0, 0), V(0.47f, 0, 0), V(0.52f, 0, 0.17f)], 0.06f), radius, 12);
        foreach (var side in new[] { 1f, -1f })
        {
            model[Finish.Rubber].Block(V(side * 0.3f, 0, 0), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, V(0.022f, radius + 0.01f, radius + 0.012f));
            model[Finish.Steel].Block(V(side * 0.3f, radius + 0.013f, 0), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, V(0.028f, 0.003f, radius + 0.02f));
            model[Finish.Steel].Tube(V(side * 0.52f, -0.01f, 0.17f), V(side * 0.52f, 0.1f, 0.17f), 0.006f, 8);
        }

        return model;
    }

    /// <summary>
    /// A leaf spring's traction bars, from the axle's U-bolt plate under the leaf (the origin is the axle seat on top of the
    /// leaf, the axle above it) forward: a Traction Master's bar to a bracket on the frame, a Lakewood's up to the leaf's
    /// front eye, a slapper's short bar with its snubber under the leaf, a ladder bar's two tubes to one pivot far forward
    /// </summary>
    public static Model TractionBar(string id)
    {
        var model = new Model(id);
        var plate = model[Finish.Steel];
        plate.Block(V(-0.04f, -0.075f, -0.06f), V(0.04f, -0.06f, 0.06f));
        switch (id)
        {
            case "traction_master":
                model[Finish.Chrome].Sweep([V(0, -0.07f, -0.04f), V(0, -0.06f, -0.6f)], 0.014f);
                model[Finish.Paint].Block(V(-0.03f, -0.08f, -0.66f), V(0.03f, 0.12f, -0.6f));
                break;
            case "lakewood":
                model[Finish.Gold].Sweep(GearMesh.Rounded([V(0, -0.075f, 0.04f), V(0, -0.06f, -0.45f), V(0, 0.06f, -0.66f)], 0.08f), 0.014f);
                model[Finish.Rubber].Block(V(-0.025f, -0.045f, -0.42f), V(0.025f, -0.01f, -0.36f));
                break;
            case "slapper":
                model[Finish.Paint].Block(V(-0.025f, -0.08f, -0.4f), V(0.025f, -0.065f, 0.02f));
                model[Finish.Rubber].Block(V(-0.025f, -0.065f, -0.4f), V(0.025f, -0.03f, -0.34f));
                break;
            default:
                // Ladder bars: one tube from over the axle, one from under it, to a pivot near the middle of the car
                var pivot = V(0, 0.03f, -1.05f);
                model[Finish.Chrome].Sweep([V(0, 0.12f, 0.02f), pivot], 0.013f);
                model[Finish.Chrome].Sweep([V(0, -0.07f, 0.02f), pivot], 0.013f);
                for (var k = 1; k <= 3; k++)
                {
                    var t = k * 0.22f;
                    model[Finish.Chrome].Sweep([V(0, 0.12f + (0.03f - 0.12f) * t, 0.02f - 1.07f * t), V(0, -0.07f + (0.03f + 0.07f) * t, 0.02f - 1.07f * t)], 0.009f);
                }

                model[Finish.Paint].Block(V(-0.03f, 0.0f, -1.08f), V(0.03f, 0.06f, -1.03f));
                model[Finish.Steel].Block(V(-0.035f, -0.07f, -0.02f), V(0.035f, 0.13f, 0.03f));
                break;
        }

        return model;
    }

    /// <summary>
    /// A drum's backing plate (step 31 phase 5), in its space at the wheel (+X outboard, its origin the corner's brake slot):
    /// a pressed steel dish just inboard of the drum's open side, as big as the drum and a little over, its middle dished
    /// in towards the axle with the bolts that hold it, and the boss the wheel cylinder sits against at the top. The drum
    /// hangs 0.08 m outboard of the slot (its own mount), so the drum's open side is at 0.08 m plus its own inner face.
    /// </summary>
    /// <param name="drumInnerX">Where the drum's open side is in its own space (its model's least X)</param>
    /// <param name="drumRadius">The drum's outside radius</param>
    public static Model BackingPlate(string id, float drumInnerX, float drumRadius)
    {
        var model = new Model(id);
        var face = 0.08f + drumInnerX - 0.004f;
        const float thick = 0.003f;
        var rim = drumRadius + 0.006f;
        var paint = model[Finish.Paint];

        // The flat face out to the drum, a lip round its edge towards the drum, the middle dished in with a hole for the axle
        paint.Lathe(X(0), Vec3.UnitX, [(face, 0.085f), (face, rim), (face + 0.009f, rim), (face + 0.009f, rim - thick), (face + thick, rim - thick),
            (face + thick, 0.085f + thick), (face, 0.085f)], 40);
        paint.Lathe(X(0), Vec3.UnitX, [(face - 0.014f, 0.034f), (face - 0.014f, 0.062f), (face, 0.085f), (face + thick, 0.085f - thick),
            (face - 0.014f + thick, 0.062f - thick), (face - 0.014f + thick, 0.034f), (face - 0.014f, 0.034f)], 32);

        // The bolts round the middle, their heads on the inside
        for (var k = 0; k < 4; k++)
        {
            var a = MathF.PI / 4 + k * MathF.PI / 2;
            var at = V(face - 0.014f, MathF.Cos(a) * 0.048f, MathF.Sin(a) * 0.048f);
            model[Finish.Steel].Hex(at, at - X(0.008f), 0.008f);
        }

        // The anchor pin's boss under the wheel cylinder, and the adjuster's slot cover at the bottom
        model[Finish.CastIron].Block(V(face + 0.006f, 0.085f, 0), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, V(0.006f, 0.012f, 0.012f));
        model[Finish.Rubber].Block(V(face - 0.001f, -drumRadius * 0.7f, 0), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, V(0.002f, 0.008f, 0.014f));
        return model;
    }

    /// <summary>
    /// A front hub (step 31 phase 5) in the drum's space at the wheel (+X outboard): the boss through the drum's middle with
    /// its bearings, five wheel studs out through the drum's face and the dust cap; the same for every drum and disc
    /// </summary>
    public static Model Hub()
    {
        var model = new Model("hub");
        model[Finish.CastIron].Lathe(X(0), Vec3.UnitX, [(-0.02f, 0f), (-0.02f, 0.042f), (0.072f, 0.042f), (0.072f, 0f)], 28);
        model[Finish.Chrome].Lathe(X(0), Vec3.UnitX, [(0.072f, 0f), (0.072f, 0.026f), (0.09f, 0.024f), (0.096f, 0.016f), (0.098f, 0f)], 24);
        for (var k = 0; k < 5; k++)
        {
            var a = k * MathF.Tau / 5;
            var at = V(0, MathF.Cos(a) * 0.0603f, MathF.Sin(a) * 0.0603f);
            model[Finish.Steel].Tube(at + X(0.04f), at + X(0.092f), 0.0063f, 10);
        }

        return model;
    }
}
