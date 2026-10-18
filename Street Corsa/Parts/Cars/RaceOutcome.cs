using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// What a race did to a car, put onto the car and its parts from what Assetto Corsa reported at the end of it
/// (<see cref="Apply"/>): an over-revved or crashed engine on its rotating parts, the gearbox's damage on the
/// transmission, a bent corner on its spring and shock, the tread on the tyres, the body's hits on the car, the fuel,
/// the oil and the dirt as the race left them. What the car is in before and after is <see cref="CarCondition"/>'s to
/// say; this only changes it. <c>Tear</c> is damage and only a repair takes it off; <c>Wear</c> is mileage.
/// </summary>
public static class RaceOutcome
{
    /// <summary>Engine life, as AC counts it, that the heat or the oil must take for the report to say so</summary>
    private const double TellLifeLost = 5;

    /// <summary>The water got this hot, and the engine went flat at the top</summary>
    private const double TellHotC = 110;

    /// <summary>How fast the rear axle's parts wear against the engine's, per km</summary>
    private const double AxleMileageShare = 0.5;

    /// <summary>
    /// Puts what AC reported at the end of a race onto the car and its parts, and brings the car's own figures up to
    /// date. Returns what happened to the car, a line each, for the player.
    /// </summary>
    /// <param name="groupOf">Null when there is no parts catalog: the car's own figures take it all</param>
    /// <param name="wearMultiplier">The save's <see cref="GameRules.CarWearMultiplier"/>, on the mileage wear. AC's own
    /// damage is already the difficulty's (<see cref="GameRules.RaceDamagePercent"/>)</param>
    /// <param name="keepsFuelAndDirt">False for a rival's car: the racers fill up, top up the oil and wash before every
    /// race, so the tank, the oil and the dirt a race leaves are not kept</param>
    /// <param name="catalog">The parts, to tell which of the rear axle's let go when it broke; null leaves the axle as it was</param>
    public static ConditionReport Apply(Car car, RaceCarCondition condition, double distanceKm, Func<string, string?>? groupOf,
        double wearMultiplier = 1.0, bool keepsFuelAndDirt = true, PartsCatalog? catalog = null)
    {
        var report = new ConditionReport();
        ApplyBody(car, condition, report);
        ApplyHeat(condition, report);

        if (groupOf != null && car.HasPartsAssigned)
        {
            ApplyEngine(car, condition, groupOf, report);
            ApplyGearbox(car, condition, groupOf, report);
            ApplyMileage(car, distanceKm * GameRules.Sane(wearMultiplier));
        }
        else
        {
            ApplyToFigures(car, condition, report);
        }

        if (groupOf != null && car.HasRunningGearAssigned) ApplyRunningGear(car, condition, report);
        if (catalog != null && car.HasAxleAssigned) ApplyAxle(car, condition, distanceKm * GameRules.Sane(wearMultiplier), catalog, report);

        if (groupOf != null) CarCondition.RefreshFigures(car, groupOf);
        else car.BodyCondition = CarCondition.BodyCondition(car);

        if (keepsFuelAndDirt)
        {
            ApplyFuelAndDirt(car, condition, distanceKm, report);
            ApplyOil(car, distanceKm, groupOf, report);
        }

        return report;
    }

    /// <summary>The oil the race burned; the report tells when it runs low, once</summary>
    private static void ApplyOil(Car car, double distanceKm, Func<string, string?>? groupOf, ConditionReport report)
    {
        if (car.Engine == null) return;

        var before = CarCondition.OilLeft(car);
        var left = Math.Max(0, before - CarCondition.OilBurned(car, distanceKm, groupOf));
        car.OilLevel = left;
        if (left < 0.4 && before >= 0.4) report.Note(ReportKind.Oil, CarCondition.OilNote + PartWords.Say("condition.oil.light"));
        else if (left < CarCondition.LowOil && before >= CarCondition.LowOil) report.Note(ReportKind.Oil, CarCondition.OilNote + PartWords.Say("condition.oil.burning"));
    }

    /// <summary>What the engine's heat and oil did, and what would help; the life they took is in the engine's</summary>
    private static void ApplyHeat(RaceCarCondition condition, ConditionReport report)
    {
        if (condition.Heat is not { } heat) return;

        var cooked = Clean(heat.HeatLifeLost ?? 0);
        var peak = heat.PeakWaterC is { } p && double.IsFinite(p) ? p : 0;
        if (cooked >= TellLifeLost)
            report.Add(PartWords.Say("condition.engine.overheated", ("peak", peak)));
        else if (peak >= TellHotC)
            report.Add(PartWords.Say("condition.engine.hot", ("peak", peak)));

        if (Clean(heat.OilLifeLost ?? 0) >= TellLifeLost)
            report.Add(PartWords.Say("condition.engine.oil_surged"));
    }

    /// <summary>
    /// The fuel left and the tank's size as AC reported them; dirt from the race and the road. Fuel is kept only
    /// against a known tank: without its size a car could be left too dry to race with nothing to fill it up to.
    /// </summary>
    private static void ApplyFuelAndDirt(Car car, RaceCarCondition condition, double distanceKm, ConditionReport report)
    {
        if (condition.MaxFuelLitres is { } tank && double.IsFinite(tank) && tank > 0) car.FuelTankLitres = tank;
        if (condition.FuelLitres is { } fuel && double.IsFinite(fuel) && CarCondition.TankLitres(car) is { } size)
        {
            var left = Math.Clamp(fuel, 0, size);
            car.FuelLitres = left;
            if (left < CarCondition.EmptyTankLitres) report.Note(ReportKind.Fuel, CarCondition.FuelNote + PartWords.Say("condition.fuel.dry"));
            else if (left < CarCondition.TooLittleToRaceLitres) report.Note(ReportKind.Fuel, CarCondition.FuelNote + PartWords.Say("condition.fuel.too_little", ("litres", left)));
            else if (left < size * CarCondition.LowTankShare) report.Note(ReportKind.Fuel, CarCondition.FuelNote + PartWords.Say("condition.fuel.low", ("litres", left)));
        }

        // AC starts the body as dirty as the car went in: what it reports is the whole of it
        var dirt = Unit.Clamp01(car.BodyDirt, ifNotFinite: 0);
        if (condition.Dirt is { } reported && double.IsFinite(reported)) dirt = Math.Max(dirt, Unit.Clamp01(reported, ifNotFinite: 0));
        car.BodyDirt = Math.Min(1, dirt + Clean(distanceKm) * CarCondition.DirtPerKm);
    }

    private static void ApplyBody(Car car, RaceCarCondition condition, ConditionReport report)
    {
        if (condition.BodyDamageKmh is not { } reported) return;

        // AC starts the race with the body as the car had it: what it reports is the whole of it. A start that did not
        // take (no physics API) reports less than the car had, so the worse of the two stands.
        var zones = CarCondition.Body(car);
        var frontBefore = zones[0];
        var wasTotaled = zones.Sum() >= CarCondition.TotaledKmh;
        for (var i = 0; i < CarCondition.Zones && i < reported.Count; i++)
        {
            var now = Math.Min(Clean(reported[i]), CarCondition.MaxZoneKmh);
            if (now > zones[i] + 0.5) report.Add(PartWords.Say("condition.body.hit", ("zone", CarCondition.ZoneName(i)), ("kmh", now - zones[i])));
            zones[i] = Math.Max(zones[i], now);
        }

        car.BodyDamageKmh = zones;
        if (!wasTotaled && zones.Sum() >= CarCondition.TotaledKmh) report.Add(PartWords.Say("condition.body.totaled"));
        if (CarLooks.CrackFromHit(car, frontBefore)) report.Add(PartWords.Say("condition.windscreen.cracked"));
    }

    private static void ApplyEngine(Car car, RaceCarCondition condition, Func<string, string?> groupOf, ConditionReport report)
    {
        if (condition.EngineLife is not { } life || !double.IsFinite(life)) return;

        // AC reports the life left, having started from the car's (its weakest rotating part's). The weakest part is
        // the one that gave: it takes the whole of the loss, down to what AC left, and the rest of the rotating
        // assembly a share of it. Life the heat or the oil took (the race mode's, at least half the loss) is no one
        // part's: the whole assembly comes down to what is left.
        // AC was given the life to a tenth (RaceStartState) and reports it to a tenth: an engine it left alone comes
        // back as it went in, and any loss below that counts
        var newLife = CarCondition.NewEngineLife;
        var left = Clamp01(life / newLife);
        var before = CarCondition.EngineLife(car, groupOf) / newLife;
        if (life >= Math.Round(before * newLife, 1, MidpointRounding.AwayFromZero) - 1e-6) return;
        if (CarCondition.WeakestRotatingPart(car, groupOf) is not { } failed) return;

        var loss = before - left;
        var heat = Clean(condition.Heat?.HeatLifeLost ?? 0);
        var oil = Clean(condition.Heat?.OilLifeLost ?? 0);
        var cooked = heat + oil >= loss * newLife * 0.5;
        foreach (var part in CarCondition.LifeParts(car, groupOf))
        {
            part.Tear = cooked || ReferenceEquals(part, failed)
                ? Math.Min(Clamp01(part.Tear), left)
                : Math.Max(left, Clamp01(part.Tear) - loss * CarCondition.SharedEngineDamage);
        }

        var group = groupOf(failed.DefinitionId);
        var blown = left <= 0 && before > 0;
        if (cooked)
        {
            var what = PartWords.Say(heat >= oil ? "condition.cause.heat" : "condition.cause.no_oil");
            if (blown) report.Add(PartWords.Say("condition.engine.cooked_blown", ("cause", what)));
            else if (loss > 0.005) report.Add(PartWords.Say("condition.engine.cooked_worn", ("cause", what), ("before", before * 100), ("after", left * 100)));
        }
        else if (blown) report.Add(PartWords.Say("condition.engine.let_go", ("part", CarCondition.FailedPartName(group))));
        else if (loss > 0.005) report.Add(PartWords.Say("condition.engine.beating", ("part", CarCondition.DamagedPartName(group)), ("before", before * 100), ("after", left * 100)));
    }

    private static void ApplyGearbox(Car car, RaceCarCondition condition, Func<string, string?> groupOf, ConditionReport report)
    {
        if (condition.GearboxDamage is not { } damage || CarCondition.Transmission(car, groupOf) is not { } gearbox) return;

        // AC starts every race with a new gearbox: what it reports is this race's
        damage = Clamp01(Clean(damage));
        if (damage <= 0) return;
        var before = Clamp01(gearbox.Tear);
        gearbox.Tear = Math.Max(0, before - damage);
        report.Add(gearbox.Tear < CarCondition.BrokenBelow
            ? PartWords.Say("condition.gearbox.wrecked")
            : PartWords.Say("condition.gearbox.hurt", ("before", before * 100), ("after", gearbox.Tear * 100)));
    }

    private static void ApplyMileage(Car car, double distanceKm)
    {
        var wear = Clean(distanceKm) * CarCondition.MileageWearPerKm;
        if (wear <= 0 || car.Engine == null) return;
        foreach (var part in car.Engine.SelfAndDescendants()) part.Wear = Math.Max(0, Clamp01(part.Wear) - wear);
    }

    /// <summary>
    /// The rear axle: its mileage (slower than the engine's), and when the race broke it, its weakest part wrecked: the
    /// shaft, the gears or the carrier that held the least
    /// </summary>
    private static void ApplyAxle(Car car, RaceCarCondition condition, double distanceKm, PartsCatalog catalog, ConditionReport report)
    {
        var axle = RearAxle.Mounted(car.Parts);
        var wear = Clean(distanceKm) * CarCondition.MileageWearPerKm * AxleMileageShare;
        foreach (var part in axle.All) part.Wear = Math.Max(0, Clamp01(part.Wear) - wear);

        if (condition.AxleBroken != true || RearAxle.Weakest(catalog, axle) is not { } weakest) return;
        weakest.Part.Tear = 0;
        var name = catalog.Get(weakest.Part.DefinitionId) is { } definition ? definition.DisplayName ?? definition.Name : weakest.Part.DefinitionId;
        report.Add(PartWords.Say("condition.axle.broke", ("part", name)));
    }

    private static void ApplyRunningGear(Car car, RaceCarCondition condition, ConditionReport report)
    {
        if (condition.Wheels is not { } wheels) return;

        var corners = RunningGear.Mounted(car.Parts);
        for (var i = 0; i < corners.Length && i < wheels.Count; i++)
        {
            var wheel = wheels[i];
            var corner = corners[i];
            var name = RunningGear.CornerName(i);

            if (corner.Tyre is { } tyre)
            {
                tyre.Wear = Math.Max(0, Clamp01(tyre.Wear) - Clamp01(Clean(wheel.TyreWear ?? 0)));
                if (wheel.TyreBlown == true && tyre.Tear > 0)
                {
                    tyre.Tear = 0;
                    report.Add(PartWords.Say("condition.tyre.blew", ("corner", name)));
                }
            }

            // AC starts every race with straight corners: the bend it reports is this race's
            var bend = Clamp01(Clean(wheel.SuspensionDamage ?? 0) / RaceStartState.MaxSuspensionBendMetres);
            if (bend <= 0.005) continue;
            foreach (var part in new[] { corner.Spring, corner.Shock })
            {
                if (part != null) part.Tear = Math.Max(0, Clamp01(part.Tear) - bend);
            }

            report.Add(PartWords.Say("condition.suspension.bent", ("corner", name)));
        }
    }

    /// <summary>A car without parts: AC's report goes on its own figures</summary>
    private static void ApplyToFigures(Car car, RaceCarCondition condition, ConditionReport report)
    {
        if (condition.EngineLife is { } life && double.IsFinite(life))
        {
            var left = Clamp01(life / CarCondition.NewEngineLife);
            if (left <= 0 && car.EngineHealth > 0) report.Add(PartWords.Say("condition.engine.blew"));
            car.EngineHealth = Math.Min(Clamp01(car.EngineHealth), left);
        }

        if (condition.GearboxDamage is { } damage)
            car.TransmissionHealth = Math.Max(0, Clamp01(car.TransmissionHealth) - Clamp01(Clean(damage)));

        if (condition.Wheels is { Count: > 0 } wheels)
            car.TireCondition = Math.Max(0, Clamp01(car.TireCondition) - wheels.Average(w => Clamp01(Clean(w.TyreWear ?? 0))));
    }

    private static double Clamp01(double value) => CarCondition.Clamp01(value);

    private static double Clean(double value) => CarCondition.Clean(value);
}
