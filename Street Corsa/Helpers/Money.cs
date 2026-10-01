using System.Globalization;

namespace StreetCorsa.Helpers;

/// <summary>
/// A sum of money as the player reads it, in the game's currency (<see cref="Locale.Money"/>): "$1,234", "-$1,234"
/// (not "$-1,234"), "1.234 €". Handed to the text as a value it writes itself: <c>{price}</c>; <c>{price:N2}</c>
/// writes it with that number format instead of the currency's decimals.
/// </summary>
public readonly record struct Money(decimal Amount) : IFormattable
{
    /// <summary>The amount written with the currency's sign</summary>
    public static string Text(decimal amount) => Write(amount, null);

    /// <summary>The amount written with the currency's sign and that many decimals</summary>
    public static string Text(decimal amount, int decimals) => Write(amount, "N" + Math.Clamp(decimals, 0, 6));

    /// <summary>The amount alone, without the currency's sign: "1,234", "-1,234"; a number format in place of the currency's decimals</summary>
    public string Number(string? format = null) => (Amount < 0 ? "-" : string.Empty) + Digits(Amount, format);

    public override string ToString() => Write(Amount, null);

    public string ToString(string? format, IFormatProvider? formatProvider) => Write(Amount, format);

    private static string Write(decimal amount, string? format)
    {
        var style = Locale.Money;
        var number = Digits(amount, format);
        var space = style.Space ? " " : string.Empty;
        var written = style.After ? number + space + style.Symbol : style.Symbol + space + number;
        return amount < 0 ? "-" + written : written;
    }

    private static string Digits(decimal amount, string? format) =>
        Math.Abs(amount).ToString(string.IsNullOrWhiteSpace(format) ? "N" + Math.Clamp(Locale.Money.Decimals, 0, 6) : format, Locale.Culture);
}
