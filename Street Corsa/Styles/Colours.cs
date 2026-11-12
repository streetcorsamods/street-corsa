using Color = System.Windows.Media.Color;

namespace StreetCorsa.Styles;

/// <summary>
/// Colour text from a mod ("#RRGGBB", a named colour) read the one way: a theme's colours and a crew's alike.
/// </summary>
public static class Colours
{
    /// <summary>The colour <paramref name="text"/> writes, null when it reads as none</summary>
    public static Color? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();

        // A colour with a colour profile names a file to open (even on a network share): no colour a mod needs
        if (text.StartsWith("ContextColor", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            return System.Windows.Media.ColorConverter.ConvertFromString(text) is Color color ? color : null;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or InvalidOperationException or ArgumentException
                                       or OverflowException or IndexOutOfRangeException)
        {
            // "sc#1" and its like throw InvalidOperationException from WPF's tokenizer
            return null;
        }
    }
}
