using System.IO;
using StreetCorsa.Services.Scripting;
using StreetCorsa.Services.Station;
using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// The gas station, seen from the driver's seat of the player's car: it comes up the road, turns in and stops at the
/// pump, and pulls out onto the road again (<see cref="DriveInViewport3D"/>). The driver looks at the pump while the
/// tank fills; a drive in with no script for it still rings the driveway bell. The attendant (<see cref="ActorProps"/>,
/// the station's <see cref="StationScene.Attendant"/>) stands by the pump the car stops at, talking with his hands,
/// from the moment the station is on screen.
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

    protected override void OnSceneUp(GarageRenderer renderer) => ShowAttendant(renderer);

    /// <summary>
    /// The attendant where the station has him, on its ground, his talk going round: his model read off the UI thread
    /// and set into the renderer once it is up (a station with no attendant, or one whose prop is missing or broken,
    /// has none: logged, and in the mods' problems).
    /// </summary>
    private async void ShowAttendant(GarageRenderer renderer)
    {
        if (Scene is not StationScene { Attendant: { } spot }) return;

        var problems = new List<string>();
        var actor = ActorProps.Find(Mods.ModSet.Installed, spot.Prop, problems);
        foreach (var problem in problems) Logger.Warning("Station attendant: {Problem}", problem);
        Mods.ModProblems.Add("Station attendant", problems);
        if (actor == null)
        {
            Logger.Warning("The station names an attendant {Prop} no mod has: nobody is at the pump", spot.Prop);
            return;
        }

        // A mod's model or animation is not trusted to be whole: AcTools throws a plain Exception for a file that is no
        // KN5, and the animator parses the .ksanim as it is made, so anything that escapes leaves nobody at the pump
        AcTools.Kn5File.IKn5 kn5;
        try
        {
            kn5 = await Task.Run(() => AcTools.Kn5File.Kn5.FromFile(actor.Model));
        }
        catch (Exception ex)
        {
            Logger.Warning("The attendant could not be read from {Model}: {Error}", actor.Model, ex.Message);
            return;
        }

        if (Renderer != renderer || !IsLoaded) return;
        var placement = Sx.Matrix.RotationY(spot.Heading * MathF.PI / 180f) * Sx.Matrix.Translation(spot.X, GroundAt(spot.X, spot.Z) + spot.Y, spot.Z);
        try
        {
            renderer.SetActor(kn5, placement, actor.Animation, actor.Seconds);
        }
        catch (Exception ex)
        {
            Logger.Warning("The attendant {Model} could not be set up with {Animation}: {Error}", actor.Model, actor.Animation, ex.Message);
            renderer.SetActor(null, placement, null, 0f);
        }
        Invalidate();
    }
}
