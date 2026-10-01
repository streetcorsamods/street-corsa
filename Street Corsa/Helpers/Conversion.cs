namespace StreetCorsa.Helpers;

/// <summary>
/// The exact factors between the US units and metric, for every place that converts: <see cref="Measure"/> for what
/// the player reads, the AC data's weights, the gas station's quart of oil.
///
/// Plain BCL on purpose, like <see cref="Unit"/>: the tools link it.
/// </summary>
public static class Conversion
{
    public const double KmPerMile = 1.609344;
    public const double LitresPerGallon = 3.785411784;
    public const double LitresPerQuart = 0.946352946;
    public const double KgPerLb = 0.45359237;
    public const double NmPerLbFt = 1.3558179483;
    public const double TonnesPerShortTon = 0.90718474;
}
