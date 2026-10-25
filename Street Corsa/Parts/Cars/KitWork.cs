using StreetCorsa.Models.GameState;
using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// An engine kit on sale where a rival can buy it (<see cref="EngineTuner.TuneUp"/>): new on a shop's shelf
/// (<paramref name="ShopId"/>) or used in the paper (<paramref name="AdId"/>), its parts loose as they come in the crate
/// </summary>
public sealed record KitOffer(Guid? AdId, string? ShopId, string BuildId, string SizeId, string Name, IReadOnlyList<PartInstance> Parts, decimal Price);

/// <summary>
/// Engine kits: an engine build's parts in one crate, as far as a kit's size takes them
/// (<see cref="EngineKits"/>), put together on the engine stand in one go; and an engine taken apart for its parts to go
/// in the paper as a used kit, short of what whoever took it apart kept back.
/// </summary>
public static class KitWork
{
    /// <summary>The parts of <paramref name="build"/> a kit of <paramref name="size"/> holds, in the build's order: the block always</summary>
    public static List<PartDefinition> PartsOf(PartsCatalog catalog, EngineBuild build, KitSize size) =>
        build.Parts
            .Select(catalog.Get)
            .OfType<PartDefinition>()
            .Where(p => PartKinds.IsBlock(p) || catalog.Kits.Holds(size, catalog.WorkOrder.KindOf(p)))
            .ToList();

    /// <summary>A new kit's parts, loose and new; empty for a build whose block the catalog does not have</summary>
    public static List<PartInstance> NewParts(PartsCatalog catalog, EngineBuild build, KitSize size)
    {
        var parts = PartsOf(catalog, build, size);
        return parts.Any(PartKinds.IsBlock) ? parts.Select(p => new PartInstance(p.Id)).ToList() : [];
    }

    /// <summary>
    /// The crate's parts put together, every part where it fits as a build's are (<see cref="PartTreeBuilder"/>), each
    /// piece the very one out of the crate (its wear, its cuts, its id); what finds no place, or lacks what it needs
    /// (<see cref="EngineWork.TakeOffWhatLacksItsNeeds"/>), is left over, loose. Null when there is no block among them.
    /// The engine comes loose, for the stand.
    /// </summary>
    public static (PartInstance Engine, List<PartInstance> Left)? Assemble(PartsCatalog catalog, IReadOnlyList<PartInstance> parts)
    {
        // A piece with parts on it already stays as it is: only loose parts go together
        var loose = parts.Where(p => p.Children is not { Count: > 0 } && catalog.Get(p.DefinitionId) != null).ToList();
        var tree = PartTreeBuilder.Build(catalog, loose.Select(p => catalog.Get(p.DefinitionId)!.Id), PartClasses.EngineBlock);
        if (tree == null) return null;

        var engine = PartTrees.FromInstalled(tree.Root);
        var crate = loose
            .GroupBy(p => catalog.Get(p.DefinitionId)!.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new Queue<PartInstance>(g), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<PartInstance>(ReferenceEqualityComparer.Instance);
        foreach (var placed in engine.SelfAndDescendants())
        {
            if (!crate.TryGetValue(placed.DefinitionId, out var pieces) || pieces.Count == 0) continue;

            var own = pieces.Dequeue();
            used.Add(own);
            placed.InstanceId = own.InstanceId;
            placed.Wear = own.Wear;
            placed.Tear = own.Tear;
            placed.Tuning = new Dictionary<string, double>(own.Tuning ?? []);
            placed.Cuts = new Dictionary<string, int>(own.Cuts ?? []);
        }

        var left = parts.Where(p => !used.Contains(p)).ToList();
        left.AddRange(EngineWork.TakeOffWhatLacksItsNeeds(catalog, engine));
        return (engine, left);
    }

    /// <summary>
    /// An engine taken apart for a crate: every part of it loose (copies, keeping their ids), the block first, and of
    /// those besides the smallest kit's (<see cref="EngineKits.Smallest"/>) as many as <paramref name="mostKept"/> kept
    /// back by whoever took it apart, at random: what the used kit's ad says is missing
    /// </summary>
    public static (List<PartInstance> Parts, List<PartInstance> Kept) TearDown(PartsCatalog catalog, PartInstance engine, Random random, int mostKept)
    {
        var all = engine.SelfAndDescendants().Select(p =>
        {
            var piece = PartTrees.Clone(p, keepIds: true);
            piece.Children = [];
            piece.ParentSlot = 0;
            piece.OwnSlot = 0;
            return piece;
        }).ToList();

        var smallest = catalog.Kits.Smallest;
        var spare = all.Skip(1)
            .Where(p => catalog.Get(p.DefinitionId) is { } d && !PartKinds.IsBlock(d) && (smallest == null || !catalog.Kits.Holds(smallest, catalog.WorkOrder.KindOf(d))))
            .OrderBy(_ => random.Next())
            .ToList();
        var kept = spare.Take(random.Next(0, Math.Min(Math.Max(0, mostKept), spare.Count) + 1)).ToList();
        all.RemoveAll(kept.Contains);
        return (all, kept);
    }

    /// <summary>What the parts of a crate are worth together (<see cref="PartPricing.WorthOfAssembly"/>)</summary>
    public static double WorthOf(PartsCatalog catalog, IEnumerable<PartInstance> parts) => parts.Sum(p => PartPricing.WorthOfAssembly(catalog, p));

    /// <summary>The names of <paramref name="parts"/> as the player reads them, for what a used kit is short of</summary>
    public static List<string> NamesOf(PartsCatalog catalog, IEnumerable<PartInstance> parts) =>
        parts.Select(p => catalog.Get(p.DefinitionId) is { } d ? d.DisplayName ?? d.Name : p.DefinitionId).ToList();

    /// <summary>The engine a crate is of, as the player reads it: the block's name; empty without one</summary>
    public static string EngineNameOf(PartsCatalog catalog, IEnumerable<PartInstance> parts) =>
        parts.Select(p => catalog.Get(p.DefinitionId)).FirstOrDefault(d => d != null && PartKinds.IsBlock(d)) is { } block
            ? block.DisplayName ?? block.Name
            : string.Empty;
}
