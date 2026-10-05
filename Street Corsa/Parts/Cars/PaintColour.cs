using System.Globalization;

namespace StreetCorsa.Parts.Cars;

/// <summary>A colour of paint, as #RRGGBB</summary>
public readonly record struct PaintColour(byte R, byte G, byte B)
{
    public static readonly PaintColour Black = new(0, 0, 0);

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Its channels, 0..1</summary>
    public (float R, float G, float B) Unit => (R / 255f, G / 255f, B / 255f);

    public static PaintColour Of(float r, float g, float b) =>
        new((byte)Math.Clamp((int)MathF.Round(r * 255f), 0, 255), (byte)Math.Clamp((int)MathF.Round(g * 255f), 0, 255), (byte)Math.Clamp((int)MathF.Round(b * 255f), 0, 255));

    /// <summary>A colour written #RRGGBB (or RRGGBB); null for anything else</summary>
    public static PaintColour? Parse(string? text)
    {
        var hex = text?.Trim().TrimStart('#');
        if (hex is not { Length: 6 } || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return null;
        return new PaintColour((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public override string ToString() => Hex;
}

/// <summary>
/// A car's paint as a race is given it: a tinted texture (the race mode's file) and the name of the car's texture it
/// takes the place of (car_parts.lua, CAR_n_PAINT_k)
/// </summary>
public sealed record ShownPaint(ShownModel File, string Texture);
