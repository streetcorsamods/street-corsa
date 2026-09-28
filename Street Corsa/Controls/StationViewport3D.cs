using System.IO;
using System.Windows;
using System.Windows.Input;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Audio;
using StreetCorsa.Controls.Street;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;
using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// The gas station, seen from the driver's seat of the player's car: it comes up the road, turns in and stops at the
/// pump, and pulls out onto the road again, as the screen's <see cref="StationStage"/> says. The car sits on the
/// scene's ground (<see cref="StationGround"/>), its wheels turn, its body dives on the brakes, its engine follows the
/// revs, and the view goes with it: ahead while it drives, at the pump while it stands. Dragging with the mouse looks
/// round; a double click looks back where the driver would.
/// </summary>
public class StationViewport3D : D3DViewportBase
{
    private const float MaxLookYaw = 160f;
    private const float MaxLookPitch = 20f;
    private const float LookDegreesPerPixel = 0.18f;
    // AC's own onboard lens, as the player set it
    private float _fieldOfView = 56f;

    // The driver's head turns towards the pump over about a second, and looks a little into a bend while driving
    private const float LookSeconds = 0.9f;
    private const float LookIntoBend = 0.35f;

    // Where the wheels touch, from the car's middle: the ground under them tilts the car
    private const float HalfWheelbase = 1.4f;
    private const float HalfTrack = 0.75f;

    // A car driving off is gone for the screen this long before its drive ends: the curtain comes down on it moving
    private const float GoneBeforeTheEnd = 1.2f;

    private const int SettleFrames = 4;
    private static readonly TimeSpan SceneFadeOut = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan SceneFadeIn = TimeSpan.FromMilliseconds(900);

    // What stands in the scene
    private string? _kn5;
    private StreetLight? _light;
    private StreetCar? _player;
    private StationScene? _station;
    private StationGround _ground = StationGround.Flat;
    private StationStage? _stageListened;
    private EngineRunner? _engineListened;

    // The car on its way
    private PathDrive? _drive;

    // Where the arrival stops, worked out once a scene
    private PathState? _pumpPose;
    private float _driveTime;
    private float _wheelAngle;
    private float _lastDistance;
    private Sx.Matrix _placement = Sx.Matrix.Identity;
    private readonly ChassisPitch _pitch = new();
    private CarBodyRock? _rock;

    // The dashboard's needles and the steering wheel; what they show
    private CarInstruments? _instruments;
    private float _speed;
    private float _steer;

    // The wheel turns into a bend this long before the car gets there, and follows over about this long
    private const float SteerAhead = 0.35f;
    private const float SteerSeconds = 0.25f;

    // Warm, as a car that drove over is
    private const float WaterC = 85f;

    // The driver's eyes in their car's own space, and where they look: degrees from straight ahead, to the left positive
    private Sx.Vector3 _eye = SceneLights.FallbackEye;
    private float _lookYaw;
    private float _lookPitch;

    // How the car sits and where its driver looks, from its data (AcCarFrame)
    private AcCarFrame _frame = new(SceneLights.FallbackEye, 0f);
    private bool _lookHeld;
    private System.Windows.Point? _dragFrom;
    private float _bend;

    private int _fadeInAfter = -1;

    public StationViewport3D()
        : base("Station", new LabelLook(13, 0xD8, 0x90, new Thickness(8, 4, 8, 4), new Vector(16, 16)))
    {
        Cursor = System.Windows.Input.Cursors.Hand;

        // A viewport that cannot show the station says so at once: the screen does not wait on it
        Failed += (_, _) =>
        {
            if (_stageListened != null) _stageListened.CannotShow = true;
        };
    }

    protected override string ViewportName => "Station viewport";

    #region Dependency properties

    public static readonly DependencyProperty StationProperty = DependencyProperty.Register(
        nameof(Station), typeof(StationScene), typeof(StationViewport3D),
        new PropertyMetadata(null, (d, _) => ((StationViewport3D)d).RequestLoad()));

    public StationScene? Station
    {
        get => (StationScene?)GetValue(StationProperty);
        set => SetValue(StationProperty, value);
    }

    public static readonly DependencyProperty LightProperty = DependencyProperty.Register(
        nameof(Light), typeof(StreetLight), typeof(StationViewport3D),
        new PropertyMetadata(StreetLight.Day, (d, _) => ((StationViewport3D)d).RequestLoad()));

    /// <summary>Day, dusk or night: another sky, and another light</summary>
    public StreetLight Light
    {
        get => (StreetLight)GetValue(LightProperty);
        set => SetValue(LightProperty, value);
    }

    public static readonly DependencyProperty PlayerCarProperty = DependencyProperty.Register(
        nameof(PlayerCar), typeof(StreetCar), typeof(StationViewport3D),
        new PropertyMetadata(null, (d, _) => ((StationViewport3D)d).RequestLoad()));

    public StreetCar? PlayerCar
    {
        get => (StreetCar?)GetValue(PlayerCarProperty);
        set => SetValue(PlayerCarProperty, value);
    }

    public static readonly DependencyProperty StageProperty = DependencyProperty.Register(
        nameof(Stage), typeof(StationStage), typeof(StationViewport3D),
        new PropertyMetadata(null, (d, _) => ((StationViewport3D)d).OnStageChanged()));

    public StationStage? Stage
    {
        get => (StationStage?)GetValue(StageProperty);
        set => SetValue(StageProperty, value);
    }

    #endregion

    #region Loading

    private string? WantedKn5 => Station is { HasModel: true } station ? station.Kn5 : null;

    protected override bool IsUpToDate => WantedKn5 == null || PlayerCar == null
        ? Renderer == null
        : Renderer != null && _kn5 == WantedKn5 && _player == PlayerCar && _light == Light;

    protected override string LoadDescription => $"{PlayerCar?.Directory} at {WantedKn5}";

    protected override async Task LoadAsync()
    {
        var station = Station;
        var player = PlayerCar;
        var kn5 = WantedKn5;
        if (station == null || player == null || kn5 == null)
        {
            DisposeRenderer(releaseDevice: false);
            return;
        }

        if (Renderer != null && !IsPictureHidden)
        {
            await FadeOutAsync(SceneFadeOut);
            if (!IsLoaded) return;
        }

        DisposeRenderer(releaseDevice: false);

        var light = Light;
        var renderer = NewRenderer(null, kn5);
        renderer.AutoAdjustTarget = false;
        renderer.LoadCarLights = light == StreetLight.Night;
        renderer.TryToGuessCarLights = light == StreetLight.Night;

        var ground = await Task.Run(() => StationGround.Load(station.GroundFile));
        if (!await StartRendererAsync(renderer)) return;

        var lighting = station.LightingFor(light);
        SceneLights.Apply(renderer, lighting, station.Sun);
        ShowSky(renderer, light);

        // What the reflections see past the sky's reach (a chrome mirror): the sky's colour, not the renderer's white
        renderer.BackgroundColor = System.Drawing.Color.FromArgb(lighting.AmbientUp[0], lighting.AmbientUp[1], lighting.AmbientUp[2]);

        var skin = SkinOrDefault(player.Directory, player.Skin);
        await TrackDeviceWork(renderer.MainSlot.SetCarAsync(CarDescription.FromDirectory(player.Directory), skin));
        if (Renderer != renderer || !IsLoaded) return;

        _kn5 = kn5;
        _light = light;
        _player = player;
        _station = station;
        _ground = ground;

        renderer.MainSlot.ResetCarBoundingBox();
        var data = SceneLights.DataOf(player.Directory, ex => Logger.Warning(ex, "The data of {Car} could not be read: it sits and looks as most cars do", player.Directory));
        _frame = SceneLights.FrameOf(data);
        _eye = SceneLights.DriverEye(renderer, _frame);
        _lookPitch = SceneLights.SeatPitch;
        _fieldOfView = SceneLights.OnboardFieldOfView();
        if (renderer.CarNode is { } car && light == StreetLight.Night) car.HeadlightsEnabled = true;
        _instruments = CarInstruments.TryRead(renderer.CarNode, data, player.Directory, Logger);

        renderer.UseFpsCamera = true;
        StartFromStage();
        Logger.Information("Station {Station} up at {Light} with {Car}", station.Id, light, Path.GetFileName(player.Directory));

        if (Stage is { } stage) stage.IsShown = true;
        _fadeInAfter = SettleFrames;
        AnimateFor(TimeSpan.FromSeconds(1));
        Invalidate();
    }

    /// <summary>The sky of the light of the day; the others hidden</summary>
    private static void ShowSky(GarageRenderer renderer, StreetLight light)
    {
        if (renderer.ShowroomNode is not { } scene) return;

        var wanted = StationScene.SkyNode(light);
        foreach (var mesh in scene.Meshes)
        {
            var name = mesh.OriginalNode.Name;
            if (name.StartsWith("STATION_SKY_", StringComparison.Ordinal)) mesh.IsEnabled = name == wanted;
        }
    }

    /// <summary>The needles and the steering wheel as the car is now</summary>
    private void ShowInstruments()
    {
        _instruments?.Show(_engineListened?.Rpm ?? 0, _speed * 3.6f, _stageListened?.FuelLitres ?? 0f, WaterC, _steer);
    }

    protected override void OnRendererDisposed()
    {
        _instruments = null;
        if (_stageListened != null) _stageListened.IsShown = false;
        _kn5 = null;
        _light = null;
        _player = null;
        _station = null;
        _rock = null;
        _pumpPose = null;
        _fadeInAfter = -1;

        // Whatever the car was doing, it is done at once: nobody sees it
        var drive = _drive;
        _drive = null;
        _engineListened?.Drive(null);
        if (drive != null && _stageListened is { } stage)
        {
            if (drive.IsLeaving) stage.ReportGone();
            else stage.ReportAtPump();
        }
    }

    #endregion

    #region The car

    private void OnStageChanged()
    {
        if (_stageListened != null)
        {
            _stageListened.Changed -= OnStageAction;
            _stageListened.FuelChanged -= OnFuelChanged;
            _stageListened.IsShown = false;
            _stageListened.CannotShow = false;
        }

        _stageListened = Stage;
        if (_stageListened != null)
        {
            _stageListened.Changed += OnStageAction;
            _stageListened.FuelChanged += OnFuelChanged;
            _stageListened.IsShown = Renderer != null && _kn5 != null;
            _stageListened.CannotShow = HasFailed;
        }
    }

    /// <summary>The pump runs: the fuel needle climbs, whether the render loop is going or not</summary>
    private void OnFuelChanged()
    {
        if (Renderer == null) return;
        ShowInstruments();
        Invalidate();
    }

    private void OnStageAction()
    {
        ListenToEngine(_stageListened?.Engine);
        if (Renderer == null || _station == null || _stageListened is not { } stage) return;

        switch (stage.Phase)
        {
            case StationPhase.Arriving:
                StartDrive(PathDrive.Arrive(_station.Arrive, IdleRpm()));
                break;
            case StationPhase.Leaving:
                StartDrive(PathDrive.Leave(_station.Leave, IdleRpm()));
                break;
            case StationPhase.AtPump:
                StandAtPump();
                break;
        }
    }

    /// <summary>A scene loaded again (another light) picks the car up where the stage has it</summary>
    private void StartFromStage()
    {
        ListenToEngine(_stageListened?.Engine);
        _drive = null;
        switch (_stageListened?.Phase)
        {
            case StationPhase.Arriving:
                StartDrive(PathDrive.Arrive(_station!.Arrive, IdleRpm()));
                break;
            case StationPhase.Leaving:
            case StationPhase.Gone:
                StartDrive(PathDrive.Leave(_station!.Leave, IdleRpm()));
                break;
            case StationPhase.AtPump:
                StandAtPump();
                break;
            default:
                // Waiting to drive in: at the start of the way in, out on the road
                Place(PathDrive.Arrive(_station!.Arrive, IdleRpm()).At(0f));
                _lookYaw = 0f;
                AimCamera();
                break;
        }
    }

    /// <summary>The headlights: on while the car drives at night, off while it stands at the pump</summary>
    private void Headlights(bool on)
    {
        if (Renderer?.CarNode is { } node) node.HeadlightsEnabled = on && _light == StreetLight.Night;
    }

    private void StartDrive(PathDrive drive)
    {
        Headlights(true);
        _drive = drive;
        _driveTime = 0f;
        _lastDistance = 0f;
        _pitch.Reset();
        _lookHeld = false;
        Place(drive.At(0f));
        AnimateFor(TimeSpan.FromSeconds(1));
        Invalidate();
    }

    private void StandAtPump()
    {
        if (_station is not { } station) return;

        _drive = null;
        _engineListened?.Drive(null);
        Headlights(false);
        if (_pumpPose is not { } pose)
        {
            var arrival = PathDrive.Arrive(station.Arrive, IdleRpm());
            _pumpPose = pose = arrival.At(arrival.Duration);
        }

        Place(pose);
        _lookYaw = station.LookAtPump;
        _lookHeld = false;
        AimCamera();
        Invalidate();
    }

    private double IdleRpm() => _stageListened?.Engine?.Spec?.IdleRpm is > 200 and var idle ? idle : 800;

    /// <summary>The player's engine rocks the car at the pump: every change of its pose is a frame</summary>
    private void ListenToEngine(EngineRunner? engine)
    {
        if (ReferenceEquals(engine, _engineListened)) return;
        if (_engineListened != null) _engineListened.PoseChanged -= OnEnginePose;
        _engineListened = engine;
        if (_engineListened != null) _engineListened.PoseChanged += OnEnginePose;
    }

    private void OnEnginePose()
    {
        if (_drive == null && Renderer != null) Invalidate();
    }

    /// <summary>The car where the drive has it, on the ground under its wheels</summary>
    private void Place(PathState state)
    {
        var heading = state.Heading * MathF.PI / 180f;
        var forward = new Sx.Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading));

        // A car's own +x is its left
        var left = new Sx.Vector3(MathF.Cos(heading), 0f, -MathF.Sin(heading));
        float Ground(Sx.Vector3 at) => _ground.HeightAt(at.X, at.Z);

        var middle = new Sx.Vector3(state.X, 0f, state.Z);
        var front = Ground(middle + forward * HalfWheelbase);
        var rear = Ground(middle - forward * HalfWheelbase);
        var leftSide = Ground(middle + left * HalfTrack);
        var rightSide = Ground(middle - left * HalfTrack);

        var z = Sx.Vector3.Normalize(new Sx.Vector3(forward.X * 2 * HalfWheelbase, front - rear, forward.Z * 2 * HalfWheelbase));
        var xAxis = Sx.Vector3.Normalize(new Sx.Vector3(left.X * 2 * HalfTrack, leftSide - rightSide, left.Z * 2 * HalfTrack));
        var y = Sx.Vector3.Normalize(Sx.Vector3.Cross(z, xAxis));
        xAxis = Sx.Vector3.Cross(y, z);
        var height = (front + rear + leftSide + rightSide) / 4f;

        _placement = new Sx.Matrix
        {
            M11 = xAxis.X, M12 = xAxis.Y, M13 = xAxis.Z,
            M21 = y.X, M22 = y.Y, M23 = y.Z,
            M31 = z.X, M32 = z.Y, M33 = z.Z,
            M41 = state.X, M42 = height + _frame.Lift, M43 = state.Z, M44 = 1f
        };

        _wheelAngle += RivalDrive.WheelAngle(state.Distance - _lastDistance);
        _lastDistance = state.Distance;
        PoseCar();
    }

    /// <summary>The body on its springs: the engine's rock, and the dive and squat of the drive</summary>
    private void PoseCar()
    {
        if (Renderer?.CarNode is not { } node) return;

        var engine = _engineListened?.Pose ?? BodyPose.Rest;
        var pose = new BodyPose(engine.Roll, engine.Pitch + _pitch.Pitch, engine.Heave);
        var fresh = _rock == null || !ReferenceEquals(_rock.Car, node);
        if (fresh) _rock = new CarBodyRock(node);
        if (_rock!.Apply(pose, _wheelAngle, _placement) || fresh) Renderer.RefreshShadows();
    }

    /// <summary>Moves the car along its drive by a frame; true while it moves</summary>
    private bool StepDrive(float dt)
    {
        if (_drive is not { } drive || _stageListened is not { } stage) return false;

        _driveTime += dt;
        var state = drive.At(_driveTime);
        _speed = state.Speed;
        _steer += (drive.At(_driveTime + SteerAhead).Curvature - _steer) * (1f - MathF.Exp(-dt / SteerSeconds));
        _pitch.Tick(dt, state.Acceleration);
        _engineListened?.Drive(state.Rpm, state.Throttle);
        if (Renderer?.CarNode is { } node) node.BrakeLightsEnabled = state.Braking;

        // The head turns a little into a bend: the heading's turn over the next second. Both are to the left positive
        // (a heading growing is a turn to the left, as PathDrive has it)
        var ahead = drive.At(_driveTime + 1f).Heading;
        var turn = (ahead - state.Heading + 540f) % 360f - 180f;
        _bend += (turn * LookIntoBend - _bend) * (1f - MathF.Exp(-dt / 0.5f));
        Place(state);

        if (drive.IsLeaving && _driveTime >= drive.Duration - GoneBeforeTheEnd) stage.ReportGone();
        if (!state.Done) return true;

        _drive = null;
        _engineListened?.Drive(null);
        if (drive.IsLeaving)
        {
            stage.ReportGone();
        }
        else
        {
            Headlights(false);
            stage.ReportAtPump();
        }

        return false;
    }

    #endregion

    #region Camera

    /// <summary>Where the driver looks now: ahead while driving, at the pump while standing; a drag holds it</summary>
    private float TargetYaw() => _lookHeld ? _lookYaw
        : _stageListened?.Phase == StationPhase.AtPump && _drive == null ? _station?.LookAtPump ?? 0f
        : _bend;

    private void AimCamera()
    {
        if (Renderer is not { } renderer || renderer.Camera is not { } camera) return;

        var lean = _rock?.WorldLean ?? Sx.Matrix.Identity;
        var yaw = _lookYaw * MathF.PI / 180f;
        var pitch = _lookPitch * MathF.PI / 180f;

        // In the car's own space: +z ahead, +x to the left
        var look = new Sx.Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        var eye = Sx.Vector3.TransformCoordinate(Sx.Vector3.TransformCoordinate(_eye, _placement), lean);
        var direction = Sx.Vector3.TransformNormal(Sx.Vector3.TransformNormal(look, _placement), lean);
        var up = Sx.Vector3.TransformNormal(Sx.Vector3.TransformNormal(Sx.Vector3.UnitY, _placement), lean);

        camera.FovY = _fieldOfView * MathF.PI / 180f;
        camera.LookAt(eye, eye + direction, up);
        camera.SetLens(renderer.AspectRatio);
        renderer.IsDirty = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            _lookHeld = false;
            _lookPitch = SceneLights.SeatPitch;
            AnimateFor(TimeSpan.FromSeconds(LookSeconds * 3));
            e.Handled = true;
            return;
        }

        _dragFrom = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragFrom = null;
        ReleaseMouseCapture();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed) return;

        var now = e.GetPosition(this);
        _lookHeld = true;
        _lookYaw = Math.Clamp(_lookYaw + (float)(from.X - now.X) * LookDegreesPerPixel, -MaxLookYaw, MaxLookYaw);
        _lookPitch = Math.Clamp(_lookPitch + (float)(from.Y - now.Y) * LookDegreesPerPixel, -MaxLookPitch, MaxLookPitch);
        _dragFrom = now;
        AimCamera();
        Invalidate();
    }

    #endregion

    #region Rendering

    protected override bool StepFrame(GarageRenderer renderer, float dt)
    {
        var moving = StepDrive(dt);
        if (!moving)
        {
            _speed = 0f;
            _pitch.Tick(dt, 0);
            PoseCar();
        }

        ShowInstruments();

        // The head turns towards where the driver would look
        var target = TargetYaw();
        var turning = MathF.Abs(target - _lookYaw) > 0.05f;
        if (turning) _lookYaw += (target - _lookYaw) * (1f - MathF.Exp(-dt / (LookSeconds / 3f)));
        AimCamera();

        return moving || turning || !_pitch.IsSettled || _engineListened is { IsActive: true };
    }

    protected override void OnFramePresented()
    {
        if (_fadeInAfter < 0) return;
        if (_fadeInAfter-- > 0) return;

        if (IsPictureHidden) FadeIn(SceneFadeIn);
    }

    // The station's own engine listener rocks the car; the base's is not used
    protected override void OnPoseChanged()
    {
    }

    #endregion
}
