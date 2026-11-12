using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Reflections;
using AcTools.Render.Base.Shadows;
using AcTools.Render.Kn5SpecificForwardDark.Lights;
using SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// A room's lamp that casts shadows (<see cref="Street.SceneLights.Apply"/>). Its shadow is six faces of the whole room
/// drawn again, and the renderer asks for all the lamps' at once whenever it draws the sun's (a car rocking, driving in,
/// on the lift, a part on the hoist, the clerk at his counter): told so, the lamp only notes that its shadow is stale, and
/// the renderer draws it again in its turn (<see cref="GarageRenderer"/>, <c>RedrawLampShadows</c>).
/// </summary>
internal sealed class LampLight : DarkPointLight
{
    private static long _order;

    /// <summary>Its shadow no longer shows the scene: to be drawn again</summary>
    public bool Stale { get; private set; }

    /// <summary>When it went stale, against the other lamps: the longest waiting is drawn first</summary>
    public long StaleOrder { get; private set; }

    public override void InvalidateShadows()
    {
        if (Stale) return;
        Stale = true;
        StaleOrder = Interlocked.Increment(ref _order);
    }

    /// <summary>Its shadow drawn again this frame</summary>
    public void RedrawShadows()
    {
        Stale = false;
        base.InvalidateShadows();
    }

    /// <summary>
    /// The renderer is drawing a face of its reflections (<see cref="GarageRenderer"/>'s reflection pass), whose lights are
    /// set up without shadows. On the thread that draws.
    /// </summary>
    [ThreadStatic]
    internal static bool InReflection;

    // A reflection face sets the lights up with no shadows to draw, and AcTools then clears the lamp's map, which went
    // round the stale mark and had the frame's own pass draw all six faces of every lamp again: one face a frame while the
    // reflections update (the car moving, a texture arriving). The reflections never look at the lamps' maps, so the map
    // is left as it is.
    protected override void UpdateShadowsOverride(AcTools.Render.Base.DeviceContextHolder holder, Vector3 shadowsPosition, IShadowsDraw? shadowsDraw)
    {
        if (shadowsDraw == null && InReflection) return;
        base.UpdateShadowsOverride(holder, shadowsPosition, shadowsDraw);
    }
}

/// <summary>How many lamps' shadows a frame draws again (<see cref="GarageRenderer"/>'s lamp shadows)</summary>
internal static class LampShadowBudget
{
    /// <summary>
    /// While the scene keeps moving, every lamp's shadow is drawn again once in this many frames: a quarter of them a
    /// frame, each at 15 a second at 60, in place of all of them every frame. Lamps overlap and their shadows are soft:
    /// one a few frames behind the car does not show, eight drawn in one frame did (about 50 passes over the room).
    /// </summary>
    public const int Rounds = 4;

    /// <summary>Shadows asked for again this soon after the last were drawn: the scene is moving, the lamps take turns</summary>
    public const double BusySeconds = 0.15;

    /// <summary>
    /// How many of the <paramref name="stale"/> lamps' shadows are drawn this frame out of the <paramref name="casting"/>
    /// that cast one: all of them for a scene that was still (a part put on, the lights changed), a turn's worth while it
    /// moves (<paramref name="busy"/>: shadows asked for again soon after the last, or by something on the move from its
    /// first frame, <see cref="GarageRenderer.RefreshMovingShadows"/>)
    /// </summary>
    public static int For(int casting, int stale, bool busy)
    {
        if (stale <= 0) return 0;
        if (!busy) return stale;
        return Math.Min(stale, Math.Max(1, (casting + Rounds - 1) / Rounds));
    }
}

public partial class GarageRenderer
{
    // When lamps' shadows were last drawn again (Stopwatch ticks, 0 never), and whether this frame's are still to be
    // worked out: the reflections' passes prepare the lights too, before the frame's own
    private long _lampsRedrawnAt;
    private bool _lampsDue;
    private bool _inReflection;

    protected override void DrawSceneForReflectionPrepare(ICamera camera)
    {
        _inReflection = true;
        LampLight.InReflection = true;
        try
        {
            base.DrawSceneForReflectionPrepare(camera);
        }
        finally
        {
            _inReflection = false;
            LampLight.InReflection = false;
        }
    }

    // After the sun's shadows (which mark the lamps' stale), before the lights draw theirs
    protected override void DrawPrepareEffect(Vector3 eyesPosition, Vector3 light, ShadowsDirectional? shadows, ReflectionCubemap? reflection, bool singleLight)
    {
        if (_lampsDue && !_inReflection)
        {
            _lampsDue = false;
            RedrawLampShadows();
        }

        base.DrawPrepareEffect(eyesPosition, light, shadows, reflection, singleLight);
    }

    /// <summary>
    /// The stale lamps' shadows drawn again, the longest waiting first: all at once for a scene that was still, a turn's
    /// worth (<see cref="LampShadowBudget"/>) while it moves, the rest in the frames after, drawn for that
    /// </summary>
    private void RedrawLampShadows()
    {
        // Asked for by something on the move: its turns from its first frame, not every lamp at once in that one
        var moving = _shadowsMoving;
        _shadowsMoving = false;
        var lights = Lights;
        var casting = 0;
        var stale = 0;
        foreach (var light in lights)
        {
            if (light is not LampLight lamp || !lamp.UseShadows) continue;
            casting++;
            if (lamp.Stale) stale++;
        }

        if (stale == 0) return;

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var busy = moving || _lampsRedrawnAt != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_lampsRedrawnAt, now).TotalSeconds < LampShadowBudget.BusySeconds;
        var budget = LampShadowBudget.For(casting, stale, busy);
        _lampsRedrawnAt = now;

        for (var i = 0; i < budget; i++)
        {
            LampLight? oldest = null;
            foreach (var light in lights)
            {
                if (light is LampLight { Stale: true } lamp && lamp.UseShadows && (oldest == null || lamp.StaleOrder < oldest.StaleOrder)) oldest = lamp;
            }

            oldest?.RedrawShadows();
        }

        // The viewport draws on demand: the lamps left waiting get their frames
        if (stale > budget) IsDirty = true;
    }
}
