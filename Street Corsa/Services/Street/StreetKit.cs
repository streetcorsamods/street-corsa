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

        /// <summary>How it may be painted where it stands (a shop's stucco, a house's siding); null: always as made</summary>
        public PiecePaint? Paint { get; init; }
    }

    /// <summary>
    /// The colours a piece may be painted in (piece.json's <c>paint</c>): its materials whose names match
    /// <see cref="Materials"/> take one of <see cref="Colours"/>, drawn for each place it stands in, or stay as made. The
    /// picture keeps its light and shade, its middle tone brought to the colour by <see cref="Amount"/> (0 to 1).
    /// </summary>
    public sealed record PiecePaint(IReadOnlyList<System.Text.RegularExpressions.Regex> Materials, IReadOnlyList<byte[]> Colours, float Amount)
    {
        /// <summary>Whether a material of the piece takes the paint</summary>
        public bool Takes(string material) => Materials.Any(m => m.IsMatch(material));
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

        private sealed record PaintJson(List<string?>? Materials, List<int[]?>? Colours, float? Amount);

        private sealed record PieceJson(float[]? Footprint, float Height, List<string?>? Tags, float? Weight, float[]? Lamp, string? Model, string? Credit,
            PaintJson? Paint);

        private readonly Dictionary<string, StreetPiece> _pieces;

        private StreetKit(Dictionary<string, StreetPiece> pieces) => _pieces = pieces;

        /// <summary>A kit of the pieces given (a test's)</summary>
        public static StreetKit From(IEnumerable<StreetPiece> pieces) =>
            new(pieces.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase));

        private static readonly ContentCache<StreetKit> InstalledKit = new(() =>
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
                        Credit = json.Credit ?? string.Empty,
                        Paint = PaintOf(json.Paint, source, problems)
                    };
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{source}/{FileName} could not be read: {ex.Message}");
                }
            }

            return new StreetKit(pieces);
        }

        /// <summary>A piece's paint as its file gives it; null, and told, when it names no material or colour that is one</summary>
        private static PiecePaint? PaintOf(PaintJson? paint, string source, List<string> problems)
        {
            if (paint == null) return null;

            var materials = new List<System.Text.RegularExpressions.Regex>();
            foreach (var pattern in (paint.Materials ?? []).Where(m => !string.IsNullOrWhiteSpace(m)))
            {
                try
                {
                    materials.Add(new System.Text.RegularExpressions.Regex(pattern!, System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
                }
                catch (ArgumentException ex)
                {
                    problems.Add($"{source}: the paint's material \"{pattern}\" is no pattern ({ex.Message}); left out");
                }
            }

            var colours = (paint.Colours ?? []).Where(c => c is { Length: 3 }).Select(c => c!.Select(v => (byte)Math.Clamp(v, 0, 255)).ToArray()).ToList();
            if (materials.Count == 0 || colours.Count == 0)
            {
                problems.Add($"{source}: a paint with no material and colour [r, g, b] to it; never painted");
                return null;
            }

            var amount = paint.Amount is { } a && float.IsFinite(a) ? Math.Clamp(a, 0f, 1f) : 0.8f;
            return new PiecePaint(materials, colours, amount);
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

        /// <summary>The credit of a piece the game made itself (a diner's drums, its fence, its neon): nobody to name</summary>
        public const string OwnCredit = "Street Corsa (our own)";

        /// <summary>
        /// The credits whose licences ask their makers named: all but the game's own and those whose every part (a credit
        /// names its models one after another, split by ';') is CC0, however a licence is spelled
        /// </summary>
        public IReadOnlyList<string> NamedCredits => Credits
            .Where(c => c != OwnCredit && !c.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .All(part => part.Contains("CC0", StringComparison.OrdinalIgnoreCase)))
            .ToList();

    }
}
