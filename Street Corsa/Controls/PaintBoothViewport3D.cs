using System.Windows;
using System.Windows.Input;
using AcTools.Kn5File;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Controls.Street;
using StreetCorsa.Services.PaintBooth;
using StreetCorsa.Services.Scripting;
using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// The paint booth (<see cref="DriveInViewport3D"/>), seen from inside it, not from the driver's seat: the car rolls in
/// from the body shop through the open door and stops over the grate, and the camera walks round it while the player
/// picks its colour (<see cref="PaintJobShow"/>, what it wears as they pick). Then the painter, who waited by his cart,
/// goes round the car with his gun, his arm swinging up and down its sides in strokes and the spray coming out of the
/// gun, each coat going on as he goes, the camera going round ahead of him. Dragging walks the camera round by hand, the
/// wheel steps it closer or back; it never leaves the booth (<see cref="BoothCamera"/>).
/// </summary>
public class PaintBoothViewport3D : DriveInViewport3D
{
    // The camera: how quickly it goes where it is meant to be, how fast it walks round the car while the player picks,
    // the lens, where on the car it looks, and how far round ahead of the painter it keeps
    private const float CameraSettleRate = 1.6f;
    private const float WalkRound = 0.1f;
    private const float FieldOfViewDegrees = 70f;
    private const float LookAtHeight = 0.7f;
    private const float WheelStep = 0.0008f;
    private const float MinDistance = 2.4f;

    // While the car comes in, the camera stands in the booth this far round from straight behind where it stops, ahead
    // of it to its left: it sees it come through the door
    private const float ArrivingAngle = MathF.PI + 0.6f;

    // While the painter sprays, the camera is up across the car from him, a little ahead of him, looking down over the
    // roof at him and his gun; while the player picks, at a man's eye height
    private const float AcrossFromPainter = MathF.PI + 0.35f;
    private const float SprayHeightOver = 0.85f;

    // The job's time: walking in from the shop to the car, round it spraying, back out again
    private const float WalkOutShare = 0.14f;
    private const float WalkBackShare = 0.1f;

    // How square the painter's way round the car is (a superellipse's power: 2 an ellipse, higher squarer)
    private const float RoundSquareness = 4f;

    // His step: how high he bobs, how often
    private const float Bob = 0.015f;
    private const float StepsPerMetre = 1.6f;

    private PaintJobShow? _show;
    private Kn5RenderableList? _arm;
    private Kn5RenderableList? _mist;
    private Sx.Matrix _armHome = Sx.Matrix.Identity;
    private Sx.Matrix _mistHome = Sx.Matrix.Identity;
    private DateTime _sprayStarted;
    private bool _wasSpraying;
    private int _painterLoad;

    // Where the camera is now: round where the car stands (radians, 0 behind it), how far, how high
    private float _cameraAngle = float.NaN;
    private float _distance = float.NaN;
    private float _height = float.NaN;
    private float _zoom = 1f;

    // Where the car stands in the booth, painted: the camera goes round it, from before the car is there
    private (float X, float Z, float Heading)? _home;

    public PaintBoothViewport3D()
        : base("PaintBooth")
    {
    }

    protected override string ViewportName => "Paint booth viewport";

    protected override string ArriveTrigger => ScriptTriggers.BoothArrive;

    protected override string StandTrigger => ScriptTriggers.BoothStanding;

    protected override string LeaveTrigger => ScriptTriggers.BoothLeave;

    // The camera can go all the way round the car
    protected override float MinLookYaw => -180f;

    /// <summary>The sun's shadows round the car where it is, its and the painter's on the floor under it</summary>
    protected override void FocusShadows(GarageRenderer renderer)
    {
        base.FocusShadows(renderer);
        renderer.ShadowsCentre = CarPosition;
    }

    #region Dependency properties

    public static readonly DependencyProperty ShowProperty = DependencyProperty.Register(
        nameof(Show), typeof(PaintJobShow), typeof(PaintBoothViewport3D),
        new PropertyMetadata(null, (d, _) => ((PaintBoothViewport3D)d).OnShowChanged()));

    /// <summary>What the car wears and what the painter does</summary>
    public PaintJobShow? Show
    {
        get => (PaintJobShow?)GetValue(ShowProperty);
        set => SetValue(ShowProperty, value);
    }

    #endregion

    private PaintBoothScene? Booth => Scene as PaintBoothScene;

    #region The show

    private void OnShowChanged()
    {
        if (IsLoaded) ListenToShow(Show);
    }

    protected override void ListenToStage(bool listen)
    {
        base.ListenToStage(listen);
        ListenToShow(listen ? Show : null);
    }

    private void ListenToShow(PaintJobShow? show)
    {
        if (ReferenceEquals(show, _show)) return;
        if (_show != null)
        {
            _show.LookChanged -= OnLookChanged;
            _show.SprayChanged -= OnSprayChanged;
            _show.Coated -= OnCoated;
        }

        _show = show;
        if (_show != null)
        {
            _show.LookChanged += OnLookChanged;
            _show.SprayChanged += OnSprayChanged;
            _show.Coated += OnCoated;
        }
    }

    private void OnLookChanged()
    {
        if (IsSceneUp && Renderer is { } renderer) _ = WearAsync(renderer);
    }

    /// <summary>The car in the skin and paint the show has it in</summary>
    private async Task WearAsync(GarageRenderer renderer)
    {
        if (Renderer != renderer || _show is not { } show || renderer.CarNode is not { } car) return;
        try
        {
            // The skin as the paint is worked out in (Looks.CarPaint.SkinOf): an id with no folder is the car's first skin
            var skin = Looks.CarPaint.SkinOf(PlayerCar?.Directory ?? car.RootDirectory, show.Skin);
            if (!string.Equals(car.CurrentSkin, skin, StringComparison.OrdinalIgnoreCase))
                renderer.MainSlot.SelectSkin(skin ?? Kn5RenderableCar.DefaultSkin);
            await ShowPartsAsync(renderer, car, show.Shown);
            if (Renderer != renderer) return;
            Invalidate();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "The car could not be shown in its new paint");
        }
    }

    private void OnCoated()
    {
        if (_show?.Coat is not { } coat || Renderer is not { } renderer || renderer.CarNode is not { } car) return;
        ShownPartsOnCar.Coat(car, renderer.DeviceContextHolder, coat);
        Invalidate();
    }

    private void OnSprayChanged()
    {
        if (_show?.IsSpraying == true) _sprayStarted = DateTime.UtcNow;
        AnimateFor(TimeSpan.FromSeconds(1));
        Invalidate();
    }

    #endregion

    #region The painter

    /// <summary>The booth and the car are up: the painter comes to wait by his cart, and the car wears what the show says</summary>
    protected override void OnSceneUp(GarageRenderer renderer)
    {
        _home = Booth?.ScriptMarks.Stand(Booth.StandMark, "arrive") is { } home ? (home.X, home.Z, home.Heading) : null;
        _ = PainterAsync(renderer, ++_painterLoad);
        if (_show?.Shown != null || _show?.Skin != null) _ = WearAsync(renderer);
    }

    protected override void OnRendererDisposed()
    {
        base.OnRendererDisposed();
        _painterLoad++;
        _arm = null;
        _mist = null;
        _cameraAngle = float.NaN;
        _distance = float.NaN;
        _height = float.NaN;
        _home = null;
    }

    private async Task PainterAsync(GarageRenderer renderer, int load)
    {
        if (Booth is not { } booth || booth.PainterKn5.Length == 0) return;
        try
        {
            var kn5 = await Task.Run(() => Kn5.FromFile(booth.PainterKn5));
            if (load != _painterLoad || Renderer != renderer) return;
            renderer.SetFixture(kn5, PainterAt(booth.Round.Wait.X, booth.Round.Wait.Z, FacingCar(booth.Round.Wait.X, booth.Round.Wait.Z), 0f));
            _arm = renderer.Fixture?.GetDummyByName("ARM");
            _mist = renderer.Fixture?.GetDummyByName("MIST");
            _armHome = _arm?.LocalMatrix ?? Sx.Matrix.Identity;
            _mistHome = _mist?.LocalMatrix ?? Sx.Matrix.Identity;
            if (_mist != null) _mist.IsEnabled = false;
            Invalidate();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "The painter could not be put in the booth: the car is painted without him");
        }
    }

    private static Sx.Matrix PainterAt(float x, float z, float yaw, float bob) =>
        Sx.Matrix.RotationY(yaw) * Sx.Matrix.Translation(x, bob, z);

    /// <summary>The way from a point to the car's middle, as a turn (0 faces +z)</summary>
    private float FacingCar(float x, float z)
    {
        var car = CarPosition;
        return MathF.Atan2(car.X - x, car.Z - z);
    }

    /// <summary>The car's half length and half width, from its box as it stands; something car-sized without one</summary>
    private (float Length, float Width) CarHalves()
    {
        if (Renderer?.MainSlot.GetCarBoundingBox() is { } box && float.IsFinite(box.Minimum.X) && box.Maximum.X > box.Minimum.X)
        {
            // The box is the world's; the booth's car stands along z or along x
            var (dx, dz) = ((box.Maximum.X - box.Minimum.X) / 2f, (box.Maximum.Z - box.Minimum.Z) / 2f);
            var alongZ = MathF.Abs(MathF.Cos(CarHeading)) > 0.7f;
            return alongZ ? (dz, dx) : (dx, dz);
        }
        return (2.4f, 0.95f);
    }

    /// <summary>
    /// Where the painter is on his way round the car at <paramref name="t"/> (0..1 of a lap, from beside its nose on its
    /// left), in the world, and which way he faces: at the car's long middle line, square onto its sides
    /// </summary>
    private (float X, float Z, float Yaw) RoundAt(float t, BoothRound round)
    {
        var (halfLength, halfWidth) = CarHalves();
        var a = halfLength + round.Gap;
        var b = halfWidth + round.Gap;

        // In the car's own space: +z ahead, +x to its left
        var angle = MathF.PI / 2f - t * MathF.Tau;
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        var p = 2f / RoundSquareness;
        var localX = b * MathF.Sign(c) * MathF.Pow(MathF.Abs(c), p);
        var localZ = a * MathF.Sign(s) * MathF.Pow(MathF.Abs(s), p);

        // He faces the nearest point of the car's middle line, short of its ends: square onto a side, along the car at either end
        var reach = Math.Max(0f, halfLength - 0.7f);
        var aimX = -localX;
        var aimZ = Math.Clamp(localZ, -reach, reach) - localZ;

        var heading = CarHeading;
        var (cos, sin) = (MathF.Cos(heading), MathF.Sin(heading));
        var car = CarPosition;
        var x = car.X + localX * cos + localZ * sin;
        var z = car.Z - localX * sin + localZ * cos;
        var worldAimX = aimX * cos + aimZ * sin;
        var worldAimZ = -aimX * sin + aimZ * cos;
        return (x, z, MathF.Atan2(worldAimX, worldAimZ));
    }

    /// <summary>
    /// The painter where the job has him: walking out from his cart, round the car spraying, walking back; at his cart
    /// when there is no job. True while he moves.
    /// </summary>
    private bool StepPainter(GarageRenderer renderer)
    {
        if (renderer.Fixture == null || Booth is not { } booth) return false;
        var round = booth.Round;
        var wait = round.Wait;

        var spraying = _show?.IsSpraying == true;
        if (!spraying)
        {
            if (_wasSpraying)
            {
                _wasSpraying = false;
                renderer.MoveFixture(PainterAt(wait.X, wait.Z, FacingCar(wait.X, wait.Z), 0f));
                if (_mist != null) _mist.IsEnabled = false;
                if (_arm != null) _arm.LocalMatrix = _armHome;
            }
            return false;
        }
        _wasSpraying = true;

        var total = (float)Math.Max(1, _show!.SprayTime.TotalSeconds);
        var elapsed = (float)(DateTime.UtcNow - _sprayStarted).TotalSeconds;
        var share = Math.Clamp(elapsed / total, 0f, 1f);

        // The lap starts beside the car nearest his cart
        var start = NearestOnRound(wait, round);
        float x, z, yaw, walked;
        var sprays = false;
        var lapShare = 1f - WalkOutShare - WalkBackShare;
        if (share < WalkOutShare)
        {
            var k = Smooth(share / WalkOutShare);
            var to = RoundAt(start, round);
            (x, z) = (wait.X + (to.X - wait.X) * k, wait.Z + (to.Z - wait.Z) * k);
            yaw = MathF.Atan2(to.X - wait.X, to.Z - wait.Z);
            walked = k * MathF.Sqrt((to.X - wait.X) * (to.X - wait.X) + (to.Z - wait.Z) * (to.Z - wait.Z));
        }
        else if (share < WalkOutShare + lapShare)
        {
            var lap = (share - WalkOutShare) / lapShare;
            (x, z, yaw) = RoundAt(start + lap, round);
            var (halfLength, halfWidth) = CarHalves();
            walked = lap * 4f * (halfLength + halfWidth + 2 * round.Gap);
            sprays = true;
        }
        else
        {
            var k = Smooth((share - WalkOutShare - lapShare) / WalkBackShare);
            var from = RoundAt(start + 1f, round);
            (x, z) = (from.X + (wait.X - from.X) * k, from.Z + (wait.Z - from.Z) * k);
            yaw = k < 0.85f ? MathF.Atan2(wait.X - from.X, wait.Z - from.Z) : FacingCar(wait.X, wait.Z);
            walked = k * MathF.Sqrt((wait.X - from.X) * (wait.X - from.X) + (wait.Z - from.Z) * (wait.Z - from.Z));
        }

        var bob = Bob * MathF.Abs(MathF.Sin(walked * StepsPerMetre * MathF.PI));
        renderer.MoveFixture(PainterAt(x, z, yaw, bob));

        // His gun up and down the side in strokes, the spray out of it while he sprays; a flicker in the spray's size
        if (_arm != null)
        {
            var strokes = round.Strokes * 4f * elapsed / (total * lapShare);
            var swing = sprays ? (0.5f - 0.5f * MathF.Cos(strokes * MathF.Tau)) : 0.35f;
            var pitch = -(round.Low + (round.High - round.Low) * swing) * MathF.PI / 180f;
            _arm.LocalMatrix = Sx.Matrix.RotationX(pitch) * _armHome;
        }
        if (_mist != null)
        {
            _mist.IsEnabled = sprays;
            var flicker = 1f + 0.08f * MathF.Sin(elapsed * 23f) + 0.05f * MathF.Sin(elapsed * 37f);
            _mist.LocalMatrix = Sx.Matrix.Scaling(flicker, flicker, 1f + 0.06f * MathF.Sin(elapsed * 17f)) * _mistHome;
        }

        _painterAngle = CameraAngleOf(x, z);
        renderer.RefreshShadows();
        return true;
    }

    // Round the car where the painter is, as the camera measures it
    private float _painterAngle;

    /// <summary>The lap's start nearest a point (beside the car where he comes from)</summary>
    private float NearestOnRound(Services.Station.GroundPoint from, BoothRound round)
    {
        var best = 0f;
        var nearest = float.MaxValue;
        for (var i = 0; i < 48; i++)
        {
            var t = i / 48f;
            var (x, z, _) = RoundAt(t, round);
            var d = (x - from.X) * (x - from.X) + (z - from.Z) * (z - from.Z);
            if (d < nearest) (nearest, best) = (d, t);
        }
        return best;
    }

    private static float Smooth(float k) => k * k * (3f - 2f * k);

    #endregion

    #region Camera

    /// <summary>Where the car stands painted: the middle of the camera's round</summary>
    private (float X, float Z, float Heading) Home => _home ?? (CarPosition.X, CarPosition.Z, CarHeading);

    /// <summary>How far round where the car stands a point is, as the camera measures it: 0 straight behind it</summary>
    private float CameraAngleOf(float x, float z)
    {
        var home = Home;
        var world = MathF.Atan2(x - home.X, z - home.Z);
        return world - home.Heading - MathF.PI;
    }

    /// <summary>Where the camera means to be now, round the car</summary>
    private float WantedAngle(float dt)
    {
        var drag = LookYaw * MathF.PI / 180f;
        var phase = Stage?.Phase;
        if (_show?.IsSpraying == true) return _painterAngle + AcrossFromPainter + drag;
        if (phase is not DriveInPhase.Standing) return ArrivingAngle + drag;

        // Standing there: the camera walks slowly round the car while the player picks
        _walk += WalkRound * dt;
        return ArrivingAngle + _walk + drag;
    }

    private float _walk;

    protected override void AimView()
    {
        if (Renderer is not { } renderer || renderer.Camera is not { } camera || Booth is not { } booth) return;

        if (float.IsNaN(_cameraAngle)) _cameraAngle = WantedAngle(0f);
        if (float.IsNaN(_distance)) _distance = booth.Camera.Distance;
        if (float.IsNaN(_height)) _height = booth.Camera.Height;

        // Round where the car stands, looking at the car where it is (on its way in, as it comes)
        var home = Home;
        var car = CarPosition;
        var target = new Sx.Vector3(car.X, LookAtHeight, car.Z);
        var world = _cameraAngle + home.Heading + MathF.PI;
        var direction = new Sx.Vector3(MathF.Sin(world), 0f, MathF.Cos(world));

        // As far out as wanted, but inside the walls
        var reach = _distance;
        var cam = booth.Camera;
        if (direction.X > 1e-4f) reach = Math.Min(reach, (cam.Max.X - home.X) / direction.X);
        if (direction.X < -1e-4f) reach = Math.Min(reach, (cam.Min.X - home.X) / direction.X);
        if (direction.Z > 1e-4f) reach = Math.Min(reach, (cam.Max.Z - home.Z) / direction.Z);
        if (direction.Z < -1e-4f) reach = Math.Min(reach, (cam.Min.Z - home.Z) / direction.Z);
        reach = Math.Max(0.5f, reach);

        var eye = new Sx.Vector3(home.X + direction.X * reach, _height, home.Z + direction.Z * reach);
        camera.FovY = FieldOfViewDegrees * MathF.PI / 180f;
        camera.LookAt(eye, target, Sx.Vector3.UnitY);
        camera.SetLens(renderer.AspectRatio);
        renderer.IsDirty = true;
    }

    /// <summary>The camera eased towards where it means to be; true while it still moves</summary>
    private bool StepCamera(float dt)
    {
        if (Booth is not { } booth) return false;
        var wanted = WantedAngle(dt);
        if (float.IsNaN(_cameraAngle)) _cameraAngle = wanted;
        var k = 1f - MathF.Exp(-CameraSettleRate * Math.Min(dt, GarageRenderer.MaxTickSeconds * 2f));
        var delta = (float)Math.IEEERemainder(wanted - _cameraAngle, Math.PI * 2);
        _cameraAngle += delta * k;

        var distance = Math.Max(MinDistance, booth.Camera.Distance * _zoom);
        if (float.IsNaN(_distance)) _distance = distance;
        _distance += (distance - _distance) * k;

        var height = booth.Camera.Height + (_show?.IsSpraying == true ? SprayHeightOver : 0f);
        if (float.IsNaN(_height)) _height = height;
        _height += (height - _height) * k;

        AimView();
        return MathF.Abs(delta) > 0.002f || MathF.Abs(distance - _distance) > 0.002f || MathF.Abs(height - _height) > 0.002f
               || Stage?.Phase == DriveInPhase.Standing;
    }

    protected override void LookBack()
    {
        LookYaw = 0f;
        _zoom = 1f;
        AnimateFor(TimeSpan.FromSeconds(2));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _zoom = Math.Clamp(_zoom * (1f - e.Delta * WheelStep), 0.5f, 1.4f);
        AnimateFor(TimeSpan.FromSeconds(2));
        e.Handled = true;
    }

    #endregion

    #region Rendering

    protected override bool StepFrame(GarageRenderer renderer, float dt)
    {
        var moving = base.StepFrame(renderer, dt);
        var painting = StepPainter(renderer);
        var camera = StepCamera(dt);
        return moving || painting || camera;
    }

    #endregion
}
