using System.IO;
using Newtonsoft.Json;

namespace StreetCorsa.Mods;

/// <summary>The folder a kind of content lives in, in every mod</summary>
public static class ContentTypes
{
    public const string Parts = "parts";
    public const string Sounds = "sounds";
    public const string Dealers = "dealers";
    public const string PartShops = "partshops";
    public const string Racers = "racers";
    public const string Events = "events";
    public const string Milestones = "milestones";
    public const string Victories = "victories";
    public const string Streets = "streets";
    public const string Stations = "stations";
    public const string Showcase = "showcase";
    public const string Garages = "garages";
    public const string Text = "text";
}

/// <summary>A mod's mod.json, and the folder it was found in</summary>
public sealed class ModInfo
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("version")]
    public string Version { get; set; } = string.Empty;

    [JsonProperty("author")]
    public string Author { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Mods load in this order after the base game, lowest first; ties by id</summary>
    [JsonProperty("order")]
    public int Order { get; set; }

    [JsonIgnore]
    public string Folder { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsBase => ModIds.IsBase(Id);

    /// <summary>The mod's folder for a kind of content, null when it has none</summary>
    public string? Dir(string type)
    {
        var dir = Path.Combine(Folder, type);
        return Directory.Exists(dir) ? dir : null;
    }

    public override string ToString() => string.IsNullOrEmpty(Version) ? Id : $"{Id} {Version}";
}

/// <summary>
/// The mods under the Content folder, in load order: the base game first (Content\streetcorsa), then every folder
/// with a mod.json. A folder whose name starts with '_' or '.' is switched off. Each kind of content is read from
/// every mod that has its folder, in this order, and merged (see <see cref="ContentRegistry{T}"/>).
///
/// Plain BCL on purpose: the tools compile it by link.
/// </summary>
public sealed class ModSet
{
    public const string FolderName = "Content";
    public const string ManifestFileName = "mod.json";

    private static readonly Lazy<ModSet> InstalledSet = new(() => Load(DefaultRoot));

    private readonly List<string> _problems = new();

    private ModSet(string root, List<ModInfo> mods)
    {
        Root = root;
        Mods = mods;
    }

    /// <summary>The Content folder next to the game</summary>
    public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, FolderName);

    /// <summary>The mods next to the game, read once</summary>
    public static ModSet Installed => InstalledSet.Value;

    public string Root { get; }

    /// <summary>Base game first, then the others in load order</summary>
    public IReadOnlyList<ModInfo> Mods { get; }

    /// <summary>The base game; a stand-in pointing at a folder that is not there when the install has lost it</summary>
    public ModInfo Base => Mods.FirstOrDefault(m => m.IsBase)
        ?? new ModInfo { Id = ModIds.BaseId, Name = "Street Corsa", Folder = Path.Combine(Root, ModIds.BaseId) };

    /// <summary>Mods that could not be read or were left out, and why: one line each</summary>
    public IReadOnlyList<string> Problems => _problems;

    public ModInfo? Get(string id) => Mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Only the base game, from a folder of its own (tests, tools)</summary>
    public static ModSet BaseOnly(string baseFolder)
    {
        var info = ReadManifest(baseFolder, out _) ?? new ModInfo();
        info.Id = ModIds.BaseId;
        info.Folder = baseFolder;
        if (string.IsNullOrEmpty(info.Name)) info.Name = "Street Corsa";
        return new ModSet(Path.GetDirectoryName(baseFolder) ?? baseFolder, new List<ModInfo> { info });
    }

    /// <remarks>Never throws for what is in the folder: a mod that does not read is left out, with a line in <see cref="Problems"/></remarks>
    public static ModSet Load(string root)
    {
        var problems = new List<string>();
        var mods = new List<ModInfo>();

        if (Directory.Exists(root))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith('_') || name.StartsWith('.')) continue;

                var isBase = ModIds.IsBase(name);
                var info = ReadManifest(folder, out var problem);
                if (problem != null)
                {
                    problems.Add($"{name}: {problem}");
                    if (!isBase) continue;
                }

                if (info == null)
                {
                    // The base game is the base game with or without its mod.json; any other folder needs one
                    if (!isBase)
                    {
                        problems.Add($"{name}: no {ManifestFileName}, not a mod; left out");
                        continue;
                    }

                    info = new ModInfo();
                }

                if (isBase) info.Id = ModIds.BaseId;
                if (string.IsNullOrWhiteSpace(info.Id)) info.Id = name.ToLowerInvariant();
                if (!ModIds.IsValidModId(info.Id))
                {
                    problems.Add($"{name}: \"{info.Id}\" is no mod id (lower case letters, digits, '_' and '-'; not \"car\"); left out");
                    continue;
                }

                if (!isBase && ModIds.IsBase(info.Id))
                {
                    problems.Add($"{name}: the id \"{info.Id}\" is the base game's; left out");
                    continue;
                }

                if (!seen.Add(info.Id))
                {
                    problems.Add($"{name}: another folder is already mod \"{info.Id}\"; left out");
                    continue;
                }

                info.Folder = folder;
                if (string.IsNullOrWhiteSpace(info.Name)) info.Name = isBase ? "Street Corsa" : info.Id;
                mods.Add(info);
            }
        }

        if (!mods.Any(m => m.IsBase)) problems.Add($"The base game's content ({Path.Combine(root, ModIds.BaseId)}) is missing");

        var ordered = mods.Where(m => m.IsBase)
            .Concat(mods.Where(m => !m.IsBase).OrderBy(m => m.Order).ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var set = new ModSet(root, ordered);
        set._problems.AddRange(problems);
        return set;
    }

    private static ModInfo? ReadManifest(string folder, out string? problem)
    {
        problem = null;
        var file = Path.Combine(folder, ManifestFileName);
        if (!File.Exists(file)) return null;

        try
        {
            return JsonConvert.DeserializeObject<ModInfo>(File.ReadAllText(file)) ?? new ModInfo();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = $"{ManifestFileName} could not be read ({ex.Message})";
            return null;
        }
    }

    /// <summary>Every mod that has a folder for this kind of content, in load order</summary>
    public IEnumerable<(ModInfo Mod, string Dir)> Folders(string type)
    {
        foreach (var mod in Mods)
        {
            if (mod.Dir(type) is { } dir) yield return (mod, dir);
        }
    }

    /// <summary>The .json files directly in each mod's folder for this kind of content: mods in load order, files by name</summary>
    public IEnumerable<(ModInfo Mod, string File)> JsonFiles(string type)
    {
        foreach (var (mod, dir) in Folders(type))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
                yield return (mod, file);
        }
    }

    /// <summary>The folders in each mod's folder for this kind of content (a street, a sound, a garage)</summary>
    public IEnumerable<(ModInfo Mod, string Dir)> SubFolders(string type)
    {
        foreach (var (mod, dir) in Folders(type))
        {
            foreach (var sub in Directory.EnumerateDirectories(dir).Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(sub);
                if (!name.StartsWith('_') && !name.StartsWith('.')) yield return (mod, sub);
            }
        }
    }

    /// <summary>Id and version of each mod, for a save to remember what it was made with</summary>
    public List<ModRef> References() => Mods.Select(m => new ModRef { Id = m.Id, Name = m.Name, Version = m.Version }).ToList();
}

/// <summary>A mod as a save remembers it</summary>
public sealed class ModRef
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    public override string ToString() => string.IsNullOrEmpty(Version) ? Name : $"{Name} {Version}";
}
