using StreetCorsa.Logging;
using StreetCorsa.Models.GameState;
using StreetCorsa.Mods;
using StreetCorsa.Services.Career;

namespace StreetCorsa.Services.Storage
{
    /// <summary>
    /// A save and the mods: which mods it was last played with (<see cref="GameState.Mods"/>, stamped on every save),
    /// which of them are not installed now, and what a save holds of content no mod has any more.
    ///
    /// Each kind of content copes with a missing id its own way. A mod's parts are kept and skipped (they may come back;
    /// an engine of a missing mod waits on the shelf, whole) and renamed ones follow the aliases
    /// (<c>CarPartsService.BringUpToDate</c>); cars of a car mod that is gone are
    /// <c>Legacy</c>; racers live whole in the save; news and street talk are saved as text; a path picked that no mod
    /// has is no path until it is back. What only makes sense while its content is there goes here: invitations to
    /// events nobody holds, and cars on the lots of dealers that are gone.
    /// </summary>
    public static class ModsInSave
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Save);

        /// <summary>The mods a save names that are not installed (the base game is always there)</summary>
        public static List<ModRef> Missing(GameState gameState, ModSet installed) =>
            (gameState.Mods ?? [])
                .Where(m => !ModIds.IsBase(m.Id) && installed.Get(m.Id) == null)
                .ToList();

        /// <summary>What the player is told before a save with missing mods loads</summary>
        public static string Warning(IReadOnlyList<ModRef> missing) =>
            "This game was played with mods that are not installed now:\n\n"
            + string.Join("\n", missing.Select(m => "  " + m))
            + "\n\nWhat they added is missing until they are back. Their parts wait on your shelf (an engine of theirs "
            + "comes out of its car, which gets its factory engine); their events and their dealers' cars are gone. Load it anyway?";

        /// <summary>
        /// Takes out what only makes sense while its content is there, when its mod is not installed; true when anything
        /// went. What an installed mod merely left out (a bad entry, a file that did not read) stays: the save waits
        /// for the fix, and the game skips what it cannot find meanwhile.
        /// </summary>
        public static bool Tidy(GameState gameState, CareerContent career, ModSet installed)
        {
            var changed = false;
            bool Gone(string? id) => !string.IsNullOrEmpty(id) && ModIds.IsQualified(id)
                                     && !ModIds.IsBase(ModIds.ModOf(id)) && installed.Get(ModIds.ModOf(id)) == null;

            var events = gameState.Career.ActiveEvents.RemoveAll(e => Gone(e.EventDefinitionId));
            if (events > 0)
            {
                Logger.Information("{Count} invitation(s) to events of mods that are not installed are taken out of the paper", events);
                changed = true;
            }

            if (gameState.UsedCarMarket is { } market)
            {
                var gone = market.RemoveAll(l => Gone(l.DealerLocation));
                if (gone > 0)
                {
                    Logger.Information("{Count} car(s) on the lots of dealers of mods that are not installed are taken off the market", gone);
                    changed = true;
                }
            }

            // The daily market refresh stocks every dealer the save knows: one gone would be stocked again
            if (gameState.DealerLocations is { } dealers && dealers.RemoveAll(d => Gone(d.Id)) > 0) changed = true;

            if (gameState.Career.ActiveVictoryType is { } path && career.Victories.All(v => !v.Id.Equals(path, StringComparison.OrdinalIgnoreCase)))
                Logger.Warning("The victory path {Path} picked in this game is in no mod: no path until it is back", path);

            return changed;
        }
    }
}
