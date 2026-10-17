using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// What parts cost. New price is what the part does (<see cref="PartPriceBook"/>), as the source game priced its parts,
/// and a whole engine what it makes (<see cref="EngineNewPrice"/>); a used part is worth what the source game gave for one: damage scales it, wear
/// takes up to 70% off.
/// </summary>
public static class PartPricing
{
    private const double WearFloor = 0.3;

    // More than anything in the game costs, well inside what a decimal holds
    private const double MaxPrice = 1e12;

    /// <summary>The figures every price is worked out with: <see cref="Scale"/>, <see cref="UsedShopFactor"/>, <see cref="TradeInFactor"/></summary>
    public readonly record struct Figures(double Scale, double UsedShopFactor, double TradeInFactor);

    /// <summary>
    /// Where the figures come from, asked each time a price is worked out. Process-wide on purpose: every price in the
    /// game is one of these, asked for from anywhere without a service at hand. The game points it at the rules of the
    /// game in play (<c>economy</c> in the rules), once, when the parts service is made; the tools that link this file
    /// (EngineBench) have no rules and leave it null: the scripts' own dollars, at 1, and the base game's shares. A
    /// figure that is not a positive number is the base game's.
    /// </summary>
    public static Func<Figures>? Source { get; set; }

    private static Figures Now => Source?.Invoke() ?? new Figures(1.0, 0.6, 0.4);

    /// <summary>
    /// The price book every new price is read from, asked each time: process-wide like <see cref="Source"/>. The game
    /// points it at the parts service's book (made from the catalog once, when first asked); a tool makes its own. Null,
    /// or a book being made: every part at its kind's base price and figure (<see cref="PartPriceBook.Empty"/>).
    /// </summary>
    public static Func<PartPriceBook?>? Book { get; set; }

    /// <summary>What a shop asks for a used part, against the same part new in the same shape</summary>
    public static double UsedShopFactor => Sane(Now.UsedShopFactor, 0.6);

    /// <summary>What a shop pays when it buys a part</summary>
    public static double TradeInFactor => Sane(Now.TradeInFactor, 0.4);

    /// <summary>The economy's factor on every part's price: part_groups.json's prices are the game's dollars at 1</summary>
    public static double Scale => Sane(Now.Scale, 1.0);

    private static double Sane(double value, double otherwise) => double.IsFinite(value) && value > 0 ? value : otherwise;

    public static double NewPrice(PartDefinition part) => (Book?.Invoke() ?? PartPriceBook.Empty).Price(part) * Scale;

    /// <summary>Worth of one part in its condition, nothing mounted on it counted; a wear or damage that is no number is as new</summary>
    public static double Worth(PartDefinition part, PartInstance instance) =>
        NewPrice(part) * Unit.Clamp01(instance.Tear, 1) * (WearFloor + (1 - WearFloor) * Unit.Clamp01(instance.Wear, 1));

    /// <summary>
    /// Worth of a part with everything that is mounted on it. A whole engine (a block with what is on it) that runs is
    /// worth what it makes (<see cref="EngineNewPrice"/>), shared over its parts by their own prices, each share in its
    /// part's wear and damage.
    /// </summary>
    public static double WorthOfAssembly(PartsCatalog catalog, PartInstance root)
    {
        var parts = root.SelfAndDescendants()
            .Select(p => (Instance: p, Definition: catalog.Get(p.DefinitionId)))
            .Where(p => p.Definition != null)
            .ToList();
        var worth = parts.Sum(p => Worth(p.Definition!, p.Instance));
        if (catalog.Get(root.DefinitionId) is not { } definition || PartKinds.GroupOf(definition) != PartGroups.Blocks) return worth;

        var loose = parts.Sum(p => NewPrice(p.Definition!));
        if (loose <= 0) return worth;

        var whole = EngineNewPrice(parts.Select(p => p.Definition!).ToList(), PowerAsNew(catalog, root));
        return worth * (whole / loose);
    }

    /// <summary>
    /// What an engine of <paramref name="parts"/> making <paramref name="powerHp"/> on the dyno costs new: its power on the
    /// blocks' curve (<see cref="PartPriceBook.PowerPrice"/>), wherever in the engine the power is, and every other part
    /// for being what it is; or its parts' own prices when they come to more, an engine being worth no less than what it
    /// is made of. No power (it does not run, or nobody knows): its parts.
    /// </summary>
    public static double EngineNewPrice(IReadOnlyCollection<PartDefinition> parts, double? powerHp)
    {
        var loose = parts.Sum(NewPrice);
        if (powerHp is not { } hp || !double.IsFinite(hp) || hp <= 0) return loose;

        var book = Book?.Invoke() ?? PartPriceBook.Empty;
        var whole = (PartPriceBook.PowerPrice(hp) + parts.Where(p => PartKinds.GroupOf(p) != PartGroups.Blocks).Sum(book.BasePrice)) * Scale;
        return double.IsFinite(whole) ? Math.Max(loose, whole) : loose;
    }

    // Engines measured, by catalog and make-up: a car's worth is asked for often, a dyno run takes milliseconds
    private static readonly ConditionalWeakTable<PartsCatalog, ConcurrentDictionary<string, double?>> Powers = new();
    private const int MaxKeptPowers = 4096;

    /// <summary>
    /// What <paramref name="engine"/> makes on the dyno with every part of it as new: what it is built to make, its
    /// wear aside (that is in its worth already); null when it does not run
    /// </summary>
    public static double? PowerAsNew(PartsCatalog catalog, PartInstance engine)
    {
        var kept = Powers.GetValue(catalog, _ => new ConcurrentDictionary<string, double?>(StringComparer.OrdinalIgnoreCase));
        var makeUp = MakeUp(engine);
        if (kept.TryGetValue(makeUp, out var power)) return power;

        var asNew = PartTrees.Clone(engine, keepIds: true);
        foreach (var part in asNew.SelfAndDescendants())
        {
            part.Wear = 1;
            part.Tear = 1;
        }

        try
        {
            power = EngineTuner.PowerOf(catalog, asNew);
        }
        catch (Exception)
        {
            // A part that breaks the dyno makes an engine that is worth its parts
            power = null;
        }

        if (kept.Count >= MaxKeptPowers) kept.Clear();
        kept[makeUp] = power;
        return power;
    }

    /// <summary>What an engine is made of, as text: every part, where it goes and how it is set, wear aside</summary>
    private static string MakeUp(PartInstance engine)
    {
        var text = new StringBuilder();
        Append(engine);
        return text.ToString();

        void Append(PartInstance part)
        {
            text.Append(part.DefinitionId).Append('@').Append(part.ParentSlot).Append(':').Append(part.OwnSlot);
            foreach (var (field, value) in (part.Tuning ?? []).OrderBy(t => t.Key, StringComparer.Ordinal))
                text.Append(';').Append(field).Append('=').Append(value.ToString("R", CultureInfo.InvariantCulture));

            text.Append('(');
            foreach (var child in part.Children.OrderBy(c => c.ParentSlot).ThenBy(c => c.DefinitionId, StringComparer.OrdinalIgnoreCase))
                Append(child);
            text.Append(')');
        }
    }

    /// <summary>What a shop pays for a part with everything that is mounted on it: the one trade-in rule for the player and the rivals</summary>
    public static decimal TradeIn(PartsCatalog catalog, PartInstance part) =>
        Round(WorthOfAssembly(catalog, part) * TradeInFactor);

    public static decimal Round(double price)
    {
        // A decimal holds up to 7.9e28 and nothing that is not a number: the cast would throw
        price = double.IsFinite(price) ? Math.Clamp(price, 0, MaxPrice) : 0;
        var step = Step((decimal)price);
        return Math.Max(1, Math.Round((decimal)price / step) * step);
    }

    /// <summary>The first price over this one that <see cref="Round"/> can give: a dollar over below $20, a five over from there</summary>
    public static decimal Above(decimal price)
    {
        if (price < 1) return 1;

        var step = Step(price);
        return Math.Floor(price / step) * step + step;
    }

    /// <summary>The first price under this one that <see cref="Round"/> can give; $1 at the least</summary>
    public static decimal Below(decimal price)
    {
        if (price <= 1) return 1;

        var step = Step(price - 1);
        return Math.Max(1, Math.Ceiling(price / step) * step - step);
    }

    /// <summary>How far apart the prices are around this one: whole dollars below $20, fives from there</summary>
    private static decimal Step(decimal price) => price < 20 ? 1 : 5;
}
