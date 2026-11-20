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

        /// <summary>The traffic cars as recipes over the player's own cars, by id</summary>
        public IReadOnlyDictionary<string, TrafficCarRecipe> Cars { get; }

        /// <summary>The fleets whose traffic keeps to the left (the UK, Japan): their roads' plans are built that way round</summary>
        public IReadOnlySet<string> KeepLeft { get; }

        /// <summary>
        /// The fleet every road falls back on when too little of its own can be made from the player's cars (fewer than
        /// the rules' <see cref="Rules.RaceRules.TrafficLeastModels"/>): Kunos' road cars, which every player has some
        /// of; null when no mod names one
        /// </summary>
        public string? Base { get; }

        /// <summary>The least weight written for the mode: two decimals are written, and a 0 would cast a model that never comes</summary>
        public const double LeastWeight = 0.01;

        public IReadOnlyList<string> Problems { get; }

        private TrafficFleets(Dictionary<string, IReadOnlyList<FleetModel>> fleets, Dictionary<string, string> tracks, List<string> problems,
            Dictionary<string, TrafficCarRecipe>? cars = null, HashSet<string>? keepLeft = null, string? baseFleet = null)
        {
            Fleets = fleets;
            Tracks = tracks;
            Problems = problems;
            Cars = cars ?? new(StringComparer.OrdinalIgnoreCase);
            KeepLeft = keepLeft ?? new(StringComparer.OrdinalIgnoreCase);
            Base = baseFleet;
        }

        /// <summary>Fleets as given, for tests and tools</summary>
        public static TrafficFleets From(Dictionary<string, IReadOnlyList<FleetModel>> fleets, Dictionary<string, string> tracks,
            IEnumerable<TrafficCarRecipe>? cars = null, IEnumerable<string>? keepLeft = null, string? baseFleet = null) =>
            new(new(fleets, StringComparer.OrdinalIgnoreCase), new(tracks, StringComparer.OrdinalIgnoreCase), [],
                (cars ?? []).ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase), new(keepLeft ?? [], StringComparer.OrdinalIgnoreCase), baseFleet);

        /// <summary>The name of a road's fleet: by track and layout, else by the track alone; null when no mod casts it</summary>
        public string? FleetOf(string trackId, string? layout)
        {
            foreach (var key in new[] { KeyOf(trackId, layout), trackId }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Tracks.TryGetValue(key, out var fleet) && Fleets.ContainsKey(fleet)) return fleet;
            }
            return null;
        }

        /// <summary>Whether a road's traffic keeps to the left: its fleet says so</summary>
        public bool KeepsLeft(string trackId, string? layout) => FleetOf(trackId, layout) is { } fleet && KeepLeft.Contains(fleet);

        /// <summary>The base fleet's models; empty when there is none</summary>
        public IReadOnlyList<FleetModel> BaseModels => Base != null && Fleets.TryGetValue(Base, out var models) ? models : [];

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
        /// INI reader splits a value at commas and gives a text setting the first piece only); a weight too small to
        /// write is written as the least there is (<see cref="LeastWeight"/>), never as 0
        /// </summary>
        public static string Spec(IEnumerable<FleetModel> models) =>
            string.Join(" ", models.Select(m => m.Id + ":" + Math.Max(m.Weight, LeastWeight).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

        /// <summary>Every mod's fleets and tracks, in load order, a later name over an earlier</summary>
        public static TrafficFleets Load(ModSet mods)
        {
            var problems = new List<string>();
            var fleets = new Dictionary<string, IReadOnlyList<FleetModel>>(StringComparer.OrdinalIgnoreCase);
            var tracks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cars = new Dictionary<string, TrafficCarRecipe>(StringComparer.OrdinalIgnoreCase);
            var keepLeft = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? baseFleet = null;
            foreach (var (mod, file) in mods.JsonFiles(ContentTypes.Traffic))
            {
                var source = $"{mod.Id}/{ContentTypes.Traffic}/{Path.GetFileName(file)}";
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    Read(document.RootElement, source, fleets, tracks, problems, keepLeft);
                    ReadCars(document.RootElement, source, cars, problems);
                    if (ModJson.Text(document.RootElement, "base") is { Length: > 0 } named) baseFleet = named;
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{source}: does not read ({ex.Message}); left out");
                }
            }

            if (baseFleet != null && !fleets.ContainsKey(baseFleet))
            {
                problems.Add($"traffic: the base fleet \"{baseFleet}\" is in no mod; there is none");
                baseFleet = null;
            }

            // A road cast with a fleet no mod has gets none
            foreach (var (track, fleet) in tracks.Where(t => !fleets.ContainsKey(t.Value)).ToList())
            {
                problems.Add($"traffic: track {track}'s fleet \"{fleet}\" is in no mod; it has none");
                tracks.Remove(track);
            }

            foreach (var problem in problems) LazyLogger.Value.Warning("Traffic: {Problem}", problem);
            ModProblems.Add("Traffic", problems);
            return new TrafficFleets(fleets, tracks, problems, cars, keepLeft, baseFleet);
        }

        // An id goes into race.ini between spaces and colons, and into file names: none of those, nor a comma or anything
        // else an INI line is cut at, nor a dot or a slash (a path out of the models' folder), in it
        private static bool IsId(string? id) =>
            !string.IsNullOrEmpty(id)
            && !id.Any(c => char.IsWhiteSpace(c) || c is ':' or ',' or ';' or '=' or '[' or ']' or '/' or '\\' or '.' || Path.GetInvalidFileNameChars().Contains(c));

        /// <summary>
        /// The traffic cars: <c>"cars": { "&lt;id&gt;": { "cars": [AC car folders, the first installed taken], "name",
        /// "year", "maxSpeed", "mass", "color", "chance", "dynamic", "paint", "lights", "maxTexture", "nearFull", "skins" } }</c>
        /// (<c>"car"</c> for a single one); a later mod's id over an earlier's
        /// </summary>
        private static void ReadCars(JsonElement root, string source, Dictionary<string, TrafficCarRecipe> cars, List<string> problems)
        {
            if (root.ValueKind != JsonValueKind.Object || !ModJson.TryGet(root, "cars", out var named)) return;
            if (named.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{source}: \"cars\" is not an object of traffic cars; left out");
                return;
            }

            foreach (var car in named.EnumerateObject())
            {
                var e = car.Value;
                if (!IsId(car.Name) || e.ValueKind != JsonValueKind.Object)
                {
                    problems.Add($"{source}: traffic car \"{car.Name}\" is not an id and an object; left out");
                    continue;
                }

                var sources = new List<string>();
                if (ModJson.Text(e, "car") is { Length: > 0 } one) sources.Add(one);
                if (ModJson.TryGet(e, "cars", out var list) && list.ValueKind == JsonValueKind.Array)
                    sources.AddRange(list.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).Where(s => s.Length > 0));
                // A car is a folder of content\cars by its name: nothing that leads out of it
                foreach (var bad in sources.Where(s => !Helpers.PathNames.IsSafeSegment(s)).ToList())
                {
                    problems.Add($"{source}: traffic car \"{car.Name}\" names \"{bad}\", which is no car folder's name; left out of its cars");
                    sources.Remove(bad);
                }
                if (sources.Count == 0)
                {
                    problems.Add($"{source}: traffic car \"{car.Name}\" names no AC car to be made from; left out");
                    continue;
                }

                double? Number(string key) => ModJson.TryGet(e, key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;

                IReadOnlyList<string>? paint = null;
                if (ModJson.TryGet(e, "paint", out var p))
                {
                    if (p.ValueKind == JsonValueKind.False) paint = [];
                    else if (p.ValueKind == JsonValueKind.Array) paint = p.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList();
                }

                Dictionary<string, IReadOnlyList<string>>? lights = null;
                if (ModJson.TryGet(e, "lights", out var l) && l.ValueKind == JsonValueKind.Object)
                {
                    lights = new(StringComparer.Ordinal);
                    foreach (var role in l.EnumerateObject().Where(r => r.Value.ValueKind == JsonValueKind.Array))
                        lights[role.Name] = role.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList();
                }

                int? skins = null;
                if (ModJson.TryGet(e, "skins", out var s))
                {
                    if (s.ValueKind == JsonValueKind.False) skins = 0;
                    else if (s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var n) && n >= 0) skins = n;
                }

                var color = ModJson.Text(e, "color");
                cars[car.Name] = new TrafficCarRecipe
                {
                    Id = car.Name,
                    Name = ModJson.Text(e, "name"),
                    Year = Number("year") is { } year ? (int)year : null,
                    Cars = sources,
                    MaxSpeed = Number("maxSpeed") is > 0 and var speed ? speed : 90,
                    Mass = Number("mass") is > 0 and var mass ? mass : null,
                    Color = color == "modern" ? "modern" : "vintage",
                    Chance = Number("chance") is > 0 and var chance ? chance : 1,
                    Dynamic = Number("dynamic") is { } dynamic ? Math.Clamp(dynamic, 0, 1) : 0.4,
                    Paint = paint,
                    Lights = lights,
                    MaxTexture = Number("maxTexture") is >= 64 and var side ? (int)side : null,
                    NearFull = ModJson.TryGet(e, "nearFull", out var nf) && nf.ValueKind == JsonValueKind.True,
                    Skins = skins
                };
            }
        }

        private static void Read(JsonElement root, string source, Dictionary<string, IReadOnlyList<FleetModel>> fleets, Dictionary<string, string> tracks, List<string> problems,
            HashSet<string> keepLeft)
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
                    // A fleet is its list of models, or an object of them ("models") and the side its traffic keeps to
                    var list = fleet.Value;
                    if (list.ValueKind == JsonValueKind.Object)
                    {
                        if (ModJson.Text(list, "keep") is { } keep)
                        {
                            if (keep.Equals("left", StringComparison.OrdinalIgnoreCase)) keepLeft.Add(fleet.Name);
                            else keepLeft.Remove(fleet.Name);
                        }
                        if (!ModJson.TryGet(list, "models", out list)) list = default;
                    }
                    if (list.ValueKind != JsonValueKind.Array)
                    {
                        problems.Add($"{source}: fleet \"{fleet.Name}\" is not a list; left out");
                        continue;
                    }

                    var models = new List<FleetModel>();
                    foreach (var entry in list.EnumerateArray())
                    {
                        var id = entry.ValueKind == JsonValueKind.Object ? ModJson.Text(entry, "id")?.Trim() : null;
                        var weight = entry.ValueKind == JsonValueKind.Object && ModJson.TryGet(entry, "weight", out var w) && w.ValueKind == JsonValueKind.Number
                            ? w.GetDouble() : 1;
                        // The same id a traffic car has: it goes into race.ini and into file names
                        if (!IsId(id) || !double.IsFinite(weight) || weight <= 0)
                        {
                            problems.Add($"{source}: fleet \"{fleet.Name}\" has a model that is not an id and a weight above 0; left out");
                            continue;
                        }
                        models.Add(new FleetModel(id!, weight));
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
