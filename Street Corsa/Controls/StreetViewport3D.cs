using System.IO;
using System.Windows;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Audio;
using StreetCorsa.Controls.Street;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Street;
using CarSlot = AcTools.Render.Kn5SpecificForward.ForwardKn5ObjectRenderer.CarSlot;
using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// The Cruise screen's street, seen from the driver's seat of the player's parked car, looking out of the side window
/// at the lane beside it. A rival drives up that lane and stops alongside, or pulls away, as the screen's
/// <see cref="StreetStage"/> says: its wheels turn, its body dives on the brakes and squats as it leaves, its brake
/// lights come on, and its engine is heard from where the car is. The player's own engine rocks the car they sit in,
/// and the view with it. Dragging with the mouse looks round; a double click looks back out of the window at once.
/// </summary>
public class StreetViewport3D : OnboardViewport3D
{
    // Where the driver looks: degrees from straight ahead towards the lane
    private const float DefaultLookYaw = 72f;

    // Loud beside the window, fading as it goes up the street
    private const float RivalFullVolumeWithin = 5f;
    private const float RivalQuietest = 0.06f;

    // What stands in the scene
    private string? _kn5;
    private StreetCar? _player;
    private StreetScene? _scene;
    private StreetStage? _stageListened;

    // The rival's car's lift over the road (AcCarFrame)
    private float _rivalLift;

    // The rival's car
    private CarSlot? _rivalSlot;
    private StreetCar? _rivalShown;
    private int _rivalLoad;

    // Rival loads run one after another: two into the one slot at once would draw on the device from two workers
    private Task _rivalLoads = Task.CompletedTask;
    private bool _rivalReady;
    private RivalDrive? _drive;
    private float _driveTime;
    private float _rivalPosition;
    private float _wheelAngle;
    private readonly ChassisPitch _pitch = new();
    private CarBodyRock? _rivalRock;

    public StreetViewport3D()
        : base("Street")
    {
        LookYaw = DefaultLookYaw;
    }

    protected override string ViewportName => "Street viewport";

    #region Dependency properties

    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(StreetScene), typeof(StreetViewport3D),
        new PropertyMetadata(null, (d, _) => ((StreetViewport3D)d).RequestLoad()));

    /// <summary>The street</summary>
    public StreetScene? Scene
    {
        get => (StreetScene?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public static readonly DependencyProperty StageProperty = DependencyProperty.Register(
        nameof(Stage), typeof(StreetStage), typeof(StreetViewport3D),
        new PropertyMetadata(null, (d, _) => ((StreetViewport3D)d).OnStageChanged()));

    /// <summary>What the rival does, as the screen decides</summary>
    public StreetStage? Stage
    {
        get => (StreetStage?)GetValue(StageProperty);
        set => SetValue(StageProperty, value);
    }

    #endregion

    #region Loading

    private string? WantedKn5 => Scene?.Kn5For(Light);

    protected override bool IsUpToDate => Scene == null || PlayerCar == null
        ? Renderer == null
        : Renderer != null && _kn5 == WantedKn5 && _player == PlayerCar;

    protected override string LoadDescription => $"{PlayerCar?.Directory} in {WantedKn5}";

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

        LightScene(renderer, scene.LightingFor(light), scene.Sun);

        // Parked where the scene has the player, lifted onto the road as the car's own data says
        if (!await LoadPlayerCarAsync(renderer, player, light, frame => Placement(scene, scene.Player, 0f, lift: frame.Lift))) return;

        _kn5 = kn5;
        _player = player;
        _scene = scene;
        LookYaw = DefaultLookYaw;

        AimCamera(renderer);
        renderer.RefreshShadows();

        Logger.Information("Street {Street} up at {Light} with {Car}", scene.Id, light, Path.GetFileName(player.Directory));

        if (Stage is { } stage)
        {
            stage.IsShown = true;

            // A rival already there comes back into the new scene where the stage has it
            if (stage.Phase != RivalPhase.None) QueueRival(renderer, stage);
        }

        OnPoseChanged();
        FadeInOnceSettled();
        Invalidate();
    }

    /// <summary>
    /// A car standing at <paramref name="spot"/>, moved <paramref name="along"/> metres ahead of it, its model's origin
    /// <paramref name="lift"/> over the road (<see cref="AcCarFrame.Lift"/>)
    /// </summary>
    private static Sx.Matrix Placement(StreetScene scene, StreetSpot spot, float along, float aside = 0f, float lift = 0f)
    {
        var heading = spot.Heading * MathF.PI / 180f;
        var forward = new Sx.Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading));

        // A car's own +x is its left (its driver's side, in the cars of the day)
        var left = new Sx.Vector3(MathF.Cos(heading), 0f, -MathF.Sin(heading));
        var at = new Sx.Vector3(spot.X, lift, spot.Z) + forward * along + left * aside;
        return Sx.Matrix.RotationY(heading) * Sx.Matrix.Translation(at);
    }

    protected override void OnRendererDisposed()
    {
        base.OnRendererDisposed();
        if (_stageListened != null) _stageListened.IsShown = false;
        _kn5 = null;
        _player = null;
        _scene = null;
        _rivalSlot = null;
        _rivalShown = null;
        _rivalReady = false;
        _rivalRock = null;
        _rivalLoad++;

        // Whatever the rival was doing, it has no car to do it in: the stage hears of it as if at once
        if (_stageListened is { Phase: RivalPhase.Arriving }) _stageListened.ReportAlongside();
        if (_stageListened is { Phase: RivalPhase.Leaving }) _stageListened.ReportGone();
        _drive = null;
    }

    #endregion

    #region The rival

    private void OnStageChanged()
    {
        if (IsLoaded) ListenTo(Stage);
    }

    protected override void ListenToStage(bool listen) => ListenTo(listen ? Stage : null);

    /// <summary>Listens to <paramref name="stage"/> and no other: only while the control is loaded, so a stage that outlives it does not keep it alive through its event</summary>
    private void ListenTo(StreetStage? stage)
    {
        if (ReferenceEquals(stage, _stageListened)) return;

        if (_stageListened != null)
        {
            _stageListened.Changed -= OnStageAction;
            _stageListened.IsShown = false;
        }

        _stageListened = stage;
        if (_stageListened != null)
        {
            _stageListened.Changed += OnStageAction;
            _stageListened.IsShown = Renderer != null && _kn5 != null;
        }
    }

    private void OnStageAction()
    {
        var renderer = Renderer;
        var stage = _stageListened;
        if (renderer == null || stage == null || _scene == null) return;

        switch (stage.Phase)
        {
            case RivalPhase.Arriving:
                QueueRival(renderer, stage);
                break;
            case RivalPhase.Leaving:
                StartLeaving(stage);
                break;
            case RivalPhase.None:
                HideRival();
                break;
        }
    }

    /// <summary>
    /// Shows the rival once the loads asked for before are done (<see cref="ShowRivalAsync"/>). The newest ask is the
    /// one that counts: one overtaken while it waited, or hidden meanwhile, does nothing.
    /// </summary>
    private void QueueRival(GarageRenderer renderer, StreetStage stage)
    {
        var load = ++_rivalLoad;
        var before = _rivalLoads;
        _rivalLoads = After(before, () => ShowRivalAsync(renderer, stage, load));

        static async Task After(Task before, Func<Task> next)
        {
            try
            {
                await before;
            }
            catch
            {
                // The load before has said how it ended; this one goes ahead all the same
            }

            await next();
        }
    }

    /// <summary>
    /// Puts the rival's car in its slot, if it is not the one there already, and starts it up the lane. A car that
    /// will not load is not seen: the stage hears it arrived all the same, so the screen goes on.
    /// </summary>
    private async Task ShowRivalAsync(GarageRenderer renderer, StreetStage stage, int load)
    {
        if (load != _rivalLoad || Renderer != renderer) return;

        var car = stage.RivalCar;
        var scene = _scene;
        if (car == null || scene == null) return;

        _drive = null;
        _rivalReady = false;

        try
        {
            if (_rivalSlot == null || _rivalShown != car)
            {
                _rivalRock?.Release();
                _rivalRock = null;

                var hidden = Sx.Matrix.Translation(0f, -50f, 0f);
                _rivalSlot ??= renderer.AddCar(null);
                _rivalSlot.LocalMatrix = hidden;

                var skin = SkinOrDefault(car.Directory, car.Skin);
                await TrackDeviceWork(_rivalSlot.SetCarAsync(CarDescription.FromDirectory(car.Directory), skin, RendererToken));
                if (Renderer != renderer || load != _rivalLoad) return;

                _rivalShown = car;
                _rivalLift = SceneLights.FrameOf(car.Directory).Lift;
                if (_rivalSlot.CarNode is { } node && Light == StreetLight.Night) node.HeadlightsEnabled = true;
            }
        }
        catch (Exception ex) when (!IsDeviceLost(ex))
        {
            Logger.Error(ex, "The rival's car {Car} could not be shown", car.Directory);
            _rivalShown = null;
            stage.ReportAlongside();
            return;
        }

        if (_rivalSlot?.CarNode == null)
        {
            stage.ReportAlongside();
            return;
        }

        _rivalReady = true;
        _pitch.Reset();

        var engine = stage.RivalEngine;
        var (idle, limiter) = RpmRange(engine);

        if (stage.Phase == RivalPhase.Alongside)
        {
            // Came back into a new scene: already stopped where it was
            _rivalPosition = scene.RivalStop;
            _drive = null;
        }
        else
        {
            _drive = RivalDrive.Arrive(-scene.ApproachFrom, scene.RivalStop, idle, limiter);
            _driveTime = 0f;
            _rivalPosition = -scene.ApproachFrom;
        }

        // Placed and heard from where it is before it makes a sound
        PlaceRival();
        HearRival();
        engine?.StartRunning();
        AnimateFor(TimeSpan.FromSeconds(1));
    }

    /// <summary>The engine's idle and limiter, or a V8's of the day when it does not say</summary>
    private static (double Idle, double Limiter) RpmRange(EngineRunner? engine) =>
        (engine?.Spec?.IdleRpm is > 200 and var idle ? idle : 800,
         engine?.Spec?.LimiterRpm is > 1000 and var limiter ? limiter : 6000);

    private void StartLeaving(StreetStage stage)
    {
        if (!_rivalReady || _scene == null)
        {
            stage.ReportGone();
            return;
        }

        var engine = stage.RivalEngine;
        var (idle, limiter) = RpmRange(engine);
        engine?.SetPedal(null);
        _drive = RivalDrive.Leave(_rivalPosition, _scene.LeaveTo, idle, limiter);
        _driveTime = 0f;
        AnimateFor(TimeSpan.FromSeconds(1));
    }

    private void HideRival()
    {
        _drive = null;
        _rivalReady = false;
        _rivalLoad++;
        _pitch.Reset();
        _rivalRock?.Release();
        _rivalRock = null;
        if (_rivalSlot != null) _rivalSlot.LocalMatrix = Sx.Matrix.Translation(0f, -50f, 0f);
        Invalidate();
    }

    private void PlaceRival()
    {
        if (_rivalSlot?.CarNode is not { } node || _scene is not { } scene) return;

        var placement = Placement(scene, scene.Player, _rivalPosition, scene.LaneOffset, _rivalLift);
        var engine = _stageListened?.RivalEngine;
        var pose = engine?.Pose ?? BodyPose.Rest;
        var body = new BodyPose(pose.Roll, pose.Pitch + _pitch.Pitch, pose.Heave);

        _rivalRock ??= new CarBodyRock(node);
        _rivalRock.Apply(body, _wheelAngle, placement);
    }

    /// <summary>Moves the rival along its drive by a frame; true while it moves, its engine runs or its body settles</summary>
    private bool StepRival(float dt)
    {
        var stage = _stageListened;
        if (!_rivalReady || stage == null) return false;

        var engine = stage.RivalEngine;
        if (_drive is { } drive)
        {
            _driveTime += dt;
            var state = drive.At(_driveTime);
            _wheelAngle += RivalDrive.WheelAngle(state.Position - _rivalPosition);
            _rivalPosition = state.Position;
            _pitch.Tick(dt, state.Acceleration);
            engine?.Drive(state.Rpm, state.Throttle);
            if (_rivalSlot?.CarNode is { } node) node.BrakeLightsEnabled = state.Braking;

            if (state.Done)
            {
                _drive = null;
                engine?.Drive(null);
                if (drive.IsLeaving)
                {
                    HideRival();
                    stage.ReportGone();
                    return false;
                }

                stage.ReportAlongside();
            }
        }
        else
        {
            _pitch.Tick(dt, 0);
        }

        PlaceRival();
        HearRival();
        return _drive != null || engine is { IsActive: true } || !_pitch.IsSettled;
    }

    /// <summary>The rival's engine heard from where the car is, from where the player looks</summary>
    private void HearRival()
    {
        if (_stageListened?.RivalEngine is not { } engine || _scene is not { } scene || Renderer?.Camera is not { } camera) return;

        var at = Placement(scene, scene.Player, _rivalPosition, scene.LaneOffset);
        var car = new Sx.Vector3(at.M41, 0.5f, at.M43);
        var offset = car - camera.Position;
        var distance = offset.Length();

        // Into the listener's own axes, as FMOD takes them: x right, y up, z where they look. The renderer's space
        // is right-handed (a car's +x is its left), so right is look x up
        var look = Sx.Vector3.Normalize(camera.Look);
        var right = Sx.Vector3.Normalize(Sx.Vector3.Cross(look, Sx.Vector3.UnitY));
        var up = Sx.Vector3.Cross(right, look);
        var direction = (Sx.Vector3.Dot(offset, right), Sx.Vector3.Dot(offset, up), Sx.Vector3.Dot(offset, look));

        var volume = Math.Clamp(RivalFullVolumeWithin / Math.Max(distance, 0.1f), RivalQuietest, 1f);
        engine.Place(direction, volume);
    }

    #endregion

    #region Camera

    /// <summary>From the driver's eyes in the parked car, as its body leans with the running engine</summary>
    private void AimCamera(GarageRenderer renderer)
    {
        if (_scene is not { } scene) return;

        AimCamera(renderer, Placement(scene, scene.Player, 0f, lift: Frame.Lift), CarLean);
    }

    /// <summary>A little way round from straight ahead: the lane is to the left, the far side of the street not far to the right</summary>
    protected override float MinLookYaw => -35f;

    /// <summary>The view snaps back to looking out of the window</summary>
    protected override void LookBack()
    {
        LookYaw = DefaultLookYaw;
        LookPitch = SceneLights.SeatPitch;
        if (Renderer is { } renderer) AimCamera(renderer);
        Invalidate();
    }

    protected override void OnLookDragged()
    {
        if (Renderer is { } renderer) AimCamera(renderer);
    }

    #endregion

    #region Rendering

    protected override bool StepFrame(GarageRenderer renderer, float dt)
    {
        // The player's engine shakes the car, and the view with it (OnPoseChanged aims the camera as it does); a
        // viewport that changed shape is aimed again for its new lens
        if (renderer.AspectRatio != AimedAspect) AimCamera(renderer);

        var moving = StepRival(dt);

        // The tachometer follows the player's engine; the car stands, warm
        Instruments?.Show(RunningEngine?.Rpm ?? 0, 0f, null, 85f, 0f);
        return moving || RunningEngine is { IsActive: true };
    }

    protected override void OnPoseChanged()
    {
        RockCar(Renderer?.CarNode);
        if (Renderer is { } renderer) AimCamera(renderer);
    }

    #endregion
}
