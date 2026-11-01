using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace StreetCorsa.Parts;

/// <summary>
/// What kind of job a part is on an engine or a car's running gear, and what has to be off before it comes off (or goes
/// back on): the crankshaft only on the stand, with nothing on it and the oil pan off; the intake with the distributor
/// out; the belt only over the water pump and the fan; a drum with its wheel off, the rear axle on its leaf springs.
/// <see cref="Cars.EngineWork"/> puts it to an engine, <see cref="Cars.CarWork"/> to the rest of a car.
/// Read from <see cref="FileName"/> in every mod's parts folder, so a mod's blocks get rules of their own.
/// </summary>
/// <remarks>
/// Only the big parts have a class of their own to tell them by. Timing covers, chains, dampers,
/// distributors, water pumps, fans and belts are plain engine parts in every pack, but their names say what they are
/// in the same words across makes. So a part's kind is, in turn: the <c>"kind"</c> its pack gives it
/// (<see cref="PartDefinition.WorkKind"/>), the first kind listing one of its classes, the first kind with a word
/// its name holds. Kinds are tried in the order they are listed, a later mod's before the core's.
/// </remarks>
public sealed class WorkOrder
{
    public const string FileName = "work_order.json";

    /// <summary>What a kind can do for the engine's cooling (<see cref="WorkKind.Cooling"/>); "none" says it does nothing</summary>
    public static readonly IReadOnlyList<string> CoolingRoles = [CoolingRadiator, CoolingWaterPump, CoolingFan, CoolingNone];

    public const string CoolingRadiator = "radiator";
    public const string CoolingWaterPump = "water_pump";
    public const string CoolingFan = "fan";
    public const string CoolingNone = "none";

    private readonly List<WorkKind> _kinds = new();
    private readonly Dictionary<string, WorkKind> _kindsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorkRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _serves = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<PartWay>> _ways = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EngineSide> _sides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _cooling = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _standStrips = new();
    private readonly ConcurrentDictionary<PartDefinition, string?> _kindOf = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<PartDefinition, IReadOnlyList<PartWay>?> _ownWayOf = new(ReferenceEqualityComparer.Instance);

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

    /// <summary>The ids of every kind, in the order they are tried</summary>
    public IEnumerable<string> KindIds => _kinds.Select(k => k.Id);

    public static WorkOrder Empty => new();

    /// <summary>The kind of a part; null for one none of the kinds takes in, which has no rules</summary>
    public string? KindOf(PartDefinition part) => _kindOf.GetOrAdd(part, FindKind);

    public WorkRule? RuleOf(PartDefinition part) => KindOf(part) is { } kind ? RuleFor(kind) : null;

    /// <summary>The rule of a kind; null for one without</summary>
    public WorkRule? RuleFor(string kind) => _rules.GetValueOrDefault(kind);

    /// <summary>
    /// A part of the kind does the job a part that needs <paramref name="needed"/> is after: a fan, or a radiator
    /// with an electric fan of its own (<see cref="WorkKind.Serves"/>)
    /// </summary>
    public bool DoesJobOf(string kind, string needed) =>
        kind.Equals(needed, StringComparison.OrdinalIgnoreCase) || _serves.TryGetValue(kind, out var serves) && serves.Contains(needed);

    /// <summary>
    /// The way a part comes off the engine, leg by leg: its own (<see cref="PartDefinition.ComesOff"/>), or else its
    /// kind's; empty for a kind that does not say
    /// </summary>
    public IReadOnlyList<PartWay> WayOf(PartDefinition part)
    {
        if (part.ComesOff != null && _ownWayOf.GetOrAdd(part, p => ReadWay(p.ComesOff!, null, null)) is { } own) return own;

        return KindOf(part) is { } kind && _ways.TryGetValue(kind, out var way) ? way : Array.Empty<PartWay>();
    }

    /// <summary>
    /// The tool a part of the kind is heard to come off and go on with (<see cref="WorkKind.Tool"/>): <c>"impact"</c> for the
    /// air gun; null for a kind that does not say, which takes the socket wrench
    /// </summary>
    public string? ToolOf(string? kind) => kind != null ? _tools.GetValueOrDefault(kind) : null;

    /// <summary>Whether any kind says what it does for the cooling (<see cref="WorkKind.Cooling"/>)</summary>
    public bool TellsCooling => _cooling.Count > 0;

    /// <summary>
    /// What a part does for the engine's cooling, as its kind says (<see cref="WorkKind.Cooling"/>): one of
    /// <see cref="CoolingRoles"/>; null for a kind that does not say
    /// </summary>
    public string? CoolingOf(PartDefinition part) => KindOf(part) is { } kind ? _cooling.GetValueOrDefault(kind) : null;

    /// <summary>A part of a cooling kind that does the job of a fan too: its kind serves a kind that is a fan (a radiator with an electric fan)</summary>
    public bool HasOwnFan(PartDefinition part) =>
        KindOf(part) is { } kind && _serves.TryGetValue(kind, out var serves)
        && serves.Any(served => _cooling.GetValueOrDefault(served) == CoolingFan);

    /// <summary>
    /// Which side of the engine a part is worked on from, which turns the engine on the stand that side up
    /// (<see cref="WorkKind.WorkedFrom"/>); null for a kind that does not say, which leaves the engine as it is
    /// </summary>
    public EngineSide? SideOf(PartDefinition part) => KindOf(part) is { } kind && _sides.TryGetValue(kind, out var side) ? side : null;

    /// <summary>
    /// Every part that says how it comes off, told in <paramref name="problems"/> of a way it says that is no way;
    /// a part with not one leg that reads comes off the way its kind does
    /// </summary>
    public static void CheckWays(IEnumerable<PartDefinition> parts, List<string> problems)
    {
        foreach (var part in parts.Where(p => p.ComesOff != null)) ReadWay(part.ComesOff!, part.Id, problems);
    }

    /// <summary>
    /// A way to come off read leg by leg, the legs that are none of the ways left out and told of; null when there
    /// were legs but not one of them read, an empty list only for no legs at all
    /// </summary>
    private static List<PartWay>? ReadWay(IEnumerable<string?> legs, string? label, List<string>? problems)
    {
        var way = new List<PartWay>();
        var any = false;
        foreach (var leg in legs)
        {
            any = true;
            if (Named<PartWay>(leg) is { } parsed) way.Add(parsed);
            else problems?.Add($"{label} comes off \"{leg}\", which is none of up, down, forward, back, out; left out");
        }

        return way.Count > 0 || !any ? way : null;
    }

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
    /// The work order of every mod, in load order (the core first). A file that does not read is a work order
    /// that is not there: one line in <paramref name="problems"/>, and the parts it was about come off freely.
    /// </summary>
    public static WorkOrder Load(IEnumerable<(string ModId, bool IsBase, string Dir)> roots, List<string> problems)
    {
        var order = new WorkOrder();
        var kindsByMod = new List<List<WorkKind>>();

        ModBookFile.ReadEach<WorkOrderFile>(roots, FileName, problems, "its rules are left out", (label, read) =>
        {
            var kinds = new List<WorkKind>();
            foreach (var kind in read.Kinds ?? new())
            {
                if (order.ReadKind(kind, label, problems)) kinds.Add(kind!);
            }

            kindsByMod.Add(kinds);

            foreach (var rule in read.Rules ?? new()) order.ReadRule(rule, label, problems);

            // The stand is the stand: the last mod to say what comes off for it says so for every block
            if (read.StandStrips is { } strips)
            {
                order._standStrips.Clear();
                order._standStrips.AddRange(strips.OfType<string>());
            }
        });

        // A later mod's kinds are tried first: it knows its own parts better than the core's words do
        for (var i = kindsByMod.Count - 1; i >= 0; i--) order._kinds.AddRange(kindsByMod[i]);

        order.Validate(problems);
        return order;
    }

    /// <summary>
    /// One kind of a mod's file into the order: its name (the first mod to name a kind names it for the player), the
    /// jobs it does (they add up), and its looks and sound (the way it comes off, the side it is worked from, its cooling
    /// role, its tool: the last mod to say so says so). False for a kind without an id, which is left out.
    /// </summary>
    private bool ReadKind(WorkKind? kind, string label, List<string> problems)
    {
        if (kind == null || string.IsNullOrWhiteSpace(kind.Id))
        {
            problems.Add($"{label}: a kind without an id is left out");
            return false;
        }

        kind.Classes = PartDefinition.NonNull(kind.Classes);
        kind.Names = PartDefinition.NonNull(kind.Names).Select(n => n.ToLowerInvariant()).ToList();

        // The first mod to name a kind names it for the player; a later one may only add ways to tell it
        if (!_kindsById.ContainsKey(kind.Id)) _kindsById[kind.Id] = kind;

        // The jobs it does add up: a later mod may find it another
        kind.Serves = PartDefinition.NonNull(kind.Serves);
        if (kind.Serves.Count > 0)
        {
            if (!_serves.TryGetValue(kind.Id, out var serves)) _serves[kind.Id] = serves = new(StringComparer.OrdinalIgnoreCase);
            serves.UnionWith(kind.Serves);
        }

        // Which way it comes off is only looks: the last mod to say so says so
        if (kind.ComesOff is { } legs)
        {
            // Not one leg that reads: the way it came off before stands, rather than none at all
            if (ReadWay(legs, $"{label}: {kind.Id}", problems) is { } way) _ways[kind.Id] = way;
            else if (_ways.ContainsKey(kind.Id)) problems.Add($"{label}: {kind.Id} keeps the way it comes off as before");
        }

        // What it does for the cooling: the last mod to say so says so
        if (kind.Cooling is { } cooling)
        {
            var role = cooling.Trim().ToLowerInvariant();
            if (CoolingRoles.Contains(role)) _cooling[kind.Id] = role;
            else problems.Add($"{label}: {kind.Id} does \"{cooling}\" for the cooling, which is none of {string.Join(", ", CoolingRoles)}; left out");
        }

        // The tool it is heard to take is only sound: the last mod to say so says so
        if (kind.Tool is { } tool && tool.Trim() is { Length: > 0 } named) _tools[kind.Id] = named.ToLowerInvariant();

        // Which side it is worked from is only looks too
        if (kind.WorkedFrom is { } from)
        {
            if (Named<EngineSide>(from) is { } side) _sides[kind.Id] = side;
            else problems.Add($"{label}: {kind.Id} is worked from \"{from}\", which is neither above nor below; left out");
        }

        return true;
    }

    /// <summary>
    /// One rule of a mod's file into the order. A rule for a kind that has one already only adds to it: more to wait
    /// for, more it needs. Taking any of it back is on purpose (<see cref="WorkRule.Replace"/>), so a mod written before
    /// the core ruled a kind still counts. A rule without a kind is left out.
    /// </summary>
    private void ReadRule(WorkRule? rule, string label, List<string> problems)
    {
        if (rule == null || string.IsNullOrWhiteSpace(rule.Kind))
        {
            problems.Add($"{label}: a rule without a kind is left out");
            return;
        }

        rule.After = PartDefinition.NonNull(rule.After);
        rule.Needs = PartDefinition.NonNull(rule.Needs);
        rule.HangsOn = PartDefinition.NonNull(rule.HangsOn);

        if (_rules.TryGetValue(rule.Kind, out var ruled) && !rule.Replace)
        {
            ruled.StandOnly |= rule.StandOnly;
            ruled.Bare |= rule.Bare;
            ruled.Lifted |= rule.Lifted;
            ruled.After = ruled.After.Union(rule.After, StringComparer.OrdinalIgnoreCase).ToList();
            ruled.Needs = ruled.Needs.Union(rule.Needs, StringComparer.OrdinalIgnoreCase).ToList();
            ruled.HangsOn = ruled.HangsOn.Union(rule.HangsOn, StringComparer.OrdinalIgnoreCase).ToList();
            return;
        }

        _rules[rule.Kind] = rule;
    }

    /// <summary>
    /// What the rules of every mod come to together, once all are read: a rule that names a kind no work order lists, one
    /// that both needs a kind and waits for it to be off, and kinds whose rules lead round to each other
    /// (<see cref="Loops"/>), each a line in <paramref name="problems"/>; the order stands as it is
    /// </summary>
    private void Validate(List<string> problems)
    {
        foreach (var rule in _rules.Values)
        {
            foreach (var after in rule.After.Where(a => !_kindsById.ContainsKey(a)))
                problems.Add($"{FileName}: the rule of {rule.Kind} waits for {after}, a kind no work order lists");
            foreach (var need in rule.Needs.Where(n => !_kindsById.ContainsKey(n)))
                problems.Add($"{FileName}: the rule of {rule.Kind} needs {need}, a kind no work order lists");
            foreach (var on in rule.HangsOn.Where(h => !_kindsById.ContainsKey(h)))
                problems.Add($"{FileName}: the rule of {rule.Kind} hangs on {on}, a kind no work order lists");
            foreach (var both in rule.Needs.Intersect(rule.After, StringComparer.OrdinalIgnoreCase))
                problems.Add($"{FileName}: the rule of {rule.Kind} both needs {both} and waits for it to be off: {rule.Kind} would never go on");
        }

        foreach (var loop in Loops(_rules.Values, r => r.After, withItself: true))
            problems.Add($"{FileName}: {string.Join(", ", loop)} wait for each other to be off: none of them would come off");
        foreach (var loop in Loops(_rules.Values, r => r.Needs, withItself: false))
            problems.Add($"{FileName}: {string.Join(", ", loop)} need each other on: none of them would go on");
    }

    /// <summary>
    /// Kinds whose rules lead from one to the next and back to the first: each set of kinds that lead to each other
    /// once (a strongly connected component, Tarjan's way), its kinds from the first by name on, in the order they
    /// lead. A rule waiting for its own kind is a loop (two cylinder heads, each in the other's way); one needing its
    /// own kind is not (the part is of that kind itself).
    /// </summary>
    /// <remarks>
    /// Linear in the rules and what they name, and without recursion: a work order is any mod's file, and listing
    /// every simple loop of a dense one would take for ever at catalog load.
    /// </remarks>
    private static List<List<string>> Loops(IEnumerable<WorkRule> rules, Func<WorkRule, List<string>> next, bool withItself)
    {
        var leads = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules) leads[rule.Kind] = next(rule);

        // Every kind by the one spelling its rule has, so a kind named in another case is the same kind
        var kindOf = leads.Keys.ToDictionary(k => k, k => k, StringComparer.OrdinalIgnoreCase);
        var edges = leads.ToDictionary(l => l.Key, l => l.Value.Select(to => kindOf.GetValueOrDefault(to)).OfType<string>().ToList(),
            StringComparer.OrdinalIgnoreCase);
        List<string> To(string kind) => edges[kind];

        var number = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var low = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var open = new Stack<string>();
        var onOpen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new List<List<string>>();

        foreach (var first in leads.Keys)
        {
            if (number.ContainsKey(first)) continue;

            var work = new Stack<(string Kind, int Edge)>();
            Visit(first);
            while (work.Count > 0)
            {
                var (kind, edge) = work.Pop();
                var leadsTo = To(kind);
                if (edge < leadsTo.Count)
                {
                    work.Push((kind, edge + 1));
                    var to = leadsTo[edge];
                    if (!number.ContainsKey(to)) Visit(to);
                    else if (onOpen.Contains(to)) low[kind] = Math.Min(low[kind], number[to]);
                    continue;
                }

                if (work.Count > 0)
                {
                    var parent = work.Peek().Kind;
                    low[parent] = Math.Min(low[parent], low[kind]);
                }

                if (low[kind] != number[kind]) continue;

                var component = new List<string>();
                string member;
                do
                {
                    member = open.Pop();
                    onOpen.Remove(member);
                    component.Add(member);
                } while (!member.Equals(kind, StringComparison.OrdinalIgnoreCase));

                components.Add(component);
            }

            void Visit(string kind)
            {
                number[kind] = low[kind] = number.Count;
                open.Push(kind);
                onOpen.Add(kind);
                work.Push((kind, 0));
            }
        }

        var loops = new List<List<string>>();
        foreach (var component in components)
        {
            var members = new HashSet<string>(component, StringComparer.OrdinalIgnoreCase);
            var head = component.Order(StringComparer.OrdinalIgnoreCase).First();
            if (component.Count == 1 && !(withItself && To(head).Contains(head, StringComparer.OrdinalIgnoreCase))) continue;

            // From the first by name, along the rules, each kind once
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var walk = new Stack<string>();
            walk.Push(head);
            while (walk.Count > 0)
            {
                var kind = walk.Pop();
                if (!seen.Add(kind)) continue;

                ordered.Add(kind);
                foreach (var to in Enumerable.Reverse(To(kind)).Where(members.Contains)) walk.Push(to);
            }

            loops.Add(ordered);
        }

        return loops.OrderBy(l => l[0], StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// One value of an enum by its name, any case: not a number, nor a list of names, which the enum would read as the
    /// sum of them
    /// </summary>
    private static T? Named<T>(string? name) where T : struct, Enum
    {
        var trimmed = name?.Trim();
        foreach (var value in Enum.GetValues<T>())
        {
            if (value.ToString().Equals(trimmed, StringComparison.OrdinalIgnoreCase)) return value;
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

    /// <summary>Part classes (<see cref="PartClasses"/>), the part's own or one above it ("crankshaft")</summary>
    [JsonProperty("classes")]
    public List<string> Classes { get; set; } = new();

    /// <summary>Words the part's name holds, lower case ("timing chain cover")</summary>
    [JsonProperty("names")]
    public List<string> Names { get; set; } = new();

    /// <summary>
    /// Kinds whose job it does for a part that needs them (<see cref="WorkRule.Needs"/>): a radiator with an electric
    /// fan of its own serves as the fan a belt needs. Only for needs: the rules of the kinds it serves are not its own.
    /// </summary>
    [JsonProperty("serves")]
    public List<string> Serves { get; set; } = new();

    /// <summary>
    /// How the part is seen to come off, leg by leg, in the engine's own directions (a <see cref="PartWay"/> each):
    /// ["back", "down"] for a gearbox. Parts mounted on it go along. Null leaves it to the kind as another mod says.
    /// </summary>
    [JsonProperty("comes_off")]
    public List<string?>? ComesOff { get; set; }

    /// <summary>
    /// The side of the engine the part goes on from, <c>"above"</c> or <c>"below"</c> (an <see cref="EngineSide"/>):
    /// the engine on the stand turns over for a crankshaft or an oil pan, and back for a cylinder head. Only looks,
    /// nothing is refused either way. Null leaves it to another mod, or the engine as it is.
    /// </summary>
    [JsonProperty("worked_from")]
    public string? WorkedFrom { get; set; }

    /// <summary>
    /// What a part of the kind does for the engine's cooling: <c>"radiator"</c>, <c>"water_pump"</c>, <c>"fan"</c>, or
    /// <c>"none"</c> for a kind whose name only sounds like one (a water pump's mounting hardware). A radiator kind that
    /// serves a fan kind (<see cref="Serves"/>) has a fan of its own. Null leaves it to another mod, or to the words of
    /// the part's name (<see cref="Cars.EngineCooling"/>); whether one is a performance part is the part's own
    /// <see cref="PartDefinition.Performance"/>.
    /// </summary>
    [JsonProperty("cooling")]
    public string? Cooling { get; set; }

    /// <summary>
    /// The tool a part of the kind is heard to come off and go on with: <c>"impact"</c> (the air gun: the drivetrain's big
    /// bolts, the wheels' lug nuts), else the socket wrench. Only sound. Null leaves it to another mod, or the wrench.
    /// </summary>
    [JsonProperty("tool")]
    public string? Tool { get; set; }
}

/// <summary>The side of the engine a part is worked on from, as the engine sits in a car</summary>
public enum EngineSide
{
    Above,
    Below
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

    /// <summary>
    /// Kinds that must be on the engine before this goes on, and stay on while it is: a belt needs the pulleys it runs
    /// on. Only where the engine takes one, so a block without a place for a fan needs none.
    /// </summary>
    [JsonProperty("needs")]
    public List<string> Needs { get; set; } = new();

    /// <summary>
    /// Kinds the part sits on, on a car (<see cref="Cars.CarWork"/>; an engine's work has no use for it): the rear axle on
    /// its leaf springs. On a car that has a part of the kind, or whose factory running gear has one, the part goes on once
    /// every place the car has for the kind holds one, and a part of the kind comes off once this part is off.
    /// </summary>
    [JsonProperty("hangs_on")]
    public List<string> HangsOn { get; set; } = new();

    /// <summary>
    /// Only with the car up on the lift, at either of its stops (<see cref="Cars.CarWork"/>; an engine's work has no use
    /// for it): a wheel comes off and goes on with the car's weight off it
    /// </summary>
    [JsonProperty("lifted")]
    public bool Lifted { get; set; }

    /// <summary>A mod taking back some of a rule it did not write: on purpose, or its rule only adds to it</summary>
    [JsonProperty("replace")]
    public bool Replace { get; set; }
}
