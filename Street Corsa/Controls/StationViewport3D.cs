using System.IO;
using System.Windows;
using StreetCorsa.Audio;
using StreetCorsa.Controls.Scripting;
using StreetCorsa.Controls.Street;
using StreetCorsa.Services.Scripting;
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

    // What stands in the scene
    private string? _kn5;
    private StreetLight? _light;
    private StreetCar? _player;
    private StationScene? _station;
    private StationGround _ground = StationGround.Flat;
    private StationStage? _stageListened;
    private EngineRunner? _engineListened;

    // The player's car as its scripts move it (its home the pump)
    private ScriptedCar? _car;
    private Sx.Matrix _placement = Sx.Matrix.Identity;
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

        // Whatever the car was doing, it is done at once: nobody sees it
        var playing = Playing;
        StopRun();
        _car?.Drop();
        _car = null;
        _engineListened?.Drive(null);
        if (_stageListened is { } stage)
        {
            if (playing == ScriptTriggers.StationLeave) stage.ReportGone();
            else if (playing == ScriptTriggers.StationArrive) stage.ReportAtPump();
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
            _stageListened.PlayTrigger = null;
            ListenToEngine(null);
        }

        _stageListened = stage;
        if (_stageListened != null)
        {
            _stageListened.Changed += OnStageAction;
            _stageListened.FuelChanged += OnFuelChanged;
            _stageListened.PlayTrigger = PlayFlowScript;
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
                DriveIn();
                break;
            case StationPhase.Leaving:
                DriveOut();
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
        StopRun();
        switch (_stageListened?.Phase)
        {
            case StationPhase.Arriving:
                DriveIn();
                break;
            case StationPhase.Leaving:
            case StationPhase.Gone:
                DriveOut();
                break;
            case StationPhase.AtPump:
                StandAtPump();
                break;
            default:
                // Waiting to drive in: at the start of the way in, out on the road
                Car.Place(_station!.ScriptMarks.PathStart(Arrive) ?? Car.Home);
                PlaceCar(Car.Pose);
                LookYaw = 0f;
                AimCamera();
                break;
        }
    }

    /// <summary>The player's car as the scripts move it: it stands at the pump when nothing moves it</summary>
    private ScriptedCar Car => _car ??= NewCar();

    // The way in, and the pump at its end, as the scripts have them (a station's file may name its own)
    private const string Arrive = "arrive";
    private const string Pump = "pump";

    private ScriptedCar NewCar()
    {
        // Where the scripts bring it: a mod's own way in moves both
        var home = _station!.ScriptMarks.Stand(Pump, Arrive) ?? default;
        var car = new ScriptedCar(home, new ScriptEngine(() => _stageListened?.Engine), (head, brake) =>
        {
            if (Renderer?.CarNode is not { } node) return;
            if (head is { } on) node.HeadlightsEnabled = on;
            if (brake is { } braking) node.BrakeLightsEnabled = braking;
            Invalidate();
        });
        car.Started += () => AnimateFor(TimeSpan.FromSeconds(1));
        return car;
    }

    /// <summary>
    /// The script for <paramref name="trigger"/> plays with the player's car; null with no scene to play it in. A script
    /// of the flow's is told the screens (<paramref name="told"/>).
    /// </summary>
    private ScriptRun? Play(string trigger, ScriptFacts? told = null)
    {
        if (Renderer == null || _station is not { } station || _stageListened is not { } stage) return null;

        _lookHeld = false;
        var facts = new ScriptFacts(stage.Time, Light, stage.Towed) { Screen = told?.Screen, From = told?.From, To = told?.To };
        return PlayScript(trigger, Car, SceneStage.Player, station.ScriptMarks, facts);
    }

    /// <summary>
    /// A script of the flow's (screen.&lt;id&gt;.enter or .leave) with the player's car; done once it lets the screen go
    /// on. With no script for the moment nothing is touched (the station's own show goes on): null.
    /// </summary>
    private Task? PlayFlowScript(string trigger, ScriptFacts told)
    {
        if (_stageListened is not { } stage) return null;

        var facts = new ScriptFacts(stage.Time, Light, stage.Towed) { Screen = told.Screen, From = told.From, To = told.To };
        if (!HasScript(trigger, () => stage.Engine, facts)) return null;
        return Play(trigger, told) is { Script: not null } run ? run.Released : null;
    }

    /// <summary>The car drives in off the road; at the pump once its script lets the screen go on</summary>
    private void DriveIn()
    {
        if (Play(ScriptTriggers.StationArrive) is not { } run) return;

        // No script for it: the car is where the drive would end, at the pump, and the bell rings as the script's would
        if (run.Script == null)
        {
            Car.GoHome();
            PlaceCar(Car.Pose);
            Audio.Sfx.Play(Audio.Sfx.StationBell);
        }

        AfterRelease(run, () =>
        {
            _stageListened?.ReportAtPump();
            Play(ScriptTriggers.StationAtPump);
        });
    }

    /// <summary>The car pulls out onto the road; gone once its script lets the screen go (it drives on behind the curtain)</summary>
    private void DriveOut()
    {
        if (Play(ScriptTriggers.StationLeave) is not { } run) return;
        AfterRelease(run, () => _stageListened?.ReportGone());
    }

    /// <summary>The car at the pump at once (towed there, or back from a way out that went nowhere), the driver looking at the pump</summary>
    private void StandAtPump()
    {
        if (_station == null) return;

        StopRun();
        Car.GoHome();
        Car.Lights(false, false);
        _engineListened?.Drive(null);
        PlaceCar(Car.Pose);
        LookYaw = _station.LookAtPump;
        _lookHeld = false;
        AimCamera();
        Play(ScriptTriggers.StationAtPump);
        Invalidate();
    }

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
        if (_car?.Plan == null && Renderer != null) Invalidate();
    }

    /// <summary>The car at <paramref name="pose"/>, on the ground under its wheels</summary>
    private void PlaceCar(GroundPose pose)
    {
        var heading = pose.Heading;
        var forward = new Sx.Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading));

        // A car's own +x is its left
        var left = new Sx.Vector3(MathF.Cos(heading), 0f, -MathF.Sin(heading));
        float Ground(Sx.Vector3 at) => _ground.HeightAt(at.X, at.Z);

        var middle = new Sx.Vector3(pose.X, 0f, pose.Z);
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
            M41 = pose.X, M42 = height + Frame.Lift, M43 = pose.Z, M44 = 1f
        };

        PoseCar();
    }

    /// <summary>The body on its springs: the engine's rock, and the dive and squat of the drive</summary>
    private void PoseCar()
    {
        if (Renderer?.CarNode is not { } node) return;

        var engine = _engineListened?.Pose ?? BodyPose.Rest;
        var (roll, pitch) = _car?.Body ?? (0f, 0f);
        var pose = new BodyPose(engine.Roll + roll, engine.Pitch + pitch, engine.Heave);
        var fresh = _rock == null || !ReferenceEquals(_rock.Car, node);
        if (fresh) _rock = new CarBodyRock(node);
        if (_rock!.Apply(pose, _car?.WheelSpin ?? 0f, _placement, _car?.Steer ?? 0f) || fresh) Renderer.RefreshShadows();
    }

    /// <summary>Moves the car along its drive by a frame; true while it moves or its body settles</summary>
    private bool StepCar(float dt)
    {
        if (_car is not { } car) return false;

        var moving = car.Step(dt);
        _speed = car.Speed;
        if (car.Plan != null)
        {
            // The wheel turns into a bend a moment before the car gets there
            _steer += ((car.Ahead(SteerAhead)?.Curvature ?? 0f) - _steer) * (1f - MathF.Exp(-dt / SteerSeconds));

            // The head turns a little into a bend: the heading's turn over the next second, to the left positive
            var ahead = car.Ahead(1f)?.Heading ?? car.Pose.Heading;
            var turn = GroundPose.Turn(car.Pose.Heading, ahead) * 180f / MathF.PI;
            _bend += (turn * LookIntoBend - _bend) * (1f - MathF.Exp(-dt / 0.5f));
        }

        PlaceCar(car.Pose);
        return moving;
    }

    #endregion

    #region Camera

    /// <summary>Where the driver looks now: ahead while driving, at the pump while standing; a drag holds it</summary>
    private float TargetYaw() => _lookHeld ? LookYaw
        : _stageListened?.Phase == StationPhase.AtPump && _car?.Plan == null ? _station?.LookAtPump ?? 0f
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
        var moving = StepCar(dt);
        if (_car == null) PoseCar();

        ShowInstruments();

        // The head turns towards where the driver would look
        var target = TargetYaw();
        var turning = MathF.Abs(target - LookYaw) > 0.05f;
        if (turning) LookYaw += (target - LookYaw) * (1f - MathF.Exp(-dt / (LookSeconds / 3f)));
        AimCamera();

        return moving || turning || _engineListened is { IsActive: true };
    }

    // The station's own engine listener rocks the car; the base's is not used
    protected override void OnPoseChanged()
    {
    }

    #endregion
}
