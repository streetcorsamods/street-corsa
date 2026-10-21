using System.IO;
using System.Numerics;
using System.Text.Json;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// A piece a street is laid out with (a shop, a house on its lot, a palm, a lamp post, a car at the curb): its model,
    /// stood on its base with the middle of its footprint at its origin, facing +z, and what a street's recipe picks it by
    /// </summary>
    /// <param name="Id">Its folder's name under a mod's streetkit</param>
    /// <param name="Model">Its KN5, in its folder</param>
    /// <param name="Width">Its footprint across (x), metres, as made: a placement's scale scales it</param>
    /// <param name="Depth">Its footprint front to back (z), metres</param>
    /// <param name="Tags">What a recipe asks for (shop, house, curb_car, palm, lamp...)</param>
    /// <param name="Weight">How often it is picked against the other pieces of a tag, 1 the usual</param>
    public sealed record StreetPiece(string Id, string Model, float Width, float Depth, float Height, IReadOnlySet<string> Tags, float Weight)
    {
        /// <summary>Where a lamp post's light is lit at night, in the piece's own space (over the road); null for no lamp</summary>
        public Vector3? Lamp { get; init; }

        /// <summary>Who made it (piece.json's <c>credit</c>)</summary>
        public string Credit { get; init; } = string.Empty;
    }

    /// <summary>
    /// The pieces of every mod's streetkit (step 39): a folder a piece, its <c>piece.json</c> and its model. A later mod's
    /// piece of an id takes the earlier one's place. A piece whose file does not read, or whose model is missing, is left out
    /// and told (the mods' problems).
    /// </summary>
    public sealed class StreetKit
    {
        public const string FileName = "piece.json";

        /// <summary>The least a piece's footprint and height may be, metres: a sliver would fill a row's end by the thousand</summary>
        public const float MinSize = 0.05f;

        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("StreetKit");

        private sealed record PieceJson(float[]? Footprint, float Height, List<string?>? Tags, float? Weight, float[]? Lamp, string? Model, string? Credit);

        private readonly Dictionary<string, StreetPiece> _pieces;

        private StreetKit(Dictionary<string, StreetPiece> pieces) => _pieces = pieces;

        /// <summary>A kit of the pieces given (a test's)</summary>
        public static StreetKit From(IEnumerable<StreetPiece> pieces) =>
            new(pieces.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase));

        private static readonly Lazy<StreetKit> InstalledKit = new(() =>
        {
            var problems = new List<string>();
            var kit = Load(ModSet.Installed, problems);
            foreach (var problem in problems) Logger.Warning("{Problem}", problem);
            ModProblems.Add("Street kit", problems);
            return kit;
        });

        /// <summary>The pieces of the mods next to the game, read once</summary>
        public static StreetKit Installed => InstalledKit.Value;

        public static StreetKit Load(ModSet mods, List<string> problems)
        {
            var pieces = new Dictionary<string, StreetPiece>(StringComparer.OrdinalIgnoreCase);
            foreach (var (mod, dir) in mods.SubFolders(ContentTypes.StreetKit))
            {
                var id = Path.GetFileName(dir);
                var file = Path.Combine(dir, FileName);
                if (!File.Exists(file)) continue;

                var source = $"{mod.Id}/{ContentTypes.StreetKit}/{id}";
                try
                {
                    var json = JsonSerializer.Deserialize<PieceJson>(File.ReadAllText(file), ModJson.Options);
                    if (json?.Footprint is not { Length: 2 } footprint || !footprint.All(f => float.IsFinite(f) && f >= MinSize)
                        || !float.IsFinite(json.Height) || json.Height < MinSize)
                    {
                        problems.Add($"{source}: no footprint [width, depth] and height in metres (each {MinSize} or more); left out");
                        continue;
                    }

                    var weight = json.Weight ?? 1f;
                    if (!float.IsFinite(weight) || weight < 0f)
                    {
                        problems.Add($"{source}: a weight of {weight} is none; left out");
                        continue;
                    }

                    if (!Garage.GarageProps.TryModel(mod.Id, dir, id, json.Model, problems, out var model, ContentTypes.StreetKit)) continue;

                    var tags = (json.Tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    pieces[id] = new StreetPiece(id, model, footprint[0], footprint[1], json.Height, tags, weight)
                    {
                        Lamp = json.Lamp is { Length: 3 } lamp && lamp.All(float.IsFinite) ? new Vector3(lamp[0], lamp[1], lamp[2]) : null,
                        Credit = json.Credit ?? string.Empty
                    };
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{source}/{FileName} could not be read: {ex.Message}");
                }
            }

            return new StreetKit(pieces);
        }

        public int Count => _pieces.Count;

        public IEnumerable<StreetPiece> All => _pieces.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase);

        /// <summary>The piece of an id; null when no mod has it</summary>
        public StreetPiece? Find(string id) => _pieces.GetValueOrDefault(id);

        /// <summary>The pieces with any of the tags, in the order of their ids (a layout's picks follow it)</summary>
        public IReadOnlyList<StreetPiece> Tagged(IEnumerable<string> tags)
        {
            var wanted = tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return All.Where(p => p.Weight > 0f && p.Tags.Overlaps(wanted)).ToList();
        }

        /// <summary>Whether any piece is of the id or has the tag: a recipe's word that is neither takes nothing</summary>
        public bool Knows(string word) => Find(word) != null || _pieces.Values.Any(p => p.Tags.Contains(word));

        /// <summary>Who made the pieces: each credit once, in the order of the pieces' ids</summary>
        public IReadOnlyList<string> Credits => All.Select(p => p.Credit).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>
        /// The credits whose licences ask their makers named: all but those whose every part (a credit names its models
        /// one after another, split by ';') is CC0, however a licence is spelled
        /// </summary>
        public IReadOnlyList<string> NamedCredits => Credits
            .Where(c => !c.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .All(part => part.Contains("CC0", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
