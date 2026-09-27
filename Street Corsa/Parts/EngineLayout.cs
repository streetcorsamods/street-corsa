using StreetCorsa.Parts.Export;

namespace StreetCorsa.Parts;

/// <summary>Where a car carries its engine</summary>
public enum EngineLayout
{
    /// <summary>Ahead of the driver, behind the front axle, gearbox pointing back</summary>
    Front,

    /// <summary>Behind the driver, ahead of the rear axle, facing the way a front engine does: gearbox pointing back</summary>
    Mid,

    /// <summary>Behind the rear axle, turned around: gearbox pointing forward to the rear axle</summary>
    Rear
}

public static class EngineLayouts
{
    /// <summary>Front weight share below which a car that drives its rear wheels carries its engine behind the driver</summary>
    public const double EngineBehindBelowFrontShare = 0.44;

    /// <summary>
    /// Where a car carries its engine, guessed from its own data, since AC's data never says: a front-wheel-drive car
    /// has it in the front, and so has one whose weight is not mostly on the rear axle. Behind the driver it is taken
    /// for a rear engine, not a mid one: from the weight alone the two look the same (a 911 and an Elise both carry
    /// about 40% at the front), and the rear engine is the one of the era the game is set in. The catalog editor sets
    /// a mid-engined car right.
    /// </summary>
    public static EngineLayout Guess(AcCarSpecs? specs)
    {
        if (specs == null || specs.Traction == "FWD") return EngineLayout.Front;

        return specs.FrontWeightShare < EngineBehindBelowFrontShare ? EngineLayout.Rear : EngineLayout.Front;
    }
}
