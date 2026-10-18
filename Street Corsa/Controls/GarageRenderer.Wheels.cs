using AcTools.Render.Base;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Objects;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Controls;

/// <summary>
/// The car's own wheels in the parts view: left out of the ghost while the parts are on show (which draw the wheels as
/// parts of their own), and standing in for the part wheels of the factory's at their corners
/// </summary>
public partial class GarageRenderer
{
    // The car's own wheels (its model's WHEEL_xx nodes, the part wheels shown on them in the scenes among what they hold),
    // found once a car (EnsureWheels), in a list and by their node names; and whether each was drawn before a draw without
    // them, kept from frame to frame so a ghost draw makes no garbage
    private Kn5RenderableCar? _wheelsOf;
    private List<RenderableList> _carWheels = new();
    private Dictionary<string, RenderableList> _carWheelsByName = new(StringComparer.OrdinalIgnoreCase);
    private bool[] _carWheelsWere = [];

    // The car's own wheels standing in for the part wheels of the factory's at their corners (SetStandIns): the part's
    // node, kept to be picked but not drawn, and the car's wheel drawn solid in its place, with what to let through when
    // the car is drawn for that wheel alone (the wheel, what is in it and the nodes it hangs from). The wheels drawn so
    // right now (those none of whose parts the player has hidden), worked out again only once the stand-ins or the
    // hiding change, not on every pass.
    private readonly Dictionary<PartNode, RenderableList> _standIns = new();
    private readonly Dictionary<RenderableList, HashSet<IRenderableObject>> _standInPaths = new();
    private readonly List<RenderableList> _shownStandIns = new();
    private bool _standInsChanged;

    /// <summary>
    /// The car's own wheels stand in for the mounted parts' nodes at their corners (<paramref name="nodes"/>: a factory rim
    /// and its tyre, corner 0 front left to 3 right rear): the car's wheel is drawn solid where the part's is, and the
    /// part's node is still there to be picked, to glow (as the car's wheel) and to be taken off. Given once for each model
    /// put in, right after it. Must be called on the thread that draws.
    /// </summary>
    public void SetStandIns(IEnumerable<(int Node, int Corner)> nodes)
    {
        if (CarNode is not { } car || car.RootObject is not { } root) return;
        EnsureWheels(car);
        foreach (var (index, corner) in nodes)
        {
            if (index < 0 || index >= _propNodes.Count || corner < 0 || corner >= WheelFit.WheelNodes.Length) continue;
            if (!_carWheelsByName.TryGetValue(WheelFit.WheelNodes[corner], out var wheel)) continue;

            var node = _propNodes[index];
            foreach (var mesh in node.List.GetAllChildren().OfType<IRenderableObject>().Where(o => o is not RenderableList)) mesh.IsEnabled = false;
            node.IsStandIn = true;
            _standIns[node] = wheel;
            if (!_standInPaths.ContainsKey(wheel) && PathTo(root, wheel) is { } path)
                _standInPaths[wheel] = path.Concat(wheel.GetAllChildren()).Append(wheel).ToHashSet();
        }

        _standInsChanged = true;
        IsDirty = true;
    }

    /// <summary>The part's own model back in sight, and the car's wheel no longer standing in for it, nor for the rest of its wheel</summary>
    private void EndStandIn(PartNode node)
    {
        if (!_standIns.TryGetValue(node, out var wheel)) return;
        foreach (var (other, _) in _standIns.Where(s => ReferenceEquals(s.Value, wheel)).ToList())
        {
            foreach (var mesh in other.List.GetAllChildren().OfType<IRenderableObject>().Where(o => o is not RenderableList)) mesh.IsEnabled = true;
            other.IsStandIn = false;
            _standIns.Remove(other);
        }

        _standInsChanged = true;
    }

    /// <summary>The nodes from <paramref name="from"/> down to <paramref name="target"/>, both in; null when it is not under it</summary>
    private static List<IRenderableObject>? PathTo(RenderableList from, RenderableList target)
    {
        if (ReferenceEquals(from, target)) return [from];
        foreach (var child in from.OfType<RenderableList>())
        {
            if (PathTo(child, target) is { } path)
            {
                path.Insert(0, from);
                return path;
            }
        }

        return null;
    }

    /// <summary>The car's own wheels standing in for parts, but those whose parts are put out of sight (the wheels' show)</summary>
    private void DrawStandIns(DeviceContextHolder holder, ICamera camera, SpecialRenderMode mode)
    {
        if (_standIns.Count == 0 || CarNode is not { } car) return;

        if (_standInsChanged)
        {
            _standInsChanged = false;
            _shownStandIns.Clear();
            _shownStandIns.AddRange(_standIns.GroupBy(s => s.Value).Where(g => g.All(s => !s.Key.IsHidden)).Select(g => g.Key));
        }

        foreach (var wheel in _shownStandIns) DrawStandIn(holder, camera, mode, car, wheel);
    }

    private void DrawStandIn(DeviceContextHolder holder, ICamera camera, SpecialRenderMode mode, Kn5RenderableCar car, RenderableList wheel)
    {
        if (_standInPaths.TryGetValue(wheel, out var through)) car.Draw(holder, camera, mode, through.Contains);
    }

    /// <summary>
    /// The car's own wheels found, once a car: one node a corner (<see cref="ShownPartsOnCar.WheelNodeOf"/>; a model with
    /// two nodes of a wheel's name keeps the first, where a dictionary of them all would throw). The paths through the car
    /// before it go with it: they held its wheel nodes, and everything under them, for as long as the renderer lived.
    /// </summary>
    private void EnsureWheels(Kn5RenderableCar car)
    {
        if (ReferenceEquals(_wheelsOf, car)) return;

        _wheelsOf = car;
        _carWheels = new List<RenderableList>();
        _carWheelsByName = new Dictionary<string, RenderableList>(StringComparer.OrdinalIgnoreCase);
        _standInPaths.Clear();
        for (var corner = 0; corner < WheelFit.WheelNodes.Length; corner++)
        {
            if (ShownPartsOnCar.WheelNodeOf(car, corner) is not { } wheel) continue;
            _carWheels.Add(wheel);
            _carWheelsByName[WheelFit.WheelNodes[corner]] = wheel;
        }

        _carWheelsWere = new bool[_carWheels.Count];
    }

    /// <summary>
    /// Draws the car without its own wheels while the car's parts are on show (the wheels are among them), with them
    /// otherwise: the wheel nodes are switched off for the draw and back as they were after it
    /// </summary>
    private void DrawCarWithoutWheels(Action draw)
    {
        if (_prop == null || _standActive || CarNode is not { } car)
        {
            draw();
            return;
        }

        EnsureWheels(car);
        var was = _carWheelsWere;
        for (var i = 0; i < _carWheels.Count; i++)
        {
            was[i] = _carWheels[i].IsEnabled;
            _carWheels[i].IsEnabled = false;
        }

        try
        {
            draw();
        }
        finally
        {
            for (var i = 0; i < _carWheels.Count; i++) _carWheels[i].IsEnabled = was[i];
        }
    }
}
