using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// The catalog's <see cref="WorkOrder"/> put to an engine: whether a part may come off it now, or go on it, and why
/// not. The rules work both ways: what has to be off before a part comes off has to be off before it goes back on,
/// so an engine taken apart can always be put together again in the opposite order.
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
            .Select(p => (Part: p, Rule: catalog.Get(p.DefinitionId) is { } definition ? order.RuleOf(definition) : null))
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
            blocking.UnionWith(all.Where(p => !ReferenceEquals(p, ruledPart) && IsOneOf(catalog, p, rule.After)));
        }

        var blockers = all.Where(blocking.Contains).ToList();
        return blockers.Count == 0 ? null : $"Take off {List(catalog, blockers)} first.";
    }

    /// <summary>Why the loose part, with what is on it, cannot go on the engine now; null when it can</summary>
    public static string? WhyNotOn(PartsCatalog catalog, PartInstance engine, PartInstance loose, bool onStand)
    {
        var order = catalog.WorkOrder;
        var looseParts = loose.SelfAndDescendants().ToList();
        foreach (var part in looseParts)
        {
            if (catalog.Get(part.DefinitionId) is not { } definition || order.RuleOf(definition) is not { } rule) continue;

            if (rule.StandOnly && !onStand)
                return $"The {order.NameOf(rule.Kind)} only go{(IsPlural(order, rule.Kind) ? "" : "es")} in on the engine stand.";

            if (rule.Bare && part.Children.Count > 0)
                return $"Take {List(catalog, part.Children.SelfAndDescendantsOf())} off the {order.NameOf(rule.Kind)} first: {(IsPlural(order, rule.Kind) ? "they go" : "it goes")} in by {(IsPlural(order, rule.Kind) ? "themselves" : "itself")}.";

            var blockers = engine.SelfAndDescendants().Concat(looseParts)
                .Where(p => !ReferenceEquals(p, part) && IsOneOf(catalog, p, rule.After))
                .ToList();
            if (blockers.Count > 0) return $"Take off {List(catalog, blockers)} first.";
        }

        return null;
    }

    /// <summary>
    /// Takes off what comes off an engine as it goes on the stand, kind by kind in the order of
    /// <see cref="WorkOrder.StandStrips"/> (the radiator, then what is in the way of bolting it on by its bellhousing
    /// face: the transmission, the clutch, the flywheel), each with whatever is on it. Returns them in that order.
    /// </summary>
    public static List<PartInstance> StripForStand(PartsCatalog catalog, PartInstance engine)
    {
        var taken = new List<PartInstance>();
        foreach (var kind in catalog.WorkOrder.StandStrips)
        {
            foreach (var part in engine.SelfAndDescendants().Where(p => !ReferenceEquals(p, engine) && IsOneOf(catalog, p, new[] { kind })).ToList())
            {
                if (PartTrees.FindParent(engine, part) is not { } parent) continue;

                parent.Children.Remove(part);
                part.ParentSlot = 0;
                part.OwnSlot = 0;
                taken.Add(part);
            }
        }

        return taken;
    }

    /// <summary>What comes off an engine as it goes on the stand, for the player: "the transmission, the clutch and the flywheel"</summary>
    public static string StandStripsDisplay(PartsCatalog catalog)
    {
        var order = catalog.WorkOrder;
        return JoinAnd(order.StandStrips.Select(k => "the " + order.NameOf(k)).ToList()) ?? string.Empty;
    }

    private static bool IsOneOf(PartsCatalog catalog, PartInstance part, IReadOnlyCollection<string> kinds) =>
        kinds.Count > 0 && catalog.Get(part.DefinitionId) is { } definition && catalog.WorkOrder.KindOf(definition) is { } kind
        && kinds.Contains(kind, StringComparer.OrdinalIgnoreCase);

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
