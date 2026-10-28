using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Rules;

/// <summary>
/// The player's tuning of a car (<c>"tuning"</c> in the rules): how long it takes in the garage, and what any car may
/// adjust without a part for it (<see cref="CarSetup"/>). A tune costs nothing but the time.
/// </summary>
public sealed class TuningRules
{
    /// <summary>The garage's minutes for each item changed in a tune</summary>
    public double MinutesPerItem { get; set; } = 15;

    /// <summary>The most a tune takes, minutes, however many items it changes</summary>
    public double MaxMinutes { get; set; } = 120;

    /// <summary>
    /// The car-wide items any car may adjust (<see cref="CarSetup.Items"/>), each an offset from what its parts make of it
    /// in the item's unit: a stock car's tyre pressures, and its toe and camber a little. A part's <c>adjust</c> opens
    /// the others and widens these.
    /// </summary>
    public Dictionary<string, TuningItem> Items { get; set; } = new(StringComparer.Ordinal)
    {
        [CarSetup.PressureFront] = new() { Min = -0.3, Max = 0.3, Step = 0.05 },
        [CarSetup.PressureRear] = new() { Min = -0.3, Max = 0.3, Step = 0.05 },
        [CarSetup.CamberFront] = new() { Min = -0.5, Max = 0.5, Step = 0.1 },
        [CarSetup.CamberRear] = new() { Min = -0.5, Max = 0.5, Step = 0.1 },
        [CarSetup.ToeFront] = new() { Min = -1, Max = 1, Step = 0.1 },
        [CarSetup.ToeRear] = new() { Min = -1, Max = 1, Step = 0.1 }
    };

    /// <summary>The items as ranges, for <see cref="CarSetup.For"/></summary>
    public IReadOnlyDictionary<string, SetupRange> Ranges() =>
        Items.ToDictionary(i => i.Key, i => new SetupRange(i.Value.Min ?? 0, i.Value.Max ?? 0, i.Value.Step ?? 0), StringComparer.Ordinal);

    /// <summary>Each figure held to what the game can play by; an item the game does not know, or not a range, told and left out</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(MinutesPerItem), 0, 24 * 60, problems);
        Figures.Within(this, path, nameof(MaxMinutes), 0, 7 * 24 * 60, problems);

        foreach (var (id, item) in Items.ToList())
        {
            var label = $"{path}.items.{id}";
            if (!CarSetup.IsItem(id))
            {
                problems.Add($"{label}: no such item (one of {string.Join(", ", CarSetup.Items)}); left out");
                Items.Remove(id);
                continue;
            }

            // A figure a mod leaves out of an item is the core's for it (0 for an item the core does not give)
            var core = new TuningRules().Items.GetValueOrDefault(id);
            if (CarSetup.Range(item.Min ?? core?.Min ?? 0, item.Max ?? core?.Max ?? 0, item.Step ?? core?.Step ?? 0, label, problems) is { } range)
                Items[id] = new TuningItem { Min = range.Min, Max = range.Max, Step = range.Step };
            else Items.Remove(id);
        }
    }
}

/// <summary>
/// How far a car-wide item may move without parts: offsets from the parts' figure, in steps (the Tune screen's). A
/// figure a mod leaves out is the core's for the item.
/// </summary>
public sealed class TuningItem
{
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
}
