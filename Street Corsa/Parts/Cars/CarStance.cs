using StreetCorsa.Helpers;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// How high a car sits, the one rule for the race's data and for the scenes. Assetto Corsa moves an axle, not a corner
/// (one <c>ROD_LENGTH</c> per axle): its springs lower it by the mean of its corners' <c>drop</c> over the factory spring's
/// (the car's model already sits on the factory's), the player's ride height moves it, a broken spring's sag lets it
/// down, and all of it together is held to <see cref="Rules.TuningRules.MostTravelLowered"/> of the axle's travel
/// (<c>PACKER_RANGE</c>), past which the car would ride on its packers. The scenes show the same axle, each corner's own
/// sag about it (a broken spring leans the body); an axle without a <c>ROD_LENGTH</c> nothing moves, in the race or in
/// the scenes. A car that cannot drive goes to no race: it shows how it would sit once it runs.
/// </summary>
public static class CarStance
{
    /// <summary>A drop smaller than this, mm, is none: the data is left as its author wrote it</summary>
    public const double LeastDrop = 0.5;

    /// <summary>An axle's travel in the data, metres: its <c>PACKER_RANGE</c>; null for none, or none that reads as metres</summary>
    public static double? Travel(IniText suspensions, string axle) =>
        suspensions.GetNumber(axle, "PACKER_RANGE") is { } travel && travel > 0 && travel < 1 ? travel : null;

    /// <summary>How far an axle's height may move either way, mm (<see cref="Rules.TuningRules.MostTravelLowered"/> of its travel); null with no travel known</summary>
    public static double? Most(double? travel) =>
        travel is { } metres ? metres * Rules.RuleBook.Current.Tuning.MostTravelLowered / Unit.MetresPerMm : null;

    /// <summary>A move of an axle's height, mm, held to <see cref="Most"/>; as it is with no travel known</summary>
    public static double Held(double mm, double? travel)
    {
        if (!double.IsFinite(mm)) return 0;
        return Most(travel) is { } most ? Math.Clamp(mm, -most, most) : mm;
    }

    /// <summary>
    /// How far an axle's springs lower it from the factory's, mm (negative: lift): the mean of its corners' drop over the
    /// factory spring's, a corner without a spring counted as the factory's; under <see cref="LeastDrop"/> none
    /// </summary>
    public static double Drop(PartsCatalog catalog, IEnumerable<CornerParts> axle, PartDefinition factorySpring)
    {
        var factory = RunningGear.Drop(factorySpring);
        double drop = 0;
        var n = 0;
        foreach (var corner in axle)
        {
            n++;
            if (RunningGear.Definition(catalog, corner.Spring) != null) drop += RunningGear.Drop(catalog, corner.Spring) - factory;
        }

        drop /= Math.Max(1, n);
        return Math.Abs(drop) >= LeastDrop ? drop : 0;
    }

    /// <summary>What sets a car's height, for <see cref="Of"/></summary>
    /// <param name="Sag">Each corner's sag, metres (<see cref="CarLooks.Sag(Models.GameState.Car)"/>)</param>
    /// <param name="Corners">What is on the wheel slots; null when the car races on its data's running gear</param>
    /// <param name="Factory">The running gear the data describes; null when the race does not change the running gear</param>
    /// <param name="RideHeight">The setup's ride height front and rear, mm (<see cref="CarSetup.RideHeights"/>)</param>
    /// <param name="Specs">The car's data; null when it cannot be read (the car shows its sag alone)</param>
    public readonly record struct Inputs(double[] Sag, CornerParts[]? Corners, (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? Factory,
        (double Front, double Rear) RideHeight, AcCarSpecs? Specs);

    /// <summary>
    /// How far each corner sits from where the car's model has it, metres, AC's wheel order (negative: lower), as the race
    /// will have it
    /// </summary>
    public static double[] Of(PartsCatalog catalog, Inputs inputs)
    {
        var heights = new double[RunningGear.Corners];
        for (var i = 0; i < heights.Length && i < inputs.Sag.Length; i++) heights[i] = double.IsFinite(inputs.Sag[i]) ? inputs.Sag[i] : 0;
        if (inputs.Specs is not { } specs) return heights;

        foreach (var front in new[] { true, false })
        {
            var axle = specs.Axle(front);
            var first = front ? 0 : 2;

            // The race moves no axle without a ROD_LENGTH, the sag neither (AcDamageData)
            if (!axle.HasRodLength)
            {
                heights[first] = heights[first + 1] = 0;
                continue;
            }

            var drop = inputs is { Corners: { Length: RunningGear.Corners } corners, Factory: var (frontFactory, rearFactory) }
                ? Drop(catalog, corners.Skip(first).Take(2), (front ? frontFactory : rearFactory).Spring)
                : 0;
            var sag = (heights[first] + heights[first + 1]) / 2;
            var mm = (front ? inputs.RideHeight.Front : inputs.RideHeight.Rear) - drop + sag / Unit.MetresPerMm;
            var by = Held(mm, axle.PackerRange) * Unit.MetresPerMm - sag;
            heights[first] += by;
            heights[first + 1] += by;
        }

        return heights;
    }
}
