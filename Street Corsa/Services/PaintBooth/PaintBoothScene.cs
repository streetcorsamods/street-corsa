using System.IO;
using System.Text.Json;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Services.Station;
using StreetCorsa.Services.Street;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Services.PaintBooth
{
    /// <summary>
    /// A paint booth, as a mod's booths/&lt;folder&gt;/booth.json describes it: its model and its painter's, the way the car
    /// rolls in through its door to where it is painted, how the painter walks round the car (<see cref="Round"/>), where
    /// the camera stands, and its lights. The booth is indoors: it is lit the same at every hour unless the file says
    /// otherwise.
    /// </summary>
    public sealed record PaintBoothScene(
        string Id,
        string Title,
        string Folder,
        string Model,
        string Painter,
        IReadOnlyList<GroundPoint> Arrive,
        BoothRound Round,
        BoothCamera Camera,
        float[] Sun,
        IReadOnlyDictionary<StreetLight, StreetLighting> Lights) : IDriveInScene
    {
        /// <summary>The booth's model, a KN5 in its folder; empty when the file names one outside it</summary>
        public string Kn5 => PathNames.TryCombineUnder(Folder, Model, out var kn5) ? kn5 : string.Empty;

        public bool HasModel => Kn5.Length > 0 && File.Exists(Kn5);

        /// <summary>The painter's model, a KN5 in its folder (a node ARM, his arm, holding MIST, the spray); empty without one</summary>
        public string PainterKn5 => PathNames.TryCombineUnder(Folder, Painter, out var kn5) && File.Exists(kn5) ? kn5 : string.Empty;

        /// <summary>The ground's height file next to the model; a booth without one has a flat floor</summary>
        public string GroundFile => Path.Combine(Folder, "ground.bin");

        // Indoors: no sky
        public const string SkyPrefix = "BOOTH_SKY_";

        string IDriveInScene.SkyNodeAt(StreetLight light) => SkyPrefix + StreetLights.Describe(light).ToUpperInvariant();

        string IDriveInScene.SkyPrefix => SkyPrefix;

        /// <summary>The car is painted at the end of the way in</summary>
        public string StandMark => "paint";

        float IDriveInScene.LookWhileStanding => 0f;

        float IDriveInScene.LookUpWhileStanding => 0f;

        IReadOnlyDictionary<string, float> IDriveInScene.GlowAt(StreetLight light) => new Dictionary<string, float>();

        public StreetLighting LightingFor(StreetLight light) => SceneLighting.For(Lights, light);

        /// <summary>The scene's own marks and paths for the scripts</summary>
        public Scripting.SceneMarks Marks { get; init; } = Scripting.SceneMarks.None;

        /// <summary>The colours of the day the player is offered to start from (<c>colours</c>); none without them, they mix their own</summary>
        public IReadOnlyList<BoothColour> Colours { get; init; } = [];

        /// <summary>
        /// What the scripts drive along and to: the file's own, the way in (<c>arrive</c>) and where the car is painted at
        /// its end (<c>paint</c>) unless the file names its own
        /// </summary>
        public Scripting.SceneMarks ScriptMarks
        {
            get
            {
                var marks = Marks.With(paths: new Dictionary<string, IReadOnlyList<GroundPoint>> { ["arrive"] = Arrive });
                var end = marks.Paths["arrive"][^1];
                return marks.With(new Dictionary<string, Scripting.MarkSpec> { ["paint"] = new(X: end.X, Z: end.Z) });
            }
        }
    }

    /// <summary>A colour the booth offers: its name (a text pool key, or the name itself) and the colour</summary>
    public sealed record BoothColour(string Name, StreetCorsa.Parts.Cars.PaintColour Colour);

    /// <summary>
    /// How the painter goes round the car: <see cref="Gap"/> metres off its sides, nose and tail, his gun swinging from
    /// <see cref="Low"/> to <see cref="High"/> degrees (down negative), <see cref="Strokes"/> strokes up and down a side;
    /// he waits at <see cref="Wait"/> while there is nothing to paint
    /// </summary>
    public sealed record BoothRound(float Gap, float Low, float High, int Strokes, GroundPoint Wait)
    {
        public static readonly BoothRound Default = new(0.75f, -32f, 6f, 5, new GroundPoint(2.25f, 2.85f));
    }

    /// <summary>
    /// Where the camera stands: <see cref="Distance"/> metres from the car's middle, its eye <see cref="Height"/> over the
    /// floor, never outside the box <see cref="Min"/>..<see cref="Max"/> (inside the walls), however far it is wheeled back
    /// </summary>
    public sealed record BoothCamera(float Distance, float Height, GroundPoint Min, GroundPoint Max)
    {
        public static readonly BoothCamera Default = new(4.4f, 1.5f, new GroundPoint(-2.55f, -4.35f), new GroundPoint(2.55f, 4.4f));
    }

    public static class PaintBooths
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("PaintBooth");

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        private sealed record RoundEntry(float? Gap, float? Low, float? High, int? Strokes, float[]? Wait);

        private sealed record CameraEntry(float? Distance, float? Height, float[]? Min, float[]? Max);

        private sealed record ColourEntry(string? Name, string? Hex);

        private sealed record BoothEntry(string? Id, string? Title, string? Model, string? Painter, List<float[]>? Arrive, RoundEntry? Round,
            CameraEntry? Camera, float[]? Sun, Dictionary<string, SceneLighting.LightEntry?>? Lights, List<ColourEntry?>? Colours);

        /// <summary>Every paint booth of every mod that loads, by full id; empty when none does</summary>
        public static IReadOnlyList<PaintBoothScene> LoadAll(ModSet mods) =>
            ModScenes.LoadAll(mods, ContentTypes.Booths, "Paint booths", Logger, Load, s => s.Id, (s, id) => s with { Id = id });

        private static readonly Lazy<IReadOnlyList<PaintBoothScene>> InstalledBooths = new(() => LoadAll(ModSet.Installed));

        /// <summary>The paint booths of the mods next to the game, read once</summary>
        public static IReadOnlyList<PaintBoothScene> Installed => InstalledBooths.Value;

        /// <summary>The booth in <paramref name="folder"/>; null when its file is missing or broken (logged)</summary>
        public static PaintBoothScene? Load(string folder)
        {
            var file = Path.Combine(folder, "booth.json");
            try
            {
                if (!File.Exists(file)) return null;
                using var document = Scripting.SceneMarks.Parse(file);
                if (Build(folder, file, document.RootElement.Deserialize<BoothEntry>(Options)) is not { } booth) return null;
                var problems = new List<string>();
                booth = booth with { Marks = Scripting.SceneMarks.Read(document.RootElement, file, problems) };
                ModProblems.Add("Paint booths", problems);
                return booth;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not read the paint booth {Path}", file);
                ModProblems.Add("Paint booths", $"{file} could not be read ({ex.Message}); left out");
                return null;
            }
        }

        private static PaintBoothScene? Build(string folder, string file, BoothEntry? entry)
        {
            var arrive = ScenePaths.Points(entry?.Arrive);
            if (entry == null || !ScenePaths.GoesSomewhere(arrive))
            {
                Logger.Warning("The paint booth {Path} has no way in (two points at least, a metre apart)", file);
                ModProblems.Add("Paint booths", $"{file} has no way in (two points at least, a metre apart); left out");
                return null;
            }

            var round = BoothRound.Default;
            if (entry.Round is { } r)
                round = new BoothRound(Math.Clamp(r.Gap ?? round.Gap, 0.3f, 3f), Math.Clamp(r.Low ?? round.Low, -80f, 30f),
                    Math.Clamp(r.High ?? round.High, -60f, 60f), Math.Clamp(r.Strokes ?? round.Strokes, 1, 20), Point(r.Wait) ?? round.Wait);
            var camera = BoothCamera.Default;
            if (entry.Camera is { } c)
            {
                var (min, max) = (Point(c.Min) ?? camera.Min, Point(c.Max) ?? camera.Max);
                if (min.X >= max.X || min.Z >= max.Z) (min, max) = (camera.Min, camera.Max);
                camera = new BoothCamera(Math.Clamp(c.Distance ?? camera.Distance, 1.5f, 30f), Math.Clamp(c.Height ?? camera.Height, 0.3f, 10f), min, max);
            }

            var id = entry.Id ?? Path.GetFileName(folder);
            var booth = new PaintBoothScene(id, entry.Title ?? id, folder, entry.Model ?? "booth.kn5", entry.Painter ?? "painter.kn5", arrive, round, camera,
                entry.Sun is { Length: 3 } sun ? sun : [0.15f, 1f, 0.25f], SceneLighting.Read(entry.Lights, id, Logger));
            var colours = new List<BoothColour>();
            foreach (var colour in entry.Colours ?? [])
            {
                if (colour?.Name is { Length: > 0 } name && StreetCorsa.Parts.Cars.PaintColour.Parse(colour.Hex) is { } paint) colours.Add(new BoothColour(name, paint));
                else ModProblems.Add("Paint booths", $"{file}: a colour needs a name and a hex (#RRGGBB); {colour?.Name ?? "one"} left out");
            }
            booth = booth with { Colours = colours };
            if (booth.Kn5.Length == 0)
                Logger.Warning("The paint booth {Id} names a model outside its folder ({Model}): it is shown without its picture", booth.Id, booth.Model);
            else if (!booth.HasModel)
                Logger.Warning("The paint booth {Id} has no model {Path}: it is shown without its picture", booth.Id, booth.Kn5);
            return booth;
        }

        private static GroundPoint? Point(float[]? xz) =>
            xz is { Length: 2 } && float.IsFinite(xz[0]) && float.IsFinite(xz[1]) ? new GroundPoint(xz[0], xz[1]) : null;
    }
}
