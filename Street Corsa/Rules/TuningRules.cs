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
    /// The most of an axle's travel (its <c>PACKER_RANGE</c>) its height may move by, either way, the parts, the setup and
    /// the damage together (<see cref="CarStance"/>): past it the car would ride on its packers
    /// </summary>
    public double MostTravelLowered { get; set; } = TuningLimits.Core.MostTravelLowered;

    /// <summary>
    /// The most the steering lock may grow or shrink the road wheels' lock, as a share of the car's own (<c>STEER_RATIO</c>
    /// over the ratio it races on, <see cref="Parts.Export.AcSetupData.SteerRatio"/>): past it a car's steering geometry
    /// goes over centre. Kunos' drift cars run 1.27 to 1.33.
    /// </summary>
    public double MostSteerGain { get; set; } = TuningLimits.Core.MostSteerGain;

    /// <summary>The limits as the export and the scenes read them (<see cref="TuningLimits.Source"/>)</summary>
    public TuningLimits Limits() => new(MostTravelLowered, MostSteerGain);

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

    /// <summary>How the rivals set their cars up (<c>"rivals"</c>)</summary>
    public RivalTuning Rivals { get; set; } = new();

    /// <summary>The items as ranges, for <see cref="CarSetup.For"/></summary>
    public IReadOnlyDictionary<string, SetupRange> Ranges() =>
        Items.ToDictionary(i => i.Key, i => new SetupRange(i.Value.Min ?? 0, i.Value.Max ?? 0, i.Value.Step ?? 0), StringComparer.Ordinal);

    /// <summary>Each figure held to what the game can play by; an item the game does not know, or not a range, told and left out</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(MinutesPerItem), 0, 24 * 60, problems);
        Figures.Within(this, path, nameof(MaxMinutes), 0, 7 * 24 * 60, problems);
        Figures.Within(this, path, nameof(MostTravelLowered), 0, 1, problems);
        Figures.Within(this, path, nameof(MostSteerGain), 1, 3, problems);
        Rivals ??= new RivalTuning();
        Rivals.Check($"{path}.rivals", problems);

        // A figure a mod leaves out of an item is the core's for it (0 for an item the core does not give)
        var core = new TuningRules().Items;
        foreach (var (id, item) in Items.ToList())
        {
            var label = $"{path}.items.{id}";
            if (!CarSetup.IsItem(id))
            {
                problems.Add($"{label}: no such item (one of {string.Join(", ", CarSetup.Items)}); left out");
                Items.Remove(id);
                continue;
            }

            var own = core.GetValueOrDefault(id);
            if (CarSetup.Range(item.Min ?? own?.Min ?? 0, item.Max ?? own?.Max ?? 0, item.Step ?? own?.Step ?? 0, label, problems) is { } range)
                Items[id] = new TuningItem { Min = range.Min, Max = range.Max, Step = range.Step };
            else Items.Remove(id);
        }
    }
}

/// <summary>
/// How the rivals set their cars up (<see cref="RivalSetup"/>): a few a day, each by a setup of <see cref="Setups"/> named
/// by the racer (<c>"setup"</c> in the racers' data), else by their crew, else <see cref="Setup"/>. A setup names where
/// each item goes in what the car's parts let it move: a car-wide item (<see cref="CarSetup.Items"/>) or a part's spec by
/// its id, <c>"min"</c>, <c>"max"</c>, <c>"mid"</c> or <c>"stock"</c> of what the parts let it move; and
/// <c>"engine": "power"</c>, every adjustable spec of the engine set where it makes the most on the dyno. An item a setup
/// does not name, or names <c>"keep"</c>, is left as the racer had it. A mod's setup of an id the core has goes over the
/// core's item by item (the rules merge so): <c>"keep"</c> takes one of the core's items out.
/// </summary>
public sealed class RivalTuning
{
    /// <summary>The setup a racer goes by who names none, nor does their crew; empty or one with no items: none</summary>
    public string Setup { get; set; } = "street";

    /// <summary>The chance a racer looks over their car's setup on a given day</summary>
    public double Chance { get; set; } = 0.2;

    /// <summary>At most this many racers set their cars up in a day (an engine setup is a few dozen dyno runs)</summary>
    public int MaxPerDay { get; set; } = 5;

    public Dictionary<string, Dictionary<string, string>> Setups { get; set; } = new(StringComparer.Ordinal)
    {
        ["street"] = new(StringComparer.Ordinal),
        ["drag"] = new(StringComparer.Ordinal)
        {
            [CarSetup.PressureFront] = RivalSetup.Max, [CarSetup.PressureRear] = RivalSetup.Min, [RivalSetup.Engine] = RivalSetup.Power
        },
        ["grip"] = new(StringComparer.Ordinal)
        {
            [CarSetup.CamberFront] = RivalSetup.Min, [CarSetup.CamberRear] = RivalSetup.Min, [CarSetup.RideHeightFront] = RivalSetup.Min,
            [CarSetup.RideHeightRear] = RivalSetup.Min, ["drop"] = RivalSetup.Max, ["damping"] = RivalSetup.Max,
            [RivalSetup.Engine] = RivalSetup.Power
        },
        ["drift"] = new(StringComparer.Ordinal)
        {
            [CarSetup.CamberFront] = RivalSetup.Min, [CarSetup.SteerLock] = RivalSetup.Max, [CarSetup.PressureRear] = RivalSetup.Max,
            [CarSetup.RideHeightFront] = RivalSetup.Min, [CarSetup.RideHeightRear] = RivalSetup.Min, ["drop"] = RivalSetup.Max,
            ["damping"] = RivalSetup.Max, [RivalSetup.Engine] = RivalSetup.Power
        }
    };

    /// <summary>Whether the installed rules (<see cref="RuleBook.Installed"/>) have a setup of that id, for the racers' and crews' data</summary>
    public static bool Has(string id) => RuleBook.Installed.Tuning.Rivals.Setups.ContainsKey(id.Trim());

    /// <summary>
    /// The first of these ids a setup has (the racer's, their crew's, the rules'): one this save's rules do not have is
    /// passed over for the next
    /// </summary>
    public string? Named(params string?[] ids) => ids.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id) && Setups.ContainsKey(id.Trim()));

    /// <summary>The setup of that id; null for none, or an id no setup has</summary>
    public IReadOnlyDictionary<string, string>? Get(string? id) =>
        !string.IsNullOrWhiteSpace(id) && Setups.TryGetValue(id.Trim(), out var setup) && setup.Count > 0 ? setup : null;

    /// <summary>Each figure held to what the game can play by; a setup's position that is none of the words told and left out</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(Chance), 0, 1, problems);
        Figures.Within(this, path, nameof(MaxPerDay), 0, 1000, problems);
        Setup = Setup?.Trim() ?? string.Empty;
        Setups ??= new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (id, setup) in Setups.ToList())
        {
            if (setup == null)
            {
                Setups[id] = new Dictionary<string, string>(StringComparer.Ordinal);
                continue;
            }

            foreach (var (item, position) in setup.ToList())
            {
                if (RivalSetup.IsPosition(item, position)) continue;

                problems.Add($"{path}.setups.{id}.{item}: \"{position}\" is not min, max, mid, stock, keep or (for engine) power; left out");
                setup.Remove(item);
            }
        }

        if (Setup.Length > 0 && !Setups.ContainsKey(Setup))
            problems.Add($"{path}.setup: no setup \"{Setup}\" (one of {string.Join(", ", Setups.Keys)}); the racers leave their cars as they are");
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
