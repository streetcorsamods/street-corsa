namespace StreetCorsa.Parts.Cars;

/// <summary>
/// The tuning rules' limits on what a setup may do to a car's data (<c>"tuning"</c> in the rules, <c>TuningRules</c>):
/// how far its height may move in its travel (<see cref="CarStance"/>) and its steering lock grow or shrink
/// (<see cref="Export.AcSetupData.SteerRatio"/>). Linked into EngineBench, which has no rules: <see cref="Source"/> is
/// pointed at the career's rules by the game (<c>PartsWiring</c>); null there, which leaves the core's.
/// </summary>
public sealed record TuningLimits(double MostTravelLowered, double MostSteerGain)
{
    /// <summary>The core's limits, the ones TuningRules starts from</summary>
    public static readonly TuningLimits Core = new(0.5, 1.4);

    /// <summary>The rules in play; null: the core's</summary>
    public static Func<TuningLimits>? Source { get; set; }

    /// <summary>The limits in play</summary>
    public static TuningLimits Now => Source?.Invoke() ?? Core;
}
