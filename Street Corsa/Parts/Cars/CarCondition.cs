using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Models.Race;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// A car's damage as Assetto Corsa sees it, kept on the car's parts, and back: what the car is in, and what keeps it
/// off the road.
///
/// After a race, what AC reports lands on the parts (<see cref="RaceOutcome"/>, through <see cref="ApplyRace"/>): an
/// over-revved or crashed engine on its rotating parts (crankshaft, rods, pistons, camshafts), the gearbox's damage on
/// the transmission, a bent corner on its spring and shock, the tread on the tyres. Body damage stays on the car
/// (<see cref="Car.BodyDamageKmh"/>): a car's parts have no panels. <c>Tear</c> is damage and only a repair takes it
/// off; <c>Wear</c> is mileage.
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
    internal static List<PartInstance> LifeParts(Car car, Func<string, string?> groupOf)
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
            RideHeight = groupOf != null ? CarLooks.Sag(car) : new double[4],
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
        if (car.IsLifted) problems.Add(PartWords.Say("condition.problem.lifted"));
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
    /// Puts what AC reported at the end of a race onto the car and its parts (<see cref="RaceOutcome.Apply"/>) and
    /// brings the car's own figures up to date. Returns what happened to the car, a line each, for the player.
    /// </summary>
    public static ConditionReport ApplyRace(Car car, RaceCarCondition condition, double distanceKm, Func<string, string?>? groupOf,
        double wearMultiplier = 1.0, bool keepsFuelAndDirt = true, PartsCatalog? catalog = null) =>
        RaceOutcome.Apply(car, condition, distanceKm, groupOf, wearMultiplier, keepsFuelAndDirt, catalog);

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
    internal static double Clamp01(double value) => Unit.Clamp01(value, ifNotFinite: 1);

    /// <summary>A reported figure, not negative and a number</summary>
    internal static double Clean(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
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
