namespace StreetCorsa.Rules;

/// <summary>
/// Where and when the game is set (<c>"world"</c> in the rules): when the sun sets through the year, the seasons the
/// rivals race in, and the weather and the place Assetto Corsa is told of for a race
/// </summary>
public sealed class WorldRules
{
    /// <summary>
    /// The sunset over the year, hours: the mean, how far it swings either side, and the day of the year it is latest
    /// (midsummer; about 355 for the south). The core's is the middle of the States: about 16:25 at the turn of
    /// the year, 20:10 at midsummer.
    /// </summary>
    public double MeanSunset { get; set; } = 18.3;

    public double SunsetSwing { get; set; } = 1.9;

    public int Midsummer { get; set; } = 172;

    /// <summary>The street is dark until this hour, and in dusk light until <see cref="DayFromHour"/></summary>
    public double DawnHour { get; set; } = 5.5;

    public double DayFromHour { get; set; } = 7;

    /// <summary>The months the rivals race more in (summer) and less in (winter), and by how much</summary>
    public int[] SummerMonths { get; set; } = [6, 7, 8];

    public double SummerRacing { get; set; } = 1.3;

    public int[] WinterMonths { get; set; } = [12, 1, 2];

    public double WinterRacing { get; set; } = 0.7;

    /// <summary>AC's weather for a race (a folder of content\weather), and the weather type Content Manager gives it</summary>
    public string Weather { get; set; } = "4_mid_clear";

    public int WeatherType { get; set; } = 19;

    /// <summary>The air and the road, degrees Celsius</summary>
    public int AmbientC { get; set; } = 12;

    public int RoadC { get; set; } = 13;

    /// <summary>The wind, km/h, least to most</summary>
    public int WindMinKmh { get; set; } = 2;

    public int WindMaxKmh { get; set; } = 40;

    /// <summary>Where on earth AC puts the track for its sky (degrees) and its time zone (seconds from UTC)</summary>
    public double Latitude { get; set; } = 47.9602777777778;

    public double Longitude { get; set; } = 0.231944444444444;

    public int TimeZoneOffset { get; set; } = 3600;
}

/// <summary>How a race is run in Assetto Corsa (<c>"race"</c> in the rules)</summary>
public sealed class RaceRules
{
    /// <summary>Mechanical and body damage in a race, in percent: AC's full rate, which an easy game turns down</summary>
    public int Damage { get; set; } = 100;

    /// <summary>Tyre wear in a race: 1 is AC's normal rate</summary>
    public int TyreWear { get; set; } = 1;

    /// <summary>Fuel use in a race: 1 is AC's normal rate. The fuel a race burns stays burned.</summary>
    public int FuelRate { get; set; } = 1;

    /// <summary>How the police drive: flat out, and not shy of contact (the race mode takes them further once they are out)</summary>
    public int PoliceAILevel { get; set; } = 100;

    public int PoliceAIAggression { get; set; } = 100;

    /// <summary>
    /// Each figure held to what goes into AC's files: the damage is a percentage (clamped against it at the launch,
    /// so below 0 it would throw there), the rates are multipliers, the police's AI is race.ini's level and aggression
    /// </summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Within(this, path, nameof(Damage), 0, 200, problems);
        Figures.Within(this, path, nameof(TyreWear), 0, 100, problems);
        Figures.Within(this, path, nameof(FuelRate), 0, 100, problems);
        Figures.Within(this, path, nameof(PoliceAILevel), 0, 200, problems);
        Figures.Within(this, path, nameof(PoliceAIAggression), 0, 100, problems);
    }
}
