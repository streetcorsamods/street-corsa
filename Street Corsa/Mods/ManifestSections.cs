using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace StreetCorsa.Mods;

/// <summary>
/// The sections of the mod.json files read lately, each file parsed once for all its sections and kept while it is
/// read, then let go: a content load asks one mod.json for its builds, parts, sounds and the rest one after another,
/// and a file changed on disk is read anew. Plain BCL, linked into EngineBench.
/// </summary>
internal static class ManifestSections
{
    /// <summary>A file not read for this long is let go</summary>
    private const long KeepMs = 30_000;

    private sealed record Sections(DateTime Written, long Length, Dictionary<string, string?> ByName)
    {
        public long LastUse = Environment.TickCount64;
    }

    private static readonly ConcurrentDictionary<string, Sections> Read = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SweepLock = new();
    private static System.Threading.Timer? _sweep;

    /// <summary>The raw JSON of the section <paramref name="name"/> of the file; null when it has none, says null, or does not read</summary>
    public static string? Get(string manifestPath, string name)
    {
        var file = new FileInfo(manifestPath);
        if (!file.Exists) return null;

        if (!Read.TryGetValue(file.FullName, out var sections) || sections.Written != file.LastWriteTimeUtc || sections.Length != file.Length)
        {
            sections = new Sections(file.LastWriteTimeUtc, file.Length, Parse(file.FullName));
            Read[file.FullName] = sections;
            KeepSweeping();
        }

        sections.LastUse = Environment.TickCount64;
        return sections.ByName.GetValueOrDefault(name);
    }

    /// <summary>Every name at the top of the file with its value's text (null for a null), the first of a name in any case</summary>
    private static Dictionary<string, string?> Parse(string path)
    {
        var byName = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), ModJson.DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return byName;

            foreach (var property in document.RootElement.EnumerateObject())
                byName.TryAdd(property.Name, property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetRawText());
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Told where the manifest was read
        }

        return byName;
    }

    private static void KeepSweeping()
    {
        lock (SweepLock) _sweep ??= new System.Threading.Timer(_ => Sweep(), null, KeepMs, KeepMs);
    }

    /// <summary>Lets go of the files not read lately; the timer stops once none is kept</summary>
    private static void Sweep()
    {
        var now = Environment.TickCount64;
        foreach (var (path, sections) in Read)
        {
            if (now - sections.LastUse > KeepMs) Read.TryRemove(new KeyValuePair<string, Sections>(path, sections));
        }

        lock (SweepLock)
        {
            if (!Read.IsEmpty || _sweep == null) return;
            _sweep.Dispose();
            _sweep = null;
        }
    }
}
