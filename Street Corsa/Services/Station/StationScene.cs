using System.IO;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.Station
{
    /// <summary>A point on the ground of a scene, metres from its middle</summary>
    public readonly record struct GroundPoint(float X, float Z);

    /// <summary>
    /// A gas station, as a mod's stations/&lt;folder&gt;/station.json describes it: its model (with a sky for each light of
    /// the day, <see cref="SkyNode"/>) and ground, the way a car drives in off the road to the pump and out again, where
    /// the driver looks while the tank fills, the lights of the day and what the station charges.
    /// </summary>
    public sealed record StationScene(
        string Id,
        string Title,
        string Folder,
        string Model,
        IReadOnlyList<GroundPoint> Arrive,
        IReadOnlyList<GroundPoint> Leave,
        float LookAtPump,
        float[] Sun,
        IReadOnlyDictionary<StreetLight, StreetLighting> Lights,
        StationPrices Prices) : IDriveInScene
    {
        /// <summary>
        /// The station's model, a KN5 in its folder; the screen works without it (no 3D), the drive and all. Empty when
        /// the file names a model outside the folder: the game reads no file a mod points it to elsewhere.
        /// </summary>
        public string Kn5 => PathNames.TryCombineUnder(Folder, Model, out var kn5) ? kn5 : string.Empty;

        public bool HasModel => Kn5.Length > 0 && File.Exists(Kn5);

        /// <summary>The ground's height file next to the model (<see cref="StationGround"/>)</summary>
        public string GroundFile => Path.Combine(Folder, "ground.bin");

        /// <summary>The sky dome drawn at a light of the day; the others are hidden</summary>
        public static string SkyNode(StreetLight light) => SkyPrefix + StreetLights.Describe(light).ToUpperInvariant();

        /// <summary>What the names of the station's sky domes start with</summary>
        public const string SkyPrefix = "STATION_SKY_";

        string IDriveInScene.SkyNodeAt(StreetLight light) => SkyNode(light);

        string IDriveInScene.SkyPrefix => SkyPrefix;

        /// <summary>The car stands at the pump, at the end of the way in</summary>
        public string StandMark => "pump";

        float IDriveInScene.LookWhileStanding => LookAtPump;

        float IDriveInScene.LookUpWhileStanding => 0f;

        IReadOnlyDictionary<string, float> IDriveInScene.GlowAt(StreetLight light) => new Dictionary<string, float>();

        public StreetLighting LightingFor(StreetLight light) => SceneLighting.For(Lights, light);

        /// <summary>The scene's own marks and paths for the scripts (<see cref="Scripting.SceneMarks"/>)</summary>
        public Scripting.SceneMarks Marks { get; init; } = Scripting.SceneMarks.None;

        /// <summary>
        /// What the scripts drive along and to: the file's own, and the ways in and out (<c>arrive</c>, <c>leave</c>)
        /// and the pump at the end of the way in (<c>pump</c>, the file's way in if it has its own) unless the file
        /// names its own. The car stands at the pump when nothing moves it.
        /// </summary>
        public Scripting.SceneMarks ScriptMarks
        {
            get
            {
                var marks = Marks.With(paths: new Dictionary<string, IReadOnlyList<GroundPoint>> { ["arrive"] = Arrive, ["leave"] = Leave });
                var end = marks.Paths["arrive"][^1];
                return marks.With(new Dictionary<string, Scripting.MarkSpec> { ["pump"] = new(X: end.X, Z: end.Z) });
            }
        }
    }

    public static class StationScenes
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Station");

        private sealed record PricesEntry(decimal? FuelPerLitre, decimal? OilPerLitre, decimal? Wash, decimal? Tow);

        private sealed record SceneEntry(string? Id, string? Title, string? Model, List<float[]>? Arrive, List<float[]>? Leave,
            float? LookAtPump, float[]? Sun, Dictionary<string, SceneLighting.LightEntry?>? Lights, PricesEntry? Prices);

        /// <summary>Every gas station of every mod that loads, by full id (a mod's get its namespace); empty when none does</summary>
        public static IReadOnlyList<StationScene> LoadAll(ModSet mods) =>
            ModScenes.LoadAll(mods, ContentTypes.Stations, "Stations", Logger, Load, s => s.Id, (s, id) => s with { Id = id });

        private static readonly Lazy<IReadOnlyList<StationScene>> InstalledScenes = new(() => LoadAll(ModSet.Installed));

        /// <summary>The gas stations of the mods next to the game, read once</summary>
        public static IReadOnlyList<StationScene> Installed => InstalledScenes.Value;

        /// <summary>
        /// The station in <paramref name="folder"/>; null when its file is missing or broken (logged). A mod's file is not
        /// trusted to be whole: a light or a lamp left empty is skipped, and a model outside the folder is not read.
        /// </summary>
        public static StationScene? Load(string folder)
        {
            var file = Path.Combine(folder, "station.json");
            return SceneFiles.Load<SceneEntry, StationScene>(file, "Stations", "gas station", Logger, entry => Build(folder, file, entry),
                (station, marks) => station with { Marks = marks });
        }

        private static StationScene? Build(string folder, string file, SceneEntry? entry)
        {
            var arrive = ScenePaths.Points(entry?.Arrive);
            var leave = ScenePaths.Points(entry?.Leave);
            if (entry == null || !ScenePaths.GoesSomewhere(arrive) || !ScenePaths.GoesSomewhere(leave))
            {
                Logger.Warning("The gas station {Path} has no way in or no way out (two points each at least, a metre apart)", file);
                ModProblems.Add("Stations", $"{file} has no way in or no way out (two points each at least, a metre apart); left out");
                return null;
            }

            var defaults = StationPrices.Default;
            var prices = entry.Prices == null ? defaults : new StationPrices(
                Price(entry.Prices.FuelPerLitre, defaults.FuelPerLitre), Price(entry.Prices.OilPerLitre, defaults.OilPerLitre),
                Price(entry.Prices.Wash, defaults.Wash), Price(entry.Prices.Tow, defaults.Tow));

            var station = new StationScene(entry.Id ?? Path.GetFileName(folder), entry.Title ?? entry.Id ?? GameText.Say("station.ui.default_title"), folder,
                entry.Model ?? "station.kn5", arrive, leave, entry.LookAtPump ?? 72f, entry.Sun is { Length: 3 } sun ? sun : [0.2f, 1f, 0.8f],
                SceneLighting.Read(entry.Lights, entry.Id ?? Path.GetFileName(folder), Logger), prices);
            if (station.Kn5.Length == 0)
                Logger.Warning("The gas station {Id} names a model outside its folder ({Model}): it is shown without its picture", station.Id, station.Model);
            else if (!station.HasModel)
                Logger.Warning("The gas station {Id} has no model {Path}: it is shown without its picture", station.Id, station.Kn5);
            return station;
        }

        // A price a mod left out, or one no station would ask, is the base game's
        private static decimal Price(decimal? price, decimal fallback) => price is > 0 and < 10000 ? price.Value : fallback;
    }
}
