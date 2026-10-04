using StreetCorsa.Audio;

namespace StreetCorsa.Controls.Street;

/// <summary>Where the player's car is at the gas station</summary>
public enum StationPhase
{
    /// <summary>Not there yet: the screen waits for the station to be up</summary>
    Waiting,

    /// <summary>Driving in off the road</summary>
    Arriving,

    /// <summary>Stopped at the pump, engine idling</summary>
    AtPump,

    /// <summary>Pulling out onto the road</summary>
    Leaving,

    /// <summary>Gone down the road</summary>
    Gone
}

/// <summary>
/// What goes on at the gas station, between the screen, which decides, and the station's viewport, which shows it: the
/// screen sends the car in and out again; the viewport drives it and says when it has stopped at the pump and when it
/// has gone. A car towed in is at the pump from the start. With no viewport up (no 3D, or not up yet) the car is simply
/// there at once, and gone at once. Lives on the UI thread.
/// </summary>
public sealed class StationStage
{
    public StationPhase Phase { get; private set; }

    /// <summary>The car came in on the tow truck: it is at the pump, not driven there</summary>
    public bool Towed { get; private set; }

    /// <summary>The game's clock, for the scripts that move the car (the screen keeps it)</summary>
    public DateTime? Time { get; set; }

    /// <summary>The player's engine, which the viewport drives while the car moves</summary>
    public EngineRunner? Engine { get; private set; }

    private float _fuelLitres;

    /// <summary>What is in the tank, litres, for the fuel gauge: the screen keeps it as the pump runs</summary>
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

    /// <summary>A viewport has the station up and drives the car</summary>
    public bool IsShown { get; internal set; }

    /// <summary>The viewport has given up (no 3D here, or the car would not load): nobody will show the station</summary>
    public bool CannotShow { get; internal set; }

    /// <summary>
    /// Set by the viewport while it shows the station: plays a script of the flow's (screen.&lt;id&gt;.enter or .leave)
    /// with the player's car; done once it lets the screen go on, null when none plays
    /// </summary>
    public Func<string, Services.Scripting.ScriptFacts, Task?>? PlayTrigger { get; internal set; }

    /// <summary>Something for the viewport to act on</summary>
    public event Action? Changed;

    /// <summary>The tank's litres changed: the gauge's needle moves</summary>
    public event Action? FuelChanged;

    /// <summary>The car has stopped at the pump</summary>
    public event Action? AtPump;

    /// <summary>The car has driven out of sight</summary>
    public event Action? Gone;

    /// <summary>The car drives in, or is at the pump already when the tow truck brought it</summary>
    public void Arrive(EngineRunner engine, bool towed)
    {
        Engine = engine;
        Towed = towed;
        Phase = towed ? StationPhase.AtPump : StationPhase.Arriving;
        Changed?.Invoke();

        // A drive nobody shows is over at once, the driveway bell heard all the same; a tow is at the pump from the start
        if (towed) AtPump?.Invoke();
        else if (!IsShown) DrivenInUnseen();
    }

    public void Leave()
    {
        if (Phase != StationPhase.AtPump) return;

        Phase = StationPhase.Leaving;
        Changed?.Invoke();
        if (!IsShown) ReportGone();
    }

    /// <summary>A car that left but could not get home (the garage would not open) is at the pump again</summary>
    public void BackToPump()
    {
        if (Phase is not (StationPhase.Leaving or StationPhase.Gone)) return;

        Phase = StationPhase.AtPump;
        Changed?.Invoke();
    }

    /// <summary>The screen is going: nobody is there any more</summary>
    public void Clear()
    {
        Phase = StationPhase.Waiting;
        Engine = null;
        Towed = false;
        Changed?.Invoke();
    }

    /// <summary>The car drove in with nothing to show it (no 3D, no arrive script): the bell, and it is at the pump</summary>
    internal void DrivenInUnseen()
    {
        if (Phase != StationPhase.Arriving) return;
        Sfx.Play(Sfx.StationBell);
        ReportAtPump();
    }

    /// <summary>The viewport's word: the car has stopped at the pump</summary>
    internal void ReportAtPump()
    {
        if (Phase != StationPhase.Arriving) return;
        Phase = StationPhase.AtPump;
        AtPump?.Invoke();
    }

    /// <summary>The viewport's word: the car has gone</summary>
    internal void ReportGone()
    {
        if (Phase != StationPhase.Leaving) return;
        Phase = StationPhase.Gone;
        Gone?.Invoke();
    }
}
