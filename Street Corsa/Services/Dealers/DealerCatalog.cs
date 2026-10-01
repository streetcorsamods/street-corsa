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

        public DealerCatalog() : this(ModSet.Installed)
        {
        }

        public DealerCatalog(ModSet mods)
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
                        Map = map;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        problems.Add($"{entry.Source}: map {entry.Id}: {ex.Message}; left out");
                    }
                }

                var dealers = ModJson.Load(mods, ContentTypes.Dealers, DealersList, "dealer", problems, entry =>
                    entry.Id == null && entry.Replaces == null ? null : entry.As<DealerDefinition>(), otherLists: [ShowroomsList, MapsList]);
                foreach (var (id, dealer, _) in dealers.Entries)
                {
                    dealer.Id = id;
                    _byId[id] = dealer;
                    _dealers.Add(dealer);
                }

                foreach (var line in dealers.Replaced) _logger.Information("{Line}", line);
                _logger.Information("Loaded {Count} dealers and {Showrooms} showroom measurements, on {Map}", _dealers.Count, _showrooms.Count,
                    Map?.Id ?? "no map");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not read the dealers");
            }

            foreach (var problem in problems) _logger.Warning("Dealers: {Problem}", problem);
            ModProblems.Add("Dealers", problems);
        }

        private const string DealersList = "dealers";
        private const string ShowroomsList = "showrooms";
        private const string MapsList = "maps";

        public DealerMapSpec? Map { get; private set; }

        public ShowroomSpec GetShowroom(string id)
        {
            if (id != null && _showrooms.TryGetValue(id, out var spec)) return spec;

            _logger.Warning("Showroom {Showroom} has not been measured; assuming a small room", id);
            return ShowroomSpec.Unknown(id ?? string.Empty);
        }

        public IReadOnlyList<DealerDefinition> All => _dealers;

        public DealerDefinition? Get(string id) =>
            id != null && _byId.TryGetValue(id, out var dealer) ? dealer : null;

        public List<DealerLocation> ToLocations() => _dealers.Select(d => d.ToLocation()).ToList();
    }
}
