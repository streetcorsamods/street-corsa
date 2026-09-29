using System.Windows;
using System.Windows.Input;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Controls.Street;
using StreetCorsa.Services.Street;
using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// What the street's and the gas station's viewports share: a scene seen from the driver's seat of the player's car.
/// This puts the scene up for the light of the day (its reflections past the sky's reach take the ambient's colour),
/// stands the player's car in it with its headlights on at night, seats the driver at its steering wheel
/// (<see cref="SceneLights.DriverEye"/>) under AC's own onboard lens, reads its gauges, aims the camera from the
/// driver's eyes, lets the mouse drag the view round, and fades the picture in once the new scene has settled.
///
/// Each screen keeps its own ways: where the car stands and what leans it, how far round the driver can look, what a
/// double click does (the station turns the head back over a moment, the street snaps the view back to the window),
/// and when the camera is aimed again (the station every frame, its car on the move; the street when the car rocks or
/// the viewport changes shape).
/// </summary>
public abstract class OnboardViewport3D : D3DViewportBase
{
    /// <summary>How far round the driver can look, degrees from straight ahead to the left</summary>
    protected const float MaxLookYaw = 160f;

    private const float MaxLookPitch = 20f;
    private const float LookDegreesPerPixel = 0.18f;

    // Frames drawn with the new scene before it is faded up: the first ones are still working out shadows and reflections
    private const int SettleFrames = 4;
    private static readonly TimeSpan SceneFadeOut = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan SceneFadeIn = TimeSpan.FromMilliseconds(900);

    private System.Windows.Point? _dragFrom;

    // Frames still to draw before the scene is faded up, or -1 when there is nothing to fade up
    private int _fadeInAfter = -1;

    protected OnboardViewport3D(string logCategory)
        : base(logCategory, new LabelLook(13, 0xD8, 0x90, new Thickness(8, 4, 8, 4), new Vector(16, 16)))
    {
        Cursor = System.Windows.Input.Cursors.Hand;

        // After the base's own: the scene is taken down, and the stage told, before the stage is let go of
        Loaded += (_, _) => ListenToStage(true);
        Unloaded += (_, _) => ListenToStage(false);
    }

    #region Dependency properties

    public static readonly DependencyProperty LightProperty = DependencyProperty.Register(
        nameof(Light), typeof(StreetLight), typeof(OnboardViewport3D),
        new PropertyMetadata(StreetLight.Day, (d, _) => ((OnboardViewport3D)d).RequestLoad()));

    /// <summary>Day, dusk or night: another light, and the scene's own sky or model for it</summary>
    public StreetLight Light
    {
        get => (StreetLight)GetValue(LightProperty);
        set => SetValue(LightProperty, value);
    }

    public static readonly DependencyProperty PlayerCarProperty = DependencyProperty.Register(
        nameof(PlayerCar), typeof(StreetCar), typeof(OnboardViewport3D),
        new PropertyMetadata(null, (d, _) => ((OnboardViewport3D)d).RequestLoad()));

    /// <summary>The car the player sits in</summary>
    public StreetCar? PlayerCar
    {
        get => (StreetCar?)GetValue(PlayerCarProperty);
        set => SetValue(PlayerCarProperty, value);
    }

    #endregion

    #region The driver

    /// <summary>AC's own onboard lens, as the player set it: degrees up and down</summary>
    protected float FieldOfView { get; private set; } = 56f;

    /// <summary>The driver's eyes in their car's own space</summary>
    protected Sx.Vector3 Eye { get; private set; } = SceneLights.FallbackEye;

    /// <summary>Where the driver looks: degrees from straight ahead, to the left positive</summary>
    protected float LookYaw { get; set; }

    /// <summary>Where the driver looks: degrees up from level</summary>
    protected float LookPitch { get; set; }

    /// <summary>How the player's car sits and where its driver looks, from its data (<see cref="AcCarFrame"/>)</summary>
    private protected AcCarFrame Frame { get; private set; } = new(SceneLights.FallbackEye, 0f);

    /// <summary>The player's dashboard needles and steering wheel; null while there is no car or they cannot be read</summary>
    private protected CarInstruments? Instruments { get; private set; }

    /// <summary>The shape of the viewport the camera was last aimed for</summary>
    protected float AimedAspect { get; private set; }

    /// <summary>How far round to the right the driver can look: degrees from straight ahead, to the left positive</summary>
    protected virtual float MinLookYaw => -MaxLookYaw;

    #endregion

    #region Loading

    /// <summary>
    /// The scene on screen faded out and taken down, and a new one started for <paramref name="kn5"/> in
    /// <paramref name="light"/>: headlights only at night, as by day a car's light sources are only more for the
    /// shader to go through. Null when the control was unloaded in the meantime.
    /// </summary>
    protected async Task<GarageRenderer?> StartSceneAsync(string kn5, StreetLight light)
    {
        if (Renderer != null && !IsPictureHidden)
        {
            await FadeOutAsync(SceneFadeOut);
            if (!IsLoaded) return null;
        }

        DisposeRenderer(releaseDevice: false);

        var renderer = NewRenderer(null, kn5);
        renderer.AutoAdjustTarget = false;
        renderer.LoadCarLights = light == StreetLight.Night;
        renderer.TryToGuessCarLights = light == StreetLight.Night;

        return await StartRendererAsync(renderer) ? renderer : null;
    }

    /// <summary>
    /// The scene lit for the light of the day. What the reflections see past the sky's reach (a chrome mirror) is the
    /// sky's colour, not the renderer's white.
    /// </summary>
    protected static void LightScene(GarageRenderer renderer, StreetLighting lighting, float[] sun)
    {
        SceneLights.Apply(renderer, lighting, sun);
        renderer.BackgroundColor = System.Drawing.Color.FromArgb(lighting.AmbientUp[0], lighting.AmbientUp[1], lighting.AmbientUp[2]);
    }

    /// <summary>
    /// Puts the player's car in the main slot and the driver in it: how it sits (<see cref="Frame"/>), where the eyes
    /// are, the lens, the seat's look down, the gauges, and the headlights at night. <paramref name="placementOf"/>
    /// stands the slot where the car goes, from how it sits; null leaves it where it is. False when the scene went
    /// while the car was loading.
    /// </summary>
    private protected async Task<bool> LoadPlayerCarAsync(GarageRenderer renderer, StreetCar player, StreetLight light, Func<AcCarFrame, Sx.Matrix>? placementOf)
    {
        var skin = SkinOrDefault(player.Directory, player.Skin);
        var data = SceneLights.DataOf(player.Directory, ex => Logger.Warning(ex, "The data of {Car} could not be read: it sits and looks as most cars do", player.Directory));
        var frame = SceneLights.FrameOf(data);
        var placement = placementOf?.Invoke(frame);

        // The slot hands a matrix set while it was empty to the car it is given: set before and after
        if (placement is { } before) renderer.MainSlot.LocalMatrix = before;
        await TrackDeviceWork(renderer.MainSlot.SetCarAsync(CarDescription.FromDirectory(player.Directory), skin, RendererToken));
        if (Renderer != renderer || !IsLoaded) return false;

        if (placement is { } after) renderer.MainSlot.LocalMatrix = after;
        renderer.MainSlot.ResetCarBoundingBox();
        if (renderer.CarNode is { } car && light == StreetLight.Night) car.HeadlightsEnabled = true;
        Instruments = CarInstruments.TryRead(renderer.CarNode, data, player.Directory, Logger);
        Frame = frame;

        // Read off the car as it stands, placement taken out
        if (placement != null) renderer.MainSlot.LocalMatrix = Sx.Matrix.Identity;
        Eye = SceneLights.DriverEye(renderer, frame);
        if (placement is { } again) renderer.MainSlot.LocalMatrix = again;

        FieldOfView = SceneLights.OnboardFieldOfView();
        LookPitch = SceneLights.SeatPitch;
        renderer.UseFpsCamera = true;
        return true;
    }

    /// <summary>The scene is up: its picture fades in once a few frames have drawn it</summary>
    protected void FadeInOnceSettled() => _fadeInAfter = SettleFrames;

    /// <summary>The screen's stage is listened to while the control is loaded, and let go of while it is not</summary>
    protected abstract void ListenToStage(bool listen);

    protected override void OnRendererDisposed()
    {
        Instruments = null;
        _fadeInAfter = -1;
    }

    #endregion

    #region Camera

    /// <summary>
    /// Aims the camera from the driver's eyes where they look: the car standing at <paramref name="placement"/>, its
    /// body leaning by <paramref name="lean"/> on top, in world space
    /// </summary>
    protected void AimCamera(GarageRenderer renderer, Sx.Matrix placement, Sx.Matrix lean)
    {
        if (renderer.Camera is not { } camera) return;

        var yaw = LookYaw * MathF.PI / 180f;
        var pitch = LookPitch * MathF.PI / 180f;

        // In the car's own space: +z ahead, +x to the left
        var look = new Sx.Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        var eye = Sx.Vector3.TransformCoordinate(Sx.Vector3.TransformCoordinate(Eye, placement), lean);
        var direction = Sx.Vector3.TransformNormal(Sx.Vector3.TransformNormal(look, placement), lean);
        var up = Sx.Vector3.TransformNormal(Sx.Vector3.TransformNormal(Sx.Vector3.UnitY, placement), lean);

        camera.FovY = FieldOfView * MathF.PI / 180f;
        camera.LookAt(eye, eye + direction, up);
        camera.SetLens(renderer.AspectRatio);
        AimedAspect = renderer.AspectRatio;
        renderer.IsDirty = true;
    }

    /// <summary>A double click: the driver looks back where they would</summary>
    protected abstract void LookBack();

    /// <summary>The view was dragged round to <see cref="LookYaw"/> and <see cref="LookPitch"/>: the camera goes with it</summary>
    protected abstract void OnLookDragged();

    /// <summary>Dragging looks round; a double click looks back</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            LookBack();
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
        LookYaw = Math.Clamp(LookYaw + (float)(from.X - now.X) * LookDegreesPerPixel, MinLookYaw, MaxLookYaw);
        LookPitch = Math.Clamp(LookPitch + (float)(from.Y - now.Y) * LookDegreesPerPixel, -MaxLookPitch, MaxLookPitch);
        _dragFrom = now;

        OnLookDragged();
        Invalidate();
    }

    #endregion

    #region Rendering

    protected override void OnFramePresented()
    {
        if (_fadeInAfter < 0) return;
        if (_fadeInAfter-- > 0) return;

        if (IsPictureHidden) FadeIn(SceneFadeIn);
    }

    #endregion
}
