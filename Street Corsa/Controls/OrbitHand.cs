using System.Numerics;

namespace StreetCorsa.Controls;

/// <summary>
/// The player's hand on an orbit camera: a drag turns the camera's goal by the pixels moved, and letting go while still
/// moving throws it on a little further, dying down (a fling), as the camera on a phone does. The viewport eases its
/// orbit after the goal, so the picture never jumps a mouse event at a time. Pure figures, clocked in seconds by the
/// caller, so the tests can hold it to it; a clock coarser than the mouse's reports is allowed for (the moves it cannot
/// tell apart are counted together).
/// </summary>
public sealed class OrbitHand
{
    /// <summary>How quickly a fling dies down: the share of its speed lost every second</summary>
    public const float FlingDecay = 5f;

    /// <summary>A hand held still this long before letting go throws nothing</summary>
    public const float PauseSeconds = 0.08f;

    /// <summary>The speed the hand's moves are smoothed over: a mouse reports many small moves a second, unevenly</summary>
    private const float SmoothSeconds = 0.04f;

    /// <summary>Slower than this a fling has stopped (radians a second)</summary>
    private const float Stopped = 0.02f;

    private readonly float _radiansPerPixel;
    private Vector2 _velocity;
    private Vector2 _pending;
    private double _lastMoveAt = double.NaN;
    private bool _flinging;

    /// <param name="radiansPerPixel">How far the camera turns for a pixel the pointer moves</param>
    public OrbitHand(float radiansPerPixel)
    {
        _radiansPerPixel = radiansPerPixel;
    }

    /// <summary>True while a let-go hand still carries the camera on</summary>
    public bool IsFlinging => _flinging;

    /// <summary>
    /// The pointer moved <paramref name="dx"/>, <paramref name="dy"/> pixels while held down, at <paramref name="now"/>
    /// seconds: how far the goal turns (alpha, beta), in radians
    /// </summary>
    public Vector2 Drag(double dx, double dy, double now)
    {
        _flinging = false;
        var turn = new Vector2((float)dx, (float)dy) * _radiansPerPixel;

        if (double.IsNaN(_lastMoveAt))
        {
            // The first move: how long it took is not known (the hand came down some time before)
            _lastMoveAt = now;
            return turn;
        }

        // Moves the clock cannot tell apart (a mouse reports more often than a coarse clock ticks) are counted together
        // over the first tick that tells them apart: dropped, the speed came out short by their number a tick
        _pending += turn;
        if (now > _lastMoveAt)
        {
            var dt = (float)(now - _lastMoveAt);
            var speed = _pending / dt;
            var w = 1f - MathF.Exp(-dt / SmoothSeconds);
            _velocity += (speed - _velocity) * w;
            _pending = Vector2.Zero;
            _lastMoveAt = now;
        }

        return turn;
    }

    /// <summary>The hand lets go at <paramref name="now"/>: on the move, the camera is thrown on; held still first, it stays</summary>
    public void Release(double now)
    {
        var still = double.IsNaN(_lastMoveAt) || now - _lastMoveAt > PauseSeconds;
        _flinging = !still && _velocity.Length() > Stopped;
        if (!_flinging) _velocity = Vector2.Zero;
        _pending = Vector2.Zero;
        _lastMoveAt = double.NaN;
    }

    /// <summary>
    /// A frame of the fling, <paramref name="dt"/> seconds: how far the goal turns, dying down as it goes; nothing once it
    /// has stopped
    /// </summary>
    public Vector2 Step(float dt)
    {
        if (!_flinging || dt <= 0f) return Vector2.Zero;

        // What a speed dying down at the decay covers over dt, and what is left of it after
        var left = MathF.Exp(-FlingDecay * dt);
        var turn = _velocity * ((1f - left) / FlingDecay);
        _velocity *= left;
        if (_velocity.Length() < Stopped)
        {
            _velocity = Vector2.Zero;
            _flinging = false;
        }

        return turn;
    }

    /// <summary>The hand is off and nothing is thrown: the camera is somebody else's again</summary>
    public void Stop()
    {
        _velocity = Vector2.Zero;
        _pending = Vector2.Zero;
        _flinging = false;
        _lastMoveAt = double.NaN;
    }
}
