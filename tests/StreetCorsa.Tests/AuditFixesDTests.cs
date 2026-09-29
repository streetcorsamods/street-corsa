using System.IO;
using StreetCorsa.Audio;
using StreetCorsa.Controls;
using StreetCorsa.Controls.Street;

namespace StreetCorsa.Tests;

/// <summary>
/// Tech audit 2026-09-29, the viewports and the engine sound: the pure logic behind the fixes. The rendering itself
/// (render loop guards, cancelled loads, device checks, the onboard viewports) cannot be run here.
/// </summary>
public class AuditFixesDTests
{
    // ---- Shadows drawn again only for a lean that shows ----

    [Fact]
    public void An_idling_engine_shake_does_not_draw_the_shadows_again()
    {
        // The idle shake at its most, a lumpy four's: 0.13 degrees and 2 mm either way
        var shake = new BodyPose(0.13f * MathF.PI / 180f, 0.0003f, 0.002f);
        Assert.False(CarBodyRock.ShadowsDue(BodyPose.Rest, shake));
        Assert.False(CarBodyRock.ShadowsDue(shake, new BodyPose(-shake.Roll, -shake.Pitch, -shake.Heave)));
    }

    [Fact]
    public void A_blip_that_leans_the_body_draws_the_shadows_again()
    {
        // Full torque leans the body 1.3 degrees
        var blip = new BodyPose(1.3f * MathF.PI / 180f, 0f, 0f);
        Assert.True(CarBodyRock.ShadowsDue(BodyPose.Rest, blip));

        // Past the step from where they were last drawn, whichever way it moved
        Assert.True(CarBodyRock.ShadowsDue(blip, BodyPose.Rest));
        Assert.True(CarBodyRock.ShadowsDue(BodyPose.Rest, new BodyPose(0f, -CarBodyRock.ShadowLeanStep, 0f)));
        Assert.True(CarBodyRock.ShadowsDue(BodyPose.Rest, new BodyPose(0f, 0f, CarBodyRock.ShadowHeaveStep)));
        Assert.False(CarBodyRock.ShadowsDue(blip, blip with { Roll = blip.Roll + CarBodyRock.ShadowLeanStep / 2f }));
    }

    // ---- The onboard viewports read the car's data the way the race export does ----

    [Fact]
    public void A_car_folder_without_data_has_none_and_says_nothing()
    {
        using var temp = new TempDir();
        var failures = 0;
        Assert.Null(SceneLights.DataOf(temp.Path, _ => failures++));
        Assert.Null(AcCarFrame.Read(temp.Path));
        Assert.Equal(0, failures);
    }

    [Fact]
    public void Vectors_and_numbers_come_out_of_an_ini_as_ac_tools_read_them()
    {
        var ini = new Parts.Export.IniText("[GRAPHICS]\nDRIVEREYES= 0.4 , 1.1,-0.2 ; the eyes\nBAD=1,2\nPART=1,x,3\n[CONTROLS]\nSTEER_RATIO=14.5\nEMPTY=\n");

        Assert.Equal([0.4f, 1.1f, -0.2f], CarData.Vector3(ini, "GRAPHICS", "DRIVEREYES")!);
        Assert.Null(CarData.Vector3(ini, "GRAPHICS", "BAD"));
        Assert.Null(CarData.Vector3(ini, "GRAPHICS", "MISSING"));
        Assert.Equal([1f, 0f, 3f], CarData.Vector3(ini, "GRAPHICS", "PART")!);

        Assert.Equal(14.5f, CarData.Float(ini, "CONTROLS", "STEER_RATIO", 16f));
        Assert.Equal(16f, CarData.Float(ini, "CONTROLS", "STEER_LOCK", 16f));
        Assert.Null(CarData.Text(ini, "CONTROLS", "EMPTY"));
    }
}
