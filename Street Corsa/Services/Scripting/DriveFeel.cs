namespace StreetCorsa.Services.Scripting
{
    /// <summary>How a drive is worked out: what kind of driving it is</summary>
    public enum DriveKind
    {
        /// <summary>Along a path, as fast as its bends allow up to a cruising speed, braking in time for a stop</summary>
        Road,

        /// <summary>Hard off the line through the gears, along a path until its end</summary>
        Launch,

        /// <summary>Pulling away gently in first gear, steering for a point if it has one, and keeping on</summary>
        Pull,

        /// <summary>Rolling in and braking to a stop, the braking fading to nothing as it stops</summary>
        RollIn
    }

    /// <summary>
    /// The feel of a drive (scripts/feels.json): its kind and the numbers that make it. Speeds in metres a second,
    /// accelerations in metres a second squared, times in seconds, lengths in metres. A feel that leaves a number out
    /// has its kind's (<see cref="Defaults"/>): the core's own feels.
    /// </summary>
    public sealed record DriveFeel
    {
        public string Id { get; init; } = "";

        public DriveKind Kind { get; init; } = DriveKind.Road;

        /// <summary>Road: the speed it cruises at between bends; roll in: the speed it rolls in at</summary>
        public float CruiseSpeed { get; init; } = 13.5f;

        /// <summary>Launch and pull: the most it gets to</summary>
        public float TopSpeed { get; init; } = 38f;

        /// <summary>How hard it speeds up: road and pull at most, launch off the line</summary>
        public float Acceleration { get; init; } = 2.2f;

        /// <summary>Road: how hard it brakes for a stop</summary>
        public float Brake { get; init; } = 3.2f;

        /// <summary>Road: the most sideways pull a bend is taken at; the speed in a bend is held to it</summary>
        public float CornerAccel { get; init; } = 1.5f;

        /// <summary>Road: the clutch goes in below this on the way to a stop (or slips under it pulling away)</summary>
        public float ClutchSpeed { get; init; } = 3f;

        /// <summary>Pull: how long the clutch takes to take up</summary>
        public float ClutchSeconds { get; init; } = 0.6f;

        /// <summary>The throttle while cruising (0..1)</summary>
        public double CruiseThrottle { get; init; } = 0.18;

        /// <summary>Pull: the throttle while it pulls hardest (0..1)</summary>
        public double Throttle { get; init; } = 0.45;

        /// <summary>Road: the gear it stays in (1 first); 0 changes up as the speed goes up</summary>
        public int Gear { get; init; }

        /// <summary>Launch: a change of gear, off the throttle</summary>
        public float ShiftSeconds { get; init; } = 0.25f;

        /// <summary>Pull and roll in: the engine's speed over its idle for each metre a second</summary>
        public double RpmPerMetrePerSecond { get; init; } = 260;

        /// <summary>How much of the springs' lean in a bend the body shows (0 none, 1 all)</summary>
        public float Lean { get; init; }

        /// <summary>Pull: straight out of its place this far before it turns for where it is going</summary>
        public float StraightOut { get; init; } = 1.2f;

        /// <summary>Pull: the tightest circle it turns on</summary>
        public float TurningRadius { get; init; } = 4.2f;

        /// <summary>Pull: how sharply it corrects onto where it is going</summary>
        public float SteerGain { get; init; } = 1.5f;

        /// <summary>Pull: the wheel is wound on and off over about this long</summary>
        public float SteerSeconds { get; init; } = 0.45f;

        /// <summary>Pull: the wheel starts winding on this far before the bend</summary>
        public float SteerLead { get; init; } = 0.6f;

        /// <summary>Half the distance between the axles: the car turns about its rear axle, this far behind its middle</summary>
        public float HalfWheelbase { get; init; } = 1.4f;

        /// <summary>Pull: how long it goes on for</summary>
        public float Seconds { get; init; } = 12f;

        /// <summary>The gearbox's ratios, first gear first</summary>
        public IReadOnlyList<float> Ratios { get; init; } = [2.52f, 1.88f, 1.46f, 1.0f];

        public float FinalDrive { get; init; } = 3.55f;

        public float TyreRadius { get; init; } = 0.33f;

        /// <summary>How far a wheel turns for a distance travelled, in radians</summary>
        public float WheelAngle(float distance) => distance / TyreRadius;

        /// <summary>The engine's speed with the wheels at <paramref name="speed"/> in <paramref name="gear"/> (0 first)</summary>
        public double GearRpm(float speed, int gear) =>
            speed / (2 * Math.PI * TyreRadius) * 60 * Ratios[Math.Clamp(gear, 0, Ratios.Count - 1)] * FinalDrive;

        /// <summary>The core's feel of each kind: what a feel that leaves a number out has</summary>
        public static DriveFeel Defaults(DriveKind kind) => kind switch
        {
            DriveKind.Launch => new DriveFeel { Kind = kind, Acceleration = 5.2f, TopSpeed = 38f, ShiftSeconds = 0.25f },
            DriveKind.Pull => new DriveFeel
            {
                Kind = kind, Acceleration = 1.8f, TopSpeed = 5f, ClutchSeconds = 0.6f, RpmPerMetrePerSecond = 260,
                Throttle = 0.45, CruiseThrottle = 0.2, Lean = 0.6f
            },
            DriveKind.RollIn => new DriveFeel { Kind = kind, CruiseSpeed = 5f, RpmPerMetrePerSecond = 156 },
            _ => new DriveFeel { Kind = kind }
        };

        /// <summary>The feel a drive has when its script names none</summary>
        public static readonly DriveFeel Road = Defaults(DriveKind.Road) with { Id = "road" };
    }
}
