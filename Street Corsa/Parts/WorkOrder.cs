using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json;

namespace StreetCorsa.Parts;

/// <summary>
/// What kind of job a part is on an engine, and what has to be off before it comes off (or goes back on): the
/// crankshaft only on the stand, with nothing on it and the oil pan off; the intake with the distributor out.
/// Read from <see cref="FileName"/> in every mod's parts folder, so a mod's blocks get rules of their own.
/// </summary>
/// <remarks>
/// Only the big parts have a class of the source game's framework to tell them by. Timing covers, chains, dampers,
/// distributors, water pumps, fans and belts are plain engine parts in every pack, but their names say what they are
/// in the same words across makes. So a part's kind is, in turn: the <c>"kind"</c> its pack gives it
/// (<see cref="PartDefinition.WorkKind"/>), the first kind listing one of its classes, the first kind with a word
/// its name holds. Kinds are tried in the order they are listed, a later mod's before the base game's.
/// </remarks>
public sealed class WorkOrder
{
    public const string FileName = "work_order.json";

    private readonly List<WorkKind> _kinds = new();
    private readonly Dictionary<string, WorkKind> _kindsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorkRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<PartWay>> _ways = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _standStrips = new();
    private readonly ConcurrentDictionary<PartDefinition, string?> _kindOf = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Kinds that come off an engine as it goes on the stand, in that order: what stays with the car (the radiator), and
    /// what is in the way of bolting it on by its bellhousing face
    /// </summary>
    public IReadOnlyList<string> StandStrips => _standStrips;

    /// <summary>Where a kind comes in <see cref="StandStrips"/>, any case; <see cref="int.MaxValue"/> for one not there</summary>
    public int StandStripTurnOf(string kind)
    {
        var turn = _standStrips.FindIndex(k => k.Equals(kind, StringComparison.OrdinalIgnoreCase));
        return turn < 0 ? int.MaxValue : turn;
    }

    public IReadOnlyCollection<WorkRule> Rules => _rules.Values;

    public static WorkOrder Empty => new();

    /// <summary>The kind of a part; null for one none of the kinds takes in, which has no rules</summary>
    public string? KindOf(PartDefinition part) => _kindOf.GetOrAdd(part, FindKind);

    public WorkRule? RuleOf(PartDefinition part) => KindOf(part) is { } kind ? _rules.GetValueOrDefault(kind) : null;

    /// <summary>The way a part comes off the engine, leg by leg; empty for a kind that does not say</summary>
    public IReadOnlyList<PartWay> WayOf(PartDefinition part) =>
        KindOf(part) is { } kind && _ways.TryGetValue(kind, out var way) ? way : Array.Empty<PartWay>();

    /// <summary>How a kind is named to the player: "oil pan", "cylinder heads" for more than one</summary>
    public string NameOf(string kind, int count = 1)
    {
        if (!_kindsById.TryGetValue(kind, out var found)) return kind.Replace('_', ' ');

        var name = string.IsNullOrWhiteSpace(found.Name) ? kind.Replace('_', ' ') : found.Name!;
        if (count <= 1) return name;

        return string.IsNullOrWhiteSpace(found.Plural) ? name.EndsWith('s') ? name : name + "s" : found.Plural!;
    }

    private string? FindKind(PartDefinition part)
    {
        if (!string.IsNullOrWhiteSpace(part.WorkKind)) return part.WorkKind;

        foreach (var kind in _kinds)
        {
            if (kind.Classes.Any(c => PartKinds.Is(part, c))) return kind.Id;
        }

        var name = (part.DisplayName ?? part.Name).ToLowerInvariant();
        foreach (var kind in _kinds)
        {
            if (kind.Names.Any(word => HasWord(name, word))) return kind.Id;
        }

        return null;
    }

    /// <summary>The word, or words, in the name as whole words, a plural s allowed: "fan" is in "fans", not in "fantastic"</summary>
    private static bool HasWord(string name, string word)
    {
        if (word.Length == 0) return false;

        for (var at = name.IndexOf(word, StringComparison.Ordinal); at >= 0; at = name.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            var end = at + word.Length;
            if (end < name.Length && name[end] == 's') end++;
            if ((at == 0 || !char.IsLetter(name[at - 1])) && (end == name.Length || !char.IsLetter(name[end]))) return true;
        }

        return false;
    }

    /// <summary>
    /// The work order of every mod, in load order (the base game first). A file that does not read is a work order
    /// that is not there: one line in <paramref name="problems"/>, and the parts it was about come off freely.
    /// </summary>
    public static WorkOrder Load(IEnumerable<(string ModId, bool IsBase, string Dir)> roots, List<string> problems)
    {
        var order = new WorkOrder();
        var kindsByMod = new List<List<WorkKind>>();

        foreach (var (modId, isBase, dir) in roots)
        {
            var file = Path.Combine(dir, FileName);
            if (!File.Exists(file)) continue;

            var label = (isBase ? string.Empty : modId + "/") + FileName;
            WorkOrderFile? read;
            try
            {
                read = JsonConvert.DeserializeObject<WorkOrderFile>(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{label}: {ex.Message}; its rules are left out");
                continue;
            }

            if (read == null) continue;

            var kinds = new List<WorkKind>();
            foreach (var kind in read.Kinds ?? new())
            {
                if (kind == null || string.IsNullOrWhiteSpace(kind.Id))
                {
                    problems.Add($"{label}: a kind without an id is left out");
                    continue;
                }

                kind.Classes = PartDefinition.NonNull(kind.Classes);
                kind.Names = PartDefinition.NonNull(kind.Names).Select(n => n.ToLowerInvariant()).ToList();
                kinds.Add(kind);

                // The first mod to name a kind names it for the player; a later one may only add ways to tell it
                if (!order._kindsById.ContainsKey(kind.Id)) order._kindsById[kind.Id] = kind;

                // Which way it comes off is only looks: the last mod to say so says so
                if (kind.ComesOff is { } legs)
                {
                    var way = new List<PartWay>();
                    foreach (var leg in legs)
                    {
                        if (WayNamed(leg) is { } parsed) way.Add(parsed);
                        else problems.Add($"{label}: {kind.Id} comes off \"{leg}\", which is none of up, down, forward, back, out; left out");
                    }

                    // Not one leg that reads: the way it came off before stands, rather than none at all
                    if (way.Count > 0 || legs.Count == 0) order._ways[kind.Id] = way;
                    else if (order._ways.ContainsKey(kind.Id)) problems.Add($"{label}: {kind.Id} keeps the way it comes off as before");
                }
            }

            kindsByMod.Add(kinds);

            foreach (var rule in read.Rules ?? new())
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Kind))
                {
                    problems.Add($"{label}: a rule without a kind is left out");
                    continue;
                }

                if (order._rules.ContainsKey(rule.Kind) && !rule.Replace)
                {
                    problems.Add($"{label}: {rule.Kind} has a rule already; say \"replace\": true to change it. Left out");
                    continue;
                }

                rule.After = PartDefinition.NonNull(rule.After);
                order._rules[rule.Kind] = rule;
            }

            // The stand is the stand: the last mod to say what comes off for it says so for every block
            if (read.StandStrips is { } strips)
            {
                order._standStrips.Clear();
                order._standStrips.AddRange(strips.OfType<string>());
            }
        }

        // A later mod's kinds are tried first: it knows its own parts better than the base game's words do
        for (var i = kindsByMod.Count - 1; i >= 0; i--) order._kinds.AddRange(kindsByMod[i]);

        foreach (var rule in order._rules.Values)
        {
            foreach (var after in rule.After.Where(a => !order._kindsById.ContainsKey(a)))
                problems.Add($"{FileName}: the rule of {rule.Kind} waits for {after}, a kind no work order lists");
        }

        return order;
    }

    /// <summary>
    /// One way by its name, any case: not a number, nor a list of names, which the enum would read as the sum of them
    /// </summary>
    private static PartWay? WayNamed(string? name)
    {
        var trimmed = name?.Trim();
        foreach (var way in Enum.GetValues<PartWay>())
        {
            if (way.ToString().Equals(trimmed, StringComparison.OrdinalIgnoreCase)) return way;
        }

        return null;
    }

    private sealed class WorkOrderFile
    {
        [JsonProperty("kinds")]
        public List<WorkKind?>? Kinds { get; set; }

        [JsonProperty("rules")]
        public List<WorkRule?>? Rules { get; set; }

        [JsonProperty("stand_strips")]
        public List<string?>? StandStrips { get; set; }
    }
}

/// <summary>A kind of part as the work order knows it, and how to tell a part is one</summary>
public sealed class WorkKind
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>What the player reads: "oil pan"</summary>
    [JsonProperty("name")]
    public string? Name { get; set; }

    /// <summary>For more than one, when adding an s will not do</summary>
    [JsonProperty("plural")]
    public string? Plural { get; set; }

    /// <summary>Simple names of script classes, any in the part's class chain ("Crankshaft")</summary>
    [JsonProperty("classes")]
    public List<string> Classes { get; set; } = new();

    /// <summary>Words the part's name holds, lower case ("timing chain cover")</summary>
    [JsonProperty("names")]
    public List<string> Names { get; set; } = new();

    /// <summary>
    /// How the part is seen to come off, leg by leg, in the engine's own directions (a <see cref="PartWay"/> each):
    /// ["back", "down"] for a gearbox. Parts mounted on it go along. Null leaves it to the kind as another mod says.
    /// </summary>
    [JsonProperty("comes_off")]
    public List<string?>? ComesOff { get; set; }
}

/// <summary>
/// A direction a part comes off in, as the engine stands: up and down, forward towards the crankshaft's nose, back
/// towards the gearbox, out away from the engine on the side the part is on
/// </summary>
public enum PartWay
{
    Up,
    Down,
    Forward,
    Back,
    Out
}

/// <summary>What has to be the case before a part of a kind comes off an engine, or goes on one</summary>
public sealed class WorkRule
{
    [JsonProperty("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Only with the engine on the stand, never in the car</summary>
    [JsonProperty("stand_only")]
    public bool StandOnly { get; set; }

    /// <summary>With nothing on it: it comes off, and goes on, by itself</summary>
    [JsonProperty("bare")]
    public bool Bare { get; set; }

    /// <summary>Kinds that must be off the engine first, the ones on this very part too</summary>
    [JsonProperty("after")]
    public List<string> After { get; set; } = new();

    /// <summary>A mod changing a rule it did not write: on purpose, or it is reported and left out</summary>
    [JsonProperty("replace")]
    public bool Replace { get; set; }
}
