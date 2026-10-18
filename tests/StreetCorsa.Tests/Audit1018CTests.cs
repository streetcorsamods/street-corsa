using StreetCorsa.Controls;
using StreetCorsa.Controls.Street;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-10-18 audit's fixes in the 3D controls: the one rule for a car's wheel nodes, the wait on the parts being
/// laid out that took the place of a poll, and a car whose data cannot be read saying so
/// </summary>
public class Audit1018CTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ----- The wheel nodes: one rule, whatever the case the model has them in -----

    [Theory]
    [InlineData("WHEEL_LF", 0)]
    [InlineData("wheel_rf", 1)]
    [InlineData("Wheel_LR", 2)]
    [InlineData("WHEEL_RR", 3)]
    [InlineData("WHEEL_LF_SUB", -1)]
    [InlineData("SUSP_LF", -1)]
    [InlineData("", -1)]
    [InlineData(null, -1)]
    public void A_wheel_node_is_known_by_its_name_in_any_case(string? name, int corner) =>
        Assert.Equal(corner, ShownPartsOnCar.WheelCornerOf(name));

    [Fact]
    public void The_wheel_corners_are_the_fit_rules_corners()
    {
        for (var corner = 0; corner < WheelFit.WheelNodes.Length; corner++)
            Assert.Equal(corner, ShownPartsOnCar.WheelCornerOf(WheelFit.WheelNodes[corner]));
    }

    // ----- The wait on the parts: ends as the latest run does, looks again for a run queued meanwhile, gives up in time -----

    [Fact]
    public async Task The_wait_on_the_parts_ends_as_the_latest_run_does()
    {
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var latest = first.Task;
        var wait = CarViewport3D.AwaitLatestAsync(() => latest, () => true, TimeSpan.FromSeconds(20));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(wait.IsCompleted);

        // The run queued another before it was done: the wait looks again and finds the newer one
        latest = second.Task;
        first.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(wait.IsCompleted);

        second.SetResult();
        Assert.True(await wait.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_wait_on_the_parts_is_over_at_once_when_nothing_runs()
    {
        Assert.True(await CarViewport3D.AwaitLatestAsync(() => Task.CompletedTask, () => true, TimeSpan.FromSeconds(20)));
        Assert.False(await CarViewport3D.AwaitLatestAsync(() => new TaskCompletionSource().Task, () => false, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public async Task The_wait_on_the_parts_gives_up_after_its_patience()
    {
        var never = new TaskCompletionSource();
        var wait = CarViewport3D.AwaitLatestAsync(() => never.Task, () => true, TimeSpan.FromMilliseconds(100));
        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    // ----- A car whose data cannot be read: the caller hears why, the car sits as most do -----

    [Fact]
    public void A_car_whose_data_cannot_be_read_says_so_and_sits_as_most_cars_do()
    {
        var car = _temp.Combine("cars", "bad_acd");
        Directory.CreateDirectory(car);
        // An archive whose first entry claims a name longer than the file: the reader refuses it
        File.WriteAllBytes(Path.Combine(car, "data.acd"), BitConverter.GetBytes(1000));

        Exception? failed = null;
        var frame = SceneLights.FrameOf(car, null, ex => failed = ex);

        Assert.NotNull(failed);
        Assert.Equal(0f, frame.Lift);
    }

    [Fact]
    public void A_car_without_data_is_no_failure()
    {
        var car = _temp.Combine("cars", "no_data");
        Directory.CreateDirectory(car);

        var failed = false;
        var frame = SceneLights.FrameOf(car, null, _ => failed = true);

        Assert.False(failed);
        Assert.Equal(0f, frame.Lift);
    }
}
