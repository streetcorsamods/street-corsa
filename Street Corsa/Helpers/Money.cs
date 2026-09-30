namespace StreetCorsa.Helpers;

/// <summary>Sums of money as the player reads them</summary>
public static class Money
{
    /// <summary>Whole dollars with the sign in front: "$1,234", "-$1,234" (not "$-1,234")</summary>
    public static string Dollars(decimal amount) => amount < 0 ? $"-${-amount:N0}" : $"${amount:N0}";
}
