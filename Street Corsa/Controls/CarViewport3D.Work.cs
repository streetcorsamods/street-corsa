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
/// The part a job of the parts view's "Work on it" is being done on: a loose one comes down onto the workbench and turns
/// there, a mounted one gets its grime drawn over it where it is; and the workbench itself, where the garage's props put it
/// </summary>
public partial class CarViewport3D
{
    public static readonly DependencyProperty WorkPieceProperty = DependencyProperty.Register(
        nameof(WorkPiece), typeof(WorkPiece), typeof(CarViewport3D),
        new PropertyMetadata(null, (d, e) => ((CarViewport3D)d).OnWorkPieceChanged((WorkPiece?)e.NewValue)));

    /// <summary>
    /// The part a job of the parts view's "Work on it" is being done on: a loose one comes down onto the workbench and
    /// turns there, the camera goes to a mounted one where it is; its grime is drawn over it at <see cref="WorkGrime"/>.
    /// Null when no job is on: the part goes, the camera comes back.
    /// </summary>
    public WorkPiece? WorkPiece
    {
        get => (WorkPiece?)GetValue(WorkPieceProperty);
        set => SetValue(WorkPieceProperty, value);
    }

    public static readonly DependencyProperty WorkGrimeProperty = DependencyProperty.Register(
        nameof(WorkGrime), typeof(double), typeof(CarViewport3D),
        new PropertyMetadata(0.0, (d, _) => ((CarViewport3D)d).ApplyWorkGrime()));

    /// <summary>How much grime the part being worked on still carries, 0 clean to 1</summary>
    public double WorkGrime
    {
        get => (double)GetValue(WorkGrimeProperty);
        set => SetValue(WorkGrimeProperty, value);
    }

    // Coming down onto the bench from this high, over this long, at how far in sight; turning slowly while it is there
    private const float WorkDrop = 0.45f;
    private const float WorkArriveSeconds = 0.8f;
    private const float WorkLeaveSeconds = 0.5f;
    private const float WorkTurnRate = 0.22f;

    // The camera on the bench (from in front of it and the stand's side), and on a mounted part
    private const float BenchRadius = 1.7f;
    private const float BenchAlpha = 0.9f;
    private const float BenchBeta = 0.42f;
    private const float WorkRadius = 1.5f;
    private const float WorkBeta = 0.35f;

    // The loose part on the bench: its model's box, how long it has been there, when it began to go and how far in
    // sight and above the bench it was then, and the version of the job it belongs to (a new job, or none, while one
    // is still being built, leaves the old one out)
    private System.Numerics.Vector3 _workMin;
    private System.Numerics.Vector3 _workMax;
    private float _workTime;
    private float _workLeftAt;
    private float _workLeaveOpacity;
    private float _workLeaveLift;
    private bool _workOnBench;
    private bool _workLeaving;
    private int _workVersion;

    // The camera went to the work: it comes back to the car, or the stand, when the work is done
    private bool _cameraOnWork;

    private void OnWorkPieceChanged(WorkPiece? piece)
    {
        var version = ++_workVersion;
        if (piece == null)
        {
            if (_workOnBench && !_workLeaving && Renderer != null)
            {
                // Goes from where it is and as it looks: a part still coming down fades out where it is
                _workLeaveLift = WorkLift();
                _workLeaveOpacity = Renderer.WorkOpacity;
                _workLeftAt = _workTime;
                _workLeaving = true;
                AnimateFor(TimeSpan.FromSeconds(WorkLeaveSeconds) + AnimationWindow);
            }
            else if (!_workOnBench) Renderer?.SetWork(null, null, SlimDX.Matrix.Identity, false);

            // Whether or not the parts are still on show: the view may have closed as the work ended
            if (Renderer != null && _cameraOnWork) ResetCamera();
            _cameraOnWork = false;
            return;
        }

        ApplyWorkAsync(piece, version);
    }

    private async void ApplyWorkAsync(WorkPiece piece, int version)
    {
        var renderer = Renderer;
        var catalog = PartsCatalog;
        if (renderer == null || catalog == null) return;

        try
        {
            if (piece.OnBench)
            {
                // A garage without a workbench shows the job without the part
                if (_bench is not { } bench) return;

                // Built where it lies: its box puts it on the bench's top, whatever its own origin
                var built = await Task.Run(() =>
                {
                    var placed = PartAssembler.Assemble(piece.Part, System.Numerics.Matrix4x4.Identity);
                    var clean = PartAssembler.BuildModel(catalog, "work", placed);
                    return (Clean: clean, Grime: Looks.PartGrime.Model(catalog, placed));
                });
                if (version != _workVersion || renderer != Renderer) return;

                _workMin = built.Clean.Min;
                _workMax = built.Clean.Max;
                _workOnBench = true;
                _workLeaving = false;
                _workTime = 0f;
                renderer.SetWork(built.Clean.Kn5, built.Grime.Kn5, BenchPlacement(), false);
                renderer.WorkOpacity = 0f;
                ApplyWorkGrime();

                renderer.AutoAdjustTarget = false;
                _autoAdjustWhenSettled = false;
                _cameraOnWork = true;
                var top = WorkbenchLayout.TopOf(bench.Placement);
                _targetGoal = new SlimDX.Vector3(top.X, top.Y + Math.Min(0.25f, (_workMax.Y - _workMin.Y) / 2f), top.Z);
                _radiusGoal = BenchRadius;
                _alphaGoal = BenchAlpha - bench.Heading * MathF.PI / 180f;
                _betaGoal = BenchBeta;
                AnimateFor(TimeSpan.FromSeconds(WorkArriveSeconds) + AnimationWindow);
                return;
            }

            // A mounted part: once the parts are built again after the job (queued to the dispatcher by the new engine
            // and gear, then building), its place among them, and its grime there. Not for ever: a build that never
            // ends is not waited on past its patience.
            await AwaitLatestAsync(() => _partsApplied, () => version == _workVersion, TimeSpan.FromMilliseconds(PartsPatienceMs));
            if (version != _workVersion || renderer != Renderer) return;

            var mounted = _partWorlds.FirstOrDefault(p => piece.Part.InstanceId != Guid.Empty && p.Key.InstanceId == piece.Part.InstanceId);
            if (mounted.Key == null) return;

            var grimeModel = await Task.Run(() => Looks.PartGrime.Model(catalog, [new PlacedPart(mounted.Key.Definition, mounted.Value)]));
            if (version != _workVersion || renderer != Renderer) return;

            _workOnBench = false;
            renderer.SetWork(null, grimeModel.Kn5, SlimDX.Matrix.Identity, true);
            ApplyWorkGrime();

            if (renderer.WorkCentre is { } centre)
            {
                renderer.AutoAdjustTarget = false;
                _autoAdjustWhenSettled = false;
                _cameraOnWork = true;
                _targetGoal = centre;
                _radiusGoal = WorkRadius;
                _betaGoal = WorkBeta;
            }

            AnimateFor(AnimationWindow);
        }
        catch (Exception ex)
        {
            // Only the showing: the job is done whatever the view makes of it
            Logger.Warning(ex, "Could not show the part being worked on");
        }
    }

    private void ApplyWorkGrime()
    {
        if (Renderer is not { } renderer) return;

        renderer.WorkGrime = (float)Math.Clamp(WorkGrime, 0, 1);
        Invalidate();
    }

    /// <summary>How high above the bench the loose part is now: coming down, or where it was when it began to go</summary>
    private float WorkLift() =>
        _workLeaving ? _workLeaveLift : WorkDrop * (1f - EaseWork(Math.Clamp(_workTime / WorkArriveSeconds, 0f, 1f)));

    /// <summary>The loose part on the bench as it is now: coming down, turning</summary>
    private SlimDX.Matrix BenchPlacement() =>
        _bench is { } bench
            ? ToSlim(WorkbenchLayout.PlacementOf(bench.Placement, _workMin, _workMax, _workTime * WorkTurnRate, WorkLift()))
            : SlimDX.Matrix.Identity;

    private static float EaseWork(float t) => t * t * (3f - 2f * t);

    /// <summary>The loose part on the bench, a frame on; true while it moves</summary>
    private bool StepWork(GarageRenderer renderer, float dt)
    {
        if (!_workOnBench) return false;

        _workTime += dt;
        if (_workLeaving)
        {
            var gone = Math.Clamp((_workTime - _workLeftAt) / WorkLeaveSeconds, 0f, 1f);
            renderer.WorkOpacity = _workLeaveOpacity * (1f - gone);
            renderer.MoveWork(BenchPlacement());
            if (gone < 1f) return true;

            renderer.SetWork(null, null, SlimDX.Matrix.Identity, false);
            renderer.WorkOpacity = 1f;
            _workOnBench = false;
            _workLeaving = false;
            return false;
        }

        renderer.WorkOpacity = EaseWork(Math.Clamp(_workTime / (WorkArriveSeconds * 0.6f), 0f, 1f));
        renderer.MoveWork(BenchPlacement());
        return true;
    }

    // The renderer the workbench was put into, and where it stands in the garage; null in a garage without one
    private GarageRenderer? _benchRenderer;
    private Services.Garage.PlacedProp? _bench;

    /// <summary>
    /// The workbench, where the garage's props put it (<see cref="WorkbenchLayout.PropId"/>): the game's own model drawn
    /// from boxes unless a mod names one, set into the renderer once. A garage with no place for it has none, and a
    /// loose part's job is shown there without the part.
    /// </summary>
    private async void ApplyBench()
    {
        var renderer = Renderer;
        if (renderer == null || ReferenceEquals(renderer, _benchRenderer)) return;
        _benchRenderer = renderer;
        _bench = null;

        await LoadPropAsync(renderer, WorkbenchLayout.PropId, builtIn: true, WorkbenchLayout.CreateModel, (kn5, bench) =>
        {
            renderer.SetBench(kn5, ToSlim(bench.Placement));
            _bench = bench;
        });
    }
}
