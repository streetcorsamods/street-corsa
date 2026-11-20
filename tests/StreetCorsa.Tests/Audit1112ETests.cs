using StreetCorsa.Audio;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Screens.RaceLoading;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services;

namespace StreetCorsa.Tests;

/// <summary>
/// The 2026-11-12 audit's fixes in the screens and the sound: an earlier pending race's crash or bust neither tows nor
/// charges for this race's car, the tows a race leaves to pay, one rule for why a car will not drive home, and the exit's
/// wait on the monotonic clock
/// </summary>
public sealed class Audit1112ETests
{
    private static readonly Func<Car, bool> Runs = _ => true;
    private static readonly Func<Car, bool> WontRun = _ => false;

    private static PlayerMessage Crash() => new("Crash", "Towed home") { TowedToGarage = true };

    // ----- An earlier race's messages go with this one's, shown but not towing -----

    [Fact]
    public void The_earlier_races_messages_are_kept_but_none_says_this_car_was_towed()
    {
        var earlier = AssettoCorsaLauncher.AsEarlier([Crash(), new PlayerMessage("Prize", "A camshaft")]);

        Assert.Equal(["Towed home", "A camshaft"], earlier.Select(m => m.Text));
        Assert.DoesNotContain(earlier, m => m.TowedToGarage);
    }

    [Fact]
    public void An_earlier_races_crash_does_not_send_this_races_car_home_on_the_hook()
    {
        var mine = new Car();
        var messages = AssettoCorsaLauncher.AsEarlier([Crash()]);

        var (way, _) = RaceLoadingScreenViewModel.CarAfterRace(null, [mine], mine.InstanceId, towed: messages.Any(m => m.TowedToGarage), Runs);
        var tows = RaceLoadingScreenViewModel.TowsAfterRace([mine], mine.InstanceId, null, messages, Runs);

        Assert.Equal(AfterRaceCar.Kept, way);
        Assert.Equal(-1, tows.Crash);
        Assert.Null(tows.WontRun);
    }

    // ----- The tows a race leaves to pay -----

    [Fact]
    public void A_crash_and_a_car_won_that_will_not_drive_are_two_tows()
    {
        var mine = new Car();
        var won = new Car();
        var messages = AssettoCorsaLauncher.AsEarlier([Crash()]);
        messages.Add(Crash());

        var tows = RaceLoadingScreenViewModel.TowsAfterRace([mine, won], mine.InstanceId, won, messages, WontRun);

        // The crash message is this race's (the second), and carries the raced car's tow; no separate one for it
        Assert.Equal(1, tows.Crash);
        Assert.Same(mine, tows.Raced);
        Assert.Null(tows.WontRun);
        Assert.Same(won, tows.Won);
    }

    [Fact]
    public void An_impounded_car_is_the_polices_and_never_towed()
    {
        var mine = new Car { ImpoundedUntil = new DateTime(1968, 6, 1) };
        var won = new Car { ImpoundedUntil = new DateTime(1968, 6, 1) };

        var tows = RaceLoadingScreenViewModel.TowsAfterRace([mine, won], mine.InstanceId, won, [Crash()], WontRun);

        Assert.Equal(-1, tows.Crash);
        Assert.Null(tows.Raced);
        Assert.Null(tows.WontRun);
        Assert.Null(tows.Won);
    }

    [Fact]
    public void A_car_that_will_not_run_is_towed_and_one_that_drives_home_is_not()
    {
        var mine = new Car();

        Assert.Same(mine, RaceLoadingScreenViewModel.TowsAfterRace([mine], mine.InstanceId, null, [], WontRun).WontRun);

        var home = RaceLoadingScreenViewModel.TowsAfterRace([mine], mine.InstanceId, null, [], Runs);
        Assert.Equal(-1, home.Crash);
        Assert.Null(home.WontRun);
        Assert.Null(home.Won);
    }

    // ----- One rule for why a car will not drive home -----

    [Fact]
    public void A_car_drives_home_exactly_when_nothing_keeps_it_from_it()
    {
        var full = new Car("car") { FuelLitres = 40, FuelTankLitres = 60 };
        var dry = new Car("car") { FuelLitres = 0.1, FuelTankLitres = 60 };
        var impounded = new Car("car") { ImpoundedUntil = new DateTime(1968, 6, 1) };

        foreach (var car in new[] { full, dry, impounded })
            Assert.Equal(DrivingHome.WhyNotHome(car, null).Count == 0, DrivingHome.Drives(car, null));

        Assert.True(DrivingHome.Drives(full, null));
        Assert.Single(DrivingHome.WhyNotHome(dry, null));
        Assert.False(DrivingHome.Drives(impounded, null));
    }

    // ----- The exit's wait and a failed sample load -----

    [Fact]
    public void The_exits_wait_counts_down_on_the_monotonic_clock_and_never_goes_below_zero()
    {
        Assert.Equal(TimeSpan.Zero, FmodLifetime.Left(Environment.TickCount64 - 1000));

        var left = FmodLifetime.Left(Environment.TickCount64 + 1500);
        Assert.InRange(left, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void Samples_that_failed_to_load_are_not_told_as_an_fmod_result_code()
    {
        var failed = Assert.Throws<FmodException>(() => EngineLoudness.SamplesIn(FmodStudio.ResultOk, FmodStudio.LoadingStateError));

        Assert.Null(failed.Code);
        Assert.DoesNotContain("error 4", failed.Message);
    }
}
