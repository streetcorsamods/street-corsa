using System.Globalization;

namespace StreetCorsa.Mods;

/// <summary>
/// A version as mods write them: numbers with dots ("1", "1.1", "0.1.0"), whatever follows the numbers ignored
/// ("1.2-beta" is 1.2). Missing parts are 0, so "1" and "1.0.0" are the same version.
///
/// Plain BCL on purpose: the tools compile it by link.
/// </summary>
public readonly record struct ModVersion(int Major, int Minor, int Patch) : IComparable<ModVersion>
{
    public static ModVersion Zero => default;

    /// <summary>The version in <paramref name="text"/>; false when it does not start with a number</summary>
    public static bool TryParse(string? text, out ModVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim().TrimStart('v', 'V');
        var parts = new int[3];
        var index = 0;
        var at = 0;
        while (index < 3)
        {
            var start = at;
            while (at < s.Length && char.IsAsciiDigit(s[at])) at++;
            if (at == start || !int.TryParse(s.AsSpan(start, at - start), NumberStyles.None, CultureInfo.InvariantCulture, out parts[index]))
                break;

            index++;
            if (at >= s.Length || s[at] != '.') break;
            at++;
        }

        if (index == 0) return false;
        version = new ModVersion(parts[0], parts[1], parts[2]);
        return true;
    }

    public int CompareTo(ModVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public static bool operator <(ModVersion a, ModVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(ModVersion a, ModVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(ModVersion a, ModVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(ModVersion a, ModVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
