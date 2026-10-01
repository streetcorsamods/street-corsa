namespace StreetCorsa.Helpers;

/// <summary>What a figure measures; each is kept in metric inside the game</summary>
public enum Quantity
{
    /// <summary>km/h</summary>
    Speed,

    /// <summary>A road's length, an odometer: km</summary>
    Distance,

    /// <summary>Litres of fuel</summary>
    Fuel,

    /// <summary>Litres of oil (quarts in imperial, not gallons)</summary>
    Oil,

    /// <summary>°C</summary>
    Temperature,

    /// <summary>kg</summary>
    Mass,

    /// <summary>N·m</summary>
    Torque,

    /// <summary>hp, in either system</summary>
    Power,

    /// <summary>hp per tonne (per short ton in imperial)</summary>
    PowerToWeight
}

/// <summary>
/// A figure in the units the player reads (<see cref="Locale.Units"/>): "104 mph", "167 km/h". Kept in metric, written
/// in either system. Handed to the text as a value it writes itself, unit and all: <c>{speed}</c>, or with a number
/// format, <c>{speed:0.00}</c>.
/// </summary>
public readonly record struct Measure(Quantity Quantity, double Metric) : IFormattable
{
    private const double KmPerMile = 1.609344;
    private const double LitresPerGallon = 3.785411784;
    private const double LitresPerQuart = 0.946352946;
    private const double KgPerLb = 0.45359237;
    private const double NmPerLbFt = 1.3558179483;
    private const double TonnesPerShortTon = 0.90718474;

    public static Measure Speed(double kmh) => new(Quantity.Speed, kmh);
    public static Measure SpeedFromMph(double mph) => new(Quantity.Speed, mph * KmPerMile);
    public static Measure Distance(double km) => new(Quantity.Distance, km);
    public static Measure Fuel(double litres) => new(Quantity.Fuel, litres);
    public static Measure Oil(double litres) => new(Quantity.Oil, litres);
    public static Measure Temperature(double celsius) => new(Quantity.Temperature, celsius);
    public static Measure Mass(double kg) => new(Quantity.Mass, kg);
    public static Measure Torque(double nm) => new(Quantity.Torque, nm);
    public static Measure Power(double hp) => new(Quantity.Power, hp);
    public static Measure PowerToWeight(double hpPerTonne) => new(Quantity.PowerToWeight, hpPerTonne);

    /// <summary>The figure in the player's units</summary>
    public double Value => In(Locale.Units);

    /// <summary>The unit's id in the player's units ("mph"), the key of its words: <c>unit.mph</c></summary>
    public string Unit => UnitOf(Quantity, Locale.Units);

    /// <summary>The figure in the units of <paramref name="system"/></summary>
    public double In(UnitSystem system)
    {
        if (system == UnitSystem.Metric) return Metric;
        return Quantity switch
        {
            Quantity.Speed or Quantity.Distance => Metric / KmPerMile,
            Quantity.Fuel => Metric / LitresPerGallon,
            Quantity.Oil => Metric / LitresPerQuart,
            Quantity.Temperature => Metric * 9 / 5 + 32,
            Quantity.Mass => Metric / KgPerLb,
            Quantity.Torque => Metric / NmPerLbFt,
            Quantity.PowerToWeight => Metric * TonnesPerShortTon,
            _ => Metric
        };
    }

    /// <summary>The id of the unit a quantity is read in: "mph", "kmh", "qt", "l"</summary>
    public static string UnitOf(Quantity quantity, UnitSystem system) =>
        (quantity, system) switch
        {
            (Quantity.Speed, UnitSystem.Imperial) => "mph",
            (Quantity.Speed, _) => "kmh",
            (Quantity.Distance, UnitSystem.Imperial) => "mi",
            (Quantity.Distance, _) => "km",
            (Quantity.Fuel, UnitSystem.Imperial) => "gal",
            (Quantity.Oil, UnitSystem.Imperial) => "qt",
            (Quantity.Fuel or Quantity.Oil, _) => "l",
            (Quantity.Temperature, UnitSystem.Imperial) => "f",
            (Quantity.Temperature, _) => "c",
            (Quantity.Mass, UnitSystem.Imperial) => "lb",
            (Quantity.Mass, _) => "kg",
            (Quantity.Torque, UnitSystem.Imperial) => "lbft",
            (Quantity.Torque, _) => "nm",
            (Quantity.PowerToWeight, UnitSystem.Imperial) => "hp_ton",
            (Quantity.PowerToWeight, _) => "hp_t",
            _ => "hp"
        };

    /// <summary>The name of the unit a quantity is read in, for a column's heading: "mph"</summary>
    public static string NameOf(Quantity quantity) => Locale.UnitName(UnitOf(quantity, Locale.Units));

    /// <summary>The number alone, in the player's units and culture</summary>
    public string Number(string? format = null) =>
        Value.ToString(string.IsNullOrWhiteSpace(format) ? DefaultFormat : format, Locale.Culture);

    // Whole units, but a tank's gallons and the oil's quarts are small numbers
    private string DefaultFormat => Quantity is Quantity.Fuel or Quantity.Oil && Locale.Units == UnitSystem.Imperial ? "0.#"
        : Quantity == Quantity.Distance ? "N0" : "0";

    public override string ToString() => Locale.UnitWords(Unit, Number());

    public string ToString(string? format, IFormatProvider? formatProvider) => Locale.UnitWords(Unit, Number(format));
}
