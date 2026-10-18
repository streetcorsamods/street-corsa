using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>A machine job of <see cref="PartRebuilds"/> as it stands for one part: the size it cuts to, what it costs and takes, and why not</summary>
/// <param name="Size">The size the part goes to; null for a job that cuts no metal (a radiator re-cored)</param>
/// <param name="Step">Which of the job's sizes that is, 1 for the first; 0 for no cut</param>
/// <param name="WhyNot">Why it cannot be done now, in the player's words; null when it can</param>
/// <param name="After">The part's condition after it, as the job aims</param>
/// <param name="Fitted">New parts fitted to suit the cut and in its price, by name ("pistons"); empty for none</param>
public sealed record MachineOffer(RebuildJob Job, CutSize? Size, int Step, string Name, decimal Cost, int Days, string? WhyNot, double After,
    IReadOnlyList<string> Fitted)
{
    public bool CanDo => WhyNot == null;
}

/// <summary>
/// The machine shop's work (<see cref="PartRebuilds.Machine"/> jobs): a part cut to the next of its job's sizes (a block
/// bored +.030 over, a crank ground .010 under, a head milled), each size's figures going on the dyno from then on
/// (<see cref="PartRebuilds.CutFigures"/>) and its worth on the part's price (<see cref="PartRebuilds.WorthOf"/>); and
/// the speed work, which leaves the part's wear as it is. A cut that wants new parts to suit fits them (a bore, oversize
/// pistons), in the price. Past the last size a part cannot be cut again. The money, the days and where the part is
/// meanwhile are the caller's.
/// </summary>
public static class Machining
{
    /// <summary>The job at the size: <c>rebuild.sized</c> of the job's name and the size's</summary>
    public static string NameOf(RebuildJob job, CutSize? size) =>
        size == null ? Rebuilding.NameOf(job) : PartWords.Say("rebuild.sized", ("job", Rebuilding.NameOf(job)), ("size", SizeName(job, size)));

    /// <summary>A size as the player reads it: <c>rebuild.&lt;job&gt;.size.&lt;id&gt;</c> in the text, else its own</summary>
    public static string SizeName(RebuildJob job, CutSize size)
    {
        var key = $"rebuild.{job.Id}.size.{size.Id}";
        var said = PartWords.Say(key);
        if (said != key) return said;
        return size.Say is { Length: > 0 } say ? say : size.Id;
    }

    /// <summary>
    /// What the cuts on a part say on its card: <c>rebuild.&lt;job&gt;.done</c> with the size ("bored +.030 over"), else
    /// the job's name and the size; empty for a part as it left the factory
    /// </summary>
    public static IReadOnlyList<string> Describe(PartsCatalog catalog, PartInstance part)
    {
        if (part.Cuts is not { Count: > 0 } cuts || catalog.Get(part.DefinitionId) is not { } definition) return [];

        var kind = catalog.WorkOrder.KindOf(definition);
        var lines = new List<string>();
        foreach (var (cut, done) in cuts.Where(c => c.Value > 0).OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            if (catalog.Rebuilds.CutJob(definition, kind, cut) is not { } job || catalog.Rebuilds.SizeOf(definition, kind, cut, done) is not { } size) continue;

            var key = $"rebuild.{job.Id}.done";
            var said = PartWords.Say(key, ("size", SizeName(job, size)));
            lines.Add(said != key ? said : NameOf(job, size));
        }

        return lines;
    }

    /// <summary>
    /// The size the job takes the part to (1 for the first) and the size: the next of its cut, or <paramref name="step"/> of
    /// a job made to any size; (0, null) for a job that cuts nothing
    /// </summary>
    public static (int Step, CutSize? Size) NextOf(RebuildJob job, PartInstance part, int? step = null)
    {
        if (job.Cut == null) return (0, null);

        var next = job.AnySize && step is { } picked ? picked : part.CutOf(job.Cut) + 1;
        return (next, next >= 1 && next <= job.Sizes.Count ? job.Sizes[next - 1] : null);
    }

    /// <summary>
    /// Why the machine job cannot be done on the part (at <paramref name="step"/>, a job made to any size); null when it
    /// can. A repair cut that changes nothing on the dyno (a crank ground, drums turned) is no use on a part with no wear
    /// or damage for it to take off; one that does (a bore, a head milled) is the speed work it was in the day, and speed
    /// work is wanted on any part.
    /// </summary>
    public static string? WhyNot(RebuildJob job, PartInstance part, int? step = null)
    {
        if (job.Bare && part.Children is { Count: > 0 }) return PartWords.Say("rebuild.why.bare");
        if (job.Cut != null)
        {
            if (job.AnySize && step == part.CutOf(job.Cut)) return PartWords.Say("rebuild.why.that_size");
            if (NextOf(job, part, step).Size is not { } size)
                return PartWords.Say(job.Sizes.Count == 1 ? "rebuild.why.done_already" : "rebuild.why.no_cuts_left");
            if (job.Speed || job.AnySize || size.ReadFigures != null) return null;
        }

        var wear = Unit.Clamp01(part.Wear, 1);
        var damaged = job.FixesDamage && Unit.Clamp01(part.Tear, 1) < PartInstance.UndamagedTear;
        return wear >= job.Restores - Rebuilding.Enough && !damaged ? PartWords.Say("rebuild.why.no_need") : null;
    }

    /// <summary>
    /// Every machine job for the part, as it stands, at a shop asking <paramref name="shopFactor"/> of the work's list
    /// price; none for a part the catalog does not know. Never under <paramref name="valueShare"/> of what the work adds
    /// to the part's worth (the owner, 2026-10-17: a little cheaper than the value it adds, so boring a worn block is
    /// not always the better buy than a good one)
    /// </summary>
    /// <param name="root">What the part is on, as it was brought (a whole engine): the worth the work adds is its</param>
    /// <param name="wanted">The jobs and sizes (by step) offered at all, asked before any is priced; every one when null</param>
    /// <param name="rootWorth">What <paramref name="root"/> is worth as it is, when the caller has it already</param>
    public static IReadOnlyList<MachineOffer> Offers(PartsCatalog catalog, PartInstance part, double shopFactor = 1.0, double priceMultiplier = 1.0,
        double valueShare = 0, PartInstance? root = null, Func<RebuildJob, int, bool>? wanted = null, double? rootWorth = null)
    {
        if (catalog.Get(part.DefinitionId) is not { } definition) return [];

        var offers = new List<MachineOffer>();
        foreach (var job in catalog.Rebuilds.For(definition, catalog.WorkOrder.KindOf(definition)).Where(j => j.IsMachine))
        {
            // A job made to any size is offered at each but the part's own
            var steps = job.AnySize ? Enumerable.Range(1, job.Sizes.Count).Where(s => s != part.CutOf(job.Cut!)).Select(s => (int?)s).ToList() : [null];
            foreach (var picked in steps)
            {
                var (step, size) = NextOf(job, part, picked);
                if (wanted != null && !wanted(job, step)) continue;

                var shown = size ?? (job.Cut != null && job.Sizes.Count > 0 ? job.Sizes[^1] : null);
                var whyNot = WhyNot(job, part, picked);
                var fitted = whyNot == null ? FittedNames(catalog, job, part) : [];
                if (whyNot == null && valueShare > 0) rootWorth ??= PartPricing.WorthOfAssembly(catalog, root ?? part);
                var cost = whyNot == null ? CostFor(catalog, job, definition, part, shopFactor, priceMultiplier, valueShare, picked, root, rootWorth) : 0;
                var after = job.Restores > 0 ? Math.Max(Unit.Clamp01(part.Wear, 1), job.Restores) : Unit.Clamp01(part.Wear, 1);
                offers.Add(new MachineOffer(job, shown, step, NameOf(job, shown), cost, job.Days, whyNot, after, fitted));
            }
        }

        return offers.OrderBy(o => o.Job.Speed ? 1 : 0).ToList();
    }

    /// <summary>
    /// The work's price: its share of the part's new price at the shop's prices (<see cref="Rebuilding.CostOf"/>), and the
    /// new parts it fits at their share; never under <paramref name="valueShare"/> of what it adds to the worth of the
    /// part and what comes with it (at the best the work comes out), at the shop's prices too
    /// </summary>
    /// <param name="step">The size of a job made to any size; the next size else</param>
    /// <param name="root">
    /// What the part is on (a whole engine): the worth added is the whole's, so heads ported on an engine are charged for
    /// the power the engine gains; the part itself when null
    /// </param>
    /// <param name="rootWorth">What the whole is worth before the work, when the caller has it already</param>
    public static decimal CostFor(PartsCatalog catalog, RebuildJob job, PartDefinition definition, PartInstance part, double shopFactor = 1.0,
        double priceMultiplier = 1.0, double valueShare = 0, int? step = null, PartInstance? root = null, double? rootWorth = null)
    {
        var work = (double)Rebuilding.CostOf(job, definition, priceMultiplier)
                   + Fitted(catalog, job, part).Sum(d => PartPricing.NewPrice(d) * job.FitsNewCost) * Multiplier.Sane(priceMultiplier);
        if (valueShare > 0 && double.IsFinite(valueShare))
        {
            var whole = root ?? part;
            var after = PartTrees.Clone(whole, keepIds: true);
            var worked = after.SelfAndDescendants().FirstOrDefault(p => p.InstanceId == part.InstanceId) ?? after;
            var extras = Apply(catalog, job, worked, Rebuilding.Best(job), step);
            var gained = PartPricing.WorthOfAssembly(catalog, after) + extras.Sum(e => PartPricing.WorthOfAssembly(catalog, e))
                         - (rootWorth ?? PartPricing.WorthOfAssembly(catalog, whole));
            work = Math.Max(work, gained * valueShare);
        }

        return PartPricing.Round(work * Multiplier.Sane(shopFactor));
    }

    /// <summary>
    /// Does the job on the part, exactly as it aims: the cut to its next size, the wear up to what it restores, the parts
    /// of <see cref="RebuildJob.FitsNew"/>'s kind on it swapped for new ones of the size. The new set that comes with a
    /// part that had none on it is given back, loose; it goes where the part goes.
    /// </summary>
    /// <param name="step">The size of a job made to any size (<see cref="RebuildJob.AnySize"/>); the next size else</param>
    public static List<PartInstance> Apply(PartsCatalog catalog, RebuildJob job, PartInstance part, int? step = null) =>
        Apply(catalog, job, part, job.Restores, step);

    /// <summary>
    /// Does the job as it comes out: a machinist's work lands round what it aims at (<see cref="Rebuilding.Landing"/>),
    /// a hair under now and then, never worse than the part was
    /// </summary>
    public static (RebuildOutcome Outcome, List<PartInstance> Extras) Do(PartsCatalog catalog, RebuildJob job, PartInstance part, Random random, int? step = null)
    {
        var (lands, _) = Rebuilding.Landing(job, random.NextDouble());
        var extras = Apply(catalog, job, part, lands, step);
        return (new RebuildOutcome(Unit.Clamp01(part.Wear, 1), null), extras);
    }

    /// <summary>The job done with the part's wear taken up to <paramref name="restores"/>: how it came out, or the best it can</summary>
    internal static List<PartInstance> Apply(PartsCatalog catalog, RebuildJob job, PartInstance part, double restores, int? picked)
    {
        var (step, size) = NextOf(job, part, picked);
        if (job.Cut != null && size == null) return [];

        if (job.Restores > 0) part.Wear = Math.Max(Unit.Clamp01(part.Wear, 1), restores);
        if (job.FixesDamage) part.Tear = 1;
        if (job.Cut == null) return [];

        part.Cuts ??= new();
        part.Cuts[job.Cut] = step;
        if (job.FitsNew == null) return [];

        // The parts of the kind on it: new ones of the cut's size in their place, with what was on them
        var swapped = false;
        foreach (var holder in part.SelfAndDescendants().ToList())
        {
            for (var i = 0; i < holder.Children.Count; i++)
            {
                var old = holder.Children[i];
                if (old == null || !IsKind(catalog, old, job.FitsNew)) continue;

                holder.Children[i] = FreshFor(old.DefinitionId, job.Cut, part.Cuts[job.Cut], old);
                swapped = true;
            }
        }

        if (swapped) return [];
        return StockOf(catalog, part.DefinitionId, job.FitsNew) is { } stock ? [FreshFor(stock.Id, job.Cut, part.Cuts[job.Cut], null)] : [];
    }

    /// <summary>A new part of the size, mounted where the old one was and with what was on it</summary>
    private static PartInstance FreshFor(string definitionId, string cut, int step, PartInstance? old)
    {
        var fresh = new PartInstance(definitionId);
        fresh.Cuts[cut] = step;
        if (old == null) return fresh;

        fresh.ParentSlot = old.ParentSlot;
        fresh.OwnSlot = old.OwnSlot;
        fresh.Tuning = new Dictionary<string, double>(old.Tuning ?? []);
        fresh.Children = old.Children ?? [];
        return fresh;
    }

    /// <summary>The new parts the job fits for its price: the kind's parts on it, else the set the block came with</summary>
    private static IReadOnlyList<PartDefinition> Fitted(PartsCatalog catalog, RebuildJob job, PartInstance part)
    {
        if (job.FitsNew == null || job.Cut == null) return [];

        var on = part.SelfAndDescendants().Skip(1).Where(p => IsKind(catalog, p, job.FitsNew)).Select(p => catalog.Get(p.DefinitionId)!).ToList();
        if (on.Count > 0) return on;
        return StockOf(catalog, part.DefinitionId, job.FitsNew) is { } stock ? [stock] : [];
    }

    private static IReadOnlyList<string> FittedNames(PartsCatalog catalog, RebuildJob job, PartInstance part) =>
        Fitted(catalog, job, part).Select(d => d.DisplayName ?? d.Name).Distinct().ToList();

    /// <summary>
    /// The part of the kind an engine built on the block came with (the first build that has the block): the pistons a
    /// bare block is bored for; null when no build of it has one
    /// </summary>
    public static PartDefinition? StockOf(PartsCatalog catalog, string blockId, string kind)
    {
        foreach (var build in catalog.EngineBuilds)
        {
            if (!build.Parts.Any(p => string.Equals(p, blockId, StringComparison.OrdinalIgnoreCase))) continue;

            foreach (var id in build.Parts)
            {
                if (catalog.Get(id) is { } definition && string.Equals(catalog.WorkOrder.KindOf(definition), kind, StringComparison.OrdinalIgnoreCase))
                    return definition;
            }
        }

        return null;
    }

    private static bool IsKind(PartsCatalog catalog, PartInstance part, string kind) =>
        catalog.Get(part.DefinitionId) is { } definition && string.Equals(catalog.WorkOrder.KindOf(definition), kind, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Why parts cannot go together for their cuts; null when they can: a block bored to a size takes pistons of that size
    /// only, and standard pistons only a standard bore (<see cref="PartRebuilds.MatchedCuts"/>). Asked of every pair in
    /// <paramref name="whole"/> with one of them among <paramref name="moving"/>.
    /// </summary>
    public static string? WhyNotTogether(PartsCatalog catalog, IReadOnlyList<PartInstance> whole, IReadOnlyCollection<PartInstance> moving)
    {
        foreach (var (cut, kinds, fitted) in catalog.Rebuilds.MatchedCuts())
        {
            var cutOn = whole.Where(p => catalog.Get(p.DefinitionId) is { } d && catalog.WorkOrder.KindOf(d) is { } k
                                         && kinds.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
            var fits = whole.Where(p => IsKind(catalog, p, fitted)).ToList();
            foreach (var cutPart in cutOn)
            {
                foreach (var fit in fits.Where(f => moving.Contains(f) || moving.Contains(cutPart)))
                {
                    if (fit.CutOf(cut) == cutPart.CutOf(cut)) continue;

                    var definition = catalog.Get(cutPart.DefinitionId)!;
                    var job = catalog.Rebuilds.CutJob(definition, catalog.WorkOrder.KindOf(definition), cut);
                    string SizeOf(PartInstance p) => p.CutOf(cut) is > 0 and var n && job != null ? SizeName(job, job.Sizes[Math.Min(n, job.Sizes.Count) - 1])
                        : PartWords.Say("rebuild.standard");
                    return PartWords.Say("work.cut_mismatch", ("part", PartName(catalog, cutPart)), ("size", SizeOf(cutPart)),
                        ("other", PartName(catalog, fit)), ("other_size", SizeOf(fit)));
                }
            }
        }

        return null;
    }

    private static string PartName(PartsCatalog catalog, PartInstance part) =>
        catalog.Get(part.DefinitionId) is { } definition ? definition.DisplayName ?? definition.Name : part.DefinitionId;
}
