using System.Globalization;
using Newtonsoft.Json.Linq;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// How far one setup item may move: from <see cref="Min"/> to <see cref="Max"/> in <see cref="Step"/>s. A car-wide item's
/// figures are offsets from what the car's parts make of it (0 is the car as it came), in the item's unit (see
/// <see cref="CarSetup"/>'s items); a part's own spec's are the spec's values.
/// </summary>
public sealed record SetupRange(double Min, double Max, double Step)
{
    /// <summary>Nothing to move: the item stays as the parts make it</summary>
    public static readonly SetupRange Fixed = new(0, 0, 0);

    /// <summary>Both ranges in one: the least of the least, the most of the most, the finer step</summary>
    public SetupRange Union(SetupRange other) =>
        new(Math.Min(Min, other.Min), Math.Max(Max, other.Max), Step <= 0 ? other.Step : other.Step <= 0 ? Step : Math.Min(Step, other.Step));

    /// <summary>A value held inside the range; one that is no number is taken as 0, then held</summary>
    public double Clamp(double value)
    {
        if (!double.IsFinite(value)) value = 0;
        return Math.Clamp(value, Min, Max);
    }

    /// <summary>A value on the range's steps (counted from 0, the parts' own figure) and inside it: the Tune screen's</summary>
    public double Snap(double value)
    {
        var held = Clamp(value);
        if (Step <= 0) return held;

        var snapped = Math.Round(held / Step) * Step;
        return Math.Round(snapped >= Min - 1e-9 && snapped <= Max + 1e-9 ? snapped : held, 9);
    }
}

/// <summary>
/// A car's setup (chassis tuning step 42, phase 1): what the player sets on the car beyond the parts' own figures. The
/// car-wide items (alignment, ride height, pressures, brake bias, steering lock) are kept on the car
/// (<c>Car.Setup</c>), as offsets from what the parts make of them; a part's own adjustable specs (a coilover's
/// damping) in the part's <see cref="PartInstance.Tuning"/>.
///
/// What may move and how far is data: the rules' <c>tuning.items</c> give what any car may adjust without parts (the
/// tyre pressures, a little toe and camber), and a part's <c>adjust</c> spec opens an item or widens its range while it is
/// on the car: <c>"adjust": { "camber_front": { "min": -3, "max": 1, "step": 0.1 } }</c>. An id that is not a car-wide
/// item names a spec of the part itself (<c>"damping"</c> on a shock): the part's tuning moves it within the range.
/// </summary>
public static class CarSetup
{
    public const string CamberFront = "camber_front";
    public const string CamberRear = "camber_rear";
    public const string ToeFront = "toe_front";
    public const string ToeRear = "toe_rear";
    public const string RideHeightFront = "ride_height_front";
    public const string RideHeightRear = "ride_height_rear";
    public const string PressureFront = "pressure_front";
    public const string PressureRear = "pressure_rear";
    public const string BrakeBias = "brake_bias";
    public const string SteerLock = "steer_lock";

    /// <summary>The part spec the setup key of an adjustable part names</summary>
    public const string AdjustSpec = "adjust";

    /// <summary>
    /// The car-wide items the game knows, in order. Their units: camber degrees, toe mm of steering rod, ride height mm,
    /// pressure bar, brake bias the share of the braking at the front, steering lock degrees of the steering wheel.
    /// </summary>
    public static IReadOnlyList<string> Items { get; } =
        [CamberFront, CamberRear, ToeFront, ToeRear, RideHeightFront, RideHeightRear, PressureFront, PressureRear, BrakeBias, SteerLock];

    private static readonly HashSet<string> ItemSet = new(Items, StringComparer.Ordinal);

    public static bool IsItem(string id) => ItemSet.Contains(id);

    /// <summary>The most any range may give, by an item's figures: past it a figure is no setup but a typo</summary>
    public const double MostFigure = 1_000_000;

    /// <summary>
    /// A range as a rule or a part gives it, checked: numbers, the least first, a step of nothing or more; null with the
    /// problem told when it is none of that
    /// </summary>
    public static SetupRange? Range(double min, double max, double step, string label, List<string>? problems)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || !double.IsFinite(step) || Math.Abs(min) > MostFigure || Math.Abs(max) > MostFigure)
        {
            problems?.Add($"{label}: min, max and step must be numbers; left out");
            return null;
        }

        if (min > max)
        {
            problems?.Add(FormattableString.Invariant($"{label}: min ({min}) is over max ({max}); taken the other way round"));
            (min, max) = (max, min);
        }

        if (step < 0)
        {
            problems?.Add(FormattableString.Invariant($"{label}: step ({step}) is below nothing; none taken"));
            step = 0;
        }

        return new SetupRange(min, max, step);
    }

    /// <summary>Every part's <c>adjust</c> read, as the catalog loads; what does not read is told and left out</summary>
    public static void Read(IEnumerable<PartDefinition> parts, List<string> problems)
    {
        foreach (var part in parts) part.Adjusts = Read(part, problems);
    }

    /// <summary>A part's <c>adjust</c> spec: item or spec id to its range; empty for a part that adjusts nothing</summary>
    public static IReadOnlyDictionary<string, SetupRange> Read(PartDefinition part, List<string>? problems)
    {
        var adjusts = new Dictionary<string, SetupRange>(StringComparer.Ordinal);
        if (part.Specs.GetValueOrDefault(AdjustSpec) is not { } given) return adjusts;

        if (given is not JObject table)
        {
            problems?.Add($"{part.Id} spec \"{AdjustSpec}\": not a table of ranges; left out");
            return adjusts;
        }

        foreach (var (id, token) in table)
        {
            var label = $"{part.Id} \"{AdjustSpec}\" \"{id}\"";
            if (token is not JObject range)
            {
                problems?.Add($"{label}: not {{ \"min\", \"max\", \"step\" }}; left out");
                continue;
            }

            // A typo names neither an item nor anything of the part: told, and kept (a spec the part leaves at its default
            // is no typo, and moves all the same)
            if (!IsItem(id) && !IsOwn(part, id))
                problems?.Add($"{label}: not a car-wide item ({string.Join(", ", Items)}) nor one of the part's specs or figures");

            if (Range(Figure(range, "min"), Figure(range, "max"), Figure(range, "step", 0), label, problems) is { } read) adjusts[id] = read;
        }

        return adjusts;
    }

    /// <summary>Whether an id names a spec of the part (a list's element as "ratio[2]") or one of its figures ("timing")</summary>
    private static bool IsOwn(PartDefinition part, string id)
    {
        var spec = id.IndexOf('[') is > 0 and var at ? id[..at] : id;
        return spec != AdjustSpec && part.Specs.ContainsKey(spec) || part.ReadFigures?.Numbers.Any(f => f.Name == id) == true;
    }

    /// <summary>A range's figure; <paramref name="fallback"/> when it is left out, no number (NaN, told) when it is not one</summary>
    private static double Figure(JObject range, string key, double fallback = double.NaN) => range[key] switch
    {
        null => fallback,
        JValue { Type: JTokenType.Integer or JTokenType.Float } value => Convert.ToDouble(value.Value, CultureInfo.InvariantCulture),
        _ => double.NaN
    };

    /// <summary>A part's adjustable items and specs: read at load, or now for a part made in code</summary>
    public static IReadOnlyDictionary<string, SetupRange> AdjustsOf(PartDefinition part) => part.Adjusts ?? Read(part, null);

    /// <summary>
    /// How far each car-wide item may move on a car with these parts on it: the rules' own, each widened by what the
    /// parts open. An item neither gives is not in it.
    /// </summary>
    public static Dictionary<string, SetupRange> Allowed(IReadOnlyDictionary<string, SetupRange>? rules, IEnumerable<PartDefinition> mounted)
    {
        var allowed = new Dictionary<string, SetupRange>(StringComparer.Ordinal);
        foreach (var (id, range) in rules ?? new Dictionary<string, SetupRange>())
        {
            if (IsItem(id)) allowed[id] = range;
        }

        foreach (var part in mounted)
        {
            foreach (var (id, range) in AdjustsOf(part))
            {
                if (IsItem(id)) allowed[id] = allowed.TryGetValue(id, out var before) ? before.Union(range) : range;
            }
        }

        return allowed;
    }

    /// <summary>The definitions of every part on the car, mounted on it or on another part</summary>
    public static IEnumerable<PartDefinition> Mounted(IEnumerable<PartInstance>? carParts, PartsCatalog catalog) =>
        (carParts ?? []).Where(p => p != null).SelectMany(p => p.SelfAndDescendants()).Select(p => catalog.Get(p.DefinitionId)).OfType<PartDefinition>();

    /// <summary>
    /// The car's setup as it goes racing: each item that may move, at the car's value for it (nothing for one the player has
    /// not set) held inside what it may move and on its steps. An item a part opened goes back to the parts' figure once
    /// the part is off; one a part holds away from nothing (lowering blocks) is at the nearest it allows. Items at nothing
    /// left out.
    /// </summary>
    public static Dictionary<string, double> Values(IReadOnlyDictionary<string, double>? saved, IReadOnlyDictionary<string, SetupRange> allowed)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, range) in allowed)
        {
            var held = range.Snap(saved?.GetValueOrDefault(id) ?? 0);
            if (held != 0) values[id] = held;
        }

        return values;
    }

    /// <summary>
    /// The ride heights the car goes racing with, mm front and rear: what <see cref="For"/> comes to for those two items
    /// alone (a part holding one away from nothing sets it with no setup saved), for how the car sits in the scenes
    /// </summary>
    public static (double Front, double Rear) RideHeights(IEnumerable<PartInstance>? carParts, IReadOnlyDictionary<string, double>? saved, PartsCatalog catalog,
        IReadOnlyDictionary<string, SetupRange>? rules)
    {
        var front = rules?.GetValueOrDefault(RideHeightFront);
        var rear = rules?.GetValueOrDefault(RideHeightRear);
        foreach (var part in Mounted(carParts, catalog))
        {
            var adjusts = AdjustsOf(part);
            if (adjusts.TryGetValue(RideHeightFront, out var f)) front = front?.Union(f) ?? f;
            if (adjusts.TryGetValue(RideHeightRear, out var r)) rear = rear?.Union(r) ?? r;
        }

        double Value(string id, SetupRange? range) => range?.Snap(saved?.GetValueOrDefault(id) ?? 0) ?? 0;
        return (Value(RideHeightFront, front), Value(RideHeightRear, rear));
    }

    /// <summary>What a car's setup comes to, for the export: its values and how far each item may move</summary>
    /// <param name="carParts">The parts on the car (<c>Car.Parts</c>)</param>
    /// <param name="saved">The car's setup as the player set it (<c>Car.Setup</c>)</param>
    public static Export.SetupChoice For(IEnumerable<PartInstance>? carParts, IReadOnlyDictionary<string, double>? saved, PartsCatalog catalog,
        IReadOnlyDictionary<string, SetupRange>? rules)
    {
        var allowed = Allowed(rules, Mounted(carParts, catalog));
        return new Export.SetupChoice { Values = Values(saved, allowed), Allowed = allowed };
    }
}
