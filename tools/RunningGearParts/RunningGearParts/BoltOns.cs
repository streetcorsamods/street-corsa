using AcTools.Numerics;

namespace RunningGearParts;

/// <summary>
/// The speed shop's wheels and mufflers of our own (roadmap step 41), each in its part's space:
/// <list type="bullet">
/// <item>a rim (rims/speed_shop) as the downloaded ones are (Parts/Cars/WheelFit): the axle along X, the face towards -X,
/// the origin the wheel's middle, the lip 7.5 inches off the axle (a 15-inch wheel's; the game scales a shown wheel to the
/// car's), the barrel drawn 7 inches wide;</item>
/// <item>a muffler (exhaust/glasspacks) as exhaust/mufflers are: its inlet at the origin, running back along +Z.</item>
/// </list>
/// </summary>
internal static class BoltOns
{
    private const float In = 0.0254f;
    private const float Lip = 7.5f * In;

    /// <summary>The lug nuts' circle (a 4.75 in bolt circle, the hub's studs)</summary>
    private const float Lugs = 0.0603f;

    private static Vec3 X(float x) => new(x, 0, 0);
    private static Vec3 V(float x, float y, float z) => new(x, y, z);

    /// <summary>A Cragar S/S: the chrome steel rim, the five slotted spokes of its centre, lug nuts and a domed cap</summary>
    public static Model CragarSs()
    {
        var model = new Model("cragar_ss");
        Rim(model[Finish.Chrome]);
        Centre(model, Finish.Aluminium, Finish.Chrome, 0.07f);
        foreach (var (radial, tangent) in Spokes(5))
        {
            // Each spoke two bars with the slot between them, joined at the hub and at the rim
            foreach (var side in new[] { -1f, 1f })
                Spoke(model[Finish.Aluminium], radial, tangent, side * 0.011f, 0.07f, 0.16f, 0.006f, 0.006f);
            Spoke(model[Finish.Aluminium], radial, tangent, 0, 0.064f, 0.085f, 0.017f, 0.017f);
            Spoke(model[Finish.Aluminium], radial, tangent, 0, 0.152f, 0.17f, 0.019f, 0.019f);
        }

        return model;
    }

    /// <summary>An American Racing Torq Thrust: the polished rim, the five spokes widening out to it in grey cast, lug nuts and a small cap</summary>
    public static Model TorqThrust()
    {
        var model = new Model("torq_thrust");
        Rim(model[Finish.Aluminium]);
        Centre(model, Finish.GreyCast, Finish.Aluminium, 0.075f);
        foreach (var (radial, tangent) in Spokes(5))
        {
            // Drawn in four steps along its length, each wider than the last
            const int steps = 4;
            for (var k = 0; k < steps; k++)
            {
                var (r0, r1) = (0.07f + 0.1f * k / steps, 0.07f + 0.1f * (k + 1) / steps);
                var (w0, w1) = (0.012f + 0.02f * k / steps, 0.012f + 0.02f * (k + 1) / steps);
                Spoke(model[Finish.GreyCast], radial, tangent, 0, r0, r1 + 0.003f, (w0 + w1) / 2, 0.008f);
            }
        }

        return model;
    }

    /// <summary>
    /// The rim's shell, turned about the axle: the front lip, the bead seats, the drop centre, the back lip, and its inside
    /// a little in from all of it
    /// </summary>
    private static void Rim(GearMesh mesh) =>
        mesh.Lathe(X(0), Vec3.UnitX, [
            (-0.1f, 0.166f), (-0.1f, Lip), (-0.093f, Lip), (-0.089f, 0.179f), (-0.065f, 0.179f), (-0.05f, 0.16f), (0.04f, 0.16f),
            (0.055f, 0.179f), (0.089f, 0.179f), (0.093f, Lip), (0.1f, Lip), (0.1f, 0.17f), (0.055f, 0.174f), (0.04f, 0.155f),
            (-0.05f, 0.155f), (-0.065f, 0.174f), (-0.095f, 0.174f), (-0.1f, 0.166f)], 64);

    /// <summary>The centre's mounting pad on the hub, the five lug nuts in front of it and the cap over the middle</summary>
    private static void Centre(Model model, Finish pad, Finish trim, float padRadius)
    {
        model[pad].Lathe(X(0), Vec3.UnitX, [(-0.062f, 0.036f), (-0.062f, padRadius), (-0.045f, padRadius), (-0.045f, 0.036f), (-0.062f, 0.036f)], 40);
        model[trim].Lathe(X(0), Vec3.UnitX, [(-0.074f, 0f), (-0.072f, 0.018f), (-0.067f, 0.03f), (-0.06f, 0.036f), (-0.045f, 0.036f), (-0.045f, 0f)], 32);
        for (var k = 0; k < 5; k++)
        {
            var a = MathF.PI / 5 + k * MathF.Tau / 5;
            var at = V(-0.062f, MathF.Cos(a) * Lugs, MathF.Sin(a) * Lugs);
            model[Finish.Chrome].Hex(at, at - X(0.012f), 0.0105f);
        }
    }

    /// <summary>The directions of <paramref name="count"/> spokes round the axle: out along each, and across it</summary>
    private static IEnumerable<(Vec3 Radial, Vec3 Tangent)> Spokes(int count)
    {
        for (var k = 0; k < count; k++)
        {
            var a = k * MathF.Tau / count;
            yield return (V(0, MathF.Cos(a), MathF.Sin(a)), V(0, -MathF.Sin(a), MathF.Cos(a)));
        }
    }

    /// <summary>
    /// A straight bar of a spoke from <paramref name="r0"/> to <paramref name="r1"/> off the axle, <paramref name="aside"/>
    /// across from the spoke's line; it leans out to the face as it goes (the pad is 0.06 m in from the lip's face, the
    /// spoke meets the rim near the front bead seat)
    /// </summary>
    private static void Spoke(GearMesh mesh, Vec3 radial, Vec3 tangent, float aside, float r0, float r1, float halfWidth, float halfThick)
    {
        static float Depth(float r) => -0.055f - (r - 0.07f) / 0.1f * 0.03f;
        var from = X(Depth(r0)) + radial * r0 + tangent * aside;
        var to = X(Depth(r1)) + radial * r1 + tangent * aside;
        var along = to - from;
        mesh.Block((from + to) * 0.5f, Vec3.Cross(along, tangent), along, tangent, V(halfThick, along.Length() / 2, halfWidth));
    }

    /// <summary>A glasspack: the inlet pipe, a cone out to the long straight-through can with its rolled bands, the cone and pipe out</summary>
    public static Model Glasspack()
    {
        var model = new Model("glasspack");
        var steel = model[Finish.Steel];
        steel.Lathe(Vec3.Zero, Vec3.UnitZ, [(0f, 0f), (0f, 0.0286f), (0.05f, 0.0286f), (0.075f, 0.051f), (0.545f, 0.051f), (0.57f, 0.0286f), (0.62f, 0.0286f), (0.62f, 0f)], 32);
        foreach (var z in new[] { 0.09f, 0.31f, 0.53f }) steel.Tube(V(0, 0, z - 0.006f), V(0, 0, z + 0.006f), 0.0535f, 32);
        return model;
    }

    /// <summary>A Cherry Bomb: the short can in its red paint, its pressed steel ends and pipes</summary>
    public static Model CherryBomb()
    {
        var model = new Model("cherry_bomb");
        model[Finish.Steel].Lathe(Vec3.Zero, Vec3.UnitZ, [(0f, 0f), (0f, 0.0286f), (0.05f, 0.0286f), (0.07f, 0.046f), (0.08f, 0.046f), (0.08f, 0f)], 32);
        model[Finish.Steel].Lathe(Vec3.Zero, Vec3.UnitZ, [(0.43f, 0f), (0.43f, 0.046f), (0.44f, 0.046f), (0.46f, 0.0286f), (0.51f, 0.0286f), (0.51f, 0f)], 32);
        model[Finish.Red].Tube(V(0, 0, 0.08f), V(0, 0, 0.43f), 0.047f, 32);
        foreach (var z in new[] { 0.085f, 0.425f }) model[Finish.Steel].Tube(V(0, 0, z - 0.005f), V(0, 0, z + 0.005f), 0.049f, 32);
        return model;
    }
}
