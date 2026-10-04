using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using AcTools.Render.Kn5SpecificForwardDark.Lights;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Services.PartShops;
using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>One part of a shop's stock to stand in it: a part off a shelf, or a used part out of the yard with what is on it</summary>
public sealed record ShopStockPart(PartDefinition Part, PartInstance? Tree = null);

/// <summary>
/// The shop's stock as one model, each part stood in its place (<see cref="ShopStockLayout"/>): read off the disk, so
/// built off the UI thread
/// </summary>
public static class ShopStockModel
{
    /// <summary>The stock in its places; null when nothing of it has a model, or nothing finds a place</summary>
    public static AssemblyModel? Build(PartsCatalog catalog, IReadOnlyList<ShopStockPart> stock, IReadOnlyList<ShopPlace> places)
    {
        var items = new List<ShopShownItem>();
        var models = new Dictionary<string, List<PlacedPart>>(StringComparer.Ordinal);
        foreach (var (part, index) in stock.Select((p, i) => (p, i)))
        {
            var placed = part.Tree != null
                ? PartPreviewModel.Placed(catalog, part.Tree, Matrix4x4.Identity)
                : catalog.GetModelPath(part.Part) is { } path && File.Exists(path) ? [new PlacedPart(part.Part, Matrix4x4.Identity)] : [];
            if (placed.Count == 0 || BoundsOf(catalog, placed) is not { } box) continue;

            var key = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            models[key] = placed;
            items.Add(new ShopShownItem(key, PartKinds.GroupOf(part.Part), box.Min, box.Max));
        }

        var standing = ShopStockLayout.Place(items, places);
        var all = standing.SelectMany(s => models[s.Item.Key].Select(p => p with { World = p.World * s.World })).ToList();
        if (all.Count == 0) return null;

        try
        {
            return PartAssembler.BuildModel(catalog, "stock", all);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The box round parts bolted together as they stand, from each one's own box; null when none has one</summary>
    private static (Vector3 Min, Vector3 Max)? BoundsOf(PartsCatalog catalog, IEnumerable<PlacedPart> placed)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in placed)
        {
            if (PartModelBounds.Of(catalog, part.Part) is not { } box) continue;
            for (var corner = 0; corner < 8; corner++)
            {
                var p = Vector3.Transform(new Vector3((corner & 1) == 0 ? box.Min.X : box.Max.X, (corner & 2) == 0 ? box.Min.Y : box.Max.Y,
                    (corner & 4) == 0 ? box.Min.Z : box.Max.Z), part.World);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        return min.X <= max.X ? (min, max) : null;
    }
}

/// <summary>
/// Inside a part shop: the room (<see cref="SceneKn5"/>) dressed for the kind of shop it is (<see cref="Dressing"/>), the
/// shop's own stock stood on its shelves, in its rack and on its stand (<see cref="Stock"/>), seen from just inside the
/// door. The camera sways a little by itself until the player takes hold of it: dragging looks round, the wheel steps in.
///
/// The renderer is the garage's: the room goes in as its showroom, the stock as its scenery.
/// </summary>
public class ShopViewport3D : D3DViewportBase
{
    // How far the player may look round, from the view the shop opens on, and how near the wheel brings the camera
    private const float MaxSwing = 0.9f;
    private const float MinBeta = -0.15f;
    private const float MaxBeta = 0.7f;
    private const float DragPerPixel = 0.005f;
    private const float WheelStep = 0.0008f;
    private const float NearestShare = 0.45f;

    // The sway the camera has by itself: so far either way, a round in so many seconds
    private const float SwayAlpha = 0.16f;
    private const float SwayBeta = 0.03f;
    private const float SwaySeconds = 40f;

    private const float SettleRate = 6f;

    // How far short of a wall or the ceiling the camera stops
    private const float WallMargin = 0.35f;

    // Frames drawn before the picture is faded up
    private const int SettleFrames = 4;

    private readonly List<DarkLightBase> _lamps = [];
    private string? _loadedScene;
    private IReadOnlyList<ShopStockPart>? _loadedStock;
    private int _stockBuild;
    private float _time;
    private bool _touched;
    private float _alpha;
    private float _beta;
    private float _radius;
    private float _alphaGoal;
    private float _betaGoal;
    private float _radiusGoal;
    private System.Windows.Point? _dragFrom;
    private int _fadeInAfter = -1;

    public ShopViewport3D()
        : base("ShopViewport", new LabelLook(13, 0xD8, 0x90, new Thickness(8, 4, 8, 4), new System.Windows.Vector(16, 16)))
    {
        Cursor = System.Windows.Input.Cursors.SizeAll;
    }

    protected override string ViewportName => "Shop viewport";

    #region Dependency properties

    public static readonly DependencyProperty SceneKn5Property = DependencyProperty.Register(
        nameof(SceneKn5), typeof(string), typeof(ShopViewport3D), new PropertyMetadata(null, (d, _) => ((ShopViewport3D)d).RequestLoad()));

    /// <summary>Full path to the shop's room</summary>
    public string? SceneKn5
    {
        get => (string?)GetValue(SceneKn5Property);
        set => SetValue(SceneKn5Property, value);
    }

    public static readonly DependencyProperty StockProperty = DependencyProperty.Register(
        nameof(Stock), typeof(IReadOnlyList<ShopStockPart>), typeof(ShopViewport3D), new PropertyMetadata(null, (d, _) => ((ShopViewport3D)d).RequestLoad()));

    /// <summary>What the shop has, to stand in its places; null until the screen knows (the room waits for it), empty for none</summary>
    public IReadOnlyList<ShopStockPart>? Stock
    {
        get => (IReadOnlyList<ShopStockPart>?)GetValue(StockProperty);
        set => SetValue(StockProperty, value);
    }

    public static readonly DependencyProperty CatalogProperty = DependencyProperty.Register(
        nameof(Catalog), typeof(PartsCatalog), typeof(ShopViewport3D), new PropertyMetadata(null, (d, _) => ((ShopViewport3D)d).RequestLoad()));

    /// <summary>Where the parts' models are found</summary>
    public PartsCatalog? Catalog
    {
        get => (PartsCatalog?)GetValue(CatalogProperty);
        set => SetValue(CatalogProperty, value);
    }

    public static readonly DependencyProperty PlacesProperty = DependencyProperty.Register(
        nameof(Places), typeof(IReadOnlyList<ShopPlace>), typeof(ShopViewport3D), new PropertyMetadata(null, (d, _) => ((ShopViewport3D)d).RequestLoad()));

    /// <summary>Where the stock stands</summary>
    public IReadOnlyList<ShopPlace>? Places
    {
        get => (IReadOnlyList<ShopPlace>?)GetValue(PlacesProperty);
        set => SetValue(PlacesProperty, value);
    }

    public static readonly DependencyProperty DressingProperty = DependencyProperty.Register(
        nameof(Dressing), typeof(LotDressing), typeof(ShopViewport3D), new PropertyMetadata(null, (d, _) => ((ShopViewport3D)d).ApplyDressing()));

    /// <summary>The shop's look and the light of the hour</summary>
    public LotDressing? Dressing
    {
        get => (LotDressing?)GetValue(DressingProperty);
        set => SetValue(DressingProperty, value);
    }

    public static readonly DependencyProperty ViewProperty = DependencyProperty.Register(
        nameof(View), typeof(ShopCamera?), typeof(ShopViewport3D), new PropertyMetadata(null, (d, _) => ((ShopViewport3D)d).ResetCamera(immediate: false)));

    /// <summary>The view the shop opens on</summary>
    public ShopCamera? View
    {
        get => (ShopCamera?)GetValue(ViewProperty);
        set => SetValue(ViewProperty, value);
    }

    public static readonly DependencyProperty RoomProperty = DependencyProperty.Register(
        nameof(Room), typeof(ShopRoom?), typeof(ShopViewport3D), new PropertyMetadata(null));

    /// <summary>The walls the camera stays inside; none: it goes where it is swung</summary>
    public ShopRoom? Room
    {
        get => (ShopRoom?)GetValue(RoomProperty);
        set => SetValue(RoomProperty, value);
    }

    // A scene that gives no view is looked into from its door, at its middle
    private ShopCamera ViewOrDefault => View ?? new ShopCamera(MathF.PI / 2f, 0.1f, 5f, 0f, 1.2f, 0f);

    private static readonly DependencyPropertyKey IsShopReadyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsShopReady), typeof(bool), typeof(ShopViewport3D), new PropertyMetadata(false));

    public static readonly DependencyProperty IsShopReadyProperty = IsShopReadyPropertyKey.DependencyProperty;

    /// <summary>The shop is worth looking at: the room is up with its stock in it</summary>
    public bool IsShopReady
    {
        get => (bool)GetValue(IsShopReadyProperty);
        private set => SetValue(IsShopReadyPropertyKey, value);
    }

    #endregion

    #region Loading

    protected override bool IsUpToDate =>
        Renderer != null && SceneKn5 == _loadedScene && ReferenceEquals(Stock, _loadedStock) || SceneKn5 == null && Renderer == null;

    protected override string LoadDescription => SceneKn5 ?? "";

    protected override async Task LoadAsync()
    {
        var scene = SceneKn5;
        if (scene == null || !File.Exists(scene))
        {
            if (scene != null) Logger.Warning("No shop scene at {Scene}", scene);
            DisposeRenderer(releaseDevice: false);
            _loadedScene = null;
            return;
        }

        // The same room with other stock (a part bought): only the stock is stood again
        if (Renderer != null && scene == _loadedScene)
        {
            await StandStockAsync();
            return;
        }

        DisposeRenderer(releaseDevice: false);
        IsShopReady = false;

        var started = DateTime.Now;
        var renderer = NewRenderer(null, scene);
        renderer.LoadCarLights = false;
        renderer.TryToGuessCarLights = false;
        renderer.AutoAdjustTarget = false;
        renderer.FlatMirror = false;
        if (!await StartRendererAsync(renderer)) return;

        _loadedScene = scene;
        ApplyDressing();
        ResetCamera(immediate: true);
        Logger.Information("Shop room up in {Ms} ms", (int)(DateTime.Now - started).TotalMilliseconds);

        await StandStockAsync();
    }

    /// <summary>The stock built into one model off the UI thread and stood in the room; the last asked for wins</summary>
    private async Task StandStockAsync()
    {
        var stock = Stock;
        var catalog = Catalog;
        var places = Places;
        var build = ++_stockBuild;
        _loadedStock = stock;

        AssemblyModel? model = null;
        if (stock is { Count: > 0 } && catalog != null && places is { Count: > 0 })
        {
            var started = DateTime.Now;
            try
            {
                model = await Task.Run(() => ShopStockModel.Build(catalog, stock, places));
            }
            catch (Exception ex)
            {
                // A shop with bare shelves is still a shop
                Logger.Warning("The shop's stock could not be stood on its shelves: {Error}", ex.Message);
            }

            Logger.Information("Shop stock of {Count} stood in {Ms} ms", stock.Count, (int)(DateTime.Now - started).TotalMilliseconds);
        }

        if (build != _stockBuild || Renderer is not { } renderer) return;

        renderer.SetScenery(model?.Kn5);
        renderer.RefreshShadows();
        // Shown once the screen has said what the shop has (none at all is an answer too): never bare shelves filling up
        if (stock != null) _fadeInAfter = SettleFrames;
        Invalidate();
    }

    private void ApplyDressing()
    {
        if (Renderer is not { } renderer || Dressing is not { } dressing || SceneKn5 != _loadedScene) return;
        SceneDressing.Apply(renderer, dressing, _lamps, Logger);
        Invalidate();
    }

    protected override void OnRendererDisposed()
    {
        _lamps.Clear();
        _loadedScene = null;
        _loadedStock = null;
        _fadeInAfter = -1;
        IsShopReady = false;
    }

    protected override void OnFramePresented()
    {
        if (_fadeInAfter < 0) return;
        if (_fadeInAfter-- > 0) return;

        IsShopReady = true;
        if (IsPictureHidden) FadeIn();
    }

    #endregion

    #region Camera

    private void ResetCamera(bool immediate)
    {
        var view = ViewOrDefault;
        _alphaGoal = view.Alpha;
        _betaGoal = view.Beta;
        _radiusGoal = view.Radius;
        _touched = false;
        if (immediate || Renderer == null)
        {
            (_alpha, _beta, _radius) = (_alphaGoal, _betaGoal, _radiusGoal);
            Place();
        }

        Invalidate();
    }

    protected override bool StepFrame(GarageRenderer renderer, float dt)
    {
        dt = Math.Min(dt, GarageRenderer.MaxTickSeconds);
        _time += dt;

        // Untouched, the view sways round where it looks from, as somebody standing there looks about
        var view = ViewOrDefault;
        if (!_touched)
        {
            var phase = _time * 2f * MathF.PI / SwaySeconds;
            _alphaGoal = view.Alpha + SwayAlpha * MathF.Sin(phase);
            _betaGoal = view.Beta + SwayBeta * MathF.Sin(phase * 1.7f);
        }

        var k = 1f - MathF.Exp(-SettleRate * dt);
        _alpha += (_alphaGoal - _alpha) * k;
        _beta += (_betaGoal - _beta) * k;
        _radius += (_radiusGoal - _radius) * k;
        Place();
        renderer.IsDirty = true;
        return true;
    }

    /// <summary>The camera where the angles say, pulled in short of the walls and the ceiling</summary>
    private void Place()
    {
        if (Renderer?.CameraOrbit is not { } orbit) return;

        var view = ViewOrDefault;
        var target = new Sx.Vector3(view.TargetX, view.TargetHeight, view.TargetZ);
        var direction = new Vector3(MathF.Cos(_beta) * MathF.Cos(_alpha), MathF.Sin(_beta), MathF.Cos(_beta) * MathF.Sin(_alpha));
        var radius = _radius;
        if (Room is { } room)
        {
            // The farthest out the room lets it stand, along the way it looks
            static float Limit(float from, float along, float half) =>
                MathF.Abs(along) < 1e-4f ? float.MaxValue : ((along > 0 ? half : -half) - from) / along;
            radius = Math.Min(radius, Limit(target.X, direction.X, room.HalfWidth - WallMargin));
            radius = Math.Min(radius, Limit(target.Z, direction.Z, room.HalfDepth - WallMargin));
            if (direction.Y > 1e-4f) radius = Math.Min(radius, (room.Height - WallMargin - target.Y) / direction.Y);
            if (direction.Y < -1e-4f) radius = Math.Min(radius, (WallMargin - target.Y) / direction.Y);
            radius = Math.Max(radius, 0.5f);
        }

        if (view.Fov > 0f) orbit.FovY = view.Fov * MathF.PI / 180f;
        orbit.Target = target;
        orbit.Alpha = _alpha;
        orbit.Beta = _beta;
        orbit.Radius = radius;
    }

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
        if (_dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed) return;

        var view = ViewOrDefault;
        var now = e.GetPosition(this);
        _alphaGoal = Math.Clamp(_alphaGoal + (float)(now.X - from.X) * DragPerPixel, view.Alpha - MaxSwing, view.Alpha + MaxSwing);
        _betaGoal = Math.Clamp(_betaGoal + (float)(now.Y - from.Y) * DragPerPixel, MinBeta, MaxBeta);
        _dragFrom = now;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var view = ViewOrDefault;
        _touched = true;
        _radiusGoal = Math.Clamp(_radiusGoal * (1f - e.Delta * WheelStep), view.Radius * NearestShare, view.Radius);
        Invalidate();
        e.Handled = true;
    }

    /// <summary>Nothing rocks a shop</summary>
    protected override void OnPoseChanged() { }

    #endregion
}
