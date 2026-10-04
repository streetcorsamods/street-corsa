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
        /// What the car shows that its model does not have (its wheels, the engine parts that stand out); the race mode
        /// puts it on the car (see <see cref="Parts.Cars.ShownParts"/>). Null shows the car as it is.
        /// </summary>
        public Parts.Cars.ShownSet? Shown { get; init; }

        /// <summary>The models the race mode loads for the car, once each</summary>
        public IEnumerable<Parts.Cars.ShownModel> Models => Shown?.Models ?? [];

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
            foreach (var key in ShownKeys(carIndex, Shown)) yield return key;
        }

        /// <summary>
        /// The race.ini keys of what the car at <paramref name="carIndex"/> shows (the race mode's and the free run's
        /// car_parts.lua): per wheel the rim's model, and the tyre's with where it sits on the rim; per engine part
        /// standing out (CAR_n_PART_1, _2...) its model and where it sits in the car's model
        /// </summary>
        public static IEnumerable<(string Key, string Value)> ShownKeys(int carIndex, Parts.Cars.ShownSet? shown)
        {
            if (shown == null) yield break;
            var prefix = $"CAR_{carIndex}_";
            for (var i = 0; i < shown.Extras.Count; i++)
            {
                var extra = shown.Extras[i];
                yield return (prefix + "PART_" + (i + 1), extra.Model.Name);
                yield return (prefix + "PART_" + (i + 1) + "_AT", Matrix(extra.At));
                yield return (prefix + "PART_" + (i + 1) + "_BOX",
                    List([extra.Min.X, extra.Min.Y, extra.Min.Z, extra.Max.X, extra.Max.Y, extra.Max.Z], "0.######"));
            }
            var wheels = shown.Wheels;
            for (var corner = 0; corner < Math.Min(wheels.Count, Parts.Cars.WheelFit.WheelNodes.Length); corner++)
            {
                if (wheels[corner] is not { } wheel) continue;
                // AC's own names for the wheels, in the same order
                var name = Parts.Cars.WheelFit.WheelNodes[corner]["WHEEL_".Length..];
                yield return (prefix + "RIM_" + name, wheel.Rim.Name);
                if (wheel.Tyre is not { } tyre) continue;
                yield return (prefix + "TYRE_" + name, tyre.Name);
                yield return (prefix + "TYRE_" + name + "_AT", Matrix(wheel.TyreOnRim));
                if (wheel.TyreOptional) yield return (prefix + "TYRE_" + name + "_OPTIONAL", "1");
            }
        }

        /// <summary>A matrix as race.ini carries it: its rows of 4x3, the last column left out</summary>
        private static string Matrix(System.Numerics.Matrix4x4 m) =>
            List([m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33, m.M41, m.M42, m.M43], "0.######");

        private static string List(double[] values, string format) =>
            string.Join(",", values.Select(v => (double.IsFinite(v) ? v : 0).ToString(format, CultureInfo.InvariantCulture)));
    }
}
