using System.IO;
using Newtonsoft.Json;
using StreetCorsa.Helpers;
using StreetCorsa.Mods;

namespace StreetCorsa.Audio;

/// <summary>A sound file on disk, with where it came from when a credits.csv beside it says</summary>
public sealed record SoundFile(
    string FullPath,
    string RelativePath,
    string Category,
    string Title,
    string? Author,
    string? License,
    string? Url,
    AudioBus Bus);

/// <summary>
/// The game's sound files: a folder of folders, each folder a category. A credits.csv anywhere in it (freesound's
/// columns: category, file, id, title, user, license, duration, url; file relative to the csv) names the sounds it
/// lists. Kenney's packs are CC0 and credit nobody.
/// </summary>
public static class SoundFiles
{
    public static readonly string[] Extensions = [".ogg", ".wav", ".mp3", ".flac", ".aif", ".aiff"];

    public const string CreditsFile = "credits.csv";

    /// <summary>
    /// The source tree's Assets\Sound, looked for upwards from <paramref name="start"/> (the program runs from the
    /// project's bin folder while sounds are being chosen); null when there is none
    /// </summary>
    public static string? FindDevRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            var sound = Path.Combine(dir.FullName, "Assets", "Sound");
            if (Directory.Exists(sound)) return sound;
        }

        return null;
    }

    /// <summary>Every sound under <paramref name="root"/>, by category then file name</summary>
    public static IReadOnlyList<SoundFile> Scan(string root)
    {
        var credits = ReadCredits(root);
        var files = new List<SoundFile>();

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;

            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var category = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "";
            var kenney = relative.Contains("kenney/", StringComparison.OrdinalIgnoreCase);
            credits.TryGetValue(Path.GetFullPath(path), out var credit);

            files.Add(new SoundFile(
                path,
                relative,
                category,
                credit?.Title is { Length: > 0 } title ? title : Path.GetFileNameWithoutExtension(path),
                credit?.Author ?? (kenney ? "Kenney" : null),
                credit?.License ?? (kenney ? "CC0 1.0" : null),
                credit?.Url,
                BusFor(relative)));
        }

        return files
            .OrderBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The bus a sound plays on, by the folder it is in: menus, surroundings, the radio, or the tools (everything
    /// else: a clank is the likeliest thing a stray sound is)
    /// </summary>
    public static AudioBus BusFor(string relativePath)
    {
        var path = "/" + relativePath.Replace('\\', '/').ToLowerInvariant();
        if (path.Contains("/ui/") || path.Contains("/ui-audio/") || path.Contains("/interface-sounds/")) return AudioBus.Interface;
        if (path.Contains("/ambience/")) return AudioBus.Ambience;
        if (path.Contains("/radio/")) return AudioBus.Radio;
        return AudioBus.Effects;
    }

    /// <summary>
    /// A line of a credits.csv, its cells trimmed ("" for none). <paramref name="FullPath"/> is the file it names, beside
    /// the csv; null when it names none or nothing that is a path.
    /// </summary>
    private sealed record CreditRow(string? FullPath, string Title, string Author, string License, string Source);

    /// <summary>
    /// The lines of a credits.csv: the columns found by name in its first line, either name of each pair (author or
    /// user, source or url: the game's own columns and freesound's)
    /// </summary>
    private static List<CreditRow> ReadCreditRows(string csv)
    {
        var rows = ParseCsv(File.ReadAllText(csv));
        if (rows.Count == 0) return [];

        var dir = Path.GetDirectoryName(csv)!;
        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => names.Select(n => header.IndexOf(n)).FirstOrDefault(i => i >= 0, -1);
        var (file, title, author, license, source) = (Col("file"), Col("title"), Col("author", "user"), Col("license"), Col("source", "url"));

        var lines = new List<CreditRow>();
        foreach (var row in rows.Skip(1))
        {
            string At(int i) => i >= 0 && i < row.Count ? row[i].Trim() : "";
            lines.Add(new CreditRow(FullPathOf(dir, At(file)), At(title), At(author), At(license), At(source)));
        }
        return lines;
    }

    /// <summary>The file a credits line names, beside its csv; null for none, or a cell that is no path (a NUL in it, say)</summary>
    private static string? FullPathOf(string dir, string name)
    {
        if (name.Length == 0) return null;
        try
        {
            return Path.GetFullPath(Path.Combine(dir, name));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private sealed record Credit(string Title, string? Author, string? License, string? Url);

    private static Dictionary<string, Credit> ReadCredits(string root)
    {
        static string? OrNull(string cell) => cell.Length > 0 ? cell : null;

        var credits = new Dictionary<string, Credit>(StringComparer.OrdinalIgnoreCase);
        foreach (var csv in Directory.EnumerateFiles(root, CreditsFile, SearchOption.AllDirectories))
        {
            foreach (var row in ReadCreditRows(csv))
            {
                if (row.FullPath is not { } path) continue;
                credits[path] = new Credit(row.Title, OrNull(row.Author), OrNull(row.License), OrNull(row.Source));
            }
        }

        return credits;
    }

    /// <summary>
    /// The sounds whose makers the game's credits name: the lines of every mod's sfx\credits.csv (columns file, title,
    /// author, license, source) that are neither CC0 nor the game's own, as "title, author (license)", in load order
    /// </summary>
    public static IReadOnlyList<string> NamedCredits(ModSet mods)
    {
        var named = new List<string>();
        foreach (var (_, dir) in mods.Folders(ContentTypes.Sfx))
        {
            var csv = Path.Combine(dir, CreditsFile);
            if (!File.Exists(csv)) continue;

            foreach (var row in ReadCreditRows(csv))
            {
                var (by, terms) = (row.Author, row.License);
                if (by.Length == 0 || terms.Contains("CC0", StringComparison.OrdinalIgnoreCase)
                    || terms.StartsWith("the game's own", StringComparison.OrdinalIgnoreCase)) continue;

                var line = row.Title.Length > 0 ? $"{row.Title}, {by}" : by;
                named.Add(terms.Length > 0 ? $"{line} ({terms})" : line);
            }
        }

        return named.Distinct().ToList();
    }

    /// <summary>Who made a sound and where to find them, out of a mod's sfx\credits.csv</summary>
    public sealed record Maker(string Author, Uri? Link);

    /// <summary>
    /// Who made the sound file: the author and source of its line in a credits.csv in its folder or one above it, up to
    /// <paramref name="root"/> (the mod's sfx folder). The source is a link only when it is a web address (http or
    /// https): a mod's csv never names a file or a program to open. Null when no line names the file or its author.
    /// </summary>
    public static Maker? MakerOf(string file, string root)
    {
        var full = Path.GetFullPath(file);
        // The folder itself or one in it: a sibling whose name starts the same (sfx2 beside sfx) is not in it
        var top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        for (var dir = Path.GetDirectoryName(full);
             dir != null && (dir + Path.DirectorySeparatorChar).StartsWith(top, StringComparison.OrdinalIgnoreCase);
             dir = Path.GetDirectoryName(dir))
        {
            var csv = Path.Combine(dir, CreditsFile);
            if (!File.Exists(csv)) continue;

            List<CreditRow> rows;
            try
            {
                rows = ReadCreditRows(csv);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (rows.FirstOrDefault(r => string.Equals(r.FullPath, full, StringComparison.OrdinalIgnoreCase)) is not { } line) continue;
            if (line.Author.Length == 0) return null;

            var link = Uri.TryCreate(line.Source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;
            return new Maker(line.Author, link);
        }

        return null;
    }

    /// <summary>RFC 4180: fields in double quotes may hold commas, line breaks and doubled quotes</summary>
    internal static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') field.Append(text[++i]);
                else quoted = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}

/// <summary>
/// The sounds the player marked to keep while going through the candidates: a list of paths relative to the sound
/// root, in picks.json beside them
/// </summary>
public sealed class SoundPicks
{
    public const string FileName = "picks.json";

    private readonly string _path;
    private readonly SortedSet<string> _keep = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Stored
    {
        [JsonProperty("keep")] public List<string> Keep { get; set; } = [];
    }

    public SoundPicks(string path)
    {
        _path = path;
        if (!File.Exists(path)) return;

        var stored = JsonConvert.DeserializeObject<Stored>(File.ReadAllText(path));
        foreach (var relative in stored?.Keep ?? []) _keep.Add(relative);
    }

    public bool IsKept(string relativePath) => _keep.Contains(relativePath);

    public int Count => _keep.Count;

    /// <summary>Marks or unmarks it, and writes the list at once: a pick must not be lost to a crash</summary>
    public void Set(string relativePath, bool keep)
    {
        var changed = keep ? _keep.Add(relativePath) : _keep.Remove(relativePath);
        if (!changed) return;

        SafeFile.WriteAllText(_path, JsonConvert.SerializeObject(new Stored { Keep = _keep.ToList() }, Formatting.Indented));
    }
}
