using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Tests;

/// <summary>How far an engine revs: its valves float and the torque falls away, its weakest part sets where it hurts</summary>
public sealed class RevRangeTests
{
    /// <summary>A 5.6 litre V8 of figures, no ignition cut, valves floating at 5000 and a bottom end good for 6500</summary>
    private static DynoInputs Engine(double floatRpm = 5000, double maxRpm = 6500, double rpmLimit = 0)
    {
        const double bore = 0.1, stroke = 0.09;
        var swept = Math.PI / 4 * bore * bore * stroke;
        return PartFigures.DataEngineBase with
        {
            Cylinders = 8, Bore = bore, Stroke = stroke, ClearanceVolume = swept / 8.5, MaxFuelFlow = 0.03,
            RpmLimit = rpmLimit, MaxRpm = maxRpm, FloatRpm = floatRpm
        };
    }

    [Fact]
    public void Past_where_the_valves_float_the_torque_falls_away_and_the_curve_ends_a_fifth_past()
    {
        var dyno = EngineDyno.Run(Engine());

        Assert.Equal(6000, dyno.Curve[^1].Rpm);
        var atFloat = dyno.TorqueAt(5000);
        Assert.True(dyno.TorqueAt(5500) < atFloat * 0.5, "a tenth past the float the valves bounce");
        Assert.True(dyno.Curve[^1].Torque < atFloat * 0.05, "a fifth past there is all but nothing left");
    }

    [Fact]
    public void The_engine_hurts_itself_at_its_weakest_part_or_a_tenth_past_the_float_whichever_comes_first()
    {
        Assert.Equal(5500, EngineDyno.RedlineRpm(Engine()));
        Assert.Equal(5200, EngineDyno.RedlineRpm(Engine(maxRpm: 5200)));
        Assert.Equal(6500, EngineDyno.RedlineRpm(Engine(floatRpm: 0)));
        Assert.Equal(double.MaxValue, EngineDyno.RedlineRpm(Engine(floatRpm: 0, maxRpm: 0)));
    }

    [Fact]
    public void Power_is_quoted_short_of_hurting_the_engine_and_under_its_cut()
    {
        // Valves good for far more than the rods: the engine would still pull past where they give
        var weakRods = EngineDyno.Run(Engine(floatRpm: 8000, maxRpm: 4500));
        Assert.True(weakRods.MaxPowerRpm <= 4500, $"peak quoted at {weakRods.MaxPowerRpm}");

        var cut = EngineDyno.Run(Engine(floatRpm: 8000, maxRpm: 9000, rpmLimit: 4000));
        Assert.True(cut.MaxPowerRpm <= 4000, $"peak quoted at {cut.MaxPowerRpm}");
    }

    [Fact]
    public void Without_a_float_speed_the_curve_runs_to_the_weakest_part_or_a_quarter_past_the_cut()
    {
        Assert.Equal(6500, EngineDyno.TopRpm(Engine(floatRpm: 0)));
        Assert.Equal(5000, EngineDyno.TopRpm(Engine(floatRpm: 0, maxRpm: 0, rpmLimit: 4000)));
        Assert.Equal(6000, EngineDyno.TopRpm(Engine(rpmLimit: 4000)));
    }
}
