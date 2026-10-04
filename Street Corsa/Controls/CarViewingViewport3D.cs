using System.IO;
using System.Windows;
using System.Windows.Input;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Controls.Showcase;
using StreetCorsa.Controls.Street;
using StreetCorsa.Looks;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Street;
using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// A car for sale where its seller has it: the scene (a dealer's showroom, or the street), lit as it says
/// (<paramref name="Lighting"/> and <paramref name="Sun"/>; none leaves a showroom in the renderer's own light), and the
/// car standing at <paramref name="X"/>, <paramref name="Z"/> facing <paramref name="Heading"/> degrees (0 faces +z).
/// The camera comes round to it from <paramref name="CameraFromSide"/> radians off its left side towards its nose, and
/// is never wheeled further out than <paramref name="MaxRadius"/> (a showroom's walls, the street's backdrop).
/// </summary>
public sealed record CarViewingStage(
    string? SceneKn5,
    StreetLighting? Lighting,
    float[]? Sun,
    string CarDirectory,
    string? Skin,
    float X,
    float Z,
    float Heading,
    float CameraFromSide,
    float MaxRadius,
    StreetCorsa.Parts.Cars.ShownSet? Shown = null);

/// <summary>
/// One car in a scene, to walk round: the car viewing screen's view of a car for sale. The scene and the car load
/// together and the picture is held back until the car stands there (<see cref="IsSettled"/>); then the camera glides
/// in to the car's front three-quarter and walks round it. Dragging walks round it by hand (the walk picks up again a
/// few seconds after the player lets go), the wheel steps closer or back. The car stands on the
/// ground by its tyres, and its running engine rocks it.
/// </summary>
public class CarViewingViewport3D : D3DViewportBase
{
    // The camera: eye height over the car's middle, the start of the glide in (further round and back), how quickly it
    // settles, what the pointer and the wheel do, and how near it may come
    private const float StartBeta = 0.12f;
    private const float GlideAlpha = 0.55f;
    private const float GlideRadius = 1.35f;
    private const float SettleRate = 2.4f;
    private const float Settled = 0.003f;
    private const float DragPerPixel = 0.008f;
    private const float WheelStep = 0.0007f;
    private const float MinBeta = 0.02f;
    private const float MaxBeta = 0.9f;
    private const float MinRadius = 3.2f;
    private const float FloorClearance = 0.25f;

    // Once in, the camera walks round the car (radians a second, a lap in about 25 s); the player's drag stops it, and
    // it eases back into the walk a while after the player lets go
    private const float TurnRate = 0.25f;
    private const double ResumeAfterSeconds = 4;
    private const float TurnEase = 1.5f;

    // The light over a car seen after dark: on the camera's side of it, up on a pole, warm, reaching the whole car
    private const float DarkBelow = 0.5f;
    private const float YardLightHeight = 4.5f;
    private const float YardLightOut = 2.5f;
    private const float YardLightBrightness = 2.5f;
    private const float YardLightRange = 12f;

    // Frames drawn with the car in before the picture is faded up
    private const int SettleFrames = 4;

    private CarViewingStage? _loaded;
    private Sx.Vector3 _target;
    private float _radiusGoal = 6f;
    private float _betaGoal = StartBeta;
    private float? _alphaGoal;
    private System.Windows.Point? _dragFrom;
    private bool _touched;
    private System.Windows.Threading.DispatcherTimer? _resume;
    private float _turnSpeed;
    private int _fadeInAfter = -1;

    public CarViewingViewport3D()
        : base("CarViewing", new LabelLook(14, 0xD8, 0x90, new Thickness(9, 5, 9, 5), new Vector(18, 18)))
    {
        Cursor = System.Windows.Input.Cursors.SizeAll;

        // A view that will not come up has nothing more to wait for
        Failed += (_, _) => IsSettled = true;
        NothingToShow += (_, _) => IsSettled = true;
    }

    protected override string ViewportName => "Car viewing viewport";

    #region Dependency properties

    public static readonly DependencyProperty StageProperty = DependencyProperty.Register(
        nameof(Stage), typeof(CarViewingStage), typeof(CarViewingViewport3D),
        new PropertyMetadata(null, (d, _) => ((CarViewingViewport3D)d).OnStageChanged()));

    /// <summary>The scene, and where the car stands in it</summary>
    public CarViewingStage? Stage
    {
        get => (CarViewingStage?)GetValue(StageProperty);
        set => SetValue(StageProperty, value);
    }

    private static readonly DependencyPropertyKey IsSettledPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsSettled), typeof(bool), typeof(CarViewingViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty IsSettledProperty = IsSettledPropertyKey.DependencyProperty;

    /// <summary>The car stands in its scene and is on show, or there will be nothing to show: done waiting</summary>
    public bool IsSettled
    {
        get => (bool)GetValue(IsSettledProperty);
        private set => SetValue(IsSettledPropertyKey, value);
    }

    #endregion

    #region Loading

    private void OnStageChanged()
    {
        IsSettled = false;
        RequestLoad();
    }

    protected override bool IsUpToDate => Stage == null ? Renderer == null : Renderer != null && ReferenceEquals(_loaded, Stage);

    protected override string LoadDescription => Stage is { } stage ? $"{Path.GetFileName(stage.CarDirectory)} in {stage.SceneKn5}" : "";

    protected override async Task LoadAsync()
    {
        var stage = Stage;
        DisposeRenderer(releaseDevice: false);
        if (stage == null) return;

        var scene = stage.SceneKn5;
        if (scene != null && !File.Exists(scene))
        {
            Logger.Warning("Scene not found, showing the car without it: {Scene}", scene);
            scene = null;
        }

        if (!Directory.Exists(stage.CarDirectory))
        {
            Logger.Warning("Car folder not found, nothing to show: {Directory}", stage.CarDirectory);
            IsSettled = true;
            return;
        }

        var started = DateTime.Now;

        // The car's wear is made while the scene and its model load, so it comes with the car
        _ = WearLayer.WarmAsync(stage.CarDirectory, stage.Shown);

        var renderer = NewRenderer(null, scene);
        renderer.AutoAdjustTarget = false;

        // A car for sale stands with its lights off: its lamps are only more for the shader to go through
        renderer.LoadCarLights = false;
        renderer.TryToGuessCarLights = false;

        if (!await StartRendererAsync(renderer)) return;

        if (stage.Lighting is { } lighting)
        {
            SceneLights.Apply(renderer, lighting, stage.Sun is { Length: 3 } sun ? sun : [0.2f, 1f, 0.8f]);

            // What the reflections see past the sky's reach is the sky's colour, not the renderer's white
            renderer.BackgroundColor = System.Drawing.Color.FromArgb(lighting.AmbientUp[0], lighting.AmbientUp[1], lighting.AmbientUp[2]);

            // After dark the seller has a light on over the car, or there would be nothing to see
            if (lighting.Brightness < DarkBelow) renderer.AddLight(YardLight(stage));
        }

        var placement = Sx.Matrix.RotationY(stage.Heading * MathF.PI / 180f) * Sx.Matrix.Translation(stage.X, 0f, stage.Z);

        // The slot hands a matrix set while it was empty to the car it is given: set before and after
        renderer.MainSlot.LocalMatrix = placement;
        await TrackDeviceWork(renderer.MainSlot.SetCarAsync(CarDescription.FromDirectory(stage.CarDirectory),
            SkinOrDefault(stage.CarDirectory, stage.Skin), RendererToken));
        if (Renderer != renderer || !IsLoaded) return;
        await ShownPartsOnCar.SetAsync(renderer.CarNode, stage.Shown);
        if (Renderer != renderer || !IsLoaded) return;

        renderer.MainSlot.LocalMatrix = placement;
        renderer.RefreshBounds();

        // On the floor by its tyres as they are drawn: the model's own origin (and the data's word on it) differs from
        // mod to mod, and the floor of the showroom and the street is the ground plane
        if (LowestTyre(renderer) is { } low && Math.Abs(low) > 0.002f)
        {
            placement *= Sx.Matrix.Translation(0f, -low, 0f);
            renderer.MainSlot.LocalMatrix = placement;
            renderer.RefreshBounds();
        }

        AimAtCar(renderer, stage);
        _loaded = stage;
        _fadeInAfter = SettleFrames;
        OnPoseChanged();
        Invalidate();

        Logger.Information("Car viewing: {Car} up in {Ms} ms", Path.GetFileName(stage.CarDirectory), (int)(DateTime.Now - started).TotalMilliseconds);
    }

    /// <summary>How far the lowest of the car's tyres is over the ground, as it stands now; null without its wheels</summary>
    private static float? LowestTyre(GarageRenderer renderer)
    {
        if (renderer.CarNode is not { } car) return renderer.MainSlot.GetCarBoundingBox()?.Minimum.Y;

        float? low = null;
        foreach (var name in new[] { "WHEEL_LF", "WHEEL_RF", "WHEEL_LR", "WHEEL_RR" })
        {
            if (car.RootObject.GetDummyByName(name)?.BoundingBox is not { } box || !float.IsFinite(box.Minimum.Y)) return renderer.MainSlot.GetCarBoundingBox()?.Minimum.Y;
            low = Math.Min(low ?? float.MaxValue, box.Minimum.Y);
        }

        return low;
    }

    /// <summary>A lamp over the car, out on the side the camera comes round from</summary>
    private static AcTools.Render.Kn5SpecificForwardDark.Lights.DarkPointLight YardLight(CarViewingStage stage)
    {
        var alpha = PreviewFraming.CarCameraAlpha(stage.Heading, stage.CameraFromSide);
        return new AcTools.Render.Kn5SpecificForwardDark.Lights.DarkPointLight
        {
            Position = new Sx.Vector3(stage.X + MathF.Cos(alpha) * YardLightOut, YardLightHeight, stage.Z + MathF.Sin(alpha) * YardLightOut),
            Color = System.Drawing.Color.FromArgb(255, 214, 160),
            Brightness = YardLightBrightness,
            Range = YardLightRange,
            UseShadows = false,
            IsVisibleInUi = false
        };
    }

    protected override void OnRendererDisposed()
    {
        _loaded = null;
        _fadeInAfter = -1;
        _resume?.Stop();
    }

    #endregion

    #region Camera

    /// <summary>
    /// The camera on the car: at the middle of it, as far back as takes it in, coming round to its three-quarter from
    /// further round and further back
    /// </summary>
    private void AimAtCar(GarageRenderer renderer, CarViewingStage stage)
    {
        if (renderer.CameraOrbit is not { } orbit || renderer.CarNode is not { } car) return;

        var frame = FrameOf(car, renderer);
        // Over the middle of its length, a little under half its height above the road
        var middle = Sx.Vector3.TransformCoordinate(new Sx.Vector3(0f, 0f, frame.MidZ), car.Matrix);
        _target = new Sx.Vector3(middle.X, frame.Height * 0.42f, middle.Z);

        var fit = PreviewFraming.CarDistance(frame.Length, orbit.FovY, Math.Max(0.1f, renderer.AspectRatio));
        _radiusGoal = Math.Clamp(fit, MinRadius, MaxRadius(stage));
        _betaGoal = StartBeta;
        var alpha = PreviewFraming.CarCameraAlpha(stage.Heading, stage.CameraFromSide);
        _alphaGoal = alpha;
        _touched = false;
        _turnSpeed = 0f;

        orbit.MinY = FloorClearance;
        orbit.Target = _target;
        orbit.Alpha = alpha + GlideAlpha;
        orbit.Beta = StartBeta + 0.08f;
        orbit.Radius = Math.Min(_radiusGoal * GlideRadius, MaxRadius(stage));
    }

    private float MaxRadius(CarViewingStage stage) => Math.Max(MinRadius + 0.5f, stage.MaxRadius);

    /// <summary>The car's size from its wheels, in its own space; something car-sized when they cannot be found</summary>
    private static CarFrame FrameOf(Kn5RenderableCar car, GarageRenderer renderer)
    {
        // A node's Matrix is where it is in the scene, placement included: back into the car's own space
        var toCar = Sx.Matrix.Invert(car.Matrix);
        System.Numerics.Vector3? Hub(string name)
        {
            if (car.RootObject.GetDummyByName(name)?.Matrix is not { } m) return null;
            var local = Sx.Vector3.TransformCoordinate(new Sx.Vector3(m.M41, m.M42, m.M43), toCar);
            return new System.Numerics.Vector3(local.X, local.Y, local.Z);
        }

        if (Hub("WHEEL_LF") is not { } lf || Hub("WHEEL_RF") is not { } rf || Hub("WHEEL_LR") is not { } lr || Hub("WHEEL_RR") is not { } rr)
            return CarFrame.Nominal;

        // The box is in the scene's space, the road at nought: only its top is wanted
        var height = renderer.MainSlot.GetCarBoundingBox()?.Maximum.Y ?? CarFrame.Nominal.Height;
        return CarFrame.FromWheels(lf, rf, lr, rr, height) ?? CarFrame.Nominal;
    }

    /// <summary>Eases the camera to where it is meant to be; true while it still moves</summary>
    private bool StepCamera(float dt)
    {
        if (Renderer?.CameraOrbit is not { } orbit) return false;

        var k = 1f - MathF.Exp(-SettleRate * Math.Min(dt, GarageRenderer.MaxTickSeconds * 2f));
        var moving = false;

        if (_alphaGoal is { } alphaGoal)
        {
            var delta = (float)Math.IEEERemainder(alphaGoal - orbit.Alpha, Math.PI * 2);
            if (Math.Abs(delta) > Settled)
            {
                orbit.Alpha += delta * k;
                moving = true;
            }
            else
            {
                // In: the walk round takes over from the next frame, so the pump must not stop here
                _alphaGoal = null;
                moving = true;
            }
        }
        else if (_loaded != null)
        {
            if (!_touched)
            {
                // Eased in, from the glide's end and after the player's hand
                var step = Math.Min(dt, GarageRenderer.MaxTickSeconds * 2f);
                _turnSpeed += (TurnRate - _turnSpeed) * (1f - MathF.Exp(-TurnEase * step));
                orbit.Alpha += _turnSpeed * step;
                moving = true;
            }

            // While the player's hand rests nothing is drawn: the walk is woken again by ResumeLater
        }

        if (Math.Abs(_betaGoal - orbit.Beta) > Settled)
        {
            orbit.Beta += (_betaGoal - orbit.Beta) * k;
            moving = true;
        }

        if (Math.Abs(_radiusGoal - orbit.Radius) > Settled)
        {
            orbit.Radius += (_radiusGoal - orbit.Radius) * k;
            moving = true;
        }

        return moving;
    }

    #endregion

    #region Rendering

    protected override bool StepFrame(GarageRenderer renderer, float dt) => StepCamera(dt);

    protected override void OnFramePresented()
    {
        if (_fadeInAfter < 0) return;
        if (_fadeInAfter-- > 0) return;

        if (IsPictureHidden)
        {
            FadeIn();
            IsSettled = true;
        }
    }

    /// <summary>The car's running engine rocks it</summary>
    protected override void OnPoseChanged() => RockCar(_loaded != null ? Renderer?.CarNode : null);

    #endregion

    #region Input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragFrom = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragFrom = null;
        ResumeLater();
        ReleaseMouseCapture();
    }

    /// <summary>The walk round picks up again a while after the player lets go, unless they take hold again first</summary>
    private void ResumeLater()
    {
        if (!_touched) return;

        if (_resume == null)
        {
            _resume = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(ResumeAfterSeconds) };
            _resume.Tick += (_, _) =>
            {
                _resume.Stop();
                if (_dragFrom != null) return;

                _touched = false;
                Invalidate();
            };
        }

        _resume.Stop();
        _resume.Start();
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragFrom == null) return;

        // Let go somewhere else (another window took the mouse): the walk picks up as after any let-go
        _dragFrom = null;
        ResumeLater();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed || Renderer?.CameraOrbit is not { } orbit) return;

        var now = e.GetPosition(this);
        var (dx, dy) = (now.X - from.X, now.Y - from.Y);
        _dragFrom = now;
        if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return;

        // The player has the camera now: it is no longer brought round to the car, nor walked round it
        _alphaGoal = null;
        _touched = true;
        _turnSpeed = 0f;
        orbit.Alpha += (float)dx * DragPerPixel;
        _betaGoal = Math.Clamp(orbit.Beta + (float)dy * DragPerPixel, MinBeta, MaxBeta);
        orbit.Beta = _betaGoal;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_loaded is not { } stage) return;

        _radiusGoal = Math.Clamp(_radiusGoal * (1f - e.Delta * WheelStep), MinRadius, MaxRadius(stage));
        Invalidate();
        e.Handled = true;
    }

    #endregion
}
