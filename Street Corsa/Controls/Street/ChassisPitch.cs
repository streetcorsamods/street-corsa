namespace StreetCorsa.Controls.Street;

/// <summary>
/// The body pitching on its springs: squatting as the car pulls away, diving on the brakes, and rocking back when
/// it stops. A spring and a damper, like the engine's rock.
/// </summary>
public sealed class ChassisPitch
{
    // Radians per m/s² of acceleration: a hard stop dips the nose about two and a half degrees
    private const double PitchPerAccel = 0.6 * Math.PI / 180;
    private const double Frequency = 1.3;
    private const double Damping = 0.35;

    private double _pitch;
    private double _speed;

    /// <summary>Nose up positive</summary>
    public float Pitch => (float)_pitch;

    public bool IsSettled => Math.Abs(_pitch) < 1e-5 && Math.Abs(_speed) < 1e-4;

    public void Tick(double dt, double acceleration)
    {
        var w = 2 * Math.PI * Frequency;
        var target = PitchPerAccel * Math.Clamp(acceleration, -9, 9);
        const int steps = 4;
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var a = w * w * (target - _pitch) - 2 * Damping * w * _speed;
            _speed += a * h;
            _pitch += _speed * h;
        }
    }

    public void Reset()
    {
        _pitch = 0;
        _speed = 0;
    }
}
