using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>A job of <see cref="PartRebuilds"/> as it stands for one part: what it costs and takes, and why it cannot be done now</summary>
/// <param name="WhyNot">Why it cannot be done now, in the player's words; null when it can</param>
public sealed record RebuildOffer(RebuildJob Job, string Name, decimal Cost, int Minutes, string? WhyNot, double After)
{
    public bool CanDo => WhyNot == null;
}

/// <summary>
/// Taking a part's mileage off (<c>Wear</c>) with the jobs of the catalog's <see cref="PartRebuilds"/>: a kit brings it
/// up to what the job can, no higher, and a rebuilt unit from the counter takes its damage (<c>Tear</c>) off too. The
/// money and the time are the caller's.
/// </summary>
public static class Rebuilding
{
    /// <summary>A part this close to what the job leaves it in gains nothing from it</summary>
    private const double Enough = 0.01;

    /// <summary>The job's name: <c>rebuild.&lt;id&gt;</c> in the text, else its own</summary>
    public static string NameOf(RebuildJob job)
    {
        var key = "rebuild." + job.Id;
        var said = PartWords.Say(key);
        if (said != key) return said;
        return job.Name is { Length: > 0 } name ? name : job.Id.Replace('_', ' ');
    }

    /// <summary>A step's caption: <c>rebuild.&lt;job&gt;.&lt;step&gt;</c> in the text, else its own</summary>
    public static string CaptionOf(RebuildJob job, RebuildStep step)
    {
        var key = $"rebuild.{job.Id}.{step.Id}";
        var said = PartWords.Say(key);
        if (said != key) return said;
        return step.Say is { Length: > 0 } say ? say : step.Id.Replace('_', ' ');
    }

    /// <summary>The kit or the rebuilt unit: its share of the part's new price, never less than its least, at the save's part prices</summary>
    /// <param name="priceMultiplier">The save's part prices (<c>GameRules.PartPriceMultiplier</c>)</param>
    public static decimal CostOf(RebuildJob job, PartDefinition part, double priceMultiplier = 1.0) =>
        PartPricing.Round(Math.Max(job.CostAtLeast, job.Cost * PartPricing.NewPrice(part)) * Multiplier.Sane(priceMultiplier));

    /// <summary>
    /// The kit or the rebuilt unit for the part as it is: <see cref="CostOf"/>, and never less than what the job adds to
    /// what a shop would pay for the part (<paramref name="resaleFactor"/> of its worth, with what is mounted on it,
    /// after the job less before), so a part bought worn, worked on and sold on is never money made
    /// </summary>
    /// <param name="resaleFactor">The most any shop pays for a part, against its worth; 0 for no such floor</param>
    public static decimal CostFor(RebuildJob job, PartsCatalog catalog, PartDefinition definition, PartInstance part, double priceMultiplier = 1.0, double resaleFactor = 0)
    {
        var cost = CostOf(job, definition, priceMultiplier);
        if (!(resaleFactor > 0) || !double.IsFinite(resaleFactor)) return cost;

        var after = PartTrees.Clone(part, keepIds: true);
        Apply(job, after);
        var gained = PartPricing.WorthOfAssembly(catalog, after) - PartPricing.WorthOfAssembly(catalog, part);
        return gained > 0 ? Math.Max(cost, Math.Ceiling(PartPricing.Round(gained * resaleFactor))) : cost;
    }

    /// <summary>Why the job cannot be done on the part now; null when it can</summary>
    /// <param name="loose">The part is off: on the shelf, or the engine on the stand; not on a car, nor on another part</param>
    public static string? WhyNot(RebuildJob job, PartInstance part, bool loose)
    {
        var wear = Unit.Clamp01(part.Wear, 1);
        var damaged = job.FixesDamage && Unit.Clamp01(part.Tear, 1) < PartInstance.Undamaged;
        if (wear >= job.Restores - Enough && !damaged) return PartWords.Say("rebuild.why.no_need");
        if (wear < job.MinWear) return PartWords.Say("rebuild.why.too_worn");
        if (job.Off && !loose) return PartWords.Say(job.IsExchange ? "rebuild.why.off.exchange" : "rebuild.why.off");
        if (job.Bare && part.Children.Count > 0) return PartWords.Say("rebuild.why.bare");
        return null;
    }

    /// <summary>Every job for the part, as it stands; none for a part the catalog does not know</summary>
    /// <param name="resaleFactor">The most any shop pays for a part, against its worth (<see cref="CostFor"/>)</param>
    public static IReadOnlyList<RebuildOffer> Offers(PartsCatalog catalog, PartInstance part, bool loose, double priceMultiplier = 1.0, double resaleFactor = 0)
    {
        if (catalog.Get(part.DefinitionId) is not { } definition) return [];

        return catalog.Rebuilds.For(definition, catalog.WorkOrder.KindOf(definition))
            .Select(job => new RebuildOffer(job, NameOf(job), CostFor(job, catalog, definition, part, priceMultiplier, resaleFactor), job.Minutes,
                WhyNot(job, part, loose), Math.Max(Unit.Clamp01(part.Wear, 1), job.Restores)))
            .ToList();
    }

    /// <summary>Does the job on the part: its mileage up to what the job leaves it in, its damage off for a rebuilt unit</summary>
    public static void Apply(RebuildJob job, PartInstance part)
    {
        part.Wear = Math.Max(Unit.Clamp01(part.Wear, 1), job.Restores);
        if (job.FixesDamage) part.Tear = 1;
    }
}
