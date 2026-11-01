using System.Text.Json.Serialization;

namespace StreetCorsa.Rules;

/// <summary>How a new career starts (<c>"start"</c> in the rules)</summary>
public sealed class StartRules
{
    /// <summary>The player's money on the first day</summary>
    public decimal Money { get; set; } = 10_000m;

    /// <summary>
    /// The first day of the career; it starts at the day's first hour. None: the day the career is started on (the
    /// core's; a setting names its own)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? Date { get; set; }

    /// <summary>
    /// The cars in the player's garage on the first day, the first of them the one they drive; none in the core
    /// (the player buys their first). A car Assetto Corsa does not have is left out.
    /// </summary>
    public StartCar[] Cars { get; set; } = [];

    /// <summary>The career's first moment: <see cref="Date"/> (else today) at the day's first hour</summary>
    public DateTime FirstMorning(DayRules day) => (Date ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(new TimeOnly(day.FirstHour, 0));

    /// <summary>Each figure the game divides by, loops over or pays held to what it can play by (<see cref="Figures"/>)</summary>
    internal void Check(string path, List<string> problems)
    {
        Figures.Money(this, path, problems, (double)Parts.Cars.CarBook.MaxCarPrice, nameof(Money));
    }
}

/// <summary>A car the player starts with: its factory parts, in the shape given</summary>
public sealed class StartCar
{
    /// <summary>The AC car (its folder under content\cars)</summary>
    public string Car { get; set; } = string.Empty;

    /// <summary>Its skin; empty for the car's default one (left out of the rules written then: a null kept in the save would read back as a mistake)</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Skin { get; set; }

    /// <summary>How good a shape it is in, 0 to 1</summary>
    public double Condition { get; set; } = 0.7;

    /// <summary>Kilometres on the clock</summary>
    public double Mileage { get; set; } = 60000;
}

/// <summary>The player's day (<c>"day"</c> in the rules)</summary>
public sealed class DayRules
{
    /// <summary>The hour each day starts at: the morning after the day is over</summary>
    public int StartHour { get; set; } = 8;

    /// <summary>The hour the day is over: whatever takes the time past it goes on into the next morning</summary>
    public int EndHour { get; set; } = 22;

    /// <summary><see cref="StartHour"/> held to a real hour, and before <see cref="EndHour"/></summary>
    [JsonIgnore]
    public int FirstHour => Math.Clamp(StartHour, 0, 23);

    /// <summary><see cref="EndHour"/> held to after the first hour, 24 at the latest (midnight)</summary>
    [JsonIgnore]
    public int LastHour => Math.Clamp(EndHour, FirstHour + 1, 24);
}
