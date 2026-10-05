using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreetCorsa.Mods;

namespace StreetCorsa.Parts;

/// <summary>
/// The kinds of part on a shop's shelves ("engine blocks", "tyres"), from <see cref="FileName"/> in every mod's parts
/// folder: which script classes of the source game's framework each takes in, and what the game does with it (a part
/// that never mounts on its own kind, the bolt-ons the cars on the lots were worked on with, what the rivals' tuners go
/// for and how much, what a speed shop sells). The player reads a group by its words, <c>part_group.&lt;id&gt;</c> in
/// the text, else its <c>name</c>.
///
/// A part is in the first group with a class its script descends from ("ends with", the nearest framework class), in
/// the order the groups are listed: a later mod's new groups before the ones before them. A group of an id another mod
/// has goes over it field by field, where it was. A part no group takes in is an accessory.
///
/// The ids the code knows (<see cref="Blocks"/>, <see cref="Tyres"/>...) are the game's own vocabulary: the engine's
/// internals wear and break, the running gear has a slot on each wheel. A mod may change what their classes are, and add
/// groups of its own.
///
/// The file's <c>shows</c> are the parts view's buttons that put kinds of part out of sight (<see cref="PartShow"/>),
/// merged by id the same way, a later mod's new ones after the ones before.
///
/// Plain BCL and linked into EngineBench: the problems are kept (<see cref="Problems"/>) for the game to tell.
/// </summary>
public sealed class PartGroups
{
    public const string FileName = "part_groups.json";

    public const string Blocks = "engine_blocks";
    public const string CylinderHeads = "cylinder_heads";
    public const string Camshafts = "camshafts";
    public const string Crankshafts = "crankshafts";
    public const string ConnectingRods = "connecting_rods";
    public const string Pistons = "pistons";
    public const string Transmissions = "transmissions";
    public const string AirFilters = "air_filters";
    public const string Exhaust = "exhaust";
    public const string OilPans = "oil_pans";
    public const string Tyres = "tyres";
    public const string Rims = "rims";
    public const string Brakes = "brakes";
    public const string Springs = "springs";
    public const string Shocks = "shocks";

    /// <summary>A part no group takes in</summary>
    public const string Accessories = "accessories";

    private static readonly Lazy<PartGroups> InstalledGroups = new(() => Load(ModSet.Installed.Folders(ContentTypes.Parts).Select(f => f.Dir)));
    private static volatile PartGroups? _current;

    /// <summary>The groups of the mods next to the game, read once</summary>
    public static PartGroups Installed => InstalledGroups.Value;

    /// <summary>The groups in use: the installed ones, or ones a test or a tool sets</summary>
    public static PartGroups Current
    {
        get => _current ?? Installed;
        set => _current = value;
    }

    private readonly List<PartGroup> _groups;
    private readonly Dictionary<string, PartGroup> _byId;

    private PartGroups(List<PartGroup> groups, List<PartShow> shows, List<string> problems)
    {
        _groups = groups;
        _byId = groups.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
        Shows = shows;
        Problems = problems;
    }

    public IReadOnlyList<PartGroup> All => _groups;

    /// <summary>The parts view's buttons that put kinds of part out of sight, in order</summary>
    public IReadOnlyList<PartShow> Shows { get; }

    /// <summary>
    /// The show a part of <paramref name="group"/> goes with: the first that names its group, else, for a part of the
    /// engine (<paramref name="inEngine"/>), the one that takes the rest of the engine; null: always in sight
    /// </summary>
    public PartShow? ShowOf(string group, bool inEngine) =>
        Shows.FirstOrDefault(s => s.Groups.Contains(group, StringComparer.OrdinalIgnoreCase))
        ?? (inEngine ? Shows.FirstOrDefault(s => s.RestOfEngine == true) : null);

    public IReadOnlyList<string> Problems { get; }

    public PartGroup? Get(string? id) => id != null && _byId.TryGetValue(id, out var group) ? group : null;

    /// <summary>The group a part with this framework class is in; <see cref="Accessories"/> for none</summary>
    public string GroupOfClass(string frameworkClass)
    {
        foreach (var group in _groups)
        {
            if (group.Classes.Any(suffix => suffix.Length > 0 && frameworkClass.EndsWith(suffix, StringComparison.Ordinal))) return group.Id;
        }

        return Accessories;
    }

    /// <summary>Kinds that never mount on their own kind: a head does not go on a head, whatever a config says</summary>
    public bool NeverStacks(string group) => Get(group)?.NeverStacks == true;

    /// <summary>What people change on an engine without taking it apart: the cars on the lots were worked on with these</summary>
    public bool IsBoltOn(string group) => Get(group)?.BoltOn == true;

    /// <summary>How much a rival's tuner cares about a kind of part; 0: not tuned</summary>
    public int TunePriority(string group) => Get(group)?.Tune ?? 0;

    /// <summary>The kinds of part a speed shop sells: the ones that make power</summary>
    public bool SoldBySpeedShops(string group) => Get(group)?.SpeedShop == true;

    /// <summary>Every mod's file, in load order (the base game's first)</summary>
    public static PartGroups Load(IEnumerable<string> partsFolders)
    {
        var problems = new List<string>();
        var groups = new List<PartGroup>();
        var shows = new List<PartShow>();
        foreach (var dir in partsFolders)
        {
            var file = Path.Combine(dir, FileName);
            if (!File.Exists(file)) continue;

            List<PartGroup?>? read;
            List<PartShow?>? readShows;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file), ModJson.DocumentOptions);
                var root = document.RootElement;
                read = root.GetProperty("groups").Deserialize<List<PartGroup?>>(ModJson.Options);
                readShows = root.TryGetProperty("shows", out var showsElement) ? showsElement.Deserialize<List<PartShow?>>(ModJson.Options) : null;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
            {
                problems.Add($"{file}: {ex.Message}; left out");
                continue;
            }

            var added = new List<PartGroup>();
            foreach (var group in read ?? [])
            {
                if (group == null || string.IsNullOrWhiteSpace(group.Id))
                {
                    problems.Add($"{file}: a group without an id; left out");
                    continue;
                }

                group.Classes = (group.Classes ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
                if (groups.FirstOrDefault(g => g.Id.Equals(group.Id, StringComparison.OrdinalIgnoreCase)) is { } earlier)
                {
                    earlier.Over(group);
                    continue;
                }

                if (added.Any(g => g.Id.Equals(group.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"{file}: the group {group.Id} is there twice; the second is left out");
                    continue;
                }

                added.Add(group);
            }

            groups.InsertRange(0, added);

            foreach (var show in readShows ?? [])
            {
                if (show == null || string.IsNullOrWhiteSpace(show.Id))
                {
                    problems.Add($"{file}: a show without an id; left out");
                    continue;
                }

                show.Groups = (show.Groups ?? []).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
                if (shows.FirstOrDefault(s => s.Id.Equals(show.Id, StringComparison.OrdinalIgnoreCase)) is { } earlier) earlier.Over(show);
                else shows.Add(show);
            }
        }

        return new PartGroups(groups, shows, problems);
    }
}

/// <summary>
/// One of the parts view's buttons in part_groups.json's <c>shows</c>: the kinds of part it puts out of sight and back
/// (by group id), the rest of the engine's parts when <see cref="RestOfEngine"/>, its icon. The player reads
/// <c>part_show.&lt;id&gt;</c> as its tooltip.
/// </summary>
public sealed class PartShow
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The picture on the button: an image name as the theme has them ("icons/engine")</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("groups")]
    public List<string> Groups { get; set; } = new();

    /// <summary>Every part of the engine no other show takes</summary>
    [JsonPropertyName("rest_of_engine")]
    public bool? RestOfEngine { get; set; }

    /// <summary>Another mod's show of the same id, over this one where it says something</summary>
    internal void Over(PartShow other)
    {
        if (other.Icon != null) Icon = other.Icon;
        if (other.Groups.Count > 0) Groups = other.Groups;
        if (other.RestOfEngine != null) RestOfEngine = other.RestOfEngine;
    }
}

/// <summary>A kind of part in part_groups.json</summary>
public sealed class PartGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>What the player reads where the text has no <c>part_group.&lt;id&gt;</c></summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Simple names of the source game's framework classes, matched by "ends with": "Block" takes in Block_Vee_OHV</summary>
    [JsonPropertyName("classes")]
    public List<string> Classes { get; set; } = new();

    [JsonPropertyName("never_stacks")]
    public bool? NeverStacks { get; set; }

    [JsonPropertyName("bolt_on")]
    public bool? BoltOn { get; set; }

    /// <summary>How much a rival's tuner cares about it, 10 the most; none or 0: not tuned</summary>
    [JsonPropertyName("tune")]
    public int? Tune { get; set; }

    [JsonPropertyName("speed_shop")]
    public bool? SpeedShop { get; set; }

    /// <summary>Another mod's group of the same id, over this one where it says something</summary>
    internal void Over(PartGroup other)
    {
        if (other.Name != null) Name = other.Name;
        if (other.Classes.Count > 0) Classes = other.Classes;
        if (other.NeverStacks != null) NeverStacks = other.NeverStacks;
        if (other.BoltOn != null) BoltOn = other.BoltOn;
        if (other.Tune != null) Tune = other.Tune;
        if (other.SpeedShop != null) SpeedShop = other.SpeedShop;
    }
}
