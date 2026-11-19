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

    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(1100);

    private readonly Random _random = new();

    private List<BackdropRoad> _roads = [];
    private int _turn;
    private BackdropRoad? _wanted;
    private BackdropRoad? _standing;

    // The AI line of the road on screen as distances along it, and where the camera is on it
    private Sx.Vector3[]? _line;
    private float[]? _along;
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
        var roads = Roads?.ToList() ?? [];

        // The same roads again (the list made anew): the one on screen stays
        if (roads.SequenceEqual(_roads)) return;

        _roads = roads;
        _turn = 0;
        _wanted = _roads.FirstOrDefault();
        _leaving = false;
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

        // Made off the UI thread the first time, the road before still flown meanwhile
        var file = await RoadBackdrops.GetAsync(road.Folder, road.TrackId, road.Layout);
        if (!IsLoaded || _wanted != road) return;

        var line = file == null ? null : await Task.Run(() => LineOf(road));
        if (!IsLoaded || _wanted != road) return;

        if (file == null || line == null)
        {
            // A road that cannot be filmed is left out of the turn
            Logger.Warning("{Track} {Layout} cannot be filmed; left out", road.TrackId, road.Layout);
            _roads.Remove(road);
            _wanted = _roads.Count == 0 ? null : _roads[_turn % _roads.Count];
            RequestLoad();
            return;
        }

        if (Renderer != null && !IsPictureHidden)
        {
            await FadeOutAsync(FadeOut);
            if (!IsLoaded || _wanted != road) return;
        }

        DisposeRenderer(releaseDevice: false);

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var renderer = NewRenderer(null, file);

        // A road is kilometres across: no shadows or screen-space effects worked out over all of it, nothing moved but
        // the camera
        renderer.EnableShadows = false;
        renderer.UsePcss = false;
        renderer.UseSslr = false;
        renderer.UseAo = false;
        renderer.LoadCarLights = false;
        renderer.TryToGuessCarLights = false;
        renderer.AutoAdjustTarget = false;
        renderer.CubemapReflectionMapSize = 256;
        renderer.FlatMirror = false;

        // Where the road draws nothing the renderer clears to its background at no opacity, and the picture is laid
        // over the screen premultiplied: black there is clear, and the screen's sky shows through (any other colour
        // would be added to it)
        renderer.BackgroundColor = System.Drawing.Color.Black;

        if (!await StartRendererAsync(renderer)) return;
        if (Renderer != renderer || _wanted != road) return;

        _standing = road;
        (_line, _along) = line.Value;
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
            _ = RoadBackdrops.GetAsync(next.Folder, next.TrackId, next.Layout);
    }

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
                _leaving = true;
                _turn = (_turn + 1) % _roads.Count;
                _wanted = _roads[_turn];
                RequestLoad();
            }
            // The one road's time is up, or the camera nears the end of what the backdrop keeps (the next road still
            // being made): through black to its other stretch, or back to the start of this one
            // (a road kept whole is flown round and round: its line comes back on itself)
            else if ((!_leaving && _roads.Count == 1 && _time >= RoadSeconds - fade && _stretches.Count > 1)
                     || (_stretch.To < _along![^1] && _at + Ahead + Speed * fade >= _stretch.To))
            {
                _ = JumpAsync(renderer, road);
            }
        }

        Aim(renderer);
        return true;
    }

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
        renderer.IsDirty = true;
    }

    /// <summary>The point on the line this far along it, round again past its end</summary>
    private Sx.Vector3 At(float distance)
    {
        var along = _along!;
        var line = _line!;
        var length = along[^1];
        distance %= length;
        if (distance < 0) distance += length;
        var i = Array.BinarySearch(along, distance);
        if (i < 0) i = Math.Max(0, ~i - 1);
        var j = Math.Min(i + 1, line.Length - 1);
        var part = j == i || along[j] <= along[i] ? 0f : (distance - along[i]) / (along[j] - along[i]);
        return Sx.Vector3.Lerp(line[i], line[j], part);
    }

    protected override void OnRendererDisposed()
    {
        _standing = null;
        _line = null;
        _along = null;
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
