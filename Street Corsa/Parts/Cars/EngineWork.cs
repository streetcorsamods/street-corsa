using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// The catalog's <see cref="WorkOrder"/> put to an engine: whether a part may come off it now, or go on it, and why
/// not. The rules work both ways: what has to be off before a part comes off has to be off before it goes back on,
/// so an engine taken apart can always be put together again in the opposite order. What a part needs
/// (<see cref="WorkRule.Needs"/>) holds both ways too: it stays on while the part is on, and an engine that comes into
/// the game with a part lacking what it needs has that part taken off first (<see cref="TakeOffWhatLacksItsNeeds"/>).
/// Only engines have rules; the running gear comes and goes freely.
/// </summary>
public static class EngineWork
{
    /// <summary>Why the part, with what is on it, cannot come off the engine now; null when it can</summary>
    /// <param name="engine">The engine block the part is on, with everything on it</param>
    /// <param name="onStand">The engine is on the stand, not in a car</param>
    /// <remarks>
    /// Asks the rules of every part that would come off, as <see cref="WhyNotOn"/> does of every part that would go
    /// on: a head with a camshaft on it must not leave a car it could never go back into.
    /// </remarks>
    public static string? WhyNotOff(PartsCatalog catalog, PartInstance engine, PartInstance part, bool onStand)
    {
        if (ReferenceEquals(part, engine)) return null;

        var order = catalog.WorkOrder;
        var ruled = part.SelfAndDescendants()
            .Select(p => (Part: p, Rule: RuleOf(catalog, p)))
            .Where(r => r.Rule != null)
            .ToList();

        if (!onStand && ruled.Select(r => r.Rule!).FirstOrDefault(r => r.StandOnly) is { } standOnly)
            return $"The {order.NameOf(standOnly.Kind)} only come{(IsPlural(order, standOnly.Kind) ? "" : "s")} out on the engine stand: pull the engine first.";

        // A set: a part can be in the way twice over (on a bare part and of a kind that has to be off first)
        var blocking = new HashSet<PartInstance>(ReferenceEqualityComparer.Instance);
        var all = engine.SelfAndDescendants().ToList();
        foreach (var (ruledPart, rule) in ruled)
        {
            if (rule!.Bare) blocking.UnionWith(ruledPart.Children.SelfAndDescendantsOf());
            var elsewhere = OnOthersOfItsKind(catalog, all, ruledPart);
            blocking.UnionWith(all.Where(p => !ReferenceEquals(p, ruledPart) && IsOneOf(catalog, p, rule.After) && !elsewhere.Contains(p)));
        }

        // What stays on and needs a kind that would leave with the last part doing its job: the belt when the fan
        // comes off
        var leaving = new HashSet<PartInstance>(part.SelfAndDescendants(), ReferenceEqualityComparer.Instance);
        var staying = all.Where(p => !leaving.Contains(p)).ToList();
        foreach (var stays in staying)
        {
            if (RuleOf(catalog, stays) is not { Needs.Count: > 0 } rule) continue;
            if (rule.Needs.Any(kind => leaving.Any(p => DoesJobOf(catalog, p, kind)) && !staying.Any(p => DoesJobOf(catalog, p, kind))))
                blocking.Add(stays);
        }

        var blockers = all.Where(blocking.Contains).ToList();
        return blockers.Count == 0 ? null : $"Take off {List(catalog, blockers)} first.";
    }

    /// <summary>
    /// Why the loose part, with what is on it, cannot go on the engine now; null when it can. Nothing of
    /// <see cref="WorkOrder.StandStrips"/> goes on an engine on the stand. A part that needs a kind waits for it only
    /// where the engine takes one, and only for one that may go on here (a stand-only kind in the car, one of the stand
    /// strips on the stand, is not waited for).
    /// </summary>
    public static string? WhyNotOn(PartsCatalog catalog, PartInstance engine, PartInstance loose, bool onStand)
    {
        var order = catalog.WorkOrder;
        var looseParts = loose.SelfAndDescendants().ToList();
        var whole = engine.SelfAndDescendants().Concat(looseParts).ToList();

        // What comes off for the stand stays off on it: the stand's head is bolted where the gearbox goes
        if (onStand && looseParts.Select(p => KindOf(catalog, p)).FirstOrDefault(k => k != null && order.StandStripTurnOf(k) != int.MaxValue) is { } stripped)
            return $"The {order.NameOf(stripped)} go{(IsPlural(order, stripped) ? "" : "es")} on with the engine in the car, not on the stand.";

        foreach (var part in looseParts)
        {
            if (RuleOf(catalog, part) is not { } rule) continue;

            if (rule.StandOnly && !onStand)
                return $"The {order.NameOf(rule.Kind)} only go{(IsPlural(order, rule.Kind) ? "" : "es")} in on the engine stand.";

            if (rule.Bare && part.Children.Count > 0)
                return $"Take {List(catalog, part.Children.SelfAndDescendantsOf())} off the {order.NameOf(rule.Kind)} first: {(IsPlural(order, rule.Kind) ? "they go" : "it goes")} in by {(IsPlural(order, rule.Kind) ? "themselves" : "itself")}.";

            var elsewhere = OnOthersOfItsKind(catalog, whole, part);
            var blockers = whole.Where(p => !ReferenceEquals(p, part) && IsOneOf(catalog, p, rule.After) && !elsewhere.Contains(p)).ToList();
            if (blockers.Count > 0) return $"Take off {List(catalog, blockers)} first.";

            var missing = rule.Needs.Where(kind => !whole.Any(p => DoesJobOf(catalog, p, kind)) && MayGoOn(order, kind, onStand) && Takes(catalog, engine, kind))
                .ToList();
            if (missing.Count > 0) return $"Put on {JoinAnd(missing.Select(k => "the " + order.NameOf(k)).ToList())} first.";
        }

        return null;
    }

    /// <summary>
    /// Takes off every part that lacks a kind it needs where the engine takes one (a belt on an engine without its
    /// water pump), with whatever is on it: such a part could come off but never go back on. For engines coming into
    /// the game, from a build or a save, so the rules hold both ways on every engine. Returns them.
    /// </summary>
    public static List<PartInstance> TakeOffWhatLacksItsNeeds(PartsCatalog catalog, PartInstance engine)
    {
        var taken = new List<PartInstance>();
        while (true)
        {
            var all = engine.SelfAndDescendants().ToList();

            // The kinds the engine has a free place for, once a round and only when a part needs one
            HashSet<string>? takes = null;
            bool TakesKind(string kind) => (takes ??= FreeKinds(catalog, engine)).Contains(kind);

            var lacking = all.Skip(1).FirstOrDefault(p => RuleOf(catalog, p) is { Needs.Count: > 0 } rule
                                                          && rule.Needs.Any(kind => !all.Any(q => DoesJobOf(catalog, q, kind)) && TakesKind(kind)));
            if (lacking == null) return taken;

            // A part that will not come off (one the tree holds twice, under two parents) would be found again every
            // round: what could be taken off is what there is
            if (!TakeOff(engine, lacking)) return taken;
            taken.Add(lacking);
        }
    }

    /// <summary>
    /// A part of the kind may go on the engine where it is: a stand-only kind not in the car, nothing of
    /// <see cref="WorkOrder.StandStrips"/> on the stand
    /// </summary>
    private static bool MayGoOn(WorkOrder order, string kind, bool onStand) =>
        onStand ? order.StandStripTurnOf(kind) == int.MaxValue : order.RuleFor(kind) is not { StandOnly: true };

    /// <summary>
    /// The engine has a free place a part of the kind goes on: the block a water pump, a water pump its fan. The
    /// places are the workbench's own (<see cref="Workbench.FreeSlots"/>).
    /// </summary>
    private static bool Takes(PartsCatalog catalog, PartInstance engine, string kind) =>
        Workbench.FreeSlots(catalog, new[] { engine })
            .Any(free => catalog.FindMountable(free.Definition, free.Slot).Any(m => IsKind(catalog.WorkOrder.KindOf(m.Part), kind)));

    /// <summary>Every kind the engine has a free place for, each once: <see cref="Takes"/> for many kinds at a time</summary>
    private static HashSet<string> FreeKinds(PartsCatalog catalog, PartInstance engine) =>
        Workbench.FreeSlots(catalog, new[] { engine })
            .SelectMany(free => catalog.FindMountable(free.Definition, free.Slot))
            .Select(m => catalog.WorkOrder.KindOf(m.Part))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Takes off what comes off an engine as it goes on the stand, kind by kind in the order of
    /// <see cref="WorkOrder.StandStrips"/> (the radiator, then what is in the way of bolting it on by its bellhousing
    /// face: the starter, the transmission, the clutch, the flywheel), each with whatever is on it. Returns them in
    /// that order.
    /// </summary>
    public static List<PartInstance> StripForStand(PartsCatalog catalog, PartInstance engine)
    {
        var taken = new List<PartInstance>();
        foreach (var kind in catalog.WorkOrder.StandStrips)
        {
            foreach (var part in engine.SelfAndDescendants().Where(p => !ReferenceEquals(p, engine) && IsKind(KindOf(catalog, p), kind)).ToList())
            {
                if (!TakeOff(engine, part)) continue;

                taken.Add(part);
            }
        }

        return taken;
    }

    /// <summary>
    /// What comes off an engine as it goes on the stand, for the player: "the radiator, the starter, the transmission,
    /// the clutch and the flywheel", each name once (a radiator with an electric fan is a radiator too)
    /// </summary>
    public static string StandStripsDisplay(PartsCatalog catalog)
    {
        var order = catalog.WorkOrder;
        return JoinAnd(order.StandStrips.Select(k => "the " + order.NameOf(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()) ?? string.Empty;
    }

    /// <returns>False when the part is not on the engine</returns>
    private static bool TakeOff(PartInstance engine, PartInstance part)
    {
        if (PartTrees.FindParent(engine, part) is not { } parent) return false;

        parent.Children.Remove(part);
        part.ParentSlot = 0;
        part.OwnSlot = 0;
        return true;
    }

    private static string? KindOf(PartsCatalog catalog, PartInstance part) =>
        catalog.Get(part.DefinitionId) is { } definition ? catalog.WorkOrder.KindOf(definition) : null;

    private static WorkRule? RuleOf(PartsCatalog catalog, PartInstance part) =>
        catalog.Get(part.DefinitionId) is { } definition ? catalog.WorkOrder.RuleOf(definition) : null;

    private static bool IsKind(string? kind, string wanted) => kind != null && kind.Equals(wanted, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What sits on the engine's other parts of the part's kind, in places the part has its own of: the other head's
    /// valve cover and exhaust header (with what is on them) are not in this head's way, though they are of kinds a
    /// head waits for, as this head takes its own. What one of them carries for both (an intake on one head of a pair,
    /// a belt on one of two pulleys) the part has no place of its own for, and it stays in the way.
    /// </summary>
    private static HashSet<PartInstance> OnOthersOfItsKind(PartsCatalog catalog, IEnumerable<PartInstance> all, PartInstance part)
    {
        var elsewhere = new HashSet<PartInstance>(ReferenceEqualityComparer.Instance);
        if (KindOf(catalog, part) is not { } kind || catalog.Get(part.DefinitionId) is not { } definition) return elsewhere;

        var ownPlaces = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool HasOwnPlaceFor(string childKind)
        {
            if (!ownPlaces.TryGetValue(childKind, out var has))
            {
                has = definition.Slots.Where(s => s.Id != part.OwnSlot)
                    .Any(s => catalog.FindMountable(definition, s).Any(m => IsKind(catalog.WorkOrder.KindOf(m.Part), childKind)));
                ownPlaces[childKind] = has;
            }

            return has;
        }

        foreach (var other in all)
        {
            if (ReferenceEquals(other, part) || !IsKind(KindOf(catalog, other), kind)) continue;
            foreach (var child in other.Children)
                if (KindOf(catalog, child) is { } childKind && HasOwnPlaceFor(childKind)) elsewhere.UnionWith(child.SelfAndDescendants());
        }

        return elsewhere;
    }

    private static bool IsOneOf(PartsCatalog catalog, PartInstance part, IReadOnlyCollection<string> kinds) =>
        kinds.Count > 0 && KindOf(catalog, part) is { } kind && kinds.Contains(kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>The part does the job a part that needs the kind is after (<see cref="WorkOrder.DoesJobOf"/>)</summary>
    private static bool DoesJobOf(PartsCatalog catalog, PartInstance part, string needed) =>
        KindOf(catalog, part) is { } kind && catalog.WorkOrder.DoesJobOf(kind, needed);

    private static bool IsPlural(WorkOrder order, string kind) => order.NameOf(kind).EndsWith('s');

    private static IEnumerable<PartInstance> SelfAndDescendantsOf(this IEnumerable<PartInstance> parts) => parts.SelectMany(p => p.SelfAndDescendants());

    /// <summary>"the oil pan, the timing cover and the cylinder heads", in the order the engine holds them</summary>
    private static string List(PartsCatalog catalog, IEnumerable<PartInstance> parts)
    {
        var order = catalog.WorkOrder;
        var names = new List<string>();
        foreach (var group in parts.Select(p => catalog.Get(p.DefinitionId)).Where(d => d != null).GroupBy(d => order.KindOf(d!) ?? d!.Id))
        {
            var definition = group.First()!;
            var kind = order.KindOf(definition);
            var count = group.Count();
            names.Add("the " + (kind != null ? order.NameOf(kind, count) : definition.DisplayName ?? definition.Name));
        }

        return JoinAnd(names) ?? "nothing";
    }

    /// <summary>"a, b and c"; null when there is nothing</summary>
    private static string? JoinAnd(IReadOnlyList<string> names) => names.Count switch
    {
        0 => null,
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1]
    };
}
