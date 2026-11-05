using StreetCorsa.Audio;
using StreetCorsa.Controls;
using System.Numerics;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-11-05 audit's fixes in the sound and the controls: a sample-load error is no silence, the drive script and
/// the tach share one top, and the leans of a bare rim and of a stance
/// </summary>
public sealed class Audit1105ETests
{
    // ----- EngineLoudness: samples FMOD cannot load are never heard as silence -----

    [Fact]
    public void Samples_still_loading_wait_and_loaded_ones_are_in()
    {
        Assert.False(EngineLoudness.SamplesIn(FmodStudio.ResultOk, FmodStudio.LoadingStateLoading));
        Assert.True(EngineLoudness.SamplesIn(FmodStudio.ResultOk, FmodStudio.LoadingStateLoaded));
    }

    [Fact]
    public void A_samples_error_or_a_state_fmod_cannot_tell_throws_so_it_is_kept_for_the_session_only()
    {
        // Plays takes an FmodException as this PC's trouble (Unopened), never as a silent bank (engine_silent.json)
        Assert.Throws<FmodException>(() => EngineLoudness.SamplesIn(FmodStudio.ResultOk, FmodStudio.LoadingStateError));
        Assert.Throws<FmodException>(() => EngineLoudness.SamplesIn(30, FmodStudio.LoadingStateLoaded));
    }

    // ----- EngineSpec: one idle, limiter and redline for the sim, the tach and the drive script -----

    private static EngineSpec Spec(double idle, double limiter, double redline) =>
        new("test", idle, limiter, 8, 1, _ => 300, 300, null, null, RedlineRpm: redline);

    [Theory]
    [InlineData(700, 6500, 5800, 700, 6500, 5800)]   // a redline under the cut is the top
    [InlineData(700, 6500, 7000, 700, 6500, 6500)]   // one past it is the cut
    [InlineData(700, 6500, 0, 700, 6500, 6500)]      // none says: the cut
    [InlineData(0, 0, 0, 800, 6000, 6000)]           // nothing says anything
    [InlineData(900, 1500, 0, 900, 6000, 6000)]      // a cut under a thousand past the idle says nothing
    [InlineData(700, 6500, 650, 700, 6500, 6500)]    // a redline under the idle says nothing
    public void The_spec_says_the_idle_limiter_and_redline_the_sim_runs_to(double idle, double limiter, double redline,
        double wantIdle, double wantLimiter, double wantRedline)
    {
        var spec = Spec(idle, limiter, redline);
        Assert.Equal((wantIdle, wantLimiter, wantRedline), (spec.Idle, spec.Limiter, spec.Redline));

        var sim = new EngineSim(spec);
        Assert.Equal((spec.Idle, spec.Limiter, spec.Redline), (sim.Idle, sim.Limiter, sim.Redline));
    }

    // ----- ShownPartsOnCar.LeanThrough: the plane a car's bare rims put it on -----

    private const float HalfTrack = 0.75f;
    private const float HalfBase = 1.35f;

    private static float HeightAt(Matrix4x4 lean, float x, float z) => Vector3.Transform(new Vector3(x, 0f, z), lean).Y;

    [Fact]
    public void Four_wheels_level_and_down_alike_only_lower_the_car()
    {
        var lean = ShownPartsOnCar.LeanThrough([(HalfTrack, HalfBase, -0.05f), (-HalfTrack, HalfBase, -0.05f),
            (HalfTrack, -HalfBase, -0.05f), (-HalfTrack, -HalfBase, -0.05f)]);

        Assert.NotNull(lean);
        Assert.Equal(-0.05f, HeightAt(lean.Value, HalfTrack, HalfBase), 4);
        Assert.Equal(-0.05f, HeightAt(lean.Value, -HalfTrack, -HalfBase), 4);
    }

    [Fact]
    public void One_corner_down_leans_the_car_down_there_and_up_across_from_it()
    {
        var lean = ShownPartsOnCar.LeanThrough([(HalfTrack, HalfBase, -0.1f), (-HalfTrack, HalfBase, 0f),
            (HalfTrack, -HalfBase, 0f), (-HalfTrack, -HalfBase, 0f)]);

        // The least-squares plane: a quarter down in the middle, half as much again at the low corner, up at the other
        Assert.NotNull(lean);
        Assert.Equal(-0.025f, HeightAt(lean.Value, 0f, 0f), 3);
        Assert.Equal(-0.075f, HeightAt(lean.Value, HalfTrack, HalfBase), 3);
        Assert.Equal(0.025f, HeightAt(lean.Value, -HalfTrack, -HalfBase), 3);
        Assert.Equal(-0.025f, HeightAt(lean.Value, -HalfTrack, HalfBase), 3);
    }

    [Fact]
    public void Too_few_wheels_or_wheels_in_a_line_say_no_lean()
    {
        Assert.Null(ShownPartsOnCar.LeanThrough([(HalfTrack, HalfBase, -0.1f), (-HalfTrack, HalfBase, 0f)]));
        Assert.Null(ShownPartsOnCar.LeanThrough([(0f, HalfBase, -0.1f), (0f, 0f, 0f), (0f, -HalfBase, 0f)]));
    }

    // ----- CarBodyRock.LeanOf: which way a roll and a pitch turn the body -----

    private static readonly Vector3 Pivot = new(0f, 0.45f, 0f);

    [Fact]
    public void A_positive_roll_lifts_the_left_whichever_side_the_model_has_it()
    {
        var leftAtPlusX = CarBodyRock.LeanOf(new BodyPose(0.05f, 0f, 0f), Pivot, 1f, 1f);
        Assert.True(HeightAt(leftAtPlusX, HalfTrack, 0f) > HeightAt(leftAtPlusX, -HalfTrack, 0f));

        var leftAtMinusX = CarBodyRock.LeanOf(new BodyPose(0.05f, 0f, 0f), Pivot, -1f, 1f);
        Assert.True(HeightAt(leftAtMinusX, -HalfTrack, 0f) > HeightAt(leftAtMinusX, HalfTrack, 0f));
    }

    [Fact]
    public void A_positive_pitch_lifts_the_nose_whichever_end_the_model_has_it()
    {
        var noseAtPlusZ = CarBodyRock.LeanOf(new BodyPose(0f, 0.05f, 0f), Pivot, 1f, 1f);
        Assert.True(HeightAt(noseAtPlusZ, 0f, HalfBase) > HeightAt(noseAtPlusZ, 0f, -HalfBase));

        var noseAtMinusZ = CarBodyRock.LeanOf(new BodyPose(0f, 0.05f, 0f), Pivot, 1f, -1f);
        Assert.True(HeightAt(noseAtMinusZ, 0f, -HalfBase) > HeightAt(noseAtMinusZ, 0f, HalfBase));
    }

    [Fact]
    public void The_heave_lifts_the_whole_car_and_the_pivot_stays_put_in_a_lean()
    {
        var lifted = CarBodyRock.LeanOf(new BodyPose(0f, 0f, 0.03f), Pivot, 1f, 1f);
        Assert.Equal(0.03f, HeightAt(lifted, HalfTrack, HalfBase), 5);

        var leaned = CarBodyRock.LeanOf(new BodyPose(0.05f, 0.04f, 0f), Pivot, 1f, 1f);
        var pivot = Vector3.Transform(Pivot, leaned);
        Assert.Equal(Pivot.Y, pivot.Y, 5);
        Assert.Equal(0f, pivot.X, 5);
    }
}
