using StreetCorsa.Parts.Export;

namespace StreetCorsa.Parts;

/// <summary>Where a car carries its engine, and which way it lies</summary>
public enum EngineLayout
{
    /// <summary>Ahead of the driver, behind the front axle, gearbox pointing back</summary>
    Front,

    /// <summary>Behind the driver, ahead of the rear axle, facing the way a front engine does: gearbox pointing back</summary>
    Mid,

    /// <summary>Behind the rear axle, turned around: gearbox pointing forward to the rear axle</summary>
    Rear,

    /// <summary>In the front, across the car (the crankshaft side to side) over the front axle, gearbox to the side: a front-wheel-drive car's</summary>
    FrontTransverse,

    /// <summary>Behind the driver, across the car ahead of the rear axle, gearbox to the side</summary>
    MidTransverse
}

public static class EngineLayouts
{
    /// <summary>Front weight share below which a car that drives its rear wheels carries its engine behind the driver</summary>
    public const double EngineBehindBelowFrontShare = 0.44;

    /// <summary>
    /// Where a car carries its engine, guessed from its own data, since AC's data never says: a front-wheel-drive car
    /// has it in the front, across the car as nearly every one does, and so has one whose weight is not mostly on the
    /// rear axle, lying along it. Behind the driver it is taken for a rear engine, not a mid one: from the weight alone
    /// the two look the same (a 911 and an Elise both carry about 40% at the front), and the rear engine is the one of
    /// the era the game is set in. The catalog editor (or cars.json) sets a mid-engined car right, and a front-wheel
    /// drive one whose engine lies along the car.
    /// </summary>
    public static EngineLayout Guess(AcCarSpecs? specs)
    {
        if (specs == null) return EngineLayout.Front;
        if (specs.Traction == "FWD") return EngineLayout.FrontTransverse;

        return specs.FrontWeightShare < EngineBehindBelowFrontShare ? EngineLayout.Rear : EngineLayout.Front;
    }

    /// <summary>The engine sits ahead of the driver, the nose carrying it</summary>
    public static bool IsFront(this EngineLayout layout) => layout is EngineLayout.Front or EngineLayout.FrontTransverse;

    /// <summary>The engine lies across the car, its crankshaft side to side</summary>
    public static bool IsTransverse(this EngineLayout layout) => layout is EngineLayout.FrontTransverse or EngineLayout.MidTransverse;

    /// <summary>The layout's name in content (cars.json): "front", "mid", "rear", "front_transverse", "mid_transverse"</summary>
    public static string Name(this EngineLayout layout) => layout switch
    {
        EngineLayout.FrontTransverse => "front_transverse",
        EngineLayout.MidTransverse => "mid_transverse",
        _ => layout.ToString().ToLowerInvariant()
    };

    /// <summary>What a person reads for the layout: "Front", "Front, across"</summary>
    public static string Label(this EngineLayout layout) => layout switch
    {
        EngineLayout.FrontTransverse => "Front, across",
        EngineLayout.MidTransverse => "Mid, across",
        _ => layout.ToString()
    };

    /// <summary>A layout by its name in content (<see cref="Name"/>), case and underscores aside; null for none</summary>
    public static EngineLayout? Parse(string? name) =>
        name is { Length: > 0 } && !name.Any(char.IsDigit) && Enum.TryParse<EngineLayout>(name.Replace("_", ""), ignoreCase: true, out var layout) && Enum.IsDefined(layout)
            ? layout
            : null;
}
