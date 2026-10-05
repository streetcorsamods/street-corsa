using System.Numerics;
using StreetCorsa.Controls;
using StreetCorsa.Helpers;
using StreetCorsa.Services.Station;

namespace StreetCorsa.Tests;

/// <summary>
/// Tech audit 2026-10-05, the 3D controls: the pure logic behind the fixes. The viewports themselves (the device work a
/// renderer waits for, the fade, a lost device) cannot be run here.
/// </summary>
public class Audit1005ETests
{
    // ---- The diner's parks, one after another as the street's rival loads ----

    [Fact]
    public async Task A_load_chained_after_another_starts_only_once_that_one_is_done()
    {
        var first = new TaskCompletionSource();
        var started = false;
        var second = D3DViewportBase.After(first.Task, () =>
        {
            started = true;
            return Task.CompletedTask;
        });

        Assert.False(started);
        first.SetResult();
        await second;
        Assert.True(started);
    }

    [Fact]
    public async Task A_load_chained_after_one_that_failed_goes_ahead_all_the_same()
    {
        var started = false;
        await D3DViewportBase.After(Task.FromException(new InvalidOperationException("no car")), () =>
        {
            started = true;
            return Task.CompletedTask;
        });

        Assert.True(started);
    }

    [Fact]
    public async Task Loads_chained_in_turn_never_run_at_once()
    {
        var running = 0;
        var most = 0;
        var chain = Task.CompletedTask;
        for (var i = 0; i < 5; i++)
        {
            chain = D3DViewportBase.After(chain, async () =>
            {
                most = Math.Max(most, Interlocked.Increment(ref running));
                await Task.Delay(5);
                Interlocked.Decrement(ref running);
            });
        }

        await chain;
        Assert.Equal(1, most);
    }

    // ---- The renderer's private field the diner's shadows rely on ----

    [Fact]
    public void The_linked_AcTools_renderer_still_has_the_shadows_target_field()
    {
        Assert.NotNull(GarageRenderer.PreviousShadowsTarget);
        // What the renderer writes into it: a centre that may be none
        var type = GarageRenderer.PreviousShadowsTarget!.FieldType;
        Assert.Equal("SlimDX.Vector3", Nullable.GetUnderlyingType(type)?.FullName);
    }

    // ---- One triangle probe, with the tools' slack ----

    [Fact]
    public void A_point_on_the_edge_two_triangles_share_is_in_both()
    {
        // A square split along its diagonal, a little off true as a model's floats are
        var a = new Vector3(0f, 1f, 0f);
        var b = new Vector3(1f, 1f, 0f);
        var c = new Vector3(1f, 1f, 1.00001f);
        var d = new Vector3(0f, 1f, 1f);

        Assert.Equal(1f, Triangles.HeightAt(a, b, c, 0.3f, 0.3f));
        Assert.Equal(1f, Triangles.HeightAt(a, c, d, 0.3f, 0.3f));
    }

    [Fact]
    public void The_probe_reads_a_sloped_triangle_and_misses_outside_it()
    {
        var a = new Vector3(0f, 0f, 0f);
        var b = new Vector3(2f, 2f, 0f);
        var c = new Vector3(0f, 0f, 2f);

        Assert.Equal(1f, Triangles.HeightAt(a, b, c, 1f, 0.5f)!.Value, 4);
        Assert.Null(Triangles.HeightAt(a, b, c, 1.5f, 1.5f));
        Assert.Null(Triangles.HeightAt(a, a, c, 0f, 1f));
    }

    // ---- The station's ground read without a garbage array a call ----

    [Fact]
    public void The_ground_is_read_between_its_cells_and_cells_without_ground_left_out()
    {
        // Two by two cells of a metre from (0, 0): heights 0, 1 on the first row, 2 and none on the second
        var ground = new StationGround(1f, 2, 2, 0f, 0f, [0f, 1f, 2f, float.NaN]);

        Assert.Equal(0.5f, ground.HeightAt(0.5f, 0f), 4);
        Assert.Equal(1f, ground.HeightAt(0f, 0.5f), 4);
        Assert.Equal(1f, ground.HeightAt(1f, 0f), 4);

        // The cell with no ground does not pull the height down
        Assert.Equal((0f * 0.25f + 1f * 0.25f + 2f * 0.25f) / 0.75f, ground.HeightAt(0.5f, 0.5f), 4);
        Assert.Equal(0f, StationGround.Flat.HeightAt(3f, 4f));
        Assert.Equal(0f, ground.HeightAt(-10f, -10f));
    }
}
