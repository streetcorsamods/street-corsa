using System.IO;
using System.Text.Json;
using StreetCorsa.Logging;
using StreetCorsa.Mods;

namespace StreetCorsa.Services.Race.Traffic
{
    /// <summary>One traffic model of a fleet: its id (the file name of its .json in the traffic tool's folder) and how often it comes</summary>
    public sealed record FleetModel(string Id, double Weight);

    /// <summary>
    /// Which traffic models a road gets: its fleet. CSP's traffic tool loads every model in its folder and spawns them
    /// all, so without a cast every road would carry every country's cars. Every mod's <c>traffic\*.json</c> may hold
    /// <c>fleets</c> (named lists of <c>{ "id", "weight" }</c>) and <c>tracks</c> (a track, or <c>track-layout</c>, to a
    /// fleet's name), each going over the mods' before it by name. A road is looked up by its track and layout first, then
    /// by the track alone, so one line casts a whole track and another can single out a layout.
    /// </summary>
    public sealed class TrafficFleets
    {
        private static readonly ContentCache<TrafficFleets> InstalledFleets = new(() => Load(ModSet.Installed));
        private static readonly Lazy<IAppLogger> LazyLogger = new(() => AppLoggerFactory.CreateLogger("Traffic"));

        /// <summary>The fleets of the mods in play</summary>
        public static TrafficFleets Installed => InstalledFleets.Value;

        /// <summary>The named fleets, each a cast</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<FleetModel>> Fleets { get; }

        /// <summary>A track (or <c>track-layout</c>) to the name of its fleet</summary>
        public IReadOnlyDictionary<string, string> Tracks { get; }

        public IReadOnlyList<string> Problems { get; }

        private TrafficFleets(Dictionary<string, IReadOnlyList<FleetModel>> fleets, Dictionary<string, string> tracks, List<string> problems)
        {
            Fleets = fleets;
            Tracks = tracks;
            Problems = problems;
        }

        /// <summary>Fleets as given, for tests and tools</summary>
        public static TrafficFleets From(Dictionary<string, IReadOnlyList<FleetModel>> fleets, Dictionary<string, string> tracks) =>
            new(new(fleets, StringComparer.OrdinalIgnoreCase), new(tracks, StringComparer.OrdinalIgnoreCase), []);

        /// <summary>The key a road is cast by: its track, or <c>track-layout</c></summary>
        public static string KeyOf(string trackId, string? layout) => string.IsNullOrEmpty(layout) ? trackId : trackId + "-" + layout;

        /// <summary>
        /// A road's cast: its fleet by track and layout, else by the track alone; empty when no mod casts it (the mode
        /// then takes every model there is)
        /// </summary>
        public IReadOnlyList<FleetModel> For(string trackId, string? layout)
        {
            foreach (var key in new[] { KeyOf(trackId, layout), trackId }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Tracks.TryGetValue(key, out var fleet) && Fleets.TryGetValue(fleet, out var models) && models.Count > 0) return models;
            }
            return [];
        }

        /// <summary>
        /// The cast as race.ini's <c>TRAFFIC_CARS</c> takes it: <c>id:weight</c>, separated by spaces, never commas (CSP's
        /// INI reader splits a value at commas and gives a text setting the first piece only)
        /// </summary>
        public static string Spec(IEnumerable<FleetModel> models) =>
            string.Join(" ", models.Select(m => m.Id + ":" + m.Weight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

        /// <summary>The models of a cast that are in a folder of traffic models (each an <c>&lt;id&gt;.json</c>); the cast as it is when the folder cannot be listed</summary>
        public static IReadOnlyList<FleetModel> InFolder(IReadOnlyList<FleetModel> models, string folder)
        {
            HashSet<string> there;
            try
            {
                if (!Directory.Exists(folder)) return models;
                there = Directory.EnumerateFiles(folder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LazyLogger.Value.Warning("The traffic models in {Folder} could not be listed: the cast goes as it is ({Error})", folder, ex.Message);
                return models;
            }
            return models.Where(m => there.Contains(m.Id)).ToList();
        }

        /// <summary>Every mod's fleets and tracks, in load order, a later name over an earlier</summary>
        public static TrafficFleets Load(ModSet mods)
        {
            var problems = new List<string>();
            var fleets = new Dictionary<string, IReadOnlyList<FleetModel>>(StringComparer.OrdinalIgnoreCase);
            var tracks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (mod, file) in mods.JsonFiles(ContentTypes.Traffic))
            {
                var source = $"{mod.Id}/{ContentTypes.Traffic}/{Path.GetFileName(file)}";
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    Read(document.RootElement, source, fleets, tracks, problems);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{source}: does not read ({ex.Message}); left out");
                }
            }

            // A road cast with a fleet no mod has gets none
            foreach (var (track, fleet) in tracks.Where(t => !fleets.ContainsKey(t.Value)).ToList())
            {
                problems.Add($"traffic: track {track}'s fleet \"{fleet}\" is in no mod; it has none");
                tracks.Remove(track);
            }

            foreach (var problem in problems) LazyLogger.Value.Warning("Traffic: {Problem}", problem);
            ModProblems.Add("Traffic", problems);
            return new TrafficFleets(fleets, tracks, problems);
        }

        private static void Read(JsonElement root, string source, Dictionary<string, IReadOnlyList<FleetModel>> fleets, Dictionary<string, string> tracks, List<string> problems)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{source}: is not an object of fleets and tracks; left out");
                return;
            }

            if (ModJson.TryGet(root, "fleets", out var named) && named.ValueKind == JsonValueKind.Object)
            {
                foreach (var fleet in named.EnumerateObject())
                {
                    if (fleet.Value.ValueKind != JsonValueKind.Array)
                    {
                        problems.Add($"{source}: fleet \"{fleet.Name}\" is not a list; left out");
                        continue;
                    }

                    var models = new List<FleetModel>();
                    foreach (var entry in fleet.Value.EnumerateArray())
                    {
                        var id = entry.ValueKind == JsonValueKind.Object ? ModJson.Text(entry, "id")?.Trim() : null;
                        var weight = entry.ValueKind == JsonValueKind.Object && ModJson.TryGet(entry, "weight", out var w) && w.ValueKind == JsonValueKind.Number
                            ? w.GetDouble() : 1;
                        // An id goes into race.ini between spaces and colons: none of those, nor a comma, in it
                        if (string.IsNullOrEmpty(id) || id.Any(c => char.IsWhiteSpace(c) || c is ':' or ',' or ';') || !double.IsFinite(weight) || weight <= 0)
                        {
                            problems.Add($"{source}: fleet \"{fleet.Name}\" has a model that is not an id and a weight above 0; left out");
                            continue;
                        }
                        models.Add(new FleetModel(id, weight));
                    }
                    fleets[fleet.Name] = models;
                }
            }
            else if (ModJson.TryGet(root, "fleets", out _))
            {
                problems.Add($"{source}: \"fleets\" is not an object of named lists; left out");
            }

            if (ModJson.TryGet(root, "tracks", out var cast) && cast.ValueKind == JsonValueKind.Object)
            {
                foreach (var track in cast.EnumerateObject())
                {
                    if (track.Value.ValueKind == JsonValueKind.String && track.Value.GetString() is { Length: > 0 } fleet) tracks[track.Name] = fleet;
                    else problems.Add($"{source}: track \"{track.Name}\" names no fleet; left out");
                }
            }
            else if (ModJson.TryGet(root, "tracks", out _))
            {
                problems.Add($"{source}: \"tracks\" is not an object of tracks and fleets; left out");
            }
        }
    }
}
