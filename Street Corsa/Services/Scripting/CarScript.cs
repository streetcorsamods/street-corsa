namespace StreetCorsa.Services.Scripting
{
    /// <summary>
    /// A car's moves on a screen, as a mod's scripts/*.json has them: what sets it off (<see cref="Trigger"/>), when it
    /// is the one played (<see cref="When"/>), and its steps, one after the other.
    /// </summary>
    /// <param name="Id">The full id (<c>streetcorsa:lot_take_out</c>)</param>
    /// <param name="Trigger">The moment it plays at (<see cref="ScriptTriggers"/>), or one a mod names</param>
    /// <param name="MostSeconds">The longest it may take: past that it is skipped to its end</param>
    public sealed record CarScript(string Id, string Trigger, ScriptCondition When, float MostSeconds, IReadOnlyList<ScriptStep> Steps)
    {
        /// <summary>How long a script may take when it does not say</summary>
        public const float DefaultMostSeconds = 30f;
    }

    /// <summary>The moments of the base game a script plays at</summary>
    public static class ScriptTriggers
    {
        /// <summary>The car picked in the lot is taken out: it drives off, and the screen goes</summary>
        public const string LotTakeOut = "lot.take_out";

        /// <summary>A car taken out of the lot under its own power comes into the garage</summary>
        public const string GarageArrive = "garage.arrive";

        /// <summary>The garage's car leaves for the gas station or the street</summary>
        public const string GarageLeave = "garage.leave";

        /// <summary>The player's car drives in off the road to the pump; the screen goes on once it lets go</summary>
        public const string StationArrive = "station.arrive";

        /// <summary>The player's car has stopped at the pump (or was towed there)</summary>
        public const string StationAtPump = "station.at_pump";

        /// <summary>The player's car pulls out onto the road; the screen goes once it lets go</summary>
        public const string StationLeave = "station.leave";

        /// <summary>A rival drives up the lane and stops alongside the player's parked car</summary>
        public const string StreetRivalArrive = "street.rival_arrive";

        /// <summary>The rival pulls away; it is gone once the script ends</summary>
        public const string StreetRivalLeave = "street.rival_leave";

        public static readonly IReadOnlyList<string> All =
            [LotTakeOut, GarageArrive, GarageLeave, StationArrive, StationAtPump, StationLeave, StreetRivalArrive, StreetRivalLeave];

        /// <summary>
        /// A screen of the flow is up, its own show over (<c>screen.&lt;id&gt;.enter</c>): played on the screens with a
        /// car on a 3D scene (the garage, the car lot, the gas station)
        /// </summary>
        public static string ScreenEnter(string screenId) => $"screen.{screenId}.enter";

        /// <summary>The player leaves a screen by a way out (<c>screen.&lt;id&gt;.leave</c>): the screen goes once the script lets it</summary>
        public static string ScreenLeave(string screenId) => $"screen.{screenId}.leave";

        /// <summary>Whether a trigger is a screen's leave (<see cref="ScreenLeave"/>)</summary>
        public static bool IsScreenLeave(string trigger) =>
            trigger.StartsWith("screen.", StringComparison.Ordinal) && trigger.EndsWith(".leave", StringComparison.Ordinal);
    }

    /// <summary>
    /// A place on a scene's ground: a mark the scene names (<c>"exit"</c>), one every scene has (<c>"home"</c>, where
    /// the car stands when nothing moves it; <c>"here"</c>, where it is now), a point (<c>[x, z]</c>), or a point
    /// measured from the car's home (<c>{ "behind": 7 }</c>, with <c>ahead</c>, <c>left</c>, <c>right</c>)
    /// </summary>
    public sealed record MarkSpec(string? Name = null, float? X = null, float? Z = null, float Ahead = 0f, float Left = 0f)
    {
        public const string Home = "home";
        public const string Here = "here";

        public bool IsPoint => X != null && Z != null;

        public override string ToString() => Name ?? (IsPoint ? $"[{X}, {Z}]" : $"{{ ahead {Ahead}, left {Left} }}");
    }

    /// <summary>How a fade eases: <c>in</c> starts slow, <c>out</c> ends slow</summary>
    public enum FadeEase
    {
        None,
        In,
        Out
    }

    /// <summary>One step of a script; <paramref name="Car"/> is the car it is about (null: the script's own)</summary>
    public abstract record ScriptStep(string? Car);

    /// <summary><c>lights</c>: the headlights and the brake lights on or off (one left out stays as it is)</summary>
    public sealed record LightsStep(string? Car, bool? Head, bool? Brake) : ScriptStep(Car);

    /// <summary>
    /// <c>engine</c>: switched on (with the starter, or running already: <paramref name="Crank"/> false) or off, or the
    /// throttle blipped to <paramref name="Rev"/> for <paramref name="Seconds"/>. Switched on, the step waits until it
    /// runs (<paramref name="Wait"/>, at most <paramref name="MostSeconds"/>) or keeps trying behind the next steps.
    /// </summary>
    public sealed record EngineStep(string? Car, bool? On, bool Crank, double? Rev, float Seconds, bool Wait, float MostSeconds) : ScriptStep(Car);

    /// <summary>What a wait waits for, besides time</summary>
    public enum WaitFor
    {
        Time,

        /// <summary>The body has stopped rocking on its springs</summary>
        Settled,

        /// <summary>The engine runs</summary>
        Running,

        /// <summary>The drive under way is over, or <see cref="WaitStep.EarlySeconds"/> from it</summary>
        DriveEnd
    }

    /// <summary><c>wait</c>: for a time, or until something (at most <paramref name="Seconds"/> then)</summary>
    public sealed record WaitStep(string? Car, WaitFor Until, float Seconds, float EarlySeconds) : ScriptStep(Car);

    /// <summary>
    /// <c>drive</c>: along a path the scene names (<paramref name="Path"/>), straight to a mark (<paramref name="To"/>,
    /// from <paramref name="From"/> or where it is), steering for a mark (<paramref name="Toward"/>, a pull), or a
    /// distance along its own length (<paramref name="Distance"/>, backwards when negative). Its feel says how.
    /// </summary>
    /// <param name="Rolling">Already moving at the start (else from a standstill)</param>
    /// <param name="KeepGoing">Still going at the end (else it stops there)</param>
    /// <param name="Wait">The next step waits until the drive is over</param>
    public sealed record DriveStep(string? Car, string? Path, MarkSpec? From, MarkSpec? To, MarkSpec? Toward, float? Distance,
        bool Reverse, DriveFeel Feel, bool Rolling, bool KeepGoing, bool Wait) : ScriptStep(Car);

    /// <summary><c>place</c>: the car put at a mark at once, or at the start or end of a path, facing along it</summary>
    public sealed record PlaceStep(string? Car, MarkSpec? At, string? Path, bool PathEnd, float? HeadingDegrees) : ScriptStep(Car);

    /// <summary><c>fade</c>: the screen's curtain to <paramref name="To"/> (1 black) over a time; the engine's volume with it</summary>
    public sealed record FadeStep(string? Car, float To, float Seconds, FadeEase Ease, bool Engine) : ScriptStep(Car);

    /// <summary>What the camera does while a car moves</summary>
    public enum CameraMode
    {
        /// <summary>Stays where it is and watches the car go</summary>
        Hold,

        /// <summary>Keeps the car where it has it in the picture</summary>
        Follow
    }

    /// <summary><c>camera</c>: how the camera of a viewport looking at the car goes with it (a seat's view has its own)</summary>
    public sealed record CameraStep(string? Car, CameraMode Mode) : ScriptStep(Car);

    /// <summary><c>if</c>: the steps of <paramref name="Then"/> when the condition holds, else those of <paramref name="Else"/></summary>
    public sealed record IfStep(string? Car, ScriptCondition When, IReadOnlyList<ScriptStep> Then, IReadOnlyList<ScriptStep> Else) : ScriptStep(Car);

    /// <summary><c>sound</c>: a sound cue of the mods' sfx, played once where the screen is (the driveway bell)</summary>
    public sealed record SoundStep(string Cue) : ScriptStep((string?)null);

    /// <summary><c>release</c>: the game goes on (the screen moves on) while the script plays out</summary>
    public sealed record ReleaseStep() : ScriptStep((string?)null);

    /// <summary><c>end</c>: the script is over</summary>
    public sealed record EndStep() : ScriptStep((string?)null);
}
