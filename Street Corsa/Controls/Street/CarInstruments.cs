using System.Globalization;
using AcTools.DataFile;
using AcTools.Render.Kn5Specific.Objects;
using Sx = SlimDX;

namespace StreetCorsa.Controls.Street;

/// <summary>
/// A car's dashboard needles and steering wheel, moving as in AC: the needles of its data/analog_instruments.ini (each
/// section a node, <c>OBJECT_NAME</c>, turned about its own z by <c>ZERO + value × STEP</c> degrees, the value no lower
/// than <c>MIN_VALUE</c>, or read off its <c>LUT</c> when it has one) and the steering wheel (the <c>STEER_HR</c> and
/// <c>STEER_LR</c> nodes, turned by the front wheels' angle times car.ini's <c>STEER_RATIO</c>, up to its
/// <c>STEER_LOCK</c>). The speed is km/h, the fuel litres, the temperatures degrees.
///
/// The nodes' own z points forward, away from the driver (in every model looked at): a positive turn about it is
/// clockwise as the driver sees it, the way a dial reads, and a turn of the wheel to the right.
/// </summary>
internal sealed class CarInstruments
{
    private sealed record Needle(Kn5RenderableList Node, Sx.Matrix Original, string Kind, float Zero, float Step, float Min, (float Value, float Angle)[]? Lut)
    {
        public float Angle(float value)
        {
            value = Math.Max(value, Min);
            if (Lut is not { Length: > 0 } lut) return Zero + value * Step;
            if (value <= lut[0].Value) return lut[0].Angle;
            for (var i = 1; i < lut.Length; i++)
            {
                if (value > lut[i].Value) continue;
                var f = (value - lut[i - 1].Value) / Math.Max(1e-6f, lut[i].Value - lut[i - 1].Value);
                return lut[i - 1].Angle + (lut[i].Angle - lut[i - 1].Angle) * f;
            }

            return lut[^1].Angle;
        }
    }

    private readonly List<Needle> _needles = [];
    private readonly List<(Kn5RenderableList Node, Sx.Matrix Original)> _wheels = [];
    private readonly float _steerRatio;
    private readonly float _steerLock;
    private readonly float _wheelbase;

    private CarInstruments(float steerRatio, float steerLock, float wheelbase)
    {
        _steerRatio = steerRatio;
        _steerLock = steerLock;
        _wheelbase = wheelbase;
    }

    /// <summary>The car's needles and steering wheel; null when there is no car or its data cannot be read (logged)</summary>
    /// <param name="data">The car's data, read once by the caller (<see cref="SceneLights.DataOf"/>); null when it has none</param>
    public static CarInstruments? TryRead(Kn5RenderableCar? car, DataWrapper? data, string carDirectory, Logging.IAppLogger logger)
    {
        try
        {
            return car == null || data == null ? null : Read(car, data);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "The gauges of {Car} could not be read: they stay still", carDirectory);
            return null;
        }
    }

    /// <summary>The car's needles and steering wheel</summary>
    public static CarInstruments Read(Kn5RenderableCar car, DataWrapper data)
    {
        var controls = data.GetIniFile("car.ini")["CONTROLS"];
        var ratio = controls.GetFloat("STEER_RATIO", 16f);
        var steerLock = controls.GetFloat("STEER_LOCK", 450f);
        var wheelbase = data.GetIniFile("suspensions.ini")["BASIC"].GetFloat("WHEELBASE", 2.8f);
        var instruments = new CarInstruments(ratio is > 1 and < 50 ? ratio : 16f, steerLock is > 30 and < 1200 ? steerLock : 450f,
            wheelbase is > 1.5f and < 5f ? wheelbase : 2.8f);

        foreach (var name in new[] { "STEER_HR", "STEER_LR" })
        {
            if (car.GetDummyByName(name) is { } wheel) instruments._wheels.Add((wheel, wheel.LocalMatrix));
        }

        var ini = data.GetIniFile("analog_instruments.ini");
        foreach (var (section, kind) in new[]
                 {
                     ("SPEED_INDICATOR", "speed"), ("RPM_INDICATOR", "rpm"), ("FUEL_INDICATOR", "fuel"), ("WATER_TEMP", "water"),
                     ("OIL_TEMP", "oil")
                 })
        {
            var entry = ini[section];
            var nodeName = entry.GetNonEmpty("OBJECT_NAME");
            if (nodeName == null || car.GetDummyByName(nodeName) is not { } node) continue;
            instruments._needles.Add(new Needle(node, node.LocalMatrix, kind, entry.GetFloat("ZERO", 0f), entry.GetFloat("STEP", 0f),
                entry.GetFloat("MIN_VALUE", float.MinValue), Lut(entry.GetNonEmpty("LUT"))));
        }

        return instruments;
    }

    /// <summary>"(40=0|100=30|120=60)": values and their angles, in order</summary>
    private static (float, float)[]? Lut(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var points = new List<(float, float)>();
        foreach (var pair in text.Trim('(', ')', ' ').Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=');
            if (parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                                  && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var angle))
                points.Add((value, angle));
        }

        return points.Count == 0 ? null : points.OrderBy(p => p.Item1).ToArray();
    }

    /// <summary>
    /// The needles for the engine's revs, the speed, the fuel and the water's temperature; the steering wheel for a bend
    /// of this curvature (per metre, to the left positive): the front wheels turned as a car of its wheelbase needs.
    /// A fuel level not known leaves the fuel needle as it is modelled.
    /// </summary>
    public void Show(double rpm, float speedKmh, float? fuelLitres, float waterC, float curvature)
    {
        var frontWheelDegrees = MathF.Atan(_wheelbase * curvature) * 180f / MathF.PI;
        foreach (var needle in _needles)
        {
            if (needle.Kind == "fuel" && fuelLitres == null) continue;
            var value = needle.Kind switch
            {
                "speed" => speedKmh,
                "rpm" => (float)rpm,
                "fuel" => fuelLitres!.Value,
                "water" => waterC,
                _ => waterC + 10f
            };
            needle.Node.LocalMatrix = Sx.Matrix.RotationZ(needle.Angle(value) * MathF.PI / 180f) * needle.Original;
        }

        var steer = Math.Clamp(frontWheelDegrees * _steerRatio, -_steerLock, _steerLock);
        foreach (var (node, original) in _wheels) node.LocalMatrix = Sx.Matrix.RotationZ(-steer * MathF.PI / 180f) * original;
    }
}
