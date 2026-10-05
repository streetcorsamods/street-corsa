using System.Windows;
using System.Windows.Input;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// One part alone, with whatever is bolted on it, on a studio floor: the part ads' preview. The part stands in the
/// middle, framed by its own size and lit from above; it turns slowly by itself until the player takes hold of it, then
/// dragging turns it round and the wheel brings it closer.
///
/// The renderer is the garage's, with no car and no room: the part goes in as its scenery (always drawn, never picked,
/// its textures in before the first frame), and the Dark renderer's own ground takes its shadow. A part with nothing
/// to show leaves the viewport empty and says so (<see cref="HasNothingToShow"/>): its host shows words instead.
/// </summary>
public class PartViewport3D : D3DViewportBase
{
    // The studio: a dark warm backdrop in the game's colours, a key light from almost overhead (the part's shadow falls
    // under it, where it is seen), a soft fill
    private static readonly System.Drawing.Color Backdrop = System.Drawing.Color.FromArgb(30, 28, 26);
    private static readonly Sx.Vector3 KeyLight = new(-0.3f, 1f, -0.35f);
    private const float KeyBrightness = 2.2f;
    private static readonly System.Drawing.Color KeyColor = System.Drawing.Color.FromArgb(255, 244, 228);
    private const float FillBrightness = 1.4f;
    private static readonly System.Drawing.Color FillUp = System.Drawing.Color.FromArgb(150, 150, 160);
    private static readonly System.Drawing.Color FillDown = System.Drawing.Color.FromArgb(90, 80, 70);

    // Camera feel: where it starts (off the part's front three-quarter, a little above), how it turns by itself, how it
    // follows the pointer and the wheel
    private const float StartAlpha = -MathF.PI / 2f - 0.75f;
    private const float StartBeta = 0.36f;
    private const float AutoTurnPerSecond = 0.22f;
    private const float DragPerPixel = 0.01f;
    private const float MinBeta = -0.15f;
    private const float MaxBeta = 1.35f;
    private const float WheelStep = 0.0008f;
    private const float ZoomSettleRate = 8f;

    private PartInstance? _shown;
    private PartsCatalog? _shownCatalog;
    private Sx.Vector3 _target;
    private float _fitDistance = 1f;
    private float _radiusGoal = 1f;
    private bool _touched;
    private System.Windows.Point? _dragFrom;

    public PartViewport3D()
        : base("PartPreview", new LabelLook(13, 0xD8, 0x90, new Thickness(8, 4, 8, 4), new Vector(16, 16)))
    {
        Cursor = System.Windows.Input.Cursors.SizeAll;
    }

    protected override string ViewportName => "Part viewport";

    #region Dependency properties

    public static readonly DependencyProperty PartProperty = DependencyProperty.Register(
        nameof(Part), typeof(PartInstance), typeof(PartViewport3D),
        new PropertyMetadata(null, (d, _) => ((PartViewport3D)d).RequestLoad()));

    /// <summary>The part on show, with what is bolted on it</summary>
    public PartInstance? Part
    {
        get => (PartInstance?)GetValue(PartProperty);
        set => SetValue(PartProperty, value);
    }

    public static readonly DependencyProperty CatalogProperty = DependencyProperty.Register(
        nameof(Catalog), typeof(PartsCatalog), typeof(PartViewport3D),
        new PropertyMetadata(null, (d, _) => ((PartViewport3D)d).RequestLoad()));

    /// <summary>Where the part's model is found</summary>
    public PartsCatalog? Catalog
    {
        get => (PartsCatalog?)GetValue(CatalogProperty);
        set => SetValue(CatalogProperty, value);
    }

    private static readonly DependencyPropertyKey HasNothingToShowPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasNothingToShow), typeof(bool), typeof(PartViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty HasNothingToShowProperty = HasNothingToShowPropertyKey.DependencyProperty;

    /// <summary>The part has no model that reads: there is no picture of it</summary>
    public bool HasNothingToShow
    {
        get => (bool)GetValue(HasNothingToShowProperty);
        private set => SetValue(HasNothingToShowPropertyKey, value);
    }

    #endregion

    #region Loading

    protected override bool IsUpToDate => ReferenceEquals(_shown, Part) && ReferenceEquals(_shownCatalog, Catalog) && (Renderer != null || HasNothingToShow || Part == null);

    protected override string LoadDescription => Part?.DefinitionId ?? "";

    protected override async Task LoadAsync()
    {
        var part = Part;
        var catalog = Catalog;
        DisposeRenderer(releaseDevice: false);
        HasNothingToShow = false;

        if (part == null || catalog == null)
        {
            (_shown, _shownCatalog) = (part, catalog);
            return;
        }

        var model = await Task.Run(() => PartPreviewModel.Build(catalog, part));
        if (!IsLoaded || !ReferenceEquals(part, Part)) return;

        if (model == null)
        {
            Logger.Information("{Part} has no model to show", part.DefinitionId);
            (_shown, _shownCatalog) = (part, catalog);
            HasNothingToShow = true;
            return;
        }

        var renderer = NewRenderer(null, null);
        renderer.AutoAdjustTarget = false;
        renderer.LoadCarLights = false;
        renderer.TryToGuessCarLights = false;
        renderer.BackgroundColor = Backdrop;
        renderer.Light = KeyLight;
        renderer.LightBrightness = KeyBrightness;
        renderer.LightColor = KeyColor;
        renderer.AmbientBrightness = FillBrightness;
        renderer.AmbientUp = FillUp;
        renderer.AmbientDown = FillDown;
        renderer.CubemapAmbient = 0f;

        // The renderer's own ground, in the backdrop's colour: no horizon, only the part's shadow on it
        renderer.FlatMirror = false;

        if (!await StartRendererAsync(renderer)) return;

        renderer.SetScenery(model.Kn5);

        var min = new System.Numerics.Vector3(0f, 0f, 0f);
        var max = model.Max - model.Min;
        var target = PreviewFraming.PartTarget(min, max);
        var radius = PreviewFraming.BoundingRadius(min, max);
        _target = new Sx.Vector3(target.X, target.Y, target.Z);

        if (renderer.CameraOrbit is { } orbit)
        {
            _fitDistance = PreviewFraming.PartDistance(max - min, orbit.FovY, Math.Max(0.1f, renderer.AspectRatio), StartBeta);
            orbit.NearZ = PreviewFraming.NearPlane(radius);
            orbit.Target = _target;
            orbit.Alpha = StartAlpha;
            orbit.Beta = StartBeta;
            orbit.Radius = _fitDistance;
        }

        _radiusGoal = _fitDistance;
        _touched = false;
        (_shown, _shownCatalog) = (part, catalog);
        renderer.RefreshShadows();
        FadeInAfterSettled();

        Logger.Information("Part preview of {Part} up, {Size:0.00} m across", part.DefinitionId, radius * 2f);
        Invalidate();
    }

    protected override void OnRendererDisposed()
    {
        _shown = null;
        _shownCatalog = null;
    }

    #endregion

    #region Rendering

    protected override bool StepFrame(GarageRenderer renderer, float dt)
    {
        if (renderer.CameraOrbit is not { } orbit) return false;

        var moving = false;
        if (!_touched)
        {
            orbit.Alpha += AutoTurnPerSecond * Math.Min(dt, GarageRenderer.MaxTickSeconds);
            moving = true;
        }

        if (Math.Abs(orbit.Radius - _radiusGoal) > _radiusGoal * 0.002f)
        {
            orbit.Radius += (_radiusGoal - orbit.Radius) * (1f - MathF.Exp(-ZoomSettleRate * dt));
            moving = true;
        }
        else
        {
            orbit.Radius = _radiusGoal;
        }

        if (moving) renderer.IsDirty = true;
        return moving;
    }

    /// <summary>Nothing rocks a part</summary>
    protected override void OnPoseChanged() { }

    #endregion

    #region Input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _touched = true;
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
        if (_dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed || Renderer?.CameraOrbit is not { } orbit) return;

        var now = e.GetPosition(this);
        orbit.Alpha += (float)(now.X - from.X) * DragPerPixel;
        orbit.Beta = Math.Clamp(orbit.Beta + (float)(now.Y - from.Y) * DragPerPixel, MinBeta, MaxBeta);
        _dragFrom = now;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Renderer == null) return;

        _touched = true;
        _radiusGoal = Math.Clamp(_radiusGoal * (1f - e.Delta * WheelStep),
            _fitDistance * PreviewFraming.PartZoomIn, _fitDistance * PreviewFraming.PartZoomOut);
        Invalidate();
        e.Handled = true;
    }

    #endregion
}
