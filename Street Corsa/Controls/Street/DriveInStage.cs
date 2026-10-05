using StreetCorsa.Audio;

namespace StreetCorsa.Controls.Street;

/// <summary>Where the player's car is at a place it drives into (the gas station, the diner)</summary>
public enum DriveInPhase
{
    /// <summary>Not there yet: the screen waits for the place to be up</summary>
    Waiting,

    /// <summary>Driving in off the road</summary>
    Arriving,

    /// <summary>Stopped where it stands there (at the pump, in its bay), engine idling or off</summary>
    Standing,

    /// <summary>Pulling out onto the road</summary>
    Leaving,

    /// <summary>Gone down the road</summary>
    Gone
}

/// <summary>
/// What goes on at a place the player drives into (the gas station, the diner), between the screen, which decides, and
/// the place's viewport, which shows it: the screen sends the car in and out again; the viewport drives it and says when
/// it has stopped and when it has gone. A car towed in, or back from a race, stands there from the start. With no
/// viewport up (no 3D, or not up yet) the car is simply there at once, and gone at once. Lives on the UI thread.
/// </summary>
public sealed class DriveInStage
{
    public DriveInPhase Phase { get; private set; }

    /// <summary>The car came in on the tow truck: it stands there, not driven there</summary>
    public bool Towed { get; private set; }

    /// <summary>The game's clock, for the scripts that move the car (the screen keeps it)</summary>
    public DateTime? Time { get; set; }

    /// <summary>The player's engine, which the viewport drives while the car moves</summary>
    public EngineRunner? Engine { get; private set; }

    /// <summary>The sound cue of a drive in nobody sees, as the car stops (the station's driveway bell); none when null</summary>
    public string? UnseenArrivalCue { get; init; }

    private float _fuelLitres;

    /// <summary>What is in the tank, litres, for the fuel gauge: the screen keeps it (as the pump runs)</summary>
    public float FuelLitres
    {
        get => _fuelLitres;
        set
        {
            if (_fuelLitres == value) return;
            _fuelLitres = value;
            FuelChanged?.Invoke();
        }
    }

    /// <summary>A viewport has the place up and drives the car</summary>
    public bool IsShown { get; internal set; }

    /// <summary>The viewport has given up (no 3D here, or the car would not load): nobody will show the place</summary>
    public bool CannotShow { get; internal set; }

    /// <summary>
    /// Set by the viewport while it shows the place: plays a script of the flow's (screen.&lt;id&gt;.enter or .leave)
    /// with the player's car; done once it lets the screen go on, null when none plays
    /// </summary>
    public Func<string, Services.Scripting.ScriptFacts, Task?>? PlayTrigger { get; internal set; }

    /// <summary>Something for the viewport to act on</summary>
    public event Action? Changed;

    /// <summary>The tank's litres changed: the gauge's needle moves</summary>
    public event Action? FuelChanged;

    /// <summary>The car has stopped where it stands</summary>
    public event Action? Arrived;

    /// <summary>The car has driven out of sight</summary>
    public event Action? Gone;

    /// <summary>The car drives in, or stands there already when the tow truck brought it</summary>
    public void Arrive(EngineRunner engine, bool towed)
    {
        Engine = engine;
        Towed = towed;
        Phase = towed ? DriveInPhase.Standing : DriveInPhase.Arriving;
        Changed?.Invoke();

        // A drive nobody shows is over at once, its sound heard all the same; a tow stands there from the start
        if (towed) Arrived?.Invoke();
        else if (!IsShown) DrivenInUnseen();
    }

    /// <summary>The car stands there already, driven in earlier (back from a race that started there)</summary>
    public void StandThere(EngineRunner engine)
    {
        Engine = engine;
        Towed = false;
        Phase = DriveInPhase.Standing;
        Changed?.Invoke();
        Arrived?.Invoke();
    }

    public void Leave()
    {
        if (Phase != DriveInPhase.Standing) return;

        Phase = DriveInPhase.Leaving;
        Changed?.Invoke();
        if (!IsShown) ReportGone();
    }

    /// <summary>A car that left but could not get on (the garage would not open) stands there again</summary>
    public void BackToStand()
    {
        if (Phase is not (DriveInPhase.Leaving or DriveInPhase.Gone)) return;

        Phase = DriveInPhase.Standing;
        Changed?.Invoke();
    }

    /// <summary>The screen is going: nobody is there any more</summary>
    public void Clear()
    {
        Phase = DriveInPhase.Waiting;
        Engine = null;
        Towed = false;
        Changed?.Invoke();
    }

    /// <summary>The car drove in with nothing to show it (no 3D, no arrive script): its sound, and it stands there</summary>
    internal void DrivenInUnseen()
    {
        if (Phase != DriveInPhase.Arriving) return;
        if (UnseenArrivalCue != null) Sfx.Play(UnseenArrivalCue);
        ReportArrived();
    }

    /// <summary>The viewport's word: the car has stopped where it stands</summary>
    internal void ReportArrived()
    {
        if (Phase != DriveInPhase.Arriving) return;
        Phase = DriveInPhase.Standing;
        Arrived?.Invoke();
    }

    /// <summary>The viewport's word: the car has gone</summary>
    internal void ReportGone()
    {
        if (Phase != DriveInPhase.Leaving) return;
        Phase = DriveInPhase.Gone;
        Gone?.Invoke();
    }
}
