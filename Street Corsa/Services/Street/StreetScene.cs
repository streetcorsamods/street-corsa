using System.IO;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Street
{
    /// <summary>How the street is lit: by day, at dusk, at night</summary>
    public enum StreetLight
    {
        Day,
        Dusk,
        Night
    }

    /// <summary>
    /// Which light the street is in at an hour of the game. The sun sets earlier in winter than in summer (about
    /// 16:25 at the turn of the year, 20:10 at midsummer, as in the middle of the States): dusk is the hour and a
    /// half before it, night from half an hour after it.
    /// </summary>
    public static class StreetLights
    {
        // The rules' world: when the sun sets over the year, and when the street is light in the morning
        public static double SunsetHour(DateTime date)
        {
            var world = Rules.RuleBook.Current.World;
            return world.MeanSunset + world.SunsetSwing * Math.Cos(2 * Math.PI * (date.DayOfYear - world.Midsummer) / 365.0);
        }

        public static StreetLight At(DateTime time)
        {
            var world = Rules.RuleBook.Current.World;
            var hour = time.TimeOfDay.TotalHours;
            var sunset = SunsetHour(time);
            if (hour >= sunset + 0.5 || hour < world.DawnHour) return StreetLight.Night;
            if (hour >= sunset - 1.5 || hour < world.DayFromHour) return StreetLight.Dusk;
            return StreetLight.Day;
        }

        /// <summary>How the street is spoken of at that light: "afternoon", "dusk", "night"</summary>
        public static string Describe(StreetLight light) => light switch
        {
            StreetLight.Day => "day",
            StreetLight.Dusk => "dusk",
            _ => "night"
        };
    }

    /// <summary>Where a car stands in the street: metres from the middle, degrees about the vertical (0 faces +z)</summary>
    public readonly record struct StreetSpot(float X, float Z, float Heading);

    /// <summary>A street light lit at night</summary>
    public sealed record StreetLamp(float X, float Y, float Z, byte[] Color, float Brightness, float Range);

    /// <summary>
    /// The renderer's light for a light of the day; colours r, g, b. <paramref name="CubemapAmbient"/> is the fill
    /// the renderer lights cars with from its reflections: over 0 it is a white light of about the same strength
    /// whatever the value, so the cars glow at night; 0 lights them with the ambient colours alone.
    /// </summary>
    public sealed record StreetLighting(float Brightness, byte[] Color, float Ambient, byte[] AmbientUp, byte[] AmbientDown,
        float CubemapAmbient, IReadOnlyList<StreetLamp> Lamps);

    /// <summary>
    /// A street the Cruise screen is set in, as a mod's streets/&lt;folder&gt;/street.json describes it: its models for
    /// each light, where the player's car is parked and the lane the rivals drive up in, and the lights of the day it
    /// is used in (<see cref="Times"/>).
    /// </summary>
    public sealed record StreetScene(
        string Id,
        string Title,
        string Folder,
        StreetSpot Player,
        float LaneOffset,
        float RivalStop,
        float ApproachFrom,
        float LeaveTo,
        float[] Sun,
        IReadOnlyDictionary<StreetLight, StreetLighting> Lights)
    {
        /// <summary>
        /// The lights of the day the street is used in: what its street.json says ("times": ["day", "dusk"]), else
        /// every light it has a model for. Never one it has no model for.
        /// </summary>
        public IReadOnlySet<StreetLight> Times { get; init; } = new HashSet<StreetLight>(Enum.GetValues<StreetLight>());

        /// <summary>The light itself when the street is used at it, else the nearest one it is (the later on a tie)</summary>
        public StreetLight NearestTime(StreetLight light) =>
            Times.Contains(light) || Times.Count == 0 ? light : Times.OrderBy(t => Math.Abs(t - light)).ThenByDescending(t => t).First();

        /// <summary>
        /// One model for every light of the day (<c>"model"</c>), with a sky dome for each (<see cref="SkyNode"/>); null
        /// when the street has a model for each light instead (street_day.kn5 and so on)
        /// </summary>
        public string? Model { get; init; }

        /// <summary>How bright each self-lit material of the street glows, by light of the day</summary>
        public IReadOnlyDictionary<StreetLight, IReadOnlyDictionary<string, float>> Glow { get; init; } =
            new Dictionary<StreetLight, IReadOnlyDictionary<string, float>>();

        /// <summary>Who made what the street is built of</summary>
        public string Credit { get; init; } = string.Empty;

        /// <summary>
        /// The look groups the street shows on the Cruise screen and at the curb, as node prefixes (<see cref="SceneLooks"/>,
        /// <c>"looks"</c> in street.json); a diner on the street puts its own on instead
        /// </summary>
        public IReadOnlyList<string> ShownLooks { get; init; } = [];

        /// <summary>
        /// How the street is laid out with the street kit (<c>"layout"</c> in street.json, step 39): its model then is the
        /// stage, and each save lays it out once (<see cref="StreetLayouts"/>); null for a street that is all its model
        /// </summary>
        public StreetRecipe? Recipe { get; init; }

        /// <summary>The pieces of the street kit standing on it, as the save has them laid out (<see cref="StreetLayouts"/>)</summary>
        public IReadOnlyList<Models.GameState.PlacedPiece> Pieces { get; init; } = [];

        /// <summary>
        /// The model of the street in that light: its one model, else street_day.kn5 and so on. Empty when the file names
        /// a model outside the folder: the game reads no file a mod points it to elsewhere.
        /// </summary>
        public string Kn5For(StreetLight light) => Model == null ? Path.Combine(Folder, $"street_{StreetLights.Describe(light)}.kn5")
            : Helpers.PathNames.TryCombineUnder(Folder, Model, out var kn5) ? kn5 : string.Empty;

        /// <summary>A one-model street's sky dome drawn at a light of the day; the others are hidden</summary>
        public static string SkyNode(StreetLight light) => SkyPrefix + StreetLights.Describe(light).ToUpperInvariant();

        /// <summary>What the names of a one-model street's sky domes start with</summary>
        public const string SkyPrefix = "STREET_SKY_";

        public IReadOnlyDictionary<string, float> GlowAt(StreetLight light) => SceneLighting.GlowAt(Glow, light);

        /// <summary>The light of the day as the renderer gets it; the day's when the file leaves one out</summary>
        public StreetLighting LightingFor(StreetLight light) => SceneLighting.For(Lights, light);

        /// <summary>The scene's own marks and paths for the scripts (<see cref="Scripting.SceneMarks"/>)</summary>
        public Scripting.SceneMarks Marks { get; init; } = Scripting.SceneMarks.None;

        /// <summary>A point of the rivals' lane, <paramref name="along"/> metres ahead of the player's car</summary>
        public Station.GroundPoint Lane(float along)
        {
            var heading = Player.Heading * MathF.PI / 180f;

            // A car's own +x is its left (its driver's side, in the cars of the day)
            return new Station.GroundPoint(Player.X + MathF.Sin(heading) * along + MathF.Cos(heading) * LaneOffset,
                Player.Z + MathF.Cos(heading) * along - MathF.Sin(heading) * LaneOffset);
        }

        /// <summary>
        /// What the scripts drive along and to: the file's own, and up the lane to a stop beside the player
        /// (<c>arrive</c>), on from there out of sight (<c>leave</c>), the stop at the end of the way up (<c>alongside</c>,
        /// the file's way up if it has its own) and where a car is out of sight up the lane (<c>gone</c>) unless it names
        /// its own. The rival stands alongside when nothing moves it.
        /// </summary>
        public Scripting.SceneMarks ScriptMarks
        {
            get
            {
                var marks = Marks.With(paths: new Dictionary<string, IReadOnlyList<Station.GroundPoint>>
                {
                    ["arrive"] = [Lane(-ApproachFrom), Lane(RivalStop)],
                    ["leave"] = [Lane(RivalStop), Lane(LeaveTo)]
                });
                var end = marks.Paths["arrive"][^1];
                return marks.With(new Dictionary<string, Scripting.MarkSpec>
                {
                    ["alongside"] = new(X: end.X, Z: end.Z),
                    ["gone"] = new(X: Lane(LeaveTo).X, Z: Lane(LeaveTo).Z)
                });
            }
        }
    }

    public static class StreetScenes
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Street");

        private sealed record SpotEntry(float X, float Z, float Heading);

        private sealed record SceneEntry(string? Id, string? Title, string? Model, string? Credit, SpotEntry? Player, float LaneOffset, float RivalStop,
            float ApproachFrom, float LeaveTo, float[]? Sun, Dictionary<string, SceneLighting.LightEntry?>? Lights, List<string?>? Times,
            Dictionary<string, Dictionary<string, float>?>? Glow, List<string?>? Looks, System.Text.Json.JsonElement? Layout);

        /// <summary>
        /// Every street of every mod (a folder under a mod's <c>streets</c>) that loads, by full id: a mod's streets get
        /// its namespace. Empty when there is none (logged).
        /// </summary>
        public static IReadOnlyList<StreetScene> LoadAll(ModSet mods) =>
            ModScenes.LoadAll(mods, ContentTypes.Streets, "Streets", Logger, Load, s => s.Id, (s, id) => s with { Id = id });

        private static readonly Lazy<IReadOnlyList<StreetScene>> InstalledScenes = new(() => LoadAll(ModSet.Installed));

        /// <summary>The streets of the mods next to the game, read once</summary>
        public static IReadOnlyList<StreetScene> Installed => InstalledScenes.Value;

        /// <summary>A street that is left out: in the log, and in the mods' problems for the player</summary>
        private static void LeftOut(string problem)
        {
            Logger.Warning("{Problem}", problem);
            ModProblems.Add("Streets", problem);
        }

        /// <summary>
        /// The street for a light of the day: the one the player is in while it still fits, else one of those that fit,
        /// at random; null when none does
        /// </summary>
        public static StreetScene? Pick(IReadOnlyList<StreetScene> streets, StreetLight light, Random random, StreetScene? current = null)
        {
            if (current != null && current.Times.Contains(light)) return current;

            var fitting = streets.Where(s => s.Times.Contains(light)).ToList();
            return fitting.Count == 0 ? null : fitting[random.Next(fitting.Count)];
        }

        /// <summary>
        /// The street in <paramref name="folder"/>; null when its file is missing or broken, or a model is (logged). A
        /// mod's file is not trusted to be whole: a light or a lamp left empty is skipped.
        /// </summary>
        public static StreetScene? Load(string folder)
        {
            var file = Path.Combine(folder, "street.json");
            return SceneFiles.Load<SceneEntry, StreetScene>(file, "Streets", "street", Logger, entry => Build(folder, file, entry),
                (street, marks) => street with { Marks = marks });
        }

        private static StreetScene? Build(string folder, string file, SceneEntry? entry)
        {
            if (entry?.Player == null || entry.LaneOffset <= 0f || entry.ApproachFrom <= 0f || entry.LeaveTo <= 0f)
            {
                LeftOut($"{file} has no parking spot or no lane; left out");
                return null;
            }

            var lights = SceneLighting.Read(entry.Lights, entry.Id ?? Path.GetFileName(folder), Logger);
            var problems = new List<string>();
            var recipe = StreetRecipe.Read(entry.Layout, entry.Id ?? Path.GetFileName(folder), problems);
            foreach (var problem in problems) LeftOut(problem);
            var sun = entry.Sun is { Length: 3 } s ? s : [0.2f, 1f, 0.8f];
            var scene = new StreetScene(entry.Id ?? Path.GetFileName(folder), entry.Title ?? entry.Id ?? "The street", folder,
                new StreetSpot(entry.Player.X, entry.Player.Z, entry.Player.Heading), entry.LaneOffset, entry.RivalStop,
                entry.ApproachFrom, entry.LeaveTo, sun, lights)
            {
                Model = entry.Model,
                Glow = SceneLighting.MaterialGlows(entry.Glow),
                Credit = entry.Credit ?? string.Empty,
                ShownLooks = SceneLooks.Prefixes(entry.Looks),
                Recipe = recipe
            };

            // Used at the lights it names, each of which needs its model; else at every light it has a model for
            var times = new HashSet<StreetLight>();
            if (entry.Times == null)
            {
                times.UnionWith(Enum.GetValues<StreetLight>().Where(l => File.Exists(scene.Kn5For(l))));
            }
            else
            {
                foreach (var name in entry.Times)
                {
                    if (SceneLighting.TryParse(name, out var time)) times.Add(time);
                    else Logger.Warning("The street {Id} names \"{Time}\" among its times, which is no light of the day (day, dusk, night)", scene.Id, name);
                }

                var missing = times.Select(scene.Kn5For).FirstOrDefault(p => !File.Exists(p));
                if (missing != null)
                {
                    LeftOut($"the street {scene.Id} has no model {missing}; left out");
                    return null;
                }
            }

            if (times.Count == 0)
            {
                LeftOut($"the street {scene.Id} is used at no time of day; left out");
                return null;
            }

            return scene with { Times = times };
        }
    }
}
