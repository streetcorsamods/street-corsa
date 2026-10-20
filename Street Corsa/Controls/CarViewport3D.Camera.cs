using System.IO;
using System.Windows;
using System.Windows.Input;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Controls.Showcase;
using StreetCorsa.Logging;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.Text;
using static StreetCorsa.Helpers.Matrices;

namespace StreetCorsa.Controls;

/// <summary>
/// The camera: its framing of the car, the stand or a part, eased there frame by frame, and stood clear of the screen's
/// panels; and the pointer, dragging it round, picking parts and the props under it
/// </summary>
public partial class CarViewport3D
{
    /// <summary>
    /// Frames the car. <paramref name="immediate"/> puts the camera there at once, which is right for the
    /// first car of a session; otherwise it moves there over the next moment.
    /// </summary>
    private void ResetCamera(bool immediate = false)
    {
        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        _radiusGoal = DefaultRadius;
        _alphaGoal = DefaultAlpha;
        _betaGoal = DefaultBeta;
        _targetGoal = null;
        _framingCar = false;
        _fitRadius = false;
        _followCarry = false;
        _cameraOnPart = false;
        _cameraWaitsOnStand = false;
        _zooming = false;
        _handHeld = false;
        _hand.Stop();

        // The renderer's own framing (the middle of the car's box's corners as they come out on the screen, a third of the
        // way every frame) is never used: it leaned toward the car's near end, and knew nothing of the panels. The camera
        // looks at the car's middle itself, moved over clear of the panels (StepCamera).
        Renderer!.AutoAdjustTarget = false;

        if (IsOnStand)
        {
            // Close in on the engine on the stand, a little above the crankshaft
            var spot = EngineStandLayout.Spot;
            _targetGoal = new SlimDX.Vector3(spot.X, EngineStandLayout.Spindle.Y - 0.05f, spot.Z - 0.1f);
            _radiusGoal = StandRadius;
            _alphaGoal = StandAlpha;
            _betaGoal = StandBeta;
        }
        else if (Renderer.CarNode == null)
        {
            // Empty garage: nothing to frame, so look across the room at eye level instead of at the floor
            _targetGoal = new SlimDX.Vector3(0f, EmptyGarageEyeHeight, 0f);
            _radiusGoal = MaxRadius;
            _betaGoal = MinBeta;
        }
        else
        {
            // The car: its middle, from as far back as takes it in between the panels. Its size is from its wheels:
            // the renderer's own box for a car takes in its shadow planes and comes out a metre or more too long.
            _framingCar = true;
            _fitRadius = true;
            _carFrame = CarFrame.Of(Renderer.MainSlot);
            _targetGoal = CarCentre();
            _radiusGoal = FitRadius();
        }

        if (immediate)
        {
            orbit.Radius = _radiusGoal;
            orbit.Alpha = _alphaGoal;
            orbit.Beta = _betaGoal;
            if (_targetGoal is { } target)
            {
                _lookAt = target;
                _shiftNow = SideShift(orbit.Radius, orbit.FovY);
                PlaceTarget(orbit);
            }
        }

        AnimateFor(AnimationWindow);
    }

    /// <summary>
    /// The point of the car the camera looks at (<see cref="PreviewFraming.CarMiddle"/>) where the car stands now: the
    /// lift takes it up, a script drives it, its node's matrix has both. Null without a car.
    /// </summary>
    private SlimDX.Vector3? CarCentre()
    {
        if (Renderer?.CarNode is not { } car || _carFrame is not { } frame) return null;

        var middle = PreviewFraming.CarMiddle(frame);
        return SlimDX.Vector3.TransformCoordinate(new SlimDX.Vector3(middle.X, middle.Y, middle.Z), car.Matrix);
    }

    /// <summary>
    /// How far back the camera stands to take the car in between the panels (<see cref="PreviewFraming.GarageDistance"/>),
    /// looking down from where it is meant to; remembers what it was fitted to, so it is worked out again as that changes
    /// </summary>
    private float FitRadius()
    {
        // A car not there yet is fitted again on the next frame
        if (Renderer is not { CameraOrbit: { } orbit, CarNode: not null } || _carFrame is not { } frame) return DefaultRadius;

        _fittedTo = Framing;
        var fit = PreviewFraming.GarageDistance(PreviewFraming.CarSize(frame), orbit.FovY, UncoveredAspect, _betaGoal);
        return Math.Clamp(fit, MinRadius, MaxRadius);
    }

    /// <summary>What the fit depends on besides the car: the picture and what the panels cover of it</summary>
    private (double Left, double Right, double Width, double Height) Framing => (CoveredLeft, CoveredRight, ActualWidth, ActualHeight);

    /// <summary>
    /// Points the orbit at what the camera has got to looking at, moved to the camera's left by the side shift so it
    /// stands in the middle of what the panels leave (<see cref="PreviewFraming.CameraLeft"/>)
    /// </summary>
    private void PlaceTarget(CameraOrbit orbit)
    {
        if (_lookAt is not { } at) return;

        var left = PreviewFraming.CameraLeft(orbit.Alpha);
        var target = at + new SlimDX.Vector3(left.X, left.Y, left.Z) * _shiftNow;
        if (target != orbit.Target) orbit.Target = target;
        _placedTarget = target;
    }

    /// <summary>
    /// The camera settled on the car before a script moves it: the base keeps the car where it has it in the picture,
    /// and would otherwise hold it short of the middle. Looking at something else (a part, the bench, the hoist's load)
    /// it is left where it is: snapped there, the picture would jump before the script.
    /// </summary>
    private void SettleOnTarget()
    {
        if (!_framingCar || Renderer?.CameraOrbit is not { } orbit || _targetGoal is not { } goal) return;

        _lookAt = goal;
        _shiftNow = SideShift(orbit.Radius, orbit.FovY);
        PlaceTarget(orbit);
        orbit.UpdateViewMatrix();
    }

    /// <summary>The camera is somebody else's: a part, the work, the hoist. The car's middle is not followed, nor fitted.</summary>
    private void HoldCamera()
    {
        _framingCar = false;
        _fitRadius = false;
    }

    // How far inside the room's walls and under its beams the camera keeps, and how close they may push it in
    private const float WallMargin = 0.35f;
    private const float MinRoomRadius = 1.0f;

    /// <summary>
    /// How far from <paramref name="target"/> the camera can stand, looking from <paramref name="alpha"/> and
    /// <paramref name="beta"/>, and still be inside the garage's walls and under its beams (<see cref="Room"/>); no limit
    /// in a garage that gives none. The orbit stands the camera at target + radius * (cos beta cos alpha, sin beta, cos beta sin alpha).
    /// </summary>
    private float RoomLimit(float alpha, float beta, SlimDX.Vector3 target)
    {
        if (Room?.Walls is not { } walls) return float.MaxValue;

        var level = MathF.Cos(beta);
        var limit = Math.Min(
            Reach(target.X, level * MathF.Cos(alpha), walls.MinX, walls.MaxX),
            Reach(target.Z, level * MathF.Sin(alpha), walls.MinZ, walls.MaxZ));

        var up = MathF.Sin(beta);
        if (up > 1e-4f) limit = Math.Min(limit, (walls.Height - WallMargin - target.Y) / up);

        return Math.Max(MinRoomRadius, limit);

        // Along one axis, from a point between walls at min and max
        static float Reach(float from, float step, float min, float max)
        {
            if (Math.Abs(step) < 1e-4f) return float.MaxValue;
            var wall = step > 0f ? max - WallMargin : min + WallMargin;
            return Math.Max(0f, (wall - from) / step);
        }
    }

    /// <summary>The share of what is left a move at <paramref name="rate"/> a second covers in <paramref name="dt"/></summary>
    private static float Ease(float rate, float dt) => 1f - MathF.Exp(-rate * dt);

    /// <summary>
    /// Eases the camera toward its framing. Returns true while it is still moving, so the render loop knows
    /// to keep drawing. A share of what is left every second, so it comes out the same at any frame rate: a slow one
    /// for a framing the garage picked, a quick one under the player's hand, so the picture follows the mouse closely
    /// without jumping a mouse event at a time.
    /// </summary>
    private bool StepCamera(float dt)
    {
        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return false;

        var k = Ease(CameraSettleRate, dt);
        var moving = false;

        // A script moved the target since the last frame (the base keeps its car where it has it in the picture): what
        // the camera looks at goes along, and eases on from there
        if (_lookAt is { } looked) _lookAt = looked + (orbit.Target - _placedTarget);

        // On the car: its middle, wherever the lift or the day's work has it; a script's car is left to the script. As
        // far back as fits it in what the panels leave, worked out again as a panel comes or goes or the view is resized.
        if (IsScriptMoving)
        {
            // The script's: where it has moved the target to is where the camera means to look. Eased on toward where
            // the car was, the look would trail a followed car by its speed over the settle rate.
            if (_lookAt is { } following) _targetGoal = following;
        }
        else if (_framingCar)
        {
            if (CarCentre() is { } centre) _targetGoal = centre;
            if (_fitRadius && _fittedTo != Framing) _radiusGoal = FitRadius();
        }

        // The hand let go on the move carries the camera on a little
        var thrown = _hand.Step(dt);
        if (thrown != System.Numerics.Vector2.Zero)
        {
            if (Renderer!.AutoRotate) orbit.Alpha += thrown.X;
            else _alphaGoal += thrown.X;
            _betaGoal += thrown.Y;
        }
        var handOn = _isDragging && _handHeld || _hand.IsFlinging;
        var turnK = handOn ? Ease(HandSettleRate, dt) : k;

        // Never under the floor (nor past looking up from under the lifted car), never over the top
        _betaGoal = Math.Clamp(_betaGoal, LowestBeta(_radiusGoal, (_lookAt ?? orbit.Target).Y), MaxBeta);

        // Never through a wall, not even for a frame: how far back the camera can stand depends on which way it looks from
        var limit = RoomLimit(orbit.Alpha, orbit.Beta, orbit.Target);
        var radiusGoal = Math.Min(_radiusGoal, limit);
        if (Math.Abs(radiusGoal - orbit.Radius) > CameraSettled)
        {
            orbit.Radius += (radiusGoal - orbit.Radius) * (_zooming ? Ease(ZoomSettleRate, dt) : k);
            moving = true;
        }
        else
        {
            _zooming = false;
        }

        orbit.Radius = Math.Min(orbit.Radius, limit);

        if (Math.Abs(_betaGoal - orbit.Beta) > CameraSettled)
        {
            orbit.Beta += (_betaGoal - orbit.Beta) * turnK;
            moving = true;
        }

        // Never under the floor, not even for a frame: the wheel backs the camera off quicker than beta comes up after
        // its goal, and the eye would dip under the floor on the way
        orbit.Beta = Math.Max(orbit.Beta, FloorBeta(orbit.Radius, (_lookAt ?? orbit.Target).Y));

        if (Renderer!.AutoRotate)
        {
            // The renderer is turning the car itself, a little further every tick. Easing alpha back to a
            // goal at the same time would cancel that out and leave the car shivering on the spot instead of
            // going round, so the goal follows the renderer until auto-rotate is switched off.
            _alphaGoal = orbit.Alpha;
        }
        else
        {
            // Round the short way
            var delta = (float)Math.IEEERemainder(_alphaGoal - orbit.Alpha, Math.PI * 2);
            if (Math.Abs(delta) > CameraSettled)
            {
                orbit.Alpha += delta * turnK;
                moving = true;
            }
        }

        // A fling dies down over frames of its own: drawn until it has
        if (_hand.IsFlinging) moving = true;

        if (_targetGoal is { } target)
        {
            var at = _lookAt ?? target;
            var toTarget = target - at;
            if (toTarget.LengthSquared() > CameraSettled * CameraSettled)
            {
                at += toTarget * k;
                moving = true;
            }
            else
            {
                at = target;
            }

            _lookAt = at;
        }

        // Clear of the panels: the shift goes with the distance, and a panel coming or going moves the car over gently
        var shiftGoal = SideShift(orbit.Radius, orbit.FovY);
        if (Math.Abs(shiftGoal - _shiftNow) > CameraSettled)
        {
            _shiftNow += (shiftGoal - _shiftNow) * k;
            moving = true;
        }
        else
        {
            _shiftNow = shiftGoal;
        }

        PlaceTarget(orbit);
        return moving;
    }

    private bool HitsRadio(System.Windows.Point position) =>
        Renderer != null && ActualWidth > 0 && ActualHeight > 0
        && Renderer.HitsFixture((float)(position.X / ActualWidth), (float)(position.Y / ActualHeight));

    // The camera on a part the player picked: it turns round the part, near enough to see it, and
    // goes back to the car (or the stand) as nothing is picked any more, as close as it was before
    private const float FocusFill = 2.4f;
    private const float FocusMargin = 0.7f;
    private const float FocusMaxRadius = 3.6f;
    private bool _cameraOnPart;
    private float _radiusBeforeFocus = DefaultRadius;
    private bool _fitBeforeFocus;

    /// <summary>
    /// The camera goes to the selected part in the parts view and turns round it from where it looks now; back to the car
    /// when none is selected, the view closes or a job takes the camera. Runs from property callbacks, so nothing may
    /// escape it.
    /// </summary>
    private void FocusSelection()
    {
        try
        {
            if (Renderer is not { CameraOrbit: { } orbit } renderer || _cameraOnWork) return;

            var box = PartsVisible && renderer.SelectedPart is { } node ? renderer.PartBox(node) : null;
            if (box is { } b && float.IsFinite(b.Minimum.X) && float.IsFinite(b.Maximum.X))
            {
                if (!_cameraOnPart)
                {
                    _radiusBeforeFocus = _radiusGoal;
                    _fitBeforeFocus = _fitRadius;
                }
                _cameraOnPart = true;

                HoldCamera();
                _targetGoal = (b.Minimum + b.Maximum) / 2f;
                var size = (b.Maximum - b.Minimum).Length();
                var least = IsOnStand ? StandMinRadius : PartsMinRadius;
                _radiusGoal = Math.Clamp(size * FocusFill + FocusMargin, least, FocusMaxRadius);
                _alphaGoal = orbit.Alpha;
                // Never under the floor, round a part low in the car
                _betaGoal = Math.Max(orbit.Beta, LowestBeta(_radiusGoal, _targetGoal.Value.Y));
                AnimateFor(AnimationWindow);
                return;
            }

            if (!_cameraOnPart) return;
            _cameraOnPart = false;

            if (IsOnStand || renderer.CarNode == null)
            {
                ResetCamera();
                return;
            }

            // Back round the car from where the camera looks now, as near as it was (fitted again if it was fitted)
            _alphaGoal = orbit.Alpha;
            _betaGoal = orbit.Beta;
            _radiusGoal = Math.Clamp(_radiusBeforeFocus, renderer.HasProp ? PartsMinRadius : MinRadius, MaxRadius);
            _framingCar = true;
            _fitRadius = _fitBeforeFocus;
            _fittedTo = null;
            _targetGoal = CarCentre();
            AnimateFor(AnimationWindow);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not bring the camera to the selected part");
        }
    }

    #region Camera input

    /// <summary>
    /// The hand's clock, in seconds: the stopwatch's, which a clock change never moves (so never throws the camera) and
    /// which tells a mouse's reports apart (TickCount64 ticks 15.6 ms apart, and a mouse reports many moves in that)
    /// </summary>
    private static double Now => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Renderer == null) return;

        _isDragging = true;
        _handHeld = false;
        _lastMouse = _pressedAt = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void ForgetPress()
    {
        _isDragging = false;
        _handHeld = false;
        _hand.Stop();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var wasDragging = _isDragging;
        _isDragging = false;
        LetGo();
        ReleaseMouseCapture();

        var position = e.GetPosition(this);
        if (wasDragging && (position - _pressedAt).Length <= ClickSlack) OnClick(position);
    }

    /// <summary>The drag was let go of elsewhere (another window took the pointer): as let go here</summary>
    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_isDragging) return;

        _isDragging = false;
        LetGo();
    }

    /// <summary>
    /// The hand comes off the camera: let go on the move, it is thrown on a little (<see cref="OrbitHand"/>). A click
    /// with a pixel of jitter in it picks what is under the pointer and throws nothing.
    /// </summary>
    private void LetGo()
    {
        if (!_handHeld) return;

        _handHeld = false;
        if ((_lastMouse - _pressedAt).Length <= ClickSlack) _hand.Stop();
        else _hand.Release(Now);
        if (_hand.IsFlinging) Invalidate();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        var position = e.GetPosition(this);
        if (!_isDragging)
        {
            UpdateHover(position);
            return;
        }

        if (!_handHeld)
        {
            // The hand takes the camera from where it is, not from where it was going
            _handHeld = true;
            _hand.Stop();
            _alphaGoal = orbit.Alpha;
            _betaGoal = orbit.Beta;
        }

        // The drag turns the goals; the orbit follows them closely (StepCamera). Under auto-rotate the goal follows the
        // renderer's own turning, so the hand turns the orbit itself there.
        var turn = _hand.Drag(position.X - _lastMouse.X, position.Y - _lastMouse.Y, Now);
        if (Renderer!.AutoRotate) orbit.Alpha += turn.X;
        else _alphaGoal += turn.X;
        _betaGoal = Math.Clamp(_betaGoal + turn.Y, LowestBeta(orbit.Radius, orbit.Target.Y), MaxBeta);
        _lastMouse = position;

        if ((position - _pressedAt).Length > ClickSlack) ShowPartLabel(null, default);
        Invalidate();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (Renderer != null)
        {
            Renderer.HoveredPart = null;
            Renderer.SceneryHovered = false;
            Renderer.CarHovered = false;
            Renderer.FixtureHovered = false;
        }

        Cursor = null;
        InvalidateIfDirty();
        ShowPartLabel(null, default);
    }

    private PartHit? PickAt(System.Windows.Point position) =>
        Renderer == null || ActualWidth <= 0 || ActualHeight <= 0
            ? null
            : Renderer.Pick((float)(position.X / ActualWidth), (float)(position.Y / ActualHeight));

    private void UpdateHover(System.Windows.Point position)
    {
        if (Renderer == null) return;

        // Picking tests triangles on the CPU; the pointer moves more often than it needs to be asked
        if (!_hoverPolls.Due(Environment.TickCount64)) return;

        var hit = Renderer.HasProp && PartsVisible ? PickAt(position) : null;

        // The radio on its bench: its panel is a click away
        var onRadio = hit == null && !IsOnStand && HitsRadio(position);
        Renderer.FixtureHovered = onRadio;
        if (onRadio)
        {
            Renderer.SceneryHovered = false;
            Renderer.CarHovered = false;
            Renderer.HoveredPart = null;
            Cursor = System.Windows.Input.Cursors.Hand;
            InvalidateIfDirty();
            ShowLabel(GameText.Say("garage.label.radio"), position);
            return;
        }

        // The stand beside the car, while it is not the one being worked on, and the car while the stand is: a way over
        var onStand = hit == null && !IsOnStand && HitsStand(position);
        var onCar = hit == null && IsOnStand && HitsCar(position);
        Renderer.SceneryHovered = onStand;
        Renderer.CarHovered = onCar;
        Cursor = onStand || onCar ? System.Windows.Input.Cursors.Hand : null;
        if (onStand || onCar)
        {
            Renderer.HoveredPart = null;
            InvalidateIfDirty();
            ShowLabel(onStand ? GameText.Say("garage.label.stand") : GameText.Say("garage.label.back_to_car"), position);
            return;
        }

        if (Renderer is not { HasProp: true } || !PartsVisible)
        {
            InvalidateIfDirty();
            ShowPartLabel(null, default);
            return;
        }

        Renderer.HoveredPart = hit;
        InvalidateIfDirty();
        ShowPartLabel(hit == null ? null : DefinitionOf(hit.Value), position);
    }

    private bool HitsStand(System.Windows.Point position) =>
        Renderer != null && ActualWidth > 0 && ActualHeight > 0
        && Renderer.HitsScenery((float)(position.X / ActualWidth), (float)(position.Y / ActualHeight));

    private bool HitsCar(System.Windows.Point position) =>
        Renderer != null && ActualWidth > 0 && ActualHeight > 0
        && Renderer.HitsCar((float)(position.X / ActualWidth), (float)(position.Y / ActualHeight));

    private void OnClick(System.Windows.Point position)
    {
        if (Renderer == null) return;

        var partHit = Renderer.HasProp && PartsVisible ? PickAt(position) : null;
        if (partHit == null && !IsOnStand && HitsRadio(position))
        {
            Renderer.FixtureHovered = false;
            Cursor = null;
            ShowPartLabel(null, default);
            Audio.Radio.RadioPanelViewModel.Shared.IsOpen = true;
            return;
        }

        if (partHit == null && !IsOnStand && HasStand && HitsStand(position))
        {
            Renderer.SceneryHovered = false;
            Cursor = null;
            ShowPartLabel(null, default);
            StandClicked?.Invoke();
            return;
        }

        if (partHit == null && IsOnStand && HitsCar(position))
        {
            Renderer.CarHovered = false;
            Cursor = null;
            ShowPartLabel(null, default);
            CarClicked?.Invoke();
            return;
        }

        if (Renderer is not { HasProp: true } || !PartsVisible) return;

        var hit = partHit;
        if (hit is { Layer: PartLayer.Candidate } candidate && candidate.Node < _candidateNodes.Count)
        {
            if (ReferenceEquals(_shownCandidates, Candidates)) CandidateClicked?.Invoke(_candidateNodes[candidate.Node]);
            return;
        }

        var part = hit is { Layer: PartLayer.Mounted } mounted && mounted.Node < _partNodes.Count ? _partNodes[mounted.Node].Source : null;
        PartClicked?.Invoke(part);
    }

    private PartDefinition? DefinitionOf(PartHit hit) => hit.Layer switch
    {
        PartLayer.Mounted when hit.Node < _partNodes.Count => _partNodes[hit.Node].Part,
        PartLayer.Candidate when hit.Node < _candidateNodes.Count => _candidateNodes[hit.Node].Part.Definition,
        _ => null
    };

    private void ShowPartLabel(PartDefinition? part, System.Windows.Point position) =>
        ShowLabel(part == null ? null : part.DisplayName ?? part.Name, position);

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return;

        // From the distance the camera is headed for, as far as the room lets it get there: the orbit eases after it
        // (StepCamera), quicker than it goes to a framing, so a notch is felt at once and never jolts
        var minRadius = IsOnStand ? StandMinRadius : Renderer!.HasProp ? PartsMinRadius : MinRadius;
        var from = Math.Min(_radiusGoal, RoomLimit(orbit.Alpha, orbit.Beta, orbit.Target));
        _radiusGoal = Math.Clamp(from - e.Delta * WheelPerNotch, minRadius, MaxRadius);
        _fitRadius = false;
        _zooming = true;
        Invalidate();
        e.Handled = true;
    }

    #endregion
}
