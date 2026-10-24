using System.IO;
using Newtonsoft.Json;

namespace StreetCorsa.Parts;

/// <summary>
/// The work a part can have done on it to take its mileage off, the 1960s way: rebuilt with a kit (a carburettor, a
/// water pump), a tune-up, a valve job or a ring job, or swapped at the parts counter for a rebuilt one, the old part
/// going back as the core; or left at a machine shop (<see cref="Machine"/>): a block bored oversize, a crank ground
/// undersize, a head milled, and the speed work (a hotter cam grind, heads ported). Read from <see cref="FileName"/> in
/// every mod's parts folder; a job is for the parts of its work order kinds (<see cref="WorkOrder.KindOf"/>) or of its
/// shelves (<see cref="PartKinds.GroupOf"/>).
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

    /// <summary>Left at a machine shop, back in a few days: cut to its next size, or speed work</summary>
    public const string Machine = "machine";

    /// <summary>How far the player's own work may land below what the job can do, unless the job says (<see cref="RebuildJob.Roll"/>)</summary>
    public const double KitRoll = 0.08;

    /// <summary>A machinist's: a professional's work lands about where it should</summary>
    public const double MachineRoll = 0.02;

    private readonly List<RebuildJob> _jobs = new();

    public static PartRebuilds Empty => new();

    /// <summary>Every job, in the order the mods list them, the core's first</summary>
    public IReadOnlyList<RebuildJob> All => _jobs;

    /// <summary>The jobs for a part, kits before exchange units before the machine shop's, in the order listed</summary>
    public IReadOnlyList<RebuildJob> For(PartDefinition part, string? workKind)
    {
        var group = PartKinds.GroupOf(part);
        return _jobs
            .Where(j => IsFor(j, group, workKind) && (!j.Becomes || part.Becomes?.ContainsKey(j.Id) == true))
            .OrderBy(j => j.IsMachine ? 2 : j.IsExchange ? 1 : 0)
            .ToList();
    }

    private static bool IsFor(RebuildJob job, string group, string? workKind) =>
        (workKind != null && job.Kinds.Contains(workKind, StringComparer.OrdinalIgnoreCase)) || job.Groups.Contains(group, StringComparer.OrdinalIgnoreCase);

    /// <summary>The machine job that makes the cut on the part (its sizes, what each does); null when none does</summary>
    public RebuildJob? CutJob(PartDefinition part, string? workKind, string cut)
    {
        var group = PartKinds.GroupOf(part);
        return _jobs.LastOrDefault(j => j.Cut != null && j.Cut.Equals(cut, StringComparison.OrdinalIgnoreCase) && IsFor(j, group, workKind));
    }

    /// <summary>The size a part's cut has reached (1 for the first), as its job gives it; null for a cut no job of the part makes</summary>
    public CutSize? SizeOf(PartDefinition part, string? workKind, string cut, int done) =>
        done > 0 && CutJob(part, workKind, cut) is { Sizes.Count: > 0 } job ? job.Sizes[Math.Min(done, job.Sizes.Count) - 1] : null;

    /// <summary>
    /// What the cuts on a part do to the engine's figures, every size's figures together (a bore over adds to the bore, a
    /// head milled takes from the chamber); null when they do nothing a job says
    /// </summary>
    public Logic.PartFigureSet? CutFigures(PartDefinition part, string? workKind, IReadOnlyDictionary<string, int> cuts)
    {
        var numbers = new List<Logic.PartFigure>();
        var chargers = new List<Logic.DynoCharger>();
        foreach (var (cut, done) in cuts.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            if (SizeOf(part, workKind, cut, done)?.ReadFigures is not { } figures) continue;
            numbers.AddRange(figures.Numbers);
            chargers.AddRange(figures.Chargers);
        }

        return numbers.Count == 0 && chargers.Count == 0 ? null : new Logic.PartFigureSet { Numbers = numbers, Chargers = chargers };
    }

    /// <summary>What the cuts on a part leave it worth against the factory's: each size's <see cref="CutSize.Worth"/>, multiplied</summary>
    public double WorthOf(PartDefinition part, string? workKind, IReadOnlyDictionary<string, int> cuts)
    {
        var worth = 1.0;
        foreach (var (cut, done) in cuts)
        {
            if (SizeOf(part, workKind, cut, done) is { } size) worth *= size.Worth;
        }

        return worth;
    }

    /// <summary>
    /// The cuts parts must agree on to go together: a block bored +.030 takes only pistons of +.030. The machine jobs that
    /// fit new parts to suit (<see cref="RebuildJob.FitsNew"/>), each with the kinds the cut is made on and the kind fitted.
    /// </summary>
    public IEnumerable<(string Cut, IReadOnlyList<string> Kinds, string Fitted)> MatchedCuts() =>
        _jobs.Where(j => j.Cut != null && j.FitsNew != null).Select(j => (j.Cut!, (IReadOnlyList<string>)j.Kinds, j.FitsNew!));

    /// <summary>
    /// What the parts say jobs turn them into (<see cref="PartDefinition.Becomes"/>), told in <paramref name="problems"/>
    /// and left out where it cannot be: a job of no such id or that turns nothing, a part that is not there or not on the
    /// same shelf. A job that turns parts that no part names is told too: it is never offered. The job ids are looked up
    /// as the jobs are, whatever their case.
    /// </summary>
    public void CheckBecomes(IEnumerable<PartDefinition> parts, Func<string, PartDefinition?> get, List<string> problems)
    {
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts.Where(p => p.Becomes is { Count: > 0 }))
        {
            if (!ReferenceEquals(part.Becomes!.Comparer, StringComparer.OrdinalIgnoreCase))
                part.Becomes = new Dictionary<string, string>(part.Becomes.DistinctBy(b => b.Key, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            foreach (var (jobId, target) in part.Becomes!.ToList())
            {
                // A target left null or blank (Newtonsoft reads "weld_spiders": null as one) names nothing to become
                var why = _jobs.FirstOrDefault(j => j.Id.Equals(jobId, StringComparison.OrdinalIgnoreCase)) is not { Becomes: true } ? $"no job {jobId} turns parts into others"
                    : string.IsNullOrWhiteSpace(target) ? $"{jobId} would make it nothing: \"becomes\" names no part"
                    : get(target) is not { } into ? $"{jobId} would make it {target}, which is no part"
                    : PartKinds.GroupOf(into) != PartKinds.GroupOf(part) ? $"{jobId} would make it {target}, which is on another shelf"
                    : null;
                if (why == null)
                {
                    named.Add(jobId);
                    continue;
                }

                problems.Add($"{part.Id}: {why}; left out");
                part.Becomes!.Remove(jobId);
            }
        }

        foreach (var job in _jobs.Where(j => j.Becomes && !named.Contains(j.Id)))
            problems.Add($"{job.Source}: {job.Id} turns a part into another, but no part names what it becomes; it is no part's job");
    }

    /// <summary>
    /// The kinds and shelves the jobs name that are none of <paramref name="kinds"/> (the work order's) and
    /// <paramref name="groups"/>: a job for them is never offered. One problem line each, for the mod's author; the jobs
    /// stay as they are.
    /// </summary>
    public IReadOnlyList<string> UnknownNames(IEnumerable<string> kinds, IEnumerable<string> groups)
    {
        var knownKinds = kinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownGroups = groups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        foreach (var job in _jobs)
        {
            foreach (var kind in job.Kinds.Where(k => !knownKinds.Contains(k)))
                problems.Add($"{job.Source}: {job.Id} is for the kind \"{kind}\", which is no kind of work_order.json");
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
        if (job.Way is not (Kit or Exchange or Machine))
        {
            problems.Add($"{label}: is done the \"{job.Way}\" way, which is none of {Kit}, {Exchange} and {Machine}; done as a {Kit}");
            job.Way = Kit;
        }

        if (job.Becomes && job.IsMachine)
        {
            problems.Add($"{label}: turns a part into another, which the machine shop does not; it does not");
            job.Becomes = false;
        }

        job.Cut = string.IsNullOrWhiteSpace(job.Cut) ? null : job.Cut.Trim().ToLowerInvariant();
        job.FitsNew = string.IsNullOrWhiteSpace(job.FitsNew) ? null : job.FitsNew.Trim();
        job.Sizes = PartDefinition.NonNull(job.Sizes);
        if (!job.IsMachine && (job.Cut != null || job.Sizes.Count > 0 || job.FitsNew != null || job.Speed || job.AnySize))
        {
            problems.Add($"{label}: a cut, sizes, fits_new, any_size and speed are a machine shop's; left as they are not");
            job.Cut = null;
            job.Sizes = new();
            job.FitsNew = null;
            job.Speed = false;
            job.AnySize = false;
        }

        if (job.AnySize && (job.Cut == null || job.FitsNew != null))
        {
            problems.Add($"{label}: any_size wants a cut and no fits_new; cut to the next size instead");
            job.AnySize = job.Cut != null && job.FitsNew == null && job.AnySize;
        }

        if (job.Cut == null && job.Sizes.Count > 0)
        {
            problems.Add($"{label}: has sizes but no cut to give them; they are left out");
            job.Sizes = new();
        }

        if (job.Cut != null)
        {
            if (job.Sizes.RemoveAll(z => string.IsNullOrWhiteSpace(z.Id)) > 0) problems.Add($"{label}: a size without an id is left out");
            if (job.Sizes.Count == 0)
            {
                problems.Add($"{label}: cuts \"{job.Cut}\" in no sizes; left out");
                return null;
            }

            foreach (var size in job.Sizes)
            {
                size.ReadFigures = Logic.PartFigures.Read($"{label} size {size.Id}", size.Figures, problems);
                size.Worth = Within(size.Worth, 0.05, 5, $"size {size.Id}'s worth", label, problems, 1);
            }
        }

        if (job.FitsNew != null && job.Cut == null)
        {
            problems.Add($"{label}: fits new {job.FitsNew} to suit no cut; they are not fitted");
            job.FitsNew = null;
        }

        // A cut for speed alone leaves the part's wear as it is: restores 0; so may a job that turns the part into another
        var keepsWear = (job.Cut != null || job.Becomes) && job.Restores == 0;
        if (!double.IsFinite(job.Restores) || (!keepsWear && job.Restores is <= 0 or > 1))
        {
            problems.Add($"{label}: restores {job.Restores}, not above 0 and up to 1; left out");
            return null;
        }

        if (job.Roll is { } roll && (!double.IsFinite(roll) || roll is < 0 or > 0.5))
        {
            problems.Add($"{label}: roll {roll} is not 0 to 0.5; its way's own it is");
            job.Roll = null;
        }

        job.FitsNewCost = Within(job.FitsNewCost, 0, 5, "fits_new_cost", label, problems, 1);
        if (job.IsMachine && job.Days is < 1 or > 60)
        {
            problems.Add($"{label}: takes {job.Days} days, not 1 to 60; takes 3");
            job.Days = 3;
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

    private static double Within(double value, double low, double high, string name, string label, List<string> problems, double? otherwise = null)
    {
        if (double.IsFinite(value) && value >= low && value <= high) return value;

        var instead = otherwise ?? low;
        problems.Add($"{label}: {name} {value} is not {low} to {high}; {instead} it is");
        return instead;
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

/// <summary>
/// One size a machine shop cuts a part to, in turn: +.030, then +.040, then +.060 over. What it does to the engine's
/// figures (<see cref="Logic.PartFigures"/>: <c>"bore": { "add": 0.76 }</c>), and what it leaves the part worth against
/// the factory's.
/// </summary>
public sealed class CutSize
{
    /// <summary>The player reads <c>rebuild.&lt;job id&gt;.size.&lt;id&gt;</c> in the text, else <see cref="Say"/></summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("say")]
    public string? Say { get; set; }

    [JsonProperty("figures")]
    public Dictionary<string, Newtonsoft.Json.Linq.JToken>? Figures { get; set; }

    /// <summary><see cref="Figures"/> as read, checked; null for none</summary>
    [JsonIgnore]
    public Logic.PartFigureSet? ReadFigures { get; set; }

    /// <summary>The part's worth at this size against the factory's: under 1 for every cut a block has left fewer of, over for speed work</summary>
    [JsonProperty("worth")]
    public double Worth { get; set; } = 1;
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

    public bool IsMachine => Way == PartRebuilds.Machine;

    /// <summary>
    /// A machine job's cut ("bore", "grind", "mill", "port"): each time it is done the part goes to the next of
    /// <see cref="Sizes"/>, and past the last it cannot be. None: the job takes no metal off (a radiator re-cored) and can be
    /// done again.
    /// </summary>
    [JsonProperty("cut")]
    public string? Cut { get; set; }

    /// <summary>The sizes of the cut, the first first</summary>
    [JsonProperty("sizes")]
    public List<CutSize> Sizes { get; set; } = new();

    /// <summary>A machine job's days at the shop: dropped off today, back that many mornings on</summary>
    [JsonProperty("days")]
    public int Days { get; set; } = 3;

    /// <summary>
    /// Made to any of the sizes, not cut to the next: new pistons for a block bored before. The shop offers each size
    /// but the one the part is.
    /// </summary>
    [JsonProperty("any_size")]
    public bool AnySize { get; set; }

    /// <summary>Speed work, not a repair: the machine shop lists it apart</summary>
    [JsonProperty("speed")]
    public bool Speed { get; set; }

    /// <summary>
    /// A work order kind the machinist fits new to suit the cut ("pistons" for a bore): the part's own of that kind are
    /// swapped for new ones of the cut's size, or a set comes with it when it has none, and only those go with it after
    /// </summary>
    [JsonProperty("fits_new")]
    public string? FitsNew { get; set; }

    /// <summary>What the new parts fitted cost in the job's price, a share of their new price</summary>
    [JsonProperty("fits_new_cost")]
    public double FitsNewCost { get; set; } = 1;

    /// <summary>
    /// How far below <see cref="Restores"/> the work may land (0..0.5): the player's own is a little rough now and then, a
    /// machinist's lands about where it should; never worse than the part was. Left out: the way's own
    /// (<see cref="PartRebuilds.KitRoll"/>, none for an exchange unit, <see cref="PartRebuilds.MachineRoll"/>).
    /// </summary>
    [JsonProperty("roll")]
    public double? Roll { get; set; }

    /// <summary>The roll the job is done with</summary>
    public double RollOf => Roll ?? (IsExchange ? 0 : IsMachine ? PartRebuilds.MachineRoll : PartRebuilds.KitRoll);

    /// <summary>The condition the part is in after it (0..1]: a kit is not a new part. A cut for speed alone: 0, its wear left as it is.</summary>
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

    /// <summary>
    /// The job turns the part into another: the one the part's pack names for this job (<see cref="PartDefinition.Becomes"/>),
    /// with what is on it, its wear and its damage. Offered only on a part that names one; never needed less for a part in
    /// good shape (restores may be 0: the part is no better for it, it is something else). Not a machine shop's.
    /// </summary>
    [JsonProperty("becomes")]
    public bool Becomes { get; set; }
}
