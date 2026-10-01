namespace StreetCorsa.Parts.Cars;

/// <summary>
/// What the parts code says to the player (a race's damage report, the repair shop's jobs, why a part will not come
/// off), by key. The parts code is linked into the tools, which read no text: the game points <see cref="Source"/> at
/// its own words (<c>GameText.Say</c>) as it loads, and the tools say the keys.
/// </summary>
public static class PartWords
{
    /// <summary>The words of a key, its {placeholders} filled from the values</summary>
    public static Func<string, (string Name, object? Value)[], string> Source { get; set; } = (key, _) => key;

    public static string Say(string key, params (string Name, object? Value)[] values) => Source(key, values);
}
