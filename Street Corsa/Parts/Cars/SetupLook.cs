using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// How a car is set up, as the street sees it: lowered (its ride height set down, a drop wound in, or parts that drop
/// it), the steering lock wound out past stock (an angle kit), the engine tuned off its parts' own figures, and how many
/// items moved off stock in all. What the rivals' talk and the paper's ads say of a car.
/// </summary>
public sealed record SetupLook(bool Lowered, bool AngleKit, bool EngineTuned, int Changed)
{
    public static readonly SetupLook Stock = new(false, false, false, 0);

    /// <summary>Anything worth a word</summary>
    public bool Shows => Lowered || AngleKit || EngineTuned || Changed > 0;

    /// <summary>The car's Tune screen rows against where each stands as the car came</summary>
    public static SetupLook Of(Car car, PartsCatalog catalog, IReadOnlyDictionary<string, SetupRange> ranges)
    {
        var rows = CarTune.Rows(car, catalog, CarSetup.For(car.Parts, car.Setup, catalog, ranges));
        var moved = rows.Where(r => !r.IsLocked && Math.Abs(r.Value - r.Stock) > 1e-9).ToList();
        bool Up(string item) => moved.Any(r => r.Item == item && r.Value > r.Stock);
        bool Down(string item) => moved.Any(r => r.Item == item && r.Value < r.Stock);

        var lowered = Down(CarSetup.RideHeightFront) || Down(CarSetup.RideHeightRear) || rows.Any(r => r.Item == "drop" && r.Value > 0)
            || LoweredByParts(car.Parts, catalog);
        return new SetupLook(lowered, Up(CarSetup.SteerLock), moved.Any(r => r.Tab == TuneTab.Engine), moved.Count);
    }

    /// <summary>A part on the car that drops it (lowering blocks, lowered coils), as it came from the shelf</summary>
    public static bool LoweredByParts(IEnumerable<PartInstance>? parts, PartsCatalog catalog) =>
        (parts ?? []).Where(p => p != null).SelectMany(p => p.SelfAndDescendants())
            .Any(p => catalog.Get(p.DefinitionId) is { } definition && RunningGear.Drop(definition) > 0);
}
