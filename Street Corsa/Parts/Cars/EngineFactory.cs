using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts.Cars;

/// <summary>An engine made for a car: the parts, what they come to, and what was changed from the factory build</summary>
public sealed record BuiltEngine(PartInstance Root, EngineReport Report, IReadOnlyList<string> Changes)
{
    public bool IsModified => Changes.Count > 0;
}

/// <summary>
/// Makes the engine a car shows up with. The factory build is the starting point; cars that have been around
/// may have been worked on, and then only the way somebody plausibly would have: a bigger engine of the same
/// family, or bolt-on parts that fit this very engine. Every change has to leave an engine that runs, makes
/// no less power than before and does not run away from what the car left the factory with.
/// </summary>
public static class EngineFactory
{
    private static double WearNoise => EngineRules.Now.Factory.WearNoise;
    private static double NewerPartBonus => EngineRules.Now.Factory.NewerPartBonus;

    private static double EngineSwapChance => EngineRules.Now.Factory.EngineSwapChance;
    private static double SwapMinPower => EngineRules.Now.Factory.SwapMinPower;
    private static double SwapMaxPower => EngineRules.Now.Factory.SwapMaxPower;
    private static double MaxPowerOverStock => EngineRules.Now.Factory.MaxPowerOverStock;
    private static double MinPowerGain => EngineRules.Now.Factory.MinPowerGain;
    private static int MaxBoltOns => EngineRules.Now.Factory.MaxBoltOns;
    private static int AttemptsPerBoltOn => EngineRules.Now.Factory.AttemptsPerBoltOn;


    /// <param name="condition">0..1, how worn the car is; every part ends up around it</param>
    public static BuiltEngine? CreateStock(PartsCatalog catalog, RatedBuild build, double condition, Random random)
    {
        var root = Assemble(catalog, build);
        if (root == null) return null;

        ApplyWear(root, condition, random);
        return new BuiltEngine(root, Evaluate(catalog, root)!, Array.Empty<string>());
    }

    /// <summary>
    /// Whether an engine of <paramref name="other"/> goes where one of <paramref name="build"/> came: its gearbox is of the
    /// same class (a transaxle for a transaxle, one built along the car for another), or one of them has none to tell
    /// by. A family alone is no answer: one maker's transverse four and longitudinal six are of one family.
    /// </summary>
    public static bool SameDrivetrain(PartsCatalog catalog, RatedBuild build, RatedBuild other) =>
        GearboxClass(catalog, build) is not { } mine || GearboxClass(catalog, other) is not { } theirs
        || string.Equals(mine, theirs, StringComparison.OrdinalIgnoreCase);

    private static string? GearboxClass(PartsCatalog catalog, RatedBuild build) =>
        build.Build.Parts.Select(catalog.Get).OfType<PartDefinition>()
            .FirstOrDefault(p => PartKinds.Is(p, PartClasses.Transmission))?.Class;

    /// <param name="tuneLevel">0 = as it left the factory, 1 = worked on as much as it gets</param>
    public static BuiltEngine? CreateTuned(PartsCatalog catalog, EngineBuildIndex index, RatedBuild stock, double condition,
        double tuneLevel, Random random)
    {
        var changes = new List<string>();
        var build = stock;

        if (random.NextDouble() < EngineSwapChance * tuneLevel)
        {
            var bigger = index.Runnable
                .Where(b => b.Family.Length > 0 && b.Family == stock.Family && b.Build.Id != stock.Build.Id && !b.Build.Aftermarket
                            && b.PowerHp >= stock.PowerHp * SwapMinPower && b.PowerHp <= stock.PowerHp * SwapMaxPower
                            && SameDrivetrain(catalog, stock, b))
                .ToList();
            if (bigger.Count > 0)
            {
                build = bigger[random.Next(bigger.Count)];
                changes.Add(build.BlockId.Equals(stock.BlockId, StringComparison.OrdinalIgnoreCase)
                    ? $"engine rebuilt like the one of a {build.Build.Name}"
                    : $"engine of a {build.Build.Name} swapped in");
            }
        }

        var root = Assemble(catalog, build);
        if (root == null) return null;

        ApplyWear(root, condition, random);
        var report = Evaluate(catalog, root)!;

        var boltOns = (int)Math.Round(tuneLevel * MaxBoltOns * random.NextDouble());
        for (var i = 0; i < boltOns; i++)
        {
            for (var attempt = 0; attempt < AttemptsPerBoltOn; attempt++)
            {
                var swapped = TryBoltOn(catalog, root, report, stock.PowerHp * MaxPowerOverStock, condition, random);
                if (swapped == null) continue;

                report = swapped.Value.Report;
                changes.Add(swapped.Value.Change);
                break;
            }
        }

        return new BuiltEngine(root, report, changes);
    }

    /// <summary>Works a saved engine out from its parts and runs it on the dyno; null when its block is not in the catalog</summary>
    public static EngineReport? Evaluate(PartsCatalog catalog, PartInstance root) =>
        PartTrees.ToInstalled(catalog, root) is { } live ? EngineEvaluator.Evaluate(catalog, live.AsPartTree()) : null;

    /// <remarks>A part the build puts on without what it needs (a belt, on a mod's build without a water pump) is left off</remarks>
    private static PartInstance? Assemble(PartsCatalog catalog, RatedBuild build)
    {
        if (PartTreeBuilder.BuildEngine(catalog, build.Build) is not { } tree) return null;

        var root = PartTrees.FromInstalled(tree.Root, PartInstance.CarEngineSlot);
        EngineWork.TakeOffWhatLacksItsNeeds(catalog, root);
        return root;
    }

    private static void ApplyWear(PartInstance root, double condition, Random random)
    {
        foreach (var part in root.SelfAndDescendants()) part.Wear = WearAround(condition, random);
    }

    private static double WearAround(double condition, Random random) =>
        Math.Clamp(condition + (random.NextDouble() * 2 - 1) * WearNoise, 0.05, 1.0);

    /// <summary>Swaps one bolt-on part for another that goes in its place; leaves the engine alone when it does not work out</summary>
    private static (EngineReport Report, string Change)? TryBoltOn(PartsCatalog catalog, PartInstance root, EngineReport before,
        double maxPower, double condition, Random random)
    {
        var swappable = root.SelfAndDescendants()
            .Where(p => !ReferenceEquals(p, root) && catalog.Get(p.DefinitionId) is { } d && PartGroups.Current.IsBoltOn(PartKinds.GroupOf(d)))
            .ToList();
        if (swappable.Count == 0) return null;

        var old = swappable[random.Next(swappable.Count)];
        var parent = PartTrees.FindParent(root, old)!;
        var oldDefinition = catalog.Get(old.DefinitionId)!;
        var parentDefinition = catalog.Get(parent.DefinitionId);
        var parentSlot = parentDefinition?.Slots.FirstOrDefault(s => s.Id == old.ParentSlot);
        if (parentDefinition == null || parentSlot == null) return null;

        var group = PartKinds.GroupOf(oldDefinition);
        var alternatives = catalog.FindMountable(parentDefinition, parentSlot)
            .Where(c => !ReferenceEquals(c.Part, oldDefinition) && PartKinds.GroupOf(c.Part) == group)
            .ToList();
        if (alternatives.Count == 0) return null;

        var (newDefinition, newOwnSlot) = alternatives[random.Next(alternatives.Count)];
        var replacement = new PartInstance(newDefinition.Id)
        {
            ParentSlot = old.ParentSlot,
            OwnSlot = newOwnSlot.Id,
            Wear = Math.Clamp(WearAround(condition, random) + NewerPartBonus, 0.05, 1.0)
        };

        // Whatever was on the old part has to find its place on the new one, as a tuner moves it (EngineTuner.MoveOnto)
        foreach (var child in old.Children)
        {
            if (!EngineTuner.MoveOnto(catalog, child, newDefinition, replacement)) return null;
        }

        var index = parent.Children.IndexOf(old);
        parent.Children[index] = replacement;

        var after = Evaluate(catalog, root);
        // Worth doing: more power, or at least a better part for the same power (a clutch makes none)
        var powerBefore = before.Dyno?.MaxPowerHp ?? 0;
        var improved = after is { Runs: true } && after.Dyno!.MaxPowerHp >= powerBefore && after.Dyno.MaxPowerHp <= maxPower
                       && (after.Dyno.MaxPowerHp > powerBefore * MinPowerGain || PartPricing.NewPrice(newDefinition) > PartPricing.NewPrice(oldDefinition));
        if (!improved)
        {
            parent.Children[index] = old;
            return null;
        }

        return (after!, newDefinition.DisplayName ?? newDefinition.Name);
    }
}
