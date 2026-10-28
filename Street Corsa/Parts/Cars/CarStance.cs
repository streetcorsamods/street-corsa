using StreetCorsa.Helpers;
using StreetCorsa.Parts.Export;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// How high a car sits, the one rule for the race's data and for the scenes. Assetto Corsa moves an axle, not a corner
/// (one <c>ROD_LENGTH</c> per axle): its springs lower it by the mean of its corners' <c>drop</c> over the factory spring's
/// (the car's model already sits on the factory's), the player's ride height moves it, a broken spring's sag lets it
/// down, and all of it together is held to <see cref="MostTravelLowered"/> of the axle's travel (<c>PACKER_RANGE</c>),
/// past which the car would ride on its packers. The scenes show the same axle, each corner's own sag about it (a broken
/// spring leans the body).
/// </summary>
public static class CarStance
{
    /// <summary>The most of an axle's travel its height may move by, either way</summary>
    public const double MostTravelLowered = 0.5;

    /// <summary>An axle's travel in the data, metres: its <c>PACKER_RANGE</c>; null for none, or none that reads as metres</summary>
    public static double? Travel(IniText suspensions, string axle) =>
        suspensions.GetNumber(axle, "PACKER_RANGE") is { } travel && travel > 0 && travel < 1 ? travel : null;

    /// <summary>A move of an axle's height, mm, held to <see cref="MostTravelLowered"/> of its travel; as it is with no travel known</summary>
    public static double Held(double mm, double? travel)
    {
        if (!double.IsFinite(mm)) return 0;
        if (travel is not { } metres) return mm;
        var most = metres * MostTravelLowered / Unit.MetresPerMm;
        return Math.Clamp(mm, -most, most);
    }

    /// <summary>
    /// How far an axle's springs lower it from the factory's, mm (negative: lift): the mean of its corners' drop over the
    /// factory spring's, a corner without a spring counted as the factory's
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

        return drop / Math.Max(1, n);
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
            if (!axle.HasRodLength) continue;

            var first = front ? 0 : 2;
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
