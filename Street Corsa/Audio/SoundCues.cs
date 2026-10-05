using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Helpers;
using StreetCorsa.Mods;

namespace StreetCorsa.Audio;

/// <summary>
/// A named sound the game plays at a moment of its own (a click, a part coming off, a place's air): the files it picks
/// from, the bus, how loud, how much its pitch wanders, whether it loops, and how soon it may play again.
/// </summary>
public sealed record SoundCue(
    string Name,
    AudioBus Bus,
    IReadOnlyList<string> Files,
    float Volume,
    float Pitch,
    bool Loop,
    float Cooldown);

/// <summary>
/// The cues, out of every mod's sfx folder (<c>Content/&lt;mod&gt;/sfx/*.json</c>), in load order: a mod's cue of a
/// name the base game has replaces it, so a mod can change any sound the game makes. A file is named relative to the
/// json that names it.
/// <code>
/// { "cues": { "ui.click": { "bus": "interface", "volume": 0.45, "pitch": 0.04, "files": ["ui/click.ogg"] } } }
/// </code>
/// A cue marked <c>"optional": true</c> may name files that are not there: it is no problem, the cue is only silent.
/// A file outside the mod's folder is never played, nor is a one-shot's file bigger than <see cref="LargestOneShot"/>
/// (each is held in memory, decoded, from the start).
/// </summary>
public sealed class SoundCues
{
    /// <summary>The biggest file a one-shot cue plays: a click or a part coming off is a few hundred KB</summary>
    public const long LargestOneShot = 5L * 1024 * 1024;

    private readonly Dictionary<string, SoundCue> _cues = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _problems = [];

    public static readonly SoundCues Empty = new();

    public IReadOnlyCollection<SoundCue> All => _cues.Values;

    /// <summary>What could not be read: files, cues, sounds that are not there</summary>
    public IReadOnlyList<string> Problems => _problems;

    public SoundCue? Get(string name) => _cues.GetValueOrDefault(name);

    private sealed class CueJson
    {
        [JsonPropertyName("bus")] public string? Bus { get; set; }
        [JsonPropertyName("files")] public List<string>? Files { get; set; }
        [JsonPropertyName("volume")] public float? Volume { get; set; }
        [JsonPropertyName("pitch")] public float? Pitch { get; set; }
        [JsonPropertyName("loop")] public bool? Loop { get; set; }
        [JsonPropertyName("cooldown")] public float? Cooldown { get; set; }
        [JsonPropertyName("optional")] public bool? Optional { get; set; }
    }

    private sealed class FileJson
    {
        [JsonPropertyName("cues")] public Dictionary<string, CueJson>? Cues { get; set; }
    }

    public static SoundCues Load(ModSet mods)
    {
        var cues = new SoundCues();
        // A cue a mod takes out (its mod.json's "removes") plays nothing, unless the mod or a later one defines it again
        var removals = mods.Removals(ContentTypes.Sfx, (name, _) => cues._cues.Remove(name), "the cue", cues._problems);
        foreach (var (mod, file) in mods.JsonFiles(ContentTypes.Sfx))
        {
            removals.Before(mod);
            cues.Read(file, $"{mod.Id}/{ContentTypes.Sfx}/{Path.GetFileName(file)}", mod.Folder);
        }

        removals.Rest();

        return cues;
    }

    /// <summary>The cues of one file, over those read before; their sounds inside <paramref name="root"/> (the mod's folder; the file's own when none)</summary>
    public void Read(string file, string shownAs, string? root = null)
    {
        FileJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<FileJson>(File.ReadAllText(file), ModJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _problems.Add($"{shownAs} could not be read: {ex.Message}");
            return;
        }

        var dir = Path.GetDirectoryName(file)!;
        root = string.IsNullOrEmpty(root) ? dir : root;
        foreach (var (name, json) in parsed?.Cues ?? [])
        {
            if (Parse(name, json, dir, root, shownAs) is { } cue) _cues[name] = cue;
        }
    }

    private SoundCue? Parse(string name, CueJson json, string dir, string root, string shownAs)
    {
        if (!Enum.TryParse<AudioBus>(json.Bus, ignoreCase: true, out var bus))
        {
            _problems.Add($"{shownAs}: {name} has no bus it can play on (\"{json.Bus}\"); one of {string.Join(", ", Enum.GetNames<AudioBus>()).ToLowerInvariant()}");
            return null;
        }

        // An optional cue's files may be missing (music a copy of the game is not allowed to ship): it plays what is
        // there, and nothing, without a word, when none is
        var optional = json.Optional ?? false;
        var loop = json.Loop ?? false;
        var files = new List<string>();
        foreach (var relative in json.Files ?? [])
        {
            // Named from the json's folder, and kept inside the mod's: never a path on another drive or another machine
            // (a share's name alone makes Windows sign in to it)
            if (string.IsNullOrWhiteSpace(relative) || !PathNames.TryCombineUnder(root, Path.Combine(Path.GetRelativePath(root, dir), relative), out var path))
            {
                _problems.Add($"{shownAs}: {name} names {relative}, which is not inside its mod");
                continue;
            }

            var found = new FileInfo(path);
            if (!found.Exists)
            {
                if (!optional) _problems.Add($"{shownAs}: {name} names {relative}, which is not there");
            }
            else if (!loop && found.Length > LargestOneShot)
            {
                _problems.Add($"{shownAs}: {name} names {relative}, too big for a short sound ({found.Length / 1024} KB; at most {LargestOneShot / 1024} KB): a long sound is a loop");
            }
            else
            {
                files.Add(path);
            }
        }

        if (files.Count == 0)
        {
            if (!optional) _problems.Add($"{shownAs}: {name} has no sound to play");
            return null;
        }

        return new SoundCue(name, bus, files,
            Math.Clamp(json.Volume ?? 1f, 0f, 2f),
            Math.Clamp(json.Pitch ?? 0f, 0f, 0.5f),
            loop,
            Math.Max(0f, json.Cooldown ?? 0f));
    }

    /// <summary>
    /// Which of a cue's files plays next: any but the one that played last, so a sound heard twice in a row is never
    /// the very same take
    /// </summary>
    public static int NextFile(int count, int last, Random random)
    {
        if (count <= 1) return 0;
        var pick = random.Next(count - 1);
        return last >= 0 && last < count && pick >= last ? pick + 1 : pick;
    }

    /// <summary>A playback speed around 1, up to <paramref name="spread"/> either way</summary>
    public static float Pitch(float spread, Random random) => spread <= 0f ? 1f : 1f + ((float)random.NextDouble() * 2f - 1f) * spread;
}
