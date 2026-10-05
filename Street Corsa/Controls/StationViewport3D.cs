using StreetCorsa.Services.Scripting;

namespace StreetCorsa.Controls;

/// <summary>
/// The gas station, seen from the driver's seat of the player's car: it comes up the road, turns in and stops at the
/// pump, and pulls out onto the road again (<see cref="DriveInViewport3D"/>). The driver looks at the pump while the
/// tank fills; a drive in with no script for it still rings the driveway bell.
/// </summary>
public class StationViewport3D : DriveInViewport3D
{
    public StationViewport3D()
        : base("Station")
    {
    }

    protected override string ViewportName => "Station viewport";

    protected override string ArriveTrigger => ScriptTriggers.StationArrive;

    protected override string StandTrigger => ScriptTriggers.StationAtPump;

    protected override string LeaveTrigger => ScriptTriggers.StationLeave;

    // The bell rings as the script's would
    protected override void ArrivedUnscripted() => Audio.Sfx.Play(Audio.Sfx.StationBell);
}
