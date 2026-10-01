using System.Globalization;

namespace StreetCorsa.Helpers;

/// <summary>The two ways of measuring: the game picks one in its game.json, the player may pick the other</summary>
public enum UnitSystem
{
    /// <summary>mph, miles, US gallons and quarts, °F, lb, lb-ft</summary>
    Imperial,

    /// <summary>km/h, km, litres, °C, kg, N·m</summary>
    Metric
}

/// <summary>
/// How the money is written: the game's currency, from game.json's <c>locale.money</c>. Amounts are in the game's
/// own currency (a yen game writes yen in its rules); nothing is converted.
/// </summary>
public sealed class MoneyStyle
{
    /// <summary>The currency's sign: "$", "¥", "€"</summary>
    public string Symbol { get; set; } = "$";

    /// <summary>The sign after the amount ("1.234 €") rather than before it ("$1,234")</summary>
    public bool After { get; set; }

    /// <summary>A space between the sign and the amount</summary>
    public bool Space { get; set; }

    /// <summary>The decimals shown: 0 for whole dollars</summary>
    public int Decimals { get; set; }
}

/// <summary>
/// The game's ways of writing numbers, money, units and dates, set at start-up from game.json's <c>locale</c> and the
/// player's units. The program itself keeps US English (<see cref="CultureInfo.CurrentCulture"/>) for every file it
/// reads and writes; only what the player reads goes through <see cref="Culture"/>.
/// </summary>
public static class Locale
{
    private static CultureInfo _culture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>The culture the player reads numbers and dates in: "8:00 AM", "June 3, 1971", "1,234.5"</summary>
    public static CultureInfo Culture
    {
        get => _culture;
        set => _culture = value ?? CultureInfo.GetCultureInfo("en-US");
    }

    /// <summary>How money is written</summary>
    public static MoneyStyle Money { get; set; } = new();

    /// <summary>The units the player reads: the player's pick, else the game's</summary>
    public static UnitSystem Units { get; set; } = UnitSystem.Imperial;

    /// <summary>
    /// A figure in its unit ("104 mph") from the unit's id ("mph") and the number already written; in the game, the
    /// text pool <c>unit.&lt;id&gt;</c>
    /// </summary>
    public static Func<string, string, string> UnitWords { get; set; } = (unit, number) => $"{number} {unit}";

    /// <summary>A unit's name on its own ("mph"), for a column's heading; in the game, the pool <c>unit.&lt;id&gt;.name</c></summary>
    public static Func<string, string> UnitName { get; set; } = unit => unit;

    /// <summary>
    /// A culture by name ("en-US", "ja-JP"); null for a name the machine does not know. Only the cultures .NET lists
    /// count (ICU would make one up for "qq-QQ" or "usa"), and only a name WPF takes as a language too ("en_US" is
    /// neither): the screens' bindings write in it.
    /// </summary>
    public static CultureInfo? FindCulture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            var culture = CultureInfo.GetCultureInfo(name.Trim(), predefinedOnly: true);
            if (culture.Equals(CultureInfo.InvariantCulture)) return null;
            System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag);
            return culture;
        }
        catch (Exception ex) when (ex is CultureNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>"imperial" or "metric", whatever the case; null for anything else</summary>
    public static UnitSystem? ParseUnits(string? name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            "imperial" => UnitSystem.Imperial,
            "metric" => UnitSystem.Metric,
            _ => null
        };
}
