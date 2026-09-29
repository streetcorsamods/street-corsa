using StreetCorsa.Services.Street;

namespace StreetCorsa.Services.Configuration
{
    /// <summary>
    /// Where AC's sun stands at a time of the game, from the same seasons the street screens are lit by
    /// (<see cref="StreetLights"/>): a December race at 17:30 is after sunset in AC as it is in the street.
    ///
    /// AC's angle is Content Manager's formula, 0 at 13:00 and 16 degrees an hour, so -80 is 08:00 and 80 is 18:00.
    /// AC alone stops there; with Custom Shaders Patch the angle goes on through the evening and the night (CM writes
    /// it from 00:00 up to 23:59), which is how a race after dark is dark. The game's clock goes into that formula
    /// with its afternoon stretched or squeezed so the day's sunset lands where AC has it on a day of the model's mean
    /// sunset: the morning is as it was (the model's sunrise does not move), 13:00 stays 0, midnight stays midnight,
    /// and on a day whose sunset is the mean the angle is the formula's own.
    /// </summary>
    public static class SeasonalSun
    {
        /// <summary>Degrees of sun angle an hour, and the hour at which it is 0</summary>
        private const double DegreesPerHour = 16.0, ZeroHour = 13.0;

        /// <summary>The angles CM writes: 00:00 and 23:59</summary>
        public const double MinAngle = DegreesPerHour * (0 - ZeroHour), MaxAngle = DegreesPerHour * (23 + 59 / 60.0 - ZeroHour);

        /// <summary>
        /// The street model's sunset over a whole year, at which the game's clock goes into CM's formula unchanged.
        /// Worked out from the model itself, so a change there follows here.
        /// </summary>
        public static readonly double MeanSunset =
            Enumerable.Range(0, 365).Average(day => StreetLights.SunsetHour(new DateTime(2001, 1, 1).AddDays(day)));

        /// <summary>The hour AC's clock shows for the game's <paramref name="time"/>, in its season</summary>
        public static double AcHour(DateTime time)
        {
            var hour = time.TimeOfDay.TotalHours;
            if (hour <= ZeroHour) return hour;

            var sunset = StreetLights.SunsetHour(time);
            return hour <= sunset
                ? ZeroHour + (hour - ZeroHour) * (MeanSunset - ZeroHour) / (sunset - ZeroHour)
                : MeanSunset + (hour - sunset) * (24 - MeanSunset) / (24 - sunset);
        }

        /// <summary>AC's SUN_ANGLE for the game's <paramref name="time"/>, to the hundredth, within what CM writes</summary>
        public static double AcSunAngle(DateTime time) =>
            Math.Round(Math.Clamp(DegreesPerHour * (AcHour(time) - ZeroHour), MinAngle, MaxAngle), 2);
    }
}
