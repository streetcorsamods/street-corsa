using StreetCorsa.Services.Station;

namespace StreetCorsa.Services.Scripting
{
    /// <summary>A car's engine as a script works it</summary>
    public interface IScriptEngine
    {
        /// <summary>The car has an engine that starts</summary>
        bool Runs { get; }

        /// <summary>It is running now (not cranking)</summary>
        bool IsRunning { get; }

        /// <summary>The key turned: the starter cranks it (<paramref name="crank"/>), or it is running at once (a car that drives up)</summary>
        void Start(bool crank);

        void Stop();

        /// <summary>The throttle held this far (0..1); null lets it go</summary>
        void Pedal(double? depth);

        /// <summary>How loud it is against the level the screen has it at, 0..1 (a fade takes it down with the picture)</summary>
        void Volume(float scale);

        /// <summary>What it idles at and where its limiter is: a drive's revs are worked out from them</summary>
        (double Idle, double Limiter) Revs { get; }
    }

    /// <summary>A car on a screen, as a script moves it</summary>
    public interface IScriptCar
    {
        /// <summary>Where it stands when nothing moves it</summary>
        GroundPose Home { get; }

        /// <summary>Where it is now</summary>
        GroundPose Pose { get; }

        /// <summary>Its engine; null when nothing is heard of it here</summary>
        IScriptEngine? Engine { get; }

        /// <summary>Its body has stopped rocking on its springs, and it is not on the move</summary>
        bool IsSettled { get; }

        /// <summary>Seconds left of the drive under way; 0 when it is not driving</summary>
        float DriveLeft { get; }

        /// <summary>Its lights on or off; one left null stays as it is</summary>
        void Lights(bool? head, bool? brake);

        /// <summary>Sets off on <paramref name="plan"/>; done when the drive is over, or cut short (the car jumped, or gone)</summary>
        Task Drive(DrivePlan plan);

        /// <summary>To the end of <paramref name="plan"/> at once, the drive under way (if any) over</summary>
        void Jump(DrivePlan plan);

        /// <summary>Put down at <paramref name="pose"/> at once, standing</summary>
        void Place(GroundPose pose);
    }

    /// <summary>
    /// A screen a script plays on: its cars (by role: <c>player</c>, <c>rival</c>; the trigger's own car when a step
    /// names none), its marks and paths, its curtain and its camera. Lives on the UI thread.
    /// </summary>
    public interface IScriptStage
    {
        /// <summary>A car by role; null for the script's own car. Null when there is no such car here.</summary>
        IScriptCar? Car(string? role);

        /// <summary>A mark the scene names (it may be measured from a car's home); null when it has none of that name</summary>
        MarkSpec? Mark(string name);

        /// <summary>A path the scene names; null when it has none</summary>
        IReadOnlyList<GroundPoint>? Path(string name);

        /// <summary>The screen's curtain to <paramref name="opacity"/> (1 black); done at once on a screen without one</summary>
        Task Fade(float opacity, TimeSpan over, FadeEase ease);

        /// <summary>How the camera goes with <paramref name="car"/> while it moves; a screen whose camera is a seat ignores it</summary>
        void Camera(IScriptCar car, CameraMode mode);

        /// <summary>A sound cue heard on the screen; every screen plays it through the game's sound effects</summary>
        void Sound(string cue) => Audio.Sfx.Play(cue);
    }

    /// <summary>How a script played</summary>
    public enum ScriptEnd
    {
        /// <summary>To its end</summary>
        Finished,

        /// <summary>Skipped to its end: a click, or it took longer than it may</summary>
        Skipped,

        /// <summary>A step went wrong: the rest was skipped to the end</summary>
        Failed,

        /// <summary>The screen went: nothing more was done</summary>
        Cancelled,

        /// <summary>No script plays at this moment</summary>
        None
    }
}
