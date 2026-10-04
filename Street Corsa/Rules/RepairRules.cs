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

    /// <summary>A new windscreen, fitted</summary>
    public decimal WindscreenCost { get; set; } = 60m;

    /// <summary>Less damage than this on a body is a small job</summary>
    public double SmallBodyJobKmh { get; set; } = 40;

    /// <summary>What the can holds, litres: about a gallon</summary>
    public double CanOfGasLitres { get; set; } = 4;
}
