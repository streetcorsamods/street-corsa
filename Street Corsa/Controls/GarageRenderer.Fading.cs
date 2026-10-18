using AcTools.Kn5File;
using AcTools.Render.Base;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Objects;
using AcTools.Render.Base.PostEffects;
using AcTools.Render.Base.TargetTextures;
using AcTools.Render.Kn5Specific.Objects;
using SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// The parts drawn see-through: on their way off the car or onto it, fading as they travel, and the kinds the player
/// has put out of sight, fading out and back in. Worked out once a frame (<see cref="UpdateFading"/>) and drawn over
/// the scene after the rest (<see cref="DrawFading"/>).
/// </summary>
public partial class GarageRenderer
{
    // Opacities a fade is drawn at: fine enough not to be seen stepping
    private const float FadeSteps = 32f;

    // Mounted parts the player has put out of sight by their kind (the parts view's show buttons): not drawn, not picked.
    // Kept apart from the nodes' own on/off, which the motions own: a part hidden on its way somewhere goes on its way,
    // out of sight. Those fading out or back in, at how far they are in sight (0 gone, 1 in sight).
    private HashSet<PartNode> _hiddenNodes = new();
    private readonly Dictionary<PartNode, float> _hideFades = new();

    // The part nodes drawn see-through over the scene right now, at their step of the fade (0: not drawn at all, a
    // hidden part), and the filter that leaves them out of every other pass
    private Dictionary<RenderableList, float>? _fading;
    private Func<IRenderableObject, bool>? _settled;

    // UpdateFading's working, kept from frame to frame so a fade makes no garbage: the nodes fading as worked out now,
    // the nodes looked at already, and how far each moving part is in sight on its way (the first motion of a node's)
    private Dictionary<RenderableList, float> _fadingNow = new();
    private readonly HashSet<PartNode> _fadeLooked = new();
    private readonly Dictionary<PartNode, float> _travel = new();

    /// <summary>
    /// Works out which nodes are drawn see-through now, once a frame rather than for every pass. A part that starts to
    /// fade drops its shadow then, not when it is gone.
    /// </summary>
    private void UpdateFading()
    {
        _travel.Clear();
        foreach (var motion in _motions)
        {
            if (motion is PartMotion part) _travel.TryAdd(part.Node, part.Opacity);
        }

        var fading = _fadingNow;
        fading.Clear();
        _fadeLooked.Clear();
        foreach (var motion in _motions) LookAt(motion.Node);
        foreach (var node in _hiddenNodes) LookAt(node);
        foreach (var node in _hideFades.Keys) LookAt(node);

        if (fading.Count == 0)
        {
            if (_fading == null) return;
            _fading = null;
            _settled = null;
            SetShadowsDirty();
            return;
        }

        // The same nodes fading: only how far each is along changes, and the filter on every other pass stays
        if (_fading != null && _fading.Count == fading.Count && SameKeys(fading, _fading))
        {
            foreach (var (list, step) in fading) _fading[list] = step;
            return;
        }

        // The worked-out set becomes the one drawn; the one drawn before is the working for the next frame
        _fadingNow = _fading ?? new();
        _fading = fading;
        _settled = LeavingOut(fading);
        SetShadowsDirty();

        void LookAt(PartNode node)
        {
            if (!_fadeLooked.Add(node) || !node.List.IsEnabled) return;
            var step = (float)Math.Round(OpacityOf(node) * FadeSteps);
            if (step < FadeSteps) fading[node.List] = step;
        }

        static bool SameKeys(Dictionary<RenderableList, float> a, Dictionary<RenderableList, float> b)
        {
            foreach (var key in a.Keys)
            {
                if (!b.ContainsKey(key)) return false;
            }
            return true;
        }
    }

    /// <summary>The filter that leaves the <paramref name="fading"/> nodes out of every other pass</summary>
    private static Func<IRenderableObject, bool> LeavingOut(Dictionary<RenderableList, float> fading) =>
        o => o is not RenderableList list || !fading.ContainsKey(list);

    /// <summary>
    /// How much of a part node is in sight, drawn see-through over the scene below 1 (<see cref="DrawFading"/>): the way
    /// it is going (a part fades as it travels, as UpdateFading has found its motion) and whether the player hid it
    /// </summary>
    private float OpacityOf(PartNode node)
    {
        var travel = _travel.TryGetValue(node, out var opacity) ? opacity : 1f;
        var shown = _hideFades.TryGetValue(node, out var fade) ? fade : _hiddenNodes.Contains(node) ? 0f : 1f;
        return travel * shown;
    }

    /// <summary>The parts going out of sight or coming back, a step further; each is done with once it gets there</summary>
    private void TickHideFades(float step)
    {
        if (_hideFades.Count == 0) return;

        foreach (var node in _hideFades.Keys.ToList())
        {
            var to = _hiddenNodes.Contains(node) ? 0f : 1f;
            var at = _hideFades[node];
            at = to > at ? Math.Min(to, at + step / PartMotionSeconds) : Math.Max(to, at - step / PartMotionSeconds);
            if (at == to) _hideFades.Remove(node);
            else _hideFades[node] = at;
        }

        if (_motions.Count == 0)
        {
            UpdateFading();
            IsDirty = true;
        }
    }

    /// <summary>
    /// The mounted parts out of sight, given by node: those newly hidden fade out wherever they are, those shown again
    /// fade back in, at once without <paramref name="fade"/>. A part on its way somewhere goes on its way, in sight or
    /// not. No sound: nothing comes off, they are only not looked at. A new prop shows everything until it is told again.
    /// </summary>
    public void SetHiddenParts(IReadOnlyCollection<int> nodes, bool fade)
    {
        var hidden = nodes.Where(n => n >= 0 && n < _propNodes.Count).Select(n => _propNodes[n]).ToHashSet();
        var going = hidden.Where(n => !_hiddenNodes.Contains(n)).ToList();
        var coming = _hiddenNodes.Where(n => !hidden.Contains(n) && _propNodes.Contains(n)).ToList();
        _hiddenNodes = hidden;
        if (going.Count == 0 && coming.Count == 0) return;

        foreach (var node in going.Concat(coming))
        {
            node.IsHidden = hidden.Contains(node);
            if (!fade) _hideFades.Remove(node);
            else if (!_hideFades.ContainsKey(node)) _hideFades[node] = node.IsHidden ? 1f : 0f;
        }

        // A wheel the car's own stands in for is drawn only while none of its parts is hidden (DrawStandIns)
        _standInsChanged = true;

        if (_selected is { } selected && selected < _propNodes.Count && hidden.Contains(_propNodes[selected])) _selected = null;
        _hovered = null;
        SetShadowsDirty();
        UpdateFading();
        IsDirty = true;
    }

    /// <summary>
    /// Parts on their way, at how far gone they are. As with the ghost car, materials force their own blend state:
    /// the parts go over a copy of the scene, blended back at their opacity. Parts that move together fade together,
    /// so there is a pass for each step of the fade there is: one mostly, two or three while an engine is stripped for
    /// the stand, one part starting before the one ahead of it is gone.
    /// </summary>
    private void DrawFading(DeviceContextHolder holder, ICamera camera)
    {
        if (_prop == null || _fading == null) return;

        var prop = _prop;
        var siblings = _propNodes.Count > 0 ? _propNodes[0].Siblings : new HashSet<RenderableList>();
        foreach (var group in _fading.GroupBy(f => f.Value))
        {
            var opacity = group.Key / FadeSteps;
            if (opacity <= 0f) continue;

            var lists = group.Select(f => f.Key).ToHashSet();
            bool Only(IRenderableObject o) => o is not RenderableList list || !siblings.Contains(list) || lists.Contains(list);

            DrawSeeThrough(holder, opacity, mode => prop.Draw(holder, camera, mode, Only));
        }
    }
}
