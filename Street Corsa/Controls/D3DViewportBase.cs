using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Controls.Scripting;
using StreetCorsa.Logging;
using StreetCorsa.Services.Scripting;
using StreetCorsa.Services.Station;

namespace StreetCorsa.Controls;

/// <summary>
/// What every AcTools viewport on a WPF screen has in common: the renderer draws off-screen into a shared DX11
/// texture, a <see cref="SharedTextureBridge"/> hands it to a D3DImage, and the control composes like any other WPF
/// element. This owns the life of the renderer and the bridge (load, resize, DPI, the render pump, disposal, failure
/// and recovery from a lost device), the read-only state a screen binds to, the label next to the pointer and the
/// plumbing that rocks a car with its running engine.
///
/// What each viewport does with its scene stays with it. So do a few deliberate differences: the garage keeps drawing
/// while its car turns by itself and fades in on its first frame; the lot holds its picture back until every car is
/// standing; the lot keeps its D3D9 device across a change of showroom; only the garage leans its parts with the body.
/// </summary>
public abstract class D3DViewportBase : System.Windows.Controls.Grid
{
    /// <summary>
    /// D3DImage can drop the first frame after a back buffer swap, and the shared surface is read without GPU sync,
    /// so a lone frame may never show up. Keep drawing briefly after every change instead.
    /// </summary>
    protected static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(300);

    protected static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(600);

    // Frames drawn with a new scene before it is faded up: the first ones are still working out shadows and reflections
    private const int SettleFrames = 4;

    // A drag-resize is dozens of sizes a second. Each one would make the renderer new targets and the bridge a new
    // shared texture, so only the size the drag ends on is applied (the picture stretches meanwhile).
    private static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(100);

    // While nothing moves the frame clock is let go. The renderer can still mark itself dirty from the inside (a
    // texture that finished loading), which is looked for this often.
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(100);

    // A scene rebuilt after a lost device that still draws this long after is sound again: a later loss (the next
    // driver update, hours on) is rebuilt once more rather than failed
    private static readonly TimeSpan RecoveryGrace = TimeSpan.FromMinutes(1);

    // How long a started renderer may go without a front buffer to draw to. Under software rendering or a remote
    // session D3DImage never offers one, and neither Ready nor Failed would come: the 2D fallback shows instead.
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(5);

    // A GPU that went away under the renderer: a driver update or reset (TDR), or a hung device. DXGI's own codes
    // (DEVICE_REMOVED, DEVICE_HUNG, DEVICE_RESET, DRIVER_INTERNAL_ERROR) and D3D9's (DEVICELOST, DEVICEREMOVED, DEVICEHUNG)
    private static readonly HashSet<int> DeviceLostCodes =
    [
        unchecked((int)0x887A0005), unchecked((int)0x887A0006), unchecked((int)0x887A0007), unchecked((int)0x887A0020),
        unchecked((int)0x88760868), unchecked((int)0x88760870), unchecked((int)0x88760874)
    ];

    private readonly System.Windows.Controls.Image _image;
    private readonly SharedTextureBridge _bridge = new();
    private readonly System.Windows.Controls.Border _label;
    private readonly System.Windows.Controls.TextBlock _labelText;
    private readonly Vector _labelOffset;
    private readonly DispatcherTimer _resizeTimer;
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _firstFrameTimer;

    private GarageRenderer? _renderer;

    // Device calls a car load is still making on the current renderer: it is disposed only once they are done
    private Task _deviceWork = Task.CompletedTask;

    // Cancelled as the current renderer goes: a car still loading into it stops at its next step rather than
    // finishing a model nobody will see while the old renderer waits on it (see TrackDeviceWork)
    private CancellationTokenSource _rendererGone = new();

    private bool _isLoading;
    private bool _reloadRequested;
    private bool _failed;
    private bool _recovered;
    private DateTime _recoveredAt;
    private bool _pumping;
    private TimeSpan _lastRenderingTime;
    private DateTime _animateUntil = DateTime.MinValue;

    // Frames still to draw before the picture is faded up, or -1 when there is nothing to fade up; how long the fade
    // takes, and who hears the scene has settled (FadeInAfterSettled)
    private int _fadeInAfter = -1;
    private TimeSpan _fadeInFor;
    private Action? _settled;

    private Audio.EngineRunner? _poseSource;
    private CarBodyRock? _rock;

    // Kicks to the body that ring out on the springs, by how long they have been going (less than 0 while they wait):
    // +1 lifts the body (an engine coming out), -1 drops it (one going in); the nose goes with it for an engine in the
    // front, against it for one behind the driver (Front false). They go by the frames the renderer moves the parts by,
    // not by the clock, so a bump lands with the engine however slowly the frames come.
    private readonly List<(float Age, float Sign, bool Front)> _bumps = new();

    // A car moving by a script (the lot's drive-off, the garage's drive-in): the script, the car and where it stood (in
    // its parent's space, and the way from there to the world), and how the camera followed the car before the script
    // took that over
    private ScriptRun? _run;
    private ScriptedCar? _scripted;
    private Kn5RenderableCar? _scriptedNode;
    private SlimDX.Matrix _scriptFrom = SlimDX.Matrix.Identity;
    private SlimDX.Matrix _scriptParent = SlimDX.Matrix.Identity;
    private SlimDX.Matrix _scriptParentInverse = SlimDX.Matrix.Identity;
    private bool _scriptAutoAdjust;
    private bool _follow;

    /// <summary>How the label next to the pointer looks, and where it sits from the pointer</summary>
    protected readonly record struct LabelLook(double FontSize, byte BackgroundAlpha, byte BorderAlpha, Thickness Padding, Vector Offset);

    protected D3DViewportBase(string logCategory, LabelLook look)
    {
        Logger = AppLoggerFactory.CreateLogger(logCategory);

        Background = System.Windows.Media.Brushes.Transparent;
        ClipToBounds = true;
        Focusable = false;

        _image = new System.Windows.Controls.Image
        {
            Source = _bridge.Image,
            Stretch = Stretch.Fill,
            Opacity = 0
        };
        Children.Add(_image);

        _labelText = new System.Windows.Controls.TextBlock
        {
            Foreground = new SolidColorBrush(Styles.Theme.Color("PointerLabelText")),
            FontSize = look.FontSize,
            FontWeight = FontWeights.SemiBold
        };
        _label = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Styles.Theme.Color("PointerLabelGlass", look.BackgroundAlpha)),
            BorderBrush = new SolidColorBrush(Styles.Theme.Color("PointerLabelEdge", look.BorderAlpha)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = look.Padding,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _labelText
        };
        _labelOffset = look.Offset;
        Children.Add(_label);

        _resizeTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = ResizeDelay };
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            UpdateRendererSize();
        };

        _idleTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = IdlePollInterval };
        _idleTimer.Tick += OnIdlePoll;

        _firstFrameTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = FirstFrameTimeout };
        _firstFrameTimer.Tick += OnFirstFrameTimeout;

        Loaded += (_, _) =>
        {
            SetPoseSource(RunningEngine);
            RequestLoad();
        };
        Unloaded += (_, _) =>
        {
            SetPoseSource(null);
            DisposeRenderer(releaseDevice: true);

            // A failure is the failure of that visit: the next time the control is shown it tries again
            _failed = false;
            _recovered = false;
            HasFailed = false;
        };
        IsVisibleChanged += (_, _) =>
        {
            RequestLoad();
            if (IsVisible) Invalidate();
            else StopPump(idle: false);
        };
        SizeChanged += (_, _) =>
        {
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };
        _bridge.FrontBufferRestored += (_, _) => Invalidate();
    }

    protected IAppLogger Logger { get; }

    /// <summary>The renderer on screen; null while there is none</summary>
    protected GarageRenderer? Renderer => _renderer;

    /// <summary>What this viewport is called in the log</summary>
    protected abstract string ViewportName { get; }

    /// <summary>Raised once when the first 3D frame is on screen</summary>
    public event EventHandler? Ready;

    /// <summary>Raised if the renderer could not be started or crashed</summary>
    public event EventHandler? Failed;

    /// <summary>Raised when the viewport is shown with nothing to load: neither Ready nor Failed will come</summary>
    public event EventHandler? NothingToShow;

    #region Dependency properties

    public static readonly DependencyProperty RunningEngineProperty = DependencyProperty.Register(
        nameof(RunningEngine), typeof(Audio.EngineRunner), typeof(D3DViewportBase),
        new PropertyMetadata(null, (d, e) =>
            ((D3DViewportBase)d).OnRunningEngineChanged((Audio.EngineRunner?)e.OldValue, (Audio.EngineRunner?)e.NewValue)));

    /// <summary>The engine of the car on show, started where it stands: the body rocks with it</summary>
    public Audio.EngineRunner? RunningEngine
    {
        get => (Audio.EngineRunner?)GetValue(RunningEngineProperty);
        set => SetValue(RunningEngineProperty, value);
    }

    private static readonly DependencyPropertyKey IsReadyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsReady), typeof(bool), typeof(D3DViewportBase), new PropertyMetadata(false));

    public static readonly DependencyProperty IsReadyProperty = IsReadyPropertyKey.DependencyProperty;

    /// <summary>True once the first 3D frame is on screen. Until then the viewport is transparent.</summary>
    public bool IsReady
    {
        get => (bool)GetValue(IsReadyProperty);
        private set => SetValue(IsReadyPropertyKey, value);
    }

    private static readonly DependencyPropertyKey HasFailedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasFailed), typeof(bool), typeof(D3DViewportBase), new PropertyMetadata(false));

    public static readonly DependencyProperty HasFailedProperty = HasFailedPropertyKey.DependencyProperty;

    /// <summary>True if the 3D renderer could not be started; hosts should show a 2D fallback</summary>
    public bool HasFailed
    {
        get => (bool)GetValue(HasFailedProperty);
        private set => SetValue(HasFailedPropertyKey, value);
    }

    #endregion

    #region Loading

    /// <summary>The scene on screen is the one the properties ask for: nothing to load</summary>
    protected abstract bool IsUpToDate { get; }

    /// <summary>Builds or updates the scene. Runs one at a time; a request made meanwhile runs after it.</summary>
    protected abstract Task LoadAsync();

    /// <summary>What was being loaded, for the log line of a load that failed</summary>
    protected virtual string LoadDescription => "";

    protected async void RequestLoad()
    {
        if (!IsLoaded || !IsVisible || _failed) return;
        if (IsUpToDate)
        {
            // Up to date with no renderer: there is nothing to show (a station without its model, a street without a car)
            if (_renderer == null && !_isLoading) NothingToShow?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_isLoading)
        {
            _reloadRequested = true;
            return;
        }

        _isLoading = true;
        var what = LoadDescription;
        try
        {
            // Never load from inside whatever asked for it. A binding can ask in the middle of a layout pass,
            // where the dispatcher is locked and a wait doesn't pump; AcTools waits for the finalizers when it
            // swaps a car (GCHelper.CleanUp), and a finalizer that needs the UI thread then hangs the game for good.
            // After the yield this runs as a dispatcher operation of its own, where the wait pumps.
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (!IsLoaded || !IsVisible || _failed || IsUpToDate) return;

            what = LoadDescription;
            await LoadAsync();
        }
        catch (Exception ex) when (!_recovered && IsDeviceLost(ex))
        {
            // The GPU went away while the scene was loading: built again from scratch, once, as the render loop does
            RebuildAfterDeviceLost(ex, $"loading {what}");
        }
        catch (Exception ex)
        {
            // Leave the viewport transparent so whatever is behind it stays visible. A control unloaded meanwhile has
            // had its failure of that visit cleared already: the next visit tries again, not failed by this one.
            Logger.Error(ex, "{Viewport} failed to load {What}", ViewportName, what);
            if (!IsLoaded) return;
            Fail();
        }
        finally
        {
            _isLoading = false;
        }

        if (_reloadRequested)
        {
            _reloadRequested = false;
            RequestLoad();
        }
    }

    /// <summary>A renderer set up the way both viewports draw, at the control's size in pixels</summary>
    protected GarageRenderer NewRenderer(CarDescription? car, string? showroomKn5)
    {
        var (width, height) = GetPixelSize();
        return new GarageRenderer(car, showroomKn5)
        {
            WpfMode = true,
            UseMsaa = false, // shared surfaces can't be multisampled
            VisibleUi = false,
            AutoRotate = false,
            UseFxaa = true,
            UseSslr = true,
            UseAo = true,
            UseBloom = true,
            EnableShadows = true,
            UsePcss = true,
            Width = width,
            Height = height
        };
    }

    /// <summary>
    /// Initialises a new renderer off the UI thread and puts it on screen. False when the control was unloaded in
    /// the meantime; the renderer is gone then. Throws when it cannot be started, and it is gone then too.
    /// </summary>
    protected async Task<bool> StartRendererAsync(GarageRenderer renderer)
    {
        try
        {
            await Task.Run(() => renderer.Initialize());
        }
        catch
        {
            renderer.Dispose();
            throw;
        }

        // The control may have been unloaded while the scene was loading. Check before raising a device, or one is
        // left behind on a control that will never draw again.
        if (!IsLoaded)
        {
            renderer.Dispose();
            return false;
        }

        try
        {
            // No D3D9Ex (a remote session, a driver in the middle of a reset): the renderer must not outlive this
            _bridge.EnsureDevice();
        }
        catch
        {
            renderer.Dispose();
            throw;
        }

        _renderer = renderer;
        _deviceWork = Task.CompletedTask;
        _firstFrameTimer.Stop();
        _firstFrameTimer.Start();

        // The size was taken when the renderer was made; a resize while it was loading found no renderer to tell
        UpdateRendererSize();
        Invalidate();
        return true;
    }

    /// <summary>
    /// Cancelled when the current renderer goes, for <c>SetCarAsync</c>: a load cancelled this way ends quietly
    /// without a car, so the caller checks the renderer is still the one it loaded into, as it does after every await
    /// </summary>
    protected CancellationToken RendererToken => _rendererGone.Token;

    /// <summary>
    /// <paramref name="next"/> once <paramref name="before"/> is done, however that ended: loads into the same slots
    /// made one after another, never two at once. The newest ask is the one that counts: each load checks its own
    /// counter as it starts and after every await.
    /// </summary>
    internal static async Task After(Task before, Func<Task> next)
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

    /// <summary>
    /// The GPU went away under the scene (a driver update, a reset), found <paramref name="doing"/>. Everything on it is
    /// gone, the scene included, so it is built again from scratch, once: a device lost again soon after is a failure.
    /// </summary>
    protected void RebuildAfterDeviceLost(Exception ex, string doing)
    {
        if (_recovered)
        {
            Logger.Error(ex, "{Viewport}: the graphics device was lost again {Doing}", ViewportName, doing);
            Fail();
            return;
        }

        Logger.Warning(ex, "{Viewport}: the graphics device was lost {Doing}, starting the scene again", ViewportName, doing);
        _recovered = true;
        _recoveredAt = DateTime.Now;
        DisposeRenderer(releaseDevice: true);
        _ = Dispatcher.InvokeAsync(RequestLoad);
    }

    /// <summary>
    /// Device work done for the current renderer that may still be running when the control goes: a car being put
    /// in a slot ends with device calls made from a worker thread, outside the renderer's lock. The renderer waits
    /// for it before it is disposed.
    /// </summary>
    protected Task TrackDeviceWork(Task work)
    {
        _deviceWork = _deviceWork.IsCompleted ? work : Task.WhenAll(_deviceWork, work);
        return work;
    }

    /// <inheritdoc cref="TrackDeviceWork(Task)"/>
    protected Task<T> TrackDeviceWork<T>(Task<T> work)
    {
        TrackDeviceWork((Task)work);
        return work;
    }

    /// <summary>
    /// Makes <paramref name="car"/>, in <paramref name="renderer"/>, show <paramref name="shown"/>
    /// (<see cref="ShownPartsOnCar.SetAsync"/>): device work the renderer waits for before it is disposed, which stops
    /// short of its device calls once the renderer has gone. True when the car changed.
    /// </summary>
    protected Task<bool> ShowPartsAsync(GarageRenderer renderer, Kn5RenderableCar? car, Parts.Cars.ShownSet? shown,
        int wearPicture = Looks.WearLayer.FullPicture) =>
        TrackDeviceWork(ShownPartsOnCar.SetAsync(car, shown, renderer.DeviceContextHolder, wearPicture, RendererToken));

    #endregion

    #region Rendering

    /// <summary>
    /// Moves whatever moves by itself (the camera, mostly) by a frame. True while something is still moving, so the
    /// pump keeps drawing.
    /// </summary>
    protected abstract bool StepFrame(GarageRenderer renderer, float dt);

    /// <summary>The first frame is on screen; the viewport decides when its picture shows</summary>
    protected virtual void OnFirstFrame() { }

    /// <summary>Another frame is on screen</summary>
    protected virtual void OnFramePresented() { }

    /// <summary>The renderer went; whatever the viewport knew about its scene goes with it</summary>
    protected virtual void OnRendererDisposed() { }

    /// <summary>The picture is still held back: nothing has faded it in yet</summary>
    protected bool IsPictureHidden => _image.Opacity == 0;

    protected void FadeIn() => FadeIn(FadeInDuration);

    protected void FadeIn(TimeSpan duration) => _image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));

    /// <summary>
    /// The scene is up: once a few frames have drawn it, <paramref name="settled"/> hears so (the picture still as it
    /// was, held back or not) and a picture held back fades in over <paramref name="duration"/> (the usual fade when null)
    /// </summary>
    protected void FadeInAfterSettled(TimeSpan? duration = null, Action? settled = null)
    {
        _fadeInAfter = SettleFrames;
        _fadeInFor = duration ?? FadeInDuration;
        _settled = settled;
    }

    /// <summary>A frame of the settling counted; at the end of it, the scene shown</summary>
    private void StepFadeIn()
    {
        if (_fadeInAfter < 0) return;
        if (_fadeInAfter-- > 0) return;

        var settled = _settled;
        _settled = null;
        settled?.Invoke();
        if (IsPictureHidden) FadeIn(_fadeInFor);
    }

    /// <summary>
    /// Fades the picture to nothing. Done when it is dark, or when the fade was cut short (the renderer went): never
    /// left waiting on an animation that was taken off.
    /// </summary>
    protected Task FadeOutAsync(TimeSpan duration)
    {
        var done = new TaskCompletionSource();
        var fade = new DoubleAnimation(_image.Opacity, 0, duration);
        fade.Completed += (_, _) => done.TrySetResult();
        _image.BeginAnimation(OpacityProperty, fade);
        return Task.WhenAny(done.Task, Task.Delay(duration + TimeSpan.FromMilliseconds(250)));
    }

    /// <summary>A still of the picture on screen, or null when there is none to take</summary>
    protected System.Windows.Media.Imaging.BitmapSource? CopyPicture() => _bridge.CopyFrame();

    protected void ShowPicture() => _image.Opacity = 1;

    /// <summary>Something in the scene changed: draw it, and keep drawing for the settle window</summary>
    protected void Invalidate()
    {
        if (_renderer != null) _renderer.IsDirty = true;
        AnimateFor(SettleWindow);
    }

    /// <summary>
    /// For a change made straight on the renderer that marks it dirty only when something did change (the part under
    /// the pointer): draws it at once rather than on the next look the idle clock takes
    /// </summary>
    protected void InvalidateIfDirty()
    {
        if (_renderer is { IsDirty: true }) Invalidate();
    }

    /// <summary>Keeps drawing for a while, for something that plays out by itself (doors, parts flying in)</summary>
    protected void AnimateFor(TimeSpan window)
    {
        var until = DateTime.Now + window;
        if (until > _animateUntil) _animateUntil = until;
        if (_renderer != null) _renderer.IsDirty = true;
        StartPump();
    }

    private void StartPump()
    {
        if (_renderer == null || !IsVisible) return;

        _idleTimer.Stop();
        if (_pumping) return;

        _pumping = true;
        CompositionTarget.Rendering += OnRendering;
    }

    /// <param name="idle">The scene is standing still but still on show: keep an eye out for the renderer's own changes</param>
    private void StopPump(bool idle)
    {
        if (_pumping)
        {
            _pumping = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        if (idle && _renderer != null) _idleTimer.Start();
        else _idleTimer.Stop();
    }

    private void OnIdlePoll(object? sender, EventArgs e)
    {
        if (_renderer == null || !IsVisible)
        {
            _idleTimer.Stop();
            return;
        }

        if (_renderer.IsDirty) StartPump();
    }

    /// <summary>A frame this long after the last, ms, is one the player sees stutter (three at 60 Hz)</summary>
    private const double SlowFrameMs = 45;

    private void OnRendering(object? sender, EventArgs e)
    {
        // CompositionTarget.Rendering can fire more than once per frame
        var args = (RenderingEventArgs)e;
        if (args.RenderingTime == _lastRenderingTime) return;

        var dt = (float)(args.RenderingTime - _lastRenderingTime).TotalSeconds;
        var gapMs = dt * 1000;
        _lastRenderingTime = args.RenderingTime;
        var frameStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        var collections = GC.CollectionCount(2);
        if (dt <= 0f || dt > 0.25f) dt = 1f / 60f;

        var renderer = _renderer;
        if (renderer == null || !IsVisible || !_bridge.IsFrontBufferAvailable)
        {
            // Coming back into view, or the front buffer coming back, starts it again
            StopPump(idle: false);
            return;
        }

        try
        {
            // The frame's own steps too: whatever throws in them would otherwise reach the dispatcher every frame
            var moving = StepFrame(renderer, dt);

            if (_scripted != null)
            {
                StepScripted(dt);
                moving = true;
            }

            // A bump rings out frame by frame, on top of whatever the running engine does
            if (_bumps.Count > 0)
            {
                var step = Math.Min(dt, GarageRenderer.MaxTickSeconds);
                for (var i = 0; i < _bumps.Count; i++) _bumps[i] = _bumps[i] with { Age = _bumps[i].Age + step };
                OnPoseChanged();
                moving = true;
            }

            var now = DateTime.Now;
            if (renderer.IsDirty && _animateUntil < now + SettleWindow)
            {
                _animateUntil = now + SettleWindow;
            }

            if (!moving && now >= _animateUntil && _bridge.BoundTarget != IntPtr.Zero)
            {
                StopPump(idle: true);
                return;
            }

            renderer.Draw();
            _bridge.Present(renderer.GetRenderTarget());

            if (!IsReady)
            {
                IsReady = true;
                OnFirstFrame();
                Ready?.Invoke(this, EventArgs.Empty);
            }

            OnFramePresented();
            StepFadeIn();

            // A car on the move shows every frame that came late: what makes a scripted move stutter (not the first after the
            // scene stood still, whose gap is the time it stood)
            var workMs = System.Diagnostics.Stopwatch.GetElapsedTime(frameStarted).TotalMilliseconds;
            if (_scripted != null && ((gapMs > SlowFrameMs && gapMs < 1000) || workMs > SlowFrameMs / 2))
                Logger.Debug("Slow frame on the move: {Gap:0} ms since the last, {Work:0} ms to draw{Gc}", gapMs, workMs,
                    GC.CollectionCount(2) != collections ? ", a full garbage collection in it" : "");

            // A scene that has drawn well for a while since it was rebuilt has earned another rebuild: a device lost
            // again soon after is still a failure, so this cannot loop
            if (_recovered && DateTime.Now - _recoveredAt >= RecoveryGrace) _recovered = false;
        }
        catch (Exception ex) when (!_recovered && IsDeviceLost(ex))
        {
            // The GPU went away under the renderer: the scene is built again from scratch, once
            RebuildAfterDeviceLost(ex, "drawing");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "{Viewport} render loop failed", ViewportName);
            Fail();
        }
    }

    private void OnFirstFrameTimeout(object? sender, EventArgs e)
    {
        _firstFrameTimer.Stop();
        if (_renderer == null || IsReady || _failed || _bridge.IsFrontBufferAvailable) return;

        Logger.Warning("{Viewport}: no front buffer to draw to (software rendering or a remote session): showing the 2D view", ViewportName);
        Fail();
    }

    /// <summary>The GPU went away under the renderer; the scene is built again rather than failed</summary>
    protected static bool IsDeviceLost(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            var code = e is SlimDX.SlimDXException slim ? slim.ResultCode.Code : e.HResult;
            if (DeviceLostCodes.Contains(code)) return true;
        }

        return false;
    }

    protected (int Width, int Height) GetPixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(16, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(16, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        return (width, height);
    }

    /// <summary>Moved to a monitor of other scaling: the same size in DIPs is another size in pixels</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateRendererSize();
    }

    private void UpdateRendererSize()
    {
        var renderer = _renderer;
        if (renderer == null) return;

        var (width, height) = GetPixelSize();
        if (renderer.Width == width && renderer.Height == height) return;

        renderer.Width = width;
        renderer.Height = height;
        Invalidate();
    }

    /// <summary>
    /// Takes the renderer off screen and disposes it, once any device work still running on it is done.
    /// <paramref name="releaseDevice"/> also lets go of the bridge's D3D9 device; the next renderer raises it again.
    /// </summary>
    protected void DisposeRenderer(bool releaseDevice)
    {
        StopPump(idle: false);
        _resizeTimer.Stop();
        _firstFrameTimer.Stop();
        _rock = null;
        _bumps.Clear();
        _fadeInAfter = -1;
        _settled = null;
        DropScript();

        var renderer = _renderer;
        _renderer = null;

        // A car still loading into it stops where it is. Not disposed: the load may still be looking at the token.
        if (renderer != null)
        {
            _rendererGone.Cancel();
            _rendererGone = new CancellationTokenSource();
        }

        // The target belongs to the renderer: let go of it before the renderer goes
        _bridge.ReleaseSurface();

        if (renderer != null)
        {
            var work = _deviceWork;
            _deviceWork = Task.CompletedTask;
            _ = DisposeAfterAsync(renderer, work);
        }

        if (releaseDevice) _bridge.Dispose();

        OnRendererDisposed();

        IsReady = false;
        ShowLabel(null, default);
        _image.BeginAnimation(OpacityProperty, null);
        _image.Opacity = 0;
    }

    private async Task DisposeAfterAsync(GarageRenderer renderer, Task work)
    {
        try
        {
            await work;
        }
        catch
        {
            // Whoever started the work has logged how it ended; all that matters here is that it has
        }

        try
        {
            renderer.Dispose();
        }
        catch (Exception ex)
        {
            // A renderer on a device that is gone can fail on the way out too; nothing is left to be done with it
            Logger.Warning(ex, "{Viewport}: the renderer did not dispose cleanly", ViewportName);
        }
    }

    protected void Fail()
    {
        _failed = true;
        DisposeRenderer(releaseDevice: true);
        HasFailed = true;
        Failed?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Label

    /// <summary>A label next to the pointer; null takes it away</summary>
    protected void ShowLabel(string? text, System.Windows.Point position)
    {
        if (text == null)
        {
            _label.Visibility = Visibility.Collapsed;
            return;
        }

        _labelText.Text = text;
        _label.Margin = new Thickness(position.X + _labelOffset.X, position.Y + _labelOffset.Y, 0, 0);
        _label.Visibility = Visibility.Visible;
    }

    #endregion

    /// <summary>The car's skin when its folder has it, or the car's default one</summary>
    protected static string SkinOrDefault(string carDirectory, string? skin) =>
        skin != null && System.IO.Directory.Exists(System.IO.Path.Combine(carDirectory, "skins", skin))
            ? skin
            : AcTools.Render.Kn5Specific.Objects.Kn5RenderableCar.DefaultSkin;

    #region Body rock

    /// <summary>How the running engine leans the rocked car, in world space; none while nothing rocks it</summary>
    protected SlimDX.Matrix CarLean => _rock?.WorldLean ?? SlimDX.Matrix.Identity;

    /// <summary>Only the garage leans the parts it shows along with the body</summary>
    protected virtual bool LeansParts => false;

    private void OnRunningEngineChanged(Audio.EngineRunner? old, Audio.EngineRunner? runner)
    {
        // An engine a car on the move was driving is out of gear: it would otherwise stay held at the wheels' speed
        if (_scripted != null) old?.Drive(null);

        // Only while on screen: a runner that outlives the control must not keep it alive through the event
        if (IsLoaded) SetPoseSource(runner);
        OnPoseChanged();
    }

    private void SetPoseSource(Audio.EngineRunner? runner)
    {
        if (ReferenceEquals(runner, _poseSource)) return;

        if (_poseSource != null) _poseSource.PoseChanged -= OnPoseChanged;
        _poseSource = runner;
        if (_poseSource != null) _poseSource.PoseChanged += OnPoseChanged;
    }

    /// <summary>The engine moved the body, or the engine changed: the viewport says which car it rocks</summary>
    protected abstract void OnPoseChanged();

    // A bump: how far the nose goes and the body drops at first, how quickly it swings and how soon it settles. Small:
    // a car shrugging off a load, not a car hitting a kerb.
    private const float BumpPitch = 0.012f;
    private const float BumpHeave = 0.008f;
    private const float BumpHertz = 1.8f;
    private const float BumpSettleSeconds = 0.35f;
    private const float BumpSeconds = 1.6f;

    /// <summary>
    /// The car bounces once on its springs, <paramref name="delaySeconds"/> from now: a heavy load leaving it
    /// (<paramref name="lighter"/>, its end comes up first) or landing in it (its end dips first). The load is in the
    /// front, so the nose moves, or else behind the driver (<paramref name="atFront"/> false), so the tail does.
    /// </summary>
    protected void BumpCar(bool lighter, float delaySeconds = 0f, bool atFront = true)
    {
        _bumps.Add((-Math.Max(0f, delaySeconds), lighter ? 1f : -1f, atFront));
        AnimateFor(TimeSpan.FromSeconds(Math.Max(0f, delaySeconds) + BumpSeconds));
    }

    /// <summary>What the bumps under way add to the body's pose now, the ones that have rung out dropped</summary>
    private Audio.BodyPose BumpPose()
    {
        _bumps.RemoveAll(b => b.Age >= BumpSeconds);

        var pitch = 0f;
        var heave = 0f;
        foreach (var (t, sign, front) in _bumps)
        {
            if (t <= 0f) continue;

            var swing = sign * (float)(Math.Exp(-t / BumpSettleSeconds) * Math.Sin(2 * Math.PI * BumpHertz * t));
            pitch += front ? swing : -swing;
            heave += swing;
        }

        // The loaded end leads; the whole body follows a little
        return new Audio.BodyPose(0f, BumpPitch * pitch, BumpHeave * heave);
    }

    /// <summary>
    /// The car's engine faces backwards (a rear engine): its crankshaft turns the other way under the body, so the
    /// body leans the other way as it revs. Where the running engine's car carries it, unless the viewport knows better.
    /// </summary>
    protected virtual bool EngineTurnedRound => RunningEngine?.Spec?.Layout == StreetCorsa.Parts.EngineLayout.Rear;

    /// <summary>
    /// The car's engine lies across it: its crankshaft turns under the body from side to side, so as it revs the body
    /// pitches where a lengthwise engine rolls it
    /// </summary>
    protected virtual bool EngineAcross => RunningEngine?.Spec?.Layout is { } layout && StreetCorsa.Parts.EngineLayouts.IsTransverse(layout);

    /// <summary>
    /// Rocks <paramref name="carNode"/> the way the running engine has it, and any bump with it; null, or an engine at
    /// rest and no bump, lets it settle
    /// </summary>
    protected void RockCar(Kn5RenderableCar? carNode)
    {
        var renderer = _renderer;
        var running = RunningEngine?.Pose ?? Audio.BodyPose.Rest;
        if (EngineTurnedRound) running = running with { Roll = -running.Roll };
        if (EngineAcross) running = running with { Roll = 0f, Pitch = running.Pitch + running.Roll };
        var bump = BumpPose();
        var moved = _scripted != null && ReferenceEquals(_scriptedNode, carNode) ? _scripted : null;
        // A car on the move squats or dives, and leans out of a bend
        var (moveRoll, movePitch) = moved?.Body ?? (0f, 0f);
        var pose = new Audio.BodyPose(running.Roll + bump.Roll + moveRoll, running.Pitch + bump.Pitch + movePitch, running.Heave + bump.Heave);
        // A corner on a broken spring sags: the body stays leaned that way, the car at rest too
        var heights = ShownPartsOnCar.HeightsOf(carNode);
        if (renderer == null || carNode == null || (moved == null && pose.IsRest && heights == null && RunningEngine?.IsActive != true && _bumps.Count == 0))
        {
            ReleaseRock();
            return;
        }

        if (_rock == null || !ReferenceEquals(_rock.Car, carNode))
        {
            ReleaseRock();
            _rock = new CarBodyRock(carNode);
        }

        var sag = _rock.PoseOf(heights);
        pose = new Audio.BodyPose(pose.Roll + sag.Roll, pose.Pitch + sag.Pitch, pose.Heave + sag.Heave);

        // A car on the move goes where its script has it, its wheels turning and its front wheels steered as it goes
        if (!_rock.Apply(pose, moved?.WheelSpin ?? 0f, moved == null ? null : Placement(moved.Pose), moved?.Steer ?? 0f)) return;

        if (LeansParts) renderer.BodyLean = _rock.WorldLean;

        // The shadow maps are drawn again only for a lean that shows in them, not for every shake at idle; a car on the
        // move takes its shadow along every frame
        if (_rock.TakeShadows(pose, moving: moved != null)) renderer.RefreshShadows();

        Invalidate();
    }

    /// <summary>Puts the rocked car back on its wheels</summary>
    protected void ReleaseRock()
    {
        if (_rock == null) return;

        _rock.Release();
        _rock = null;
        if (_renderer != null)
        {
            if (LeansParts) _renderer.BodyLean = SlimDX.Matrix.Identity;
            _renderer.RefreshShadows();
            _renderer.IsDirty = true;
            StartPump();
        }
    }

    #endregion

    #region Scripted moves

    /// <summary>
    /// Plays the script for <paramref name="trigger"/> with <paramref name="car"/> as its car: it stands where it stands
    /// now (its home, on the floor under it), and moves as the script says, its engine
    /// (<paramref name="engine"/>) driven by the wheels. The camera stays where it is while it plays (or follows the car,
    /// as the script says). Null when there is no scene to play it in.
    /// </summary>
    /// <param name="headlights">Switches the car's headlights (the garage's are a property of its own)</param>
    /// <param name="scene">The scene's marks and paths, by name</param>
    /// <param name="fade">The screen's curtain to an opacity over a time; null for a screen without one</param>
    protected ScriptRun? PlayScript(string trigger, Kn5RenderableCar car, Func<Audio.EngineRunner?> engine, ScriptFacts facts,
        Action<bool> headlights, SceneMarks? scene = null, Func<float, TimeSpan, FadeEase, Task>? fade = null)
    {
        if (_renderer is not { } renderer) return null;
        StopScript();

        // No script at this moment: nothing moves, and nothing is taken over
        var scriptEngine = new ScriptEngine(engine);
        if (ScriptBook.Installed.For(trigger, facts, scriptEngine) is not { } script) return ScriptRun.Nothing();

        // The rock knows which end is the nose; the car stands where it does less any lean
        if (_rock == null || !ReferenceEquals(_rock.Car, car))
        {
            ReleaseRock();
            _rock = new CarBodyRock(car);
        }

        _scriptFrom = _rock.Base;
        _scriptParent = SlimDX.Matrix.Invert(car.LocalMatrix) * car.Matrix;
        _scriptParentInverse = SlimDX.Matrix.Invert(_scriptParent);
        var world = _scriptFrom * _scriptParent;
        var front = _rock.FrontSign;
        var home = new GroundPose(world.M41, world.M43, MathF.Atan2(world.M31 * front, world.M33 * front));

        var scripted = new ScriptedCar(home, scriptEngine, (head, brake) =>
        {
            if (head is { } on) headlights(on);
            if (brake is { } braking) car.BrakeLightsEnabled = braking;
            Invalidate();
        });
        scripted.Started += () => AnimateFor(SettleWindow);
        _scripted = scripted;
        _scriptedNode = car;
        _scriptAutoAdjust = renderer.AutoAdjustTarget;
        _follow = false;
        renderer.AutoAdjustTarget = false;

        var run = ScriptRun.Play(script, new SceneStage(scripted, SceneStage.Player, scene ?? SceneMarks.None, fade,
            mode => _follow = mode == CameraMode.Follow), facts);
        _run = run;
        AnimateFor(SettleWindow);
        return run;
    }

    /// <summary>A script plays for <paramref name="trigger"/> at this moment: a screen that readies itself for one (its camera) asks first</summary>
    protected static bool HasScript(string trigger, Func<Audio.EngineRunner?> engine, ScriptFacts facts) =>
        ScriptBook.Installed.For(trigger, facts, new ScriptEngine(engine)) != null;

    /// <summary>
    /// The script playing stops, and its car is back where it stood, at rest, the camera as it was and the engine out
    /// of gear. A viewport about to take the car off its scene calls this first, while the car is still there.
    /// </summary>
    protected void StopScript()
    {
        _run?.Cancel();
        _run = null;
        if (_scripted is not { } scripted || _scriptedNode is not { } car) return;

        _scripted = null;
        _scriptedNode = null;
        scripted.GoHome();
        scripted.Drop();
        car.BrakeLightsEnabled = false;
        if (_rock is { } rock && ReferenceEquals(rock.Car, car)) rock.Apply(Audio.BodyPose.Rest, 0f, _scriptFrom);
        else car.LocalMatrix = _scriptFrom;
        if (_renderer != null) _renderer.AutoAdjustTarget = _scriptAutoAdjust;
        RunningEngine?.Drive(null);
        ReleaseRock();
        OnPoseChanged();
    }

    /// <summary>The script goes with the scene: whoever waits is let go; there is no car left to put back</summary>
    private void DropScript()
    {
        _run?.Cancel();
        _run = null;
        _scripted?.Drop();
        _scripted = null;
        _scriptedNode = null;
        RunningEngine?.Drive(null);
    }

    /// <summary>A script under way here, if one is: a click skips it</summary>
    protected virtual ScriptRun? SkippableRun => _run;

    private System.Windows.Point? _pressedAt;

    protected override void OnPreviewMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        _pressedAt = SkippableRun is { IsOver: false } ? e.GetPosition(this) : null;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    /// <summary>A click (not a drag) on a car moving by its script skips the show to its end</summary>
    protected override void OnPreviewMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_pressedAt is { } pressed && SkippableRun is { IsOver: false } run && (e.GetPosition(this) - pressed).Length < 4)
        {
            run.Skip();

            // The press is used up: the click handlers below never hear of it, so what they took hold of is let go here
            e.Handled = true;
            ForgetPress();
            ReleaseMouseCapture();
        }

        _pressedAt = null;
        base.OnPreviewMouseLeftButtonUp(e);
    }

    /// <summary>A press used up skipping a script: whatever the viewport's own handlers began with it (a drag) is over</summary>
    protected virtual void ForgetPress()
    {
    }

    private void StepScripted(float dt)
    {
        if (_scripted is not { } scripted || _scriptedNode is null) return;

        var before = scripted.Pose;
        scripted.Step(Math.Min(dt, GarageRenderer.MaxTickSeconds));

        // The camera keeps the car where it has it in the picture
        if (_follow && _renderer?.CameraOrbit is { } orbit && scripted.Pose != before)
        {
            var (now, then) = (World(scripted.Pose), World(before));
            orbit.Target += new SlimDX.Vector3(now.M41 - then.M41, now.M42 - then.M42, now.M43 - then.M43);
            orbit.UpdateViewMatrix();
        }

        // Played out and standing: let go of it, back in its place if the script left it elsewhere (the screen is the
        // player's again, and a car out of its place would stand in the way)
        if (_run?.IsOver != false && scripted.IsSettled)
        {
            StopScript();
            return;
        }

        // A running engine rocks the car on its own frames, and the move goes with it then: once a frame, not twice
        if (RunningEngine?.IsActive != true) OnPoseChanged();
    }

    /// <summary>Where the scripted car stands at <paramref name="pose"/>, in its parent's space: turned and moved from its home on the floor</summary>
    private SlimDX.Matrix Placement(GroundPose pose) =>
        _scripted is { } scripted && pose != scripted.Home ? World(pose) * _scriptParentInverse : _scriptFrom;

    /// <summary>Where the scripted car stands at <paramref name="pose"/>, in the world</summary>
    private SlimDX.Matrix World(GroundPose pose)
    {
        var world = _scriptFrom * _scriptParent;
        if (_scripted is not { } scripted) return world;

        // Turned about its own origin from where it stood, then moved to where it is now; RotationY adds to the heading
        var home = scripted.Home;
        return world
               * SlimDX.Matrix.Translation(-home.X, 0f, -home.Z)
               * SlimDX.Matrix.RotationY(pose.Heading - home.Heading)
               * SlimDX.Matrix.Translation(pose.X, 0f, pose.Z);
    }

    #endregion
}
