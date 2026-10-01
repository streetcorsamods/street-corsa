using StreetCorsa.Helpers;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Parts.Cars;

/// <summary>
/// What parts cost. New price is the part script's own value; a used part is worth what the source game
/// gave for one: damage scales it, wear takes up to 70% off.
/// </summary>
public static class PartPricing
{
    /// <summary>Parts without a script (batteries, plates) carry no value of their own</summary>
    private const double ValueWithoutScript = 35.0;

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

    /// <summary>What a shop asks for a used part, against the same part new in the same shape</summary>
    public static double UsedShopFactor => Sane(Now.UsedShopFactor, 0.6);

    /// <summary>What a shop pays when it buys a part</summary>
    public static double TradeInFactor => Sane(Now.TradeInFactor, 0.4);

    /// <summary>
    /// From the money of the part scripts to the money of the game. The scripts price parts in the dollars of
    /// the source game's day; the game's cars cost what cars cost in its own day.
    /// </summary>
    public static double Scale => Sane(Now.Scale, 1.0);

    private static double Sane(double value, double otherwise) => double.IsFinite(value) && value > 0 ? value : otherwise;

    public static double NewPrice(PartDefinition part)
    {
        // pack.json may hold "NaN", "Infinity" or 1e400 (which Newtonsoft reads as infinity): no price is the default one
        var value = part.Number("value");
        return (value > 0 && double.IsFinite(value) ? value : ValueWithoutScript) * Scale;
    }

    /// <summary>Worth of one part in its condition, nothing mounted on it counted; a wear or damage that is no number is as new</summary>
    public static double Worth(PartDefinition part, PartInstance instance) =>
        NewPrice(part) * Unit.Clamp01(instance.Tear, 1) * (WearFloor + (1 - WearFloor) * Unit.Clamp01(instance.Wear, 1));

    /// <summary>Worth of a part with everything that is mounted on it</summary>
    public static double WorthOfAssembly(PartsCatalog catalog, PartInstance root) =>
        root.SelfAndDescendants().Sum(p => catalog.Get(p.DefinitionId) is { } definition ? Worth(definition, p) : 0);

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

    /// <summary>How far apart the prices are around this one: whole dollars below $20, fives from there</summary>
    private static decimal Step(decimal price) => price < 20 ? 1 : 5;
}
