using System.IO;
using Newtonsoft.Json;

namespace StreetCorsa.Parts;

/// <summary>
/// The work a part can have done on it to take its mileage off, the 1960s way: rebuilt with a kit (a carburettor, a
/// water pump), a tune-up, a valve job or a ring job, or swapped at the parts counter for a rebuilt one, the old part
/// going back as the core. Read from <see cref="FileName"/> in every mod's parts folder; a job is for the parts of its
/// work order kinds (<see cref="WorkOrder.KindOf"/>) or of its shelves (<see cref="PartKinds.GroupOf"/>).
/// </summary>
/// <remarks>
/// A later mod's job of the same id takes the place of the one before, whole; one with <c>"disabled": true</c> takes
/// it out.
/// </remarks>
public sealed class PartRebuilds
{
    public const string FileName = "rebuilds.json";

    /// <summary>Swapped at the parts counter for a rebuilt one</summary>
    public const string Exchange = "exchange";

    /// <summary>The player's own work, with a kit</summary>
    public const string Kit = "kit";

    private readonly List<RebuildJob> _jobs = new();

    public static PartRebuilds Empty => new();

    /// <summary>Every job, in the order the mods list them, the base game's first</summary>
    public IReadOnlyList<RebuildJob> All => _jobs;

    /// <summary>The jobs for a part, kits before exchange units, in the order listed</summary>
    public IReadOnlyList<RebuildJob> For(PartDefinition part, string? workKind)
    {
        var group = PartKinds.GroupOf(part);
        return _jobs
            .Where(j => (workKind != null && j.Kinds.Contains(workKind, StringComparer.OrdinalIgnoreCase))
                        || j.Groups.Contains(group, StringComparer.OrdinalIgnoreCase))
            .OrderBy(j => j.IsExchange ? 1 : 0)
            .ToList();
    }

    /// <summary>
    /// The kinds and shelves the jobs name that no part of <paramref name="kinds"/> and <paramref name="groups"/> is: a
    /// job for them is never offered. One problem line each, for the mod's author; the jobs stay as they are.
    /// </summary>
    public IReadOnlyList<string> UnknownNames(IEnumerable<string> kinds, IEnumerable<string> groups)
    {
        var knownKinds = kinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownGroups = groups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        foreach (var job in _jobs)
        {
            foreach (var kind in job.Kinds.Where(k => !knownKinds.Contains(k)))
                problems.Add($"{job.Source}: {job.Id} is for the kind \"{kind}\", which no part is (work_order.json's kinds)");
            foreach (var group in job.Groups.Where(g => !knownGroups.Contains(g)))
                problems.Add($"{job.Source}: {job.Id} is for the shelf \"{group}\", which no part is on (part_groups.json)");
        }

        return problems;
    }

    /// <summary>The sounds the jobs' steps name that are no sound cue (<paramref name="isCue"/>): those steps are silent</summary>
    public IReadOnlyList<string> UnknownSounds(Func<string, bool> isCue)
    {
        var problems = new List<string>();
        foreach (var job in _jobs)
        {
            foreach (var step in job.Steps)
            {
                foreach (var sound in new[] { step.Sound, step.Start }.Where(s => !string.IsNullOrWhiteSpace(s) && !isCue(s!)))
                    problems.Add($"{job.Source}: {job.Id}'s step {step.Id} plays \"{sound}\", which is no sound cue (sfx.json); it is silent");
            }
        }

        return problems;
    }

    public static PartRebuilds Load(IEnumerable<(string ModId, bool IsBase, string Dir)> roots, List<string> problems)
    {
        var book = new PartRebuilds();
        foreach (var (modId, isBase, dir) in roots)
        {
            var file = Path.Combine(dir, FileName);
            if (!File.Exists(file)) continue;

            var label = (isBase ? string.Empty : modId + "/") + FileName;
            RebuildsFile? read;
            try
            {
                read = JsonConvert.DeserializeObject<RebuildsFile>(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{label}: {ex.Message}; its jobs are left out");
                continue;
            }

            foreach (var job in read?.Jobs ?? new())
            {
                if (job == null || string.IsNullOrWhiteSpace(job.Id))
                {
                    problems.Add($"{label}: a job without an id is left out");
                    continue;
                }

                var index = book._jobs.FindIndex(j => j.Id.Equals(job.Id, StringComparison.OrdinalIgnoreCase));
                if (job.Disabled)
                {
                    if (index >= 0) book._jobs.RemoveAt(index);
                    continue;
                }

                if (Check(job, $"{label}: {job.Id}", problems) is not { } good) continue;
                good.Source = label;

                if (index >= 0) book._jobs[index] = good;
                else book._jobs.Add(good);
            }
        }

        return book;
    }

    /// <summary>The job with its lists and figures made safe to play by; null for one that is no job</summary>
    private static RebuildJob? Check(RebuildJob job, string label, List<string> problems)
    {
        job.Kinds = PartDefinition.NonNull(job.Kinds);
        job.Groups = PartDefinition.NonNull(job.Groups);
        job.Steps = PartDefinition.NonNull(job.Steps);
        if (job.Steps.RemoveAll(s => string.IsNullOrWhiteSpace(s.Id)) > 0)
            problems.Add($"{label}: a step without an id is left out");
        if (job.Kinds.Count == 0 && job.Groups.Count == 0)
        {
            problems.Add($"{label}: names no kinds and no groups, so it is no part's job; left out");
            return null;
        }

        job.Way = string.IsNullOrWhiteSpace(job.Way) ? Kit : job.Way.Trim().ToLowerInvariant();
        if (job.Way is not (Kit or Exchange))
        {
            problems.Add($"{label}: is done the \"{job.Way}\" way, which is neither {Kit} nor {Exchange}; done as a {Kit}");
            job.Way = Kit;
        }

        if (!double.IsFinite(job.Restores) || job.Restores is <= 0 or > 1)
        {
            problems.Add($"{label}: restores {job.Restores}, not above 0 and up to 1; left out");
            return null;
        }

        job.MinWear = Within(job.MinWear, 0, job.Restores, nameof(job.MinWear), label, problems);
        job.Cost = Within(job.Cost, 0, 10, nameof(job.Cost), label, problems);
        job.CostAtLeast = Within(job.CostAtLeast, 0, 1_000_000, nameof(job.CostAtLeast), label, problems);
        if (job.Minutes is < 0 or > 7 * 24 * 60)
        {
            problems.Add($"{label}: takes {job.Minutes} minutes, not 0 to a week; takes an hour");
            job.Minutes = 60;
        }

        return job;
    }

    private static double Within(double value, double low, double high, string name, string label, List<string> problems)
    {
        if (double.IsFinite(value) && value >= low && value <= high) return value;

        problems.Add($"{label}: {name} {value} is not {low} to {high}; {low} it is");
        return low;
    }

    private sealed class RebuildsFile
    {
        [JsonProperty("jobs")]
        public List<RebuildJob?>? Jobs { get; set; }
    }
}

/// <summary>A step of a job as the player sees it done: its caption and the sound of the work</summary>
public sealed class RebuildStep
{
    /// <summary>The player reads <c>rebuild.&lt;job id&gt;.&lt;id&gt;</c> in the text, else <see cref="Say"/></summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("say")]
    public string? Say { get; set; }

    /// <summary>The sfx cue that loops under it (the mods' sfx.json); none: the bench's own</summary>
    [JsonProperty("sound")]
    public string? Sound { get; set; }

    /// <summary>An sfx cue played once as it starts: the bell over the parts counter's door</summary>
    [JsonProperty("start")]
    public string? Start { get; set; }
}

/// <summary>One job of <see cref="PartRebuilds"/>: what it is for, what it costs and takes, what it brings a part up to</summary>
public sealed class RebuildJob
{
    /// <summary>The file the job was read from, as the mod problems name it</summary>
    [JsonIgnore]
    public string Source { get; set; } = PartRebuilds.FileName;

    /// <summary>The player reads <c>rebuild.&lt;id&gt;</c> in the text, else <see cref="Name"/></summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string? Name { get; set; }

    /// <summary>The work order's kinds it is for ("carburettor")</summary>
    [JsonProperty("kinds")]
    public List<string> Kinds { get; set; } = new();

    /// <summary>The shelves it is for ("brakes"), the parts no work order kind takes in among them</summary>
    [JsonProperty("groups")]
    public List<string> Groups { get; set; } = new();

    /// <summary><see cref="PartRebuilds.Kit"/> (the player's own work) or <see cref="PartRebuilds.Exchange"/> (a rebuilt one from the counter)</summary>
    [JsonProperty("way")]
    public string Way { get; set; } = PartRebuilds.Kit;

    public bool IsExchange => Way == PartRebuilds.Exchange;

    /// <summary>The condition the part is in after it (0..1]: a kit is not a new part</summary>
    [JsonProperty("restores")]
    public double Restores { get; set; } = 0.9;

    /// <summary>A part worn below this is too far gone for the job: it wants a machine shop</summary>
    [JsonProperty("min_wear")]
    public double MinWear { get; set; }

    /// <summary>What the kit or the rebuilt unit costs, a share of the part's new price</summary>
    [JsonProperty("cost")]
    public double Cost { get; set; } = 0.1;

    /// <summary>The least it costs, dollars: a gasket set costs something whatever the part</summary>
    [JsonProperty("cost_at_least")]
    public double CostAtLeast { get; set; }

    /// <summary>The time it takes: the work, or the trip to the parts counter</summary>
    [JsonProperty("minutes")]
    public int Minutes { get; set; } = 60;

    /// <summary>Done with the part off: loose on the shelf, or on the engine stand for a block</summary>
    [JsonProperty("off")]
    public bool Off { get; set; }

    /// <summary>Done with nothing mounted on the part</summary>
    [JsonProperty("bare")]
    public bool Bare { get; set; }

    /// <summary>Takes the part's damage off too (<c>Tear</c>): a rebuilt unit from the counter is a sound one</summary>
    [JsonProperty("fixes_damage")]
    public bool FixesDamage { get; set; }

    /// <summary>
    /// What the player sees done while the job runs, in turn: a caption each (<c>rebuild.&lt;job&gt;.&lt;step&gt;</c> in the
    /// text, else its <see cref="RebuildStep.Say"/>) over the sound of the work. None: the job's name over the bench's sound.
    /// </summary>
    [JsonProperty("steps")]
    public List<RebuildStep> Steps { get; set; } = new();

    /// <summary>In a mod: takes the job of this id out</summary>
    [JsonProperty("disabled")]
    public bool Disabled { get; set; }
}
