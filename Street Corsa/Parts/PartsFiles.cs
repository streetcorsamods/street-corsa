using System.IO;
using System.Text.Json;
using StreetCorsa.Mods;

namespace StreetCorsa.Parts;

/// <summary>
/// A file every mod's parts folder may have (part_groups.json, part_classes.json, fuels.json): read from each in load
/// order, the installed set once, or one a test or a tool sets. Plain BCL, linked into EngineBench.
/// </summary>
public sealed class PartsFiles<T> where T : class
{
    private readonly ContentCache<T> _installed;
    private volatile T? _current;

    /// <param name="load">Reads the files of the parts folders, in load order</param>
    public PartsFiles(Func<IEnumerable<string>, T> load)
    {
        _installed = new ContentCache<T>(() => load(ModSet.Installed.Folders(ContentTypes.Parts).Select(f => f.Dir)));
    }

    /// <summary>The mods next to the game's, read once</summary>
    public T Installed => _installed.Value;

    /// <summary>The ones in use: the installed ones, or ones a test or a tool sets</summary>
    public T Current
    {
        get => _current ?? Installed;
        set => _current = value;
    }
}

public static class PartsFiles
{
    /// <summary>
    /// Hands <paramref name="read"/> each folder's <paramref name="fileName"/> that is there, in load order, with the
    /// file's path. A file that cannot be read, or that <paramref name="read"/> cannot make sense of (a property it
    /// needs is missing, a value of the wrong kind), is a problem line and left out.
    /// </summary>
    public static void ReadEach(IEnumerable<string> partsFolders, string fileName, List<string> problems, Action<string, JsonElement> read)
    {
        foreach (var dir in partsFolders)
        {
            var file = Path.Combine(dir, fileName);
            if (!File.Exists(file)) continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file), ModJson.DocumentOptions);
                read(file, document.RootElement);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
            {
                problems.Add($"{file}: {ex.Message}; left out");
            }
        }
    }
}
