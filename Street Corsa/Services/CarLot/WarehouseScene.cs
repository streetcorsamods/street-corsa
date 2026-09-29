using System.IO;
using System.Text.Json;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Services.CarLot
{
    /// <summary>Where a car stands in a warehouse: metres from the middle of the floor, degrees about the vertical (0 faces +z)</summary>
    public readonly record struct WarehouseBay(float X, float Z, float Heading);

    /// <summary>The view the lot opens on: orbiting a point <paramref name="TargetHeight"/> over the middle of the floor</summary>
    public readonly record struct WarehouseCamera(float Alpha, float Beta, float Radius, float TargetHeight);

    /// <summary>
    /// The inside of the warehouse, which the camera stays in: the walls <paramref name="HalfLength"/> either side of
    /// the middle along x and <paramref name="HalfWidth"/> along z, the roof <paramref name="Height"/> up at its lowest
    /// </summary>
    public readonly record struct WarehouseRoom(float HalfLength, float HalfWidth, float Height);

    /// <summary>
    /// What a warehouse wears: the picture on each of the scene's surfaces, by material ("wh_wall" -> a picture in the
    /// model), and the colour its lamps give
    /// </summary>
    public sealed record WarehouseLook(string Id, IReadOnlyDictionary<string, string> Surfaces, byte[] LampColor);

    /// <summary>
    /// How the scene is dressed for one warehouse at one hour: the renderer's light (the lamps coloured by the look), which
    /// picture each material wears (the look's surfaces and the number on the sign), and how bright the self-lit ones
    /// glow. Everything the lot's viewport changes without loading the scene again.
    /// </summary>
    public sealed record WarehouseDressing(
        StreetLighting Lighting,
        float[] Sun,
        IReadOnlyDictionary<string, string> Pictures,
        IReadOnlyDictionary<string, float> Glow);

    /// <summary>
    /// A warehouse the player's cars are kept in, as a mod's warehouses/&lt;folder&gt;/warehouse.json describes it: its
    /// model, its bays, the view down it and the walls it stays inside, its light at each hour, and the looks one
    /// warehouse wears differently from the next. The base game's is built by tools/WarehouseScene.
    /// </summary>
    public sealed record WarehouseScene(
        string Id,
        string Title,
        string Kn5,
        IReadOnlyList<WarehouseBay> Bays,
        WarehouseCamera Camera,
        WarehouseRoom? Room,
        float[] Sun,
        IReadOnlyDictionary<StreetLight, StreetLighting> Lights,
        IReadOnlyDictionary<StreetLight, float> Skylight,
        IReadOnlyList<WarehouseLook> Looks,
        string? TensMaterial,
        string? UnitsMaterial,
        string? DigitPicture)
    {
        /// <summary>The self-lit material whose glow follows the hour</summary>
        public const string SkylightMaterial = "wh_skylight";

        /// <summary>How many cars it holds</summary>
        public int Capacity => Bays.Count;

        /// <summary>
        /// The scene dressed as warehouse <paramref name="number"/> (from 1) at <paramref name="light"/>: the look it
        /// wears in turn (<paramref name="round"/>, how many times the scenes have been gone through before it), its
        /// number on the sign
        /// </summary>
        public WarehouseDressing Dress(int number, int round, StreetLight light)
        {
            var look = Looks.Count == 0 ? null : Looks[round % Looks.Count];
            var lighting = Lights.TryGetValue(light, out var l) ? l : Lights.Values.FirstOrDefault() ?? StreetScenes.DefaultLighting;
            if (look != null)
                lighting = lighting with { Lamps = lighting.Lamps.Select(lamp => lamp with { Color = look.LampColor }).ToList() };

            var pictures = new Dictionary<string, string>(look?.Surfaces ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            if (DigitPicture != null)
            {
                // Two plates: 1 to 99, and past that the last two digits, which nobody with a hundred warehouses will mind
                var shown = number % 100;
                if (TensMaterial != null) pictures[TensMaterial] = string.Format(DigitPicture, shown / 10);
                if (UnitsMaterial != null) pictures[UnitsMaterial] = string.Format(DigitPicture, shown % 10);
            }

            var glow = new Dictionary<string, float>(StringComparer.Ordinal);
            if (Skylight.TryGetValue(light, out var sky)) glow[SkylightMaterial] = sky;

            return new WarehouseDressing(lighting, Sun, pictures, glow);
        }
    }

    public static class WarehouseScenes
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Warehouse");

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        private sealed record BayEntry(float X, float Z, float Heading);

        private sealed record CameraEntry(float Alpha, float Beta, float Radius, float TargetHeight);

        private sealed record RoomEntry(float HalfLength, float HalfWidth, float Height);

        private sealed record LampEntry(float X, float Y, float Z, float Brightness, float Range);

        private sealed record LightEntry(float Brightness, int[]? Color, float Ambient, int[]? AmbientUp, int[]? AmbientDown,
            float? CubemapAmbient, float? LampScale);

        private sealed record LookEntry(string? Id, Dictionary<string, string?>? Surfaces, int[]? LampColor);

        private sealed record DigitsEntry(string? Tens, string? Units, string? Picture);

        private sealed record SceneEntry(string? Id, string? Title, string? Model, List<BayEntry?>? Bays, CameraEntry? Camera,
            RoomEntry? Room, float[]? Sun, Dictionary<string, LightEntry?>? Lights, Dictionary<string, float>? Skylight,
            List<LampEntry?>? Lamps, List<LookEntry?>? Looks, DigitsEntry? Digits);

        /// <summary>
        /// Every warehouse of every mod (a folder under a mod's <c>warehouses</c>) that loads, by full id. Empty when
        /// there is none (logged): the lot is then a list, without a scene.
        /// </summary>
        public static IReadOnlyList<WarehouseScene> LoadAll(ModSet mods) =>
            ModScenes.LoadAll(mods, ContentTypes.Warehouses, "Warehouses", Logger, Load, s => s.Id, (s, id) => s with { Id = id });

        /// <summary>
        /// The warehouse in <paramref name="folder"/>; null when its file or model is missing or broken, or it has no bays
        /// (logged). A mod's file is not trusted to be whole: an empty entry in a list is skipped, and a sign picture
        /// that is no picture name for a digit is dropped.
        /// </summary>
        public static WarehouseScene? Load(string folder)
        {
            var file = Path.Combine(folder, "warehouse.json");
            try
            {
                if (!File.Exists(file)) return null;
                return Build(folder, JsonSerializer.Deserialize<SceneEntry>(File.ReadAllText(file), Options));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not read the warehouse {Path}", file);
                return null;
            }
        }

        private static WarehouseScene? Build(string folder, SceneEntry? entry)
        {
            if (entry == null) return null;

            var id = entry.Id ?? Path.GetFileName(folder);
            var bays = (entry.Bays ?? []).OfType<BayEntry>().Select(b => new WarehouseBay(b.X, b.Z, b.Heading)).ToList();
            if (bays.Count == 0)
            {
                Logger.Warning("The warehouse {Id} has no bays", id);
                return null;
            }

            var kn5 = Path.Combine(folder, entry.Model ?? "warehouse.kn5");
            if (!File.Exists(kn5))
            {
                Logger.Warning("The warehouse {Id} has no model {Path}", id, kn5);
                return null;
            }

            var camera = entry.Camera is { } c && c.Radius > 0f
                ? new WarehouseCamera(c.Alpha, c.Beta, c.Radius, c.TargetHeight)
                : new WarehouseCamera(0f, 0.2f, 12f, 1.5f);

            var room = entry.Room is { HalfLength: > 0f, HalfWidth: > 0f, Height: > 0f } r
                ? new WarehouseRoom(r.HalfLength, r.HalfWidth, r.Height)
                : (WarehouseRoom?)null;

            var lamps = (entry.Lamps ?? []).OfType<LampEntry>().ToList();
            var lights = new Dictionary<StreetLight, StreetLighting>();
            foreach (var (name, light) in entry.Lights ?? [])
            {
                if (light == null) continue;
                if (!Enum.TryParse<StreetLight>(name, ignoreCase: true, out var which))
                {
                    Logger.Warning("The warehouse {Id} has a light \"{Light}\", which is no light of the day (day, dusk, night)", id, name);
                    continue;
                }

                var scale = light.LampScale ?? 1f;
                lights[which] = new StreetLighting(light.Brightness, Rgb(light.Color, 200), light.Ambient,
                    Rgb(light.AmbientUp, 150), Rgb(light.AmbientDown, 150), light.CubemapAmbient ?? 0f,
                    lamps.Select(l => new StreetLamp(l.X, l.Y, l.Z, Rgb(null, 255), l.Brightness * scale, l.Range)).ToList());
            }

            var skylight = new Dictionary<StreetLight, float>();
            foreach (var (name, glow) in entry.Skylight ?? [])
            {
                if (Enum.TryParse<StreetLight>(name, ignoreCase: true, out var which)) skylight[which] = glow;
            }

            var looks = (entry.Looks ?? []).OfType<LookEntry>()
                .Select((l, i) => new WarehouseLook(l.Id ?? $"look{i + 1}",
                    (l.Surfaces ?? []).Where(p => p.Value != null).ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal),
                    Rgb(l.LampColor, 255)))
                .ToList();

            var digitPicture = entry.Digits?.Picture;
            if (digitPicture != null && !IsDigitPicture(digitPicture))
            {
                Logger.Warning("The warehouse {Id} has a sign picture \"{Picture}\" that takes no digit: the sign keeps its number", id, digitPicture);
                digitPicture = null;
            }

            var sun = entry.Sun is { Length: 3 } s ? s : [0.25f, 1f, 0.35f];
            return new WarehouseScene(id, entry.Title ?? "The warehouse", kn5, bays, camera, room, sun, lights, skylight, looks,
                entry.Digits?.Tens, entry.Digits?.Units, digitPicture);
        }

        /// <summary>A picture name with the digit in it as <c>{0}</c>, and nothing else string.Format would trip on</summary>
        private static bool IsDigitPicture(string picture)
        {
            try
            {
                return string.Format(picture, 0) != string.Format(picture, 1);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // JSON numbers, not the base64 a byte[] is read from
        private static byte[] Rgb(int[]? color, byte fallback) =>
            color is { Length: 3 } ? color.Select(c => (byte)Math.Clamp(c, 0, 255)).ToArray() : [fallback, fallback, fallback];
    }
}
