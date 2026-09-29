using System.IO;
using Newtonsoft.Json;
using StreetCorsa.Helpers;

namespace StreetCorsa.Audio.Radio;

/// <summary>A song file in one of the radio's folders: its path, and what its tags (or its name) say it is</summary>
public sealed record RadioSong(string Path, string Title, string? Artist, string? Album, uint LengthMs)
{
    /// <summary>"Title — Artist", or the title alone</summary>
    public string Display => Artist is { Length: > 0 } artist ? $"{Title} — {artist}" : Title;

    public string LengthText => LengthMs == 0 ? "" : TimeSpan.FromMilliseconds(LengthMs).ToString(LengthMs >= 3_600_000 ? @"h\:mm\:ss" : @"m\:ss");
}

/// <summary>A list of songs the player put together, in their order; songs by path, so a list outlives a rescan</summary>
public sealed class RadioPlaylist
{
    [JsonProperty("name")] public string Name { get; set; } = "";
    [JsonProperty("songs")] public List<string> Songs { get; set; } = [];
}

/// <summary>Everything the radio remembers between sessions, in %AppData%\StreetCorsa\radio.json</summary>
public sealed class RadioState
{
    public const string FileName = "radio.json";

    /// <summary>Where the music is: any folders on the PC, their subfolders too</summary>
    [JsonProperty("folders")] public List<string> Folders { get; set; } = [];
    [JsonProperty("playlists")] public List<RadioPlaylist> Playlists { get; set; } = [];
    [JsonProperty("shuffle")] public bool Shuffle { get; set; } = true;
    [JsonProperty("repeat"), JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))] public RadioRepeat Repeat { get; set; } = RadioRepeat.All;

    /// <summary>The radio comes on by itself when the game starts, where it was left</summary>
    [JsonProperty("autostart")] public bool AutoStart { get; set; }

    /// <summary>Whether it was on at the end of the last session</summary>
    [JsonProperty("on")] public bool WasOn { get; set; }

    /// <summary>The list that was playing (null: every song), the song and how far into it</summary>
    [JsonProperty("list")] public string? List { get; set; }
    [JsonProperty("song")] public string? Song { get; set; }
    [JsonProperty("position_ms")] public uint PositionMs { get; set; }

    /// <summary>The folder the radio starts with before the player names any: Documents\Street Corsa\Radio</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Street Corsa", "Radio");

    public static RadioState Load(string path)
    {
        RadioState? state = null;
        if (File.Exists(path)) state = JsonConvert.DeserializeObject<RadioState>(File.ReadAllText(path));

        state ??= new RadioState { Folders = [DefaultFolder] };
        state.Folders = state.Folders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        state.Playlists = state.Playlists.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        return state;
    }

    public void Save(string path) => SafeFile.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
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

    public sealed record ScanResult(IReadOnlyList<RadioSong> Songs, int NotPlayable, IReadOnlyList<string> MissingFolders);

    private sealed class CacheEntry
    {
        [JsonProperty("size")] public long Size { get; set; }
        [JsonProperty("written")] public long Written { get; set; }
        [JsonProperty("title")] public string? Title { get; set; }
        [JsonProperty("artist")] public string? Artist { get; set; }
        [JsonProperty("album")] public string? Album { get; set; }
        [JsonProperty("length_ms")] public uint LengthMs { get; set; }
    }

    /// <summary>
    /// Every playable song under the folders, with its tags: from the cache when the file has not changed, through
    /// <paramref name="read"/> when it has. A folder that is not there, or a subfolder that cannot be read, is skipped.
    /// On a worker.
    /// </summary>
    public static ScanResult Scan(IReadOnlyList<string> folders, string cachePath, Func<string, SongInfo?> read, CancellationToken cancel = default)
    {
        var cache = LoadCache(cachePath);
        var fresh = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        var songs = new List<RadioSong>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var notPlayable = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };

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
                if (!cache.TryGetValue(file, out var entry) || entry.Size != info.Length || entry.Written != written)
                {
                    var tags = read(file);
                    entry = new CacheEntry
                    {
                        Size = info.Length, Written = written, Title = tags?.Title, Artist = tags?.Artist, Album = tags?.Album,
                        LengthMs = tags?.LengthMs ?? 0
                    };
                }

                fresh[file] = entry;
                songs.Add(new RadioSong(file, entry.Title ?? TitleFromName(file), entry.Artist, entry.Album, entry.LengthMs));
            }
        }

        SaveCache(cachePath, fresh);
        songs.Sort((a, b) => string.Compare(a.Display, b.Display, StringComparison.CurrentCultureIgnoreCase));
        return new ScanResult(songs, notPlayable, missing);
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
            if (File.Exists(path) && JsonConvert.DeserializeObject<Dictionary<string, CacheEntry>>(File.ReadAllText(path)) is { } cache)
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
            SafeFile.WriteAllText(path, JsonConvert.SerializeObject(cache));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a cache: the next scan reads the tags again
        }
    }
}
