using System.Numerics;
using StreetCorsa.Controls;
using StreetCorsa.Controls.Scripting;
using StreetCorsa.Services.Scripting;
using StreetCorsa.Services.Station;
using Xunit;

namespace StreetCorsa.Tests;

/// <summary>Tech audit 2026-11-12, worker A: car moves lighter and smoother (the render core, the scripted car, its plans)</summary>
public class Audit1112ATests
{
    // ----- The sun's shadows' centre snapped to whole texels -----

    private static readonly Vector3 Sun = Vector3.Normalize(new Vector3(0.2f, -1f, -0.8f));
    private const float Step = 100f / 2048f;

    [Fact]
    public void A_centre_sliding_within_a_texel_keeps_the_same_snapped_point()
    {
        var snapped = SunShadowSnap.Snapped(new Vector3(1.2345f, 0.5f, -3.21f), Sun, Step);

        // On the grid already, and a slide of a tenth of a texel from there lands back on it, but for how far along the
        // sun's way it went (which the shadow cameras do not see)
        Assert.True(Vector3.Distance(snapped, SunShadowSnap.Snapped(snapped, Sun, Step)) < 1e-4f);
        var slid = SunShadowSnap.Snapped(snapped + new Vector3(Step / 10f, 0f, -Step / 10f), Sun, Step) - snapped;
        Assert.True((slid - Vector3.Dot(slid, Sun) * Sun).Length() < 1e-4f);
    }

    [Fact]
    public void A_snapped_centre_moves_only_across_the_sun_and_by_under_a_texel()
    {
        var centre = new Vector3(3.3f, 0.7f, 11.9f);
        var snapped = SunShadowSnap.Snapped(centre, Sun, Step);

        Assert.Equal(0f, Vector3.Dot(snapped - centre, Sun), 4);
        Assert.True(Vector3.Distance(snapped, centre) <= Step * 0.75f);
    }

    [Fact]
    public void A_sun_straight_down_snaps_too_and_no_step_leaves_the_centre_as_it_is()
    {
        var centre = new Vector3(0.123f, 1f, 0.456f);
        var snapped = SunShadowSnap.Snapped(centre, -Vector3.UnitY, Step);
        Assert.Equal(centre.Y, snapped.Y, 5);
        Assert.True(Vector3.Distance(snapped, centre) <= Step * 0.75f);

        Assert.Equal(centre, SunShadowSnap.Snapped(centre, Sun, 0f));
    }

    // ----- The scripted car: rolling drives joined without a lost frame, the early wait on the frame it is due -----

    private static DrivePath Straight(float fromZ, float toZ) => DrivePath.Through([new GroundPoint(0f, fromZ), new GroundPoint(0f, toZ)]);

    [Fact]
    public void A_rolling_drive_on_from_where_the_last_ended_starts_as_far_into_itself_as_the_last_frame_ran_past()
    {
        var first = DrivePlan.Road(Straight(0f, 10f), DriveFeel.Road, 800, rolling: true, stop: false);
        var second = DrivePlan.Road(Straight(10f, 20f), DriveFeel.Road, 800, rolling: true, stop: false);
        var car = new ScriptedCar(first.Start.Pose, null, (_, _) => { });
        _ = car.Drive(first);

        const float dt = 1f / 60f;
        var time = 0f;
        while (car.Plan != null)
        {
            car.Step(dt);
            time += dt;
        }

        var past = time - first.Duration;
        Assert.True(past > 0f);

        _ = car.Drive(second);
        car.Step(dt);
        Assert.Equal(second.At(past + dt).Pose.Z, car.Pose.Z, 3);
    }

    [Fact]
    public void A_drive_after_the_car_stood_a_frame_starts_from_its_own_beginning()
    {
        var first = DrivePlan.Road(Straight(0f, 10f), DriveFeel.Road, 800, rolling: true, stop: false);
        var second = DrivePlan.Road(Straight(10f, 20f), DriveFeel.Road, 800, rolling: true, stop: false);
        var car = new ScriptedCar(first.Start.Pose, null, (_, _) => { });
        _ = car.Drive(first);
        while (car.Plan != null) car.Step(1f / 60f);
        car.Step(1f / 60f);

        _ = car.Drive(second);
        car.Step(1f / 60f);
        Assert.Equal(second.At(1f / 60f).Pose.Z, car.Pose.Z, 3);
    }

    [Fact]
    public async Task The_wait_for_a_drive_to_have_so_long_left_is_done_on_the_step_that_gets_there()
    {
        var plan = DrivePlan.Road(Straight(0f, 12f), DriveFeel.Road, 800, rolling: false, stop: true);
        var car = new ScriptedCar(plan.Start.Pose, null, (_, _) => { });
        _ = car.Drive(plan);

        var due = car.DriveLeftAtMost(0.5f);
        Assert.False(due.IsCompleted);
        while (car.DriveLeft > 0.5f)
        {
            Assert.False(due.IsCompleted);
            car.Step(1f / 60f);
        }

        await due.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(car.DriveLeftAtMost(0.5f).IsCompleted);
    }

    [Fact]
    public async Task The_wait_is_let_go_when_the_drive_is_dropped()
    {
        var plan = DrivePlan.Road(Straight(0f, 12f), DriveFeel.Road, 800, rolling: false, stop: true);
        var car = new ScriptedCar(plan.Start.Pose, null, (_, _) => { });
        _ = car.Drive(plan);
        var due = car.DriveLeftAtMost(0.1f);

        car.Drop();
        await due.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    // ----- Plans kept a moment every other step -----

    [Fact]
    public void A_launch_roll_in_and_pull_still_end_where_their_way_does_and_say_so()
    {
        var path = Straight(0f, 25f);
        foreach (var plan in new[]
                 {
                     DrivePlan.Launch(path, DriveFeel.Road, 800, 6000),
                     DrivePlan.RollIn(path, DriveFeel.Road, 800),
                     DrivePlan.Pull(new GroundPose(0f, 0f, 0f), null, DriveFeel.Road, 800)
                 })
        {
            Assert.True(plan.End.Done);
            Assert.Equal(plan.End, plan.At(plan.Duration));
            Assert.True(plan.Duration > 0f);

            // At goes smoothly between the kept moments: no step of the way jumps
            var last = plan.Start.Distance;
            for (var t = 0f; t <= plan.Duration; t += 1f / 240f)
            {
                var now = plan.At(t).Distance;
                Assert.True(now >= last - 1e-4f && now - last < 0.5f, $"{t}: {last} -> {now}");
                last = now;
            }
        }

        Assert.Equal(25f, DrivePlan.RollIn(path, DriveFeel.Road, 800).End.Distance, 2);
        Assert.True(DrivePlan.Launch(path, DriveFeel.Road, 800, 6000).End.Distance >= 25f - 1e-3f);
    }
}
