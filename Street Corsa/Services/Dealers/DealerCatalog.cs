using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Dealers
{
    /// <summary>
    /// The dealers of every mod (their <c>dealers</c> folder: lists of <c>dealers</c> and <c>showrooms</c>; a file that is
    /// one entry or a bare list is dealers), read once.
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
                foreach (var entry in ModJson.Entries(mods, ContentTypes.Dealers, "showrooms", problems, listOnly: true))
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

                var dealers = ModJson.Load(mods, ContentTypes.Dealers, "dealers", "dealer", problems, entry =>
                    entry.Id == null && entry.Replaces == null ? null : entry.As<DealerDefinition>(), otherLists: ["showrooms"]);
                foreach (var (id, dealer, _) in dealers.Entries)
                {
                    dealer.Id = id;
                    _byId[id] = dealer;
                    _dealers.Add(dealer);
                }

                foreach (var line in dealers.Replaced) _logger.Information("{Line}", line);
                _logger.Information("Loaded {Count} dealers and {Showrooms} showroom measurements", _dealers.Count, _showrooms.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not read the dealers");
            }

            foreach (var problem in problems) _logger.Warning("Dealers: {Problem}", problem);
        }

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
