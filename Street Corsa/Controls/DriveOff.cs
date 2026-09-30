using StreetCorsa.Audio;

namespace StreetCorsa.Controls;

/// <summary>A viewport with a car in it that can drive off: the garage's car, the one picked in the lot</summary>
public interface ICarDrivingOff
{
    /// <summary>The car's headlights on; false when there is no car there to drive off</summary>
    bool LightsOn();

    /// <summary>The car pulls away and keeps going; false when there is no car there</summary>
    bool PullAway();

    /// <summary>The car back where it stood (the screen stays after all)</summary>
    void StayPut();
}

/// <summary>
/// A car leaving the screen under its own power, as the player takes it between the garage and the lot: the lights
/// come on, the engine is started (unless it runs already), it idles a moment, the car pulls away, and as it rolls the
/// screen and the engine fade out together. Only for an engine that runs: a car that would not start is simply moved.
/// </summary>
public static class DriveOff
{
    private static readonly TimeSpan LightsBeforeKey = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan MostCranking = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan Idling = TimeSpan.FromSeconds(1.1);
    private static readonly TimeSpan RollingBeforeFade = TimeSpan.FromSeconds(0.45);

    /// <summary>How long the screen takes to go black over the car rolling away</summary>
    public static readonly TimeSpan Fade = TimeSpan.FromSeconds(1.0);

    /// <summary>
    /// Plays it all; true once the screen is black (the caller moves on), false when the car did not go (no car, an
    /// engine that will not start: the caller moves on without the show, or puts things back if it stays)
    /// </summary>
    /// <param name="curtain">Brings the screen down to black over the given time</param>
    public static async Task<bool> PlayAsync(ICarDrivingOff car, EngineRunner engine, Func<TimeSpan, Task> curtain)
    {
        if (engine.Spec is not { Runs: true } || !car.LightsOn()) return false;
        await Task.Delay(LightsBeforeKey);

        if (!engine.IsRunning || engine.State != EngineState.Running)
        {
            if (!engine.IsRunning) engine.Toggle();
            var giveUp = DateTime.Now + MostCranking;
            while (engine.State != EngineState.Running && DateTime.Now < giveUp) await Task.Delay(40);
            if (engine.State != EngineState.Running) return false;
        }

        await Task.Delay(Idling);
        if (!car.PullAway()) return false;
        await Task.Delay(RollingBeforeFade);

        await Task.WhenAll(curtain(Fade), QuietenAsync(engine, Fade));
        return true;
    }

    /// <summary>The engine back to full volume, for a car put back where it stood</summary>
    public static void Loud(EngineRunner engine) => engine.Place(null, 1f);

    /// <summary>The engine fades out with the picture</summary>
    private static async Task QuietenAsync(EngineRunner engine, TimeSpan over)
    {
        var started = DateTime.Now;
        while (true)
        {
            var done = (DateTime.Now - started).TotalSeconds / over.TotalSeconds;
            engine.Place(null, (float)Math.Max(0, 1 - done));
            if (done >= 1) return;
            await Task.Delay(16);
        }
    }
}
