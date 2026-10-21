using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// One thing done to an engine, and what it came to. <paramref name="Removed"/> are the parts that came off (loose,
/// each without what was on it; a swapped-out engine whole), for whoever did the work to sell on or trade in;
/// <paramref name="TradeIn"/> is what a shop would pay for them. <paramref name="UsedAdId"/> is the ad the part was
/// bought from, when it was bought used out of the paper.
/// </summary>
public sealed record TuneUpgrade(string Change, string Group, double PowerBefore, double PowerAfter, decimal Cost, decimal TradeIn,
    IReadOnlyList<PartInstance> Removed, Guid? UsedAdId = null)
{
    /// <summary>
    /// The parts that went on the engine with it and were not there before, as they sit on the tuned engine: what
    /// was bought for it. None for a block swap, whose engine was bought used and whole.
    /// </summary>
    public IReadOnlyList<PartInstance> Fitted { get; init; } = [];
}

/// <summary>A part or a whole engine for sale in the paper, as a tuner weighs it against a new one</summary>
public sealed record UsedPartOffer(Guid AdId, PartInstance Part, decimal Price);

/// <summary>An engine after <see cref="EngineTuner.TuneUp"/>: the parts, their dyno report, and what was done</summary>
public sealed record TuneResult(PartInstance Engine, EngineReport Report, IReadOnlyList<TuneUpgrade> Upgrades)
{
    /// <summary>What the new parts cost</summary>
    public decimal Cost => Upgrades.Sum(u => u.Cost);

    /// <summary>What a shop would pay for the parts that came off</summary>
    public decimal TradeIn => Upgrades.Sum(u => u.TradeIn);
}

/// <summary>
/// Tuning an engine within a budget, the way a racer with money in his pocket would: the upgrade that buys the most
/// power per dollar, weighted by how much a racer cares about that part (<see cref="Priority"/>), then the next, one
/// per kind of part. Every upgrade is checked on the dyno and has to gain at least <see cref="MinGain"/>. A part the
/// new one leaves without a place (a carburettor the new manifold does not take) is replaced by the cheapest that
/// fits, to a depth of <see cref="MaxReplacementDepth"/>. The block swap is a bigger engine of the same family,
/// bought used out of the paper. New parts cost what the tuner is told they cost (the cheapest part shop that has
/// them), else what the mail order lists them at; a part in the ads
/// (<see cref="UsedPartOffer"/>) is bought used when it asks less. What comes off is traded in, or sold on by whoever
/// does the work (<see cref="TuneUpgrade.Removed"/>).
///
/// A tuner who is told how the engine is cooled (<see cref="EngineCooling"/>) keeps it cool: once the power outgrows
/// the radiator, a performance radiator or water pump goes on with the upgrade, and an upgrade that cannot be kept
/// cool within the budget is not made. Keeping it cool does not count as one of the upgrades.
///
/// The rules a rival tunes by, on the part tree and the game's own dyno.
/// </summary>
public static class EngineTuner
{
    /// <summary>An upgrade gains at least this share of power, or it is not worth the money</summary>
    public static double MinGain => EngineRules.Now.Tuner.MinGain;

    public static int MaxReplacementDepth => EngineRules.Now.Tuner.MaxReplacementDepth;

    /// <summary>A swapped-in engine makes no more than this times the power of the one it replaces: a bigger engine of the family, not a race engine</summary>
    public static double MaxSwapPower => EngineRules.Now.Tuner.MaxSwapPower;

    /// <summary>How worn an engine out of the paper is</summary>
    public static double UsedEngineCondition => EngineRules.Now.Tuner.UsedEngineCondition;

    public const string BlockGroup = PartGroups.Blocks;

    /// <summary>What a radiator or water pump put on to keep a tuned engine cool counts as: no shelf of its own</summary>
    public const string CoolingGroup = "cooling";

    /// <summary>Cooled this well (<see cref="Models.Race.EngineCoolingRating.Cooling"/>), an engine keeps its temperature at full throttle</summary>
    public static double CoolEnough => EngineRules.Now.Tuner.CoolEnough;

    /// <summary>Cooled less than this, an engine goes flat at full throttle: a tuner does not make that upgrade</summary>
    public static double RunsHotBelow => EngineRules.Now.Tuner.RunsHotBelow;

    /// <summary>How much a racer cares about each kind of part (<c>tune</c> in part_groups.json); 0: not tuned</summary>
    public static int Priority(string group) => PartGroups.Current.TunePriority(group);

    /// <summary>What the mail order asks for a new part, in the game's prices</summary>
    public static decimal NewPrice(PartDefinition part, double priceMultiplier) =>
        PartPricing.Round(PartPricing.NewPrice(part) * Multiplier.Sane(priceMultiplier));

    /// <summary>
    /// A tuned copy of <paramref name="engine"/> (which is left as it is) with up to <paramref name="maxUpgrades"/>
    /// upgrades whose parts together cost no more than <paramref name="budget"/>. Null when nothing is worth doing.
    /// </summary>
    /// <param name="maxTrials">Most dyno runs spent on looking; each takes a few milliseconds</param>
    /// <param name="used">Parts and whole engines in the paper; each is bought at most once</param>
    /// <param name="coolingOf">How well an engine is cooled at a power, in the car it goes in; null leaves the cooling alone</param>
    /// <param name="newPrice">What a new part costs the tuner (the cheapest shop that has it); null: the mail order's list price</param>
    public static TuneResult? TuneUp(PartsCatalog catalog, EngineBuildIndex builds, PartInstance engine, decimal budget,
        double priceMultiplier, Random random, int maxUpgrades = 2, int maxTrials = 60, IReadOnlyList<UsedPartOffer>? used = null,
        Func<PartInstance, double, double>? coolingOf = null, Func<PartDefinition, decimal>? newPrice = null)
    {
        var newPriceOf = newPrice ?? (part => NewPrice(part, priceMultiplier));

        var report = EngineFactory.Evaluate(catalog, engine);
        if (report is not { Runs: true }) return null;

        var upgrades = new List<TuneUpgrade>();
        var doneGroups = new HashSet<string>();
        var trials = maxTrials;
        var root = engine;

        // An engine already past its radiator is seen to first. It counts as work done on this engine: a block swap
        // after it would throw the new radiator away with the old engine, and bill it all the same
        if (coolingOf != null && coolingOf(root, report.Dyno!.MaxPowerHp) < CoolEnough
            && Cool(catalog, root, report.Dyno.MaxPowerHp, budget, newPriceOf, coolingOf) is { } cooled)
        {
            upgrades.Add(WithFitted(cooled.Upgrade, root, cooled.Engine));
            doneGroups.Add(CoolingGroup);
            root = cooled.Engine;
            budget -= cooled.Upgrade.Cost;
        }

        while (upgrades.Count(u => u.Group != CoolingGroup) < maxUpgrades && trials > 0)
        {
            var offers = (used ?? []).Where(o => upgrades.All(u => u.UsedAdId != o.AdId)).ToList();
            var best = FindBest(catalog, builds, root, report, budget, priceMultiplier, newPriceOf, random, doneGroups, offers, ref trials);
            if (best == null) break;
            doneGroups.Add(best.Upgrade.Group);

            // More power than the radiator carries off: a better one goes on with it, or the upgrade is not made
            var left = budget - best.Upgrade.Cost;
            var power = best.Report.Dyno!.MaxPowerHp;
            (PartInstance Engine, TuneUpgrade Upgrade)? cooling = null;
            if (coolingOf != null && coolingOf(best.Engine, power) < CoolEnough)
            {
                cooling = Cool(catalog, best.Engine, power, left, newPriceOf, coolingOf);
                if (coolingOf(cooling?.Engine ?? best.Engine, power) < RunsHotBelow) continue;
            }

            upgrades.Add(WithFitted(best.Upgrade, root, best.Engine));
            if (cooling is { } fitted) upgrades.Add(WithFitted(fitted.Upgrade, best.Engine, fitted.Engine));
            root = cooling?.Engine ?? best.Engine;
            report = best.Report;
            budget = left - (cooling?.Upgrade.Cost ?? 0);
        }

        if (upgrades.Count == 0) return null;

        // Each upgrade's parts as they sit on the finished engine: a later upgrade may have moved them over (same ids),
        // or taken them off again
        var final = root.SelfAndDescendants().DistinctBy(p => p.InstanceId).ToDictionary(p => p.InstanceId);
        return new TuneResult(root, report, upgrades
            .Select(u => u with { Fitted = u.Fitted.Where(p => final.ContainsKey(p.InstanceId)).Select(p => final[p.InstanceId]).ToList() })
            .ToList());
    }

    /// <summary>The upgrade with the parts on <paramref name="after"/> that were not on <paramref name="before"/>; none for a block swap</summary>
    private static TuneUpgrade WithFitted(TuneUpgrade upgrade, PartInstance before, PartInstance after)
    {
        if (upgrade.Group == BlockGroup) return upgrade;

        var had = before.SelfAndDescendants().Select(p => p.InstanceId).ToHashSet();
        return upgrade with { Fitted = after.SelfAndDescendants().Where(p => !had.Contains(p.InstanceId)).ToList() };
    }

    /// <summary>
    /// A performance radiator or water pump in place of a stock one, or a radiator where the engine has room for one
    /// and none: of those within the budget that cool the engine better, the one that cools it most (enough counts as
    /// most), the cheapest of those. The pump goes on after the radiator when one is not enough. Null when nothing
    /// cools it better.
    /// </summary>
    private static (PartInstance Engine, TuneUpgrade Upgrade)? Cool(PartsCatalog catalog, PartInstance root, double powerHp, decimal budget,
        Func<PartDefinition, decimal> newPriceOf, Func<PartInstance, double, double> coolingOf)
    {
        var now = coolingOf(root, powerHp);
        (PartInstance Engine, TuneUpgrade Upgrade, double Cooling)? best = null;

        void Consider(PartInstance tuned, PartDefinition part, decimal cost, decimal tradeIn, IReadOnlyList<PartInstance> removed)
        {
            if (cost > budget) return;
            var cooling = coolingOf(tuned, powerHp);
            if (cooling <= now + 1e-3) return;
            var reach = Math.Min(cooling, CoolEnough);
            var better = best is not { } b
                || reach > Math.Min(b.Cooling, CoolEnough) + 1e-3
                || reach >= Math.Min(b.Cooling, CoolEnough) - 1e-3 && cost < b.Upgrade.Cost;
            if (better) best = (tuned, new TuneUpgrade(Name(part), CoolingGroup, powerHp, powerHp, cost, tradeIn, removed), cooling);
        }

        static bool Performance(PartDefinition part, EngineCooling.Kind kind) =>
            kind == EngineCooling.Kind.Radiator ? EngineCooling.IsPerformanceRadiator(part) : EngineCooling.IsPerformancePump(part);

        foreach (var parent in root.SelfAndDescendants().ToList())
        {
            if (catalog.Get(parent.DefinitionId) is not { } parentDefinition) continue;
            foreach (var slot in parentDefinition.Slots.Where(s => s.Id != parent.OwnSlot))
            {
                // A stock radiator or pump to replace, or an empty place a radiator may go
                var old = parent.Children.FirstOrDefault(c => c.ParentSlot == slot.Id);
                PartDefinition? oldDefinition = null;
                if (old != null && (oldDefinition = catalog.Get(old.DefinitionId)) == null) continue;
                var kind = oldDefinition == null ? EngineCooling.Kind.Radiator : EngineCooling.KindOf(oldDefinition);
                if (kind is not (EngineCooling.Kind.Radiator or EngineCooling.Kind.WaterPump)) continue;
                if (oldDefinition != null && Performance(oldDefinition, kind)) continue;

                var alternatives = catalog.FindMountable(parentDefinition, slot)
                    .Where(c => EngineCooling.KindOf(c.Part) == kind && Performance(c.Part, kind))
                    .OrderBy(c => newPriceOf(c.Part))
                    .Take(3)
                    .ToList();
                foreach (var (definition, ownSlot) in alternatives)
                {
                    var extra = 0m;
                    var replaced = new List<PartInstance>();
                    var replacement = old == null
                        ? new PartInstance(definition.Id) { ParentSlot = slot.Id, OwnSlot = ownSlot.Id }
                        : Rebuild(catalog, definition, ownSlot.Id, old, newPriceOf, 0, replaced, ref extra);
                    if (replacement == null) continue;

                    var tuned = PartTrees.Clone(root, keepIds: true);
                    var tunedParent = tuned.SelfAndDescendants().First(p => p.InstanceId == parent.InstanceId);
                    if (old == null) tunedParent.Children.Add(replacement);
                    else tunedParent.Children[tunedParent.Children.FindIndex(c => c.InstanceId == old.InstanceId)] = replacement;

                    List<PartInstance> removed = old == null ? [] : [Loose(old), .. replaced.Select(p => Loose(p))];
                    var tradeIn = old == null ? 0m : TradeIn(catalog, old) + replaced.Sum(p => TradeIn(catalog, p));
                    Consider(tuned, definition, newPriceOf(definition) + extra, tradeIn, removed);
                }
            }
        }

        if (best is not { } found) return null;
        if (found.Cooling >= CoolEnough) return (found.Engine, found.Upgrade);

        var more = Cool(catalog, found.Engine, powerHp, budget - found.Upgrade.Cost, newPriceOf, coolingOf);
        if (more is not { } then) return (found.Engine, found.Upgrade);
        return (then.Engine, found.Upgrade with
        {
            Change = $"{found.Upgrade.Change} and a {then.Upgrade.Change}",
            Cost = found.Upgrade.Cost + then.Upgrade.Cost,
            TradeIn = found.Upgrade.TradeIn + then.Upgrade.TradeIn,
            Removed = [.. found.Upgrade.Removed, .. then.Upgrade.Removed]
        });
    }

    private sealed record Candidate(PartInstance Engine, EngineReport Report, TuneUpgrade Upgrade, double Score);

    private static Candidate? FindBest(PartsCatalog catalog, EngineBuildIndex builds, PartInstance root, EngineReport report,
        decimal budget, double priceMultiplier, Func<PartDefinition, decimal> newPriceOf, Random random, HashSet<string> doneGroups,
        List<UsedPartOffer> used, ref int trials)
    {
        var power = report.Dyno!.MaxPowerHp;
        Candidate? best = null;

        // Loose parts in the paper by what they are, the cheapest of each; whole engines on their own
        var usedParts = used
            .Where(o => o.Part.Children.Count == 0)
            .GroupBy(o => o.Part.DefinitionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Price).First(), StringComparer.OrdinalIgnoreCase);
        UsedPartOffer? UsedFor(PartDefinition part, decimal newPrice) =>
            usedParts.TryGetValue(part.Id, out var offer) && offer.Price < newPrice ? offer : null;
        decimal Price(PartDefinition part)
        {
            var newPrice = newPriceOf(part);
            return UsedFor(part, newPrice)?.Price ?? newPrice;
        }

        void Consider(PartInstance tuned, string change, string group, decimal cost, decimal tradeIn, Guid? adId, IReadOnlyList<PartInstance> removed,
            double maxPower = double.PositiveInfinity)
        {
            var after = EngineFactory.Evaluate(catalog, tuned);
            if (after is not { Runs: true }) return;

            var gain = after.Dyno!.MaxPowerHp / power - 1;
            if (gain < MinGain || after.Dyno.MaxPowerHp > maxPower) return;

            var score = gain * 100 / (double)Math.Max(1m, cost) * Priority(group);
            if (best == null || score > best.Score)
                best = new Candidate(tuned, after, new TuneUpgrade(change, group, power, after.Dyno.MaxPowerHp, cost, tradeIn, removed, adId), score);
        }

        // Bolt-ons: every part of a kind worth tuning, against what else goes where it sits
        var parts = root.SelfAndDescendants().Where(p => !ReferenceEquals(p, root)).ToList();
        foreach (var old in parts.OrderBy(_ => random.Next()))
        {
            if (trials <= 0) break;
            if (catalog.Get(old.DefinitionId) is not { } oldDefinition) continue;

            var group = PartKinds.GroupOf(oldDefinition);
            if (Priority(group) == 0 || doneGroups.Contains(group)) continue;

            var parent = PartTrees.FindParent(root, old);
            var parentDefinition = parent == null ? null : catalog.Get(parent.DefinitionId);
            var parentSlot = parentDefinition?.Slots.FirstOrDefault(s => s.Id == old.ParentSlot);
            if (parentDefinition == null || parentSlot == null) continue;

            var alternatives = catalog.FindMountable(parentDefinition, parentSlot)
                .Where(c => PartKinds.GroupOf(c.Part) == group && !c.Part.Id.Equals(oldDefinition.Id, StringComparison.OrdinalIgnoreCase))
                .Where(c => Price(c.Part) <= budget)
                .OrderBy(_ => random.Next())
                .Take(6);

            foreach (var (newDefinition, newOwnSlot) in alternatives)
            {
                if (trials <= 0) break;

                var extra = 0m;
                var replaced = new List<PartInstance>();
                var replacement = Rebuild(catalog, newDefinition, newOwnSlot.Id, old, newPriceOf, 0, replaced, ref extra);
                if (replacement == null) continue;

                // Out of the paper when it asks less than new: that very part, worn as it is
                var newPrice = newPriceOf(newDefinition);
                var offer = UsedFor(newDefinition, newPrice);
                if (offer != null)
                {
                    replacement.InstanceId = offer.Part.InstanceId;
                    replacement.Wear = offer.Part.Wear;
                    replacement.Tear = offer.Part.Tear;
                    replacement.Tuning = new Dictionary<string, double>(offer.Part.Tuning ?? []);
                    replacement.Cuts = new Dictionary<string, int>(offer.Part.Cuts ?? []);
                }

                var cost = (offer?.Price ?? newPrice) + extra;
                if (cost > budget) continue;

                var tuned = PartTrees.Clone(root, keepIds: true);
                var tunedParent = tuned.SelfAndDescendants().First(p => p.InstanceId == parent!.InstanceId);
                var index = tunedParent.Children.FindIndex(c => c.InstanceId == old.InstanceId);
                tunedParent.Children[index] = replacement;

                trials--;
                var tradeIn = TradeIn(catalog, old) + replaced.Sum(p => TradeIn(catalog, p));
                Consider(tuned, offer != null ? $"used {Name(newDefinition)}" : Name(newDefinition), group, cost, tradeIn, offer?.AdId,
                    [Loose(old), .. replaced.Select(p => Loose(p))]);
            }
        }

        // The block swap: a bigger engine of the same family, used, with what goes with it. Only on an engine not yet
        // tuned: the swap throws away the old engine's parts, and a bolt-on or a radiator bought before it would go with them
        if (doneGroups.Count == 0 && trials > 0 && builds.Runnable.FirstOrDefault(b => b.BlockId.Equals(root.DefinitionId, StringComparison.OrdinalIgnoreCase)) is { Family.Length: > 0 } current)
        {
            var bigger = builds.Runnable
                .Where(b => b.Family == current.Family && !b.Build.Aftermarket && b.PowerHp >= power * (1 + MinGain) && b.PowerHp <= power * MaxSwapPower)
                .Select(b => (Build: b, Cost: UsedEnginePrice(catalog, b, priceMultiplier)))
                .Where(b => b.Cost <= budget)
                .OrderBy(_ => random.Next())
                .Take(3)
                .ToList();

            foreach (var (build, cost) in bigger)
            {
                if (trials <= 0) break;
                if (EngineFactory.CreateStock(catalog, build, UsedEngineCondition, random) is not { } stock) continue;

                trials--;
                Consider(stock.Root, $"the engine of a {build.Build.Name}", BlockGroup, cost, PartPricing.TradeIn(catalog, root), null,
                    [Loose(root, withChildren: true)]);
            }

            // A whole engine somebody put in the paper, of the family. Not too big is the engine's own dyno figure: it
            // may have been worked on well past the stock build of its block.
            var engines = used
                .Where(o => o.Part.Children.Count > 0 && o.Price <= budget
                            && builds.Runnable.Any(b => b.Family == current.Family && b.BlockId.Equals(o.Part.DefinitionId, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(_ => random.Next())
                .Take(3)
                .ToList();

            foreach (var offer in engines)
            {
                if (trials <= 0) break;

                var engine = PartTrees.Clone(offer.Part, keepIds: true);
                engine.ParentSlot = root.ParentSlot;
                engine.OwnSlot = root.OwnSlot;

                var name = catalog.Get(offer.Part.DefinitionId) is { } block ? Name(block) : offer.Part.DefinitionId;
                trials--;
                Consider(engine, $"a used {name} out of the paper", BlockGroup, offer.Price, PartPricing.TradeIn(catalog, root),
                    offer.AdId, [Loose(root, withChildren: true)], maxPower: power * MaxSwapPower);
            }
        }

        return best;
    }

    /// <summary>
    /// The kinds of part on <paramref name="engine"/> (not the block): the only kinds <see cref="PowerWith"/> can try, so
    /// nothing else need go on the dyno
    /// </summary>
    public static HashSet<string> KindsOn(PartsCatalog catalog, PartInstance engine) =>
        engine.SelfAndDescendants()
            .Where(p => !ReferenceEquals(p, engine))
            .Select(p => catalog.Get(p.DefinitionId))
            .OfType<PartDefinition>()
            .Select(PartKinds.GroupOf)
            .Where(g => g != BlockGroup)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// What <paramref name="engine"/> makes on the dyno now, and the most it makes with <paramref name="part"/> in the place
    /// of a part of its kind (what was on the old part moved over, as a tuner does it); null when the engine does not run,
    /// or the part takes the place of none of its parts. A whole engine is no part to try (<see cref="BlockGroup"/>).
    /// <paramref name="powerNow"/> is what the engine makes now, when the caller has it (many parts tried on one engine).
    /// </summary>
    public static (double Before, double After)? PowerWith(PartsCatalog catalog, PartInstance engine, PartDefinition part, double? powerNow = null)
    {
        var group = PartKinds.GroupOf(part);
        if (group == BlockGroup) return null;

        // A part of a kind not on the engine takes the place of nothing: no dyno run at all
        var olds = engine.SelfAndDescendants()
            .Where(p => !ReferenceEquals(p, engine))
            .Select(p => (Part: p, Definition: catalog.Get(p.DefinitionId)))
            .Where(o => o.Definition != null && PartKinds.GroupOf(o.Definition) == group)
            .ToList();
        if (olds.Count == 0) return null;

        var before = powerNow ?? PowerOf(catalog, engine);
        if (before is not { } now) return null;

        double? best = null;
        foreach (var (old, oldDefinition) in olds)
        {
            if (oldDefinition!.Id.Equals(part.Id, StringComparison.OrdinalIgnoreCase)) return (now, now);

            var parent = PartTrees.FindParent(engine, old);
            var parentDefinition = parent == null ? null : catalog.Get(parent.DefinitionId);
            var parentSlot = parentDefinition?.Slots.FirstOrDefault(s => s.Id == old.ParentSlot);
            if (parentDefinition == null || parentSlot == null) continue;

            foreach (var (_, ownSlot) in catalog.FindMountable(parentDefinition, parentSlot).Where(c => c.Part.Id.Equals(part.Id, StringComparison.OrdinalIgnoreCase)))
            {
                var extra = 0m;
                var replacement = Rebuild(catalog, part, ownSlot.Id, old, _ => 0m, 0, [], ref extra);
                if (replacement == null) continue;

                var tuned = PartTrees.Clone(engine, keepIds: true);
                var tunedParent = tuned.SelfAndDescendants().First(p => p.InstanceId == parent!.InstanceId);
                tunedParent.Children[tunedParent.Children.FindIndex(c => c.InstanceId == old.InstanceId)] = replacement;
                if (EngineFactory.Evaluate(catalog, tuned) is { Runs: true, Dyno: { } after } && (best == null || after.MaxPowerHp > best))
                    best = after.MaxPowerHp;
            }
        }

        return best is { } most ? (now, most) : null;
    }

    /// <summary>What <paramref name="engine"/> makes on the dyno; null when it does not run</summary>
    public static double? PowerOf(PartsCatalog catalog, PartInstance engine) =>
        EngineFactory.Evaluate(catalog, engine) is { Runs: true, Dyno: { } dyno } ? dyno.MaxPowerHp : null;

    /// <summary>A part that came off, as it goes on a shelf or into the paper: a copy, loose, without what was on it unless said</summary>
    private static PartInstance Loose(PartInstance part, bool withChildren = false)
    {
        var loose = PartTrees.Clone(part, keepIds: true);
        if (!withChildren) loose.Children = [];
        loose.ParentSlot = 0;
        return loose;
    }

    /// <summary>A used engine out of the paper: what it makes (<see cref="PartPricing.EngineNewPrice"/>) at a used shop's price</summary>
    public static decimal UsedEnginePrice(PartsCatalog catalog, RatedBuild build, double priceMultiplier) =>
        PartPricing.Round(PartPricing.EngineNewPrice(CarPricing.PartsOf(catalog, build), build.PowerHp)
                          * PartPricing.UsedShopFactor * UsedEngineCondition * Multiplier.Sane(priceMultiplier));

    /// <summary>What a shop pays for a part that came off, without what is on it</summary>
    private static decimal TradeIn(PartsCatalog catalog, PartInstance part) =>
        PartPricing.Round((catalog.Get(part.DefinitionId) is { } definition ? PartPricing.Worth(definition, part) : 0) * PartPricing.TradeInFactor);

    /// <summary>
    /// A new <paramref name="definition"/> in the place of <paramref name="old"/>, with what was on the old part moved
    /// over; a part that does not go on the new one is replaced by the cheapest of its kind that does. Null when
    /// something finds no place.
    /// </summary>
    private static PartInstance? Rebuild(PartsCatalog catalog, PartDefinition definition, int ownSlot, PartInstance old,
        Func<PartDefinition, decimal> newPriceOf, int depth, List<PartInstance> replaced, ref decimal extraCost)
    {
        var replacement = new PartInstance(definition.Id) { ParentSlot = old.ParentSlot, OwnSlot = ownSlot };

        foreach (var child in old.Children)
        {
            var childDefinition = catalog.Get(child.DefinitionId);
            if (childDefinition == null || childDefinition.Slots.All(s => s.Id != child.OwnSlot)) return null;
            if (MoveOnto(catalog, child, definition, replacement)) continue;

            // It does not go on the new part: the cheapest of its kind that does, with what was on it
            if (depth >= MaxReplacementDepth) return null;

            var group = PartKinds.GroupOf(childDefinition);
            var options = FreeSlotsFor(definition, replacement, child)
                .SelectMany(s => catalog.FindMountable(definition, s).Select(c => (Slot: s, c.Part, OwnSlot: c.Slot)))
                .Where(c => PartKinds.GroupOf(c.Part) == group)
                .OrderBy(c => newPriceOf(c.Part));

            PartInstance? substitute = null;
            foreach (var option in options)
            {
                var deeper = 0m;
                var deeperReplaced = new List<PartInstance>();
                var stub = new PartInstance(child.DefinitionId) { ParentSlot = option.Slot.Id, OwnSlot = child.OwnSlot, Children = child.Children };
                substitute = Rebuild(catalog, option.Part, option.OwnSlot.Id, stub, newPriceOf, depth + 1, deeperReplaced, ref deeper);
                if (substitute == null) continue;

                extraCost += newPriceOf(option.Part) + deeper;
                replaced.Add(child);
                replaced.AddRange(deeperReplaced);
                break;
            }

            if (substitute == null) return null;
            replacement.Children.Add(substitute);
        }

        return replacement;
    }

    /// <summary>
    /// What was on the old part finds its place on the new one: <paramref name="child"/> (with what is on it, as a copy
    /// keeping its ids) onto a free slot of <paramref name="replacement"/>, the slot of the same id as it sat on first,
    /// else the first it mates with. False when it goes nowhere on the new part. The one way for the tuner and for the
    /// factory's bolt-ons (<see cref="EngineFactory"/>).
    /// </summary>
    internal static bool MoveOnto(PartsCatalog catalog, PartInstance child, PartDefinition definition, PartInstance replacement)
    {
        var childDefinition = catalog.Get(child.DefinitionId);
        var childSlot = childDefinition?.Slots.FirstOrDefault(s => s.Id == child.OwnSlot);
        if (childDefinition == null || childSlot == null) return false;

        var target = FreeSlotsFor(definition, replacement, child).FirstOrDefault(s => PartsCatalog.CanMate(childSlot, s));
        if (target == null) return false;

        var moved = PartTrees.Clone(child, keepIds: true);
        moved.ParentSlot = target.Id;
        replacement.Children.Add(moved);
        return true;
    }

    /// <summary>The slots of <paramref name="replacement"/> still free for <paramref name="child"/>, the one it sat on before first</summary>
    private static List<PartSlot> FreeSlotsFor(PartDefinition definition, PartInstance replacement, PartInstance child) =>
        definition.Slots
            .Where(s => s.Id != replacement.OwnSlot && replacement.Children.All(c => c.ParentSlot != s.Id))
            .OrderByDescending(s => s.Id == child.ParentSlot)
            .ToList();

    private static string Name(PartDefinition part) => part.DisplayName ?? part.Name;
}
