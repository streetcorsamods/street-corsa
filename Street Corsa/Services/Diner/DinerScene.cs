using System.IO;
using System.Text.Json;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Diner
{
    /// <summary>A parking bay: the middle of a car standing in it, and the way it faces (radians, as <see cref="Scripting.GroundPose.Heading"/>)</summary>
    public readonly record struct DinerBay(float X, float Z, float Heading);

    /// <summary>
    /// The diner on a street, as a mod's streets/&lt;folder&gt;/diner.json describes it next to the street's street.json:
    /// its model (the street's one model, with a sky for each light of the day, <see cref="StreetScene.SkyNode"/>) and
    /// ground, the way the player's car comes down the street and parks in its bay
    /// (<c>arrive</c>), backs out of it (<c>back_out</c>) and drives off (<c>leave</c>), where the driver looks while it
    /// stands there, the bays the racers' cars stand in, the lights of the day, and who made what it is built of.
    /// </summary>
    public sealed record DinerScene(
        string Id,
        string Title,
        string Folder,
        string Model,
        string Credit,
        IReadOnlyList<GroundPoint> Arrive,
        IReadOnlyList<GroundPoint> BackOut,
        IReadOnlyList<GroundPoint> Leave,
        float LookWhileParked,
        float LookUpWhileParked,
        IReadOnlyList<DinerBay> Bays,
        float[] Sun,
        IReadOnlyDictionary<StreetLight, StreetLighting> Lights) : IDriveInScene
    {
        /// <summary>
        /// The street's model, a KN5 in its folder; the diner works without it (no 3D). Empty when the file names a model
        /// outside the folder: the game reads no file a mod points it to elsewhere.
        /// </summary>
        public string Kn5 => Helpers.PathNames.TryCombineUnder(Folder, Model, out var kn5) ? kn5 : string.Empty;

        public bool HasModel => Kn5.Length > 0 && File.Exists(Kn5);

        /// <summary>The ground's height file next to the model (<see cref="StationGround"/>)</summary>
        public string GroundFile => Path.Combine(Folder, "ground.bin");

        string IDriveInScene.SkyNodeAt(StreetLight light) => StreetScene.SkyNode(light);

        string IDriveInScene.SkyPrefix => StreetScene.SkyPrefix;

        public StreetLighting LightingFor(StreetLight light) => SceneLighting.For(Lights, light);

        /// <summary>How bright each self-lit material (the signs, the windows) glows, by light of the day</summary>
        public IReadOnlyDictionary<StreetLight, IReadOnlyDictionary<string, float>> Glow { get; init; } =
            new Dictionary<StreetLight, IReadOnlyDictionary<string, float>>();

        public IReadOnlyDictionary<string, float> GlowAt(StreetLight light) => SceneLighting.GlowAt(Glow, light);

        /// <summary>The car stands in its bay, at the end of the way in</summary>
        public string StandMark => "bay";

        float IDriveInScene.LookWhileStanding => LookWhileParked;

        float IDriveInScene.LookUpWhileStanding => LookUpWhileParked;

        /// <summary>The scene's own marks and paths for the scripts (<see cref="Scripting.SceneMarks"/>)</summary>
        public Scripting.SceneMarks Marks { get; init; } = Scripting.SceneMarks.None;

        /// <summary>
        /// What the scripts drive along and to: the file's own, and the ways in, back out and out (<c>arrive</c>,
        /// <c>back_out</c>, <c>leave</c>) and the bay at the end of the way in (<c>bay</c>) unless the file names its own
        /// </summary>
        public Scripting.SceneMarks ScriptMarks
        {
            get
            {
                var paths = new Dictionary<string, IReadOnlyList<GroundPoint>> { ["arrive"] = Arrive, ["back_out"] = BackOut, ["leave"] = Leave };
                var marks = Marks.With(paths: paths);
                var end = marks.Paths["arrive"][^1];
                return marks.With(new Dictionary<string, Scripting.MarkSpec> { ["bay"] = new(X: end.X, Z: end.Z) });
            }
        }
    }

    public static class DinerScenes
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Diner");

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        private sealed record BayEntry(float X, float Z, float Heading);

        private sealed record SceneEntry(string? Id, string? Title, string? Model, string? Credit, List<float[]>? Arrive, List<float[]>? BackOut,
            List<float[]>? Leave, float? LookWhileParked, float? LookUpWhileParked, List<BayEntry?>? Bays, float[]? Sun, Dictionary<string, SceneLighting.LightEntry?>? Lights,
            Dictionary<string, Dictionary<string, float>?>? Glow);

        /// <summary>Every diner of every mod's streets (a street's folder with a diner.json) that loads, by full id (a mod's get its namespace); empty when none does</summary>
        public static IReadOnlyList<DinerScene> LoadAll(ModSet mods) =>
            ModScenes.LoadAll(mods, ContentTypes.Streets, "Diners", Logger, Load, s => s.Id, (s, id) => s with { Id = id });

        private static readonly Lazy<IReadOnlyList<DinerScene>> InstalledScenes = new(() => LoadAll(ModSet.Installed));

        /// <summary>The diner streets of the mods next to the game, read once</summary>
        public static IReadOnlyList<DinerScene> Installed => InstalledScenes.Value;

        /// <summary>The street the diner is seen on: the last mod's, so a mod's street takes the base game's place; null when there is none</summary>
        public static DinerScene? Current => Installed.Count == 0 ? null : Installed[^1];

        /// <summary>
        /// The diner on the street in <paramref name="folder"/>; null when it has none, or its file is broken (logged). What
        /// the diner's file leaves out (the model, the sun, the lights, the glow, the credit) is the street's. A mod's file
        /// is not trusted to be whole: a bay or a light left empty is skipped, and a model outside the folder is not read.
        /// </summary>
        public static DinerScene? Load(string folder)
        {
            var file = Path.Combine(folder, "diner.json");
            try
            {
                if (!File.Exists(file)) return null;
                using var document = Scripting.SceneMarks.Parse(file);
                if (Build(folder, file, document.RootElement.Deserialize<SceneEntry>(Options), StreetScenes.Load(folder)) is not { } scene) return null;
                var problems = new List<string>();
                scene = scene with { Marks = Scripting.SceneMarks.Read(document.RootElement, file, problems) };
                ModProblems.Add("Diners", problems);
                return scene;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not read the diner street {Path}", file);
                ModProblems.Add("Diners", $"{file} could not be read ({ex.Message}); left out");
                return null;
            }
        }

        private static DinerScene? Build(string folder, string file, SceneEntry? entry, StreetScene? street)
        {
            // The car leaves the way it came in, backwards out of its bay: the diner's scripts drive all three
            var arrive = ScenePaths.Points(entry?.Arrive);
            var backOut = ScenePaths.Points(entry?.BackOut);
            var leave = ScenePaths.Points(entry?.Leave);
            if (entry == null || !ScenePaths.GoesSomewhere(arrive) || !ScenePaths.GoesSomewhere(backOut) || !ScenePaths.GoesSomewhere(leave))
            {
                Logger.Warning("The diner street {Path} has no way in, back out or out (two points each at least, a metre apart)", file);
                ModProblems.Add("Diners", $"{file} has no way in, back out or out (two points each at least, a metre apart); left out");
                return null;
            }

            var bays = (entry.Bays ?? []).Where(b => b != null && float.IsFinite(b.X) && float.IsFinite(b.Z) && float.IsFinite(b.Heading))
                .Select(b => new DinerBay(b!.X, b.Z, b.Heading * MathF.PI / 180f)).ToList();
            var id = entry.Id ?? Path.GetFileName(folder);
            var scene = new DinerScene(id, entry.Title ?? GameText.Say("diner.title"), folder, entry.Model ?? street?.Model ?? "street.kn5",
                entry.Credit ?? street?.Credit ?? string.Empty, arrive, backOut, leave,
                entry.LookWhileParked ?? 0f, Math.Clamp(entry.LookUpWhileParked ?? 0f, -15f, 15f), bays,
                entry.Sun is { Length: 3 } sun ? sun : street?.Sun ?? [0.3f, 0.7f, 0.65f],
                entry.Lights == null && street != null ? street.Lights : SceneLighting.Read(entry.Lights, id, Logger))
            {
                Glow = entry.Glow == null && street != null ? street.Glow : SceneLighting.MaterialGlows(entry.Glow)
            };
            if (scene.Kn5.Length == 0)
                Logger.Warning("The diner street {Id} names a model outside its folder ({Model}): it is shown without its picture", scene.Id, scene.Model);
            else if (!scene.HasModel)
                Logger.Warning("The diner street {Id} has no model {Path}: it is shown without its picture", scene.Id, scene.Kn5);
            return scene;
        }


    }
}
