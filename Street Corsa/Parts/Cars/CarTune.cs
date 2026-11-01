using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Export;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts.Cars;

/// <summary>The Tune screen's tabs, in the order they are shown</summary>
public enum TuneTab
{
    Alignment,
    Suspension,
    Engine,
    Gearing,
    BrakesAndTyres
}

/// <summary>
/// One thing the player can set on the Tune screen: a car-wide item of the setup (<see cref="CarSetup.Items"/>, an offset
/// from what the parts make of it) or a spec of a mounted part its <c>adjust</c> opens (a coilover's damping, a cam's
/// timing; the spec's own figure). A car-wide item no mounted part opens is shown too, locked, with the parts that would
/// (<see cref="Needs"/>).
/// </summary>
public sealed record TuneRow
{
    /// <summary>The row's id on the screen: the item's id, or the part's instance and the spec ("&lt;guid&gt;:damping")</summary>
    public required string Key { get; init; }

    public required TuneTab Tab { get; init; }

    /// <summary>The car-wide item, or the part's spec</summary>
    public required string Item { get; init; }

    /// <summary>The part the spec is of; null for a car-wide item</summary>
    public PartInstance? Part { get; init; }

    public PartDefinition? Definition { get; init; }

    /// <summary>How far it may move: offsets for a car-wide item, the spec's figures for a part's</summary>
    public required SetupRange Range { get; init; }

    /// <summary>
    /// Where it stands as the car came: for a car-wide item nothing, or the nearest to it the parts allow (lowering blocks
    /// hold a ride height away from it, <see cref="CarSetup.Values"/>); the part's own figure for a spec
    /// </summary>
    public double Stock { get; init; }

    /// <summary>Where it stands now</summary>
    public double Value { get; init; }

    /// <summary>For a locked item, the shelves (<see cref="PartKinds.GroupName"/>) of the parts that would open it</summary>
    public IReadOnlyList<string> Needs { get; init; } = [];

    public bool IsLocked => Range.Max <= Range.Min;

    public bool IsCarItem => Part == null;

    /// <summary>A value on the row's steps and inside its range, the steps counted from where a part's spec stands as it came</summary>
    public double Snap(double value) => Range.Snap(value, IsCarItem ? 0 : Stock);
}

/// <summary>
/// What the Tune screen shows and does (chassis tuning phase 3): the rows of a car (<see cref="Rows"/>), each set item
/// written back (<see cref="Apply"/>), and the garage time it takes (<see cref="Minutes"/>). A tune costs nothing but the
/// time.
/// </summary>
public static class CarTune
{
    /// <summary>
    /// Every row of the car: the car-wide items as the car's data takes them (<paramref name="setup"/>,
    /// <see cref="AcSetupData.ForData"/>: none for camber on a live axle), the ones a part could open locked, then each
    /// mounted part's own adjustable specs
    /// </summary>
    /// <param name="setup">The car's setup as its data takes it</param>
    /// <param name="gone">The car-wide items the car's data has no place for (camber and toe on a live axle): not shown at all</param>
    public static List<TuneRow> Rows(Car car, PartsCatalog catalog, SetupChoice setup, IReadOnlySet<string>? gone = null)
    {
        var rows = new List<TuneRow>();
        foreach (var item in CarSetup.Items)
        {
            if (gone?.Contains(item) == true) continue;

            if (setup.Allowed.TryGetValue(item, out var range) && range.Max > range.Min)
            {
                rows.Add(new TuneRow
                {
                    Key = item, Tab = TabOf(item), Item = item, Range = range, Stock = range.Snap(0), Value = setup.Values.GetValueOrDefault(item)
                });
                continue;
            }

            // Locked: shown only when some part would open it
            var needs = Opening(catalog, item);
            if (needs.Count > 0)
                rows.Add(new TuneRow { Key = item, Tab = TabOf(item), Item = item, Range = SetupRange.Fixed, Needs = needs });
        }

        var engine = car.Engine?.SelfAndDescendants().ToHashSet() ?? [];
        foreach (var part in (car.Parts ?? []).Where(p => p != null).SelectMany(p => p.SelfAndDescendants()))
        {
            if (catalog.Get(part.DefinitionId) is not { } definition) continue;

            foreach (var (spec, range) in CarSetup.AdjustsOf(definition))
            {
                if (CarSetup.IsItem(spec) || range.Max <= range.Min) continue;

                var stock = StockOf(definition, spec);
                var value = part.Tuning != null && part.Tuning.TryGetValue(spec, out var tuned) && double.IsFinite(tuned) ? range.Clamp(tuned) : stock;
                rows.Add(new TuneRow
                {
                    Key = $"{part.InstanceId:N}:{spec}", Tab = TabOf(definition, spec, engine.Contains(part)), Item = spec, Part = part,
                    Definition = definition, Range = range, Stock = stock, Value = value
                });
            }
        }

        return rows;
    }

    /// <summary>
    /// The values the player set written onto the car: a car-wide item into <c>Car.Setup</c> (left out at nothing), a
    /// part's spec into its tuning (left out at the part's own figure). Each held to its range and on its steps; a
    /// locked row is left as it is.
    /// </summary>
    /// <returns>How many rows changed</returns>
    public static int Apply(Car car, IEnumerable<(TuneRow Row, double Value)> set)
    {
        var changed = 0;
        foreach (var (row, wanted) in set)
        {
            if (row.IsLocked) continue;

            var value = row.Snap(wanted);
            if (Same(value, row.Value)) continue;

            changed++;
            if (row.Part is { } part)
            {
                part.Tuning ??= new Dictionary<string, double>();
                if (Same(value, row.Stock)) part.Tuning.Remove(row.Item);
                else part.Tuning[row.Item] = value;
            }
            else
            {
                car.Setup ??= new Dictionary<string, double>();
                if (Same(value, 0) || Same(value, row.Stock)) car.Setup.Remove(row.Item);
                else car.Setup[row.Item] = value;
            }
        }

        return changed;
    }

    /// <summary>The garage's minutes for a tune that changes that many items: each its own, no more than the most a tune takes</summary>
    public static double Minutes(int changed, Rules.TuningRules rules) =>
        changed <= 0 ? 0 : Math.Min(changed * Math.Max(0, rules.MinutesPerItem), Math.Max(0, rules.MaxMinutes));

    /// <summary>
    /// The engine as it would be with the tuning of these rows (the screen's live dyno): a copy of the car's engine with
    /// the engine rows' values on its parts, the car's own left alone; null for a car with no engine
    /// </summary>
    public static PartInstance? EngineWith(Car car, IEnumerable<(TuneRow Row, double Value)> set)
    {
        if (car.Engine is not { } engine) return null;

        var copy = PartTrees.Clone(engine, keepIds: true);
        var parts = copy.SelfAndDescendants().ToDictionary(p => p.InstanceId);
        foreach (var (row, wanted) in set)
        {
            if (row.Part == null || row.IsLocked || !parts.TryGetValue(row.Part.InstanceId, out var part)) continue;

            var value = row.Snap(wanted);
            part.Tuning = new Dictionary<string, double>(part.Tuning ?? []);
            if (Same(value, row.Stock)) part.Tuning.Remove(row.Item);
            else part.Tuning[row.Item] = value;
        }

        return copy;
    }

    /// <summary>The tab a car-wide item is on</summary>
    public static TuneTab TabOf(string item) => item switch
    {
        CarSetup.RideHeightFront or CarSetup.RideHeightRear => TuneTab.Suspension,
        CarSetup.PressureFront or CarSetup.PressureRear or CarSetup.BrakeBias => TuneTab.BrakesAndTyres,
        _ => TuneTab.Alignment
    };

    /// <summary>
    /// The tab a part's spec is on: the gearbox's and the axle's ratios on Gearing, the rest of the engine on Engine, the
    /// wheels, brakes and electronics on Brakes &amp; tyres, the rest of the car on Suspension
    /// </summary>
    public static TuneTab TabOf(PartDefinition part, string spec, bool inEngine)
    {
        if (PartKinds.Is(part, PartClasses.Transmission) || spec.StartsWith("ratio", StringComparison.Ordinal) || spec.StartsWith("final", StringComparison.Ordinal))
            return TuneTab.Gearing;
        if (inEngine) return TuneTab.Engine;

        return PartKinds.GroupOf(part) switch
        {
            PartKinds.RearAxles or PartKinds.Differentials or PartKinds.RingPinions or PartKinds.AxleShafts => TuneTab.Gearing,
            PartKinds.Tyres or PartKinds.Rims or PartKinds.Brakes or PartKinds.BrakeValves or PartKinds.Electronics => TuneTab.BrakesAndTyres,
            _ => TuneTab.Suspension
        };
    }

    /// <summary>
    /// A spec's own figure on the part, where a tuning of it starts: a list's element ("ratio[2]"), a spec, or one of
    /// its figures ("timing"). A spec the part does not give is nothing to the code that reads it (a drop of 0): the
    /// nearest to nothing its range allows.
    /// </summary>
    public static double StockOf(PartDefinition part, string spec)
    {
        var range = CarSetup.AdjustsOf(part).GetValueOrDefault(spec) ?? SetupRange.Fixed;
        double own;
        if (spec.IndexOf('[') is > 0 and var at && spec.EndsWith(']') && int.TryParse(spec[(at + 1)..^1], out var index))
            own = part.Element(spec[..at], index, double.NaN);
        else if (part.Specs.ContainsKey(spec))
            own = part.Number(spec, double.NaN);
        else
            own = part.ReadFigures?.Numbers.FirstOrDefault(f => f.Name == spec)?.Value ?? double.NaN;

        return double.IsFinite(own) ? own : range.Clamp(0);
    }

    /// <summary>The shelves of the parts that would open a car-wide item, in the catalog's order, each once</summary>
    private static List<string> Opening(PartsCatalog catalog, string item) =>
        catalog.Parts.Values
            .Where(p => CarSetup.AdjustsOf(p).TryGetValue(item, out var range) && range.Max > range.Min)
            .Select(PartKinds.GroupOf)
            .Distinct(StringComparer.Ordinal)
            .Select(PartKinds.GroupName)
            .ToList();

    private static bool Same(double a, double b) => Math.Abs(a - b) < 1e-9;
}
