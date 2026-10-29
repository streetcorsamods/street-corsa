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

        /// <summary>
        /// How far each corner sits from its height (front left, front right, rear left, rear right), metres, negative
        /// lower: a broken spring's corner sags (<see cref="Parts.Cars.CarLooks.Sag(GameState.Car)"/>). AC has no setter: the car's
        /// data carries it, an axle the mean of its corners.
        /// </summary>
        public double[] RideHeight { get; init; } = new double[4];

        /// <summary>
        /// Nm at the axle the rear axle's weakest part holds as it is (<see cref="Parts.Cars.RearAxle.Weakest"/>): the race
        /// mode breaks it when the rear tyres put down more. Null for a car without an axle of parts: nothing breaks.
        /// </summary>
        public double? AxleStrength { get; init; }

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
            BodyKmh.All(z => z <= 0) && EngineLife >= 1000 && GearboxWear <= 0 && SuspensionBend.All(b => b <= 0) && RideHeight.All(h => h == 0);

        /// <summary>The race.ini keys for the car at <paramref name="carIndex"/>, in AC's invariant number format</summary>
        public IEnumerable<(string Key, string Value)> IniKeys(int carIndex, ICollection<string>? leftOut = null)
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
            if (AxleStrength is { } axle && double.IsFinite(axle) && axle > 0)
                yield return (prefix + "AXLE", Math.Min(axle, 1e6).ToString("0", CultureInfo.InvariantCulture));
            if (Cooling is { } cooling)
                yield return (prefix + "COOLING", List([cooling.Cooling, cooling.Fan, cooling.OilG], "0.000"));
            foreach (var key in ShownKeys(carIndex, Shown, leftOut)) yield return key;
        }

        /// <summary>
        /// The race.ini keys of what the car at <paramref name="carIndex"/> shows (the race mode's and the free run's
        /// car_parts.lua): per wheel the rim's model, and the tyre's with where it sits on the rim; per engine part
        /// standing out (CAR_n_PART_1, _2...) its model and where it sits in the car's model
        /// </summary>
        public static IEnumerable<(string Key, string Value)> ShownKeys(int carIndex, Parts.Cars.ShownSet? shown, ICollection<string>? leftOut = null)
        {
            if (shown == null) yield break;
            var prefix = $"CAR_{carIndex}_";
            // A name from the car's own files (a node, a texture) goes in as it is, so one that would break its line of
            // race.ini is left out (told in leftOut) rather than let it add or change a key; the mode then does without it
            if (shown.Frame is { } frame && Fits(frame.Node, carIndex, "model node", leftOut))
            {
                yield return (prefix + "MODEL_NODE", frame.Node);
                yield return (prefix + "MODEL_AT", Matrix(frame.At));
            }
            if (shown.Layer is { } layer && Fits(layer.Name, carIndex, "look", leftOut)) yield return (prefix + "LOOK", layer.Name);
            // The mode reads the paints and the parts up to the first missing number: one left out is no gap
            var paints = shown.PaintFiles.Where(p => Fits(p.File.Name, carIndex, "paint file", leftOut) && Fits(p.Texture, carIndex, "paint texture", leftOut)).ToList();
            for (var i = 0; i < paints.Count; i++)
            {
                // Each tinted texture, and the car's texture it takes the place of
                var paint = paints[i];
                yield return (prefix + "PAINT_" + (i + 1), paint.File.Name);
                yield return (prefix + "PAINT_" + (i + 1) + "_TEXTURE", paint.Texture);
            }
            var extras = shown.Extras.Where(e => Fits(e.Model.Name, carIndex, "part model", leftOut)).ToList();
            for (var i = 0; i < extras.Count; i++)
            {
                var extra = extras[i];
                yield return (prefix + "PART_" + (i + 1), extra.Model.Name);
                yield return (prefix + "PART_" + (i + 1) + "_AT", Matrix(extra.At));
                yield return (prefix + "PART_" + (i + 1) + "_BOX",
                    List([extra.Min.X, extra.Min.Y, extra.Min.Z, extra.Max.X, extra.Max.Y, extra.Max.Z], "0.######"));
            }
            var wheels = shown.Wheels;
            for (var corner = 0; corner < Math.Min(wheels.Count, Parts.Cars.WheelFit.WheelNodes.Length); corner++)
            {
                if (wheels[corner] is not { } wheel || !Fits(wheel.Rim.Name, carIndex, "rim model", leftOut)) continue;
                // AC's own names for the wheels, in the same order
                var name = Parts.Cars.WheelFit.WheelNodes[corner]["WHEEL_".Length..];
                yield return (prefix + "RIM_" + name, wheel.Rim.Name);
                if (wheel.Tyre is not { } tyre || !Fits(tyre.Name, carIndex, "tyre model", leftOut)) continue;
                yield return (prefix + "TYRE_" + name, tyre.Name);
                yield return (prefix + "TYRE_" + name + "_AT", Matrix(wheel.TyreOnRim));
                if (wheel.TyreOptional) yield return (prefix + "TYRE_" + name + "_OPTIONAL", "1");
            }
        }

        /// <summary>
        /// Nothing in <paramref name="value"/> could end its line, open a section, start a comment or make it a key of its
        /// own: it goes into a cfg file as it is
        /// </summary>
        public static bool IsIniValue(string value) => !value.Any(c => char.IsControl(c) || c is '[' or ']' or '=' or ';');

        /// <summary>The name can go into race.ini as it is; else it is told in <paramref name="leftOut"/></summary>
        private static bool Fits(string? value, int carIndex, string what, ICollection<string>? leftOut)
        {
            if (value != null && IsIniValue(value)) return true;

            leftOut?.Add($"car {carIndex}: the {what} '{value}'");
            return false;
        }

        /// <summary>A matrix as race.ini carries it: its rows of 4x3, the last column left out</summary>
        private static string Matrix(System.Numerics.Matrix4x4 m) =>
            List([m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33, m.M41, m.M42, m.M43], "0.######");

        private static string List(double[] values, string format) =>
            string.Join(",", values.Select(v => (double.IsFinite(v) ? v : 0).ToString(format, CultureInfo.InvariantCulture)));
    }
}
