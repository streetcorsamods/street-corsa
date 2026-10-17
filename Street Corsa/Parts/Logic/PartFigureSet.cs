namespace StreetCorsa.Parts.Logic;

/// <summary>What a part's figure does to the engine's: puts its own number, adds to it, or multiplies it</summary>
public enum FigureOp
{
    Set,
    Add,
    Times
}

/// <summary>One figure of a part, by its name in <see cref="PartFigures"/>, in the figure's own (metric) unit</summary>
public sealed record PartFigure(string Name, FigureOp Op, double Value);

/// <summary>
/// A charger a part brings, from its figures: boost in bar over the atmosphere, building from where it starts to
/// spool to where it is all there, then held. A turbo's boost comes with a lag (Assetto Corsa's per-frame figures,
/// closer to 1 slower); a blower's comes with the throttle.
/// </summary>
public sealed record DynoCharger(bool Turbo, double Boost, double SpoolRpm, double FullBoostRpm, double LagUp, double LagDown)
{
    /// <summary>Boost at this engine speed with the throttle open, bar</summary>
    public double BoostAt(double rpm)
    {
        if (rpm <= SpoolRpm) return 0;
        if (rpm >= FullBoostRpm) return Boost;

        var x = (rpm - SpoolRpm) / (FullBoostRpm - SpoolRpm);
        return Boost * x * x * (3 - 2 * x);
    }
}

/// <summary>A part's figures as the catalog read them: checked, each with what it does, the gears and chargers apart</summary>
public sealed class PartFigureSet
{
    public IReadOnlyList<PartFigure> Numbers { get; init; } = Array.Empty<PartFigure>();

    /// <summary>Forward gear ratios, first gear first; null when the part gives none</summary>
    public IReadOnlyList<double>? Gears { get; init; }

    public IReadOnlyList<DynoCharger> Chargers { get; init; } = Array.Empty<DynoCharger>();

    /// <summary>
    /// One of <paramref name="count"/> parts' share of the figures: a multiplier to the root of the count, an addition
    /// divided by it, so the shares of every one together are the figures once; a part's own numbers, gears and
    /// chargers as they are
    /// </summary>
    public PartFigureSet SharedBy(int count)
    {
        if (count <= 1) return this;

        var numbers = Numbers.Select(f => f.Op switch
        {
            FigureOp.Times => f with { Value = Math.Pow(f.Value, 1.0 / count) },
            FigureOp.Add => f with { Value = f.Value / count },
            _ => f
        }).ToList();
        return new PartFigureSet { Numbers = numbers, Gears = Gears, Chargers = Chargers };
    }
}
