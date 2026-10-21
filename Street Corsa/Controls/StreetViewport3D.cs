using System.IO;
using System.Windows;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Audio;
using StreetCorsa.Controls.Scripting;
using StreetCorsa.Controls.Street;
using StreetCorsa.Looks;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Scripting;
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
    private StreetLight? _light;
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
    private CarBodyRock? _rivalRock;

    // The rival as its scripts move it (its home the spot alongside)
    private ScriptedCar? _rival;

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
        : Renderer != null && _kn5 == WantedKn5 && _player == PlayerCar && _light == Light;

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
        if (!await DressSceneAsync(renderer, scene.Pieces)) return;

        SceneLights.ApplyWithSky(renderer, scene.LightingFor(light), scene.Sun);

        // A street of one model for every light: its sky for the hour, its signs and windows lit for it, its own look
        if (scene.Model != null) SceneDressing.ShowSky(renderer, StreetScene.SkyPrefix, StreetScene.SkyNode(light));
        SceneDressing.ShowLooks(renderer, scene.ShownLooks);
        SceneDressing.Glow(renderer, scene.GlowAt(light));

        // Parked where the scene has the player, lifted onto the road as the car's own data says
        if (!await LoadPlayerCarAsync(renderer, player, light, frame => Placement(scene, scene.Player, 0f, lift: frame.Lift))) return;

        _kn5 = kn5;
        _light = light;
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
        _light = null;
        _player = null;
        _scene = null;
        _rivalSlot = null;
        _rivalShown = null;
        _rivalReady = false;
        _rivalRock = null;
        _rivalLoad++;

        // Whatever the rival was doing, it has no car to do it in: the stage hears of it as if at once
        StopRun();
        _rival?.Drop();
        _rival = null;
        if (_stageListened is { Phase: RivalPhase.Arriving }) _stageListened.ReportAlongside();
        if (_stageListened is { Phase: RivalPhase.Leaving }) _stageListened.ReportGone();
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

        // The rival's wear is made now, while the loads before it finish and its model loads, not as it is drawn
        if (stage.RivalCar is { } rival && rival != _rivalShown) _ = WearLayer.WarmAsync(rival.Directory, rival.Shown);
        _rivalLoads = After(before, () => ShowRivalAsync(renderer, stage, load));
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

        StopRun();
        _rival = null;
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
                await ShowPartsAsync(renderer, _rivalSlot.CarNode, car.Shown);
                if (Renderer != renderer || load != _rivalLoad) return;

                _rivalShown = car;
                _rivalLift = SceneLights.FrameOf(car.Directory, car.Shown,
                    ex => Logger.Warning(ex, "The data of {Car} could not be read: it sits as most cars do", car.Directory)).Lift;
                if (_rivalSlot.CarNode is { } node && Light == StreetLight.Night)
                {
                    node.HeadlightsEnabled = true;
                    SceneLights.BrightenHeadlights(renderer);
                }
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

        // Sent away while its car loaded (the stage heard it gone at once): it never comes
        if (stage.Phase is not (RivalPhase.Arriving or RivalPhase.Alongside))
        {
            HideRival();
            return;
        }

        _rivalReady = true;

        // Its engine is its own for as long as it is here: the stage lets go of it as the rival goes
        var engine = stage.RivalEngine;
        // It stands where the scripts bring it: a mod's own way up the lane moves both
        var marks = scene.ScriptMarks;
        var alongside = scene.Lane(scene.RivalStop);
        var home = marks.Stand("alongside", Arrive) ?? new GroundPose(alongside.X, alongside.Z, scene.Player.Heading * MathF.PI / 180f);
        _rival = new ScriptedCar(home, new ScriptEngine(() => engine), RivalLights);
        _rival.Started += () => AnimateFor(TimeSpan.FromSeconds(1));

        if (stage.Phase == RivalPhase.Alongside)
        {
            // Came back into a new scene: already stopped where it was, running
            PlaceRival();
            HearRival();
            engine?.StartRunning();
        }
        else
        {
            // Placed and heard from where it comes from before it makes a sound
            if (marks.PathStart(Arrive) is { } start) _rival.Place(start);

            var run = Play(ScriptTriggers.StreetRivalArrive, stage);

            // No script for it: the rival is where the drive would end, alongside, running
            if (run?.Script == null)
            {
                _rival.GoHome();
                engine?.StartRunning();
            }

            PlaceRival();
            HearRival();
            if (run != null) AfterRelease(run, () => _stageListened?.ReportAlongside());
            else stage.ReportAlongside();
        }

        AnimateFor(TimeSpan.FromSeconds(1));
    }

    /// <summary>The rival's lights: the headlights and the brake lights</summary>
    private void RivalLights(bool? head, bool? brake)
    {
        if (_rivalSlot?.CarNode is not { } node) return;
        if (head is { } on) node.HeadlightsEnabled = on;
        if (brake is { } braking) node.BrakeLightsEnabled = braking;
        Invalidate();
    }

    // The way up the lane, as the scripts have it (a street's file may name its own)
    private const string Arrive = "arrive";

    /// <summary>The script for <paramref name="trigger"/> plays with the rival; null with no rival to play it with</summary>
    private ScriptRun? Play(string trigger, StreetStage stage) =>
        _rival is { } rival && _scene is { } scene
            ? PlayScript(trigger, rival, SceneStage.Rival, scene.ScriptMarks, new ScriptFacts(stage.Time, Light))
            : null;

    private void StartLeaving(StreetStage stage)
    {
        if (!_rivalReady || _scene == null)
        {
            stage.ReportGone();
            return;
        }

        stage.RivalEngine?.SetPedal(null);
        if (Play(ScriptTriggers.StreetRivalLeave, stage) is not { } run)
        {
            HideRival();
            stage.ReportGone();
            return;
        }

        GoneAfter(run);
    }

    /// <summary>
    /// The rival is gone out of sight when its leaving script is over, and the screen hears it then; a script that lets
    /// the screen go on before has the rival hidden once it is over (unless another has taken its place meanwhile).
    /// Nobody waits on this: what goes wrong in it is logged here.
    /// </summary>
    private async void GoneAfter(ScriptRun run)
    {
        try
        {
            await run.Released;
            if (!IsPlaying(run)) return;

            if (run.IsOver)
            {
                HideRival();
                _stageListened?.ReportGone();
                return;
            }

            _stageListened?.ReportGone();
            await run.Ended;
            if (IsPlaying(run)) HideRival();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "{Viewport}: the rival could not be seen off", ViewportName);
        }
    }

    private void HideRival()
    {
        StopRun();
        _rival?.Drop();
        _rival = null;
        _rivalReady = false;
        _rivalLoad++;
        _rivalRock?.Release();
        _rivalRock = null;
        if (_rivalSlot != null) _rivalSlot.LocalMatrix = Sx.Matrix.Translation(0f, -50f, 0f);
        Invalidate();
    }

    private void PlaceRival()
    {
        if (_rivalSlot?.CarNode is not { } node || _rival is not { } rival) return;

        var at = rival.Pose;
        var placement = Sx.Matrix.RotationY(at.Heading) * Sx.Matrix.Translation(at.X, _rivalLift, at.Z);
        var engine = (rival.Engine as ScriptEngine)?.Runner;
        var pose = engine?.Pose ?? BodyPose.Rest;
        var (roll, pitch) = rival.Body;
        var body = new BodyPose(pose.Roll + roll, pose.Pitch + pitch, pose.Heave);

        _rivalRock ??= new CarBodyRock(node);
        _rivalRock.Apply(body, rival.WheelSpin, placement, rival.Steer);
    }

    /// <summary>Moves the rival along its drive by a frame; true while it moves, its engine runs or its body settles</summary>
    private bool StepRival(float dt)
    {
        var stage = _stageListened;
        if (!_rivalReady || stage == null) return false;

        if (_rival is not { } rival) return false;

        var moving = rival.Step(dt);
        PlaceRival();
        HearRival();
        return moving || rival.Engine?.Runner is { IsActive: true };
    }

    /// <summary>The rival's engine heard from where the car is, from where the player looks</summary>
    private void HearRival()
    {
        if (_rival is not { Engine.Runner: { } engine } rival || Renderer?.Camera is not { } camera) return;

        var car = new Sx.Vector3(rival.Pose.X, 0.5f, rival.Pose.Z);
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
