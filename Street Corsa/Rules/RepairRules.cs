namespace StreetCorsa.Rules;

/// <summary>The garage's repairs (<c>"repairs"</c> in the rules): what parts and labour cost to put a car right</summary>
public sealed class RepairRules
{
    /// <summary>Share of a part's new price a repair of a fully wrecked part costs</summary>
    public double PartShare { get; set; } = 0.6;

    /// <summary>The labour on each repair, on top of the parts</summary>
    public decimal Labour { get; set; } = 15m;

    /// <summary>Body work, per km/h of collision the body took</summary>
    public decimal BodyCostPerKmh { get; set; } = 8m;

    /// <summary>
    /// Cutting the rust out, welding in new metal and a respray, for a body all rust; a respray alone, for paint worn
    /// all over. A body shop does both in one job, for its share of each.
    /// </summary>
    public decimal RustCost { get; set; } = 900m;

    public decimal PaintCost { get; set; } = 300m;

    /// <summary>
    /// A paint job in the paint booth: the car sprayed another of its make's colours (its mod's skins), door shuts and
    /// all, the old paint's wear gone with it; and on top of it, a colour mixed to the player's own order
    /// </summary>
    public decimal ColourChangeCost { get; set; } = 350m;

    public decimal CustomColourCost { get; set; } = 75m;

    /// <summary>How long the painter takes going round the car, seconds of the player's time (the game's clock is the job's own, a paint job's time)</summary>
    public double SpraySeconds { get; set; } = 18;

    /// <summary>A new windscreen, fitted</summary>
    public decimal WindscreenCost { get; set; } = 60m;

    /// <summary>Less rust than this (0..1) is not worth a body shop's while; less worn paint than this is not worth a respray</summary>
    public double SomeRust { get; set; } = 0.02;

    public double SomePaintWear { get; set; } = 0.05;

    /// <summary>Rust from this on is a big job (a day's work, not a morning's)</summary>
    public double BigRustJob { get; set; } = 0.25;

    /// <summary>Less damage than this on a body is a small job</summary>
    public double SmallBodyJobKmh { get; set; } = 40;

    /// <summary>What the can holds, litres: about a gallon</summary>
    public double CanOfGasLitres { get; set; } = 4;
}
