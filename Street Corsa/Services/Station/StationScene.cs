using System.IO;
using System.Text.Json;
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
        StationPrices Prices)
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
        public static string SkyNode(StreetLight light) => "STATION_SKY_" + StreetLights.Describe(light).ToUpperInvariant();

        public StreetLighting LightingFor(StreetLight light) => SceneLighting.For(Lights, light);

        /// <summary>The scene's own marks and paths for the scripts (<see cref="Scripting.SceneMarks"/>)</summary>
        public Scripting.SceneMarks Marks { get; init; } = Scripting.SceneMarks.None;

        /// <summary>
        /// What the scripts drive along and to: the file's own, and the ways in and out (<c>arrive</c>, <c>leave</c>)
        /// and the pump at the end of the way in (<c>pump</c>) unless the file names its own
        /// </summary>
        public Scripting.SceneMarks ScriptMarks => Marks.With(
            new Dictionary<string, Scripting.MarkSpec> { ["pump"] = new(X: Arrive[^1].X, Z: Arrive[^1].Z) },
            new Dictionary<string, IReadOnlyList<GroundPoint>> { ["arrive"] = Arrive, ["leave"] = Leave });
    }

    public static class StationScenes
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Station");

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

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
            try
            {
                if (!File.Exists(file)) return null;
                if (Build(folder, file, JsonSerializer.Deserialize<SceneEntry>(File.ReadAllText(file), Options)) is not { } station) return null;
                var problems = new List<string>();
                station = station with { Marks = Scripting.SceneMarks.ReadFile(file, problems) };
                ModProblems.Add("Stations", problems);
                return station;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not read the gas station {Path}", file);
                ModProblems.Add("Stations", $"{file} could not be read ({ex.Message}); left out");
                return null;
            }
        }

        private static StationScene? Build(string folder, string file, SceneEntry? entry)
        {
            var arrive = Points(entry?.Arrive);
            var leave = Points(entry?.Leave);
            if (entry == null || !GoesSomewhere(arrive) || !GoesSomewhere(leave))
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

        /// <summary>A way that gets a car somewhere: two points at least, not all on one spot</summary>
        private static bool GoesSomewhere(IReadOnlyList<GroundPoint> points) =>
            points.Count >= 2 && points.Any(p => MathF.Abs(p.X - points[0].X) + MathF.Abs(p.Z - points[0].Z) >= 1f);

        private static List<GroundPoint> Points(List<float[]>? points) =>
            (points ?? []).Where(p => p is { Length: 2 } && float.IsFinite(p[0]) && float.IsFinite(p[1])).Select(p => new GroundPoint(p[0], p[1])).ToList();

        // A price a mod left out, or one no station would ask, is the base game's
        private static decimal Price(decimal? price, decimal fallback) => price is > 0 and < 10000 ? price.Value : fallback;
    }
}
