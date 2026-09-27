namespace StreetCorsa.Mods;

/// <summary>
/// Entries of one kind of content merged from every mod, by full id, in the order they came. A mod's entry gets
/// its mod's namespace (<see cref="ModIds.Qualify"/>), so mods never clash by accident; one that names an entry it
/// <c>replaces</c> takes that entry's id and place, on purpose. What cannot be taken is left out with a line in
/// <see cref="Problems"/>: an entry without an id, one that replaces nothing, one a mod has twice (the later wins).
///
/// Plain BCL on purpose: the tools compile it by link.
/// </summary>
public sealed class ContentRegistry<T> where T : class
{
    private readonly Dictionary<string, (T Entry, ModInfo Mod)> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = new();
    private readonly List<string> _problems = new();
    private readonly List<string> _replaced = new();

    public ContentRegistry(string kind)
    {
        Kind = kind;
    }

    /// <summary>What the entries are, for the report ("dealer", "event")</summary>
    public string Kind { get; }

    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Entries a mod replaced on purpose, one line each: not a problem, but worth a line in the log</summary>
    public IReadOnlyList<string> Replaced => _replaced;

    public int Count => _order.Count;

    /// <summary>Every entry in the order it was first added (a replacement keeps the place of what it replaced)</summary>
    public IEnumerable<T> All => _order.Select(id => _entries[id].Entry);

    public IEnumerable<(string Id, T Entry, ModInfo Mod)> Entries => _order.Select(id => (id, _entries[id].Entry, _entries[id].Mod));

    public bool Contains(string id) => _entries.ContainsKey(id);

    public T? Get(string id) => _entries.TryGetValue(id, out var found) ? found.Entry : null;

    /// <summary>The mod an entry came from (the one that replaced it, if any)</summary>
    public ModInfo? ModOf(string id) => _entries.TryGetValue(id, out var found) ? found.Mod : null;

    /// <summary>A reference in <paramref name="mod"/>'s files to the full id it means (see <see cref="ModIds.Resolve"/>)</summary>
    public string Resolve(ModInfo mod, string reference) => ModIds.Resolve(mod.Id, reference, Contains);

    /// <summary>Takes an entry from a mod's file</summary>
    /// <param name="localId">The id as the mod wrote it</param>
    /// <param name="replaces">The full id of an entry this one takes the place of, on purpose; null for a new entry</param>
    /// <param name="source">Where it came from, for the report ("mymod/dealers/east.json")</param>
    /// <returns>The entry's full id, null when it was left out</returns>
    public string? Add(ModInfo mod, string? localId, string? replaces, T entry, string source)
    {
        if (!string.IsNullOrWhiteSpace(replaces))
        {
            var target = ModIds.Normalize(replaces.Trim());
            if (!_entries.TryGetValue(target, out var old))
            {
                _problems.Add($"{source}: replaces {Kind} {target}, which no mod before it has; left out");
                return null;
            }

            _entries[target] = (entry, mod);
            _replaced.Add($"{source}: {Kind} {target} of {old.Mod.Id} replaced by {mod.Id}");
            return target;
        }

        if (string.IsNullOrWhiteSpace(localId))
        {
            _problems.Add($"{source}: a {Kind} without an id is left out");
            return null;
        }

        localId = localId.Trim();
        if (ModIds.IsQualified(localId))
        {
            _problems.Add($"{source}: {Kind} id \"{localId}\" has a '{ModIds.Separator}' in it; ids are written without their mod (to take the place of another mod's, say which with \"replaces\"); left out");
            return null;
        }

        var id = ModIds.Qualify(mod.Id, localId);
        if (_entries.ContainsKey(id))
            _problems.Add($"{source}: {Kind} {id} is there twice in {mod.Id}; the later one is used");
        else
            _order.Add(id);

        _entries[id] = (entry, mod);
        return id;
    }
}
