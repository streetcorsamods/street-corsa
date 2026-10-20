using StreetCorsa.Audio;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.Parts;

namespace StreetCorsa.Tests;

/// <summary>
/// A car the game has no engine build for starts and sounds on the engine Assetto Corsa runs it on
/// (<see cref="EngineSpecs.FromAcData"/>): engine.ini's idle, limiter and inertia, the torque curve it names
/// </summary>
public sealed class AcEngineSpecTests
{
    private const string EngineIni = "[HEADER]\r\nVERSION=1\r\nPOWER_CURVE=power.lut\r\nCOAST_CURVE=FROM_COAST_REF\r\n\r\n[ENGINE_DATA]\r\nALTITUDE_SENSITIVITY=0.1\r\nINERTIA=0.150\r\nLIMITER=5500\r\nLIMITER_HZ=30\r\nMINIMUM=750\r\n";
    private const string PowerLut = "0|120.0\r\n1000|250.0\r\n3000|400.0\r\n5000|350.0\r\n";

    private static Func<string, string?> Data(string? engine = EngineIni, string? lut = PowerLut, string lutName = "power.lut") =>
        name => name == "engine.ini" ? engine : name == lutName ? lut : null;

    [Fact]
    public void The_idle_the_limiter_and_the_curve_are_the_cars_own()
    {
        var spec = EngineSpecs.FromAcData("Fury", Data(), null, EngineLayout.Front)!;

        Assert.Equal(750, spec.IdleRpm);
        Assert.Equal(5500, spec.LimiterRpm);
        Assert.Equal(400, spec.PeakTorque);
        Assert.Equal(250, spec.TorqueAt(1000));
        Assert.Equal(325, spec.TorqueAt(2000));
        Assert.Equal(350, spec.TorqueAt(9000));
        Assert.Equal(120, spec.TorqueAt(0));
        Assert.True(spec.Runs);
        Assert.Null(spec.Sound);
        Assert.Equal(0, spec.Cylinders);
        Assert.Equal("Fury", spec.Name);
    }

    [Fact]
    public void The_inertia_comes_back_from_kg_m2_to_the_parts_unit_the_sim_scales()
    {
        var spec = EngineSpecs.FromAcData("Fury", Data(), null, EngineLayout.Front)!;

        Assert.Equal(0.150 / PartFigures.KgM2PerInertiaUnit, spec.Inertia, 6);
        var sim = new EngineSim(spec);
        Assert.Equal(750, sim.Idle);
        Assert.Equal(5500, sim.Limiter);
        Assert.Equal(400, sim.PeakTorque);
    }

    [Fact]
    public void The_curve_file_engine_ini_names_is_the_one_read()
    {
        var ini = EngineIni.Replace("POWER_CURVE=power.lut", "POWER_CURVE=torque.lut");
        var spec = EngineSpecs.FromAcData("Fury", Data(ini, PowerLut, "torque.lut"), null, EngineLayout.Rear)!;

        Assert.Equal(400, spec.PeakTorque);
        Assert.Equal(EngineLayout.Rear, spec.Layout);
    }

    [Fact]
    public void A_curve_file_named_out_of_the_folder_is_not_followed()
    {
        var ini = EngineIni.Replace("POWER_CURVE=power.lut", "POWER_CURVE=..\\..\\power.lut");
        var spec = EngineSpecs.FromAcData("Fury", Data(ini, PowerLut, "..\\..\\power.lut"), null, EngineLayout.Front)!;

        Assert.Equal(0, spec.PeakTorque);
    }

    [Fact]
    public void Without_an_engine_ini_there_is_no_engine()
    {
        Assert.Null(EngineSpecs.FromAcData("Fury", Data(engine: null), null, EngineLayout.Front));
    }

    [Fact]
    public void Figures_missing_or_spoilt_are_left_to_the_sim()
    {
        var spec = EngineSpecs.FromAcData("Fury", Data("[ENGINE_DATA]\r\nINERTIA=abc\r\n", "junk\r\n|\r\n3000|x\r\n"), null, EngineLayout.Front)!;

        Assert.Equal(0, spec.IdleRpm);
        Assert.Equal(0, spec.LimiterRpm);
        Assert.Equal(0, spec.Inertia);
        Assert.Equal(0, spec.PeakTorque);
        Assert.Equal(0, spec.TorqueAt(3000));
        var sim = new EngineSim(spec);
        Assert.Equal(800, sim.Idle);
        Assert.Equal(6000, sim.Limiter);
    }
}
