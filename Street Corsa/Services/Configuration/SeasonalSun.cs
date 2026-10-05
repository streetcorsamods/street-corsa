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
        /// The street model's sunset over a whole year, at which the game's clock goes into CM's formula unchanged: the
        /// model swings its sunset evenly either side of the mean the rules give (<see cref="StreetLights.SunsetHour"/>)
        /// </summary>
        public static double MeanSunset => Rules.RuleBook.Current.World.MeanSunset;

        /// <summary>
        /// The earliest and latest a sunset is taken at: the afternoon is stretched between 13:00 and the sunset and the
        /// evening between the sunset and midnight, so a sunset of the rules' at either (or past them) would divide by nothing
        /// </summary>
        private const double EarliestSunset = ZeroHour + 0.5, LatestSunset = 23.5;

        /// <summary>The hour AC's clock shows for the game's <paramref name="time"/>, in its season</summary>
        public static double AcHour(DateTime time)
        {
            var hour = time.TimeOfDay.TotalHours;
            return hour <= ZeroHour ? hour : AcHour(hour, StreetLights.SunsetHour(time), MeanSunset);
        }

        /// <summary>The hour AC's clock shows for <paramref name="hour"/> of a day whose sunset is <paramref name="sunset"/>, the year's mean <paramref name="mean"/></summary>
        public static double AcHour(double hour, double sunset, double mean)
        {
            if (hour <= ZeroHour) return hour;

            // A sunset that is no number leaves the clock as it is
            if (!double.IsFinite(sunset) || !double.IsFinite(mean)) return hour;

            sunset = Math.Clamp(sunset, EarliestSunset, LatestSunset);
            mean = Math.Clamp(mean, EarliestSunset, LatestSunset);
            return hour <= sunset
                ? ZeroHour + (hour - ZeroHour) * (mean - ZeroHour) / (sunset - ZeroHour)
                : mean + (hour - sunset) * (24 - mean) / (24 - sunset);
        }

        /// <summary>AC's SUN_ANGLE for the game's <paramref name="time"/>, to the hundredth, within what CM writes; noon's when it is no number</summary>
        public static double AcSunAngle(DateTime time) => SunAngleAt(AcHour(time));

        /// <summary>CM's SUN_ANGLE for AC's clock at <paramref name="acHour"/>; noon's when it is no number</summary>
        public static double SunAngleAt(double acHour)
        {
            var angle = DegreesPerHour * (acHour - ZeroHour);
            return double.IsFinite(angle) ? Math.Round(Math.Clamp(angle, MinAngle, MaxAngle), 2) : IniModificationService.NoonSunAngle;
        }
    }
}
