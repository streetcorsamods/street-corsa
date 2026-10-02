using System.IO;
using System.Text.Json;

namespace StreetCorsa.Mods;

/// <summary>An entry of a mod's content file, not yet read into its type</summary>
public readonly record struct ModEntry(ModInfo Mod, string Source, JsonElement Element)
{
    /// <summary>The id as the mod wrote it</summary>
    public string? Id => ModJson.Text(Element, "id");

    /// <summary>The full id of another mod's entry this one takes the place of, on purpose</summary>
    public string? Replaces => ModJson.Text(Element, "replaces");

    public T? As<T>() => Element.Deserialize<T>(ModJson.Options);
}

/// <summary>
/// Reads a kind of content from every mod's .json files. A file holds its entries in a list under a name
/// (<c>{ "dealers": [ ... ] }</c>), or is a list itself, or is one entry: a mod can ship one file per entry or
/// all of them in one. Files that do not read are one line each in the problems, the rest stand.
/// </summary>
public static class ModJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <param name="listName">The name the entries are listed under in a file ("dealers")</param>
    /// <param name="problems">Where a file that does not read is reported</param>
    /// <param name="idName">What an entry's id is called besides "id": a file that is one entry has one of them</param>
    /// <param name="listOnly">Only the list under <paramref name="listName"/> is read: for a kind that shares its folder with another</param>
    /// <param name="otherLists">The lists of other kinds in the same folder: a file holding one of them is not this kind's to report</param>
    public static List<ModEntry> Entries(ModSet mods, string type, string listName, List<string> problems, string idName = "id",
        bool listOnly = false, IReadOnlyCollection<string>? otherLists = null)
    {
        var entries = new List<ModEntry>();
        foreach (var (mod, file) in mods.JsonFiles(type))
        {
            var source = $"{mod.Id}/{type}/{Path.GetFileName(file)}";
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file), DocumentOptions);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && TryGet(root, listName, out var list))
                {
                    if (list.ValueKind == JsonValueKind.Array) AddAll(list);
                    else problems.Add($"{source}: \"{listName}\" is not a list; left out");
                }
                else if (listOnly || (otherLists ?? []).Any(other => TryGet(root, other, out _)))
                {
                    // Another kind's file, or one entry of it
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    AddAll(root);
                }
                else if (root.ValueKind == JsonValueKind.Object
                         && (TryGet(root, idName, out _) || TryGet(root, "id", out _) || TryGet(root, "replaces", out _)))
                {
                    entries.Add(new ModEntry(mod, source, root.Clone()));
                }
                else
                {
                    problems.Add($"{source}: no \"{listName}\" list and no \"{idName}\"; left out");
                }

                void AddAll(JsonElement array)
                {
                    foreach (var element in array.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.Object) entries.Add(new ModEntry(mod, source, element.Clone()));
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{source}: {ex.Message}; left out");
            }
        }

        return entries;
    }

    /// <summary>
    /// The entries of a kind of content merged into a registry by id (see <see cref="ContentRegistry{T}"/>);
    /// <paramref name="read"/> gives null for an entry it cannot use, and says why in the problems
    /// </summary>
    public static ContentRegistry<T> Load<T>(ModSet mods, string type, string listName, string kind, List<string> problems,
        Func<ModEntry, T?> read, IReadOnlyCollection<string>? otherLists = null, bool listOnly = false) where T : class
    {
        var registry = new ContentRegistry<T>(kind);
        var removals = registry.Removals(mods, type);
        foreach (var entry in Entries(mods, type, listName, problems, listOnly: listOnly, otherLists: otherLists))
        {
            removals.Before(entry.Mod);
            T? value;
            try
            {
                value = read(entry);
            }
            // A mod's file is anyone's: whatever one entry does wrong (a null where a list goes...) leaves out that entry, never the game
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                problems.Add($"{entry.Source}: {kind} {entry.Id}: {ex.Message}; left out");
                continue;
            }

            if (value != null) registry.Add(entry.Mod, entry.Id, entry.Replaces, value, entry.Source);
        }

        removals.Rest();
        problems.AddRange(registry.Problems);
        return registry;
    }

    public static string? Text(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A property by name, whatever its case</summary>
    public static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
