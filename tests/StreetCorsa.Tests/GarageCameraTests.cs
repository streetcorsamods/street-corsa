using System.Numerics;
using StreetCorsa.Controls;

namespace StreetCorsa.Tests;

/// <summary>
/// The garage camera: the car framed between the screen's panels (both sides now), the player's hand on the orbit
/// (a drag eased, a fling on letting go), and the pointer asked what it is on so often at most
/// </summary>
public class GarageCameraTests
{
    private const float FovY = 32f * MathF.PI / 180f;

    // A sixties sedan's box, as the car's world box has it: across, high, long
    private static readonly Vector3 Sedan = new(1.9f, 1.4f, 5.0f);

    #region Framing between the panels

    [Fact]
    public void Panels_on_both_sides_alike_leave_the_car_in_the_middle()
    {
        Assert.Equal(0f, PreviewFraming.SideShift(310, 310, 1920, 1080, 7f, FovY), 5);
    }

    [Fact]
    public void A_panel_on_the_right_moves_the_car_the_other_way_as_far()
    {
        var left = PreviewFraming.SideShift(340, 0, 1920, 1080, 7f, FovY);
        var right = PreviewFraming.SideShift(0, 340, 1920, 1080, 7f, FovY);

        Assert.True(left > 0f);
        Assert.Equal(-left, right, 5);
        // A panel on the left alone is the old one-sided shift
        Assert.Equal(PreviewFraming.SideShift(340, 1920, 1080, 7f, FovY), left, 5);
    }

    [Fact]
    public void Two_panels_shift_by_the_difference()
    {
        var both = PreviewFraming.SideShift(400, 300, 1920, 1080, 7f, FovY);
        var alone = PreviewFraming.SideShift(100, 0, 1920, 1080, 7f, FovY);

        Assert.Equal(alone, both, 5);
    }

    [Fact]
    public void The_uncovered_picture_is_what_the_panels_leave()
    {
        Assert.Equal(1920f / 1080f, PreviewFraming.UncoveredAspect(0, 0, 1920, 1080), 4);
        Assert.Equal(1300f / 1080f, PreviewFraming.UncoveredAspect(310, 310, 1920, 1080), 4);
        // Never under a third of the width, however much is covered; the whole picture before the view is laid out
        Assert.Equal(640f / 1080f, PreviewFraming.UncoveredAspect(1000, 1000, 1920, 1080), 4);
        Assert.Equal(16f / 9f, PreviewFraming.UncoveredAspect(300, 0, 0, 0), 4);
    }

    [Fact]
    public void The_car_stands_further_back_than_it_did_and_takes_in_the_whole_car()
    {
        var distance = PreviewFraming.GarageDistance(Sedan, FovY, 16f / 9f, 0.15f);

        // Further than the 6.5 m the garage had the camera at, which stood the car too close; not out of a 16 m room
        Assert.InRange(distance, 7.5f, 9f);

        // At its widest as it turns (the diagonal of its footprint) the car is inside the picture, with air round it
        var halfWidth = (distance - MathF.Sqrt(Sedan.X * Sedan.X + Sedan.Z * Sedan.Z) / 4f) * MathF.Tan(FovY / 2f) * 16f / 9f;
        var across = MathF.Sqrt(Sedan.X * Sedan.X + Sedan.Z * Sedan.Z);
        Assert.True(across / 2f < halfWidth * 0.9f);
        Assert.True(across / 2f > halfWidth * 0.6f);
    }

    [Fact]
    public void The_car_is_measured_from_its_wheels_not_the_renderers_box()
    {
        // A Nova: wheelbase 2.8 m, track 1.5 m, 1.4 m high; the renderer's box for it is 9.6 m long
        var frame = StreetCorsa.Controls.Showcase.CarFrame.FromWheels(
            new Vector3(0.75f, 0.33f, 1.4f), new Vector3(-0.75f, 0.33f, 1.4f),
            new Vector3(0.75f, 0.33f, -1.4f), new Vector3(-0.75f, 0.33f, -1.4f), 1.4f)!.Value;

        var size = PreviewFraming.CarSize(frame);
        var middle = PreviewFraming.CarMiddle(frame);

        Assert.InRange(size.X, 1.7f, 2.0f);
        Assert.Equal(1.4f, size.Y, 4);
        Assert.InRange(size.Z, 4.6f, 5.4f);
        Assert.Equal(frame.Length, size.Z, 4);
        // Looked at mid-length, under half way up, on the car's own middle line
        Assert.Equal(0f, middle.X);
        Assert.Equal(frame.MidZ, middle.Z, 4);
        Assert.InRange(middle.Y, 0.5f, 0.7f);
    }

    [Fact]
    public void Panels_push_the_camera_back_and_a_longer_car_too()
    {
        var open = PreviewFraming.GarageDistance(Sedan, FovY, PreviewFraming.UncoveredAspect(0, 0, 1920, 1080), 0.15f);
        var between = PreviewFraming.GarageDistance(Sedan, FovY, PreviewFraming.UncoveredAspect(310, 310, 1920, 1080), 0.15f);
        var wagon = PreviewFraming.GarageDistance(Sedan with { Z = 5.6f }, FovY, 16f / 9f, 0.15f);

        Assert.True(between > open);
        Assert.True(wagon > open);
    }

    #endregion

    #region The hand on the orbit

    [Fact]
    public void A_drag_turns_by_the_pixels_moved()
    {
        var hand = new OrbitHand(0.01f);

        var turn = hand.Drag(50, -20, 1.000);

        Assert.Equal(0.5f, turn.X, 5);
        Assert.Equal(-0.2f, turn.Y, 5);
        Assert.False(hand.IsFlinging);
    }

    [Fact]
    public void Let_go_on_the_move_the_camera_is_thrown_on_and_dies_down()
    {
        var hand = new OrbitHand(0.01f);
        // A steady drag to the right, 8 px every 8 ms (1 px/ms: 10 rad/s)
        var now = 1.0;
        for (var i = 0; i < 20; i++)
        {
            now += 0.008;
            hand.Drag(8, 0, now);
        }

        hand.Release(now + 0.01);
        Assert.True(hand.IsFlinging);

        // The fling goes the drag's way, covers about the speed over the decay (10 / 5 = 2 rad), and ends
        var carried = Vector2.Zero;
        var frames = 0;
        while (hand.IsFlinging && frames++ < 1000) carried += hand.Step(1f / 60f);

        Assert.False(hand.IsFlinging);
        Assert.InRange(carried.X, 1.5f, 2.2f);
        Assert.Equal(0f, carried.Y, 5);
        Assert.True(frames < 200);
        Assert.Equal(Vector2.Zero, hand.Step(1f / 60f));
    }

    [Fact]
    public void Held_still_before_letting_go_nothing_is_thrown()
    {
        var hand = new OrbitHand(0.01f);
        var now = 1.0;
        for (var i = 0; i < 20; i++)
        {
            now += 0.008;
            hand.Drag(8, 0, now);
        }

        hand.Release(now + 0.3);

        Assert.False(hand.IsFlinging);
        Assert.Equal(Vector2.Zero, hand.Step(1f / 60f));
    }

    [Fact]
    public void A_hand_back_on_the_camera_stops_the_throw()
    {
        var hand = new OrbitHand(0.01f);
        var now = 1.0;
        for (var i = 0; i < 20; i++)
        {
            now += 0.008;
            hand.Drag(8, 0, now);
        }
        hand.Release(now + 0.01);
        Assert.True(hand.IsFlinging);

        hand.Drag(0, 0, now + 0.5);
        Assert.False(hand.IsFlinging);

        hand.Stop();
        Assert.Equal(Vector2.Zero, hand.Step(1f / 60f));
    }

    #endregion

    #region Asking the pointer so often at most

    [Fact]
    public void The_first_ask_goes_through_and_the_next_waits_the_interval()
    {
        var throttle = new PollThrottle(30);

        Assert.True(throttle.Due(1_000_000));
        Assert.False(throttle.Due(1_000_010));
        Assert.False(throttle.Due(1_000_029));
        Assert.True(throttle.Due(1_000_030));
        Assert.False(throttle.Due(1_000_040));
    }

    [Fact]
    public void A_clock_read_before_it_was_ever_asked_is_due_whatever_it_says()
    {
        // The old throttle took now minus long.MinValue, which overflows negative: nothing was ever due, and the garage's
        // parts went without their outline under the pointer
        Assert.True(new PollThrottle(30).Due(0));
        Assert.True(new PollThrottle(30).Due(long.MaxValue));
        Assert.True(new PollThrottle(30).Due(Environment.TickCount64));
    }

    #endregion
}
