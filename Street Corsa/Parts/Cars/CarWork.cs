using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// The catalog's <see cref="WorkOrder"/> put to a car's running gear, as <see cref="EngineWork"/> puts it to an engine:
/// whether a part may come off the car now, or go on it, and why not. The engine's parts are left to that; this is
/// everything else on the car: the wheels, the brakes, the springs, the rear axle.
/// </summary>
/// <remarks>
/// Two things differ from the engine's. A rule counts at the part's own corner: the left front drum waits for the left
/// front wheel, not for the others; a part across an axle (the rear axle, a sway bar) counts at both of its corners, one
/// on no corner at all of them. And what goes along with a part (what is mounted on it) never stands in its way, so the
/// rear axle comes off whole with the brakes and springs it carries; only <see cref="WorkRule.Bare"/> asks for a part's own
/// parts off it first. <see cref="WorkRule.HangsOn"/> is the car's alone: the rear axle sits on its leaf springs; so is
/// <see cref="WorkRule.Lifted"/>: a wheel comes off and goes on with the car up on the lift.
/// </remarks>
public static class CarWork
{
    private const int AllCorners = 0b1111;

    /// <summary>Why the part, with what is on it, cannot come off the car now; null when it can, or when it is the engine's</summary>
    /// <param name="carParts">The parts mounted on the car itself (<c>Car.Parts</c>), the engine among them</param>
    /// <param name="lifted">The car is up on the lift (the car's LiftStop); a caller with no lift to tell of: true</param>
    public static string? WhyNotOff(PartsCatalog catalog, List<PartInstance> carParts, PartInstance part, bool lifted = true)
    {
        var corners = Corners(carParts);
        if (!corners.ContainsKey(part)) return null;
        if (!lifted && PartWork.RuleOf(catalog, part) is { Lifted: true }) return PartWords.Say("work.lift_first");

        var leaving = new HashSet<PartInstance>(part.SelfAndDescendants(), ReferenceEqualityComparer.Instance);
        var staying = corners.Keys.Where(p => !leaving.Contains(p)).ToList();
        var at = corners[part];
        var blocking = new HashSet<PartInstance>(ReferenceEqualityComparer.Instance);

        if (PartWork.RuleOf(catalog, part) is { } rule)
        {
            if (rule.Bare) blocking.UnionWith(part.Children.SelectMany(c => c.SelfAndDescendants()));
            blocking.UnionWith(staying.Where(p => (corners[p] & at) != 0 && PartWork.IsOneOf(catalog, p, rule.After)));
        }

        foreach (var stays in staying)
        {
            if (PartWork.RuleOf(catalog, stays) is not { } staysRule) continue;

            // What stays on and needs a kind at its corner that would leave with the last part doing its job there: the
            // leaf's hanger while the leaf is on
            var near = corners[stays];
            if (staysRule.Needs.Any(kind => leaving.Any(p => (corners[p] & near) != 0 && PartWork.DoesJobOf(catalog, p, kind))
                                            && !staying.Any(p => (corners[p] & near) != 0 && PartWork.DoesJobOf(catalog, p, kind))))
                blocking.Add(stays);

            // What sits on a part that would leave: the rear axle on its leaves
            if (staysRule.HangsOn.Count > 0 && leaving.Any(p => (corners[p] & near) != 0 && PartWork.IsOneOf(catalog, p, staysRule.HangsOn)))
                return PartWords.Say("work.hangs_on.off", ("part", EngineWork.List(catalog, [stays])));
        }

        var blockers = staying.Where(blocking.Contains).Concat(part.SelfAndDescendants().Where(blocking.Contains)).ToList();
        return blockers.Count == 0 ? null : PartWords.Say("work.take_off_first", ("parts", EngineWork.List(catalog, blockers)));
    }

    /// <summary>Why the loose part, with what is on it, cannot go on the car in this place now; null when it can</summary>
    /// <param name="carParts">The parts mounted on the car itself, the engine among them</param>
    /// <param name="liveAxle">Whether the car's rear is a live axle (where its rear running gear goes: <see cref="Workbench.FindPlaces"/>)</param>
    /// <param name="factory">The car's factory running gear, which tells what the car is sprung on before a part of it is there</param>
    /// <param name="lifted">The car is up on the lift (the car's LiftStop); a caller with no lift to tell of: true</param>
    public static string? WhyNotOn(PartsCatalog catalog, List<PartInstance> carParts, PartInstance loose, MountPlace place, bool liveAxle,
        IReadOnlyCollection<PartDefinition>? factory = null, bool lifted = true)
    {
        if (PartWork.RuleOf(catalog, loose) is not { } rule) return null;
        if (!lifted && rule.Lifted) return PartWords.Say("work.lift_first");

        var order = catalog.WorkOrder;
        var corners = Corners(carParts);
        var at = place.Parent == null ? CornersOfCarSlot(place.ParentSlot)
            : corners.TryGetValue(place.Parent, out var parentCorners) ? ChildCorners(place.Parent, place.ParentSlot, parentCorners)
            : 0;
        if (at == 0) return null;

        if (rule.Bare && loose.Children.Count > 0)
            return PartWords.Say(EngineWork.Plural(order, rule.Kind, "work.bare"), ("parts", EngineWork.List(catalog, loose.Children.SelectMany(c => c.SelfAndDescendants()))),
                ("kind", order.NameOf(rule.Kind)));

        var near = corners.Where(c => (c.Value & at) != 0).Select(c => c.Key).ToList();
        var blockers = near.Where(p => PartWork.IsOneOf(catalog, p, rule.After)).ToList();
        if (blockers.Count > 0) return PartWords.Say("work.take_off_first", ("parts", EngineWork.List(catalog, blockers)));

        var looseParts = loose.SelfAndDescendants().ToList();
        var missing = rule.Needs.Where(kind => !near.Concat(looseParts).Any(p => PartWork.DoesJobOf(catalog, p, kind))).ToList();
        if (missing.Count > 0) return PartWords.Say("work.put_on_first", ("parts", EngineWork.JoinAnd(missing.Select(k => EngineWork.The(order.NameOf(k))).ToList())));

        // What it sits on: every place the car has for it holds one, on a car sprung on that kind
        foreach (var kind in rule.HangsOn)
        {
            var uses = corners.Keys.Any(p => PartWork.IsKind(catalog, p, kind)) || factory?.Any(d => PartWork.KindIs(order.KindOf(d), kind)) == true;
            if (uses && HasFreePlaceFor(catalog, carParts, kind, liveAxle))
                return PartWords.Say("work.hangs_on.on", ("name", order.NameOf(rule.Kind)), ("kinds", order.NameOf(kind, 2)));
        }

        return null;
    }

    /// <summary>
    /// Why the car cannot come down off the lift now: a wheel is off, and it would stand on its hub; null when it can
    /// </summary>
    /// <param name="carParts">The parts mounted on the car itself</param>
    /// <param name="hasRunningGear">The car has its running gear as parts: one of an older save that has not has no wheels to tell of</param>
    public static string? WhyNotDown(List<PartInstance> carParts, bool hasRunningGear) =>
        hasRunningGear && RunningGear.Rims(carParts).Select(p => p.ParentSlot).Distinct().Count() < RunningGear.Corners
            ? PartWords.Say("work.wheels_first")
            : null;

    /// <summary>
    /// Whether a part of the kind could go on the car somewhere now: a free place for one. Parts that mount the same way
    /// (the same shelf, axle and slots) find the same places, so one of each is asked, not every leaf spring of the catalog
    /// </summary>
    private static bool HasFreePlaceFor(PartsCatalog catalog, List<PartInstance> carParts, string kind, bool liveAxle) =>
        catalog.Parts.Values.Where(d => PartWork.KindIs(catalog.WorkOrder.KindOf(d), kind))
            .DistinctBy(MountingOf)
            .Any(d => Workbench.FindPlaces(catalog, carParts, new PartInstance(d.Id), liveAxle).Count > 0);

    /// <summary>How a part mounts, as <see cref="Workbench.FindPlaces"/> reads it: its shelf, the axle it fits and its slots' fittings</summary>
    private static string MountingOf(PartDefinition part) =>
        PartKinds.GroupOf(part) + "|" + RunningGear.Text(part, "fits_axle") + "|" + string.Join(";", part.Slots.Select(s =>
            $"{s.Id}:{string.Join(",", s.Fits)}:{string.Join(",", s.Takes)}"));

    /// <summary>
    /// Every part on the car but the engine's, with the corners it works at (bit i for corner i): a wheel's, brake's,
    /// spring's or shock's own; what is on the rear axle's housing at a corner slot or a shaft slot that side's; the rear
    /// axle and its middle both rear corners, a sway bar its axle's; anything else every corner. What is on a part works
    /// where it does.
    /// </summary>
    private static Dictionary<PartInstance, int> Corners(List<PartInstance> carParts)
    {
        var corners = new Dictionary<PartInstance, int>(ReferenceEqualityComparer.Instance);
        foreach (var root in carParts.Where(p => p.ParentSlot != PartInstance.CarEngineSlot)) Add(root, CornersOfCarSlot(root.ParentSlot), 0);
        return corners;

        // No deeper than a save is read (PartInstance.MaxDepth): a save is anyone's file, and a stack overflow cannot be caught
        void Add(PartInstance part, int at, int depth)
        {
            if (depth >= PartInstance.MaxDepth || !corners.TryAdd(part, at)) return;
            foreach (var child in part.Children) Add(child, ChildCorners(part, child.ParentSlot, at), depth + 1);
        }
    }

    /// <summary>The corners a part on a slot of another works at: on the rear axle's housing, the side of the slot</summary>
    private static int ChildCorners(PartInstance parent, int slot, int parentCorners)
    {
        if (parent.ParentSlot != RearAxle.CarSlot) return parentCorners;
        if (RearAxle.CarSlotFor(slot) is { } carSlot && RunningGear.CornerOf(carSlot) is >= 0 and var corner) return 1 << corner;
        return slot switch
        {
            RearAxle.LeftShaftSlot => 1 << 2,
            RearAxle.RightShaftSlot => 1 << 3,
            _ => parentCorners
        };
    }

    private static int CornersOfCarSlot(int carSlot) => carSlot switch
    {
        RearAxle.CarSlot or RunningGear.RearSwayBarSlot => 0b1100,
        RunningGear.FrontSwayBarSlot => 0b0011,
        _ when RunningGear.CornerOf(carSlot) is >= 0 and var corner => 1 << corner,
        _ => AllCorners
    };

}
