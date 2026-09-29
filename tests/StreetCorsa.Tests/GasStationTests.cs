using StreetCorsa.Controls.Street;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Mods;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Tests;

/// <summary>Step 19: the gas station, the oil the engines burn, and what the attendant notices</summary>
public class GasStationTests
{
    private static Lazy<PartsCatalog> Catalog => TestCars.Catalog;

    private static Car CarWithRealEngine(PartsCatalog catalog) => TestCars.WithRealEngine(catalog);

    private static Car CarWithEngineAndWheels(double wear = 1)
    {
        var car = new Car("car_a") { HasPartsAssigned = true, HasRunningGearAssigned = true };
        car.Parts.Add(new PartInstance("block") { ParentSlot = PartInstance.CarEngineSlot, Wear = wear });
        TestCars.AddRunningGear(car, springs: false);
        return car;
    }

    private static RaceCarCondition Finished(double fuel = 30, double tank = 60) => new() { FuelLitres = fuel, MaxFuelLitres = tank };

    // ---- The oil ----

    [Fact]
    public void A_race_burns_a_little_oil_and_a_worn_engine_burns_more()
    {
        var fresh = CarWithEngineAndWheels(wear: 1);
        var worn = CarWithEngineAndWheels(wear: 0);
        Assert.Equal(1, CarCondition.OilLeft(fresh));

        CarCondition.ApplyRace(fresh, Finished(), 10, null);
        CarCondition.ApplyRace(worn, Finished(), 10, null);

        var freshBurn = 1 - CarCondition.OilLeft(fresh);
        var wornBurn = 1 - CarCondition.OilLeft(worn);
        Assert.Equal(CarCondition.OilBurnPerRace + 10 * CarCondition.OilBurnPerKm, freshBurn, 9);
        Assert.Equal(freshBurn * CarCondition.WornOilBurn, wornBurn, 9);
    }

    [Fact]
    public void The_report_tells_once_when_the_oil_runs_low_and_again_when_it_is_nearly_dry()
    {
        var car = CarWithEngineAndWheels();
        car.OilLevel = CarCondition.LowOil + 0.001;
        var report = CarCondition.ApplyRace(car, Finished(), 1, null);
        var note = Assert.Single(report, l => l.StartsWith(CarCondition.OilNote, StringComparison.Ordinal));
        Assert.Contains("gas station", note);
        Assert.All(report, l => Assert.False(CarCondition.IsDamage(l)));

        // Already low: nothing more until it is nearly dry
        Assert.DoesNotContain(CarCondition.ApplyRace(car, Finished(), 1, null), l => l.StartsWith(CarCondition.OilNote, StringComparison.Ordinal));

        car.OilLevel = 0.401;
        Assert.Contains(CarCondition.ApplyRace(car, Finished(), 1, null), l => l.Contains("oil light", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rivals_race_and_a_car_without_an_engine_burn_no_oil()
    {
        var rival = CarWithEngineAndWheels();
        CarCondition.ApplyRace(rival, Finished(), 20, null, keepsFuelAndDirt: false);
        Assert.Null(rival.OilLevel);

        var shell = new Car("car_a");
        CarCondition.ApplyRace(shell, Finished(), 20, null);
        Assert.Null(shell.OilLevel);
    }

    [Fact]
    public void A_low_sump_surges_sooner()
    {
        var car = new Car("car_a");
        Assert.Equal(1, CarCondition.SumpShare(car));
        car.OilLevel = 0.5;
        Assert.Equal(CarCondition.DrySumpShare + (1 - CarCondition.DrySumpShare) * 0.5, CarCondition.SumpShare(car), 9);
        car.OilLevel = double.NaN;
        Assert.Equal(1, CarCondition.SumpShare(car));
        car.OilLevel = -3;
        Assert.Equal(CarCondition.DrySumpShare, CarCondition.SumpShare(car), 9);
    }

    // ---- The station's jobs ----

    [Fact]
    public void The_station_tops_up_the_oil_by_the_quart_and_charges_at_least_one()
    {
        var car = CarWithEngineAndWheels();
        Assert.DoesNotContain(GasStation.Jobs(car, null, StationPrices.Default), j => j.Name == GasStation.TopUpOil);

        car.OilLevel = 0.9;
        var oil = Assert.Single(GasStation.Jobs(car, null, StationPrices.Default), j => j.Name == GasStation.TopUpOil);
        Assert.Equal("A quart low", oil.Detail);
        Assert.True(oil.IsChore);
        Assert.Equal(PartPricing.Round((double)StationPrices.Default.OilPerLitre * 0.946), oil.Cost);

        car.OilLevel = 0.2;
        oil = Assert.Single(GasStation.Jobs(car, null, StationPrices.Default), j => j.Name == GasStation.TopUpOil);
        Assert.StartsWith("Way down: four quarts low", oil.Detail);
        oil.Apply();
        Assert.Null(car.OilLevel);
        Assert.Equal(1, CarCondition.OilLeft(car));
    }

    [Fact]
    public void The_prices_are_the_stations_and_the_difficultys()
    {
        var car = new Car("car_a") { FuelLitres = 10, FuelTankLitres = 60, BodyDirt = 0.6 };
        var dear = new StationPrices(0.2m, 1m, 10m, 50m);
        var jobs = GasStation.Jobs(car, null, dear, priceMultiplier: 1.5);
        Assert.Equal(PartPricing.Round(0.2 * 50 * 1.5), jobs.Single(j => j.Name == GasStation.FillUp).Cost);
        Assert.Equal(PartPricing.Round(10 * 1.5), jobs.Single(j => j.Name == GasStation.Wash).Cost);
        Assert.All(jobs, j => Assert.Equal(GameAction.GarageChore, j.Time));
    }

    [Fact]
    public void A_car_without_the_gas_to_get_there_is_towed_and_the_drive_takes_a_little_from_the_tank()
    {
        var car = new Car("car_a") { FuelLitres = 10, FuelTankLitres = 60 };
        Assert.False(GasStation.NeedsTow(car));
        GasStation.Drive(car);
        Assert.Equal(10 - GasStation.TripLitres, car.FuelLitres!.Value, 9);

        car.FuelLitres = GasStation.TripLitres - 0.01;
        Assert.True(GasStation.NeedsTow(car));

        // A full tank, or one whose size is not known, is never short
        Assert.False(GasStation.NeedsTow(new Car("car_b")));
        Assert.False(GasStation.NeedsTow(new Car("car_c") { FuelLitres = 0.1 }));
    }

    [Fact]
    public void The_oil_short_reads_in_quarts()
    {
        Assert.Equal("a quart", GasStation.Quarts(0.2));
        Assert.Equal("two quarts", GasStation.Quarts(1.9));
        Assert.Equal("7 quarts", GasStation.Quarts(6.6));
    }

    [Fact]
    public void A_real_engines_sump_is_its_oil_pans_or_a_v8s_of_the_day()
    {
        var catalog = Catalog.Value;
        var car = CarWithRealEngine(catalog);
        var sump = GasStation.SumpLitres(car, catalog);
        Assert.InRange(sump, 1, 20);
        Assert.Equal(GasStation.DefaultSumpLitres, GasStation.SumpLitres(car, null));
    }

    // ---- The attendant ----

    [Fact]
    public void A_car_in_good_shape_gives_the_attendant_nothing_to_say()
    {
        Assert.Empty(AttendantCheck.Look(CarWithEngineAndWheels(), null));
        Assert.Empty(AttendantCheck.Look(CarWithRealEngine(Catalog.Value), Catalog.Value));
    }

    [Fact]
    public void Worn_tyres_brakes_and_shocks_are_named_by_corner_and_axle_worst_first()
    {
        var car = CarWithEngineAndWheels();
        var corners = RunningGear.Mounted(car.Parts);
        corners[0].Tyre!.Wear = 0.2;
        corners[3].Brake!.Wear = 0.05;
        corners[2].Shock!.Wear = 0.25;
        corners[1].Tyre!.Wear = 0.5;

        var hints = AttendantCheck.Look(car, null);
        Assert.Equal(["station.hint.brakes.now", "station.hint.tyre.soon", "station.hint.shocks.soon"], hints.Select(h => h.TextKey));
        Assert.Equal(["rear", "front left", "rear"], hints.Select(h => h.Where));
    }

    [Fact]
    public void The_attendant_mentions_three_things_at_most_and_never_a_blown_tyre()
    {
        var car = CarWithEngineAndWheels();
        foreach (var corner in RunningGear.Mounted(car.Parts))
        {
            corner.Tyre!.Wear = 0.1;
            corner.Brake!.Wear = 0.1;
        }

        Assert.Equal(AttendantCheck.MostHints, AttendantCheck.Look(car, null).Count);

        var flat = CarWithEngineAndWheels();
        var tyre = RunningGear.Mounted(flat.Parts)[0].Tyre!;
        tyre.Wear = 0.05;
        tyre.Tear = 0;
        Assert.Empty(AttendantCheck.Look(flat, null));
    }

    [Fact]
    public void A_worn_radiator_in_a_real_engine_is_seen_and_a_tired_bottom_end_is_heard()
    {
        var catalog = Catalog.Value;
        var car = CarWithRealEngine(catalog);
        var radiator = car.Engine!.SelfAndDescendants().FirstOrDefault(p => catalog.Get(p.DefinitionId) is { } d && EngineCooling.KindOf(d) == EngineCooling.Kind.Radiator);
        Assert.NotNull(radiator);
        radiator.Wear = 0.2;

        var hint = Assert.Single(AttendantCheck.Look(car, catalog));
        Assert.Equal("station.hint.radiator.soon", hint.TextKey);

        foreach (var part in CarCondition.RotatingParts(car, CarCondition.Groups(catalog))) part.Tear = 0.2;
        Assert.Contains(AttendantCheck.Look(car, catalog), h => h.TextKey == "station.hint.engine.now");
    }

    [Fact]
    public void Every_thing_the_attendant_can_notice_has_words_in_the_base_game()
    {
        var text = GameText.Load(ModSet.Installed);
        var keys = new[] { "radiator", "water_pump", "fan", "exhaust", "air_filter", "carburettor", "clutch", "engine", "gearbox", "tyre", "brakes", "shocks" };
        foreach (var key in keys)
        {
            foreach (var how in new[] { "soon", "now" }) Assert.True(text.Has($"station.hint.{key}.{how}"), $"station.hint.{key}.{how}");
        }

        foreach (var key in new[] { "station.greeting", "station.greeting.towed", "station.filled", "station.washed", "station.oil.low", "station.oil.very_low",
                     "station.oil.ok", "station.oil.topped", "station.all_good", "station.goodbye" })
            Assert.True(text.Has(key), key);
    }

    // ---- The station's scene ----

    [Fact]
    public void The_base_games_station_loads_even_without_its_model()
    {
        var station = Assert.Single(StationScenes.LoadAll(ModSet.Installed), s => s.Id == "tomwixon");
        Assert.Equal("Tomwixon Garages", station.Title);
        Assert.True(station.Arrive.Count >= 2);
        Assert.True(station.Leave.Count >= 2);
        Assert.Equal(station.Arrive[^1], station.Leave[0]);
        Assert.Equal(StationPrices.Default, station.Prices);
        Assert.Equal("STATION_SKY_DUSK", StationScene.SkyNode(StreetLight.Dusk));
    }

    [Fact]
    public void A_station_without_a_way_in_is_left_out_and_bad_prices_fall_back()
    {
        using var temp = new TempDir();
        var folder = Path.Combine(temp.Path, "a");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "station.json"), """{ "id": "a", "arrive": [[0, 0]], "leave": [[0, 0], [1, 1]] }""");
        Assert.Null(StationScenes.Load(folder));

        File.WriteAllText(Path.Combine(folder, "station.json"),
            """{ "id": "a", "arrive": [[0, 10], [0, 0]], "leave": [[0, 0], [1, 1]], "prices": { "fuelPerLitre": -1, "wash": 5 } }""");
        var station = StationScenes.Load(folder)!;
        Assert.Equal(StationPrices.Default.FuelPerLitre, station.Prices.FuelPerLitre);
        Assert.Equal(5m, station.Prices.Wash);
        Assert.False(station.HasModel);
    }

    [Fact]
    public void The_ground_is_read_between_its_cells_and_is_flat_where_it_has_none()
    {
        using var temp = new TempDir();
        var file = Path.Combine(temp.Path, "ground.bin");
        using (var writer = new BinaryWriter(File.Create(file)))
        {
            writer.Write("SCG1"u8);
            writer.Write(1f);
            writer.Write(2);
            writer.Write(2);
            writer.Write(0f);
            writer.Write(0f);
            foreach (var h in new[] { 0f, 1f, 2f, float.NaN }) writer.Write(h);
        }

        var ground = StationGround.Load(file);
        Assert.Equal(0.5f, ground.HeightAt(0.5f, 0f), 4);
        Assert.Equal(1f, ground.HeightAt(0f, 0.5f), 4);
        Assert.Equal(0f, ground.HeightAt(50f, 50f));
        Assert.Equal(0f, StationGround.Load(Path.Combine(temp.Path, "none.bin")).HeightAt(1, 1));

        File.WriteAllText(file, "junk");
        Assert.Equal(0f, StationGround.Load(file).HeightAt(0.5f, 0f));
    }

    // ---- The drive ----

    private static readonly GroundPoint[] Arrival = [new(-60, 28), new(-20, 28), new(-8, 22), new(2, 12), new(2, 5)];

    [Fact]
    public void Arriving_the_car_rolls_in_and_stops_at_the_pump_facing_along_the_last_stretch()
    {
        var drive = PathDrive.Arrive(Arrival, 800);
        var start = drive.At(0);
        Assert.True(start.Speed > 5, "it comes in rolling");
        Assert.InRange(start.Heading, 89f, 91f);

        var end = drive.At(drive.Duration + 1);
        Assert.True(end.Done);
        Assert.Equal(0, end.Speed);
        Assert.Null(end.Rpm);
        Assert.Equal(2, end.X, 2);
        Assert.Equal(5, end.Z, 2);
        Assert.InRange(Math.Abs(end.Heading), 178f, 182f);
        Assert.InRange(drive.Duration, 5, 30);
    }

    [Fact]
    public void Leaving_the_car_pulls_away_from_a_standstill_and_is_still_going_at_the_end()
    {
        var drive = PathDrive.Leave([new(0, 0), new(0, 20), new(0, 60)], 800);
        Assert.Equal(0, drive.At(0).Speed);
        Assert.True(drive.At(0.5f).Speed > 0);
        var end = drive.At(drive.Duration);
        Assert.True(end.Done);
        Assert.True(end.Speed > 5);
        Assert.NotNull(drive.At(1).Rpm);
    }

    [Fact]
    public void A_bend_is_taken_slower_than_the_straight_and_the_clock_only_goes_forward()
    {
        var drive = PathDrive.Arrive(Arrival, 800);
        var fastest = 0f;
        var slowestInBend = float.MaxValue;
        var last = drive.At(0);
        for (var t = 0.05f; t < drive.Duration; t += 0.05f)
        {
            var state = drive.At(t);
            Assert.True(state.Distance >= last.Distance - 1e-4f);
            Assert.True(state.Speed <= PathDrive.CruiseSpeed + 1e-3f);
            if (state.X < -30) fastest = Math.Max(fastest, state.Speed);
            if (state.X is > -10 and < 0) slowestInBend = Math.Min(slowestInBend, state.Speed);
            last = state;
        }

        Assert.True(slowestInBend < fastest, $"{slowestInBend} in the bend against {fastest} on the road");
    }

    [Fact]
    public void The_base_games_drives_never_swing_the_car_round_faster_than_a_driver_would()
    {
        var station = StationScenes.LoadAll(ModSet.Installed).Single(s => s.Id == "tomwixon");
        foreach (var drive in new[] { PathDrive.Arrive(station.Arrive, 800), PathDrive.Leave(station.Leave, 800) })
        {
            const float step = 0.05f;
            var worst = 0f;
            var where = drive.At(0);
            for (var t = step; t < drive.Duration; t += step)
            {
                var turn = (drive.At(t).Heading - drive.At(t - step).Heading + 540f) % 360f - 180f;
                if (Math.Abs(turn) / step <= worst) continue;
                worst = Math.Abs(turn) / step;
                where = drive.At(t);
            }

            // A car taking a street corner turns some 30 degrees a second; the spin was hundreds
            Assert.True(worst < 45f, $"{(drive.IsLeaving ? "leaving" : "arriving")}: {worst:0} degrees a second at ({where.X:0.0}, {where.Z:0.0}), {where.Speed:0.0} m/s");
        }
    }

    [Fact]
    public void A_short_stretch_into_a_long_one_does_not_loop()
    {
        // The uniform curve overshot here into a loop: the car spun on the spot
        var drive = PathDrive.Leave([new(40, 11), new(45, 19.5f), new(51, 27), new(60, 28.4f), new(125, 28.4f)], 800);
        var last = drive.At(0);
        for (var t = 0.05f; t <= drive.Duration; t += 0.05f)
        {
            var state = drive.At(t);
            Assert.True(state.X >= last.X - 0.01f, $"went back from x {last.X:0.00} to {state.X:0.00}");
            last = state;
        }
    }

    [Fact]
    public void A_car_sits_on_the_road_and_its_driver_looks_as_its_data_says()
    {
        using var temp = new TempDir();
        var car = Path.Combine(temp.Path, "some_car_nobody_has");
        TestCars.WriteData(car, "[BASIC]\nGRAPHICS_OFFSET=0,-0.165,-0.25\n[GRAPHICS]\nDRIVEREYES=0.37,0.889,-0.178\nON_BOARD_PITCH_ANGLE=-2.7\n");

        var frame = StreetCorsa.Controls.Street.AcCarFrame.Read(car)!;

        // The eyes as the mod gives them, in the model's own space; the centre of gravity rides radius - BASEY over the
        // road, the model's origin the offset under it
        Assert.Equal(0.889f, frame.EyeY, 4);
        Assert.Equal(-0.178f, frame.EyeZ, 4);
        Assert.Equal((0.485f + 0.495f) / 2f - 0.165f, frame.Lift, 4);

        // A car without its data sits as it is modelled
        Assert.Null(StreetCorsa.Controls.Street.AcCarFrame.Read(Path.Combine(temp.Path, "none")));
    }

    // ---- The stage ----

    [Fact]
    public void With_nothing_on_screen_the_car_is_at_the_pump_at_once_and_gone_at_once()
    {
        var stage = new StationStage();
        var events = new List<string>();
        stage.AtPump += () => events.Add("pump");
        stage.Gone += () => events.Add("gone");

        stage.Leave();
        Assert.Empty(events);

        stage.Arrive(new Audio.EngineRunner(), towed: false);
        Assert.Equal(StationPhase.AtPump, stage.Phase);
        stage.Leave();
        Assert.Equal(StationPhase.Gone, stage.Phase);
        Assert.Equal(["pump", "gone"], events);
    }

    [Fact]
    public void A_car_towed_in_is_at_the_pump_even_while_the_station_is_shown()
    {
        var stage = new StationStage { IsShown = true };
        var atPump = 0;
        stage.AtPump += () => atPump++;

        stage.Arrive(new Audio.EngineRunner(), towed: true);
        Assert.Equal(StationPhase.AtPump, stage.Phase);
        Assert.Equal(1, atPump);
        Assert.True(stage.Towed);

        // Shown and driven: the viewport says when it is there
        var driven = new StationStage { IsShown = true };
        driven.Arrive(new Audio.EngineRunner(), towed: false);
        Assert.Equal(StationPhase.Arriving, driven.Phase);
    }

    [Fact]
    public void A_car_that_could_not_get_home_is_back_at_the_pump_and_can_leave_again()
    {
        var stage = new StationStage();
        var gone = 0;
        stage.Gone += () => gone++;
        stage.Arrive(new Audio.EngineRunner(), towed: false);
        stage.Leave();
        Assert.Equal(StationPhase.Gone, stage.Phase);

        stage.BackToPump();
        Assert.Equal(StationPhase.AtPump, stage.Phase);
        stage.Leave();
        Assert.Equal(2, gone);
    }

    [Fact]
    public void The_fuel_gauge_hears_every_change_of_the_tank()
    {
        var stage = new StationStage();
        var heard = 0;
        stage.FuelChanged += () => heard++;
        stage.FuelLitres = 10;
        stage.FuelLitres = 10;
        stage.FuelLitres = 12.5f;
        Assert.Equal(2, heard);
    }

    // ---- Review fixes ----

    [Fact]
    public void A_car_without_driver_eyes_still_sits_on_the_road()
    {
        using var temp = new TempDir();
        var car = Path.Combine(temp.Path, "some_car_without_eyes");
        TestCars.WriteData(car, "[BASIC]\nGRAPHICS_OFFSET=0,-0.165,-0.25\n[GRAPHICS]\nDRIVEREYES=0,0,0\n");

        var frame = StreetCorsa.Controls.Street.AcCarFrame.Read(car)!;
        Assert.Equal((0.485f + 0.495f) / 2f - 0.165f, frame.Lift, 4);
        Assert.Equal(1.05f, frame.EyeY, 4);
    }

    [Fact]
    public void A_way_whose_points_are_all_on_one_spot_is_no_way_and_does_not_crash_the_drive()
    {
        using var temp = new TempDir();
        var folder = Path.Combine(temp.Path, "nowhere");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "station.json"), """{ "id": "nowhere", "arrive": [[0,0],[0,0]], "leave": [[0,0],[5,0]] }""");
        Assert.Null(StationScenes.Load(folder));

        var drive = PathDrive.Arrive([new(3, 3), new(3, 3)], 800);
        Assert.True(drive.At(drive.Duration).Done);
    }

    [Fact]
    public void A_car_too_dry_to_reach_the_gas_station_gets_a_can_of_gas_at_the_garage()
    {
        var car = new Car("car_a") { FuelLitres = 0.1, FuelTankLitres = 60 };
        var can = Assert.Single(RepairShop.Jobs(car, null), j => j.Name == RepairShop.CanOfGas);
        Assert.True(can.IsChore);
        Assert.True(can.Cost >= 1);

        can.Apply();
        Assert.False(GasStation.NeedsTow(car));
        Assert.Equal(0.1 + RepairShop.CanOfGasLitres, car.FuelLitres!.Value, 6);
        Assert.DoesNotContain(RepairShop.Jobs(car, null), j => j.Name == RepairShop.CanOfGas);
    }
}
