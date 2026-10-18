using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// The work order asked of a saved part (<see cref="PartInstance"/>): its kind and its rule by its catalog definition,
/// and whether it is one of some kinds or does a kind's job. <see cref="EngineWork"/> and <see cref="CarWork"/> ask the
/// same questions of an engine's parts and the car's running gear; a part the catalog does not know has no kind and so
/// no rule.
/// </summary>
internal static class PartWork
{
    public static string? KindOf(PartsCatalog catalog, PartInstance part) =>
        catalog.Get(part.DefinitionId) is { } definition ? catalog.WorkOrder.KindOf(definition) : null;

    public static WorkRule? RuleOf(PartsCatalog catalog, PartInstance part) =>
        catalog.Get(part.DefinitionId) is { } definition ? catalog.WorkOrder.RuleOf(definition) : null;

    /// <summary>The part is of the kind, any case</summary>
    public static bool IsKind(PartsCatalog catalog, PartInstance part, string kind) => KindIs(KindOf(catalog, part), kind);

    /// <summary>A kind (null for none) is the one wanted, any case</summary>
    public static bool KindIs(string? kind, string wanted) => kind != null && kind.Equals(wanted, StringComparison.OrdinalIgnoreCase);

    /// <summary>The part is of one of the kinds; never of none</summary>
    public static bool IsOneOf(PartsCatalog catalog, PartInstance part, IReadOnlyCollection<string> kinds) =>
        kinds.Count > 0 && KindOf(catalog, part) is { } kind && kinds.Contains(kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>The part does the job a part that needs the kind is after (<see cref="WorkOrder.DoesJobOf"/>)</summary>
    public static bool DoesJobOf(PartsCatalog catalog, PartInstance part, string needed) =>
        KindOf(catalog, part) is { } kind && catalog.WorkOrder.DoesJobOf(kind, needed);
}
