using System.IO;
using StreetCorsa.Helpers;
using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Dealers
{
    /// <summary>
    /// The dealers of every mod (their <c>dealers</c> folder: lists of <c>dealers</c>, <c>showrooms</c> and <c>maps</c>; a
    /// file that is one entry or a bare list is dealers), read once.
    /// A mod's dealers get its namespace; a showroom is measured by its folder name (an AC showroom or a mod's
    /// garage), and a later mod's measurements of the same room win.
    ///
    /// No dealers at all is not fatal: the game falls back to the five dealers the market service has always made,
    /// without map positions. The map screen then has nothing to show and says so, but the listing screen and
    /// everything else carries on.
    /// </summary>
    public class DealerCatalog : IDealerCatalog
    {
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("DealerCatalog");
        private readonly List<DealerDefinition> _dealers = [];
        private readonly Dictionary<string, DealerDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ShowroomSpec> _showrooms = new(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new();
        // The mods read from: given, or the content in play, read again once another plays
        private readonly ModSet? _given;
        private ModSet? _readFrom;
        private DealerMapSpec? _map;

        /// <summary>The dealers of the content in play, read again when another content plays</summary>
        public DealerCatalog()
        {
        }

        public DealerCatalog(ModSet mods)
        {
            _given = mods;
        }

        // The dealers as read from the mods they are for now
        private DealerCatalog Dealers
        {
            get
            {
                var mods = _given ?? ModSet.Installed;
                lock (_lock)
                {
                    if (!ReferenceEquals(_readFrom, mods))
                    {
                        _dealers.Clear();
                        _byId.Clear();
                        _showrooms.Clear();
                        _map = null;
                        Read(mods);
                        _readFrom = mods;
                    }
                }

                return this;
            }
        }

        private void Read(ModSet mods)
        {
            var problems = new List<string>();
            try
            {
                // Showrooms share the folder: only what a "showrooms" list holds is one, the rest is the dealers'
                foreach (var entry in ModJson.Entries(mods, ContentTypes.Dealers, ShowroomsList, problems, listOnly: true))
                {
                    try
                    {
                        var spec = entry.As<ShowroomSpec>();
                        if (spec == null || string.IsNullOrWhiteSpace(spec.Id)) continue;
                        _showrooms[spec.Id] = spec;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        problems.Add($"{entry.Source}: showroom {entry.Id}: {ex.Message}; left out");
                    }
                }

                // The map the pins stand on: the last mod's, its picture next to its dealer files. Only what a "maps"
                // list holds is one: a file that is one entry, or a bare list, is dealers.
                foreach (var entry in ModJson.Entries(mods, ContentTypes.Dealers, MapsList, problems, listOnly: true))
                {
                    try
                    {
                        var map = entry.As<DealerMapSpec>();
                        if (map == null) continue;
                        if (entry.Id is not { Length: > 0 } id || string.IsNullOrWhiteSpace(id))
                        {
                            problems.Add($"{entry.Source}: a map with no id; left out");
                            continue;
                        }

                        var folder = Path.Combine(entry.Mod.Folder, ContentTypes.Dealers);
                        if (!PathNames.TryCombineUnder(folder, map.Image, out var image) || !File.Exists(image))
                        {
                            problems.Add($"{entry.Source}: map {entry.Id}: no picture \"{map.Image}\" next to it; left out");
                            continue;
                        }

                        map.ImagePath = image;
                        map.Id = ModIds.Qualify(entry.Mod.Id, id);
                        _map = map;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        problems.Add($"{entry.Source}: map {entry.Id}: {ex.Message}; left out");
                    }
                }

                var dealers = ModJson.Load(mods, ContentTypes.Dealers, DealersList, "dealer", problems, entry =>
                    entry.Id == null && entry.Replaces == null ? null : entry.As<DealerDefinition>(), otherLists: [ShowroomsList, MapsList]);
                foreach (var (id, dealer, mod) in dealers.Entries)
                {
                    dealer.Id = id;
                    FindPortrait(dealer.Salesman, mod, ContentTypes.Dealers,
                        portrait => $"dealer {dealer.Id}: no salesman portrait \"{portrait}\"; he is shown without one", problems);
                    ReadCars(dealer, problems);
                    _byId[id] = dealer;
                    _dealers.Add(dealer);
                }

                foreach (var line in dealers.Replaced) _logger.Information("{Line}", line);
                _logger.Information("Loaded {Count} dealers and {Showrooms} showroom measurements, on {Map}", _dealers.Count, _showrooms.Count,
                    _map?.Id ?? "no map");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not read the dealers");
            }

            foreach (var problem in problems) _logger.Warning("Dealers: {Problem}", problem);
            ModProblems.Add("Dealers", problems);
        }

        /// <summary>The kind of car the lot deals in; one that does not read is told, and the lot deals in any</summary>
        private static void ReadCars(DealerDefinition dealer, List<string> problems)
        {
            if (dealer.CarsJson is not { } cars) return;

            try
            {
                dealer.Cars = Models.Career.Filters.CarFilterJsonConverter.Read(cars);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                problems.Add($"dealer {dealer.Id}: \"cars\" is no car filter ({ex.Message}); it deals in any car");
            }
        }

        /// <summary>
        /// A salesman's or a clerk's portrait, next to the <paramref name="contentType"/> files of the mod that gives them;
        /// told when it is not there (<paramref name="missing"/>: what the player is told, given the picture's name)
        /// </summary>
        internal static void FindPortrait(DealerSalesman? person, ModInfo mod, string contentType, Func<string, string> missing, List<string> problems)
        {
            if (person is not { Portrait.Length: > 0 }) return;

            var folder = Path.Combine(mod.Folder, contentType);
            if (PathNames.TryCombineUnder(folder, person.Portrait, out var file) && File.Exists(file))
                person.PortraitFile = file;
            else
                problems.Add($"{mod.Id}/{contentType}: {missing(person.Portrait)}");
        }

        private const string DealersList = "dealers";
        private const string ShowroomsList = "showrooms";
        private const string MapsList = "maps";

        public DealerMapSpec? Map => Dealers._map;

        public ShowroomSpec GetShowroom(string id)
        {
            if (id != null && Dealers._showrooms.TryGetValue(id, out var spec)) return spec;

            _logger.Warning("Showroom {Showroom} has not been measured; assuming a small room", id);
            return ShowroomSpec.Unknown(id ?? string.Empty);
        }

        public IReadOnlyList<DealerDefinition> All => Dealers._dealers;

        public DealerDefinition? Get(string id) =>
            id != null && Dealers._byId.TryGetValue(id, out var dealer) ? dealer : null;

        public List<DealerLocation> ToLocations() => Dealers._dealers.Select(d => d.ToLocation()).ToList();
    }
}
