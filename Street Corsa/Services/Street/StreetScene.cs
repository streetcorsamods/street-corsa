using System.IO;
using System.Text.Json;
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
        private const double MeanSunset = 18.3;
        private const double SunsetSwing = 1.9;
        private const int Midsummer = 172;

        public static double SunsetHour(DateTime date) =>
            MeanSunset + SunsetSwing * Math.Cos(2 * Math.PI * (date.DayOfYear - Midsummer) / 365.0);

        public static StreetLight At(DateTime time)
        {
            var hour = time.TimeOfDay.TotalHours;
            var sunset = SunsetHour(time);
            if (hour >= sunset + 0.5 || hour < 5.5) return StreetLight.Night;
            if (hour >= sunset - 1.5 || hour < 7) return StreetLight.Dusk;
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

        /// <summary>The model of the street in that light: street_day.kn5 and so on</summary>
        public string Kn5For(StreetLight light) => Path.Combine(Folder, $"street_{StreetLights.Describe(light)}.kn5");

        /// <summary>The light of the day as the renderer gets it; the day's when the file leaves one out</summary>
        public StreetLighting LightingFor(StreetLight light) =>
            Lights.TryGetValue(light, out var lighting) ? lighting
            : Lights.TryGetValue(StreetLight.Day, out var day) ? day
            : StreetScenes.DefaultLighting;
    }

    public static class StreetScenes
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Street");

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        internal static readonly StreetLighting DefaultLighting = new(1.5f, [200, 180, 180], 2f, [150, 180, 180], [150, 180, 180], 0.5f, []);


        private sealed record SpotEntry(float X, float Z, float Heading);

        private sealed record LampEntry(float X, float Y, float Z, int[]? Color, float Brightness, float Range);

        private sealed record LightEntry(float Brightness, int[]? Color, float Ambient, int[]? AmbientUp, int[]? AmbientDown,
            float? CubemapAmbient, List<LampEntry>? Lamps);

        private sealed record SceneEntry(string? Id, string? Title, SpotEntry? Player, float LaneOffset, float RivalStop,
            float ApproachFrom, float LeaveTo, float[]? Sun, Dictionary<string, LightEntry>? Lights, List<string>? Times);

        /// <summary>
        /// Every street of every mod (a folder under a mod's <c>streets</c>) that loads, by full id: a mod's streets get
        /// its namespace. Empty when there is none (logged).
        /// </summary>
        public static IReadOnlyList<StreetScene> LoadAll(ModSet mods)
        {
            var streets = new List<StreetScene>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (mod, folder) in mods.SubFolders(ContentTypes.Streets))
            {
                var scene = Load(folder);
                if (scene == null) continue;

                // A street's own id is written without its mod, like every id in a mod's files
                var id = ModIds.IsQualified(scene.Id) ? null : ModIds.Qualify(mod.Id, scene.Id);
                if (id == null || !ids.Add(id))
                {
                    Logger.Warning("Streets: {Mod}/{Folder} has the id {Id}, which is taken or has a ':' in it; left out", mod.Id, Path.GetFileName(folder), scene.Id);
                    continue;
                }

                streets.Add(scene with { Id = id });
            }

            if (streets.Count == 0) Logger.Warning("No street in the mods could be loaded");
            return streets;
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

        /// <summary>The street in <paramref name="folder"/>; null when its file is missing or broken, or a model is (logged)</summary>
        public static StreetScene? Load(string folder)
        {
            var file = Path.Combine(folder, "street.json");
            SceneEntry? entry;
            try
            {
                if (!File.Exists(file)) return null;
                entry = JsonSerializer.Deserialize<SceneEntry>(File.ReadAllText(file), Options);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not read the street {Path}", file);
                return null;
            }

            if (entry?.Player == null || entry.LaneOffset <= 0f || entry.ApproachFrom <= 0f || entry.LeaveTo <= 0f)
            {
                Logger.Warning("The street {Path} has no parking spot or no lane", file);
                return null;
            }

            var lights = new Dictionary<StreetLight, StreetLighting>();
            foreach (var (name, light) in entry.Lights ?? [])
            {
                if (!Enum.TryParse<StreetLight>(name, ignoreCase: true, out var which)) continue;
                lights[which] = new StreetLighting(light.Brightness, Rgb(light.Color, 200), light.Ambient,
                    Rgb(light.AmbientUp, 150), Rgb(light.AmbientDown, 150), light.CubemapAmbient ?? 0.5f,
                    (light.Lamps ?? []).Select(l => new StreetLamp(l.X, l.Y, l.Z, Rgb(l.Color, 255), l.Brightness, l.Range)).ToList());
            }

            var sun = entry.Sun is { Length: 3 } s ? s : [0.2f, 1f, 0.8f];
            var scene = new StreetScene(entry.Id ?? Path.GetFileName(folder), entry.Title ?? entry.Id ?? "The street", folder,
                new StreetSpot(entry.Player.X, entry.Player.Z, entry.Player.Heading), entry.LaneOffset, entry.RivalStop,
                entry.ApproachFrom, entry.LeaveTo, sun, lights);

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
                    if (Enum.TryParse<StreetLight>(name, ignoreCase: true, out var time)) times.Add(time);
                    else Logger.Warning("The street {Id} names \"{Time}\" among its times, which is no light of the day (day, dusk, night)", scene.Id, name);
                }

                var missing = times.Select(scene.Kn5For).FirstOrDefault(p => !File.Exists(p));
                if (missing != null)
                {
                    Logger.Warning("The street {Id} has no model {Path}", scene.Id, missing);
                    return null;
                }
            }

            if (times.Count == 0)
            {
                Logger.Warning("The street {Id} is used at no time of day", scene.Id);
                return null;
            }

            return scene with { Times = times };
        }

        // JSON numbers, not the base64 a byte[] is read from
        private static byte[] Rgb(int[]? color, byte fallback) =>
            color is { Length: 3 } ? color.Select(c => (byte)Math.Clamp(c, 0, 255)).ToArray() : [fallback, fallback, fallback];
    }
}
