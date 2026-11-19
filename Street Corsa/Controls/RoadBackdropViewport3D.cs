using System.Windows;
using StreetCorsa.Services.Race.Traffic;
using StreetCorsa.Services.Showcase;
using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>A road a <see cref="RoadBackdropViewport3D"/> films: where the track is, its layout, its name</summary>
public sealed record BackdropRoad(string Folder, string TrackId, string? Layout, string Title);

/// <summary>
/// A screen's backdrop of roads: a camera flown low along each road's AI line in turn, a few seconds on each, through
/// black from one to the next, round again after the last. A road is filmed from its backdrop
/// (<see cref="RoadBackdrops"/>, made the first time it is wanted while the road before stays on screen). Given other
/// roads, it goes to the first of them. Nothing here takes the mouse. Where the road draws nothing the picture is clear,
/// so the screen paints the sky behind it.
/// </summary>
public class RoadBackdropViewport3D : D3DViewportBase
{
    // How long a road is filmed, and the camera's way along it: its speed, how high, how far ahead it looks, how far it
    // swings off the line and how slowly. The look of a backdrop, not a figure the game plays by.
    private const float RoadSeconds = 10f;
    private const float Speed = 21f;
    private const float Height = 3.4f;
    private const float Ahead = 36f;
    private const float Swing = 2.2f;
    private const float SwingSeconds = 9f;
    private const float FieldOfView = 52f;

    // How far apart a line's ends lie for it to be open, a point to point road: a loop's AI line ends a few metres
    // short of its start
    private const float OpenGap = 50f;

    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(1100);

    private readonly Random _random = new();

    // The roads as the screen gave them, the ones of them that cannot be filmed this visit, and the turn: the given ones
    // less the dropped
    private List<BackdropRoad> _given = [];
    private readonly HashSet<BackdropRoad> _dropped = [];
    private List<BackdropRoad> _roads = [];
    private int _turn;
    private BackdropRoad? _wanted;
    private BackdropRoad? _standing;

    // Cancelled when the road being loaded is no longer wanted, and when the control goes: a make stops, and the load
    // gives way to the road wanted now. The next road's backdrop is made ahead the same way.
    private CancellationTokenSource _loading = new();
    private CancellationTokenSource? _prefetch;
    private BackdropRoad? _prefetching;

    // Backdrops the renderer could not open (a texture it rejects), by their file and when it was written: left out for
    // the rest of the session, tried again once made anew. Only once a backdrop has opened at all, so a 3D that does not
    // work is not taken for bad backdrops.
    private static readonly HashSet<(string File, DateTime Written)> Unopenable = [];
    private static bool _anyOpened;

    // The AI line of the road on screen as distances along it, whether it is open (its ends apart: a point to point
    // road, not a loop), and where the camera is on it
    private Sx.Vector3[]? _line;
    private float[]? _along;
    private bool _open;
    private float _at;
    private float _time;
    private float _phase;
    private bool _leaving;

    // The stretches of the road on screen its backdrop keeps (RoadBackdrops.Stretches), the one flown, the next each
    // road is to start on (they take turns), and a jump through black to another under way
    private IReadOnlyList<(float From, float To)> _stretches = [];
    private (float From, float To) _stretch;
    private readonly Dictionary<BackdropRoad, int> _nextStretch = [];
    private bool _jumping;

    public RoadBackdropViewport3D()
        : base("Road backdrop", new LabelLook(14, 0xD8, 0x90, new Thickness(9, 5, 9, 5), new Vector(18, 18)))
    {
        IsHitTestVisible = false;

        // After the base's: the renderer is gone. What is being made stops; a road dropped this visit is tried again
        // the next, from the first road.
        Unloaded += (_, _) =>
        {
            _loading.Cancel();
            _loading = new CancellationTokenSource();
            _prefetch?.Cancel();
            _prefetch = null;
            _prefetching = null;
            _dropped.Clear();
            _roads = [.. _given];
            _turn = 0;
            _wanted = _roads.FirstOrDefault();
            _leaving = false;
            _time = 0f;
        };
    }

    protected override string ViewportName => "Road backdrop viewport";

    #region Dependency properties

    public static readonly DependencyProperty RoadsProperty = DependencyProperty.Register(
        nameof(Roads), typeof(IReadOnlyList<BackdropRoad>), typeof(RoadBackdropViewport3D),
        new PropertyMetadata(null, (d, _) => ((RoadBackdropViewport3D)d).OnRoadsChanged()));

    /// <summary>The roads to film, in turn</summary>
    public IReadOnlyList<BackdropRoad>? Roads
    {
        get => (IReadOnlyList<BackdropRoad>?)GetValue(RoadsProperty);
        set => SetValue(RoadsProperty, value);
    }

    private static readonly DependencyPropertyKey CurrentTitlePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CurrentTitle), typeof(string), typeof(RoadBackdropViewport3D), new PropertyMetadata(null));

    public static readonly DependencyProperty CurrentTitleProperty = CurrentTitlePropertyKey.DependencyProperty;

    /// <summary>The name of the road on screen, or null while none is</summary>
    public string? CurrentTitle
    {
        get => (string?)GetValue(CurrentTitleProperty);
        private set => SetValue(CurrentTitlePropertyKey, value);
    }

    #endregion

    #region Loading

    private void OnRoadsChanged()
    {
        var given = Roads?.ToList() ?? [];

        // The same roads again (the list made anew): the one on screen stays, a dropped one stays dropped
        if (given.SequenceEqual(_given)) return;

        _given = given;
        _roads = given.Where(road => !_dropped.Contains(road)).ToList();
        _turn = 0;
        Want(_roads.FirstOrDefault());
        _time = 0f;
        RequestLoad();
    }

    /// <summary>
    /// <paramref name="road"/> is the one to film next: a load or a make of another stops. The road on screen wanted
    /// again stays for a time of its own; for another the road on screen is being left.
    /// </summary>
    private void Want(BackdropRoad? road)
    {
        if (road != _wanted)
        {
            _loading.Cancel();
            _loading = new CancellationTokenSource();
        }

        if (_prefetching != null && _prefetching != road)
        {
            _prefetch?.Cancel();
            _prefetch = null;
            _prefetching = null;
        }

        _wanted = road;
        _leaving = road != _standing;
        if (!_leaving) _time = 0f;
    }

    /// <summary>A road that cannot be filmed is left out of the turn for this visit, the turn going on to the next</summary>
    private void LeaveOut(BackdropRoad road)
    {
        _dropped.Add(road);
        _roads.Remove(road);
        Want(_roads.Count == 0 ? null : _roads[_turn % _roads.Count]);
        RequestLoad();
    }

    protected override bool IsUpToDate => _wanted == null
        ? Renderer == null
        : Renderer != null && _standing == _wanted;

    protected override string LoadDescription => _wanted is { } road ? $"{road.TrackId} {road.Layout}" : "";

    protected override async Task LoadAsync()
    {
        if (_wanted is not { } road)
        {
            // No roads: the screen's sky alone
            DisposeRenderer(releaseDevice: false);
            return;
        }

        // Made off the UI thread the first time, the road before still flown meanwhile; given up when another is wanted
        var cancel = _loading.Token;
        string? file;
        try
        {
            file = await RoadBackdrops.GetAsync(road.Folder, road.TrackId, road.Layout, cancel);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }

        if (!IsLoaded || _wanted != road) return;
        if (file != null && Unopenable.Contains(StampOf(file))) file = null;

        var line = file == null ? null : await Task.Run(() => LineOf(road));
        if (!IsLoaded || _wanted != road) return;

        if (file == null || line == null)
        {
            Logger.Warning("{Track} {Layout} cannot be filmed; left out", road.TrackId, road.Layout);
            LeaveOut(road);
            return;
        }

        if (Renderer != null && !IsPictureHidden)
        {
            await FadeOutAsync(FadeOut);
            if (!IsLoaded) return;
            if (_wanted != road)
            {
                // The road on screen wanted again meanwhile: it comes back
                if (Renderer != null && _wanted == _standing) FadeInAfterSettled(FadeIn);
                return;
            }
        }

        DisposeRenderer(releaseDevice: false);

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        GarageRenderer renderer;
        try
        {
            renderer = NewRenderer(null, file);

            // A road is kilometres across: no shadows or screen-space effects worked out over all of it, nothing moved
            // but the camera
            renderer.EnableShadows = false;
            renderer.UsePcss = false;
            renderer.UseSslr = false;
            renderer.UseAo = false;
            renderer.LoadCarLights = false;
            renderer.TryToGuessCarLights = false;
            renderer.AutoAdjustTarget = false;
            renderer.FlatMirror = false;

            // With no car the reflection cube sits at the world's origin, mostly far from the road filmed, so what
            // reflects it (rails, signs, glass) shows the black background. Left so: AcTools' only other centre is the
            // camera, which would draw the whole road six times more every frame it moves.
            renderer.CubemapReflectionMapSize = 256;

            // Where the road draws nothing the renderer clears to its background at no opacity, and the picture is
            // laid over the screen premultiplied: black there is clear, and the screen's sky shows through (any other
            // colour would be added to it)
            renderer.BackgroundColor = System.Drawing.Color.Black;

            if (!await StartRendererAsync(renderer)) return;
        }
        catch (Exception ex) when (IsLoaded && !IsDeviceLost(ex) && ex is not SharpGen.Runtime.SharpGenException)
        {
            // The renderer would not open this road's backdrop (a texture it rejects): the road is left out, the
            // backdrop goes on. A device lost, or the shared surface's device not made, is the view's own failure.
            CannotOpen(road, file, ex);
            return;
        }

        if (Renderer != renderer || _wanted != road) return;

        _anyOpened = true;
        _standing = road;
        (_line, _along) = line.Value;
        _open = IsOpen(Numeric(_line[0]), Numeric(_line[^1]));
        _stretches = RoadBackdrops.Stretches(_along[^1]);
        _phase = (float)(_random.NextDouble() * Math.PI * 2);
        _leaving = false;
        _jumping = false;
        StartStretch(road);
        CurrentTitle = road.Title;
        Aim(renderer);

        Logger.Information("Road backdrop {Track} {Layout} up in {Ms} ms", road.TrackId, road.Layout,
            (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        FadeInAfterSettled(FadeIn);
        Invalidate();

        // The next road's backdrop made while this one is filmed
        if (_roads.Count > 1 && _roads[(_turn + 1) % _roads.Count] is var next && next != road)
            _ = PrefetchAsync(next);
    }

    /// <summary>The road's backdrop would not open: left out of the turn; with none ever opened and no road left, the view failed</summary>
    private void CannotOpen(BackdropRoad road, string file, Exception ex)
    {
        Logger.Warning(ex, "The backdrop of {Track} {Layout} could not be opened; left out", road.TrackId, road.Layout);
        if (_anyOpened) Unopenable.Add(StampOf(file));
        LeaveOut(road);
        if (_roads.Count == 0 && !_anyOpened) Fail(ex.Message);
    }

    private static (string File, DateTime Written) StampOf(string file) => (file, System.IO.File.GetLastWriteTimeUtc(file));

    /// <summary>A road's backdrop made ahead; stopped when another road is wanted instead</summary>
    private async Task PrefetchAsync(BackdropRoad road)
    {
        _prefetch?.Cancel();
        var cancel = new CancellationTokenSource();
        _prefetch = cancel;
        _prefetching = road;
        try
        {
            await RoadBackdrops.GetAsync(road.Folder, road.TrackId, road.Layout, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // Nobody wants it now
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "The backdrop of {Track} {Layout} could not be made ahead", road.TrackId, road.Layout);
        }
        finally
        {
            if (_prefetch == cancel)
            {
                _prefetch = null;
                _prefetching = null;
            }
        }
    }

    /// <summary>A line whose ends lie this far apart is open (a point to point road): the camera cannot fly it round and round</summary>
    internal static bool IsOpen(System.Numerics.Vector3 start, System.Numerics.Vector3 end) =>
        System.Numerics.Vector3.Distance(start, end) > OpenGap;

    private static System.Numerics.Vector3 Numeric(Sx.Vector3 point) => new(point.X, point.Y, point.Z);

    /// <summary>The road's AI line, its points and how far along it each is; null when it has none to fly</summary>
    private (Sx.Vector3[] Points, float[] Along)? LineOf(BackdropRoad road)
    {
        try
        {
            if (AiLine.FileOf(road.Folder, road.Layout) is not { } file) return null;
            var points = AiLine.Read(file).Points.Select(p => new Sx.Vector3((float)p.X, (float)p.Y, (float)p.Z)).ToArray();
            if (points.Length < 2) return null;
            var along = new float[points.Length];
            for (var i = 1; i < points.Length; i++) along[i] = along[i - 1] + Sx.Vector3.Distance(points[i], points[i - 1]);
            return along[^1] > Ahead * 2 ? (points, along) : null;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not read the AI line of {Track} {Layout}", road.TrackId, road.Layout);
            return null;
        }
    }

    #endregion

    #region Rendering

    /// <summary>The camera at the start of the road's next stretch, the stretches taken in turn visit after visit</summary>
    private void StartStretch(BackdropRoad road)
    {
        var turn = _nextStretch.GetValueOrDefault(road);
        _nextStretch[road] = turn + 1;
        _stretch = _stretches[turn % _stretches.Count];
        _at = _stretch.From;
        _time = 0f;
    }

    protected override bool StepFrame(GarageRenderer renderer, float dt)
    {
        if (_line == null || _standing is not { } road) return false;

        _time += dt;
        _at += Speed * dt;

        var fade = (float)FadeOut.TotalSeconds;
        if (!_jumping)
        {
            // The road's time is up: its last seconds fade out into the next one, the camera still moving
            if (!_leaving && _roads.Count > 1 && _time >= RoadSeconds - fade)
            {
                _turn = (_turn + 1) % _roads.Count;
                Want(_roads[_turn]);
                RequestLoad();
            }
            // The one road's time is up, or the camera nears the end of what the backdrop keeps (the next road still
            // being made): through black to its other stretch, or back to the start of this one
            else if ((!_leaving && _roads.Count == 1 && _time >= RoadSeconds - fade && _stretches.Count > 1)
                     || NearsEnd(_stretch, _along![^1], _open, _at, fade))
            {
                _ = JumpAsync(renderer, road);
            }
        }

        Aim(renderer);
        return true;
    }

    /// <summary>
    /// The camera at <paramref name="at"/> is to go through black before the stretch ends: a stretch short of the line's
    /// end, or the end of an open line. A loop kept whole is flown round and round: its line comes back on itself.
    /// </summary>
    internal static bool NearsEnd((float From, float To) stretch, float length, bool open, float at, float fadeSeconds) =>
        (stretch.To < length || open) && at + Ahead + Speed * fadeSeconds >= stretch.To;

    private async Task JumpAsync(GarageRenderer renderer, BackdropRoad road)
    {
        _jumping = true;
        try
        {
            await FadeOutAsync(FadeOut);
            if (Renderer != renderer || _standing != road) return;
            StartStretch(road);
            Aim(renderer);
            FadeInAfterSettled(FadeIn);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "The road backdrop could not go to another stretch");
        }
        finally
        {
            _jumping = false;
        }
    }

    /// <summary>The camera over the line where it has got to, a little to one side, swinging slowly across, looking ahead</summary>
    private void Aim(GarageRenderer renderer)
    {
        if (_line == null || _along == null) return;

        var here = At(_at);
        var ahead = At(_at + Ahead);
        var forward = ahead - here;
        forward.Y = 0;
        if (forward.LengthSquared() < 1e-4f) return;
        forward.Normalize();
        var side = new Sx.Vector3(forward.Z, 0, -forward.X);
        var swing = Swing * MathF.Sin(_phase + _time * MathF.PI * 2 / SwingSeconds);

        var eye = here + side * swing + new Sx.Vector3(0, Height, 0);
        var look = ahead + side * (swing * 0.4f) + new Sx.Vector3(0, Height * 0.45f, 0);
        renderer.SetCamera(eye, look, FieldOfView * MathF.PI / 180f, 0f);
        renderer.Camera.NearZ = 0.5f;
        renderer.Camera.FarZ = 6000f;
        renderer.Camera.SetLens(renderer.AspectRatio);

        // AcTools' dark renderer draws everything whatever the camera looks at (for a car seen whole); a road behind
        // the camera need not be drawn
        renderer.Camera.DisableFrustum = false;
        renderer.IsDirty = true;
    }

    /// <summary>The point on the line this far along it, round again past its end (an open line's end held)</summary>
    private Sx.Vector3 At(float distance)
    {
        var along = _along!;
        var line = _line!;
        distance = OnLine(distance, along[^1], _open);
        var i = Array.BinarySearch(along, distance);
        if (i < 0) i = Math.Max(0, ~i - 1);
        var j = Math.Min(i + 1, line.Length - 1);
        var part = j == i || along[j] <= along[i] ? 0f : (distance - along[i]) / (along[j] - along[i]);
        return Sx.Vector3.Lerp(line[i], line[j], part);
    }

    /// <summary>A distance along a line this long: round again past a loop's end, held at an open line's ends</summary>
    internal static float OnLine(float distance, float length, bool open)
    {
        if (open) return Math.Clamp(distance, 0f, length);
        distance %= length;
        return distance < 0 ? distance + length : distance;
    }

    protected override void OnRendererDisposed()
    {
        _standing = null;
        _line = null;
        _along = null;
        _open = false;
        _leaving = false;
        _jumping = false;
        _stretches = [];
        CurrentTitle = null;
    }

    /// <summary>No car, no engine</summary>
    protected override void OnPoseChanged()
    {
    }

    #endregion
}
