using System.Globalization;
using System.IO;
using AcTools.DataFile;
using AcTools.Render.Kn5SpecificForwardDark.Lights;
using StreetCorsa.Parts.Export;
using StreetCorsa.Services.Street;
using Sx = SlimDX;

namespace StreetCorsa.Controls.Street;

/// <summary>What the street's and the gas station's viewports share: the light of the day, and where the driver's eyes are</summary>
internal static class SceneLights
{
    /// <summary>Where the eyes are in a car that does not say: a little left of the middle, over the seat</summary>
    public static readonly Sx.Vector3 FallbackEye = new(0.36f, 1.05f, -0.15f);

    /// <summary>
    /// The renderer lit for a light of the day: the sun from <paramref name="sun"/>, the ambient, the lamps. Returns the
    /// lamps it hung, for a scene that is lit again later to take them down first.
    /// </summary>
    public static IReadOnlyList<DarkLightBase> Apply(GarageRenderer renderer, StreetLighting lighting, float[] sun)
    {
        renderer.Light = new Sx.Vector3(sun[0], sun[1], sun[2]);
        renderer.LightBrightness = lighting.Brightness;
        renderer.LightColor = Color(lighting.Color);
        renderer.AmbientBrightness = lighting.Ambient;
        renderer.AmbientUp = Color(lighting.AmbientUp);
        renderer.AmbientDown = Color(lighting.AmbientDown);
        renderer.CubemapAmbient = lighting.CubemapAmbient;

        var lamps = new List<DarkLightBase>();
        foreach (var lamp in lighting.Lamps)
        {
            var light = new DarkPointLight
            {
                Position = new Sx.Vector3(lamp.X, lamp.Y, lamp.Z),
                Color = Color(lamp.Color),
                Brightness = lamp.Brightness,
                Range = lamp.Range,
                UseShadows = false,
                IsVisibleInUi = false
            };
            renderer.AddLight(light);
            lamps.Add(light);
        }

        return lamps;
    }

    private static System.Drawing.Color Color(byte[] rgb) => System.Drawing.Color.FromArgb(rgb[0], rgb[1], rgb[2]);

    /// <summary>
    /// How a car sits and where its driver looks, from its own data, as AC has it (<see cref="AcCarFrame.Read"/>);
    /// a car that does not say sits with its model's origin on the road, the eyes where most cars have them
    /// </summary>
    public static AcCarFrame FrameOf(string carDirectory, Action<Exception>? failed = null) => FrameOf(DataOf(carDirectory, failed));

    /// <summary><see cref="FrameOf(string, Action{Exception}?)"/> from data already read (<see cref="DataOf"/>)</summary>
    public static AcCarFrame FrameOf(CarData? data) => data == null ? new AcCarFrame(FallbackEye, 0f) : AcCarFrame.FromData(data);

    /// <summary>
    /// A car's data files (data.acd decrypted when it has one), read once for all that wants them, the way the race
    /// export reads them (<see cref="AcCarDataReader"/>); null when it has none or they cannot be read
    /// (<paramref name="failed"/> hears why)
    /// </summary>
    public static CarData? DataOf(string carDirectory, Action<Exception>? failed = null)
    {
        try
        {
            return CarData.Open(carDirectory);
        }
        catch (Exception ex)
        {
            failed?.Invoke(ex);
            return null;
        }
    }

    // The game's own seat: the driver's eyes this far over the steering wheel's centre and behind it, looking this
    // many degrees down. The mods' DRIVEREYES seat some drivers well (a Riviera) and some with their nose on the wheel
    // (a '40 Ford, a Super Bee): the user picked one seat for every car (2026-09-27).
    public const float SeatAboveWheel = 0.32f;
    public const float SeatBehindWheel = 0.50f;
    public const float SeatPitch = -2f;

    /// <summary>
    /// Where the driver's eyes are, in the car's own space: <see cref="SeatAboveWheel"/> over its steering wheel
    /// (the STEER_HR node, or STEER_LR) and <see cref="SeatBehindWheel"/> behind it, level with its centre; the car's
    /// own DRIVEREYES (<paramref name="frame"/>) when it has no steering wheel. Call it with the car standing unrocked.
    /// </summary>
    public static Sx.Vector3 DriverEye(GarageRenderer renderer, AcCarFrame frame)
    {
        if (renderer.CarNode is not { } car || (car.GetDummyByName("STEER_HR") ?? car.GetDummyByName("STEER_LR")) is not { } wheel)
            return frame.Eye;

        var centre = Sx.Vector3.TransformCoordinate(Sx.Vector3.Zero, wheel.Matrix * Sx.Matrix.Invert(car.Matrix));
        return new Sx.Vector3(centre.X, centre.Y + SeatAboveWheel, centre.Z - SeatBehindWheel);
    }

    /// <summary>AC's own onboard field of view, degrees up and down, as the player set it (cfg\camera_onboard.ini); 56 when it does not say</summary>
    public static float OnboardFieldOfView()
    {
        try
        {
            var file = Path.Combine(Services.Configuration.IniModificationService.DefaultCfgDirectory, "camera_onboard.ini");
            var fov = File.Exists(file) ? new IniFile(file)["MODE"].GetFloat("FOV", 56f) : 56f;
            return fov is >= 30f and <= 120f ? fov : 56f;
        }
        catch (Exception)
        {
            return 56f;
        }
    }
}

/// <summary>
/// A car as AC puts it on the road, from its data files (data.acd too). AC's car frame has its origin at the centre of
/// gravity: each axle's wheel centres are <c>BASEY</c> (suspensions.ini) under it, the road a tyre's <c>RADIUS</c>
/// (tyres.ini) under those; the model is drawn moved from that frame by <c>[BASIC] GRAPHICS_OFFSET</c> (car.ini). The
/// driver's eyes, <c>[GRAPHICS] DRIVEREYES</c>, are in the model's own space, as AC and Content Manager's showroom
/// have them (they sit on the 3D driver's head bone; taking the offset out put a '49 Cadillac's eyes above its roof).
/// The game seats its drivers by the steering wheel instead (<see cref="SceneLights.DriverEye"/>): the eyes here are
/// for a car without one.
///
/// Where a model's own origin is differs from mod to mod (a '65 GTO's is at its axles, a '49 Cadillac's on the road): a
/// car placed by its model's origin sank into the road.
/// </summary>
/// <param name="EyeX">The car's own driver's eyes in the model's space (<see cref="Eye"/>)</param>
/// <param name="Lift">How far above the road the model's origin is</param>
internal sealed record AcCarFrame(float EyeX, float EyeY, float EyeZ, float Lift)
{
    public AcCarFrame(Sx.Vector3 eye, float lift) : this(eye.X, eye.Y, eye.Z, lift) { }

    public Sx.Vector3 Eye => new(EyeX, EyeY, EyeZ);

    /// <summary>The car's frame; null when it has no data</summary>
    public static AcCarFrame? Read(string carDirectory) => CarData.Open(carDirectory) is { } data ? FromData(data) : null;

    /// <summary>
    /// The car's frame from its data. A car that gives no driver's eyes has them where most cars do: how it sits does
    /// not hang on them
    /// </summary>
    public static AcCarFrame FromData(CarData data)
    {
        var car = data.Ini("car.ini");
        var given = CarData.Vector3(car, "GRAPHICS", "DRIVEREYES");
        var eyes = given is { Length: 3 } && given.Any(v => v != 0) && given.All(float.IsFinite)
            ? given
            : [SceneLights.FallbackEye.X, SceneLights.FallbackEye.Y, SceneLights.FallbackEye.Z];
        var offset = CarData.Vector3(car, "BASIC", "GRAPHICS_OFFSET") ?? [0f, 0f, 0f];

        // The centre of gravity's height over the road, the axles' mean; the model's origin is the offset from it
        var suspensions = data.Ini("suspensions.ini");
        var tyres = data.Ini("tyres.ini");
        float Height(string axle) => CarData.Float(tyres, axle, "RADIUS", 0f) - CarData.Float(suspensions, axle, "BASEY", 0f);
        var (front, rear) = (Height("FRONT"), Height("REAR"));
        var cog = front > 0 && rear > 0 ? (front + rear) / 2f : 0f;
        var lift = cog > 0 ? cog + offset[1] : 0f;

        return new AcCarFrame(eyes[0], eyes[1], eyes[2], float.IsFinite(lift) && Math.Abs(lift) < 2f ? lift : 0f);
    }
}

/// <summary>
/// A car's data files as the race export reads them (<see cref="AcCarDataReader"/>, <see cref="IniText"/>): data.acd
/// decoded as Latin-1, a missing file an empty ini. The street's and the gas station's viewports read how a car sits and
/// what its gauges are from these, the same files the car races on.
/// </summary>
internal sealed class CarData(Func<string, string?> read)
{
    private readonly Dictionary<string, IniText> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The car's data; null when it has neither a data folder nor data.acd. Throws when they cannot be read.</summary>
    public static CarData? Open(string carDirectory)
    {
        if (!Directory.Exists(carDirectory)) return null;
        if (!Directory.Exists(Path.Combine(carDirectory, AcCarData.DataFolder)) && !File.Exists(Path.Combine(carDirectory, AcdFile.FileName)))
            return null;

        return new CarData(AcCarDataReader.ForCar(carDirectory));
    }

    /// <summary>One of the car's ini files, read once; empty when the car has no such file</summary>
    public IniText Ini(string name)
    {
        if (!_files.TryGetValue(name, out var ini)) _files[name] = ini = new IniText(read(name));
        return ini;
    }

    /// <summary>A number from an ini file; <paramref name="fallback"/> when it is not there or not a number</summary>
    public static float Float(IniText ini, string section, string key, float fallback) =>
        ini.GetNumber(section, key) is { } value ? (float)value : fallback;

    /// <summary>
    /// Three numbers in a row, "x, y, z"; null when the key is not there or holds another count. A part that is not a
    /// number reads as 0, as AC's own tools have it.
    /// </summary>
    public static float[]? Vector3(IniText ini, string section, string key)
    {
        var parts = ini.Get(section, key)?.Split(',');
        if (parts is not { Length: 3 }) return null;

        return parts.Select(p => float.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f).ToArray();
    }

    /// <summary>A text value, null when it is not there or empty</summary>
    public static string? Text(IniText ini, string section, string key) => ini.Get(section, key) is { Length: > 0 } text ? text : null;
}
