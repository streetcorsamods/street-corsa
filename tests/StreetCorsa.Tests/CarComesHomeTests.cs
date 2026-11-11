using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Screens.RaceLoading;
using StreetCorsa.Services.Station;

namespace StreetCorsa.Tests;

/// <summary>
/// What came of the player's car in a race that ran, for the way home: the car won is the one the race put at stake
/// (never just a new car: an earlier race settled as this one set off moves cars too), the one raced gone is lost, a
/// crash tows it, one that will not run is towed too, one the police took is theirs. And the tow is always paid.
/// </summary>
public sealed class CarComesHomeTests
{
    private static readonly Func<Car, bool> Runs = _ => true;
    private static readonly Func<Car, bool> WontRun = _ => false;

    private static RaceContext PinkSlip(Car mine, Guid theirs) =>
        new() { IsPinkSlip = true, PlayerCarInstanceId = mine.InstanceId, OpponentCarInstanceId = theirs };

    [Fact]
    public void The_car_won_is_the_one_the_race_put_at_stake()
    {
        var mine = new Car();
        var theirs = new Car();

        var (way, won) = RaceLoadingScreenViewModel.CarAfterRace(PinkSlip(mine, theirs.InstanceId), [mine, theirs], mine.InstanceId, false, Runs);

        Assert.Equal(AfterRaceCar.Won, way);
        Assert.Same(theirs, won);
    }

    [Fact]
    public void A_car_from_an_earlier_race_settled_meanwhile_is_not_won_in_this_one()
    {
        var mine = new Car();
        var fromEarlier = new Car();

        var (way, won) = RaceLoadingScreenViewModel.CarAfterRace(PinkSlip(mine, Guid.NewGuid()), [mine, fromEarlier], mine.InstanceId, false, Runs);
        Assert.Equal(AfterRaceCar.Kept, way);
        Assert.Null(won);

        (way, _) = RaceLoadingScreenViewModel.CarAfterRace(new RaceContext(), [mine, fromEarlier], mine.InstanceId, false, Runs);
        Assert.Equal(AfterRaceCar.Kept, way);
    }

    [Fact]
    public void The_car_raced_gone_is_lost_whatever_else_happened()
    {
        var other = new Car();

        var (way, _) = RaceLoadingScreenViewModel.CarAfterRace(null, [other], Guid.NewGuid(), towed: true, WontRun);

        Assert.Equal(AfterRaceCar.Lost, way);
    }

    [Fact]
    public void A_crash_tows_the_car_and_one_that_will_not_run_is_towed_too()
    {
        var mine = new Car();

        Assert.Equal(AfterRaceCar.Towed, RaceLoadingScreenViewModel.CarAfterRace(null, [mine], mine.InstanceId, true, Runs).Car);
        Assert.Equal(AfterRaceCar.WontRun, RaceLoadingScreenViewModel.CarAfterRace(null, [mine], mine.InstanceId, false, WontRun).Car);
        Assert.Equal(AfterRaceCar.Kept, RaceLoadingScreenViewModel.CarAfterRace(null, [mine], mine.InstanceId, false, Runs).Car);
    }

    [Fact]
    public void A_car_the_police_took_is_not_towed_home()
    {
        var mine = new Car { ImpoundedUntil = new DateTime(1968, 6, 1) };

        Assert.Equal(AfterRaceCar.Kept, RaceLoadingScreenViewModel.CarAfterRace(null, [mine], mine.InstanceId, false, WontRun).Car);
    }

    [Fact]
    public void A_car_won_comes_home_even_when_the_one_raced_crashed()
    {
        var mine = new Car();
        var theirs = new Car();

        var (way, won) = RaceLoadingScreenViewModel.CarAfterRace(PinkSlip(mine, theirs.InstanceId), [mine, theirs], mine.InstanceId, true, Runs);

        Assert.Equal(AfterRaceCar.Won, way);
        Assert.Same(theirs, won);
    }

    [Theory]
    [InlineData(100, 25, 25, 75)]
    [InlineData(10, 25, 10, 0)]
    [InlineData(0, 25, 0, 0)]
    [InlineData(-5, 25, 0, -5)]
    public void A_tow_is_always_paid_all_the_player_has_when_short(decimal money, decimal price, decimal paid, decimal left)
    {
        var player = new Player { Money = money };

        Assert.Equal(paid, GasStation.PayTow(player, price));
        Assert.Equal(left, player.Money);
    }

    [Fact]
    public void A_race_tow_costs_the_default_tow_without_a_station_and_follows_the_price_multiplier()
    {
        Assert.Equal(StationPrices.Default.Tow, StationScenes.RaceTow([]));
        Assert.Equal(50m, GasStation.TowPrice(25m, new GameRules { PartPriceMultiplier = 2.0 }));
    }
}
