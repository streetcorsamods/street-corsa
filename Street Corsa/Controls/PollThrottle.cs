namespace StreetCorsa.Controls;

/// <summary>
/// Lets something through this often at most, by a clock in milliseconds (Environment.TickCount64, which a clock change
/// never moves): the pointer is asked what it is on far less often than it moves, since picking tests triangles on the
/// CPU. The first ask always goes through.
/// </summary>
public sealed class PollThrottle
{
    private readonly long _intervalMs;
    private long? _lastAt;

    public PollThrottle(long intervalMs)
    {
        _intervalMs = intervalMs;
    }

    /// <summary>True when the interval has passed since the last one let through (always the first time), counting this one</summary>
    public bool Due(long now)
    {
        // Never now minus a seed: a seed of long.MinValue overflows the difference negative, and nothing is ever due
        if (_lastAt is { } last && now < last + _intervalMs) return false;
        _lastAt = now;
        return true;
    }
}
