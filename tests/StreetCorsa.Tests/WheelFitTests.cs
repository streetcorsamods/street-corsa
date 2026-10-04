using System.Numerics;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Tests;

/// <summary>
/// A part's wheel in place of a car's own, on the wheels the KN5s have (measured 2026-10-03), the same as
/// tools/sc_race_harness/test_parts.py runs the race mode on
/// </summary>
public class WheelFitTests
{
    // rims/mopar/rim_8_5 and tyres/mopar/Tyre_15_8 as the converter wrote them
    private static readonly AxleBox[] Rim =
        [new(-0.029f, -0.029f, 0.029f), new(-0.031f, 0.01f, 0.062f), new(-0.022f, 0.058f, 0.147f), new(-0.047f, 0.103f, 0.187f)];
    private static readonly AxleBox[] Tyre = [new(-0.101f + 0.027f, 0.097f + 0.027f, 0.313f)];

    /// <summary>The Valiant's left front (a right wheel is its mirror)</summary>
    private static List<WheelMesh> Valiant(int side) =>
    [
        Mesh("whitewall", "tyre_whitewall", 0.036f, 0.044f, 0.227f, side),
        Mesh("inner_rim", "inner_rim", -0.121f, 0.024f, 0.187f, side),
        Mesh("rim_centre", "rim_centre", -0.001f, 0.029f, 0.151f, side),
        Mesh("rim_outer", "rim_outer", -0.005f, 0.037f, 0.187f, side),
        Mesh("tire_tread", "tyre_tread", -0.108f, 0.026f, 0.302f, side),
        Mesh("tire_wall", "tyre_wall", -0.125f, 0.042f, 0.284f, side),
        Mesh("tyre_cap", "black", 0.028f, 0.045f, 0.163f, side),
        Mesh("wheel_hub", "wheel_hub", 0.012f, 0.029f, 0.029f, side),
        Mesh("Wheel_Nuts", "wheel_nuts", 0.021f, 0.035f, 0.062f, side),
        Mesh("brake_drum", "brake_drum", -0.119f, -0.049f, 0.139f, side),
        Mesh("hubcap_S", "hubcap_S_series", 0.021f, 0.08f, 0.188f, side)
    ];

    /// <summary>The Chevelle '70's left front: meshes named after the node, materials saying what they are</summary>
    private static readonly List<WheelMesh> Chevelle =
    [
        Mesh("WHEEL_LF_SUB5", "brakedisc", -0.021f, 0.021f, 0.142f, 1),
        Mesh("WHEEL_LF_SUB4", "EXT_tire", -0.067f, 0.164f, 0.316f, 1),
        Mesh("WHEEL_LF_SUB3", "chevelle_rim_chrome", 0.041f, 0.058f, 0.084f, 1),
        Mesh("WHEEL_LF_SUB2", "chevelle_rim_black", 0.041f, 0.153f, 0.167f, 1),
        Mesh("WHEEL_LF_SUB1", "chevelle_rim_inner", -0.072f, 0.162f, 0.231f, 1),
        Mesh("WHEEL_LF_SUB0", "chevelle_rim", 0.067f, 0.164f, 0.217f, 1)
    ];

    private static WheelMesh Mesh(string name, string material, float x0, float x1, float r, int side) =>
        new(name, material, new AxleBox(Math.Min(side * x0, side * x1), Math.Max(side * x0, side * x1), r));

    private static string[] Names(List<WheelMesh> car, WheelFitting fit) => fit.Hidden.Select(i => car[i].Name).ToArray();

    [Fact]
    public void A_rim_hides_the_cars_rim_and_keeps_its_tyre_and_brake()
    {
        var car = Valiant(1);
        var fit = WheelFit.Fit(0, car, Rim, null)!;

        Assert.Equal(["inner_rim", "rim_centre", "rim_outer", "wheel_hub", "Wheel_Nuts", "hubcap_S"], Names(car, fit));
    }

    [Fact]
    public void A_rim_faces_out_sized_to_the_lip_flush_with_the_sidewall()
    {
        var scale = 0.188f / 0.187f;
        var left = WheelFit.Fit(0, Valiant(1), Rim, null)!.Placement;
        var right = WheelFit.Fit(1, Valiant(-1), Rim, null)!.Placement;

        Assert.Equal(-scale, left.M11, 3);
        Assert.Equal(scale, left.M22, 3);
        Assert.Equal(0.045f, left.M41 + 0.047f * scale, 3);
        Assert.Equal(scale, right.M11, 3);
        Assert.Equal(-0.045f, right.M41 - 0.047f * scale, 3);
    }

    [Fact]
    public void The_chevelle_goes_by_its_materials_as_it_did_in_the_game()
    {
        var fit = WheelFit.Fit(0, Chevelle, Rim, null)!;

        // In the game (sc_race_parts.log): 4 own meshes hidden, scale 1.235 (lip 0.231 over 0.187), face at 0.164
        Assert.Equal(["WHEEL_LF_SUB3", "WHEEL_LF_SUB2", "WHEEL_LF_SUB1", "WHEEL_LF_SUB0"], Names(Chevelle, fit));
        Assert.Equal(-0.231f / 0.187f, fit.Placement.M11, 3);
        Assert.Equal(0.164f, fit.Placement.M41 + 0.047f * 0.231f / 0.187f, 3);
    }

    [Fact]
    public void A_tyre_makes_the_whole_wheel_the_parts_as_big_as_wanted_centred_on_the_cars_tyre()
    {
        var car = Valiant(-1);
        var fit = WheelFit.Fit(1, car, Rim, Tyre, 0.30f)!;
        var scale = 0.30f / 0.313f;

        Assert.Contains("tire_tread", Names(car, fit));
        Assert.DoesNotContain("brake_drum", Names(car, fit));
        Assert.Equal(scale, fit.Placement.M11, 3);
        // The car's tyre spans -0.045..0.125 (centre 0.04), the part's 0.025 from the rim's origin
        Assert.Equal(0.04f, fit.Placement.M41 + 0.025f * scale, 3);
    }

    [Fact]
    public void A_box_is_carried_into_the_wheels_space()
    {
        var box = AxleBox.Of(new Vector3(-0.1f, -0.2f, -0.3f), new Vector3(0.1f, 0.2f, 0.3f), Matrix4x4.CreateTranslation(0.5f, 0, 0));

        Assert.Equal(new AxleBox(0.4f, 0.6f, 0.3f), box);
    }

    // rims/mopar/cragar_8 and cragar_11, their outer meshes
    private static readonly AxleBox[] Cragar8 = [new(-0.039f, 0.131f, 0.165f), new(-0.073f, 0.131f, 0.187f)];
    private static readonly AxleBox[] Cragar11 = [new(-0.095f, 0.13f, 0.165f), new(-0.129f, 0.13f, 0.187f)];

    [Fact]
    public void A_rim_too_wide_for_the_cars_tyre_brings_its_factory_tyre_along()
    {
        // A 15x11 Cragar alone would be 1.21 of the Chevelle's tyre: the slick on it comes along, the car's tyre goes
        var wide = WheelFit.Fit(0, Chevelle, Cragar11, Tyre, 0.32f, tyreOptional: true)!;
        Assert.Contains("WHEEL_LF_SUB4", Names(Chevelle, wide));
        Assert.True(wide.WithTyre);
        Assert.Equal(0.32f / 0.313f, Math.Abs(wide.Placement.M11), 3);

        // A 15x8 one is 1.09 of it: the car keeps its tyre, the rim fits in it
        var fits = WheelFit.Fit(0, Chevelle, Cragar8, Tyre, 0.32f, tyreOptional: true)!;
        Assert.DoesNotContain("WHEEL_LF_SUB4", Names(Chevelle, fits));
        Assert.False(fits.WithTyre);
        Assert.Equal(0.231f / 0.187f, Math.Abs(fits.Placement.M11), 3);
    }

    [Fact]
    public void A_wheel_that_names_no_tyre_takes_the_mesh_well_outside_the_rest_as_its_tyre()
    {
        // As tools/sc_race_harness/test_parts.py has it: a mod's wheel in German
        List<WheelMesh> car =
        [
            Mesh("Reifen", "gummi", -0.1f, 0.05f, 0.31f, 1),
            Mesh("Felge", "chrom", -0.1f, 0.04f, 0.19f, 1),
            Mesh("Stern", "chrom", 0f, 0.03f, 0.15f, 1),
            Mesh("Bremse", "brake_disc", -0.09f, -0.06f, 0.14f, 1)
        ];

        Assert.Equal([true, false, false, false], WheelFit.Tyres(car));
        Assert.Equal(0.31f, WheelFit.TyreRadius(car));
        var fit = WheelFit.Fit(0, car, Rim, null)!;
        Assert.Equal(["Felge", "Stern"], Names(car, fit));
        Assert.Equal(0.19f / 0.187f, Math.Abs(fit.Placement.M11), 3);

        // One mesh a little bigger than the rest is no tyre: the wheel is all rim (or all one mesh)
        Assert.All(WheelFit.Tyres([Mesh("a", "a", 0, 0.1f, 0.2f, 1), Mesh("b", "b", 0, 0.1f, 0.19f, 1)]), Assert.False);
    }
}
