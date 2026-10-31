using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// A rival's setup of their car (<see cref="Rules.RivalTuning"/>): the Tune screen's rows (<see cref="CarTune.Rows"/>)
/// each moved where the racer's setup puts it, within what the car's parts let it move. <c>"engine": "power"</c> sets
/// every adjustable spec of the engine where it makes the most on the dyno, one spec at a time, twice over.
/// </summary>
public static class RivalSetup
{
    public const string Min = "min";
    public const string Max = "max";
    public const string Mid = "mid";
    public const string Stock = "stock";

    /// <summary>
    /// Left as the racer had it: what a mod's setup gives an item of the core's setup of the same id, which it goes over
    /// item by item (<c>"engine": "keep"</c>: no dyno tuning)
    /// </summary>
    public const string Keep = "keep";

    /// <summary>Every adjustable spec of the engine at its best on the dyno: the key <see cref="Engine"/> only</summary>
    public const string Power = "power";

    /// <summary>The key that stands for every adjustable spec of the engine</summary>
    public const string Engine = "engine";

    /// <summary>The most values of one engine spec tried on the dyno (a fine step over a wide range is sampled)</summary>
    public const int MostTries = 9;

    /// <summary>Whether <paramref name="position"/> is one a setup may give <paramref name="item"/></summary>
    public static bool IsPosition(string item, string? position) => position == Keep || (item == Engine
        ? position == Power
        : position is Min or Max or Mid or Stock);

    /// <summary>
    /// Where the setup wants each row it names, with the row (unlocked rows only; the rest are left out): a row by its
    /// item's id, then a row of the Engine tab by <see cref="Engine"/>
    /// </summary>
    /// <param name="powerOf">What an engine makes on the dyno; null when it does not run (<see cref="EngineTuner.PowerOf"/>)</param>
    public static List<(TuneRow Row, double Value)> Wanted(Car car, IReadOnlyList<TuneRow> rows, IReadOnlyDictionary<string, string> setup,
        Func<PartInstance, double?> powerOf)
    {
        var wanted = new List<(TuneRow Row, double Value)>();
        var tuneEngine = new List<TuneRow>();
        foreach (var row in rows.Where(r => !r.IsLocked))
        {
            if (setup.TryGetValue(row.Item, out var position))
            {
                if (Place(row, position) is { } value) wanted.Add((row, value));
            }
            else if (setup.GetValueOrDefault(Engine) == Power && row is { Part: not null, Tab: TuneTab.Engine })
                tuneEngine.Add(row);
        }

        if (tuneEngine.Count > 0) wanted.AddRange(BestOnTheDyno(car, tuneEngine, wanted, powerOf));
        return wanted;
    }

    /// <summary>Where a word puts a row in its range; null for a word that is none</summary>
    public static double? Place(TuneRow row, string position) => position switch
    {
        Min => row.Range.Min,
        Max => row.Range.Max,
        Mid => row.Snap((row.Range.Min + row.Range.Max) / 2),
        Stock => row.Snap(row.Stock),
        _ => null
    };

    /// <summary>
    /// The engine's rows set one at a time where the engine makes the most, the others held where they stand: first over
    /// the whole range (<see cref="MostTries"/> values at most), then step by step around what the first found (a
    /// timing that suits a boost). A value that makes no more than the one before it is not taken.
    /// </summary>
    private static IEnumerable<(TuneRow Row, double Value)> BestOnTheDyno(Car car, List<TuneRow> engine,
        List<(TuneRow Row, double Value)> others, Func<PartInstance, double?> powerOf)
    {
        var at = engine.ToDictionary(r => r.Key, r => r.Value);
        double? Power() =>
            CarTune.EngineWith(car, others.Concat(engine.Select(r => (r, at[r.Key])))) is { } with ? powerOf(with) : null;

        var best = Power() ?? double.NegativeInfinity;
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var row in engine)
            {
                var kept = at[row.Key];
                foreach (var value in pass == 0 ? Tries(row) : Around(row, kept))
                {
                    if (Math.Abs(value - kept) < 1e-9) continue;

                    at[row.Key] = value;
                    if (Power() is { } power && power > best + 1e-6)
                    {
                        best = power;
                        kept = value;
                    }
                }

                at[row.Key] = kept;
            }
        }

        return engine.Select(r => (r, at[r.Key]));
    }

    /// <summary>The values tried over a range: each step, or <see cref="MostTries"/> spread over it when there are more</summary>
    private static IEnumerable<double> Tries(TuneRow row)
    {
        var range = row.Range;
        var span = range.Max - range.Min;
        var count = Math.Clamp(Steps(range, span), 1, MostTries - 1);
        return Enumerable.Range(0, count + 1).Select(i => row.Snap(range.Min + span * i / count)).Distinct();
    }

    /// <summary>
    /// The values around <paramref name="value"/> the first look over the range stepped over: each step within one of its
    /// spacings either side, <see cref="MostTries"/> of them at most each side
    /// </summary>
    private static IEnumerable<double> Around(TuneRow row, double value)
    {
        var range = row.Range;
        var span = range.Max - range.Min;
        var steps = Steps(range, span);
        if (steps < MostTries) return [];

        var spacing = span / (MostTries - 1);
        var step = range.Step > 0 ? range.Step : spacing / (MostTries - 1);
        var each = Math.Min(MostTries, (int)Math.Ceiling(spacing / step - 1e-9));
        return Enumerable.Range(-each, 2 * each + 1).Select(i => row.Snap(value + i * step)).Distinct();
    }

    private static int Steps(SetupRange range, double span) =>
        range.Step > 0 ? (int)Math.Floor(span / range.Step + 1e-9) : int.MaxValue;
}
