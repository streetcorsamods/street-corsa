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
/// A place the player drives into (the gas station, the diner), seen from the driver's seat of the player's car: it
/// comes up the road, turns in and stops where it stands there, and pulls out onto the road again, as the screen's
/// <see cref="DriveInStage"/> says, its scripts (<see cref="ArriveTrigger"/>, <see cref="LeaveTrigger"/>) moving it.
/// The car sits on the scene's ground (<see cref="StationGround"/>), its wheels turn, its body dives on the brakes, its
/// engine follows the revs, and the view goes with it: ahead while it drives, where the place has the driver look while
/// it stands. Dragging with the mouse looks round; a double click turns the head back where the driver would look.
/// </summary>
public abstract class DriveInViewport3D : OnboardViewport3D
{
    // The driver's head turns to where it looks standing over about a second, and a little into a bend while driving
    private const float LookSeconds = 0.9f;
    private const float LookIntoBend = 0.35f;

    // Where the wheels touch, from the car's middle: the ground under them tilts the car
    private const float HalfWheelbase = 1.4f;
    private const float HalfTrack = 0.75f;

    // What stands in the scene
    private string? _kn5;
    private StreetLight? _light;
    private StreetCar? _player;
    private IDriveInScene? _scene;
    private StationGround _ground = StationGround.Flat;
    private DriveInStage? _stageListened;
    private EngineRunner? _engineListened;

    // The player's car as its scripts move it (its home where it stands)
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

    protected DriveInViewport3D(string logName)
        : base(logName)
    {
        // A viewport that cannot show the place says so at once: the screen does not wait on it
        Failed += (_, _) =>
        {
            if (_stageListened != null) _stageListened.CannotShow = true;
        };
    }


    #region Dependency properties

    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(IDriveInScene), typeof(DriveInViewport3D),
        new PropertyMetadata(null, (d, _) => ((DriveInViewport3D)d).RequestLoad()));

    public IDriveInScene? Scene
    {
        get => (IDriveInScene?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public static readonly DependencyProperty StageProperty = DependencyProperty.Register(
        nameof(Stage), typeof(DriveInStage), typeof(DriveInViewport3D),
        new PropertyMetadata(null, (d, _) => ((DriveInViewport3D)d).OnStageChanged()));

    public DriveInStage? Stage
    {
        get => (DriveInStage?)GetValue(StageProperty);
        set => SetValue(StageProperty, value);
    }

    #endregion

    #region The place's scripts

    /// <summary>The car drives in off the road; the screen goes on once it lets go</summary>
    protected abstract string ArriveTrigger { get; }

    /// <summary>The car stands there (driven in, towed in, or back)</summary>
    protected abstract string StandTrigger { get; }

    /// <summary>The car drives off; it is gone once it lets go</summary>
    protected abstract string LeaveTrigger { get; }

    /// <summary>A drive in with no script for it, the car put where the drive would end: what else the place does then</summary>
    protected virtual void ArrivedUnscripted()
    {
    }

    #endregion

    #region Loading

    private string? WantedKn5 => Scene is { HasModel: true } scene ? scene.Kn5 : null;

    protected override bool IsUpToDate => WantedKn5 == null || PlayerCar == null
        ? Renderer == null
        : Renderer != null && _kn5 == WantedKn5 && _player == PlayerCar && _light == Light;

    protected override string LoadDescription => $"{PlayerCar?.Directory} at {WantedKn5}";

    protected override async Task LoadAsync()
    {
        var scene = Scene;
        var player = PlayerCar;
        var kn5 = WantedKn5;
        if (scene == null || player == null || kn5 == null)
        {
            DisposeRenderer(releaseDevice: false);
            return;
        }

        var light = Light;
        var renderer = await StartSceneAsync(kn5, light);
        if (renderer == null) return;

        var ground = await Task.Run(() => StationGround.Load(scene.GroundFile));
        if (Renderer != renderer || !IsLoaded) return;

        LightScene(renderer, scene.LightingFor(light), scene.Sun);
        SceneDressing.Glow(renderer, scene.GlowAt(light));
        SceneDressing.ShowSky(renderer, scene.SkyPrefix, scene.SkyNodeAt(light));

        // The car stands where the drive puts it (CarBodyRock), not where its slot is
        if (!await LoadPlayerCarAsync(renderer, player, light, placementOf: null)) return;

        _kn5 = kn5;
        _light = light;
        _player = player;
        _scene = scene;
        _ground = ground;

        StartFromStage();
        Logger.Information("{Scene} up at {Light} with {Car}", scene.Id, light, Path.GetFileName(player.Directory));

        if (Stage is { } stage) stage.IsShown = true;
        FadeInOnceSettled();
        AnimateFor(TimeSpan.FromSeconds(1));
        Invalidate();
        OnSceneUp(renderer);
    }

    /// <summary>The place and the player's car are up: what else stands there (the diner's racers' cars) can come</summary>
    protected virtual void OnSceneUp(GarageRenderer renderer)
    {
    }

    /// <summary>The place and the player's car are up (<see cref="OnSceneUp"/> has been or is being told)</summary>
    protected bool IsSceneUp => Renderer != null && _kn5 != null;

    /// <summary>The height of the place's ground at a point</summary>
    protected float GroundAt(float x, float z) => _ground.HeightAt(x, z);


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
        _scene = null;
        _rock = null;

        // Whatever the car was doing, it is done at once: nobody sees it
        var playing = Playing;
        StopRun();
        _car?.Drop();
        _car = null;
        _engineListened?.Drive(null);
        if (_stageListened is { } stage)
        {
            if (playing == LeaveTrigger) stage.ReportGone();
            else if (playing == ArriveTrigger) stage.ReportArrived();
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
    private void ListenTo(DriveInStage? stage)
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
        if (Renderer == null || _scene == null || _stageListened is not { } stage) return;

        switch (stage.Phase)
        {
            case DriveInPhase.Arriving:
                DriveIn();
                break;
            case DriveInPhase.Leaving:
                DriveOut();
                break;
            case DriveInPhase.Standing:
                StandThere();
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
            case DriveInPhase.Arriving:
                DriveIn();
                break;
            case DriveInPhase.Leaving:
            case DriveInPhase.Gone:
                DriveOut();
                break;
            case DriveInPhase.Standing:
                StandThere();
                break;
            default:
                // Waiting to drive in: at the start of the way in, out on the road
                Car.Place(_scene!.ScriptMarks.PathStart(Arrive) ?? Car.Home);
                PlaceCar(Car.Pose);
                LookYaw = 0f;
                AimCamera();
                break;
        }
    }

    /// <summary>The player's car as the scripts move it: it stands where it stands there when nothing moves it</summary>
    private ScriptedCar Car => _car ??= NewCar();

    // The way in, as the scripts have it (a place's file may name its own)
    private const string Arrive = "arrive";

    private ScriptedCar NewCar()
    {
        // Where the scripts bring it: a mod's own way in moves both
        var home = _scene!.ScriptMarks.Stand(_scene.StandMark, Arrive) ?? default;
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
        if (Renderer == null || _scene is not { } scene || _stageListened is not { } stage) return null;

        _lookHeld = false;
        var facts = new ScriptFacts(stage.Time, Light, stage.Towed) { Screen = told?.Screen, From = told?.From, To = told?.To };
        return PlayScript(trigger, Car, SceneStage.Player, scene.ScriptMarks, facts);
    }

    /// <summary>
    /// A script of the flow's (screen.&lt;id&gt;.enter or .leave) with the player's car; done once it lets the screen go
    /// on. With no script for the moment nothing is touched (the place's own show goes on): null.
    /// </summary>
    private Task? PlayFlowScript(string trigger, ScriptFacts told)
    {
        if (_stageListened is not { } stage) return null;

        var facts = new ScriptFacts(stage.Time, Light, stage.Towed) { Screen = told.Screen, From = told.From, To = told.To };
        if (!HasScript(trigger, () => stage.Engine, facts)) return null;
        return Play(trigger, told) is { Script: not null } run ? run.Released : null;
    }

    /// <summary>The car drives in off the road; standing there once its script lets the screen go on</summary>
    private void DriveIn()
    {
        if (Play(ArriveTrigger) is not { } run) return;

        // No script for it: the car is where the drive would end
        if (run.Script == null)
        {
            Car.GoHome();
            PlaceCar(Car.Pose);
            ArrivedUnscripted();
        }

        AfterRelease(run, () =>
        {
            _stageListened?.ReportArrived();
            Play(StandTrigger);
        });
    }

    /// <summary>The car pulls out onto the road; gone once its script lets the screen go (it drives on behind the curtain)</summary>
    private void DriveOut()
    {
        if (Play(LeaveTrigger) is not { } run) return;
        AfterRelease(run, () => _stageListened?.ReportGone());
    }

    /// <summary>The car standing there at once (towed in, back, or back from a way out that went nowhere), the driver looking where they would</summary>
    private void StandThere()
    {
        if (_scene == null) return;

        StopRun();
        Car.GoHome();
        Car.Lights(false, false);
        _engineListened?.Drive(null);
        PlaceCar(Car.Pose);
        LookYaw = _scene.LookWhileStanding;
        LookPitch = SceneLights.SeatPitch + _scene.LookUpWhileStanding;
        _lookHeld = false;
        AimCamera();
        Play(StandTrigger);
        Invalidate();
    }

    /// <summary>The player's engine rocks the standing car: every change of its pose is a frame</summary>
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
        if (_rock!.Apply(pose, _car?.WheelSpin ?? 0f, _placement, _car?.Steer ?? 0f) || fresh)
        {
            FocusShadows(Renderer);
            Renderer.RefreshShadows();
        }
    }

    /// <summary>
    /// Where the shadows are worked out: the renderer centres them on the main car's box, which it keeps from where the
    /// car loaded; told again where the car is, so they go with it
    /// </summary>
    protected virtual void FocusShadows(GarageRenderer renderer) => renderer.MainSlot.ResetCarBoundingBox();

    /// <summary>Where the player's car stands now, in the world</summary>
    protected Sx.Vector3 CarPosition => new(_placement.M41, _placement.M42, _placement.M43);

    /// <summary>Moves the car along its drive by a frame; true while it moves or its body settles</summary>
    private bool StepCar(float dt)
    {
        if (_car is not { } car) return false;

        var moving = car.Step(dt);
        _speed = car.Speed;
        if (car.Plan != null)
        {
            // The wheel turns into a bend a moment before the car gets there; backing, the other way
            var bend = (car.Ahead(SteerAhead)?.Curvature ?? 0f) * (car.Plan.Reverse ? -1f : 1f);
            _steer += (bend - _steer) * (1f - MathF.Exp(-dt / SteerSeconds));

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

    /// <summary>Where the driver looks now: ahead while driving, where the place says while standing; a drag holds it</summary>
    private float TargetYaw() => _lookHeld ? LookYaw
        : IsStandingStill ? _scene?.LookWhileStanding ?? 0f
        : _bend;

    /// <summary>How far up the driver looks now: as the seat has it while driving, up a little more standing where the place says</summary>
    private float TargetPitch() => _lookHeld ? LookPitch
        : SceneLights.SeatPitch + (IsStandingStill ? _scene?.LookUpWhileStanding ?? 0f : 0f);

    private bool IsStandingStill => _stageListened?.Phase == DriveInPhase.Standing && _car?.Plan == null;

    /// <summary>From the driver's eyes, the car where the drive has it and its body as it leans</summary>
    private void AimCamera()
    {
        if (Renderer is { } renderer) AimCamera(renderer, _placement, _rock?.WorldLean ?? Sx.Matrix.Identity);
    }

    /// <summary>The head turns back over a moment to where the driver would look</summary>
    protected override void LookBack()
    {
        _lookHeld = false;
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

        // The head turns towards where the driver would look, and up or down to it
        var target = TargetYaw();
        var pitch = TargetPitch();
        var turning = MathF.Abs(target - LookYaw) > 0.05f || MathF.Abs(pitch - LookPitch) > 0.05f;
        var step = 1f - MathF.Exp(-dt / (LookSeconds / 3f));
        if (turning)
        {
            LookYaw += (target - LookYaw) * step;
            LookPitch += (pitch - LookPitch) * step;
        }
        AimCamera();

        return moving || turning || _engineListened is { IsActive: true };
    }

    // The place's own engine listener rocks the car; the base's is not used
    protected override void OnPoseChanged()
    {
    }

    #endregion
}
