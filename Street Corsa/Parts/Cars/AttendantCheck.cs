using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// Something the attendant noticed about the car while filling it up: what (a text key, <c>station.hint.radiator</c>),
/// how bad (<see cref="Soon"/> or not: it needs doing now), and on which part or corner, for the words
/// </summary>
public sealed record AttendantHint(string Key, bool Soon, double Condition, string? Where = null)
{
    /// <summary>The text pool the line comes from: <c>station.hint.radiator.soon</c> or <c>.now</c></summary>
    public string TextKey => $"{Key}.{(Soon ? "soon" : "now")}";
}

/// <summary>
/// What the attendant at the gas station sees, hears and smells while the tank fills: the worn parts a man with a rag
/// and a good ear would notice. Not a mechanic's inspection: only the things that show (a weeping radiator, thin tread,
/// a blowing exhaust, a black air filter) or that he hears as the car rolls in (a knocking engine, a crunching gearbox,
/// grinding brakes). A part worn under <see cref="SoonBelow"/> is mentioned as needing doing shortly, under
/// <see cref="NowBelow"/> as needing it now; the worst few, a line each.
/// </summary>
public static class AttendantCheck
{
    /// <summary>A part this worn (<see cref="PartInstance.Wear"/>) is worth mentioning: it will need replacing shortly</summary>
    public const double SoonBelow = 0.3;

    /// <summary>A part this worn needs replacing now</summary>
    public const double NowBelow = 0.12;

    /// <summary>An engine or a gearbox this damaged (<see cref="PartInstance.Tear"/>) is heard as the car rolls in</summary>
    public const double HeardBelow = 0.55;

    public const int MostHints = 3;

    private const string AirFiltersGroup = "Air filters";

    // Engine parts that show under the hood, by their shelf group
    private static readonly (string Group, string Key)[] UnderTheHood =
    [
        ("Exhaust", "station.hint.exhaust"),
        (AirFiltersGroup, "station.hint.air_filter"),
        ("Carburettors and injection", "station.hint.carburettor"),
        ("Flywheels and clutches", "station.hint.clutch")
    ];

    /// <summary>The worst few things the attendant would mention, worst first; none for a car in good shape</summary>
    /// <param name="catalog">To tell the parts apart; null leaves only what the car's own figures say</param>
    public static List<AttendantHint> Look(Car car, PartsCatalog? catalog)
    {
        var hints = new List<AttendantHint>();
        if (catalog != null && car.HasPartsAssigned && car.Engine is { } engine)
        {
            var groupOf = CarCondition.Groups(catalog);
            var parts = engine.SelfAndDescendants().Select(p => (Part: p, Definition: catalog.Get(p.DefinitionId))).Where(p => p.Definition != null).ToList();

            // The cooling: he sees the radiator weep and the pump drip, and hears the fan wobble
            foreach (var (kind, key) in new[] { (EngineCooling.Kind.Radiator, "station.hint.radiator"), (EngineCooling.Kind.WaterPump, "station.hint.water_pump"), (EngineCooling.Kind.Fan, "station.hint.fan") })
                Worn(hints, key, parts.Where(p => EngineCooling.KindOf(p.Definition!) == kind).Select(p => p.Part));

            // An open velocity stack is among the air filters but has no element to go black (it loses nothing dusted)
            foreach (var (group, key) in UnderTheHood)
                Worn(hints, key, parts.Where(p => PartKinds.GroupOf(p.Definition!) == group && !(group == AirFiltersGroup && NeverClogs(p.Definition!))).Select(p => p.Part));

            // Heard as it rolled in: a bottom end on its way out knocks, a torn-up gearbox crunches
            var life = CarCondition.EngineLife(car, groupOf) / CarCondition.NewEngineLife;
            if (life > 0 && life < HeardBelow) hints.Add(new AttendantHint("station.hint.engine", life >= HeardBelow / 2, life));
            if (CarCondition.Transmission(car, groupOf) is { } gearbox && gearbox.Tear > CarCondition.BrokenBelow && gearbox.Tear < HeardBelow)
                hints.Add(new AttendantHint("station.hint.gearbox", gearbox.Tear >= HeardBelow / 2, gearbox.Tear));
        }

        if (car.HasRunningGearAssigned)
        {
            var corners = RunningGear.Mounted(car.Parts);
            for (var i = 0; i < corners.Length; i++)
            {
                // Tread he can see at every corner; a blown tyre is no hint, it is flat
                if (corners[i].Tyre is { Tear: > 0 } tyre) Worn(hints, "station.hint.tyre", [tyre], RunningGear.CornerName(i));
            }

            // The brakes grind on the way in, the shocks bounce when he leans on the fender: an axle's worst
            foreach (var first in new[] { 0, 2 })
            {
                Worn(hints, "station.hint.brakes", new[] { corners[first].Brake, corners[first + 1].Brake }.OfType<PartInstance>(), RunningGear.AxleName(first));
                Worn(hints, "station.hint.shocks", new[] { corners[first].Shock, corners[first + 1].Shock }.OfType<PartInstance>(), RunningGear.AxleName(first));
            }
        }

        return hints.OrderBy(h => h.Condition).Take(MostHints).ToList();
    }

    /// <summary>An air part whose script keeps all its flow however dusty it gets (<c>dusted_efficiency</c> 1): nothing to clean or replace</summary>
    private static bool NeverClogs(PartDefinition part) => part.Number("dusted_efficiency") >= 1;

    /// <summary>The most worn of the parts, when it is worn enough to mention</summary>
    private static void Worn(List<AttendantHint> hints, string key, IEnumerable<PartInstance> parts, string? where = null)
    {
        var worst = parts.Select(p => Unit.Clamp01(p.Wear, 1)).DefaultIfEmpty(1).Min();
        if (worst < SoonBelow) hints.Add(new AttendantHint(key, worst >= NowBelow, worst, where));
    }
}
