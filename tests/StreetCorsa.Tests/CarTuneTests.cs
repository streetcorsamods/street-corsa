using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Rules;

namespace StreetCorsa.Tests;

/// <summary>
/// Chassis tuning phase 3: the Tune screen's rows of a car (the setup's items, locked ones with the parts that open them,
/// the mounted parts' own specs), writing a tune back and the garage time it takes
/// </summary>
public sealed class CarTuneTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private PartsCatalog Catalog()
    {
        _temp.Parts("parts/tune/test", """
            [
                { "id": "tune/test/block", "specs": { "idle_rpm": 800, "adjust": { "idle_rpm": { "min": 600, "max": 1200, "step": 50 } } } },
                { "id": "tune/test/shock", "specs": { "damping": 2000 } },
                { "id": "tune/test/coilover", "specs": { "damping": 2000, "adjust": { "damping": { "min": 1000, "max": 4000, "step": 250 } } } },
                { "id": "tune/test/coil_kit", "specs": { "adjust": { "ride_height_front": { "min": -40, "max": 10, "step": 5 } } } }
              ]
            """);
        var catalog = PartsCatalog.Load(_temp.Combine("parts"));
        Assert.DoesNotContain(catalog.Problems, p => p.Contains("adjust"));
        return catalog;
    }

    private static Car CarWith(params PartInstance[] parts) => new() { DefinitionId = "test_car", Parts = parts.ToList(), HasPartsAssigned = true };

    private static PartInstance On(string id, int slot) => new(id) { ParentSlot = slot };

    [Fact]
    public void A_stock_car_shows_the_rules_items_and_locks_what_a_part_would_open()
    {
        var catalog = Catalog();
        var car = CarWith(On("tune/test/shock", 200));

        var rows = CarTune.Rows(car, catalog, CarSetup.For(car.Parts, car.Setup, catalog, new TuningRules().Ranges()));

        var pressure = Assert.Single(rows, r => r.Item == CarSetup.PressureFront);
        Assert.False(pressure.IsLocked);
        Assert.Equal(TuneTab.BrakesAndTyres, pressure.Tab);
        Assert.Equal(0, pressure.Stock);

        // No part on the car opens the ride height: shown locked, with the kit's shelf
        var ride = Assert.Single(rows, r => r.Item == CarSetup.RideHeightFront);
        Assert.True(ride.IsLocked);
        Assert.NotEmpty(ride.Needs);
        Assert.Equal(TuneTab.Suspension, ride.Tab);

        // Nothing opens the steering lock: no row at all
        Assert.DoesNotContain(rows, r => r.Item == CarSetup.SteerLock);
        // The plain shock adjusts nothing
        Assert.DoesNotContain(rows, r => r.Part != null);
    }

    [Fact]
    public void A_mounted_part_opens_its_items_and_its_own_specs()
    {
        var catalog = Catalog();
        var coilover = On("tune/test/coilover", 200);
        coilover.Tuning["damping"] = 3000;
        var car = CarWith(coilover, On("tune/test/coil_kit", 201));

        var rows = CarTune.Rows(car, catalog, CarSetup.For(car.Parts, car.Setup, catalog, new TuningRules().Ranges()));

        Assert.False(Assert.Single(rows, r => r.Item == CarSetup.RideHeightFront).IsLocked);
        var damping = Assert.Single(rows, r => r.Item == "damping");
        Assert.Same(coilover, damping.Part);
        Assert.Equal(2000, damping.Stock);
        Assert.Equal(3000, damping.Value);
        Assert.Equal(TuneTab.Suspension, damping.Tab);
    }

    [Fact]
    public void Items_the_data_has_no_place_for_are_not_shown()
    {
        var catalog = Catalog();
        var car = CarWith();

        var rows = CarTune.Rows(car, catalog, CarSetup.For(car.Parts, car.Setup, catalog, new TuningRules().Ranges()),
            new HashSet<string> { CarSetup.CamberRear, CarSetup.ToeRear });

        Assert.Contains(rows, r => r.Item == CarSetup.CamberFront);
        Assert.DoesNotContain(rows, r => r.Item is CarSetup.CamberRear or CarSetup.ToeRear);
    }

    [Fact]
    public void A_tune_is_written_on_its_steps_and_left_out_at_stock()
    {
        var catalog = Catalog();
        var coilover = On("tune/test/coilover", 200);
        coilover.Tuning["damping"] = 3000;
        var car = CarWith(coilover);
        car.Setup[CarSetup.ToeFront] = 0.5;
        var rows = CarTune.Rows(car, catalog, CarSetup.For(car.Parts, car.Setup, catalog, new TuningRules().Ranges()));
        TuneRow Row(string item) => rows.Single(r => r.Item == item);

        var changed = CarTune.Apply(car, [
            (Row(CarSetup.PressureFront), 0.12),     // on the 0.05 steps: 0.1
            (Row(CarSetup.ToeFront), 0),             // back to the parts' own: left out
            (Row(CarSetup.CamberFront), 0),          // as it was: no change
            (Row("damping"), 2000)                   // the part's own figure: its tuning left out
        ]);

        Assert.Equal(3, changed);
        Assert.Equal(0.1, car.Setup[CarSetup.PressureFront], 9);
        Assert.False(car.Setup.ContainsKey(CarSetup.ToeFront));
        Assert.False(car.Setup.ContainsKey(CarSetup.CamberFront));
        Assert.False(coilover.Tuning.ContainsKey("damping"));
    }

    [Fact]
    public void A_locked_row_is_never_written()
    {
        var catalog = Catalog();
        var car = CarWith();
        var rows = CarTune.Rows(car, catalog, CarSetup.For(car.Parts, car.Setup, catalog, new TuningRules().Ranges()));

        var changed = CarTune.Apply(car, [(rows.Single(r => r.Item == CarSetup.RideHeightFront), -20)]);

        Assert.Equal(0, changed);
        Assert.Empty(car.Setup);
    }

    [Fact]
    public void The_time_is_each_items_up_to_the_most_a_tune_takes()
    {
        var rules = new TuningRules { MinutesPerItem = 15, MaxMinutes = 120 };

        Assert.Equal(0, CarTune.Minutes(0, rules));
        Assert.Equal(45, CarTune.Minutes(3, rules));
        Assert.Equal(120, CarTune.Minutes(20, rules));
    }

    [Fact]
    public void The_engine_on_the_dyno_is_a_copy_the_car_keeps_its_own()
    {
        var catalog = Catalog();
        var block = On("tune/test/block", PartInstance.CarEngineSlot);
        var car = CarWith(block);
        var rows = CarTune.Rows(car, catalog, CarSetup.For(car.Parts, car.Setup, catalog, new TuningRules().Ranges()));
        var idle = rows.Single(r => r.Item == "idle_rpm");
        Assert.Equal(TuneTab.Engine, idle.Tab);
        Assert.Equal(800, idle.Stock);

        var copy = CarTune.EngineWith(car, [(idle, 1010)]);

        Assert.NotNull(copy);
        Assert.NotSame(block, copy);
        Assert.Equal(1000, copy.Tuning["idle_rpm"]);
        Assert.Empty(block.Tuning);
    }
}
