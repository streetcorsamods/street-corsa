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
        /// The save's dealers brought up to the definitions (<see cref="DealerChurn.Merge"/>): a dealer defined since the
        /// save began joins it (open or waiting, as its <see cref="DealerDefinition.StartsOpen"/> says), one no longer
        /// defined goes, and the rest keep how their business is going under the name the definition gives them. No
        /// definitions at all leave the save as it is.
        ///
        /// A dealer that goes (its mod was taken out) takes the cars still on its lot with it: nobody can drive there to
        /// buy them, and the rivals must not buy off a lot that is not there. What was sold there stays on the books.
        /// </summary>
        public static void Sync(GameState gameState, IDealerCatalog catalog)
        {
            if (DealerChurn.Merge(gameState.DealerLocations, catalog.All) is not { } roster) return;

            gameState.DealerLocations = roster;
            var known = roster.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            gameState.UsedCarMarket?.RemoveAll(l => !l.IsSold && !known.Contains(l.DealerLocation));
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
