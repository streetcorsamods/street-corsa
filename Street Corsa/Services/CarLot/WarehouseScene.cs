using System.IO;
using StreetCorsa.Helpers;
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

    /// <summary>The way out, a point on the floor a car taken out of its bay drives for: metres from the middle of the floor</summary>
    public readonly record struct WarehouseExit(float X, float Z);

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
    /// warehouse wears differently from the next.
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
        string? DigitPicture,
        WarehouseExit? Exit = null)
    {
        /// <summary>The scene's own marks and paths for the scripts (<see cref="Scripting.SceneMarks"/>)</summary>
        public Scripting.SceneMarks Marks { get; init; } = Scripting.SceneMarks.None;

        /// <summary>
        /// What the scripts drive along and to: the file's own, and the way out (<c>exit</c>: the file's <c>exit</c>, else
        /// far ahead of the car, which drives straight on out of its bay) unless it names its own
        /// </summary>
        public Scripting.SceneMarks ScriptMarks => Marks.With(new Dictionary<string, Scripting.MarkSpec>
        {
            ["exit"] = Exit is { } exit ? new(X: exit.X, Z: exit.Z) : new(Ahead: StraightOn)
        });

        // A way out that far ahead of a car is never turned for: it drives straight on
        private const float StraightOn = 100f;

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
            var pictures = new Dictionary<string, string>(look?.Surfaces ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            if (DigitPicture != null)
            {
                // Two plates: 1 to 99, and past that the last two digits, which nobody with a hundred warehouses will mind
                var shown = number % 100;
                if (TensMaterial != null) pictures[TensMaterial] = string.Format(DigitPicture, shown / 10);
                if (UnitsMaterial != null) pictures[UnitsMaterial] = string.Format(DigitPicture, shown % 10);
            }

            return SceneDressing.Of(Lights, light, look?.LampColor, pictures, Sun, SkylightMaterial, Skylight);
        }
    }

    public static class WarehouseScenes
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Warehouse");

        /// <summary>The farthest the lot's camera stands from what it looks at, and the highest that is off the floor, metres</summary>
        public const float MostCameraRadius = 200f;

        /// <summary>How far the lot's camera looks up or down, radians: short of straight up or down</summary>
        public const float MostCameraBeta = 1.5f;

        private sealed record BayEntry(float X, float Z, float Heading);

        private sealed record CameraEntry(float Alpha, float Beta, float Radius, float TargetHeight);

        private sealed record RoomEntry(float HalfLength, float HalfWidth, float Height);

        private sealed record ExitEntry(float X, float Z);

        private sealed record LookEntry(string? Id, Dictionary<string, string?>? Surfaces, int[]? LampColor);

        private sealed record DigitsEntry(string? Tens, string? Units, string? Picture);

        private sealed record SceneEntry(string? Id, string? Title, string? Model, List<BayEntry?>? Bays, CameraEntry? Camera,
            RoomEntry? Room, float[]? Sun, Dictionary<string, SceneLighting.LightEntry?>? Lights, Dictionary<string, float>? Skylight,
            List<SceneLighting.LampEntry?>? Lamps, List<LookEntry?>? Looks, DigitsEntry? Digits, ExitEntry? Exit = null);

        /// <summary>
        /// Every warehouse of every mod (a folder under a mod's <c>warehouses</c>) that loads, by full id. Empty when
        /// there is none (logged): the lot is then a list, without a scene.
        /// </summary>
        public static IReadOnlyList<WarehouseScene> LoadAll(ModSet mods) =>
            ModScenes.LoadAll(mods, ContentTypes.Warehouses, "Warehouses", Logger, Load, s => s.Id, (s, id) => s with { Id = id });

        private static readonly ContentCache<IReadOnlyList<WarehouseScene>> InstalledScenes = new(() => LoadAll(ModSet.Installed));

        /// <summary>The warehouses of the mods next to the game, read once</summary>
        public static IReadOnlyList<WarehouseScene> Installed => InstalledScenes.Value;

        /// <summary>
        /// The warehouse in <paramref name="folder"/>; null when its file or model is missing or broken, or it has no bays
        /// (logged). A mod's file is not trusted to be whole: an empty entry in a list is skipped, a sign picture that is
        /// no picture name for a digit is dropped, and so is a picture name that is not a plain file name. A model outside
        /// the folder is not read.
        /// </summary>
        public static WarehouseScene? Load(string folder)
        {
            var file = Path.Combine(folder, "warehouse.json");
            return SceneFiles.Load<SceneEntry, WarehouseScene>(file, "Warehouses", "warehouse", Logger, entry => Build(folder, entry),
                (warehouse, marks) => warehouse with { Marks = marks });
        }

        private static WarehouseScene? Build(string folder, SceneEntry? entry)
        {
            if (entry == null) return null;

            var id = entry.Id ?? Path.GetFileName(folder);
            // A bay off the floor's scale (a mod's 1e30) is no place to stand a car
            var bays = (entry.Bays ?? []).OfType<BayEntry>()
                .Where(b => ScenePaths.Within(b.X) && ScenePaths.Within(b.Z) && float.IsFinite(b.Heading))
                .Select(b => new WarehouseBay(b.X, b.Z, b.Heading)).ToList();
            if (bays.Count == 0)
            {
                SceneFiles.LeftOut(Logger, "Warehouses", $"the warehouse {id} has no bays; left out");
                return null;
            }

            if (!SceneFiles.TryModel(folder, entry.Model, "warehouse.kn5", $"the warehouse {id}", Logger, "Warehouses", out var kn5)) return null;

            // The camera's figures held to a view of the floor: any turn about it, short of looking straight up or down, no
            // farther off than the camera can see
            var camera = entry.Camera is { } c && c.Radius > 0f && float.IsFinite(c.Radius) && float.IsFinite(c.Alpha) && float.IsFinite(c.Beta)
                         && float.IsFinite(c.TargetHeight)
                ? new WarehouseCamera(MathF.Abs(c.Alpha) <= 2f * MathF.PI ? c.Alpha : MathF.IEEERemainder(c.Alpha, 2f * MathF.PI), Math.Clamp(c.Beta, -MostCameraBeta, MostCameraBeta),
                    Math.Min(c.Radius, MostCameraRadius), Math.Clamp(c.TargetHeight, -MostCameraRadius, MostCameraRadius))
                : new WarehouseCamera(0f, 0.2f, 12f, 1.5f);

            var room = entry.Room is { HalfLength: > 0f, HalfWidth: > 0f, Height: > 0f } r
                       && ScenePaths.Within(r.HalfLength) && ScenePaths.Within(r.HalfWidth) && ScenePaths.Within(r.Height)
                ? new WarehouseRoom(r.HalfLength, r.HalfWidth, r.Height)
                : (WarehouseRoom?)null;

            // A warehouse's lamps are white and the same at every hour, only scaled by it (a look colours them)
            var lights = SceneFiles.Lights(entry.Lights, entry.Lamps, id, Logger);
            var skylight = SceneFiles.Glow(entry.Skylight);

            var looks = (entry.Looks ?? []).OfType<LookEntry>()
                .Select((l, i) => new WarehouseLook(l.Id ?? $"look{i + 1}", SceneFiles.Surfaces(l.Surfaces, "warehouse", id, Logger),
                    SceneLighting.Rgb(l.LampColor, 255)))
                .ToList();

            var digitPicture = entry.Digits?.Picture;
            if (digitPicture != null && !IsDigitPicture(digitPicture))
            {
                Logger.Warning("The warehouse {Id} has a sign picture \"{Picture}\" that takes no digit: the sign keeps its number", id, digitPicture);
                digitPicture = null;
            }

            var sun = entry.Sun is { Length: 3 } s ? s : [0.25f, 1f, 0.35f];
            return new WarehouseScene(id, entry.Title ?? "The warehouse", kn5, bays, camera, room, sun, lights, skylight, looks,
                entry.Digits?.Tens, entry.Digits?.Units, digitPicture,
                entry.Exit is { } exit && ScenePaths.Within(exit.X) && ScenePaths.Within(exit.Z) ? new WarehouseExit(exit.X, exit.Z) : null);
        }

        /// <summary>
        /// A picture name with the digit in it as <c>{0}</c>, and nothing else string.Format would trip on; every digit's
        /// name a plain file name (<see cref="IsPictureName"/>)
        /// </summary>
        private static bool IsDigitPicture(string picture)
        {
            try
            {
                return string.Format(picture, 0) != string.Format(picture, 1)
                       && Enumerable.Range(0, 10).All(digit => IsPictureName(string.Format(picture, digit)));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>A texture's name in the model: one plain file name, no folder in it</summary>
        private static bool IsPictureName(string picture) => PathNames.IsSafeSegment(picture);
    }
}
