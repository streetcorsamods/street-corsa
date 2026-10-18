using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Helpers;

namespace StreetCorsa.Audio.Radio;

/// <summary>A song file in one of the radio's folders: its path, and what its tags (or its name) say it is</summary>
public sealed record RadioSong(string Path, string Title, string? Artist, string? Album, uint LengthMs)
{
    /// <summary>"Title — Artist", or the title alone</summary>
    public string Display => Artist is { Length: > 0 } artist ? $"{Title} — {artist}" : Title;

    public string LengthText => LengthMs == 0 ? "" : RadioTime.Format(LengthMs);
}

/// <summary>A list of songs the player put together, in their order; songs by path, so a list outlives a rescan</summary>
public sealed class RadioPlaylist
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("songs")] public List<string> Songs { get; set; } = [];
}

/// <summary>Everything the radio remembers between sessions, in %AppData%\StreetCorsa\radio.json</summary>
public sealed class RadioState
{
    public const string FileName = "radio.json";

    /// <summary>Where the music is: any folders on the PC, their subfolders too</summary>
    [JsonPropertyName("folders")] public List<string> Folders { get; set; } = [];
    [JsonPropertyName("playlists")] public List<RadioPlaylist> Playlists { get; set; } = [];
    [JsonPropertyName("shuffle")] public bool Shuffle { get; set; } = true;
    [JsonPropertyName("repeat")] public RadioRepeat Repeat { get; set; } = RadioRepeat.All;

    /// <summary>Whether it was on at the end of the last session: it comes back so</summary>
    [JsonPropertyName("on")] public bool WasOn { get; set; }

    /// <summary>The list that was playing (null: every song), the song and how far into it</summary>
    [JsonPropertyName("list")] public string? List { get; set; }
    [JsonPropertyName("song")] public string? Song { get; set; }
    [JsonPropertyName("position_ms")] public uint PositionMs { get; set; }

    /// <summary>The folder the radio starts with before the player names any: Documents\Street Corsa\Radio</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Street Corsa", "Radio");

    public static RadioState Load(string path)
    {
        RadioState? state = null;
        if (File.Exists(path)) state = JsonSerializer.Deserialize<RadioState>(File.ReadAllText(path), RadioJson.Indented);

        state ??= new RadioState { Folders = [DefaultFolder] };
        state.Folders = state.Folders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        state.Playlists = state.Playlists.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        return state;
    }

    public void Save(string path) => SafeFile.WriteAllText(path, JsonSerializer.Serialize(this, RadioJson.Indented));
}

/// <summary>
/// How the radio's own files (radio.json, radio_songs.json) are read and written: the shape the files have had since
/// the radio was made (the names given on the properties, the repeat as a word, paths written as they are, nothing
/// escaped), names read whatever their case
/// </summary>
internal static class RadioJson
{
    /// <summary>The settings, laid out for a player to read</summary>
    public static readonly JsonSerializerOptions Indented = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The tags cache, in one line: thousands of entries</summary>
    public static readonly JsonSerializerOptions Compact = new(Indented) { WriteIndented = false };
}

/// <summary>
/// The songs in the radio's folders. Tags are read through FMOD once per file and kept in radio_songs.json by path,
/// size and time written, so a library of thousands comes back at once. The formats are the ones AC's FMOD plays.
/// </summary>
public static class RadioLibrary
{
    public const string CacheFileName = "radio_songs.json";

    public static readonly string[] Playable = [".mp3", ".ogg", ".oga", ".flac", ".wav", ".aif", ".aiff"];

    /// <summary>Music files the radio cannot play, counted so the player is told why a song is missing</summary>
    public static readonly string[] NotPlayable = [".m4a", ".aac", ".opus", ".wma", ".alac", ".ape", ".mp4"];

    /// <summary>
    /// The most songs a scan lists: more than any player's music, and a bound on what a folder far too wide (a whole
    /// Documents) costs in memory and time. The player is told the list was cut short (<see cref="ScanResult.Capped"/>).
    /// </summary>
    public const int MostSongs = 20_000;

    /// <summary>
    /// Over this many songs the folders are not watched for changes: a watcher over a tree that size fires on every
    /// write anywhere in it, and each firing is a rescan of all of it
    /// </summary>
    public const int MostWatched = 5_000;

    /// <param name="Capped">The scan stopped at <see cref="MostSongs"/>: there is more music than is listed</param>
    public sealed record ScanResult(IReadOnlyList<RadioSong> Songs, int NotPlayable, IReadOnlyList<string> MissingFolders, bool Capped = false);

    // What the tags are read into: an entry of an older reading is read again
    private const int CacheVersion = 2;

    private sealed class CacheEntry
    {
        [JsonPropertyName("v")] public int Version { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("written")] public long Written { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("artist")] public string? Artist { get; set; }
        [JsonPropertyName("album")] public string? Album { get; set; }
        [JsonPropertyName("length_ms")] public uint LengthMs { get; set; }
    }

    /// <summary>
    /// What reading a song's tags came to: its tags, or null when the file cannot be read for them; not
    /// <paramref name="Asked"/> when there was nothing to read them with (no FMOD yet, or the exit under way), and then
    /// not kept, the file asked about again at the next scan
    /// </summary>
    public readonly record struct SongRead(SongInfo? Info, bool Asked);

    /// <summary>
    /// A folder the radio will not take as a music folder: the root of a drive (<c>C:\</c>) or of a share
    /// (<c>\\server\share</c>), which would have the radio list and watch the whole of it. A folder inside one is fine.
    /// </summary>
    public static bool IsRoot(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var root = Path.GetPathRoot(full);
        return string.IsNullOrEmpty(root) || string.Equals(Path.TrimEndingDirectorySeparator(root), full, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every playable song under the folders, with its tags: from the cache when the file has not changed, through
    /// <paramref name="read"/> when it has. A folder that is not there, or a subfolder that cannot be read, is skipped.
    /// On a worker.
    /// </summary>
    public static ScanResult Scan(IReadOnlyList<string> folders, string cachePath, Func<string, SongInfo?> read, CancellationToken cancel = default) =>
        Scan(folders, cachePath, path => new SongRead(read(path), true), cancel);

    /// <summary>
    /// <see cref="Scan(IReadOnlyList{string}, string, Func{string, SongInfo?}, CancellationToken)"/>, a read that may find
    /// nothing to read with. A scan called off keeps what it read so far: the next one starts from there.
    /// </summary>
    /// <param name="mostSongs">Where the list is cut short; <see cref="MostSongs"/> unless a test lowers it</param>
    public static ScanResult Scan(IReadOnlyList<string> folders, string cachePath, Func<string, SongRead> read, CancellationToken cancel = default, int mostSongs = MostSongs)
    {
        var cache = LoadCache(cachePath);
        var fresh = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        var songs = new List<RadioSong>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var notPlayable = 0;
        var readAny = false;
        var capped = false;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };

        try
        {
            ScanFolders();
        }
        catch (OperationCanceledException)
        {
            // What was read is kept, over what was known: a scan called off again and again (music still being copied
            // in) still gets through the library a part at a time
            if (readAny)
            {
                foreach (var (file, entry) in fresh) cache[file] = entry;
                SaveCache(cachePath, cache);
            }
            throw;
        }

        SaveCache(cachePath, fresh);
        songs.Sort((a, b) => string.Compare(a.Display, b.Display, StringComparison.CurrentCultureIgnoreCase));
        return new ScanResult(songs, notPlayable, missing, capped);

        void ScanFolders()
        {
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    missing.Add(folder);
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(folder, "*", options))
                {
                    cancel.ThrowIfCancellationRequested();
                    if (songs.Count >= mostSongs)
                    {
                        capped = true;
                        return;
                    }

                    var extension = Path.GetExtension(file);
                    if (NotPlayable.Contains(extension, StringComparer.OrdinalIgnoreCase)) notPlayable++;
                    if (!Playable.Contains(extension, StringComparer.OrdinalIgnoreCase) || !seen.Add(file)) continue;

                    FileInfo info;
                    try
                    {
                        info = new FileInfo(file);
                        if (info.Length == 0) continue;
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    var written = info.LastWriteTimeUtc.Ticks;
                    if (!cache.TryGetValue(file, out var entry) || entry.Version != CacheVersion || entry.Size != info.Length || entry.Written != written)
                    {
                        var (tags, asked) = read(file);
                        entry = new CacheEntry
                        {
                            Version = CacheVersion, Size = info.Length, Written = written, Title = tags?.Title, Artist = tags?.Artist,
                            Album = tags?.Album, LengthMs = tags?.LengthMs ?? 0
                        };

                        // Listed by its name for now, and not kept as a song with no tags: it was never asked
                        if (asked)
                        {
                            fresh[file] = entry;
                            readAny = true;
                        }
                    }
                    else
                    {
                        fresh[file] = entry;
                    }

                    // Untagged: "Artist - Title" is often in the name
                    var named = FromName(file);
                    songs.Add(new RadioSong(file, entry.Title ?? named.Title, entry.Artist ?? (entry.Title == null ? named.Artist : null), entry.Album, entry.LengthMs));
                }
            }
        }
    }

    /// <summary>
    /// A title, and the artist when the name has one, out of a file name: "BEN E. KING - Stand By Me.mp3" as "Stand By
    /// Me" by "BEN E. KING"; "03 - Surf City.mp3" as "Surf City" (a track number is not an artist)
    /// </summary>
    public static (string Title, string? Artist) FromName(string path)
    {
        var name = TitleFromName(path);
        var dash = name.IndexOf(" - ", StringComparison.Ordinal);
        if (dash <= 0) return (name, null);

        var artist = name[..dash].Trim();
        var title = name[(dash + 3)..].Trim();
        return artist.Length == 0 || title.Length == 0 || artist.All(char.IsDigit) ? (name, null) : (title, artist);
    }

    /// <summary>A title out of a file name: "03 - Surf City.mp3" as "Surf City", underscores as spaces</summary>
    public static string TitleFromName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Trim();
        var digits = 0;
        while (digits < name.Length && char.IsDigit(name[digits])) digits++;
        if (digits is > 0 and <= 3 && digits < name.Length)
        {
            var rest = name[digits..].TrimStart(' ', '-', '.', ')');
            if (rest.Length > 0) name = rest;
        }

        return name.Length > 0 ? name : Path.GetFileName(path);
    }

    private static Dictionary<string, CacheEntry> LoadCache(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(path), RadioJson.Compact) is { } cache)
                return new Dictionary<string, CacheEntry>(cache, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Only a cache: read the tags again
        }

        return new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveCache(string path, Dictionary<string, CacheEntry> cache)
    {
        try
        {
            SafeFile.WriteAllText(path, JsonSerializer.Serialize(cache, RadioJson.Compact));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a cache: the next scan reads the tags again
        }
    }
}
