using System.Globalization;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Configuration;
using StreetCorsa.Services.Configuration.Models;
using StreetCorsa.Services.Time;

namespace StreetCorsa.Tests;

/// <summary>
/// Step 14, the deeper simulation: each car's cooling and oil rated from its parts for the race mode's heat, and the
/// fuel and dirt a race leaves carried to the next, with the garage to fill it up and wash it.
/// </summary>
public sealed class DeeperSimulationTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ---- The cooling, on the real catalog ----

    private static readonly Lazy<(PartsCatalog Catalog, EngineBuildIndex Builds, EngineCooling Cooling)> Real = new(() =>
    {
        var catalog = PartsCatalog.Load(RepoPaths.PartsFolder);
        var builds = EngineBuildIndex.Create(catalog);
        return (catalog, builds, EngineCooling.Create(catalog, builds));
    });

    private const string Charger440 = "cars/Charger69_RT/0006/stock";

    private static (List<PartInstance> Mounted, RatedBuild Build) Stock(string buildId)
    {
        var (catalog, builds, _) = Real.Value;
        var build = builds.Get(buildId)!;
        var engine = EngineFactory.CreateStock(catalog, build, 1.0, new Random(1))!;
        return (engine.Root.SelfAndDescendants().ToList(), build);
    }

    private static FactoryCooling Factory(RatedBuild build) => Real.Value.Cooling.Factory(build);

    [Fact]
    public void Every_factory_car_is_cooled_for_what_its_engine_makes()
    {
        var (catalog, builds, cooling) = Real.Value;
        var factory = builds.Runnable.Where(b => b.Build.Origin == EngineBuild.OriginCar).ToList();
        Assert.NotEmpty(factory);

        foreach (var build in factory)
        {
            var engine = EngineFactory.CreateStock(catalog, build, 1.0, new Random(1));
            if (engine == null) continue;
            var rating = cooling.Rate(engine.Root.SelfAndDescendants(), build.PowerHp, Factory(build));
            Assert.True(rating.Cooling >= 0.999, $"{build.Build.Id}: {rating.Cooling}");
            Assert.InRange(rating.OilG, EngineCooling.StockSumpG, EngineCooling.BigSumpG);
        }
    }

    [Fact]
    public void Tuned_past_the_factory_an_engine_runs_short_and_a_racing_radiator_and_pump_win_it_back()
    {
        var (mounted, build) = Stock(Charger440);
        var cooling = Real.Value.Cooling;
        var power = build.PowerHp * 1.4;

        var stock = cooling.Rate(mounted, power, Factory(build));
        Assert.Equal(1 / 1.4, stock.Cooling, 2);

        mounted.Single(p => p.DefinitionId == "engines/chrysler/Radiator_big").DefinitionId = "engines/chrysler/Radiator_big_3";
        var racing = cooling.Rate(mounted, power, Factory(build));
        Assert.Equal(EngineCooling.PerformanceRadiator / 1.4, racing.Cooling, 2);

        mounted.Single(p => p.DefinitionId == "engines/chrysler/Water_pomp_big_O").DefinitionId = "engines/chrysler/Water_pomp_Weiand";
        Assert.Equal(EngineCooling.PerformanceRadiator * EngineCooling.PerformancePump / 1.4, cooling.Rate(mounted, power, Factory(build)).Cooling, 2);
    }

    [Fact]
    public void An_engine_whose_factory_had_a_radiator_has_next_to_no_cooling_without_it()
    {
        var (mounted, build) = Stock(Charger440);
        mounted.RemoveAll(p => p.DefinitionId == "engines/chrysler/Radiator_big");

        Assert.Equal(EngineCooling.NoRadiator, Real.Value.Cooling.Rate(mounted, build.PowerHp, Factory(build)).Cooling);
        // A make without radiators to speak of keeps its factory's cooling
        Assert.Equal(1, Real.Value.Cooling.Rate(mounted, build.PowerHp, Factory(build) with { Radiator = false }).Cooling, 3);
    }

    [Fact]
    public void A_swapped_engine_brings_the_cooling_its_block_left_the_factory_with()
    {
        var (mounted, build) = Stock(Charger440);
        var blockHp = Real.Value.Cooling.BlockFactoryHp("engines/chrysler/_Engine_block_440")!.Value;

        // In a car whose own engine made 150 hp, the 440 is still cooled for what a factory 440 made
        var small = Factory(build) with { PowerHp = 150, BlockId = "engines/chrysler/_Engine_block_318" };
        Assert.Equal(1, Real.Value.Cooling.Rate(mounted, blockHp, small).Cooling, 3);
        Assert.True(blockHp <= build.PowerHp);

        // ...and in a car whose own engine made far more, no better than that: the radiator came with the swap
        var big = Factory(build) with { PowerHp = blockHp * 2, BlockId = "engines/chrysler/_Engine_block_426_HEMI" };
        Assert.Equal(1, Real.Value.Cooling.Rate(mounted, blockHp, big).Cooling, 3);
        Assert.Equal(0.5, Real.Value.Cooling.Rate(mounted, blockHp * 2, big).Cooling, 3);

        // The car's own block keeps the car's factory power
        Assert.Equal(1, Real.Value.Cooling.Rate(mounted, build.PowerHp, Factory(build)).Cooling, 3);
    }

    [Fact]
    public void An_engine_that_left_the_factory_with_a_water_pump_boils_without_it_and_mounting_hardware_is_not_a_pump()
    {
        var (mounted, build) = Stock(Charger440);
        Assert.True(Factory(build).WaterPump);
        mounted.RemoveAll(p => EngineCooling.KindOf(Real.Value.Catalog.Get(p.DefinitionId)!) == EngineCooling.Kind.WaterPump);

        Assert.Equal(EngineCooling.NoPump, Real.Value.Cooling.Rate(mounted, build.PowerHp, Factory(build)).Cooling, 3);
        Assert.Equal(EngineCooling.Kind.Other, EngineCooling.KindOf(Real.Value.Catalog.Get("engines/chrysler/Water_pomp_Mounting_hardwarer")!));
    }

    [Fact]
    public void Fitting_a_radiator_to_an_engine_that_left_the_factory_without_a_fan_part_keeps_its_fan()
    {
        var (mounted, build) = Stock(Charger440);
        mounted.RemoveAll(p => EngineCooling.KindOf(Real.Value.Catalog.Get(p.DefinitionId)!) == EngineCooling.Kind.Fan);
        var noFanPart = Factory(build) with { Fan = false };

        // The fan is on the pump pulley: a radiator does not take it away
        Assert.Equal(EngineCooling.StockFan, Real.Value.Cooling.Rate(mounted, build.PowerHp, noFanPart).Fan);
        mounted.RemoveAll(p => EngineCooling.KindOf(Real.Value.Catalog.Get(p.DefinitionId)!) == EngineCooling.Kind.Radiator);
        Assert.Equal(EngineCooling.StockFan, Real.Value.Cooling.Rate(mounted, build.PowerHp, noFanPart with { Radiator = false }).Fan);
    }

    [Fact]
    public void A_tuner_fits_a_better_radiator_once_the_power_outgrows_it()
    {
        var (catalog, builds, cooling) = Real.Value;
        var (_, build) = Stock(Charger440);
        var engine = EngineFactory.CreateStock(catalog, build, 1.0, new Random(1))!.Root;
        var factory = Factory(build);

        // As if the engine made 1.2 times what its radiator was sized for
        double CoolingOf(PartInstance e, double hp) => cooling.Rate(e.SelfAndDescendants(), hp * 1.2, factory).Cooling;
        Assert.True(CoolingOf(engine, build.PowerHp) < EngineTuner.CoolEnough);

        var result = EngineTuner.TuneUp(catalog, builds, engine, 1_000_000m, 1.0, new Random(7), maxUpgrades: 1, coolingOf: CoolingOf);
        Assert.NotNull(result);
        var fitted = Assert.Single(result!.Upgrades, u => u.Group == EngineTuner.CoolingGroup);
        Assert.True(CoolingOf(result.Engine, result.Report.Dyno!.MaxPowerHp) >= EngineTuner.RunsHotBelow, fitted.Change);
        Assert.Contains(result.Engine.SelfAndDescendants(), p => catalog.Get(p.DefinitionId) is { } d
            && EngineCooling.KindOf(d) == EngineCooling.Kind.Radiator && EngineCooling.IsPerformanceRadiator(d));
    }

    [Fact]
    public void A_tuner_makes_no_upgrade_it_cannot_keep_cool()
    {
        var (catalog, builds, _) = Real.Value;
        var (_, build) = Stock(Charger440);
        var engine = EngineFactory.CreateStock(catalog, build, 1.0, new Random(1))!.Root;

        // Nothing cools it: any power past the factory's runs it hot
        double CoolingOf(PartInstance e, double hp) => build.PowerHp * 1.01 / hp;
        var result = EngineTuner.TuneUp(catalog, builds, engine, 1_000_000m, 1.0, new Random(7), maxUpgrades: 2, coolingOf: CoolingOf);
        Assert.True(result == null || result.Report.Dyno!.MaxPowerHp <= build.PowerHp * 1.01 / EngineTuner.RunsHotBelow);
    }

    [Theory]
    [InlineData("engines/chrysler/Radiator_big", EngineCooling.Kind.Radiator)]
    [InlineData("engines/chrysler/Radiator_big_Paxton_Kit", EngineCooling.Kind.Radiator)]
    [InlineData("engines/stock/EnginePart_0018", EngineCooling.Kind.Radiator)]
    [InlineData("engines/chrysler/Water_pomp_small_B", EngineCooling.Kind.WaterPump)]
    [InlineData("engines/gm/Summit_sb_pump", EngineCooling.Kind.WaterPump)]
    [InlineData("engines/chrysler/Fan_HEMI", EngineCooling.Kind.Fan)]
    [InlineData("engines/ford/Flex_a_lite_fan_1", EngineCooling.Kind.Fan)]
    [InlineData("engines/chrysler/Belt_Steering_pump", EngineCooling.Kind.Other)]
    [InlineData("engines/ford_six/ford_stock_fuel_pump", EngineCooling.Kind.Other)]
    [InlineData("engines/gm/MF_283_T_intercooler", EngineCooling.Kind.Other)]
    public void Cooling_parts_are_known_by_their_names(string id, EngineCooling.Kind kind)
    {
        var part = Real.Value.Catalog.Get(id);
        Assert.NotNull(part);
        Assert.Equal(kind, EngineCooling.KindOf(part!));
    }

    [Fact]
    public void A_radiator_with_its_own_electric_fan_keeps_up_best_standing_still()
    {
        var (mounted, build) = Stock(Charger440);
        Assert.Equal(EngineCooling.StockFan, Real.Value.Cooling.Rate(mounted, build.PowerHp, Factory(build)).Fan);

        mounted.Single(p => p.DefinitionId == "engines/chrysler/Radiator_big").DefinitionId = "engines/chrysler/Radiator_big_Paxton_Kit";
        Assert.Equal(EngineCooling.ElectricFan, Real.Value.Cooling.Rate(mounted, build.PowerHp, Factory(build)).Fan);

        mounted.RemoveAll(p => EngineCooling.KindOf(Real.Value.Catalog.Get(p.DefinitionId)!) is EngineCooling.Kind.Fan or EngineCooling.Kind.Radiator);
        Assert.Equal(EngineCooling.NoFan, Real.Value.Cooling.Rate(mounted, build.PowerHp, Factory(build)).Fan);
    }

    [Theory]
    [InlineData("engines/chrysler/Oil_pan_big_block_B", EngineCooling.StockSumpG)]
    [InlineData("engines/gm/GM_small_block_oil_pan", EngineCooling.StockSumpG)]
    [InlineData("engines/chrysler/Oil_pan_small_block_Milodon_deep", EngineCooling.RaceSumpG)]
    [InlineData("engines/gm/Canton_427_oil_pan", EngineCooling.BigSumpG)]
    [InlineData("engines/gm/Milodon_427_oil_pan", EngineCooling.BigSumpG)]
    public void A_deep_or_race_oil_pan_holds_its_oil_to_more_g(string id, double g)
    {
        Assert.Equal(g, EngineCooling.SumpG(Real.Value.Catalog.Get(id)));
    }

    [Fact]
    public void No_oil_pan_holds_no_oil()
    {
        Assert.Equal(EngineCooling.NoSumpG, EngineCooling.SumpG(null));
    }

    // ---- Into the race ----

    [Fact]
    public void The_start_carries_the_fuel_the_dirt_and_the_cooling_in_AC_number_format_whatever_the_culture()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var keys = new RaceStartState { FuelLitres = 12.345, Dirt = 0.4, Cooling = new EngineCoolingRating(0.7, 0.35, 0.9) }
                .IniKeys(0).ToDictionary(k => k.Key, k => k.Value);
            Assert.Equal("12.35", keys["CAR_0_FUEL"]);
            Assert.Equal("0.400", keys["CAR_0_DIRT"]);
            Assert.Equal("0.700,0.350,0.900", keys["CAR_0_COOLING"]);

            // A full tank is filled by the mode to what the car holds; a car not rated is left a factory car
            var full = new RaceStartState().IniKeys(1).ToDictionary(k => k.Key, k => k.Value);
            Assert.Equal("-1", full["CAR_1_FUEL"]);
            Assert.Equal("0.000", full["CAR_1_DIRT"]);
            Assert.False(full.ContainsKey("CAR_1_COOLING"));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_car_goes_in_with_what_is_left_in_its_tank_and_its_dirt()
    {
        var car = new Car("car_a") { FuelLitres = 8.5, FuelTankLitres = 60, BodyDirt = 0.3 };
        var start = CarCondition.StartState(car, null);

        Assert.Equal(8.5, start.FuelLitres);
        Assert.Equal(0.3, start.Dirt);
        Assert.Null(CarCondition.StartState(new Car("car_b"), null).FuelLitres);
        // A tank of no known size is taken as full
        Assert.Null(CarCondition.StartState(new Car("car_c") { FuelLitres = 3 }, null).FuelLitres);
    }

    [Fact]
    public void Race_ini_carries_the_fuel_dirt_and_cooling_of_both_cars()
    {
        var cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(cfg);
        var service = new IniModificationService(cfg, _temp.Combine("AcRestore"));

        Assert.True(service.ApplyIntent(new DragRaceIntent
        {
            PlayerCarId = "a", OpponentCarId = "b", PlayerName = "P", OpponentName = "O",
            PlayerStart = new RaceStartState { FuelLitres = 20, Dirt = 0.25, Cooling = new EngineCoolingRating(0.8, 0.5, 1.1) },
            OpponentStart = new RaceStartState()
        }));

        var section = File.ReadAllLines(Path.Combine(cfg, "race.ini")).SkipWhile(l => l != "[STREET_CORSA]").ToList();
        Assert.Contains("CAR_0_FUEL=20.00", section);
        Assert.Contains("CAR_0_DIRT=0.250", section);
        Assert.Contains("CAR_0_COOLING=0.800,0.500,1.100", section);
        Assert.Contains("CAR_1_FUEL=-1", section);
    }

    // ---- After the race ----

    private static RaceCarCondition Report(double? fuel = null, double? tank = null, double? dirt = null, RaceHeatReport? heat = null) =>
        new() { FuelLitres = fuel, MaxFuelLitres = tank, Dirt = dirt, Heat = heat };

    [Fact]
    public void The_fuel_left_stays_in_the_tank_and_a_low_tank_is_told()
    {
        var car = new Car("car_a");
        var report = CarCondition.ApplyRace(car, Report(fuel: 40, tank: 60), 1, null);
        Assert.Equal(40, car.FuelLitres);
        Assert.Equal(60, car.FuelTankLitres);
        Assert.DoesNotContain(report, l => l.StartsWith("Fuel"));

        report = CarCondition.ApplyRace(car, Report(fuel: 5, tank: 60), 1, null);
        Assert.Contains(report, l => l.StartsWith("Fuel: 5 litres left"));
        Assert.Empty(CarCondition.WhyCannotRace(car, null));
    }

    [Fact]
    public void A_dry_tank_keeps_the_car_home_until_it_is_filled_up()
    {
        var car = new Car("car_a");
        var report = CarCondition.ApplyRace(car, Report(fuel: 0.01, tank: 60), 3, null);

        Assert.Contains(report, l => l.Contains("the tank is dry", StringComparison.Ordinal));
        Assert.Contains("the tank is empty", CarCondition.WhyCannotRace(car, null));

        var fill = Assert.Single(GasStation.Jobs(car, null, StationPrices.Default), j => j.Id == GasStation.FillUp);
        Assert.Equal(GameAction.GarageChore, fill.Time);
        Assert.Equal(PartPricing.Round((double)StationPrices.Default.FuelPerLitre * 59.99), fill.Cost);
        fill.Apply();
        Assert.Null(car.FuelLitres);
        Assert.Empty(CarCondition.WhyCannotRace(car, null));
        Assert.DoesNotContain(GasStation.Jobs(car, null, StationPrices.Default), j => j.Id == GasStation.FillUp);
    }

    [Fact]
    public void Fuel_is_not_kept_without_the_tank_size_so_a_car_is_never_stuck_dry_with_nothing_to_fill()
    {
        var car = new Car("car_a");
        var report = CarCondition.ApplyRace(car, Report(fuel: 0.1), 1, null);

        Assert.Null(car.FuelLitres);
        Assert.DoesNotContain(report, l => l.StartsWith(CarCondition.FuelNote, StringComparison.Ordinal));
        Assert.Empty(CarCondition.WhyCannotRace(car, null));

        // A save from before: fuel without a tank counts as full
        var old = new Car("car_b") { FuelLitres = 0.1 };
        Assert.Empty(CarCondition.WhyCannotRace(old, null));
        Assert.Null(CarCondition.FuelLeft(old));
    }

    [Fact]
    public void Too_little_fuel_to_race_on_keeps_the_car_home_and_the_report_says_so()
    {
        var car = new Car("car_a");
        var report = CarCondition.ApplyRace(car, Report(fuel: 1.4, tank: 60), 1, null);

        Assert.Contains(report, l => l.Contains("too little to race on", StringComparison.Ordinal));
        Assert.Contains("there's hardly any fuel in the tank", CarCondition.WhyCannotRace(car, null));
        Assert.All(report, l => Assert.False(CarCondition.IsDamage(l)));
    }

    [Fact]
    public void A_rivals_race_leaves_the_tank_and_the_dirt_alone()
    {
        var car = new Car("car_a");
        CarCondition.ApplyRace(car, Report(fuel: 0.1, tank: 60, dirt: 0.8), 5, null, keepsFuelAndDirt: false);

        Assert.Null(car.FuelLitres);
        Assert.Equal(0, car.BodyDirt);
        Assert.Empty(CarCondition.WhyCannotRace(car, null));
    }

    [Fact]
    public void Filling_up_and_washing_are_the_gas_stations_not_the_garages()
    {
        var car = new Car("car_a") { FuelLitres = 10, FuelTankLitres = 60, BodyDirt = 0.5 };
        Assert.Empty(RepairShop.Jobs(car, null));
        Assert.All(GasStation.Jobs(car, null, StationPrices.Default), j => Assert.True(j.IsChore, j.Name));
        Assert.Equal([GasStation.FillUp, GasStation.Wash], GasStation.Jobs(car, null, StationPrices.Default).Select(j => j.Id));

        car.BodyDamageKmh = [20, 0, 0, 0];
        Assert.Contains(RepairShop.Jobs(car, null), j => !j.IsChore);
    }

    [Fact]
    public void A_fuel_reading_beyond_the_tank_is_the_tank()
    {
        var car = new Car("car_a");
        CarCondition.ApplyRace(car, Report(fuel: 900, tank: 60), 1, null);
        Assert.Equal(60, car.FuelLitres);
        Assert.Equal(0, CarCondition.LitresToFill(car));
    }

    [Fact]
    public void Dirt_builds_up_and_a_wash_takes_it_off()
    {
        var car = new Car("car_a") { BodyDirt = 0.2 };

        // AC reports the whole of it: it started as dirty as the car went in
        CarCondition.ApplyRace(car, Report(dirt: 0.3), 5, null);
        Assert.Equal(0.3 + 5 * CarCondition.DirtPerKm, car.BodyDirt, 6);

        // An older file without dirt: the road's still counts, and a reading of less than the car had is not a wash
        CarCondition.ApplyRace(car, Report(dirt: 0.0), 10, null);
        Assert.Equal(0.35 + 10 * CarCondition.DirtPerKm, car.BodyDirt, 6);

        var wash = Assert.Single(GasStation.Jobs(car, null, StationPrices.Default), j => j.Id == GasStation.Wash);
        Assert.Equal(GameAction.GarageChore, wash.Time);
        wash.Apply();
        Assert.Equal(0, car.BodyDirt);
        Assert.DoesNotContain(GasStation.Jobs(car, null, StationPrices.Default), j => j.Id == GasStation.Wash);
    }

    [Fact]
    public void A_clean_car_with_a_full_tank_needs_nothing()
    {
        Assert.Empty(RepairShop.Jobs(new Car("car_a"), null));
    }

    [Fact]
    public void The_report_says_what_the_heat_and_the_oil_did_and_what_would_help()
    {
        var cooked = CarCondition.ApplyRace(new Car("a"), Report(heat: new RaceHeatReport { PeakWaterC = 124, HeatLifeLost = 220 }), 5, null);
        Assert.Contains(cooked, l => l.Contains("overheated (124 °C)", StringComparison.Ordinal) && l.Contains("bigger radiator", StringComparison.Ordinal));

        var hot = CarCondition.ApplyRace(new Car("a"), Report(heat: new RaceHeatReport { PeakWaterC = 114, HeatLifeLost = 0 }), 5, null);
        Assert.Contains(hot, l => l.Contains("ran hot (114 °C)", StringComparison.Ordinal));

        var oil = CarCondition.ApplyRace(new Car("a"), Report(heat: new RaceHeatReport { PeakWaterC = 95, OilLifeLost = 40 }), 5, null);
        Assert.Contains(oil, l => l.Contains("deeper oil pan", StringComparison.Ordinal));
        Assert.DoesNotContain(oil, l => l.Contains("radiator", StringComparison.Ordinal));

        var cool = CarCondition.ApplyRace(new Car("a"), Report(heat: new RaceHeatReport { PeakWaterC = 97, OilLifeLost = 1 }), 5, null);
        Assert.DoesNotContain(cool, l => l.StartsWith("Engine", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Breakdowns.Overheat, "engine boiled over")]
    [InlineData(Breakdowns.Oil, "engine lost its oil pressure")]
    [InlineData(Breakdowns.Fuel, "car ran out of gas")]
    [InlineData(Breakdowns.Gearbox, "gearbox gave out")]
    [InlineData(null, "car gave out")]
    public void A_breakdown_is_told_for_what_it_was(string? breakdown, string told)
    {
        Assert.Equal(told, Breakdowns.WhatHappened(breakdown));
    }
}
