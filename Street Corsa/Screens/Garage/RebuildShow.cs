using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;

namespace StreetCorsa.Screens.Garage;

/// <summary>
/// A job of <see cref="Rebuilding"/> as the player sees it done: its steps in turn, a caption and the sound of the work
/// each, over a few seconds that grow with the job's hours (<see cref="SecondsFor"/>), the part going from what it was to
/// what the job leaves it. Only the showing: the job is done, paid for and its time spent before it starts.
/// </summary>
public sealed class RebuildShow
{
    /// <summary>The sound of the work when a step names none</summary>
    public const string BenchSound = "work.bench";

    public RebuildShow(RebuildJob job, string name, double wearBefore, double wearAfter, int minutes, DateTime startClock)
    {
        Job = job;
        Name = name;
        WearBefore = wearBefore;
        WearAfter = wearAfter;
        Minutes = Math.Max(0, minutes);
        StartClock = startClock;
        Seconds = SecondsFor(Minutes);
        Steps = job.Steps.Count > 0
            ? job.Steps.Select(s => (Rebuilding.CaptionOf(job, s), string.IsNullOrWhiteSpace(s.Sound) ? BenchSound : s.Sound!,
                string.IsNullOrWhiteSpace(s.Start) ? null : s.Start)).ToList()
            : [(name, BenchSound, (string?)null)];
    }

    public RebuildJob Job { get; }

    /// <summary>The job's name, the show's title</summary>
    public string Name { get; }

    public double WearBefore { get; }
    public double WearAfter { get; }

    /// <summary>The job's time on the game's clock</summary>
    public int Minutes { get; }

    /// <summary>The game's clock when the job started</summary>
    public DateTime StartClock { get; }

    /// <summary>How long the show runs, real seconds</summary>
    public double Seconds { get; }

    /// <summary>Each step's caption, the sound cue looping under it and the one played as it starts, in turn, each as long as the others</summary>
    public IReadOnlyList<(string Caption, string Sound, string? Start)> Steps { get; }

    /// <summary>
    /// Real seconds for a job of this many minutes: 4 for a filter, about 6 for an hour, 10 for a day's work; growing as
    /// the job's hours double, so a long job feels long without keeping the player waiting
    /// </summary>
    public static double SecondsFor(int minutes) => Math.Clamp(3 + 1.5 * Math.Log2(1 + Math.Max(0, minutes) / 15.0), 4, 10);

    /// <summary>The step on at a point of the show (0..1)</summary>
    public int StepAt(double progress) => Math.Clamp((int)(Math.Clamp(progress, 0, 1) * Steps.Count), 0, Steps.Count - 1);

    /// <summary>The part's condition at a point of the show: the work done so far, eased in and out</summary>
    public double WearAt(double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        var eased = p * p * (3 - 2 * p);
        return WearBefore + (WearAfter - WearBefore) * eased;
    }

    /// <summary>
    /// The game's clock at a point of the show: from when the job started to <paramref name="now"/>, the game's clock
    /// once the job's time is spent (a job past the end of the day ends the next morning); to the start and the job's
    /// minutes while it is not spent yet
    /// </summary>
    public DateTime ClockAt(double progress, DateTime? now = null)
    {
        var end = now is { } spent && spent > StartClock ? spent : StartClock.AddMinutes(Minutes);
        return StartClock + (end - StartClock) * Math.Clamp(progress, 0, 1);
    }
}
