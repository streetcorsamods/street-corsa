using System.IO;
using System.Windows;
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
/// round; a double click turns the head back where the driver would look.
/// </summary>
public class StationViewport3D : OnboardViewport3D
{
    // The driver's head turns towards the pump over about a second, and looks a little into a bend while driving
    private const float LookSeconds = 0.9f;
    private const float LookIntoBend = 0.35f;

    // Where the wheels touch, from the car's middle: the ground under them tilts the car
    private const float HalfWheelbase = 1.4f;
    private const float HalfTrack = 0.75f;

    // A car driving off is gone for the screen this long before its drive ends: the curtain comes down on it moving
    private const float GoneBeforeTheEnd = 1.2f;

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

    // What the dashboard's needles and the steering wheel show
    private float _speed;
    private float _steer;

    // The wheel turns into a bend this long before the car gets there, and follows over about this long
    private const float SteerAhead = 0.35f;
    private const float SteerSeconds = 0.25f;

    // Warm, as a car that drove over is
    private const float WaterC = 85f;

    // A drag holds where the driver looks; how far the head turns into a bend
    private bool _lookHeld;
    private float _bend;

    public StationViewport3D()
        : base("Station")
    {
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

        var light = Light;
        var renderer = await StartSceneAsync(kn5, light);
        if (renderer == null) return;

        var ground = await Task.Run(() => StationGround.Load(station.GroundFile));
        if (Renderer != renderer || !IsLoaded) return;

        LightScene(renderer, station.LightingFor(light), station.Sun);
        ShowSky(renderer, light);

        // The car stands where the drive puts it (CarBodyRock), not where its slot is
        if (!await LoadPlayerCarAsync(renderer, player, light, placementOf: null)) return;

        _kn5 = kn5;
        _light = light;
        _player = player;
        _station = station;
        _ground = ground;

        StartFromStage();
        Logger.Information("Station {Station} up at {Light} with {Car}", station.Id, light, Path.GetFileName(player.Directory));

        if (Stage is { } stage) stage.IsShown = true;
        FadeInOnceSettled();
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
        Instruments?.Show(_engineListened?.Rpm ?? 0, _speed * 3.6f, _stageListened?.FuelLitres ?? 0f, WaterC, _steer);
    }

    protected override void OnRendererDisposed()
    {
        base.OnRendererDisposed();
        if (_stageListened != null) _stageListened.IsShown = false;
        _kn5 = null;
        _light = null;
        _player = null;
        _station = null;
        _rock = null;
        _pumpPose = null;

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
        if (IsLoaded) ListenTo(Stage);
    }

    protected override void ListenToStage(bool listen) => ListenTo(listen ? Stage : null);

    /// <summary>
    /// Listens to <paramref name="stage"/> and no other: only while the control is loaded, so a stage that outlives it
    /// does not keep it alive through its events, and neither does the engine it listens to
    /// </summary>
    private void ListenTo(StationStage? stage)
    {
        if (ReferenceEquals(stage, _stageListened)) return;

        if (_stageListened != null)
        {
            _stageListened.Changed -= OnStageAction;
            _stageListened.FuelChanged -= OnFuelChanged;
            _stageListened.IsShown = false;
            _stageListened.CannotShow = false;
            ListenToEngine(null);
        }

        _stageListened = stage;
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
                LookYaw = 0f;
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
        LookYaw = station.LookAtPump;
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
            M41 = state.X, M42 = height + Frame.Lift, M43 = state.Z, M44 = 1f
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
    private float TargetYaw() => _lookHeld ? LookYaw
        : _stageListened?.Phase == StationPhase.AtPump && _drive == null ? _station?.LookAtPump ?? 0f
        : _bend;

    /// <summary>From the driver's eyes, the car where the drive has it and its body as it leans</summary>
    private void AimCamera()
    {
        if (Renderer is { } renderer) AimCamera(renderer, _placement, _rock?.WorldLean ?? Sx.Matrix.Identity);
    }

    /// <summary>The head turns back over a moment to where the driver would look</summary>
    protected override void LookBack()
    {
        _lookHeld = false;
        LookPitch = SceneLights.SeatPitch;
        AnimateFor(TimeSpan.FromSeconds(LookSeconds * 3));
    }

    protected override void OnLookDragged()
    {
        _lookHeld = true;
        AimCamera();
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
        var turning = MathF.Abs(target - LookYaw) > 0.05f;
        if (turning) LookYaw += (target - LookYaw) * (1f - MathF.Exp(-dt / (LookSeconds / 3f)));
        AimCamera();

        return moving || turning || !_pitch.IsSettled || _engineListened is { IsActive: true };
    }

    // The station's own engine listener rocks the car; the base's is not used
    protected override void OnPoseChanged()
    {
    }

    #endregion
}
