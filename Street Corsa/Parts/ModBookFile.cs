using System.IO;
using Newtonsoft.Json;

namespace StreetCorsa.Parts;

/// <summary>An entry of a book every mod may add to: a later mod's entry of the same id takes the place of the one before</summary>
public interface IModBookEntry
{
    string Id { get; set; }

    /// <summary>Takes the entry of the same id before it out</summary>
    bool Disabled { get; }

    /// <summary>The file the entry was read from, as the mod problems name it</summary>
    string Source { get; set; }
}

/// <summary>
/// A book file every mod's parts folder may have (engine_kits.json, rebuilds.json, work_order.json), read from each in
/// load order, the core's first, and the entries of a later one laid over those before by id. Plain BCL and
/// Newtonsoft, linked into EngineBench.
/// </summary>
public static class ModBookFile
{
    /// <summary>
    /// Hands <paramref name="read"/> each root's <paramref name="fileName"/> that is there, in load order, with the
    /// label the problems name it by (the core's bare, a mod's under its id). A file that does not read is a problem
    /// line ending in <paramref name="leftOut"/>, and the next one is read; an empty one is nothing.
    /// </summary>
    public static void ReadEach<TFile>(IEnumerable<(string ModId, bool IsBase, string Dir)> roots, string fileName, List<string> problems,
        string leftOut, Action<string, TFile> read) where TFile : class
    {
        foreach (var (modId, isBase, dir) in roots)
        {
            var file = Path.Combine(dir, fileName);
            if (!File.Exists(file)) continue;

            var label = (isBase ? string.Empty : modId + "/") + fileName;
            TFile? got;
            try
            {
                got = JsonConvert.DeserializeObject<TFile>(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{label}: {ex.Message}; {leftOut}");
                continue;
            }

            if (got != null) read(label, got);
        }
    }

    /// <summary>
    /// Lays <paramref name="entry"/> of the file <paramref name="label"/> over <paramref name="book"/>: its id trimmed,
    /// one with none a problem line (<paramref name="what"/> "a job"), one disabled takes the entry of its id out.
    /// True when the entry is still to be put in (<see cref="Put"/>), at <paramref name="index"/> (-1 for a new id).
    /// </summary>
    public static bool Overlay<T>(List<T> book, T? entry, string label, string what, List<string> problems, out int index)
        where T : class, IModBookEntry
    {
        index = -1;
        if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
        {
            problems.Add($"{label}: {what} without an id is left out");
            return false;
        }

        entry.Id = entry.Id.Trim();
        var id = entry.Id;
        index = book.FindIndex(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (!entry.Disabled) return true;

        if (index >= 0) book.RemoveAt(index);
        return false;
    }

    /// <summary>The entry in the book, from <paramref name="label"/>: in place of the one of its id (<paramref name="index"/>), else at the end</summary>
    public static void Put<T>(List<T> book, int index, T entry, string label) where T : class, IModBookEntry
    {
        entry.Source = label;
        if (index >= 0) book[index] = entry;
        else book.Add(entry);
    }
}
