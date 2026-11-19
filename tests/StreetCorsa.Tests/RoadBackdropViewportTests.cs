using System.Numerics;
using StreetCorsa.Controls;

namespace StreetCorsa.Tests;

/// <summary>The backdrop camera's way along a road: round and round a loop, through black before an open line's end</summary>
public sealed class RoadBackdropViewportTests
{
    private const float Fade = 0.7f;

    [Fact]
    public void A_line_whose_ends_lie_apart_is_open_one_that_comes_back_on_itself_is_not()
    {
        Assert.True(RoadBackdropViewport3D.IsOpen(new Vector3(0, 0, 0), new Vector3(0, 0, 600)));
        Assert.False(RoadBackdropViewport3D.IsOpen(new Vector3(0, 0, 0), new Vector3(3, 0, 2)));
    }

    [Fact]
    public void An_open_road_kept_whole_goes_through_black_before_its_end_a_loop_kept_whole_does_not()
    {
        // A 600 m hill climb kept whole: one stretch, the whole line
        Assert.False(RoadBackdropViewport3D.NearsEnd((0f, 600f), 600f, open: true, at: 100f, Fade));
        Assert.True(RoadBackdropViewport3D.NearsEnd((0f, 600f), 600f, open: true, at: 560f, Fade));

        // A loop kept whole is flown round and round
        Assert.False(RoadBackdropViewport3D.NearsEnd((0f, 600f), 600f, open: false, at: 590f, Fade));

        // A stretch of a long road ends short of the line's end, open or not
        Assert.True(RoadBackdropViewport3D.NearsEnd((1000f, 2000f), 5000f, open: false, at: 1960f, Fade));
    }

    [Fact]
    public void Past_its_end_a_loop_comes_round_again_an_open_line_holds_its_end()
    {
        Assert.Equal(36f, RoadBackdropViewport3D.OnLine(636f, 600f, open: false), 3);
        Assert.Equal(590f, RoadBackdropViewport3D.OnLine(-10f, 600f, open: false), 3);
        Assert.Equal(600f, RoadBackdropViewport3D.OnLine(636f, 600f, open: true), 3);
        Assert.Equal(0f, RoadBackdropViewport3D.OnLine(-10f, 600f, open: true), 3);
    }
}
