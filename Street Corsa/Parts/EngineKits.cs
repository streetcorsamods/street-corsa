using System.IO;
using Newtonsoft.Json;

namespace StreetCorsa.Parts;

/// <summary>
/// The sizes an engine kit comes in: an engine build's parts in one crate, as far as the size takes
/// them. The core's are a short block (the block and what turns in it), a long block (that with its heads and
/// covers) and a complete engine (everything the build lists). A size names the work order kinds it holds
/// (<see cref="WorkOrder.KindOf"/>), or all of them; a size that <c>"includes"</c> another holds that one's kinds too.
/// Read from <see cref="FileName"/> in every mod's parts folder.
/// </summary>
/// <remarks>
/// A later mod's size of the same id takes the place of the one before, whole; one with <c>"disabled": true</c> takes it
/// out. The sizes are listed smallest first: the first is the one no used kit is ever short of
/// (<see cref="Cars.KitWork.TearDown"/>).
/// </remarks>
public sealed class EngineKits
{
    public const string FileName = "engine_kits.json";

    private readonly List<KitSize> _sizes = new();

    public static EngineKits Empty => new();

    /// <summary>Every size, smallest first, the core's before a mod's new ones</summary>
    public IReadOnlyList<KitSize> Sizes => _sizes;

    /// <summary>A size by its id, any case; null for one there is none of</summary>
    public KitSize? Get(string? id) => id == null ? null : _sizes.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The size that holds every part of a build; null when none does</summary>
    public KitSize? Complete => _sizes.LastOrDefault(s => s.All);

    /// <summary>The smallest size: what a used kit always has</summary>
    public KitSize? Smallest => _sizes.FirstOrDefault();

    /// <summary>Whether a part of the work order kind <paramref name="kind"/> goes in a kit of <paramref name="size"/></summary>
    public bool Holds(KitSize size, string? kind) => size.All || kind != null && KindsOf(size).Contains(kind);

    /// <summary>The kinds a size holds, those of the sizes it includes with them; worked out once a size (asked for every part of every build)</summary>
    public IReadOnlySet<string> KindsOf(KitSize size) => _kinds.GetOrAdd(size, s =>
    {
        var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var at = s; at != null && seen.Add(at.Id); at = Get(at.Includes)) kinds.UnionWith(at.Kinds);
        return kinds;
    });

    // By the size itself: Load replaces a size whole, so a replaced size's kinds are never read off the old one
    private readonly System.Collections.Concurrent.ConcurrentDictionary<KitSize, IReadOnlySet<string>> _kinds = new(ReferenceEqualityComparer.Instance);

    public static EngineKits Load(IEnumerable<(string ModId, bool IsBase, string Dir)> roots, List<string> problems)
    {
        var book = new EngineKits();
        foreach (var (modId, isBase, dir) in roots)
        {
            var file = Path.Combine(dir, FileName);
            if (!File.Exists(file)) continue;

            var label = (isBase ? string.Empty : modId + "/") + FileName;
            KitsFile? read;
            try
            {
                read = JsonConvert.DeserializeObject<KitsFile>(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{label}: {ex.Message}; its kit sizes are left out");
                continue;
            }

            foreach (var size in read?.Sizes ?? new())
            {
                if (size == null || string.IsNullOrWhiteSpace(size.Id))
                {
                    problems.Add($"{label}: a kit size without an id is left out");
                    continue;
                }

                size.Id = size.Id.Trim();
                size.Kinds = PartDefinition.NonNull(size.Kinds);
                size.Kinds.RemoveAll(string.IsNullOrWhiteSpace);
                var index = book._sizes.FindIndex(s => s.Id.Equals(size.Id, StringComparison.OrdinalIgnoreCase));
                if (size.Disabled)
                {
                    if (index >= 0) book._sizes.RemoveAt(index);
                    continue;
                }

                if (!size.All && size.Kinds.Count == 0 && string.IsNullOrWhiteSpace(size.Includes))
                {
                    problems.Add($"{label}: {size.Id} names no kinds and includes no size, so it holds nothing; left out");
                    continue;
                }

                size.Source = label;
                if (index >= 0) book._sizes[index] = size;
                else book._sizes.Add(size);
            }
        }

        foreach (var size in book._sizes.Where(s => !string.IsNullOrWhiteSpace(s.Includes) && book.Get(s.Includes) == null))
            problems.Add($"{size.Source}: {size.Id} includes \"{size.Includes}\", which is no kit size; only its own kinds go in it");

        return book;
    }

    /// <summary>The kinds the sizes name that no part of the catalog is of, one problem line each</summary>
    public IEnumerable<string> UnknownKinds(IEnumerable<string> partKinds)
    {
        var known = partKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var size in _sizes)
        {
            foreach (var kind in size.Kinds.Where(k => !known.Contains(k)))
                yield return $"{size.Source}: {size.Id} names the kind \"{kind}\", which no part is of";
        }
    }

    private sealed class KitsFile
    {
        [JsonProperty("sizes")]
        public List<KitSize?>? Sizes { get; set; }
    }
}

/// <summary>A size of engine kit (<see cref="EngineKits"/>): what of a build goes in its crate</summary>
public sealed class KitSize
{
    /// <summary>Its id, "short_block"; its words are the text pool's <c>kits.size.&lt;id&gt;</c></summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The work order kinds of the parts it holds</summary>
    [JsonProperty("kinds")]
    public List<string> Kinds { get; set; } = new();

    /// <summary>Another size whose kinds it holds too</summary>
    [JsonProperty("includes")]
    public string? Includes { get; set; }

    /// <summary>Everything the build lists</summary>
    [JsonProperty("all")]
    public bool All { get; set; }

    /// <summary>A mod's way to take out a size an earlier mod (or the core) has</summary>
    [JsonProperty("disabled")]
    public bool Disabled { get; set; }

    /// <summary>The file it came from, for the problem lines</summary>
    [JsonIgnore]
    public string Source { get; set; } = string.Empty;
}
