using System.IO;
using System.Text.Json;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Services.CarLot;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Services.PartShops
{
    /// <summary>What a place in a shop takes: a whole engine, a tyre or a rim, or anything that fits</summary>
    public static class ShopHolds
    {
        public const string Engine = "engine";
        public const string Wheel = "wheel";
        public const string Large = "large";
        public const string Shelf = "shelf";
    }

    /// <summary>
    /// A place a part stands in a shop: the middle of its floor, its room (width along its own x, depth along its own z,
    /// height), the way it faces (degrees about the vertical, 0 faces +z) and what goes there (<see cref="ShopHolds"/>)
    /// </summary>
    public sealed record ShopPlace(float X, float Y, float Z, float Width, float Depth, float Height, float Heading, string Holds);

    /// <summary>
    /// The view the shop opens on: orbiting a point at (<paramref name="TargetX"/>, <paramref name="TargetHeight"/>,
    /// <paramref name="TargetZ"/>), through a lens <paramref name="Fov"/> degrees high (0: the renderer's own)
    /// </summary>
    public readonly record struct ShopCamera(float Alpha, float Beta, float Radius, float TargetX, float TargetHeight, float TargetZ, float Fov = 0f);

    /// <summary>The inside the camera stays in: the walls either side of the middle, the ceiling up</summary>
    public readonly record struct ShopRoom(float HalfWidth, float HalfDepth, float Height);

    /// <summary>What a kind of shop wears: the picture on each surface (its sign among them), and the colour of its tubes</summary>
    public sealed record ShopLook(string Id, IReadOnlyList<string> Kinds, IReadOnlyDictionary<string, string> Surfaces, byte[] LampColor);

    /// <summary>
    /// The inside of a part shop, as a mod's shopscenes/&lt;folder&gt;/shop.json describes it: its model, the view from
    /// the door and the walls the camera stays inside, the places the shop's stock stands in, its light at each hour, and
    /// the looks each kind of shop wears. The base game's is built by tools/ShopScene.
    /// </summary>
    public sealed record ShopScene(
        string Id,
        string Title,
        string Kn5,
        ShopCamera Camera,
        ShopRoom? Room,
        float[] Sun,
        IReadOnlyDictionary<StreetLight, StreetLighting> Lights,
        IReadOnlyDictionary<StreetLight, float> WindowGlow,
        IReadOnlyList<ShopPlace> Places,
        IReadOnlyList<ShopLook> Looks)
    {
        /// <summary>The self-lit windows, whose glow follows the hour</summary>
        public const string WindowMaterial = "sh_window";

        /// <summary>
        /// The look a shop wears: the one it names (<paramref name="lookId"/>), else the first for its kind
        /// (<paramref name="kind"/>, as the part shops name kinds), else the first; null when the scene has none
        /// </summary>
        public ShopLook? LookFor(string? lookId, string kind) =>
            Looks.FirstOrDefault(l => !string.IsNullOrEmpty(lookId) && l.Id.Equals(lookId, StringComparison.OrdinalIgnoreCase))
            ?? Looks.FirstOrDefault(l => l.Kinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
            ?? Looks.FirstOrDefault();

        /// <summary>The scene dressed for a shop at <paramref name="light"/>: its look's pictures and tubes, the windows' glow</summary>
        public WarehouseDressing Dress(ShopLook? look, StreetLight light)
        {
            var lighting = SceneLighting.For(Lights, light);
            if (look != null)
                lighting = lighting with { Lamps = lighting.Lamps.Select(lamp => lamp with { Color = look.LampColor }).ToList() };

            var glow = new Dictionary<string, float>(StringComparer.Ordinal);
            if (WindowGlow.TryGetValue(light, out var window)) glow[WindowMaterial] = window;

            return new WarehouseDressing(lighting, Sun, new Dictionary<string, string>(look?.Surfaces ?? new Dictionary<string, string>(), StringComparer.Ordinal), glow);
        }
    }

    public static class ShopScenes
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("ShopScene");

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        private sealed record CameraEntry(float Alpha, float Beta, float Radius, float TargetX, float TargetHeight, float TargetZ, float Fov);

        private sealed record RoomEntry(float HalfWidth, float HalfDepth, float Height);

        private sealed record PlaceEntry(float X, float Y, float Z, float Width, float Depth, float Height, float Heading, string? Holds);

        private sealed record LookEntry(string? Id, List<string?>? Kinds, Dictionary<string, string?>? Surfaces, int[]? LampColor);

        private sealed record SceneEntry(string? Id, string? Title, string? Model, CameraEntry? Camera, RoomEntry? Room, float[]? Sun,
            Dictionary<string, SceneLighting.LightEntry?>? Lights, Dictionary<string, float>? Window, List<SceneLighting.LampEntry?>? Lamps,
            List<PlaceEntry?>? Places, List<LookEntry?>? Looks);

        /// <summary>Every shop scene of every mod (a folder under a mod's <c>shopscenes</c>) that loads, by full id</summary>
        public static IReadOnlyList<ShopScene> LoadAll(ModSet mods) =>
            ModScenes.LoadAll(mods, ContentTypes.ShopScenes, "Shop scenes", Logger, Load, s => s.Id, (s, id) => s with { Id = id });

        private static readonly Lazy<IReadOnlyList<ShopScene>> InstalledScenes = new(() => LoadAll(ModSet.Installed));

        /// <summary>The shop scenes of the mods next to the game, read once; the last mod's first (a mod's own shop over the base game's)</summary>
        public static IReadOnlyList<ShopScene> Installed => InstalledScenes.Value;

        /// <summary>The scene the part shops are seen in: the last mod's, so a mod's shop takes the base game's place; null when there is none</summary>
        public static ShopScene? Current => Installed.Count == 0 ? null : Installed[^1];

        private static void LeftOut(string problem)
        {
            Logger.Warning("{Problem}", problem);
            ModProblems.Add("Shop scenes", problem);
        }

        /// <summary>
        /// The shop in <paramref name="folder"/>; null when its file or model is missing or broken (logged). A place that
        /// is not whole (no room in it, no number) is skipped; a picture name that is not a plain file name is dropped.
        /// </summary>
        public static ShopScene? Load(string folder)
        {
            var file = Path.Combine(folder, "shop.json");
            try
            {
                if (!File.Exists(file)) return null;
                var entry = JsonSerializer.Deserialize<SceneEntry>(File.ReadAllText(file), Options);
                return entry == null ? null : Build(folder, entry);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not read the shop scene {Path}", file);
                ModProblems.Add("Shop scenes", $"{file} could not be read ({ex.Message}); left out");
                return null;
            }
        }

        private static ShopScene? Build(string folder, SceneEntry entry)
        {
            var id = entry.Id ?? Path.GetFileName(folder);
            if (!PathNames.TryCombineUnder(folder, entry.Model ?? "shop.kn5", out var kn5))
            {
                LeftOut($"the shop scene {id} names a model outside its folder ({entry.Model}); left out");
                return null;
            }

            if (!File.Exists(kn5))
            {
                LeftOut($"the shop scene {id} has no model {kn5}; left out");
                return null;
            }

            var camera = entry.Camera is { } c && c.Radius > 0f
                ? new ShopCamera(c.Alpha, c.Beta, c.Radius, c.TargetX, c.TargetHeight, c.TargetZ, c.Fov is > 10f and < 120f ? c.Fov : 0f)
                : new ShopCamera(MathF.PI / 2f, 0.1f, 5f, 0f, 1.2f, 0f);
            var room = entry.Room is { HalfWidth: > 0f, HalfDepth: > 0f, Height: > 0f } r ? new ShopRoom(r.HalfWidth, r.HalfDepth, r.Height) : (ShopRoom?)null;

            // The tubes are lit at every hour, only scaled by it; a look colours them
            var lamps = (entry.Lamps ?? []).Select(l => l == null ? null : l with { Color = null }).ToList();
            var lights = SceneLighting.Read(entry.Lights, id, Logger, cubemapAmbient: 0f, sharedLamps: lamps);

            var window = new Dictionary<StreetLight, float>();
            foreach (var (name, glow) in entry.Window ?? [])
            {
                if (SceneLighting.TryParse(name, out var which)) window[which] = glow;
            }

            var places = (entry.Places ?? []).OfType<PlaceEntry>()
                .Where(p => p.Width > 0f && p.Depth > 0f && p.Height > 0f && float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && float.IsFinite(p.Heading))
                .Select(p => new ShopPlace(p.X, p.Y, p.Z, p.Width, p.Depth, p.Height, p.Heading, (p.Holds ?? ShopHolds.Shelf).Trim().ToLowerInvariant()))
                .ToList();

            var looks = (entry.Looks ?? []).OfType<LookEntry>()
                .Select((l, i) => new ShopLook(l.Id ?? $"look{i + 1}", (l.Kinds ?? []).OfType<string>().ToList(), Surfaces(id, l.Surfaces),
                    SceneLighting.Rgb(l.LampColor, 255)))
                .ToList();

            var sun = entry.Sun is { Length: 3 } s ? s : [0.2f, 0.8f, 0.6f];
            return new ShopScene(id, entry.Title ?? "The part shop", kn5, camera, room, sun, lights, window, places, looks);
        }

        private static Dictionary<string, string> Surfaces(string id, Dictionary<string, string?>? surfaces)
        {
            var kept = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (material, picture) in surfaces ?? [])
            {
                if (picture == null) continue;
                if (PathNames.IsSafeSegment(picture)) kept[material] = picture;
                else Logger.Warning("The shop scene {Id} dresses {Material} in \"{Picture}\", which is no picture name: it keeps its own", id, material, picture);
            }

            return kept;
        }
    }
}
