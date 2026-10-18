using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// A car's damage as Assetto Corsa sees it, kept on the car's parts, and back.
///
/// After a race, what AC reports lands on the parts: an over-revved or crashed engine on its rotating parts
/// (crankshaft, rods, pistons, camshafts), the gearbox's damage on the transmission, a bent corner on its spring
/// and shock, the tread on the tyres. Body damage stays on the car (<see cref="Car.BodyDamageKmh"/>): a car's parts
/// have no panels. <c>Tear</c> is damage and only a repair takes it off; <c>Wear</c> is mileage.
///
/// Before a race the same parts give AC its start (<see cref="StartState"/>): the engine's life is its weakest
/// rotating part's, the gearbox's wear is the transmission's, each corner is as bent as its most damaged spring or
/// shock. The tyres go in as the data makes them (their wear lowers the grip in tyres.ini), and AC wears them from
/// new: carrying AC's tyre kilometres too would count the old wear twice. The fuel left in the tank and the body's
/// dirt go in as the last race left them too.
///
/// Group names come from <see cref="PartKinds.GroupOf"/>, handed in as a lookup so the rules can be tested
/// without a catalog.
/// </summary>
public static class CarCondition
{
    public const int Zones = 4;

    /// <summary>The body's zones as the report says them: "front", "rear", "left side", "right side"</summary>
    public static string ZoneName(int zone) => PartWords.Say(ZoneKeys[zone]);

    private static readonly string[] ZoneKeys = ["condition.zone.front", "condition.zone.rear", "condition.zone.left", "condition.zone.right"];

    /// <summary>A body that has taken this much, all zones together, is a write-off (a 200 km/h wreck reports 205 on two zones)</summary>
    public static double TotaledKmh => Rules.RuleBook.Current.Condition.TotaledKmh;

    /// <summary>The most a zone holds: a hand-edited save's 1e29 would make a repair bill no decimal can hold</summary>
    public const double MaxZoneKmh = 1000;

    /// <summary>AC's life of a new engine</summary>
    public static double NewEngineLife => Rules.RuleBook.Current.Condition.NewEngineLife;

    /// <summary>A gearbox or a corner in worse shape than this does not go racing</summary>
    public static double BrokenBelow => Rules.RuleBook.Current.Condition.BrokenBelow;

    /// <summary>
    /// Wear from plain driving, per km, on the engine and the gearbox. Small: what a race does to a car is AC's damage;
    /// a drag race costs a few thousandths of a percent.
    /// </summary>
    public static double MileageWearPerKm => Rules.RuleBook.Current.Condition.MileageWearPerKm;

    /// <summary>Dirt a body picks up per km of racing, on top of what AC gives it (on dirt, in the wet)</summary>
    public static double DirtPerKm => Rules.RuleBook.Current.Condition.DirtPerKm;

    /// <summary>Less fuel than this, in litres, and the tank is dry</summary>
    public static double EmptyTankLitres => Rules.RuleBook.Current.Condition.EmptyTankLitres;

    /// <summary>Less fuel than this, in litres, and the car does not go racing: it would run dry within the first minute</summary>
    public static double TooLittleToRaceLitres => Rules.RuleBook.Current.Condition.TooLittleToRaceLitres;

    /// <summary>What a fuel line in the report starts with: it tells, it is not damage</summary>
    public static string FuelNote => PartWords.Say("condition.note.fuel");

    /// <summary>The report says the tank is low below this share of it</summary>
    public static double LowTankShare => Rules.RuleBook.Current.Condition.LowTankShare;

    /// <summary>
    /// Oil an engine in good shape burns, share of a full sump: at every start (the drive there, the warm-up) and per km
    /// of racing. A worn engine burns up to <see cref="WornOilBurn"/> times as much: rings and valve guides let it by.
    /// </summary>
    public static double OilBurnPerRace => Rules.RuleBook.Current.Condition.OilBurnPerRace;
    public static double OilBurnPerKm => Rules.RuleBook.Current.Condition.OilBurnPerKm;
    public static double WornOilBurn => Rules.RuleBook.Current.Condition.WornOilBurn;

    /// <summary>The report says the oil is low below this, and the attendant would top it up</summary>
    public static double LowOil => Rules.RuleBook.Current.Condition.LowOil;

    /// <summary>What a sump holds its oil to at 0 oil, share of what it holds full: a low sump surges sooner</summary>
    public static double DrySumpShare => Rules.RuleBook.Current.Condition.DrySumpShare;

    /// <summary>What an oil line in the report starts with: it tells, it is not damage</summary>
    public static string OilNote => PartWords.Say("condition.note.oil");

    /// <summary>Engine life, as AC counts it, that the heat or the oil must take for the report to say so</summary>
    private const double TellLifeLost = 5;

    /// <summary>The water got this hot, and the engine went flat at the top</summary>
    private const double TellHotC = 110;

    public const string TransmissionGroup = PartGroups.Transmissions;

    private static readonly HashSet<string> RotatingGroups = new(StringComparer.Ordinal)
    {
        PartGroups.Crankshafts, PartGroups.ConnectingRods, PartGroups.Pistons, PartGroups.Camshafts
    };

    /// <summary>A lookup from part id to its shelf group, off the catalog</summary>
    public static Func<string, string?> Groups(PartsCatalog catalog) =>
        id => catalog.Get(id) is { } definition ? PartKinds.GroupOf(definition) : null;

    // ----- what the car is in -----

    /// <summary>The four zones, whatever an older save or a bad file left there</summary>
    public static double[] Body(Car car)
    {
        var zones = new double[Zones];
        var saved = car.BodyDamageKmh ?? Array.Empty<double>();
        for (var i = 0; i < Zones && i < saved.Length; i++) zones[i] = Math.Min(Clean(saved[i]), MaxZoneKmh);
        return zones;
    }

    public static double BodyTotal(Car car) => Body(car).Sum();

    public static bool IsTotaled(Car car) => BodyTotal(car) >= TotaledKmh;

    /// <summary>1 for a straight body, 0 for a write-off</summary>
    public static double BodyCondition(Car car) => 1 - Math.Min(1, BodyTotal(car) / TotaledKmh);

    public static IEnumerable<PartInstance> RotatingParts(Car car, Func<string, string?> groupOf) =>
        car.Engine?.SelfAndDescendants().Where(p => groupOf(p.DefinitionId) is { } g && RotatingGroups.Contains(g)) ?? Enumerable.Empty<PartInstance>();

    public static PartInstance? Transmission(Car car, Func<string, string?> groupOf) =>
        car.Engine?.SelfAndDescendants().FirstOrDefault(p => groupOf(p.DefinitionId) == TransmissionGroup);

    /// <summary>
    /// The parts AC's engine life stands for: the rotating parts, or the engine itself when it has none (a stand-in
    /// block), so the life AC takes off it is kept somewhere
    /// </summary>
    private static List<PartInstance> LifeParts(Car car, Func<string, string?> groupOf)
    {
        var rotating = RotatingParts(car, groupOf).ToList();
        return rotating.Count > 0 || car.Engine is not { } engine ? rotating : [engine];
    }

    /// <summary>AC's engine life: its weakest rotating part's (<see cref="LifeParts"/>); a car without an engine is taken as new</summary>
    public static double EngineLife(Car car, Func<string, string?> groupOf)
    {
        var tears = LifeParts(car, groupOf).Select(p => Clamp01(p.Tear)).ToList();
        return NewEngineLife * (tears.Count == 0 ? 1 : tears.Min());
    }

    public static double GearboxWear(Car car, Func<string, string?> groupOf) =>
        1 - Clamp01(Transmission(car, groupOf)?.Tear ?? 1);

    /// <summary>How bent each corner is, 0 to 1: its most damaged spring or shock</summary>
    public static double[] SuspensionBend(Car car)
    {
        var corners = RunningGear.Mounted(car.Parts);
        return corners.Select(c => 1 - Math.Min(Clamp01(c.Spring?.Tear ?? 1), Clamp01(c.Shock?.Tear ?? 1))).ToArray();
    }

    /// <summary>What the race mode puts into AC for this car at the start</summary>
    public static RaceStartState StartState(Car car, Func<string, string?>? groupOf)
    {
        var withParts = groupOf != null && car.HasPartsAssigned;
        return new RaceStartState
        {
            BodyKmh = Body(car),
            EngineLife = withParts ? EngineLife(car, groupOf!) : NewEngineLife * Clamp01(car.EngineHealth),
            GearboxWear = withParts ? GearboxWear(car, groupOf!) : 1 - Clamp01(car.TransmissionHealth),
            SuspensionBend = groupOf != null && car.HasRunningGearAssigned ? SuspensionBend(car) : new double[4],
            RideHeight = groupOf != null ? CarLooks.RideHeight(car) : new double[4],
            FuelLitres = FuelLeft(car),
            Dirt = Unit.Clamp01(car.BodyDirt, ifNotFinite: 0)
        };
    }

    /// <summary>
    /// The fuel in the tank, litres; null when it is full. A tank whose size is not known counts as full: what is left
    /// in it can be neither shown against it nor topped up.
    /// </summary>
    public static double? FuelLeft(Car car) =>
        car.FuelLitres is { } fuel && double.IsFinite(fuel) && TankLitres(car) is { } tank ? Math.Clamp(fuel, 0, tank) : null;

    /// <summary>What the tank holds, litres; null until a race says</summary>
    public static double? TankLitres(Car car) => car.FuelTankLitres is { } tank && double.IsFinite(tank) && tank > 0 ? tank : null;

    /// <summary>The litres a fill-up takes; 0 for a full tank</summary>
    public static double LitresToFill(Car car) => FuelLeft(car) is { } left && TankLitres(car) is { } tank ? tank - left : 0;

    /// <summary>The oil in the engine, 1 full to 0 dry</summary>
    public static double OilLeft(Car car) => car.OilLevel is { } level ? Unit.Clamp01(level, 1) : 1;

    /// <summary>
    /// What the sump holds its oil to with this much oil in it, share of what it holds full: a quart down it surges a
    /// little sooner, near dry at the first hard launch
    /// </summary>
    public static double SumpShare(Car car) => DrySumpShare + (1 - DrySumpShare) * OilLeft(car);

    /// <summary>
    /// How worn the engine is, 0 new to 1 worn out: the mileage of the parts that seal the combustion (the pistons, the
    /// heads) or, without them, of every engine part
    /// </summary>
    public static double EngineWornness(Car car, Func<string, string?>? groupOf)
    {
        if (car.Engine is not { } engine) return 0;
        var parts = engine.SelfAndDescendants().ToList();
        var sealing = groupOf == null ? [] : parts.Where(p => groupOf(p.DefinitionId) is "Pistons" or "Cylinder heads").ToList();
        var wear = (sealing.Count > 0 ? sealing : parts).Select(p => Clamp01(p.Wear)).DefaultIfEmpty(1).Average();
        return 1 - wear;
    }

    /// <summary>The oil a race of this length burns, share of a full sump</summary>
    public static double OilBurned(Car car, double distanceKm, Func<string, string?>? groupOf) =>
        (OilBurnPerRace + Clean(distanceKm) * OilBurnPerKm) * (1 + (WornOilBurn - 1) * EngineWornness(car, groupOf));

    /// <summary>What keeps the car from racing, in words for the player; empty when it can go</summary>
    /// <param name="counting">False leaves the tank out: what keeps the car off the road whatever is in it</param>
    public static List<string> WhyCannotRace(Car car, Func<string, string?>? groupOf, bool counting = true)
    {
        var problems = new List<string>();
        if (car.IsImpounded) problems.Add(PartWords.Say("condition.problem.impounded"));
        if (IsTotaled(car)) problems.Add(PartWords.Say("condition.problem.totaled"));
        if (counting && FuelLeft(car) is { } fuel && fuel < TooLittleToRaceLitres) problems.Add(PartWords.Say(fuel < EmptyTankLitres ? "condition.problem.tank_empty" : "condition.problem.fuel_low"));

        if (groupOf != null && car.HasPartsAssigned)
        {
            if (car.Engine != null && EngineLife(car, groupOf) <= 0) problems.Add(PartWords.Say("condition.problem.engine"));
            if (Transmission(car, groupOf) is { } gearbox && gearbox.Tear < BrokenBelow) problems.Add(PartWords.Say("condition.problem.gearbox"));
        }
        else
        {
            if (car.EngineHealth <= 0) problems.Add(PartWords.Say("condition.problem.engine"));
            if (car.TransmissionHealth < BrokenBelow) problems.Add(PartWords.Say("condition.problem.gearbox"));
        }

        if (car.HasAxleAssigned && RearAxle.Mounted(car.Parts).All.Any(p => p.Tear < BrokenBelow)) problems.Add(PartWords.Say("condition.problem.axle"));

        if (groupOf != null && car.HasRunningGearAssigned)
        {
            var corners = RunningGear.Mounted(car.Parts, groupOf);
            var bend = SuspensionBend(car);
            for (var i = 0; i < corners.Length; i++)
            {
                if (1 - bend[i] < BrokenBelow) problems.Add(PartWords.Say("condition.problem.suspension", ("corner", RunningGear.CornerName(i))));
                if (corners[i].Tyre is { Tear: <= 0 }) problems.Add(PartWords.Say("condition.problem.tyre", ("corner", RunningGear.CornerName(i))));
            }
        }

        return problems;
    }

    // ----- after a race -----

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
    public static ConditionReport ApplyRace(Car car, RaceCarCondition condition, double distanceKm, Func<string, string?>? groupOf,
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

        if (groupOf != null) RefreshFigures(car, groupOf);
        else car.BodyCondition = BodyCondition(car);

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

        var before = OilLeft(car);
        var left = Math.Max(0, before - OilBurned(car, distanceKm, groupOf));
        car.OilLevel = left;
        if (left < 0.4 && before >= 0.4) report.Note(ReportKind.Oil, OilNote + PartWords.Say("condition.oil.light"));
        else if (left < LowOil && before >= LowOil) report.Note(ReportKind.Oil, OilNote + PartWords.Say("condition.oil.burning"));
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
        if (condition.FuelLitres is { } fuel && double.IsFinite(fuel) && TankLitres(car) is { } size)
        {
            var left = Math.Clamp(fuel, 0, size);
            car.FuelLitres = left;
            if (left < EmptyTankLitres) report.Note(ReportKind.Fuel, FuelNote + PartWords.Say("condition.fuel.dry"));
            else if (left < TooLittleToRaceLitres) report.Note(ReportKind.Fuel, FuelNote + PartWords.Say("condition.fuel.too_little", ("litres", left)));
            else if (left < size * LowTankShare) report.Note(ReportKind.Fuel, FuelNote + PartWords.Say("condition.fuel.low", ("litres", left)));
        }

        // AC starts the body as dirty as the car went in: what it reports is the whole of it
        var dirt = Unit.Clamp01(car.BodyDirt, ifNotFinite: 0);
        if (condition.Dirt is { } reported && double.IsFinite(reported)) dirt = Math.Max(dirt, Unit.Clamp01(reported, ifNotFinite: 0));
        car.BodyDirt = Math.Min(1, dirt + Clean(distanceKm) * DirtPerKm);
    }

    private static void ApplyBody(Car car, RaceCarCondition condition, ConditionReport report)
    {
        if (condition.BodyDamageKmh is not { } reported) return;

        // AC starts the race with the body as the car had it: what it reports is the whole of it. A start that did not
        // take (no physics API) reports less than the car had, so the worse of the two stands.
        var zones = Body(car);
        var frontBefore = zones[0];
        var wasTotaled = zones.Sum() >= TotaledKmh;
        for (var i = 0; i < Zones && i < reported.Count; i++)
        {
            var now = Math.Min(Clean(reported[i]), MaxZoneKmh);
            if (now > zones[i] + 0.5) report.Add(PartWords.Say("condition.body.hit", ("zone", ZoneName(i)), ("kmh", now - zones[i])));
            zones[i] = Math.Max(zones[i], now);
        }

        car.BodyDamageKmh = zones;
        if (!wasTotaled && zones.Sum() >= TotaledKmh) report.Add(PartWords.Say("condition.body.totaled"));
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
        var left = Clamp01(life / NewEngineLife);
        var before = EngineLife(car, groupOf) / NewEngineLife;
        if (life >= Math.Round(before * NewEngineLife, 1, MidpointRounding.AwayFromZero) - 1e-6) return;
        if (WeakestRotatingPart(car, groupOf) is not { } failed) return;

        var loss = before - left;
        var heat = Clean(condition.Heat?.HeatLifeLost ?? 0);
        var oil = Clean(condition.Heat?.OilLifeLost ?? 0);
        var cooked = heat + oil >= loss * NewEngineLife * 0.5;
        foreach (var part in LifeParts(car, groupOf))
        {
            part.Tear = cooked || ReferenceEquals(part, failed)
                ? Math.Min(Clamp01(part.Tear), left)
                : Math.Max(left, Clamp01(part.Tear) - loss * SharedEngineDamage);
        }

        var group = groupOf(failed.DefinitionId);
        var blown = left <= 0 && before > 0;
        if (cooked)
        {
            var what = PartWords.Say(heat >= oil ? "condition.cause.heat" : "condition.cause.no_oil");
            if (blown) report.Add(PartWords.Say("condition.engine.cooked_blown", ("cause", what)));
            else if (loss > 0.005) report.Add(PartWords.Say("condition.engine.cooked_worn", ("cause", what), ("before", before * 100), ("after", left * 100)));
        }
        else if (blown) report.Add(PartWords.Say("condition.engine.let_go", ("part", FailedPartName(group))));
        else if (loss > 0.005) report.Add(PartWords.Say("condition.engine.beating", ("part", DamagedPartName(group)), ("before", before * 100), ("after", left * 100)));
    }

    /// <summary>The share of the failed part's loss the rest of the rotating assembly takes with it</summary>
    public static double SharedEngineDamage => Rules.RuleBook.Current.Condition.SharedEngineDamage;

    // Which rotating part gives first when two are as worn: the rods take the most of an over-rev
    private static readonly string[] FailureOrder = { PartGroups.ConnectingRods, PartGroups.Pistons, PartGroups.Crankshafts, PartGroups.Camshafts };

    /// <summary>
    /// The rotating part that gives when the engine takes damage: the most worn one, the rods before the pistons,
    /// the crankshaft and the camshafts when two are as worn; the engine itself when it has no rotating parts. Null
    /// for a car without an engine.
    /// </summary>
    public static PartInstance? WeakestRotatingPart(Car car, Func<string, string?> groupOf) =>
        LifeParts(car, groupOf)
            .OrderBy(p => Math.Round(Clamp01(p.Tear), 4))
            .ThenBy(p => Array.IndexOf(FailureOrder, groupOf(p.DefinitionId)) is var i and >= 0 ? i : FailureOrder.Length)
            .FirstOrDefault();

    /// <summary>
    /// The rotating part that gave, when one did: the weakest, worse off than the rest. Null when the weakest are as
    /// worn as each other (an engine the heat or the oil wore down whole, or a new one) or there is no engine.
    /// </summary>
    public static PartInstance? FailedPart(Car car, Func<string, string?> groupOf)
    {
        var parts = LifeParts(car, groupOf);
        if (WeakestRotatingPart(car, groupOf) is not { } weakest) return null;
        var tear = Math.Round(Clamp01(weakest.Tear), 4);
        return parts.Count == 1 || parts.Where(p => !ReferenceEquals(p, weakest)).All(p => Math.Round(Clamp01(p.Tear), 4) > tear) ? weakest : null;
    }

    /// <summary>The part of a group that let go, as the report says it: "a connecting rod"</summary>
    public static string FailedPartName(string? group) => PartWords.Say(group switch
    {
        PartGroups.ConnectingRods => "condition.failed.rod",
        PartGroups.Pistons => "condition.failed.piston",
        PartGroups.Crankshafts => "condition.failed.crankshaft",
        PartGroups.Camshafts => "condition.failed.camshaft",
        _ => "condition.failed.bottom_end"
    });

    /// <summary>The parts of a group that took damage, as the report says it: "the connecting rods"</summary>
    public static string DamagedPartName(string? group) => PartWords.Say(group switch
    {
        PartGroups.ConnectingRods => "condition.damaged.rods",
        PartGroups.Pistons => "condition.damaged.pistons",
        PartGroups.Crankshafts => "condition.damaged.crankshaft",
        PartGroups.Camshafts => "condition.damaged.camshaft",
        _ => "condition.damaged.bottom_end"
    });

    private static void ApplyGearbox(Car car, RaceCarCondition condition, Func<string, string?> groupOf, ConditionReport report)
    {
        if (condition.GearboxDamage is not { } damage || Transmission(car, groupOf) is not { } gearbox) return;

        // AC starts every race with a new gearbox: what it reports is this race's
        damage = Clamp01(Clean(damage));
        if (damage <= 0) return;
        var before = Clamp01(gearbox.Tear);
        gearbox.Tear = Math.Max(0, before - damage);
        report.Add(gearbox.Tear < BrokenBelow
            ? PartWords.Say("condition.gearbox.wrecked")
            : PartWords.Say("condition.gearbox.hurt", ("before", before * 100), ("after", gearbox.Tear * 100)));
    }

    private static void ApplyMileage(Car car, double distanceKm)
    {
        var wear = Clean(distanceKm) * MileageWearPerKm;
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
        var wear = Clean(distanceKm) * MileageWearPerKm * AxleMileageShare;
        foreach (var part in axle.All) part.Wear = Math.Max(0, Clamp01(part.Wear) - wear);

        if (condition.AxleBroken != true || RearAxle.Weakest(catalog, axle) is not { } weakest) return;
        weakest.Part.Tear = 0;
        var name = catalog.Get(weakest.Part.DefinitionId) is { } definition ? definition.DisplayName ?? definition.Name : weakest.Part.DefinitionId;
        report.Add(PartWords.Say("condition.axle.broke", ("part", name)));
    }

    /// <summary>How fast the rear axle's parts wear against the engine's, per km</summary>
    private const double AxleMileageShare = 0.5;

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
            var left = Clamp01(life / NewEngineLife);
            if (left <= 0 && car.EngineHealth > 0) report.Add(PartWords.Say("condition.engine.blew"));
            car.EngineHealth = Math.Min(Clamp01(car.EngineHealth), left);
        }

        if (condition.GearboxDamage is { } damage)
            car.TransmissionHealth = Math.Max(0, Clamp01(car.TransmissionHealth) - Clamp01(Clean(damage)));

        if (condition.Wheels is { Count: > 0 } wheels)
            car.TireCondition = Math.Max(0, Clamp01(car.TireCondition) - wheels.Average(w => Clamp01(Clean(w.TyreWear ?? 0))));
    }

    /// <summary>
    /// The car's own figures (what prices it and what the garage shows) worked out from its parts: the engine is
    /// its parts' mileage times the life of its weakest rotating part, the gearbox and tyres their parts' shape, the
    /// body from its damage.
    /// </summary>
    public static void RefreshFigures(Car car, Func<string, string?> groupOf)
    {
        if (car.HasPartsAssigned && car.Engine is { } engine)
        {
            var gearbox = Transmission(car, groupOf);
            var engineParts = engine.SelfAndDescendants().Where(p => !ReferenceEquals(p, gearbox)).ToList();
            var mileage = engineParts.Count == 0 ? 1 : engineParts.Average(p => Clamp01(p.Wear));
            car.EngineHealth = mileage * EngineLife(car, groupOf) / NewEngineLife;
            if (gearbox != null) car.TransmissionHealth = Clamp01(gearbox.Wear) * Clamp01(gearbox.Tear);
        }

        if (car.HasRunningGearAssigned)
        {
            var tyres = RunningGear.Mounted(car.Parts).Select(c => c.Tyre).Where(t => t != null).ToList();
            if (tyres.Count > 0) car.TireCondition = tyres.Average(t => Clamp01(t!.Wear) * Clamp01(t.Tear));
        }

        car.BodyCondition = BodyCondition(car);
    }

    /// <summary>A part's figure that is not a number counts as new: nothing measured it worn</summary>
    private static double Clamp01(double value) => Unit.Clamp01(value, ifNotFinite: 1);

    /// <summary>A reported figure, not negative and a number</summary>
    private static double Clean(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
}

/// <summary>What a line of the race report is about</summary>
public enum ReportKind
{
    Damage,
    Fuel,
    Oil
}

/// <summary>A line of <see cref="CarCondition.ApplyRace"/>'s report: its words, and whether it is damage or a note on the fuel or the oil</summary>
public sealed record ReportLine(string Text, ReportKind Kind)
{
    public bool IsDamage => Kind == ReportKind.Damage;

    public override string ToString() => Text;
}

/// <summary>The race report's lines: <see cref="Add(string)"/> a line of damage, <see cref="Note"/> one on the fuel or the oil</summary>
public sealed class ConditionReport : List<ReportLine>
{
    public void Add(string damage) => Add(new ReportLine(damage, ReportKind.Damage));

    public void Note(ReportKind kind, string text) => Add(new ReportLine(text, kind));
}
