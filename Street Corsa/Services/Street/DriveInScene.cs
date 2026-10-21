namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// A place the player's car drives into and out of again, seen from the driver's seat (the gas station, the diner):
    /// its model with a sky dome for each light of the day, its ground, the paths and marks its scripts drive along, its
    /// lights, and where the driver looks while the car stands there
    /// </summary>
    public interface IDriveInScene
    {
        string Id { get; }

        /// <summary>The model, a KN5; empty when the file names one outside its folder</summary>
        string Kn5 { get; }

        bool HasModel { get; }

        /// <summary>The ground's height file next to the model (<see cref="Station.StationGround"/>)</summary>
        string GroundFile { get; }

        /// <summary>The key light's direction by day, towards the sun</summary>
        float[] Sun { get; }

        StreetLighting LightingFor(StreetLight light);

        /// <summary>How bright the self-lit materials (a sign, the windows) glow at a light of the day, by material</summary>
        IReadOnlyDictionary<string, float> GlowAt(StreetLight light);

        /// <summary>The sky dome drawn at a light of the day; the model's other domes are hidden</summary>
        string SkyNodeAt(StreetLight light);

        /// <summary>What the names of all the model's sky domes start with (<see cref="SkyNodeAt"/>'s among them)</summary>
        string SkyPrefix { get; }

        /// <summary>
        /// The look groups shown, as node prefixes (<see cref="SceneLooks"/>): every node of the model named from
        /// <see cref="SceneLooks.Prefix"/> is hidden unless its name starts with one of these
        /// </summary>
        IReadOnlyList<string> ShownLooks { get; }

        /// <summary>
        /// What the scripts drive along and to: the way in (<c>arrive</c>), the way out (<c>leave</c>), the place the car
        /// stands at the end of the way in (<see cref="StandMark"/>) and the file's own
        /// </summary>
        Scripting.SceneMarks ScriptMarks { get; }

        /// <summary>The pieces of the street kit standing in the scene beside its model (step 39); none for a scene that is all its model</summary>
        IReadOnlyList<Models.GameState.PlacedPiece> Pieces => [];

        /// <summary>The mark the car stands at when nothing moves it</summary>
        string StandMark { get; }

        /// <summary>Where the driver looks while the car stands there, degrees from straight ahead, to the left positive</summary>
        float LookWhileStanding { get; }

        /// <summary>How far up the driver looks while the car stands there, degrees over the seat's own look</summary>
        float LookUpWhileStanding { get; }
    }
}
