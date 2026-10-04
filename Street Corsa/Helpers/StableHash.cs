namespace StreetCorsa.Helpers;

/// <summary>
/// The same number for the same text every run (string.GetHashCode is not): the same ad has the same seller and the
/// same street, whatever the day
/// </summary>
public static class StableHash
{
    public static uint Of(string text)
    {
        var hash = 17u;
        foreach (var c in text) hash = hash * 31u + c;
        return hash;
    }

    /// <summary>FNV-1a of the text: for a file named for what it was made from</summary>
    public static uint Fnv(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text) hash = (hash ^ c) * 16777619u;
        return hash;
    }
}
