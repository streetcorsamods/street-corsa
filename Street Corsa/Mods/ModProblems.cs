namespace StreetCorsa.Mods;

/// <summary>
/// Every problem the mods have had this session, from every loader (the mods themselves, their parts, racers, words,
/// sounds, rules...), for the player to see: until now they only went to the log. Each line once, however often its
/// content is read again. Thread-safe: content loads on workers.
///
/// Plain BCL on purpose, like the rest of Mods.
/// </summary>
public static class ModProblems
{
    private static readonly List<string> Lines = new();
    private static readonly HashSet<string> Seen = new(StringComparer.Ordinal);
    private static readonly object Lock = new();

    /// <summary>The problems so far, "area: problem", in the order they came</summary>
    public static IReadOnlyList<string> All
    {
        get
        {
            lock (Lock) return Lines.ToList();
        }
    }

    /// <param name="area">What was being read ("Racers", "Rules")</param>
    public static void Add(string area, IEnumerable<string> problems)
    {
        lock (Lock)
        {
            foreach (var problem in problems)
            {
                var line = $"{area}: {problem}";
                if (Seen.Add(line)) Lines.Add(line);
            }
        }
    }

    public static void Add(string area, string problem) => Add(area, [problem]);
}
