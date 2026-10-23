using StreetCorsa.Logging;
using StreetCorsa.Mods;
using StreetCorsa.Models.GameState;
using StreetCorsa.Services.Dealers;

namespace StreetCorsa.Services.PartShops
{
    /// <summary>The part shops as the mods define them</summary>
    public interface IPartShopCatalog
    {
        /// <summary>Every shop, in the order the files list them</summary>
        IReadOnlyList<PartShopDefinition> All { get; }

        /// <summary>One shop by its id, or null if no mod has it</summary>
        PartShopDefinition? Get(string? id);
    }

    /// <summary>
    /// The part shops of every mod (a <c>shops</c> list in its <c>partshops</c> folder, or a file that is one shop or a
    /// bare list), read from the content in play. A mod's shops get its namespace, and stand on the part shops' own map (drawn on the
    /// dealers' map picture, without the car lots). No shops at all is not fatal: the newspaper's parts pages carry on
    /// without them.
    /// </summary>
    public class PartShopCatalog : IPartShopCatalog
    {
        private const string ShopsList = "shops";

        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("PartShops");
        private readonly List<PartShopDefinition> _shops = [];
        private readonly Dictionary<string, PartShopDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new();
        // The mods read from: given, or the content in play, read again once another plays
        private readonly ModSet? _given;
        private ModSet? _readFrom;

        /// <summary>The shops of the content in play, read again when another content plays</summary>
        public PartShopCatalog()
        {
        }

        public PartShopCatalog(ModSet mods)
        {
            _given = mods;
        }

        // The shops as read from the mods they are for now
        private PartShopCatalog Shops
        {
            get
            {
                var mods = _given ?? ModSet.Installed;
                lock (_lock)
                {
                    if (!ReferenceEquals(_readFrom, mods))
                    {
                        _shops.Clear();
                        _byId.Clear();
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
                var shops = ModJson.Load(mods, ContentTypes.PartShops, ShopsList, "part shop", problems, entry =>
                    entry.Id == null && entry.Replaces == null ? null : entry.As<PartShopDefinition>());
                foreach (var (id, shop, mod) in shops.Entries)
                {
                    shop.Id = id;
                    shop.Makes ??= [];
                    shop.Groups ??= [];
                    shop.Look ??= string.Empty;
                    DealerCatalog.FindPortrait(shop.Clerk, mod, ContentTypes.PartShops,
                        portrait => $"shop {shop.Id}: no clerk portrait \"{portrait}\"; they are shown without one", problems);
                    _byId[id] = shop;
                    _shops.Add(shop);
                }

                foreach (var line in shops.Replaced) _logger.Information("{Line}", line);
                _logger.Information("Loaded {Count} part shops", _shops.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not read the part shops");
            }

            foreach (var problem in problems) _logger.Warning("Part shops: {Problem}", problem);
            ModProblems.Add("Part shops", problems);
        }

        public IReadOnlyList<PartShopDefinition> All => Shops._shops;

        public PartShopDefinition? Get(string? id) => id != null && Shops._byId.TryGetValue(id, out var shop) ? shop : null;
    }

    /// <summary>
    /// The part shops a game has (<see cref="GameState.PartShops"/>), each with how its business is going and its stock
    /// </summary>
    public static class PartShopRoster
    {
        /// <summary>
        /// The save's shops brought up to the definitions (<see cref="DealerChurn.Merge"/>): a shop defined since the
        /// save began joins it (open or waiting, as its <see cref="PartShopDefinition.StartsOpen"/> says), one no longer
        /// defined goes with its stock, and the rest keep their business and shelves under the name the definition gives
        /// them. No definitions at all (a <c>partshops</c> file that did not read) leave the save as it is, so the shops
        /// and what the player spent there come back when the file is mended.
        /// </summary>
        public static void Sync(GameState gameState, IPartShopCatalog catalog)
        {
            if (DealerChurn.Merge(gameState.PartShops, catalog.All) is not { } roster) return;

            foreach (var shop in roster)
            {
                shop.Shelves ??= [];
                shop.Yard ??= [];
            }

            gameState.PartShops = roster;
        }

        /// <summary>The shops selling: open, or in their closing-down sale. The map shows these.</summary>
        public static List<PartShopLocation> Trading(GameState gameState) =>
            (gameState.PartShops ?? []).Where(s => s.IsTrading).ToList();

        public static PartShopLocation? Find(GameState gameState, string? id) =>
            id == null ? null : gameState.PartShops?.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
