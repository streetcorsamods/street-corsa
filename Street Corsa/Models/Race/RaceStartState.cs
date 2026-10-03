using System.Globalization;
using StreetCorsa.Helpers;

namespace StreetCorsa.Models.Race
{
    /// <summary>
    /// How a car's engine is cooled and oiled, as the race mode runs its heat and oil (race.ini <c>CAR_n_COOLING</c>,
    /// see <see cref="Parts.Cars.EngineCooling"/>)
    /// </summary>
    /// <param name="Cooling">What the radiator and water pump carry off over what the engine makes: 1 is a factory
    /// engine at full throttle, carried off at speed with a little to spare</param>
    /// <param name="Fan">The share of that the fan keeps up at a standstill</param>
    /// <param name="OilG">The g the sump holds its oil to</param>
    /// <remarks>A car the career did not rate races as a factory car: mode.lua's own defaults, which are
    /// <see cref="Parts.Cars.EngineCooling.StockFan"/> and <see cref="Parts.Cars.EngineCooling.StockSumpG"/></remarks>
    public sealed record EngineCoolingRating(double Cooling, double Fan, double OilG);

    /// <summary>
    /// The shape a car goes into a race in, as the race mode puts it into AC at the start: what the last races left
    /// of it. Written into race.ini under [STREET_CORSA] as <c>CAR_n_*</c> keys, n being AC's index of the car.
    /// </summary>
    public sealed record RaceStartState
    {
        /// <summary>How far AC bends a steering rod at most, in metres: MAX_DAMAGE in suspensions.ini, 0.05 on every car the game has</summary>
        public const double MaxSuspensionBendMetres = 0.05;

        /// <summary>Front, rear, left, right: collision speed in km/h, as AC keeps body damage</summary>
        public double[] BodyKmh { get; init; } = new double[4];

        /// <summary>1000 for a new engine, as AC counts it</summary>
        public double EngineLife { get; init; } = 1000;

        /// <summary>0 for a gearbox in good shape, 1 for one that no longer works. AC has no setter: the mode adds it to what the race does.</summary>
        public double GearboxWear { get; init; }

        /// <summary>
        /// How bent each corner already is (front left, front right, rear left, rear right), 0 straight to 1 as far as
        /// AC bends a steering rod. AC has no setter either: the car's data carries the toe it gives, and the mode adds it
        /// to what the race does.
        /// </summary>
        public double[] SuspensionBend { get; init; } = new double[4];

        /// <summary>Litres in the tank; null fills it</summary>
        public double? FuelLitres { get; init; }

        /// <summary>The body's dirt, 0 clean to 1 filthy</summary>
        public double Dirt { get; init; }

        /// <summary>How the engine is cooled and oiled; null leaves the race mode to take a factory car's</summary>
        public EngineCoolingRating? Cooling { get; init; }

        /// <summary>
        /// The wheel each corner shows (front left, front right, rear left, rear right), null where the car keeps its own;
        /// the race mode puts it on the car (see <see cref="Parts.Cars.ShownParts"/>)
        /// </summary>
        public Parts.Cars.ShownWheel?[] Wheels { get; init; } = new Parts.Cars.ShownWheel?[4];

        /// <summary>The models the race mode loads for the car, once each</summary>
        public IEnumerable<Parts.Cars.ShownModel> Models => Wheels.OfType<Parts.Cars.ShownWheel>()
            .SelectMany(w => w.Tyre is { } tyre ? new[] { w.Rim, tyre } : [w.Rim])
            .DistinctBy(m => m.Name, StringComparer.OrdinalIgnoreCase);

        /// <summary>Nothing wrong with the car</summary>
        public bool IsPristine =>
            BodyKmh.All(z => z <= 0) && EngineLife >= 1000 && GearboxWear <= 0 && SuspensionBend.All(b => b <= 0);

        /// <summary>The race.ini keys for the car at <paramref name="carIndex"/>, in AC's invariant number format</summary>
        public IEnumerable<(string Key, string Value)> IniKeys(int carIndex)
        {
            var prefix = $"CAR_{carIndex}_";
            yield return (prefix + "BODY", List(BodyKmh, "0.0"));
            // Not a number is a car nothing is known to be wrong with: a new engine, a gearbox in good shape
            yield return (prefix + "ENGINE_LIFE", Math.Clamp(double.IsFinite(EngineLife) ? EngineLife : 1000, 0, 1000).ToString("0.0", CultureInfo.InvariantCulture));
            yield return (prefix + "GEARBOX", Unit.Clamp01(GearboxWear, ifNotFinite: 0).ToString("0.000", CultureInfo.InvariantCulture));
            yield return (prefix + "SUSPENSION", List(SuspensionBend, "0.000"));
            yield return (prefix + "FUEL", FuelLitres is { } fuel && double.IsFinite(fuel)
                ? Math.Max(0, fuel).ToString("0.00", CultureInfo.InvariantCulture)
                : "-1");
            yield return (prefix + "DIRT", Unit.Clamp01(Dirt, ifNotFinite: 0).ToString("0.000", CultureInfo.InvariantCulture));
            if (Cooling is { } cooling)
                yield return (prefix + "COOLING", List([cooling.Cooling, cooling.Fan, cooling.OilG], "0.000"));
            // AC's own names for the wheels, in the same order
            string[] wheels = ["LF", "RF", "LR", "RR"];
            for (var corner = 0; corner < Math.Min(Wheels.Length, wheels.Length); corner++)
            {
                if (Wheels[corner] is not { } wheel) continue;
                yield return (prefix + "RIM_" + wheels[corner], wheel.Rim.Name);
                if (wheel.Tyre is not { } tyre) continue;
                yield return (prefix + "TYRE_" + wheels[corner], tyre.Name);
                var m = wheel.TyreOnRim;
                yield return (prefix + "TYRE_" + wheels[corner] + "_AT",
                    List([m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33, m.M41, m.M42, m.M43], "0.######"));
            }
        }

        private static string List(double[] values, string format) =>
            string.Join(",", values.Select(v => (double.IsFinite(v) ? v : 0).ToString(format, CultureInfo.InvariantCulture)));
    }
}
