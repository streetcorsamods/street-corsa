using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Dealers
{
    /// <summary>
    /// The dealers a game has (<see cref="GameState.DealerLocations"/>), each with how its business is going, and which
    /// of them do what: every dealer the mods define is there, open, closing down, closed or waiting to open.
    /// </summary>
    public static class DealerRoster
    {
        /// <summary>
        /// The save's dealers brought up to the definitions: a dealer defined since the save began joins it (open or
        /// waiting, as its <see cref="DealerDefinition.StartsOpen"/> says), one no longer defined goes, and the rest keep
        /// how their business is going under the name the definition gives them. No definitions at all leave the save as it is.
        /// </summary>
        public static void Sync(GameState gameState, IDealerCatalog catalog)
        {
            var defined = catalog.All;
            if (defined.Count == 0) return;

            var saved = (gameState.DealerLocations ?? [])
                .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var roster = new List<DealerLocation>(defined.Count);
            foreach (var definition in defined)
            {
                if (saved.TryGetValue(definition.Id, out var known))
                {
                    known.Id = definition.Id;
                    known.Name = definition.Name;
                    known.Region = definition.Region;
                    roster.Add(known);
                }
                else
                {
                    roster.Add(definition.ToLocation());
                }
            }

            gameState.DealerLocations = roster;
        }

        /// <summary>The dealers selling cars: open, or in their closing-down sale. The map shows these.</summary>
        public static List<DealerLocation> Trading(GameState gameState) =>
            (gameState.DealerLocations ?? []).Where(d => d.IsTrading).ToList();

        /// <summary>The dealers that take cars in and restock: the open ones</summary>
        public static List<DealerLocation> Restocking(GameState gameState) => Restocking(gameState.DealerLocations);

        public static List<DealerLocation> Restocking(IEnumerable<DealerLocation>? dealers) =>
            (dealers ?? []).Where(d => d.Status == DealerStatus.Open).ToList();

        /// <summary>The dealers holding a closing-down sale</summary>
        public static List<DealerLocation> ClosingDown(GameState gameState) =>
            (gameState.DealerLocations ?? []).Where(d => d.Status == DealerStatus.ClosingDown).ToList();

        /// <summary>A dealer by id, whatever its business</summary>
        public static DealerLocation? Find(GameState gameState, string? id) =>
            id == null ? null : gameState.DealerLocations?.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
