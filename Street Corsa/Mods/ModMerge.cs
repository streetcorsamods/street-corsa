using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StreetCorsa.Mods;

/// <summary>
/// Settings files that every mod may have and that add up rather than list entries (game.json, the rules): each mod's
/// file goes over what the mods before it said, key by key. An object in one is merged into the same object before it;
/// anything else (a number, text, a list) takes the place of what was there. Keys that start with '_' are notes
/// (<c>"_comment"</c>) and are passed over. Names are matched whatever their case.
///
/// Plain BCL on purpose, like the rest of Mods.
/// </summary>
public static class ModMerge
{
    public static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>An empty object that matches names whatever their case</summary>
    public static JsonObject NewObject() => new(NodeOptions);

    /// <summary>
    /// The file of this name directly in each mod's folder (<c>game.json</c>), merged in load order; a file that does
    /// not read is one line in <paramref name="problems"/> and adds nothing
    /// </summary>
    public static JsonObject ModFiles(ModSet mods, string fileName, List<string> problems)
    {
        var merged = NewObject();
        foreach (var mod in mods.Mods)
        {
            var file = Path.Combine(mod.Folder, fileName);
            if (File.Exists(file)) MergeFile(merged, file, $"{mod.Id}/{fileName}", problems);
        }

        return merged;
    }

    /// <summary>Every .json file in each mod's folder for this kind of content (<c>rules</c>): mods in load order, files by name</summary>
    public static JsonObject ContentFiles(ModSet mods, string type, List<string> problems)
    {
        var merged = NewObject();
        foreach (var (mod, file) in mods.JsonFiles(type)) MergeFile(merged, file, $"{mod.Id}/{type}/{Path.GetFileName(file)}", problems);
        return merged;
    }

    private static void MergeFile(JsonObject into, string file, string source, List<string> problems)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(file), NodeOptions, DocumentOptions);
            if (node is JsonObject over)
            {
                // A name twice in an object (whatever its case) only shows when the object is first looked into: all
                // of the file is, before any of it goes in, so a file that does not read adds nothing at all
                Unpack(over);
                Into(into, over);
            }
            else
            {
                problems.Add($"{source}: not an object of settings ({{ ... }}); left out");
            }
        }
        catch (ArgumentException ex)
        {
            problems.Add($"{source}: a name is there twice in one object, whatever its case ({ex.Message}); left out");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            problems.Add($"{source}: {ex.Message}; left out");
        }
    }

    /// <summary>A JSON text read into an object that matches names whatever their case; null when it is not one</summary>
    public static JsonObject? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json, NodeOptions, DocumentOptions) is not JsonObject read) return null;
            Unpack(read);
            return read;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every object in the tree looked into: a name it has twice (in any case) throws <see cref="ArgumentException"/>
    /// here and not halfway through a merge
    /// </summary>
    private static void Unpack(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, value) in obj) Unpack(value);
                break;
            case JsonArray array:
                foreach (var item in array) Unpack(item);
                break;
        }
    }

    /// <summary><paramref name="over"/> merged into <paramref name="into"/>: objects key by key, the rest replaced</summary>
    public static void Into(JsonObject into, JsonObject over)
    {
        foreach (var (name, value) in over.ToList())
        {
            if (name.StartsWith('_')) continue;

            if (value is JsonObject overObject && into[name] is JsonObject intoObject)
            {
                Into(intoObject, overObject);
                continue;
            }

            // The key keeps the case it was first written in
            var key = into.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Key ?? name;
            into[key] = value is JsonObject fresh ? Copy(fresh) : value?.DeepClone();
        }
    }

    /// <summary>A copy that matches names whatever their case, the notes left out</summary>
    public static JsonObject Copy(JsonObject source)
    {
        var copy = NewObject();
        Into(copy, source);
        return copy;
    }
}
