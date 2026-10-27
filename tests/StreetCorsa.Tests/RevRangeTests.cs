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
    public void Past_where_the_valves_float_the_torque_falls_away_and_without_a_cut_the_engine_stops_a_little_past_its_redline()
    {
        var engine = Engine();
        var dyno = EngineDyno.Run(engine);

        Assert.Equal(5500 * (1 + EngineDyno.PastRedline), EngineDyno.LimiterRpm(engine), 6);
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
    public void The_curve_runs_to_where_the_engine_stops_or_hurts_whichever_is_further()
    {
        Assert.Equal(6500 * (1 + EngineDyno.PastRedline), EngineDyno.TopRpm(Engine(floatRpm: 0)), 6);
        Assert.Equal(5000, EngineDyno.TopRpm(Engine(floatRpm: 0, maxRpm: 0, rpmLimit: 4000)));
        Assert.Equal(5500, EngineDyno.TopRpm(Engine(rpmLimit: 4000)));

        // Valves good for far more than a 6000 cut and a 7000 flywheel: the curve stops at the flywheel, not at the valves
        Assert.Equal(7000, EngineDyno.Run(Engine(floatRpm: 12500, maxRpm: 7000, rpmLimit: 6000)).Curve[^1].Rpm);
    }

    [Fact]
    public void A_speed_added_to_an_engine_without_one_goes_on_how_fast_it_turns()
    {
        static PartFigureSet Add(string name, double value) => new() { Numbers = [new PartFigure(name, FigureOp.Add, value)] };

        // No bottom-end limit: the 500 goes on the redline the valves give (5500), not on nothing
        var stronger = PartFigures.Apply(Engine(maxRpm: 0), [Add("max_rpm", 500)]);
        Assert.Equal(6000, stronger.MaxRpm);

        // No cut: the 300 goes on where the engine stops
        var cut = PartFigures.Apply(Engine(), [Add("rev_limit", 300)]);
        Assert.Equal(5500 * (1 + EngineDyno.PastRedline) + 300, cut.RpmLimit, 6);

        // Nothing says at all: on the default cut the engine is given
        var bare = PartFigures.Apply(Engine(floatRpm: 0, maxRpm: 0), [Add("rev_limit", 300)], defaultCut: 6000);
        Assert.Equal(6300, bare.RpmLimit);
    }

    [Fact]
    public void An_engine_that_revs_to_next_to_nothing_says_why_it_does_not_run()
    {
        Assert.NotNull(EngineDyno.Unrunnable(Engine(floatRpm: 0, maxRpm: 200)));
        Assert.Empty(EngineDyno.Run(Engine(floatRpm: 0, maxRpm: 200)).Curve);

        // A speed under the least a part may say is no speed: the engine stops at its cut
        Assert.Equal(double.MaxValue, EngineDyno.RedlineRpm(Engine(floatRpm: 50, maxRpm: 50)));
        Assert.Null(EngineDyno.Unrunnable(Engine(floatRpm: 50, maxRpm: 50, rpmLimit: 6000)));
    }
}
