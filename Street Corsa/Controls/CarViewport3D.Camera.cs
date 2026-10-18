using System.IO;
using System.Windows;
using System.Windows.Input;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Logging;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.Text;
using static StreetCorsa.Helpers.Matrices;

namespace StreetCorsa.Controls;

/// <summary>
/// The camera: its framing of the car, the stand or a part, eased there frame by frame; and the pointer, dragging it
/// round, picking parts and the props under it
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
        _autoAdjustWhenSettled = false;
        _followCarry = false;
        _cameraOnPart = false;
        _cameraWaitsOnStand = false;

        if (IsOnStand)
        {
            // Close in on the engine on the stand, a little above the crankshaft
            Renderer!.AutoAdjustTarget = false;
            var spot = EngineStandLayout.Spot;
            _targetGoal = new SlimDX.Vector3(spot.X, EngineStandLayout.Spindle.Y - 0.05f, spot.Z - 0.1f);
            _radiusGoal = StandRadius;
            _alphaGoal = StandAlpha;
            _betaGoal = StandBeta;
        }
        else if (Renderer!.CarNode == null)
        {
            // Empty garage: nothing to frame, so look across the room at eye level instead of at the floor
            Renderer.AutoAdjustTarget = false;
            _targetGoal = new SlimDX.Vector3(0f, EmptyGarageEyeHeight, 0f);
            _radiusGoal = MaxRadius;
            _betaGoal = MinBeta;
        }
        else if (!immediate && !Renderer.AutoAdjustTarget && Renderer.MainSlot.GetCarBoundingBox() is { } box)
        {
            // Coming back from the stand, or from an empty garage
            _targetGoal = Renderer.FramedTarget(_alphaGoal, _betaGoal, _radiusGoal, (box.Minimum + box.Maximum) / 2f);
            _autoAdjustWhenSettled = true;
        }
        else
        {
            Renderer.AutoAdjustTarget = true;
        }

        if (immediate)
        {
            orbit.Radius = _radiusGoal;
            orbit.Alpha = _alphaGoal;
            orbit.Beta = _betaGoal;
            if (_targetGoal is { } target) orbit.Target = target;
        }

        AnimateFor(AnimationWindow);
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

    /// <summary>
    /// Eases the camera toward its framing. Returns true while it is still moving, so the render loop knows
    /// to keep drawing. A share of what is left every second, so it comes out the same at any frame rate.
    /// </summary>
    private bool StepCamera(float dt)
    {
        var orbit = Renderer?.CameraOrbit;
        if (orbit == null) return false;

        var k = 1f - (float)Math.Exp(-CameraSettleRate * dt);
        var moving = false;

        // Never through a wall, not even for a frame: how far back the camera can stand depends on which way it looks from
        var limit = RoomLimit(orbit.Alpha, orbit.Beta, orbit.Target);
        var radiusGoal = Math.Min(_radiusGoal, limit);
        if (Math.Abs(radiusGoal - orbit.Radius) > CameraSettled)
        {
            orbit.Radius += (radiusGoal - orbit.Radius) * k;
            moving = true;
        }

        orbit.Radius = Math.Min(orbit.Radius, limit);

        if (Math.Abs(_betaGoal - orbit.Beta) > CameraSettled)
        {
            orbit.Beta += (_betaGoal - orbit.Beta) * k;
            moving = true;
        }

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
                orbit.Alpha += delta * k;
                moving = true;
            }
        }

        if (_targetGoal is { } target)
        {
            var toTarget = target - orbit.Target;
            if (toTarget.LengthSquared() > CameraSettled * CameraSettled)
            {
                orbit.Target += toTarget * k;
                moving = true;
            }
            else if (_autoAdjustWhenSettled && !moving)
            {
                _autoAdjustWhenSettled = false;
                _targetGoal = null;
                Renderer.AutoAdjustTarget = true;
            }
        }

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
                if (!_cameraOnPart) _radiusBeforeFocus = _radiusGoal;
                _cameraOnPart = true;

                renderer.AutoAdjustTarget = false;
                _autoAdjustWhenSettled = false;
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

            // Back round the car from where the camera looks now, as near as it was
            _alphaGoal = orbit.Alpha;
            _betaGoal = orbit.Beta;
            _radiusGoal = Math.Clamp(_radiusBeforeFocus, renderer.HasProp ? PartsMinRadius : MinRadius, MaxRadius);
            if (renderer.MainSlot.GetCarBoundingBox() is { } car)
            {
                _targetGoal = renderer.FramedTarget(_alphaGoal, _betaGoal, _radiusGoal, (car.Minimum + car.Maximum) / 2f);
                _autoAdjustWhenSettled = true;
            }
            else
            {
                _targetGoal = null;
                renderer.AutoAdjustTarget = true;
            }

            AnimateFor(AnimationWindow);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not bring the camera to the selected part");
        }
    }

    #region Camera input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Renderer == null) return;

        _isDragging = true;
        _lastMouse = _pressedAt = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void ForgetPress() => _isDragging = false;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var wasDragging = _isDragging;
        _isDragging = false;
        ReleaseMouseCapture();

        var position = e.GetPosition(this);
        if (wasDragging && (position - _pressedAt).Length <= ClickSlack) OnClick(position);
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

        orbit.Alpha += (float)(position.X - _lastMouse.X) * 0.01f;
        orbit.Beta = Math.Clamp(orbit.Beta + (float)(position.Y - _lastMouse.Y) * 0.01f, LowestBeta(orbit.Radius, orbit.Target.Y), MaxBeta);
        _alphaGoal = orbit.Alpha;
        _betaGoal = orbit.Beta;
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
        var now = Environment.TickCount64;
        if (now - _lastHoverPick < HoverIntervalMs) return;
        _lastHoverPick = now;

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

        var minRadius = IsOnStand ? StandMinRadius : Renderer!.HasProp ? PartsMinRadius : MinRadius;
        orbit.Radius = Math.Clamp(orbit.Radius - e.Delta * 0.002f, minRadius, MaxRadius);
        _radiusGoal = orbit.Radius;

        // Looking up from under the lifted car, the eye goes no lower than the floor further out either
        if (orbit.Beta < LowestBeta(orbit.Radius, orbit.Target.Y)) _betaGoal = orbit.Beta = LowestBeta(orbit.Radius, orbit.Target.Y);
        Invalidate();
        e.Handled = true;
    }

    #endregion
}
